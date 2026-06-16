using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

using DiagKit.Uds.Contracts;

using DiagKit.Uds.Exceptions;
using DiagKit.Uds.Internal;

namespace DiagKit.Uds.UdsLayer;

/// <summary>
/// 异步 UDS（ISO 14229）应用层客户端。<br/>Asynchronous UDS (ISO 14229) application-layer client.
/// </summary>
/// <remarks>
/// 封装请求/响应排序、suppressPositiveResponse 检测、RC 0x78（ResponsePending）等待
/// 和 RC 0x21（BusyRepeatRequest）重试。<br/>
/// Encapsulates request/response sequencing, suppressPositiveResponse detection,
/// RC 0x78 (ResponsePending) waiting, and RC 0x21 (BusyRepeatRequest) retrying.
/// </remarks>
public sealed class AsyncUdsClient : IAsyncUdsClient
{
    private readonly Func<ReadOnlyMemory<byte>, CancellationToken, Task> _sendAsync;
    private readonly Func<CancellationToken, Task<ReadOnlyMemory<byte>>> _receiveAsync;
    private readonly Func<CancellationToken, CancellationToken, Task<ReadOnlyMemory<byte>>>? _receiveWithResponseStartAsync;
    private readonly Action? _clearBuffer;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly UdsOptions _options;

    /// <inheritdoc/>
    public UdsOptions Options => _options.Clone();

    /// <summary>
    /// 使用原始发送/接收委托创建异步 UDS 客户端。<br/>Creates an async UDS client with raw send/receive delegates.
    /// </summary>
    /// <param name="sendAsync">异步发送 UDS 载荷的委托。<br/>Delegate to send a UDS payload asynchronously.</param>
    /// <param name="receiveAsync">异步接收 UDS 载荷的委托。<br/>Delegate to receive a UDS payload asynchronously.</param>
    /// <param name="clearReceiveBuffer">可选的清空接收缓冲区操作。<br/>Optional action to clear the receive buffer.</param>
    /// <param name="options">UDS 客户端参数（可选，使用默认值）。<br/>Optional UDS client parameters.</param>
    public AsyncUdsClient(
        Func<ReadOnlyMemory<byte>, CancellationToken, Task> sendAsync,
        Func<CancellationToken, Task<ReadOnlyMemory<byte>>> receiveAsync,
        Action? clearReceiveBuffer = null,
        UdsOptions? options = null)
        : this(sendAsync, receiveAsync, null, clearReceiveBuffer, options)
    {
    }

    private AsyncUdsClient(
        Func<ReadOnlyMemory<byte>, CancellationToken, Task> sendAsync,
        Func<CancellationToken, Task<ReadOnlyMemory<byte>>> receiveAsync,
        Func<CancellationToken, CancellationToken, Task<ReadOnlyMemory<byte>>>? receiveWithResponseStartAsync,
        Action? clearReceiveBuffer,
        UdsOptions? options)
    {
        _sendAsync = sendAsync ?? throw new ArgumentNullException(nameof(sendAsync));
        _receiveAsync = receiveAsync ?? throw new ArgumentNullException(nameof(receiveAsync));
        _receiveWithResponseStartAsync = receiveWithResponseStartAsync;
        _clearBuffer = clearReceiveBuffer;
        _options = (options ?? new UdsOptions()).Clone();
        _options.Validate();
    }

    /// <summary>
    /// 从异步传输器接口创建异步 UDS 客户端。<br/>Creates an async UDS client from an <see cref="IAsyncTransmitter{T}"/>.
    /// </summary>
    /// <param name="transport">异步 UDS 传输器接口。<br/>The async UDS transport interface.</param>
    /// <param name="options">UDS 客户端参数（可选）。<br/>Optional UDS client parameters.</param>
    public AsyncUdsClient(IAsyncTransmitter<ReadOnlyMemory<byte>> transport, UdsOptions? options = null)
        : this(
            (data, ct) => transport.SendAsync(data, ct),
            transport.ReceiveAsync,
            transport is IAsyncResponseStartAwareTransmitter<ReadOnlyMemory<byte>> responseStartAware
                ? responseStartAware.ReceiveAsync
                : null,
            transport.ClearReceiveBuffer,
            options)
    {
    }

    /// <summary>从底层传输排空挂起的入站载荷。<br/>Drain pending inbound payloads from the underlying transport.</summary>
    public void ClearReceiveBuffer() => _clearBuffer?.Invoke();

    /// <inheritdoc/>
    public async Task<ReadOnlyMemory<byte>> SendRequestAsync(
        ReadOnlyMemory<byte> request,
        bool? suppressResponse = null,
        CancellationToken cancellationToken = default)
    {
        if (request.IsEmpty) throw new ArgumentException("Request is empty.", nameof(request));
        byte[] reqCopy = request.ToArray();
        ReadOnlyMemory<byte> stableRequest = reqCopy;
        if (!await _gate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException("A UDS request is already in flight.");
        var lifecycleStarted = false;
        try
        {
            _options.InitializeOrClearUpAction?.Invoke(true);
            lifecycleStarted = true;
            if (_options.ClearReceiveBufferBeforeRequest)
                _clearBuffer?.Invoke();

            bool suppress = suppressResponse ?? UdsMessage.IsSuppressPositiveResponse(stableRequest.Span);
            var overall = Stopwatch.StartNew();

            while (true)
            {
                await _sendAsync(stableRequest, cancellationToken).ConfigureAwait(false);

                // Suppressed-response handling: optionally wait briefly to catch a negative response.
                if (suppress)
                {
                    if (!_options.WaitWhileSuppressingResponse) return ReadOnlyMemory<byte>.Empty;
                    using var cts = new LinkedCts(_options.P2Client, cancellationToken);
                    while (true)
                    {
                        ReadOnlyMemory<byte> maybeResponse;
                        try
                        {
                            maybeResponse = await ReceiveWithResponseStartAsync(cts.Token, cancellationToken).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                        {
                            // Timeout - no response as expected.
                            return ReadOnlyMemory<byte>.Empty;
                        }

                        if (!UdsClientResponseHandling.IsMatchingResponse(stableRequest.Span, maybeResponse.Span))
                        {
                            if (_options.StrictServiceIdMatching)
                                throw UdsClientResponseHandling.CreateUnexpectedResponse(stableRequest.Span, maybeResponse.Span, "for suppressed");
                            continue;
                        }

                        if (UdsClientResponseHandling.IsResponsePendingFor(stableRequest.Span, maybeResponse.Span)
                            && _options.Rc78Handling == Rc78Handling.WaitForCompletion)
                        {
                            maybeResponse = await WaitForFinalResponseAfterRc78Async(stableRequest, overall, cancellationToken).ConfigureAwait(false);
                        }

                        return UdsClientResponseHandling.HandlePossibleNrc(stableRequest.Span, maybeResponse);
                    }
                }

                // Standard (unsuppressed) response path.
                ReadOnlyMemory<byte> response;
                while (true)
                {
                    try
                    {
                        response = await ReceiveWithResponseStartAsync(_options.P2Client, cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        throw new ProtocolException($"Timeout waiting for UDS response (P2 = {_options.P2Client.TotalMilliseconds:F0} ms).");
                    }

                    if (!UdsClientResponseHandling.IsMatchingResponse(stableRequest.Span, response.Span))
                    {
                        if (_options.StrictServiceIdMatching)
                            throw UdsClientResponseHandling.CreateUnexpectedResponse(stableRequest.Span, response.Span, "for");
                        continue;
                    }

                    // Handle NRC 0x78 (ResponsePending)
                    if (UdsClientResponseHandling.IsResponsePendingFor(stableRequest.Span, response.Span)
                        && _options.Rc78Handling == Rc78Handling.WaitForCompletion)
                    {
                        response = await WaitForFinalResponseAfterRc78Async(stableRequest, overall, cancellationToken).ConfigureAwait(false);
                    }

                    // Handle NRC 0x21 (BusyRepeatRequest)
                    if (UdsClientResponseHandling.IsBusyRepeatRequestFor(stableRequest.Span, response.Span)
                        && _options.Rc21Handling == Rc21Handling.Retry)
                    {
                        if (overall.Elapsed + _options.Rc21RetryInterval >= _options.Rc21CompletionTimeout)
                            throw new ProtocolException($"RC 0x21 retries exceeded {_options.Rc21CompletionTimeout.TotalMilliseconds:F0} ms.");
                        await Task.Delay(_options.Rc21RetryInterval, cancellationToken).ConfigureAwait(false);
                        break;
                    }

                    return UdsClientResponseHandling.HandlePossibleNrc(stableRequest.Span, response);
                }
            }
        }
        finally
        {
            try
            {
                if (lifecycleStarted)
                    _options.InitializeOrClearUpAction?.Invoke(false);
            }
            finally
            {
                _gate.Release();
            }
        }
    }

    private async Task<ReadOnlyMemory<byte>> WaitForFinalResponseAfterRc78Async(
        ReadOnlyMemory<byte> request,
        Stopwatch overall,
        CancellationToken cancellationToken)
    {
        if (overall.Elapsed >= _options.Rc78CompletionTimeout)
            throw UdsClientResponseHandling.CreateRc78Exceeded(_options);

        while (true)
        {
            var wait = GetNextRc78Wait(out var boundedByCompletionTimeout);
            ReadOnlyMemory<byte> response;
            try
            {
                response = await ReceiveWithResponseStartAsync(wait, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                if (boundedByCompletionTimeout || overall.Elapsed >= _options.Rc78CompletionTimeout)
                    throw UdsClientResponseHandling.CreateRc78Exceeded(_options);
                throw new ProtocolException($"Timeout waiting for final response after RC 0x78 (P2* = {_options.P2ClientExtended.TotalMilliseconds:F0} ms).");
            }

            if (!UdsClientResponseHandling.IsMatchingResponse(request.Span, response.Span))
            {
                if (_options.StrictServiceIdMatching)
                    throw new ProtocolException("Unexpected UDS response during RC 0x78 wait.");
                continue;
            }

            if (UdsClientResponseHandling.IsResponsePendingFor(request.Span, response.Span))
            {
                if (overall.Elapsed >= _options.Rc78CompletionTimeout)
                    throw UdsClientResponseHandling.CreateRc78Exceeded(_options);
                continue;
            }

            return response;
        }

        TimeSpan GetNextRc78Wait(out bool boundedByCompletionTimeout)
            => UdsClientResponseHandling.GetNextRc78Wait(_options, overall, out boundedByCompletionTimeout);
    }

    private async Task<ReadOnlyMemory<byte>> ReceiveWithResponseStartAsync(TimeSpan responseStartTimeout, CancellationToken cancellationToken)
    {
        using var cts = new LinkedCts(responseStartTimeout, cancellationToken);
        return await ReceiveWithResponseStartAsync(cts.Token, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ReadOnlyMemory<byte>> ReceiveWithResponseStartAsync(CancellationToken responseStartCancellationToken, CancellationToken cancellationToken)
    {
        if (_receiveWithResponseStartAsync is not null)
            return await _receiveWithResponseStartAsync(responseStartCancellationToken, cancellationToken).ConfigureAwait(false);
        return await _receiveAsync(responseStartCancellationToken).ConfigureAwait(false);
    }
}
