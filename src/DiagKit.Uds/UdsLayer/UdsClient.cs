using System;
using System.Diagnostics;
using System.Threading;

using DiagKit.Uds.Contracts;

using DiagKit.Uds.Exceptions;
using DiagKit.Uds.Internal;

namespace DiagKit.Uds.UdsLayer;

/// <summary>
/// 同步 UDS（ISO 14229）应用层客户端。<br/>Synchronous UDS (ISO 14229) application-layer client.
/// </summary>
public sealed class UdsClient : IUdsClient
{
    private readonly Action<ReadOnlyMemory<byte>, CancellationToken> _send;
    private readonly Func<CancellationToken, ReadOnlyMemory<byte>> _receive;
    private readonly Action? _clearBuffer;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly UdsOptions _options;

    /// <inheritdoc/>
    public UdsOptions Options => _options.Clone();

    /// <summary>
    /// 使用原始发送/接收委托创建同步 UDS 客户端。<br/>Creates a synchronous UDS client with raw send/receive delegates.
    /// </summary>
    /// <param name="send">发送 UDS 载荷的委托。<br/>Delegate to send a UDS payload.</param>
    /// <param name="receive">接收 UDS 载荷的委托。<br/>Delegate to receive a UDS payload.</param>
    /// <param name="clearReceiveBuffer">可选的清空接收缓冲区操作。<br/>Optional action to clear the receive buffer.</param>
    /// <param name="options">UDS 客户端参数（可选，使用默认值）。<br/>Optional UDS client parameters.</param>
    public UdsClient(
        Action<ReadOnlyMemory<byte>, CancellationToken> send,
        Func<CancellationToken, ReadOnlyMemory<byte>> receive,
        Action? clearReceiveBuffer = null,
        UdsOptions? options = null)
    {
        _send = send ?? throw new ArgumentNullException(nameof(send));
        _receive = receive ?? throw new ArgumentNullException(nameof(receive));
        _clearBuffer = clearReceiveBuffer;
        _options = (options ?? new UdsOptions()).Clone();
        _options.Validate();
    }

    /// <summary>
    /// 从同步传输器接口创建同步 UDS 客户端。<br/>Creates a synchronous UDS client from an <see cref="ITransmitter{T}"/>.
    /// </summary>
    /// <param name="transport">同步 UDS 传输器接口。<br/>The synchronous UDS transport interface.</param>
    /// <param name="options">UDS 客户端参数（可选）。<br/>Optional UDS client parameters.</param>
    public UdsClient(ITransmitter<ReadOnlyMemory<byte>> transport, UdsOptions? options = null)
        : this(
            (data, ct) => transport.Send(data, ct),
            transport.Receive,
            transport.ClearReceiveBuffer,
            options)
    {
    }

    /// <summary>从底层传输排空挂起的入站载荷。<br/>Drain pending inbound payloads from the underlying transport.</summary>
    public void ClearReceiveBuffer() => _clearBuffer?.Invoke();

    /// <inheritdoc/>
    public ReadOnlyMemory<byte> SendRequest(
        ReadOnlyMemory<byte> request,
        bool? suppressResponse = null,
        CancellationToken cancellationToken = default)
    {
        if (request.IsEmpty) throw new ArgumentException("Request is empty.", nameof(request));
        if (!_gate.Wait(0, cancellationToken))
            throw new InvalidOperationException("A UDS request is already in flight.");
        try
        {
            byte[] reqCopy = request.ToArray();
            bool suppress = suppressResponse ?? UdsMessage.IsSuppressPositiveResponse(request.Span);
            var overall = Stopwatch.StartNew();

            while (true)
            {
                _send(reqCopy, cancellationToken);

                if (suppress)
                {
                    if (!_options.WaitWhileSuppressingResponse) return ReadOnlyMemory<byte>.Empty;
                    using var cts = new LinkedCts(_options.P2Client, cancellationToken);
                    while (true)
                    {
                        ReadOnlyMemory<byte> maybeResponse;
                        try
                        {
                            maybeResponse = _receive(cts.Token);
                        }
                        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                        {
                            return ReadOnlyMemory<byte>.Empty;
                        }

                        if (!UdsClientResponseHandling.IsMatchingResponse(reqCopy, maybeResponse.Span))
                        {
                            if (_options.StrictServiceIdMatching)
                                throw UdsClientResponseHandling.CreateUnexpectedResponse(reqCopy, maybeResponse.Span, "for suppressed");
                            continue;
                        }

                        if (UdsClientResponseHandling.IsResponsePendingFor(reqCopy, maybeResponse.Span)
                            && _options.Rc78Handling == Rc78Handling.WaitForCompletion)
                        {
                            maybeResponse = WaitForFinalResponseAfterRc78(reqCopy, overall, cancellationToken);
                        }

                        return UdsClientResponseHandling.HandlePossibleNrc(reqCopy, maybeResponse);
                    }
                }

                ReadOnlyMemory<byte> response;
                while (true)
                {
                    try
                    {
                        using var cts = new LinkedCts(_options.P2Client, cancellationToken);
                        response = _receive(cts.Token);
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        throw new ProtocolException($"Timeout waiting for UDS response (P2 = {_options.P2Client.TotalMilliseconds:F0} ms).");
                    }

                    if (!UdsClientResponseHandling.IsMatchingResponse(reqCopy, response.Span))
                    {
                        if (_options.StrictServiceIdMatching)
                            throw UdsClientResponseHandling.CreateUnexpectedResponse(reqCopy, response.Span, "for");
                        continue;
                    }

                    if (UdsClientResponseHandling.IsResponsePendingFor(reqCopy, response.Span)
                        && _options.Rc78Handling == Rc78Handling.WaitForCompletion)
                    {
                        response = WaitForFinalResponseAfterRc78(reqCopy, overall, cancellationToken);
                    }

                    if (UdsClientResponseHandling.IsBusyRepeatRequestFor(reqCopy, response.Span)
                        && _options.Rc21Handling == Rc21Handling.Retry)
                    {
                        if (overall.Elapsed + _options.Rc21RetryInterval >= _options.Rc21CompletionTimeout)
                            throw new ProtocolException($"RC 0x21 retries exceeded {_options.Rc21CompletionTimeout.TotalMilliseconds:F0} ms.");
                        cancellationToken.WaitHandle.WaitOne(_options.Rc21RetryInterval);
                        cancellationToken.ThrowIfCancellationRequested();
                        break;
                    }

                    return UdsClientResponseHandling.HandlePossibleNrc(reqCopy, response);
                }
            }
        }
        finally { _gate.Release(); }
    }

    private ReadOnlyMemory<byte> WaitForFinalResponseAfterRc78(
        byte[] request,
        Stopwatch overall,
        CancellationToken cancellationToken)
    {
        if (overall.Elapsed >= _options.Rc78CompletionTimeout)
            throw UdsClientResponseHandling.CreateRc78Exceeded(_options);

        while (true)
        {
            var wait = GetNextRc78Wait();
            ReadOnlyMemory<byte> response;
            try
            {
                using var cts = new LinkedCts(wait, cancellationToken);
                response = _receive(cts.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                if (overall.Elapsed >= _options.Rc78CompletionTimeout)
                    throw UdsClientResponseHandling.CreateRc78Exceeded(_options);
                throw new ProtocolException($"Timeout waiting for final response after RC 0x78 (P2* = {_options.P2ClientExtended.TotalMilliseconds:F0} ms).");
            }

            if (!UdsClientResponseHandling.IsMatchingResponse(request, response.Span))
            {
                if (_options.StrictServiceIdMatching)
                    throw new ProtocolException("Unexpected UDS response during RC 0x78 wait.");
                continue;
            }

            if (UdsClientResponseHandling.IsResponsePendingFor(request, response.Span))
            {
                if (overall.Elapsed >= _options.Rc78CompletionTimeout)
                    throw UdsClientResponseHandling.CreateRc78Exceeded(_options);
                continue;
            }

            return response;
        }

        TimeSpan GetNextRc78Wait()
            => UdsClientResponseHandling.GetNextRc78Wait(_options, overall);
    }
}
