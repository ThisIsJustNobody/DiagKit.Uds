using System;
using System.Buffers.Binary;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using DiagKit.Uds.Contracts;

using DiagKit.Uds.Exceptions;
using DiagKit.Uds.Internal;

namespace DiagKit.Uds.DoIp;

/// <summary>
/// 基于字节流（如 <see cref="System.Net.Sockets.NetworkStream"/> 或已完成认证的
/// <see cref="System.Net.Security.SslStream"/>）的异步 ISO 13400-2 DoIP 传输器。<br/>
/// Async ISO 13400-2 DoIP transport over a byte stream such as
/// <see cref="System.Net.Sockets.NetworkStream"/> or an already-authenticated
/// <see cref="System.Net.Security.SslStream"/>.
/// </summary>
/// <remarks>
/// 此流通过先读取 8 字节 DoIP 通用头部再读取该头部声明的精确载荷长度来完成帧处理。
/// 此类型接受 SslStream 作为普通 Stream，但 ISO 13400-2:2025 的 TLS 安全诊断
/// 通信细节在申明端到端 TLS 合规前需要单独的标准审查。<br/>
/// The stream is framed by reading the 8-byte DoIP generic header first, then the
/// exact payload length declared by that header. The type accepts SslStream as a
/// normal Stream, but TLS-specific ISO 13400-2:2025 secured diagnostic
/// communication details require a separate standards review before claiming
/// end-to-end TLS conformance.
/// </remarks>
public sealed class AsyncDoIpStreamTransport : IAsyncTransmitter<ReadOnlyMemory<byte>>, IAsyncDisposable
{
    private readonly Stream _stream;
    private readonly bool _leaveOpen;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly SemaphoreSlim _activationGate = new(1, 1);
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly CancellationTokenSource _disposeCts = new();
    private readonly DoIpMessageInbox _inbox = new();
    private readonly object _lifetimeSync = new();
    private readonly DoIpOptions _options;
    private int _activated;
    private int _activeOperations;
    private int _disposed;
    private TaskCompletionSource? _drained;

    /// <summary>
    /// DoIP 传输参数的快照。
    /// 返回的对象是副本；修改它不会影响此传输器。<br/>
    /// Snapshot of DoIP transport parameters.
    /// The returned object is a copy; modifying it does not affect this transport.
    /// </summary>
    public DoIpOptions Options => _options.Clone();

    /// <summary>
    /// 基于字节流（如 NetworkStream / SslStream）创建 DoIP 传输器。<br/>
    /// Creates a DoIP transport over a byte stream such as NetworkStream or SslStream.
    /// </summary>
    /// <param name="stream">用于 DoIP 通信的字节流。<br/>The byte stream for DoIP communication.</param>
    /// <param name="options">DoIP 传输参数（可选）。<br/>Optional DoIP transport parameters.</param>
    /// <param name="leaveOpen">释放时是否保持流打开。<br/>Whether to leave the stream open on disposal.</param>
    public AsyncDoIpStreamTransport(Stream stream, DoIpOptions? options = null, bool leaveOpen = false)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _leaveOpen = leaveOpen;
        _options = (options ?? new DoIpOptions()).Clone();
        _options.Validate();
    }

    /// <inheritdoc/>
    public void ClearReceiveBuffer()
    {
        _inbox.Clear();
    }

    /// <inheritdoc/>
    public async Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        if (data.IsEmpty) throw new ArgumentException("Data is empty.", nameof(data));
        var operationCts = BeginOperation(cancellationToken);
        try
        {
            if (!await _operationGate.WaitAsync(0, operationCts.Token).ConfigureAwait(false))
                throw new InvalidOperationException("A DoIP transmission is already in progress.");
            try
            {
                if (_options.AutoActivate) await EnsureActivatedAsync(operationCts.Token).ConfigureAwait(false);
                await SendDiagnosticAsync(data, operationCts.Token).ConfigureAwait(false);
            }
            finally { _operationGate.Release(); }
        }
        catch (OperationCanceledException) when (IsDisposedCancellation(cancellationToken))
        {
            throw new ObjectDisposedException(nameof(AsyncDoIpStreamTransport));
        }
        finally { CompleteOperation(operationCts); }
    }

    /// <inheritdoc/>
    public async Task<ReadOnlyMemory<byte>> ReceiveAsync(CancellationToken cancellationToken = default)
    {
        var operationCts = BeginOperation(cancellationToken);
        try
        {
            if (!await _operationGate.WaitAsync(0, operationCts.Token).ConfigureAwait(false))
                throw new InvalidOperationException("A DoIP transmission is already in progress.");
            try
            {
                if (_options.AutoActivate) await EnsureActivatedAsync(operationCts.Token).ConfigureAwait(false);
                return await ReceiveDiagnosticAsync(operationCts.Token).ConfigureAwait(false);
            }
            finally { _operationGate.Release(); }
        }
        catch (OperationCanceledException) when (IsDisposedCancellation(cancellationToken))
        {
            throw new ObjectDisposedException(nameof(AsyncDoIpStreamTransport));
        }
        finally { CompleteOperation(operationCts); }
    }

    /// <summary>
    /// 执行一次路由激活（ISO 13400-2:2012, 7.1.5）。
    /// 成功激活后重复调用不会再次发送激活请求。<br/>
    /// Perform routing activation once (ISO 13400-2:2012, 7.1.5). Repeated calls
    /// return without sending another activation request after a successful one.
    /// </summary>
    /// <param name="cancellationToken">取消令牌。<br/>Cancellation token.</param>
    public async Task ActivateRoutingAsync(CancellationToken cancellationToken = default)
    {
        var operationCts = BeginOperation(cancellationToken);
        try
        {
            if (!await _operationGate.WaitAsync(0, operationCts.Token).ConfigureAwait(false))
                throw new InvalidOperationException("A DoIP transmission is already in progress.");
            try
            {
                await EnsureActivatedAsync(operationCts.Token).ConfigureAwait(false);
            }
            finally { _operationGate.Release(); }
        }
        catch (OperationCanceledException) when (IsDisposedCancellation(cancellationToken))
        {
            throw new ObjectDisposedException(nameof(AsyncDoIpStreamTransport));
        }
        finally { CompleteOperation(operationCts); }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _disposeCts.Cancel();

        Task? drainTask = null;
        lock (_lifetimeSync)
        {
            if (Volatile.Read(ref _activeOperations) != 0)
                drainTask = (_drained ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).Task;
        }

        if (drainTask is not null)
            await drainTask.ConfigureAwait(false);

        if (!_leaveOpen)
            await _stream.DisposeAsync().ConfigureAwait(false);

        _operationGate.Dispose();
        _activationGate.Dispose();
        _writeGate.Dispose();
        _disposeCts.Dispose();
    }

    private async Task EnsureActivatedAsync(CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _activated) != 0) return;

        await _activationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _activated) != 0) return;

            var payload = new byte[7 + (_options.OemSpecific?.Length == 4 ? 4 : 0)];
            DoIpMessageValidation.EnsurePayloadWithinLimit(payload.Length, _options);
            BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(0, 2), _options.SourceAddress);
            payload[2] = (byte)_options.ActivationType;
            if (_options.OemSpecific is { Length: 4 })
                _options.OemSpecific.AsSpan().CopyTo(payload.AsSpan(7, 4));

            using var cts = new LinkedCts(_options.RoutingActivationTimeout, cancellationToken);
            await WriteMessageAsync(new DoIpMessage(DoIpPayloadType.RoutingActivationRequest, payload), cts.Token).ConfigureAwait(false);

            DoIpMessage response;
            try
            {
                response = await ReceiveOfTypeAsync(cts.Token, DoIpPayloadType.RoutingActivationResponse).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new ProtocolException("Timeout waiting for routing activation response.");
            }

            DoIpMessageValidation.ValidateRoutingActivationResponse(response, _options);
            Interlocked.Exchange(ref _activated, 1);
        }
        finally { _activationGate.Release(); }
    }

    private async Task SendDiagnosticAsync(ReadOnlyMemory<byte> uds, CancellationToken cancellationToken)
    {
        DoIpMessageValidation.EnsurePayloadWithinLimit(4 + uds.Length, _options);
        var payload = new byte[4 + uds.Length];
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(0, 2), _options.SourceAddress);
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(2, 2), _options.TargetAddress);
        uds.Span.CopyTo(payload.AsSpan(4));

        using (var sendCts = new LinkedCts(_options.TcpGeneralTimeout, cancellationToken))
            await WriteMessageAsync(new DoIpMessage(DoIpPayloadType.DiagnosticMessage, payload), sendCts.Token).ConfigureAwait(false);

        if (!_options.WaitForDiagnosticAck) return;

        using var ackCts = new LinkedCts(_options.DiagnosticAckTimeout, cancellationToken);
        DoIpMessage ack;
        try
        {
            ack = await ReceiveOfTypeAsync(ackCts.Token, DoIpPayloadType.DiagnosticMessagePositiveAck).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ProtocolException("Timeout waiting for diagnostic-message acknowledgment.");
        }

        DoIpMessageValidation.ValidateDiagnosticAck(ack, _options);
    }

    private async Task<ReadOnlyMemory<byte>> ReceiveDiagnosticAsync(CancellationToken cancellationToken)
    {
        using var cts = new LinkedCts(_options.DiagnosticResponseTimeout, cancellationToken);
        DoIpMessage message;
        try
        {
            message = await ReceiveOfTypeAsync(cts.Token, DoIpPayloadType.DiagnosticMessage).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ProtocolException("Timeout waiting for diagnostic response.");
        }

        DoIpMessageValidation.ValidateDiagnosticMessage(message, _options);
        return message.Payload[4..];
    }

    private async Task<DoIpMessage> ReceiveOfTypeAsync(CancellationToken cancellationToken, params DoIpPayloadType[] expected)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_inbox.TryTake(expected, out var cached))
                return cached;

            var message = await DoIpStreamCodec.ReadMessageAsync(_stream, _options.MaxPayloadLength, cancellationToken).ConfigureAwait(false);
            if (await HandleControlMessageAsync(message, cancellationToken).ConfigureAwait(false))
                continue;

            if (DoIpMessageValidation.IsExpected(message.PayloadType, expected))
                return message;

            _inbox.Enqueue(message, _options.InboxLimit);
        }
    }

    private async Task<bool> HandleControlMessageAsync(DoIpMessage message, CancellationToken cancellationToken)
    {
        switch (message.PayloadType)
        {
            case DoIpPayloadType.GenericHeaderNegativeAcknowledge:
                DoIpMessageValidation.ThrowGenericHeaderNack(message);
                return true;

            case DoIpPayloadType.DiagnosticMessageNegativeAck:
                DoIpMessageValidation.ThrowDiagnosticNack(message, _options);
                return true;

            case DoIpPayloadType.AliveCheckRequest when _options.AutoRespondAliveCheck:
                await SendAliveCheckResponseAsync(cancellationToken).ConfigureAwait(false);
                return true;

            default:
                return false;
        }
    }

    private async Task SendAliveCheckResponseAsync(CancellationToken cancellationToken)
    {
        var payload = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(payload, _options.SourceAddress);
        using var cts = new LinkedCts(_options.TcpGeneralTimeout, cancellationToken);
        await WriteMessageAsync(new DoIpMessage(DoIpPayloadType.AliveCheckResponse, payload), cts.Token).ConfigureAwait(false);
    }

    private async Task WriteMessageAsync(DoIpMessage message, CancellationToken cancellationToken)
    {
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await DoIpStreamCodec.WriteMessageAsync(_stream, message, _options.MaxPayloadLength, cancellationToken).ConfigureAwait(false);
        }
        finally { _writeGate.Release(); }
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
            throw new ObjectDisposedException(nameof(AsyncDoIpStreamTransport));
    }

    private CancellationTokenSource BeginOperation(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        Interlocked.Increment(ref _activeOperations);
        if (Volatile.Read(ref _disposed) != 0)
        {
            CompleteOperation(null);
            throw new ObjectDisposedException(nameof(AsyncDoIpStreamTransport));
        }

        return CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _disposeCts.Token);
    }

    private void CompleteOperation(CancellationTokenSource? operationCts)
    {
        operationCts?.Dispose();
        if (Interlocked.Decrement(ref _activeOperations) != 0)
            return;

        lock (_lifetimeSync)
            _drained?.TrySetResult();
    }

    private bool IsDisposedCancellation(CancellationToken callerToken)
    {
        return _disposeCts.IsCancellationRequested && !callerToken.IsCancellationRequested;
    }

}
