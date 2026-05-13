using System;
using System.Collections.Generic;

using DiagKit.Uds.Exceptions;

namespace DiagKit.Uds.DoCan;

/// <summary>
/// 实现 ISO 15765-2 帧编码与解码的纯函数集合。无状态，可安全并发使用。<br/>
/// Pure functions that implement ISO 15765-2 frame encoding and decoding. Stateless; safe for concurrent use.
/// </summary>
/// <remarks>
/// 当前实现仅支持普通寻址。扩展寻址和混合寻址格式（包括地址扩展字节）不在这些辅助方法的编解码范围内。<br/>
/// This implementation currently supports normal addressing only. Extended and mixed addressing formats,
/// including address extension bytes, are not encoded or decoded by these helpers.
/// </remarks>
public static class DoCanFraming
{
    /// <summary>
    /// 默认汽车行业填充字节（0xCC）。<br/>Default automotive padding byte (0xCC).
    /// </summary>
    public const byte DefaultPadding = 0xCC;

    // ─── Frame type identification ──────────────────────────────────────

    /// <summary>
    /// 从 DoCAN PDU 的首字节提取帧类型（高 4 位）。<br/>Extract the frame type nibble from the first byte of a DoCAN PDU.
    /// </summary>
    /// <param name="frame">DoCAN PDU 数据。<br/>The DoCAN PDU data.</param>
    /// <returns>对应的 <see cref="DoCanFrameType"/> 枚举值；空帧返回 Reserved。<br/>The corresponding <see cref="DoCanFrameType"/>; returns Reserved for empty frames.</returns>
    public static DoCanFrameType GetFrameType(ReadOnlySpan<byte> frame)
    {
        if (frame.IsEmpty) return DoCanFrameType.Reserved;
        return (frame[0] >> 4) switch
        {
            0 => DoCanFrameType.SingleFrame,
            1 => DoCanFrameType.FirstFrame,
            2 => DoCanFrameType.ConsecutiveFrame,
            3 => DoCanFrameType.FlowControlFrame,
            _ => DoCanFrameType.Reserved,
        };
    }

    // ─── Single Frame ───────────────────────────────────────────────────

    /// <summary>
    /// 解码单帧（SF），返回指向输入数据中载荷字节的切片。<br/>Decode a Single Frame (SF). Returns a slice of the input pointing at the payload bytes.
    /// </summary>
    /// <param name="frame">单帧的原始数据。<br/>The raw single frame data.</param>
    /// <param name="payload">解码成功时指向载荷数据的切片；否则为 default。<br/>On success, a slice pointing at the payload bytes; otherwise default.</param>
    /// <returns>解码成功返回 true，否则返回 false。<br/>True if decoding succeeded; otherwise false.</returns>
    public static bool TryReadSingleFrame(ReadOnlySpan<byte> frame, out ReadOnlySpan<byte> payload)
    {
        payload = default;
        if (frame.IsEmpty) return false;
        if (GetFrameType(frame) != DoCanFrameType.SingleFrame) return false;

        int length;
        int offset;
        if (frame.Length <= 8)
        {
            length = frame[0] & 0x0F;
            offset = 1;
            if (length == 0) return false;
            if (length > 7) return false;
        }
        else
        {
            if (frame[0] != 0) return false;
            if (frame.Length < 2) return false;
            length = frame[1];
            if (length < 8) return false;
            if (length > 62) return false;
            if (length > frame.Length - 2) return false;
            offset = 2;
        }
        if (frame.Length < offset + length) return false;
        payload = frame.Slice(offset, length);
        return true;
    }

    /// <summary>
    /// 根据给定载荷构建单帧（SF），返回新分配的字节数组。<br/>Build a Single Frame (SF) from the given payload, returning a new byte array.
    /// </summary>
    /// <param name="payload">载荷数据。<br/>The payload data.</param>
    /// <param name="dlcLength">帧的总长度（DLC 对应的字节长度）。<br/>Total frame length (byte length corresponding to the DLC).</param>
    /// <param name="padding">填充字节（默认 0xCC）。<br/>Padding byte (default 0xCC).</param>
    /// <returns>编码后的单帧字节数组。<br/>The encoded single frame as a byte array.</returns>
    public static byte[] EncodeSingleFrame(ReadOnlySpan<byte> payload, int dlcLength, byte padding = DefaultPadding)
    {
        var buf = new byte[dlcLength];
        EncodeSingleFrame(payload, buf, dlcLength, padding);
        return buf;
    }

    /// <summary>
    /// 将单帧（SF）编码写入调用方提供的目的缓冲区。<br/>Encode a Single Frame (SF) into the caller-provided destination buffer.
    /// </summary>
    /// <param name="payload">载荷数据。<br/>The payload data.</param>
    /// <param name="destination">目的缓冲区。<br/>The destination buffer.</param>
    /// <param name="dlcLength">帧的总长度（DLC 对应的字节长度）。<br/>Total frame length (byte length corresponding to the DLC).</param>
    /// <param name="padding">填充字节（默认 0xCC）。<br/>Padding byte (default 0xCC).</param>
    public static void EncodeSingleFrame(ReadOnlySpan<byte> payload, Span<byte> destination, int dlcLength, byte padding = DefaultPadding)
    {
        if (dlcLength < 8) throw new ArgumentException("SF DLC length must be ≥ 8.");
        EnsureDestinationLength(destination, dlcLength);
        int offset;
        if (payload.Length <= 7)
        {
            if (dlcLength > 8)
                throw new ArgumentException("Normal-addressing SF payloads <= 7 bytes must use CAN_DL = 8.", nameof(dlcLength));
            offset = 1;
            if (payload.Length > dlcLength - offset)
                throw new ArgumentException("Payload exceeds frame capacity.", nameof(payload));
            destination.Clear();
            destination[0] = (byte)payload.Length;
            payload.CopyTo(destination[offset..]);
            for (var i = offset + payload.Length; i < dlcLength; i++) destination[i] = padding;
        }
        else if (payload.Length <= 62)
        {
            offset = 2;
            if (payload.Length > dlcLength - offset)
                throw new ArgumentException("Payload exceeds SF frame capacity.", nameof(payload));
            destination.Clear();
            destination[0] = 0;
            destination[1] = (byte)payload.Length;
            payload.CopyTo(destination[offset..]);
            for (var i = offset + payload.Length; i < dlcLength; i++) destination[i] = padding;
        }
        else
        {
            throw new ArgumentException("Payload too large for single frame (>62 bytes).", nameof(payload));
        }
    }

    // ─── First Frame ────────────────────────────────────────────────────

    /// <summary>
    /// 解码首帧（FF）。输出声明的消息总长度和本帧中包含的字节。<br/>Decode a First Frame (FF). Outputs the declared total message length and the bytes in this frame.
    /// </summary>
    /// <param name="frame">首帧的原始数据。<br/>The raw first frame data.</param>
    /// <param name="totalLength">解码成功时为声明的消息总长度；否则为 0。<br/>On success, the declared total message length; otherwise 0.</param>
    /// <param name="headPayload">解码成功时为本帧包含的载荷切片；否则为 default。<br/>On success, a slice of the payload bytes in this frame; otherwise default.</param>
    /// <returns>解码成功返回 true，否则返回 false。<br/>True if decoding succeeded; otherwise false.</returns>
    public static bool TryReadFirstFrame(ReadOnlySpan<byte> frame, out uint totalLength, out ReadOnlySpan<byte> headPayload)
    {
        totalLength = 0;
        headPayload = default;
        if (frame.IsEmpty) return false;
        if (GetFrameType(frame) != DoCanFrameType.FirstFrame) return false;
        if (frame.Length < 8) return false;

        int offset;
        if (frame[0] == 0x10 && frame[1] == 0x00)
        {
            if (frame.Length < 6) return false;
            totalLength = ((uint)frame[2] << 24) | ((uint)frame[3] << 16) | ((uint)frame[4] << 8) | frame[5];
            if (totalLength <= 0xFFF) return false; // must use long form only when > 4095
            offset = 6;
        }
        else
        {
            totalLength = (uint)(((frame[0] & 0x0F) << 8) | frame[1]);
            if (totalLength < 8) return false; // FF only used when > 7 bytes
            offset = 2;
        }

        headPayload = frame[offset..];
        return true;
    }

    /// <summary>
    /// 根据给定载荷构建首帧（FF），返回新分配的字节数组。<br/>Build a First Frame (FF) from the given payload, returning a new byte array.
    /// </summary>
    /// <param name="payload">载荷数据。<br/>The payload data.</param>
    /// <param name="dlcLength">帧的总长度（DLC 对应的字节长度）。<br/>Total frame length (byte length corresponding to the DLC).</param>
    /// <param name="consumed">放入本帧的字节数。<br/>The number of bytes placed into this frame.</param>
    /// <returns>编码后的首帧字节数组。<br/>The encoded first frame as a byte array.</returns>
    public static byte[] EncodeFirstFrame(ReadOnlySpan<byte> payload, int dlcLength, out int consumed)
    {
        var buf = new byte[dlcLength];
        EncodeFirstFrame(payload, buf, dlcLength, out consumed);
        return buf;
    }

    /// <summary>
    /// 将首帧（FF）编码写入调用方提供的目的缓冲区。<br/>Encode a First Frame (FF) into the caller-provided destination buffer.
    /// </summary>
    /// <param name="payload">载荷数据。<br/>The payload data.</param>
    /// <param name="destination">目的缓冲区。<br/>The destination buffer.</param>
    /// <param name="dlcLength">帧的总长度（DLC 对应的字节长度）。<br/>Total frame length (byte length corresponding to the DLC).</param>
    /// <param name="consumed">放入本帧的字节数。<br/>The number of bytes placed into this frame.</param>
    public static void EncodeFirstFrame(ReadOnlySpan<byte> payload, Span<byte> destination, int dlcLength, out int consumed)
    {
        if (dlcLength < 8) throw new ArgumentException("FF DLC length must be ≥ 8.");
        EnsureDestinationLength(destination, dlcLength);
        int offset;
        destination.Clear();
        if (payload.Length <= 0xFFF)
        {
            offset = 2;
            destination[0] = (byte)(0x10 | ((payload.Length >> 8) & 0x0F));
            destination[1] = (byte)(payload.Length & 0xFF);
        }
        else
        {
            offset = 6;
            destination[0] = 0x10;
            destination[1] = 0x00;
            var len = (uint)payload.Length;
            destination[2] = (byte)((len >> 24) & 0xFF);
            destination[3] = (byte)((len >> 16) & 0xFF);
            destination[4] = (byte)((len >> 8) & 0xFF);
            destination[5] = (byte)(len & 0xFF);
        }
        consumed = Math.Min(dlcLength - offset, payload.Length);
        payload[..consumed].CopyTo(destination[offset..]);
    }

    // ─── Consecutive Frame ──────────────────────────────────────────────

    /// <summary>
    /// 解码连续帧（CF），输出序列号和载荷数据。<br/>Decode a Consecutive Frame (CF), returning the sequence number and payload.
    /// </summary>
    /// <param name="frame">连续帧的原始数据。<br/>The raw consecutive frame data.</param>
    /// <param name="sequenceNumber">解码成功时为序列号（0-15）；否则为 0。<br/>On success, the sequence number (0-15); otherwise 0.</param>
    /// <param name="payload">解码成功时为本帧包含的载荷切片；否则为 default。<br/>On success, a slice of the payload bytes in this frame; otherwise default.</param>
    /// <returns>解码成功返回 true，否则返回 false。<br/>True if decoding succeeded; otherwise false.</returns>
    public static bool TryReadConsecutiveFrame(ReadOnlySpan<byte> frame, out byte sequenceNumber, out ReadOnlySpan<byte> payload)
    {
        sequenceNumber = 0;
        payload = default;
        if (frame.Length < 2) return false;
        if (GetFrameType(frame) != DoCanFrameType.ConsecutiveFrame) return false;
        sequenceNumber = (byte)(frame[0] & 0x0F);
        payload = frame[1..];
        return true;
    }

    /// <summary>
    /// 为指定载荷构建连续帧（CF），返回新分配的字节数组。<br/>Build a Consecutive Frame (CF) for the given payload, returning a new byte array.
    /// </summary>
    /// <param name="payload">载荷数据。<br/>The payload data.</param>
    /// <param name="sequenceNumber">序列号（0-15）。<br/>Sequence number (0-15).</param>
    /// <param name="dlcLength">帧的总长度（DLC 对应的字节长度）。<br/>Total frame length (byte length corresponding to the DLC).</param>
    /// <param name="padding">填充字节（默认 0xCC）。<br/>Padding byte (default 0xCC).</param>
    /// <returns>编码后的连续帧字节数组。<br/>The encoded consecutive frame as a byte array.</returns>
    public static byte[] EncodeConsecutiveFrame(ReadOnlySpan<byte> payload, byte sequenceNumber, int dlcLength, byte padding = DefaultPadding)
    {
        var buf = new byte[dlcLength];
        EncodeConsecutiveFrame(payload, buf, sequenceNumber, dlcLength, padding);
        return buf;
    }

    /// <summary>
    /// 将连续帧（CF）编码写入调用方提供的目的缓冲区。<br/>Encode a Consecutive Frame (CF) into the caller-provided destination buffer.
    /// </summary>
    /// <param name="payload">载荷数据。<br/>The payload data.</param>
    /// <param name="destination">目的缓冲区。<br/>The destination buffer.</param>
    /// <param name="sequenceNumber">序列号（0-15）。<br/>Sequence number (0-15).</param>
    /// <param name="dlcLength">帧的总长度（DLC 对应的字节长度）。<br/>Total frame length (byte length corresponding to the DLC).</param>
    /// <param name="padding">填充字节（默认 0xCC）。<br/>Padding byte (default 0xCC).</param>
    public static void EncodeConsecutiveFrame(ReadOnlySpan<byte> payload, Span<byte> destination, byte sequenceNumber, int dlcLength, byte padding = DefaultPadding)
    {
        if (dlcLength < 2) throw new ArgumentException("CF DLC length must be ≥ 2.");
        EnsureDestinationLength(destination, dlcLength);
        destination.Clear();
        destination[0] = (byte)(0x20 | (sequenceNumber & 0x0F));
        int copy = Math.Min(dlcLength - 1, payload.Length);
        payload[..copy].CopyTo(destination[1..]);
        for (var i = 1 + copy; i < dlcLength; i++) destination[i] = padding;
    }

    /// <summary>
    /// 计算下一个 ISO 15765 序列号（SN 在 0..15 范围内循环）。<br/>Next ISO 15765 sequence number (SN cycles 0..15).
    /// </summary>
    /// <param name="current">当前序列号。<br/>The current sequence number.</param>
    /// <returns>下一个序列号（(current + 1) &amp; 0x0F）。<br/>The next sequence number ((current + 1) &amp; 0x0F).</returns>
    public static byte NextSequenceNumber(byte current) => (byte)((current + 1) & 0x0F);

    // ─── Flow Control Frame ─────────────────────────────────────────────

    /// <summary>
    /// 解码流控帧（FC），输出流状态、块大小和 STmin。<br/>Decode a Flow Control (FC) frame, returning flow status, block size, and STmin.
    /// </summary>
    /// <param name="frame">流控帧的原始数据。<br/>The raw flow control frame data.</param>
    /// <param name="status">解码成功时为流状态码；否则为 Reserved。<br/>On success, the flow status code; otherwise Reserved.</param>
    /// <param name="blockSize">解码成功时为块大小（0 = 无限制）；否则为 0。<br/>On success, the block size (0 = unlimited); otherwise 0.</param>
    /// <param name="sTmin">解码成功时为最小间隔时间值；否则为 0。<br/>On success, the STmin value; otherwise 0.</param>
    /// <returns>解码成功返回 true，否则返回 false。<br/>True if decoding succeeded; otherwise false.</returns>
    public static bool TryReadFlowControlFrame(ReadOnlySpan<byte> frame, out DoCanFlowStatus status, out byte blockSize, out byte sTmin)
    {
        status = DoCanFlowStatus.Reserved;
        blockSize = 0;
        sTmin = 0;
        if (frame.Length < 3) return false;
        if (GetFrameType(frame) != DoCanFrameType.FlowControlFrame) return false;
        var fs = frame[0] & 0x0F;
        status = fs switch
        {
            0 => DoCanFlowStatus.Continue,
            1 => DoCanFlowStatus.Wait,
            2 => DoCanFlowStatus.Overflow,
            _ => DoCanFlowStatus.Reserved,
        };
        blockSize = frame[1];
        sTmin = frame[2];
        return true;
    }

    /// <summary>
    /// 构建流控帧（FC），返回新分配的字节数组。<br/>Build a Flow Control (FC) frame, returning a new byte array.
    /// </summary>
    /// <param name="status">流状态码。<br/>The flow status code.</param>
    /// <param name="blockSize">块大小（0 = 无限制）。<br/>Block size (0 = unlimited).</param>
    /// <param name="sTmin">最小间隔时间值。<br/>The STmin value.</param>
    /// <param name="dlcLength">帧的总长度（DLC 对应的字节长度，默认 8）。<br/>Total frame length (byte length corresponding to the DLC, default 8).</param>
    /// <param name="padding">填充字节（默认 0xCC）。<br/>Padding byte (default 0xCC).</param>
    /// <returns>编码后的流控帧字节数组。<br/>The encoded flow control frame as a byte array.</returns>
    public static byte[] EncodeFlowControlFrame(DoCanFlowStatus status, byte blockSize, byte sTmin, int dlcLength = 8, byte padding = DefaultPadding)
    {
        var buf = new byte[dlcLength];
        EncodeFlowControlFrame(buf, status, blockSize, sTmin, dlcLength, padding);
        return buf;
    }

    /// <summary>
    /// 将流控帧（FC）编码写入调用方提供的目的缓冲区。<br/>Encode a Flow Control (FC) frame into the caller-provided destination buffer.
    /// </summary>
    /// <param name="destination">目的缓冲区。<br/>The destination buffer.</param>
    /// <param name="status">流状态码。<br/>The flow status code.</param>
    /// <param name="blockSize">块大小（0 = 无限制）。<br/>Block size (0 = unlimited).</param>
    /// <param name="sTmin">最小间隔时间值。<br/>The STmin value.</param>
    /// <param name="dlcLength">帧的总长度（DLC 对应的字节长度，默认 8）。<br/>Total frame length (byte length corresponding to the DLC, default 8).</param>
    /// <param name="padding">填充字节（默认 0xCC）。<br/>Padding byte (default 0xCC).</param>
    public static void EncodeFlowControlFrame(Span<byte> destination, DoCanFlowStatus status, byte blockSize, byte sTmin, int dlcLength = 8, byte padding = DefaultPadding)
    {
        if (dlcLength < 3) throw new ArgumentException("FC DLC length must be ≥ 3.");
        EnsureDestinationLength(destination, dlcLength);
        destination.Clear();
        destination[0] = (byte)(0x30 | ((byte)status & 0x0F));
        destination[1] = blockSize;
        destination[2] = sTmin;
        for (var i = 3; i < dlcLength; i++) destination[i] = padding;
    }

    // ─── STmin encoding ─────────────────────────────────────────────────

    /// <summary>
    /// 将 STmin 字节值转换为 <see cref="TimeSpan"/>。<br/>Convert an STmin byte value into a <see cref="TimeSpan"/>.
    /// </summary>
    /// <param name="sTmin">STmin 字节值（0x00-0x7F 为毫秒，0xF1-0xF9 为 100 微秒单位）。<br/>The STmin byte value (0x00-0x7F in milliseconds, 0xF1-0xF9 in 100-microsecond units).</param>
    /// <returns>对应的时间间隔。<br/>The corresponding time interval.</returns>
    public static TimeSpan STminToTimeSpan(byte sTmin)
    {
        if (sTmin <= 0x7F) return TimeSpan.FromMilliseconds(sTmin);
        if (sTmin >= 0xF1 && sTmin <= 0xF9) return TimeSpan.FromMicroseconds((sTmin - 0xF0) * 100);
        // Reserved per ISO 15765 — treat as max permitted delay of 127 ms.
        return TimeSpan.FromMilliseconds(127);
    }

    /// <summary>
    /// 将 <see cref="TimeSpan"/> 转换为其 STmin 字节编码。<br/>Convert a <see cref="TimeSpan"/> to its STmin byte encoding.
    /// </summary>
    /// <param name="t">时间间隔（100 微秒到 127 毫秒）。<br/>Time interval (100 µs to 127 ms).</param>
    /// <returns>STmin 字节值。<br/>The STmin byte value.</returns>
    /// <exception cref="ArgumentOutOfRangeException">时间间隔超出 STmin 有效范围。Time interval is outside the valid STmin range.</exception>
    public static byte TimeSpanToSTmin(TimeSpan t)
    {
        if (t <= TimeSpan.Zero) return 0;
        if (t.TotalMilliseconds >= 1 && t.TotalMilliseconds <= 127) return (byte)t.TotalMilliseconds;
        if (t.TotalMicroseconds >= 100 && t.TotalMicroseconds <= 900)
            return (byte)(0xF0 + t.TotalMicroseconds / 100);
        throw new ArgumentOutOfRangeException(nameof(t), "STmin range is 100 µs .. 127 ms.");
    }

    // ─── Full segmentation ──────────────────────────────────────────────

    /// <summary>
    /// 计算为 <paramref name="contentLength"/> 字节的消息编码 SF 或 FF 时应使用的 DLC 长度。<br/>
    /// Compute the DLC length to use when encoding a SF or FF for a message of <paramref name="contentLength"/> bytes.
    /// </summary>
    /// <param name="contentLength">消息内容的总字节数。<br/>Total number of bytes in the message content.</param>
    /// <param name="minDlc">允许的最小 DLC 值。<br/>Minimum allowed DLC value.</param>
    /// <param name="maxDlc">允许的最大 DLC 值。<br/>Maximum allowed DLC value.</param>
    /// <param name="requiresMoreFrames">若消息需要分段传输（需要 FF + CF）则输出 true。<br/>Outputs true if the message requires segmentation (FF + CF).</param>
    /// <returns>推荐用于 SF 或 FF 的帧字节长度。<br/>The recommended frame byte length for the SF or FF.</returns>
    public static int GetSuitableSfOrFfLength(int contentLength, byte minDlc, byte maxDlc, out bool requiresMoreFrames)
    {
        if (minDlc > maxDlc)
            throw new ArgumentException($"minDlc ({minDlc}) must be less than or equal to maxDlc ({maxDlc}).");
        int maxLen = CanFrame.DlcToLength(maxDlc);

        if (contentLength <= 7)
        {
            // CAN_DL > 8 requires the SF escape sequence, whose SF_DL range starts at 8.
            requiresMoreFrames = false;
            return 8;
        }
        if (contentLength <= 62)
        {
            for (var d = minDlc; d <= maxDlc; d++)
            {
                int L = CanFrame.DlcToLength(d);
                if (contentLength <= L - 2)
                {
                    requiresMoreFrames = false;
                    return L;
                }
            }
        }
        requiresMoreFrames = true;
        return maxLen;
    }

    /// <summary>
    /// 按照 ISO 15765-2 标准枚举传输 <paramref name="content"/> 需要的所有帧。<br/>
    /// Enumerate the frames required to transmit <paramref name="content"/> according to ISO 15765-2.
    /// </summary>
    /// <param name="content">消息内容。<br/>The message content.</param>
    /// <param name="minDlc">允许的最小 DLC 值。<br/>Minimum allowed DLC value.</param>
    /// <param name="maxDlc">允许的最大 DLC 值。<br/>Maximum allowed DLC value.</param>
    /// <param name="padding">填充字节（默认 0xCC）。<br/>Padding byte (default 0xCC).</param>
    /// <param name="fixedDlc">若为 true，所有帧使用相同 DLC；否则最后一帧可缩小。<br/>If true, all frames use the same DLC; otherwise the last frame may shrink.</param>
    /// <returns>编码后的帧字节数组的枚举序列。<br/>An enumerable sequence of encoded frame byte arrays.</returns>
    public static IEnumerable<byte[]> Segment(ReadOnlyMemory<byte> content, byte minDlc, byte maxDlc, byte padding = DefaultPadding, bool fixedDlc = false)
    {
        if (content.IsEmpty) yield break;

        int sfLen = GetSuitableSfOrFfLength(content.Length, minDlc, maxDlc, out var needMore);
        if (!needMore)
        {
            yield return EncodeSingleFrame(content.Span, sfLen, padding);
            yield break;
        }

        // FF + CF
        int ffLen = CanFrame.DlcToLength(maxDlc);
        var firstFrame = EncodeFirstFrame(content.Span, ffLen, out var consumed);
        yield return firstFrame;

        int remaining = content.Length - consumed;
        int pos = consumed;
        byte sn = 0;

        int cfCapacity = ffLen - 1;
        while (remaining > 0)
        {
            sn = NextSequenceNumber(sn);
            int frameLen = ffLen;
            if (!fixedDlc && remaining < cfCapacity)
            {
                int minRequired = 1 + remaining; // sn + data
                // pick a smaller DLC if possible
                for (var d = minDlc; d <= maxDlc; d++)
                {
                    int L = CanFrame.DlcToLength(d);
                    if (L >= minRequired) { frameLen = L; break; }
                }
            }
            var cf = EncodeConsecutiveFrame(content.Span.Slice(pos, Math.Min(remaining, frameLen - 1)), sn, frameLen, padding);
            yield return cf;
            int copied = Math.Min(remaining, frameLen - 1);
            pos += copied;
            remaining -= copied;
        }
    }

    private static void EnsureDestinationLength(Span<byte> destination, int dlcLength)
    {
        if (destination.Length < dlcLength)
            throw new ArgumentException("Destination too small.", nameof(destination));
    }
}
