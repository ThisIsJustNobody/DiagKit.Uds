using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;

using DiagKit.Uds.Contracts;

using DiagKit.Uds.Exceptions;
using DiagKit.Uds.Internal;

namespace DiagKit.Uds.DoCan;

/// <summary>
/// 同步 ISO 15765-2（DoCAN）传输实现。<br/>Synchronous ISO 15765-2 (DoCAN) transport implementation.
/// </summary>
/// <remarks>
/// 单个实例复用同一时刻的一次会话。并发调用将被拒绝并抛出
/// <see cref="InvalidOperationException"/>。<br/>
/// A single instance multiplexes one conversation at a time. Concurrent calls are
/// rejected with <see cref="InvalidOperationException"/>.
/// </remarks>
public sealed class DoCanTransmitter : IResponseStartAwareTransmitter<ReadOnlyMemory<byte>>
{
    private readonly Action<CanFrame, CancellationToken> _send;
    private readonly Func<CancellationToken, CanFrame> _receive;
    private readonly Action? _clearBuffer;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly DoCanOptions _options;

    /// <summary>
    /// DoCAN 传输参数的快照。返回的对象是副本，修改它不会影响此传输器。<br/>
    /// Snapshot of DoCAN transport parameters. The returned object is a copy; modifying it does not affect this transmitter.
    /// </summary>
    public DoCanOptions Options => _options.Clone();

    /// <summary>
    /// 使用原始发送/接收委托创建同步 DoCAN 传输器。<br/>
    /// Creates a synchronous DoCAN transmitter with raw send/receive delegates.
    /// </summary>
    /// <param name="send">发送 CAN 帧的委托。<br/>Delegate to send a CAN frame.</param>
    /// <param name="receive">接收 CAN 帧的委托。<br/>Delegate to receive a CAN frame.</param>
    /// <param name="clearReceiveBuffer">可选的清空接收缓冲区操作。<br/>Optional action to clear the receive buffer.</param>
    /// <param name="options">DoCAN 传输参数（可选，使用默认值）。<br/>Optional DoCAN transport parameters.</param>
    public DoCanTransmitter(
        Action<CanFrame, CancellationToken> send,
        Func<CancellationToken, CanFrame> receive,
        Action? clearReceiveBuffer = null,
        DoCanOptions? options = null)
    {
        _send = send ?? throw new ArgumentNullException(nameof(send));
        _receive = receive ?? throw new ArgumentNullException(nameof(receive));
        _clearBuffer = clearReceiveBuffer;
        _options = (options ?? new DoCanOptions()).Clone();
        _options.Validate();
    }

    /// <summary>
    /// 从同步 CAN 传输器接口创建同步 DoCAN 传输器。<br/>
    /// Creates a synchronous DoCAN transmitter from an <see cref="ITransmitter{CanFrame}"/>.
    /// </summary>
    /// <param name="canTransmitter">同步 CAN 传输器接口。<br/>The synchronous CAN transmitter interface.</param>
    /// <param name="options">DoCAN 传输参数（可选）。<br/>Optional DoCAN transport parameters.</param>
    public DoCanTransmitter(ITransmitter<CanFrame> canTransmitter, DoCanOptions? options = null)
        : this(canTransmitter.Send, canTransmitter.Receive, canTransmitter.ClearReceiveBuffer, options)
    {
    }

    /// <summary>
    /// 使用 BlockingCollection 创建同步 DoCAN 传输器，用于内存中通信。<br/>
    /// Creates a synchronous DoCAN transmitter with <see cref="BlockingCollection{CanFrame}"/> for in-memory communication.
    /// </summary>
    /// <param name="sendQueue">发送 CAN 帧的阻塞队列。<br/>Blocking collection for sending CAN frames.</param>
    /// <param name="receiveQueue">接收 CAN 帧的阻塞队列。<br/>Blocking collection for receiving CAN frames.</param>
    /// <param name="options">DoCAN 传输参数（可选）。<br/>Optional DoCAN transport parameters.</param>
    public DoCanTransmitter(BlockingCollection<CanFrame> sendQueue, BlockingCollection<CanFrame> receiveQueue, DoCanOptions? options = null)
        : this(
            (frame, ct) => sendQueue.Add(frame, ct),
            ct => receiveQueue.Take(ct),
            () => { while (receiveQueue.TryTake(out _)) { } },
            options)
    {
    }

    /// <inheritdoc/>
    public void ClearReceiveBuffer() => _clearBuffer?.Invoke();

    // ─── Public API ─────────────────────────────────────────────────────

    /// <inheritdoc/>
    public void Send(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        if (data.IsEmpty) throw new ArgumentException("Data is empty.", nameof(data));
        if (!_gate.Wait(0, cancellationToken))
            throw new InvalidOperationException("A DoCAN transmission is already in progress.");
        try { SendContent(data, cancellationToken); }
        finally { _gate.Release(); }
    }

    /// <inheritdoc/>
    public ReadOnlyMemory<byte> Receive(CancellationToken cancellationToken = default)
    {
        if (!_gate.Wait(0, cancellationToken))
            throw new InvalidOperationException("A DoCAN transmission is already in progress.");
        try { return ReceiveContent(cancellationToken, cancellationToken, _options.ReceiveStartTimeout ?? _options.TimeoutAr); }
        finally { _gate.Release(); }
    }

    /// <inheritdoc/>
    public ReadOnlyMemory<byte> Receive(
        CancellationToken responseStartCancellationToken,
        CancellationToken cancellationToken = default)
    {
        if (!_gate.Wait(0, cancellationToken))
            throw new InvalidOperationException("A DoCAN transmission is already in progress.");
        try { return ReceiveContent(responseStartCancellationToken, cancellationToken, receiveStartTimeout: null); }
        finally { _gate.Release(); }
    }

    // ─── Send side ──────────────────────────────────────────────────────

    private void SendContent(ReadOnlyMemory<byte> content, CancellationToken ct)
    {
        var options = _options;

        int maxFrameLen = CanFrame.DlcToLength(options.MaxDlc);
        byte[] rented = ArrayPool<byte>.Shared.Rent(maxFrameLen);
        try
        {
            var buf = rented.AsSpan(0, maxFrameLen);
            int contentLen = content.Length;
            var span = content.Span;

            int sfOrFfLen = DoCanFraming.GetSuitableSfOrFfLength(contentLen, options.MinDlc, options.MaxDlc, out var needMore);

            if (!needMore)
            {
                // Single Frame
                int sfLen = sfOrFfLen;
                DoCanFraming.EncodeSingleFrame(span, buf[..sfLen], sfLen, options.PaddingValue);
                using var cts = new LinkedCts(options.TimeoutAs, ct);
                _send(MakeRequestFrameCopy(rented, sfLen), cts.Token);
                return;
            }

            // First Frame
            int ffLen = maxFrameLen;
            DoCanFraming.EncodeFirstFrame(span, buf, ffLen, out var consumed);
            using (var cts = new LinkedCts(options.TimeoutAs, ct))
                _send(MakeRequestFrameCopy(rented, ffLen), cts.Token);

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
                    fc = ReceiveMatchingFrame(cts.Token);
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
                        Sleep(options.FlowControlWaitInterval, ct);
                        continue;

                    case DoCanFlowStatus.Overflow:
                        throw new DoCanOverflowException();

                    case DoCanFlowStatus.Continue:
                        break;

                    default:
                        throw new ProtocolException($"Invalid flow control status: {status}.");
                }

                int maxBlock = bs == 0 ? int.MaxValue : bs;
                int sent = 0;
                var sTminDelay = DoCanFraming.STminToTimeSpan(st);

                while (sent < maxBlock && remaining > 0)
                {
                    if (options.TimeCs > TimeSpan.Zero)
                        Sleep(options.TimeCs, ct);

                    if (sent > 0 && sTminDelay > TimeSpan.Zero)
                        PreciseDelay(sTminDelay, ct);

                    // Pick frame length for this CF
                    int frameLen = DoCanTransmitterShared.GetConsecutiveFrameLength(options, ffLen, cfCapacity, remaining);

                    sn = DoCanFraming.NextSequenceNumber(sn);
                    int copy = Math.Min(remaining, frameLen - 1);
                    DoCanFraming.EncodeConsecutiveFrame(span.Slice(pos, copy), buf, sn, frameLen, options.PaddingValue);

                    using var cts = new LinkedCts(options.TimeoutAs, ct);
                    _send(MakeRequestFrameCopy(rented, frameLen), cts.Token);

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

    private ReadOnlyMemory<byte> ReceiveContent(
        CancellationToken responseStartCt,
        CancellationToken ct,
        TimeSpan? receiveStartTimeout)
    {
        var options = _options;

        CanFrame first;
        if (receiveStartTimeout.HasValue)
        {
            using var receiveStartTimeoutCts = new CancellationTokenSource(receiveStartTimeout.Value);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(receiveStartTimeoutCts.Token, responseStartCt, ct);
            first = ReceiveMatchingFrame(cts.Token);
        }
        else
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(responseStartCt, ct);
            first = ReceiveMatchingFrame(cts.Token);
        }

        var type = DoCanFraming.GetFrameType(first.Data.Span);
        switch (type)
        {
            case DoCanFrameType.SingleFrame:
                if (!DoCanFraming.TryReadSingleFrame(first.Data.Span, out var sfPayload))
                    throw new FrameFormatException("Received a malformed single frame.");
                return sfPayload.ToArray();

            case DoCanFrameType.FirstFrame:
                return ReceiveSegmented(first, ct);

            default:
                throw new FrameFormatException($"Expected SF or FF, got {type}.");
        }
    }

    private ReadOnlyMemory<byte> ReceiveSegmented(CanFrame firstFrame, CancellationToken ct)
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
                using (var cts = new LinkedCts(options.TimeoutAr, ct))
                    _send(MakeRequestFrame(fcRent.AsMemory(0, fcDlc)), cts.Token);
                throw new ProtocolException($"Segmented message length {totalLength} exceeds MaxSegmentedPayloadLength {options.MaxSegmentedPayloadLength}.");
            }

            var buffer = new byte[(int)totalLength];
            int copied = Math.Min(headPayload.Length, buffer.Length);
            headPayload[..copied].CopyTo(buffer);
            int pos = copied;

            byte expectedSn = 1;
            while (pos < buffer.Length)
            {
                if (options.TimeBr > TimeSpan.Zero)
                    Sleep(options.TimeBr, ct);

                DoCanFraming.EncodeFlowControlFrame(fcRent.AsSpan(0, fcDlc), DoCanFlowStatus.Continue, options.BlockSize, options.STmin, fcDlc, options.PaddingValue);
                using (var cts = new LinkedCts(options.TimeoutAr, ct))
                    _send(MakeRequestFrame(fcRent.AsMemory(0, fcDlc)), cts.Token);

                int block = 0;
                int maxBlock = options.BlockSize == 0 ? int.MaxValue : options.BlockSize;
                while (block < maxBlock && pos < buffer.Length)
                {
                    CanFrame cf;
                    try
                    {
                        using var cts = new LinkedCts(options.TimeoutCr, ct);
                        cf = ReceiveMatchingFrame(cts.Token);
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

    private CanFrame ReceiveMatchingFrame(CancellationToken ct)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var frame = _receive(ct);
            if (DoCanTransmitterShared.FrameMatches(frame, _options)) return frame;
        }
    }

    private static void Sleep(TimeSpan delay, CancellationToken ct)
    {
        if (delay <= TimeSpan.Zero) return;
        ct.WaitHandle.WaitOne(delay);
        ct.ThrowIfCancellationRequested();
    }

    private static void PreciseDelay(TimeSpan delay, CancellationToken ct)
    {
        if (delay <= TimeSpan.Zero) return;
        if (delay >= TimeSpan.FromMilliseconds(15))
        {
            Sleep(delay, ct);
            return;
        }
        var spinTail = TimeSpan.FromMilliseconds(1);
        var sw = Stopwatch.StartNew();
        if (delay > spinTail)
            Sleep(delay - spinTail, ct);
        while (sw.Elapsed < delay)
        {
            ct.ThrowIfCancellationRequested();
            Thread.SpinWait(10);
        }
    }
}
