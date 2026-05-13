using System;
using System.Buffers.Binary;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using DiagKit.Uds.Exceptions;

namespace DiagKit.Uds.DoIp;

/// <summary>
/// 用于 ISO 13400-2:2012 TCP_DATA 帧的异步 DoIP 流编解码器。<br/>Async DoIP stream codec for ISO 13400-2:2012 TCP_DATA framing.
/// </summary>
/// <remarks>
/// 每条消息以 8 字节通用头部开始读取，随后读取确切的声明载荷长度。
/// 此类型也可用于已完成认证的 <see cref="System.Net.Security.SslStream"/>，
/// 因为 TLS 流保持相同的字节流契约；ISO 13400-2:2025 的 TLS 安全诊断
/// 通信细节在申明合规前仍需标准审查。<br/>
/// Each message is read as an 8-byte generic header followed by exactly the
/// declared payload length. This type also works over an already-authenticated
/// <see cref="System.Net.Security.SslStream"/> because TLS streams preserve the
/// same byte-stream contract; TLS-specific ISO 13400-2:2025 secured diagnostic
/// communication details still need standards review before claiming compliance.
/// </remarks>
public static class DoIpStreamCodec
{
    /// <summary>从 <paramref name="stream"/> 读取一条完整的 DoIP 线格式消息。<br/>Read one complete DoIP wire-format message from <paramref name="stream"/>.</summary>
    /// <param name="stream">要读取的流。<br/>The stream to read from.</param>
    /// <param name="maxPayloadLength">最大可接受的载荷长度。<br/>Maximum accepted payload length.</param>
    /// <param name="cancellationToken">取消令牌。<br/>Cancellation token.</param>
    /// <returns>读取到的 DoIP 消息。<br/>The read DoIP message.</returns>
    public static async Task<DoIpMessage> ReadMessageAsync(
        Stream stream,
        int maxPayloadLength,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (maxPayloadLength <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxPayloadLength), maxPayloadLength, "Max payload length must be greater than zero.");

        var header = new byte[DoIpMessage.HeaderSize];
        await ReadExactAsync(stream, header, cancellationToken).ConfigureAwait(false);
        if (!DoIpMessage.TryParseHeader(header, out var type, out var payloadLength))
            throw new FrameFormatException("Invalid DoIP header.");
        if (payloadLength > (uint)maxPayloadLength)
            throw new ProtocolException($"DoIP payload length {payloadLength} exceeds MaxPayloadLength {maxPayloadLength}.");

        var payload = new byte[(int)payloadLength];
        if (payload.Length > 0)
            await ReadExactAsync(stream, payload, cancellationToken).ConfigureAwait(false);
        return new DoIpMessage(type, payload);
    }

    /// <summary>向 <paramref name="stream"/> 写入一条完整的 DoIP 线格式消息。<br/>Write one complete DoIP wire-format message to <paramref name="stream"/>.</summary>
    /// <param name="stream">要写入的流。<br/>The stream to write to.</param>
    /// <param name="message">要写入的 DoIP 消息。<br/>The DoIP message to write.</param>
    /// <param name="maxPayloadLength">最大可接受的载荷长度。<br/>Maximum accepted payload length.</param>
    /// <param name="cancellationToken">取消令牌。<br/>Cancellation token.</param>
    public static async Task WriteMessageAsync(
        Stream stream,
        DoIpMessage message,
        int maxPayloadLength,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (maxPayloadLength <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxPayloadLength), maxPayloadLength, "Max payload length must be greater than zero.");
        if (message.Payload.Length > maxPayloadLength)
            throw new ProtocolException($"DoIP payload length {message.Payload.Length} exceeds MaxPayloadLength {maxPayloadLength}.");

        var header = new byte[DoIpMessage.HeaderSize];
        header[0] = DoIpMessage.ProtocolVersion;
        header[1] = unchecked((byte)~DoIpMessage.ProtocolVersion);
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(2, 2), (ushort)message.PayloadType);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4, 4), (uint)message.Payload.Length);

        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        if (message.Payload.Length > 0)
            await stream.WriteAsync(message.Payload, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task ReadExactAsync(Stream stream, Memory<byte> destination, CancellationToken cancellationToken)
    {
        while (!destination.IsEmpty)
        {
            var read = await stream.ReadAsync(destination, cancellationToken).ConfigureAwait(false);
            if (read == 0)
                throw new EndOfStreamException("Stream ended before a complete DoIP message was received.");
            destination = destination[read..];
        }
    }
}
