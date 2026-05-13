using System;
using System.Buffers;
using System.Diagnostics;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

using DiagKit.Uds.Contracts;

using DiagKit.Uds.Exceptions;
using DiagKit.Uds.Internal;

namespace DiagKit.Uds.DoCan;

/// <summary>
/// 异步 ISO 15765-2（DoCAN）传输实现。<br/>Asynchronous ISO 15765-2 (DoCAN) transport implementation.
/// </summary>
/// <remarks>
/// 单个实例复用同一时刻的一次会话（发送-接收或接收-发送）。并发调用将被拒绝并抛出
/// <see cref="InvalidOperationException"/> 而不是排队，因此调用方负责串行化。<br/>
/// A single instance multiplexes one conversation at a time (send-and-receive or
/// receive-and-receive). Concurrent calls are rejected with <see cref="InvalidOperationException"/>
/// rather than queued, so the caller is responsible for sequencing.
/// </remarks>
public sealed class AsyncDoCanTransmitter : IAsyncTransmitter<ReadOnlyMemory<byte>>
{
    private readonly Func<CanFrame, CancellationToken, Task> _sendAsync;
    private readonly Func<CancellationToken, Task<CanFrame>> _receiveAsync;
    private readonly Action? _clearBuffer;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly DoCanOptions _options;

    /// <summary>
    /// DoCAN 传输参数的快照。返回的对象是副本，修改它不会影响此传输器。<br/>
    /// Snapshot of DoCAN transport parameters. The returned object is a copy; modifying it does not affect this transmitter.
    /// </summary>
    public DoCanOptions Options => _options.Clone();

    /// <summary>
    /// 使用原始发送/接收委托创建异步 DoCAN 传输器。<br/>
    /// Creates an async DoCAN transmitter with raw send/receive delegates.
    /// </summary>
    /// <param name="sendAsync">异步发送 CAN 帧的委托。<br/>Delegate to send a CAN frame asynchronously.</param>
    /// <param name="receiveAsync">异步接收 CAN 帧的委托。<br/>Delegate to receive a CAN frame asynchronously.</param>
    /// <param name="clearReceiveBuffer">可选的清空接收缓冲区操作。<br/>Optional action to clear the receive buffer.</param>
    /// <param name="options">DoCAN 传输参数（可选，使用默认值）。<br/>Optional DoCAN transport parameters.</param>
    public AsyncDoCanTransmitter(
        Func<CanFrame, CancellationToken, Task> sendAsync,
        Func<CancellationToken, Task<CanFrame>> receiveAsync,
        Action? clearReceiveBuffer = null,
        DoCanOptions? options = null)
    {
        _sendAsync = sendAsync ?? throw new ArgumentNullException(nameof(sendAsync));
        _receiveAsync = receiveAsync ?? throw new ArgumentNullException(nameof(receiveAsync));
        _clearBuffer = clearReceiveBuffer;
        _options = (options ?? new DoCanOptions()).Clone();
        _options.Validate();
    }

    /// <summary>
    /// 从异步 CAN 传输器接口创建异步 DoCAN 传输器。<br/>
    /// Creates an async DoCAN transmitter from an <see cref="IAsyncTransmitter{CanFrame}"/>.
    /// </summary>
    /// <param name="canTransmitter">异步 CAN 传输器接口。<br/>The async CAN transmitter interface.</param>
    /// <param name="options">DoCAN 传输参数（可选）。<br/>Optional DoCAN transport parameters.</param>
    public AsyncDoCanTransmitter(IAsyncTransmitter<CanFrame> canTransmitter, DoCanOptions? options = null)
        : this(canTransmitter.SendAsync, canTransmitter.ReceiveAsync, canTransmitter.ClearReceiveBuffer, options)
    {
    }

    /// <summary>
    /// 使用 System.Threading.Channels 创建异步 DoCAN 传输器，用于内存中通信。<br/>
    /// Creates an async DoCAN transmitter with <see cref="Channel{CanFrame}"/> for in-memory communication.
    /// </summary>
    /// <param name="sendChannel">发送 CAN 帧的通道。<br/>Channel for sending CAN frames.</param>
    /// <param name="receiveChannel">接收 CAN 帧的通道。<br/>Channel for receiving CAN frames.</param>
    /// <param name="options">DoCAN 传输参数（可选）。<br/>Optional DoCAN transport parameters.</param>
    public AsyncDoCanTransmitter(Channel<CanFrame> sendChannel, Channel<CanFrame> receiveChannel, DoCanOptions? options = null)
        : this(
            (frame, ct) => sendChannel.Writer.WriteAsync(frame, ct).AsTask(),
            ct => receiveChannel.Reader.ReadAsync(ct).AsTask(),
            () => { while (receiveChannel.Reader.TryRead(out _)) { } },
            options)
    {
    }

    /// <inheritdoc/>
    public void ClearReceiveBuffer() => _clearBuffer?.Invoke();

    // ─── Public API ─────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        if (data.IsEmpty) throw new ArgumentException("Data is empty.", nameof(data));
        if (!await _gate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException("A DoCAN transmission is already in progress.");
        try
        {
            await SendContentAsync(data, cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    /// <inheritdoc/>
    public async Task<ReadOnlyMemory<byte>> ReceiveAsync(CancellationToken cancellationToken = default)
    {
        if (!await _gate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException("A DoCAN transmission is already in progress.");
        try
        {
            return await ReceiveContentAsync(cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    // ─── Send side ──────────────────────────────────────────────────────

    private async Task SendContentAsync(ReadOnlyMemory<byte> content, CancellationToken ct)
    {
        var options = _options;

        int maxFrameLen = CanFrame.DlcToLength(options.MaxDlc);
        byte[] rented = ArrayPool<byte>.Shared.Rent(maxFrameLen);
        try
        {
            int contentLen = content.Length;

            int sfOrFfLen = DoCanFraming.GetSuitableSfOrFfLength(contentLen, options.MinDlc, options.MaxDlc, out var needMore);

            if (!needMore)
            {
                // Single Frame
                int sfLen = sfOrFfLen;
                DoCanFraming.EncodeSingleFrame(content.Span, rented.AsSpan(0, sfLen), sfLen, options.PaddingValue);
                using var cts = new LinkedCts(options.TimeoutAs, ct);
                await _sendAsync(MakeRequestFrameCopy(rented, sfLen), cts.Token).ConfigureAwait(false);
                return;
            }

            // First Frame
            int ffLen = maxFrameLen;
            DoCanFraming.EncodeFirstFrame(content.Span, rented.AsSpan(0, ffLen), ffLen, out var consumed);
            using (var cts = new LinkedCts(options.TimeoutAs, ct))
                await _sendAsync(MakeRequestFrameCopy(rented, ffLen), cts.Token).ConfigureAwait(false);

            int remaining = contentLen - consumed;
            int pos = consumed;
            byte sn = 0;
            int cfCapacity = ffLen - 1;
            int waitFrames = 0;

            while (remaining > 0)
            {
                CanFrame fc;
                try
                {
                    using var cts = new LinkedCts(options.TimeoutBs, ct);
                    fc = await ReceiveMatchingFrameAsync(cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    throw new ProtocolException($"Timeout waiting for flow control frame (N_Bs = {options.TimeoutBs.TotalMilliseconds:F0} ms).");
                }

                if (!DoCanFraming.TryReadFlowControlFrame(fc.Data.Span, out var status, out var bs, out var st))
                    throw new FrameFormatException("Received a malformed flow-control frame.");

                switch (status)
                {
                    case DoCanFlowStatus.Wait:
                        DoCanTransmitterShared.AcceptFlowControlWaitFrame(ref waitFrames, options.MaxFlowControlWaitFrames);
                        await Task.Delay(options.FlowControlWaitInterval, ct).ConfigureAwait(false);
                        continue;

                    case DoCanFlowStatus.Overflow:
                        throw new DoCanOverflowException();

                    case DoCanFlowStatus.Continue:
                        break;

                    default:
                        throw new ProtocolException($"Invalid flow control status: {status}.");
                }

                // Transmit up to `bs` consecutive frames (0 = unbounded)
                int maxBlock = bs == 0 ? int.MaxValue : bs;
                int sent = 0;
                var sTminDelay = DoCanFraming.STminToTimeSpan(st);

                while (sent < maxBlock && remaining > 0)
                {
                    if (options.TimeCs > TimeSpan.Zero)
                        await Task.Delay(options.TimeCs, ct).ConfigureAwait(false);

                    if (sent > 0 && sTminDelay > TimeSpan.Zero)
                        await PreciseDelay(sTminDelay, ct).ConfigureAwait(false);

                    // Pick frame length for this CF
                    int frameLen = DoCanTransmitterShared.GetConsecutiveFrameLength(options, ffLen, cfCapacity, remaining);

                    sn = DoCanFraming.NextSequenceNumber(sn);
                    int copy = Math.Min(remaining, frameLen - 1);
                    DoCanFraming.EncodeConsecutiveFrame(content.Span.Slice(pos, copy), rented.AsSpan(0, frameLen), sn, frameLen, options.PaddingValue);

                    using var cts = new LinkedCts(options.TimeoutAs, ct);
                    await _sendAsync(MakeRequestFrameCopy(rented, frameLen), cts.Token).ConfigureAwait(false);

                    pos += copy;
                    remaining -= copy;
                    sent++;
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    // ─── Receive side ───────────────────────────────────────────────────

    private async Task<ReadOnlyMemory<byte>> ReceiveContentAsync(CancellationToken ct)
    {
        var options = _options;

        CanFrame first;
        using (var cts = new LinkedCts(options.TimeoutAr, ct))
            first = await ReceiveMatchingFrameAsync(cts.Token).ConfigureAwait(false);

        var type = DoCanFraming.GetFrameType(first.Data.Span);
        switch (type)
        {
            case DoCanFrameType.SingleFrame:
                if (!DoCanFraming.TryReadSingleFrame(first.Data.Span, out var sfPayload))
                    throw new FrameFormatException("Received a malformed single frame.");
                return sfPayload.ToArray();

            case DoCanFrameType.FirstFrame:
                return await ReceiveSegmentedAsync(first, ct).ConfigureAwait(false);

            default:
                throw new FrameFormatException($"Expected SF or FF, got {type}.");
        }
    }

    private async Task<ReadOnlyMemory<byte>> ReceiveSegmentedAsync(CanFrame firstFrame, CancellationToken ct)
    {
        var options = _options;
        if (!DoCanFraming.TryReadFirstFrame(firstFrame.Data.Span, out var totalLength, out var headPayload))
            throw new FrameFormatException("Received a malformed first frame.");

        int fcDlc = Math.Max(3, (int)CanFrame.DlcToLength(options.MinDlc));
        byte[] fcRent = ArrayPool<byte>.Shared.Rent(fcDlc);
        try
        {
            if (totalLength > options.MaxSegmentedPayloadLength)
            {
                DoCanFraming.EncodeFlowControlFrame(fcRent.AsSpan(0, fcDlc), DoCanFlowStatus.Overflow, options.BlockSize, options.STmin, fcDlc, options.PaddingValue);
                using (var cts = new LinkedCts(options.TimeoutAs, ct))
                    await _sendAsync(MakeRequestFrame(fcRent.AsMemory(0, fcDlc)), cts.Token).ConfigureAwait(false);
                throw new ProtocolException($"Segmented message length {totalLength} exceeds MaxSegmentedPayloadLength {options.MaxSegmentedPayloadLength}.");
            }

            var buffer = new byte[(int)totalLength];
            int copied = Math.Min(headPayload.Length, buffer.Length);
            headPayload[..copied].CopyTo(buffer);
            int pos = copied;

            byte expectedSn = 1;
            while (pos < buffer.Length)
            {
                // Send flow control
                if (options.TimeBr > TimeSpan.Zero)
                    await Task.Delay(options.TimeBr, ct).ConfigureAwait(false);

                DoCanFraming.EncodeFlowControlFrame(fcRent.AsSpan(0, fcDlc), DoCanFlowStatus.Continue, options.BlockSize, options.STmin, fcDlc, options.PaddingValue);
                using (var cts = new LinkedCts(options.TimeoutAs, ct))
                    await _sendAsync(MakeRequestFrame(fcRent.AsMemory(0, fcDlc)), cts.Token).ConfigureAwait(false);

                int block = 0;
                int maxBlock = options.BlockSize == 0 ? int.MaxValue : options.BlockSize;
                while (block < maxBlock && pos < buffer.Length)
                {
                    CanFrame cf;
                    try
                    {
                        using var cts = new LinkedCts(options.TimeoutCr, ct);
                        cf = await ReceiveMatchingFrameAsync(cts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                    {
                        throw new ProtocolException($"Timeout waiting for consecutive frame (N_Cr = {options.TimeoutCr.TotalMilliseconds:F0} ms).");
                    }

                    if (!DoCanFraming.TryReadConsecutiveFrame(cf.Data.Span, out var sn, out var cfPayload))
                        throw new FrameFormatException("Received a malformed consecutive frame.");

                    if (sn != expectedSn)
                        throw new FrameFormatException($"Unexpected CF sequence number: got {sn:X1}, expected {expectedSn:X1}.");

                    int take = Math.Min(cfPayload.Length, buffer.Length - pos);
                    cfPayload[..take].CopyTo(buffer.AsSpan(pos));
                    pos += take;
                    expectedSn = DoCanFraming.NextSequenceNumber(expectedSn);
                    block++;
                }
            }

            return buffer;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(fcRent);
        }
    }

    // ─── Helpers ────────────────────────────────────────────────────────

    private CanFrame MakeRequestFrame(ReadOnlyMemory<byte> data)
        => CanFrame.CreateCopy(_options.RequestId, data, _options.UseFd, _options.UseFd && _options.BrsEnabled, _options.RequestIdExtended);

    /// <summary>Create a CanFrame with an owned copy of <paramref name="length"/> bytes from pooled buffer.</summary>
    private CanFrame MakeRequestFrameCopy(byte[] source, int length)
        => CanFrame.CreateCopy(
            _options.RequestId,
            new ReadOnlyMemory<byte>(source, 0, length),
            _options.UseFd,
            _options.UseFd && _options.BrsEnabled,
            _options.RequestIdExtended);

    private async Task<CanFrame> ReceiveMatchingFrameAsync(CancellationToken ct)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var frame = await _receiveAsync(ct).ConfigureAwait(false);
            if (DoCanTransmitterShared.FrameMatches(frame, _options)) return frame;
        }
    }

    private static async Task PreciseDelay(TimeSpan delay, CancellationToken ct)
    {
        if (delay <= TimeSpan.Zero) return;
        if (delay >= TimeSpan.FromMilliseconds(15))
        {
            await Task.Delay(delay, ct).ConfigureAwait(false);
            return;
        }
        var spinTail = TimeSpan.FromMilliseconds(1);
        var sw = Stopwatch.StartNew();
        if (delay > spinTail)
            await Task.Delay(delay - spinTail, ct).ConfigureAwait(false);
        while (sw.Elapsed < delay)
        {
            ct.ThrowIfCancellationRequested();
            Thread.SpinWait(10);
        }
    }
}
