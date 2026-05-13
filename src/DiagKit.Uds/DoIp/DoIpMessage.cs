using System;
using System.Buffers.Binary;

using DiagKit.Uds.Exceptions;

namespace DiagKit.Uds.DoIp;

/// <summary>
/// 单个 DoIP 消息，由 8 字节头部和类型化载荷组成。<br/>A single DoIP message consisting of an 8-byte header and a typed payload.
/// </summary>
public readonly record struct DoIpMessage
{
    /// <summary>DoIP 协议版本（ISO 13400-2 §5.5）。<br/>The DoIP protocol version (ISO 13400-2 §5.5).</summary>
    public const byte ProtocolVersion = 0x02;

    /// <summary>DoIP 头部大小（字节数）。<br/>Size of the DoIP header in bytes.</summary>
    public const int HeaderSize = 8;

    /// <summary>独立解析时默认的最大可接受载荷长度（4 MiB）。<br/>Default maximum accepted payload length for standalone parsing (4 MiB).</summary>
    public const int DefaultMaxPayloadLength = 4 * 1024 * 1024;

    /// <summary>载荷类型（ISO 13400-2 §5.5.1.4）。<br/>Payload type (ISO 13400-2 §5.5.1.4).</summary>
    public DoIpPayloadType PayloadType { get; init; }

    /// <summary>载荷字节。<br/>Payload bytes.</summary>
    public ReadOnlyMemory<byte> Payload { get; init; }

    /// <summary>构造一个 DoIP 消息。<br/>Construct a message.</summary>
    /// <param name="payloadType">载荷类型。<br/>The payload type.</param>
    /// <param name="payload">载荷字节。<br/>The payload bytes.</param>
    public DoIpMessage(DoIpPayloadType payloadType, ReadOnlyMemory<byte> payload)
    {
        PayloadType = payloadType;
        Payload = payload;
    }

    /// <summary>将完整消息（头部 + 载荷）序列化到新分配的缓冲区。<br/>Serialize the full message (header + payload) into a newly-allocated buffer.</summary>
    /// <returns>包含完整 DoIP 线格式的字节数组。<br/>A byte array containing the full DoIP wire format.</returns>
    public byte[] ToBytes()
    {
        var buf = new byte[HeaderSize + Payload.Length];
        Encode(buf);
        return buf;
    }

    /// <summary>将完整消息序列化到指定的目标缓冲区。<br/>Serialize the full message into the given destination.</summary>
    /// <param name="destination">目标缓冲区，必须足够大以容纳头部和载荷。<br/>The destination buffer, must be large enough for header plus payload.</param>
    public void Encode(Span<byte> destination)
    {
        if (destination.Length < HeaderSize + Payload.Length)
            throw new ArgumentException("Destination too small.", nameof(destination));
        destination[0] = ProtocolVersion;
        destination[1] = unchecked((byte)~ProtocolVersion);
        BinaryPrimitives.WriteUInt16BigEndian(destination.Slice(2, 2), (ushort)PayloadType);
        BinaryPrimitives.WriteUInt32BigEndian(destination.Slice(4, 4), (uint)Payload.Length);
        Payload.Span.CopyTo(destination[HeaderSize..]);
    }

    /// <summary>
    /// 尝试从 <paramref name="buffer"/> 解析 DoIP 消息头部。
    /// 成功时通过 <paramref name="payloadLength"/> 返回声明的载荷长度。<br/>
    /// Try to parse a DoIP message header from <paramref name="buffer"/>. On success, the
    /// declared payload length is returned via <paramref name="payloadLength"/>.
    /// </summary>
    /// <param name="buffer">包含 DoIP 头部字节的缓冲区。<br/>Buffer containing the DoIP header bytes.</param>
    /// <param name="type">解析出的载荷类型。<br/>The parsed payload type.</param>
    /// <param name="payloadLength">声明的载荷长度。<br/>The declared payload length.</param>
    /// <returns>解析成功返回 true，否则返回 false。<br/>true if parsing succeeded; otherwise false.</returns>
    public static bool TryParseHeader(ReadOnlySpan<byte> buffer, out DoIpPayloadType type, out uint payloadLength)
    {
        type = default;
        payloadLength = 0;
        if (buffer.Length < HeaderSize) return false;
        if (buffer[0] != ProtocolVersion) return false;
        if (buffer[1] != unchecked((byte)~ProtocolVersion)) return false;
        type = (DoIpPayloadType)BinaryPrimitives.ReadUInt16BigEndian(buffer.Slice(2, 2));
        payloadLength = BinaryPrimitives.ReadUInt32BigEndian(buffer.Slice(4, 4));
        return true;
    }

    /// <summary>
    /// 从 <paramref name="buffer"/> 解析完整的 DoIP 消息。<br/>
    /// Parse a full DoIP message from <paramref name="buffer"/>.
    /// </summary>
    /// <param name="buffer">包含完整 DoIP 消息的缓冲区。<br/>Buffer containing the full DoIP message.</param>
    /// <returns>解析出的 DoIP 消息。<br/>The parsed DoIP message.</returns>
    public static DoIpMessage Parse(ReadOnlyMemory<byte> buffer) => Parse(buffer, DefaultMaxPayloadLength);

    /// <summary>
    /// 从 <paramref name="buffer"/> 解析完整的 DoIP 消息，
    /// 拒绝声明长度超过 <paramref name="maxPayloadLength"/> 的载荷。<br/>
    /// Parse a full DoIP message from <paramref name="buffer"/>, rejecting payloads
    /// whose declared length exceeds <paramref name="maxPayloadLength"/>.
    /// </summary>
    /// <param name="buffer">包含完整 DoIP 消息的缓冲区。<br/>Buffer containing the full DoIP message.</param>
    /// <param name="maxPayloadLength">最大可接受的载荷长度。<br/>Maximum accepted payload length.</param>
    /// <returns>解析出的 DoIP 消息。<br/>The parsed DoIP message.</returns>
    public static DoIpMessage Parse(ReadOnlyMemory<byte> buffer, int maxPayloadLength)
    {
        if (maxPayloadLength <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxPayloadLength), maxPayloadLength, "Max payload length must be greater than zero.");
        if (!TryParseHeader(buffer.Span, out var type, out var len))
            throw new FrameFormatException("Invalid DoIP header.");
        if (len > (uint)maxPayloadLength)
            throw new ProtocolException($"DoIP payload length {len} exceeds MaxPayloadLength {maxPayloadLength}.");

        int payloadLength = (int)len;
        if (buffer.Length - HeaderSize < payloadLength)
            throw new FrameFormatException("Truncated DoIP payload.");
        return new DoIpMessage(type, buffer.Slice(HeaderSize, payloadLength));
    }

    /// <summary>
    /// 直接将诊断消息（0x8001）编码写入 <paramref name="destination"/>，
    /// 生成完整的线格式缓冲区（头部 + 源/目标地址 + UDS 载荷）。
    /// 避免中间载荷分配。<br/>
    /// Encode a diagnostic message (0x8001) directly into <paramref name="destination"/>,
    /// writing the full wire-format buffer (header + source/target addresses + UDS payload).
    /// Avoids intermediate payload allocations.
    /// </summary>
    /// <param name="destination">必须至少为 <c>HeaderSize + 4 + uds.Length</c> 字节。<br/>Must be at least <c>HeaderSize + 4 + uds.Length</c> bytes.</param>
    /// <param name="uds">原始 UDS 请求字节。<br/>The raw UDS request bytes.</param>
    /// <param name="sourceAddress">DoIP 源地址（大端序）。<br/>DoIP source address (big-endian).</param>
    /// <param name="targetAddress">DoIP 目标地址（大端序）。<br/>DoIP target address (big-endian).</param>
    public static void EncodeDiagnosticMessage(Span<byte> destination, ReadOnlySpan<byte> uds, ushort sourceAddress, ushort targetAddress)
    {
        if (destination.Length < HeaderSize + 4 + uds.Length)
            throw new ArgumentException("Destination too small.", nameof(destination));

        destination[0] = ProtocolVersion;
        destination[1] = unchecked((byte)~ProtocolVersion);
        BinaryPrimitives.WriteUInt16BigEndian(destination.Slice(2, 2), (ushort)DoIpPayloadType.DiagnosticMessage);
        BinaryPrimitives.WriteUInt32BigEndian(destination.Slice(4, 4), (uint)(4 + uds.Length));
        BinaryPrimitives.WriteUInt16BigEndian(destination.Slice(8, 2), sourceAddress);
        BinaryPrimitives.WriteUInt16BigEndian(destination.Slice(10, 2), targetAddress);
        uds.CopyTo(destination[12..]);
    }
}
