using System;
using System.Buffers.Binary;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

using DiagKit.Uds.Contracts;

using DiagKit.Uds.Exceptions;
using DiagKit.Uds.Internal;

namespace DiagKit.Uds.DoIp;

/// <summary>
/// 基于诊断消息载荷类型（0x8001）的异步 ISO 13400（DoIP）传输实现。
/// 发送/接收原语交换 DoIP 消息；此类负责 UDS 载荷的打包/解包，
/// 可选地等待诊断消息确认。<br/>
/// Asynchronous ISO 13400 (DoIP) transport implementation operating on the
/// diagnostic-message payload type (0x8001). The send/receive primitives exchange
/// DoIP messages; this class layers UDS payload in/out, optionally waiting for
/// diagnostic-message acknowledgments.
/// </summary>
/// <remarks>
/// 此类与传输无关：传入消息发送/接收委托或
/// <see cref="IAsyncTransmitter{DoIpMessage}"/>。调用方负责 TCP 连接生命周期、
/// 序列化和 TCP 流帧处理。如需直接使用 TCP 或已完成认证的 TLS 流，
/// 请优先使用 <see cref="AsyncDoIpStreamTransport"/>。<br/>
/// This class is transport-agnostic: pass in the message send/receive delegates
/// or an <see cref="IAsyncTransmitter{DoIpMessage}"/>. The caller is responsible
/// for TCP connection lifecycle, serialization, and framing across the TCP stream.
/// For direct TCP or already-authenticated TLS streams, prefer
/// <see cref="AsyncDoIpStreamTransport"/>.
/// </remarks>
public sealed class AsyncDoIpTransmitter : IAsyncTransmitter<ReadOnlyMemory<byte>>
{
    private readonly Func<DoIpMessage, CancellationToken, Task> _sendMessageAsync;
    private readonly Func<CancellationToken, Task<DoIpMessage>> _receiveMessageAsync;
    private readonly Action? _clearBuffer;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly SemaphoreSlim _activationGate = new(1, 1);
    private readonly DoIpMessageInbox _inbox = new();
    private readonly DoIpOptions _options;
    private int _activated;

    /// <summary>
    /// DoIP 传输参数的快照。
    /// 返回的对象是副本；修改它不会影响此传输器。<br/>
    /// Snapshot of DoIP transport parameters.
    /// The returned object is a copy; modifying it does not affect this transmitter.
    /// </summary>
    public DoIpOptions Options => _options.Clone();

    /// <summary>
    /// 使用原始发送/接收委托创建异步 DoIP 传输器。<br/>
    /// Creates an async DoIP transmitter with raw message send/receive delegates.
    /// </summary>
    /// <param name="sendMessageAsync">异步发送 DoIP 消息的委托。<br/>Delegate to send a DoIP message asynchronously.</param>
    /// <param name="receiveMessageAsync">异步接收 DoIP 消息的委托。<br/>Delegate to receive a DoIP message asynchronously.</param>
    /// <param name="clearReceiveBuffer">可选的清空接收缓冲区操作。<br/>Optional action to clear the receive buffer.</param>
    /// <param name="options">DoIP 传输参数（可选，使用默认值）。<br/>Optional DoIP transport parameters.</param>
    public AsyncDoIpTransmitter(
        Func<DoIpMessage, CancellationToken, Task> sendMessageAsync,
        Func<CancellationToken, Task<DoIpMessage>> receiveMessageAsync,
        Action? clearReceiveBuffer = null,
        DoIpOptions? options = null)
    {
        _sendMessageAsync = sendMessageAsync ?? throw new ArgumentNullException(nameof(sendMessageAsync));
        _receiveMessageAsync = receiveMessageAsync ?? throw new ArgumentNullException(nameof(receiveMessageAsync));
        _clearBuffer = clearReceiveBuffer;
        _options = (options ?? new DoIpOptions()).Clone();
        _options.Validate();
    }

    /// <summary>
    /// 从异步 DoIP 消息传输器接口创建异步 DoIP 传输器。<br/>
    /// Creates an async DoIP transmitter from an <see cref="IAsyncTransmitter{DoIpMessage}"/>.
    /// </summary>
    /// <param name="messageTransmitter">异步 DoIP 消息传输器接口。<br/>The async DoIP message transmitter interface.</param>
    /// <param name="options">DoIP 传输参数（可选）。<br/>Optional DoIP transport parameters.</param>
    public AsyncDoIpTransmitter(IAsyncTransmitter<DoIpMessage> messageTransmitter, DoIpOptions? options = null)
        : this(messageTransmitter.SendAsync, messageTransmitter.ReceiveAsync, messageTransmitter.ClearReceiveBuffer, options)
    {
    }

    /// <summary>
    /// 使用 System.Threading.Channels 创建异步 DoIP 传输器（用于内存通信）。<br/>
    /// Creates an async DoIP transmitter with <see cref="Channel{DoIpMessage}"/> for in-memory communication.
    /// </summary>
    /// <param name="sendChannel">发送 DoIP 消息的通道。<br/>Channel for sending DoIP messages.</param>
    /// <param name="receiveChannel">接收 DoIP 消息的通道。<br/>Channel for receiving DoIP messages.</param>
    /// <param name="options">DoIP 传输参数（可选）。<br/>Optional DoIP transport parameters.</param>
    public AsyncDoIpTransmitter(Channel<DoIpMessage> sendChannel, Channel<DoIpMessage> receiveChannel, DoIpOptions? options = null)
        : this(
            (msg, ct) => sendChannel.Writer.WriteAsync(msg, ct).AsTask(),
            ct => receiveChannel.Reader.ReadAsync(ct).AsTask(),
            () => { while (receiveChannel.Reader.TryRead(out _)) { } },
            options)
    {
    }

    /// <inheritdoc/>
    public void ClearReceiveBuffer()
    {
        _inbox.Clear();
        _clearBuffer?.Invoke();
    }

    // ─── Public API ─────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        if (data.IsEmpty) throw new ArgumentException("Data is empty.", nameof(data));
        if (!await _gate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException("A DoIP transmission is already in progress.");
        try
        {
            if (_options.AutoActivate) await EnsureActivatedAsync(cancellationToken).ConfigureAwait(false);
            await SendDiagnosticAsync(data, cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    /// <inheritdoc/>
    public async Task<ReadOnlyMemory<byte>> ReceiveAsync(CancellationToken cancellationToken = default)
    {
        if (!await _gate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException("A DoIP transmission is already in progress.");
        try
        {
            if (_options.AutoActivate) await EnsureActivatedAsync(cancellationToken).ConfigureAwait(false);
            return await ReceiveDiagnosticAsync(cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    /// <summary>
    /// 执行路由激活握手（ISO 13400-2 §7.1）。<br/>
    /// Perform the routing-activation handshake (ISO 13400-2 §7.1).
    /// </summary>
    /// <param name="cancellationToken">取消令牌。<br/>Cancellation token.</param>
    public async Task ActivateRoutingAsync(CancellationToken cancellationToken = default)
    {
        if (!await _gate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException("A DoIP transmission is already in progress.");
        try
        {
            await EnsureActivatedAsync(cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    // ─── Internals ──────────────────────────────────────────────────────

    private async Task EnsureActivatedAsync(CancellationToken ct)
    {
        if (Volatile.Read(ref _activated) != 0) return;

        await _activationGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _activated) != 0) return;
            await ActivateRoutingCoreAsync(ct).ConfigureAwait(false);
            Interlocked.Exchange(ref _activated, 1);
        }
        finally { _activationGate.Release(); }
    }

    private async Task ActivateRoutingCoreAsync(CancellationToken cancellationToken)
    {
        var payload = new byte[7 + (_options.OemSpecific?.Length == 4 ? 4 : 0)];
        DoIpMessageValidation.EnsurePayloadWithinLimit(payload.Length, _options);
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(0, 2), _options.SourceAddress);
        payload[2] = (byte)_options.ActivationType;
        // bytes 3..6 reserved (0x00)
        if (_options.OemSpecific is { Length: 4 })
            _options.OemSpecific.AsSpan().CopyTo(payload.AsSpan(7, 4));

        var req = new DoIpMessage(DoIpPayloadType.RoutingActivationRequest, payload);
        using var cts = new LinkedCts(_options.RoutingActivationTimeout, cancellationToken);
        await _sendMessageAsync(req, cts.Token).ConfigureAwait(false);

        DoIpMessage resp;
        try
        {
            resp = await ReceiveOfTypeAsync(DoIpPayloadType.RoutingActivationResponse, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ProtocolException("Timeout waiting for routing activation response.");
        }

        DoIpMessageValidation.ValidateRoutingActivationResponse(resp, _options);
    }

    private async Task SendDiagnosticAsync(ReadOnlyMemory<byte> uds, CancellationToken ct)
    {
        DoIpMessageValidation.EnsurePayloadWithinLimit(4 + uds.Length, _options);
        var payload = new byte[4 + uds.Length];
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(0, 2), _options.SourceAddress);
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(2, 2), _options.TargetAddress);
        uds.Span.CopyTo(payload.AsSpan(4));

        var msg = new DoIpMessage(DoIpPayloadType.DiagnosticMessage, payload);
        using (var cts = new LinkedCts(_options.TcpGeneralTimeout, ct))
            await _sendMessageAsync(msg, cts.Token).ConfigureAwait(false);

        if (!_options.WaitForDiagnosticAck) return;

        using var ackCts = new LinkedCts(_options.DiagnosticAckTimeout, ct);
        DoIpMessage ack;
        try
        {
            ack = await ReceiveOfTypeAsync(
                ackCts.Token,
                DoIpPayloadType.DiagnosticMessagePositiveAck).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new ProtocolException("Timeout waiting for diagnostic-message acknowledgment.");
        }
        DoIpMessageValidation.ValidateDiagnosticAck(ack, _options);
    }

    private async Task<ReadOnlyMemory<byte>> ReceiveDiagnosticAsync(CancellationToken ct)
    {
        using var cts = new LinkedCts(_options.DiagnosticResponseTimeout, ct);
        var msg = await ReceiveOfTypeAsync(DoIpPayloadType.DiagnosticMessage, cts.Token).ConfigureAwait(false);
        DoIpMessageValidation.ValidateDiagnosticMessage(msg, _options);
        return msg.Payload[4..];
    }

    private async Task<DoIpMessage> ReceiveOfTypeAsync(DoIpPayloadType expected, CancellationToken ct)
        => await ReceiveOfTypeAsync(ct, expected).ConfigureAwait(false);

    private async Task<DoIpMessage> ReceiveOfTypeAsync(CancellationToken ct, params DoIpPayloadType[] expected)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            if (_inbox.TryTake(expected, out var cached))
                return cached;

            var msg = await _receiveMessageAsync(ct).ConfigureAwait(false);
            DoIpMessageValidation.EnsurePayloadWithinLimit(msg.Payload.Length, _options);

            if (await HandleControlMessageAsync(msg, ct).ConfigureAwait(false))
                continue;

            if (DoIpMessageValidation.IsExpected(msg.PayloadType, expected))
                return msg;

            _inbox.Enqueue(msg, _options.InboxLimit);
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
        await _sendMessageAsync(new DoIpMessage(DoIpPayloadType.AliveCheckResponse, payload), cts.Token).ConfigureAwait(false);
    }

}
