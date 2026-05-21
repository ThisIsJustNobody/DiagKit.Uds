using System;
using System.Threading;
using System.Threading.Tasks;

using DiagKit.Uds.Contracts;
using DiagKit.Uds.Exceptions;
using DiagKit.Uds.UdsLayer;

namespace DiagKit.Uds.Services;

/// <summary>
/// SID 0x34（RequestDownload/请求下载）辅助方法。<br/>Helpers for SID 0x34 RequestDownload.
/// </summary>
public static class RequestDownload
{
    /// <summary>
    /// RequestDownload 肯定响应的解析结果。<br/>Parsed positive RequestDownload response.
    /// </summary>
    public readonly record struct Response(ulong MaxNumberOfBlockLength, int MaxTransferDataPayloadLength);

    /// <summary>
    /// 构建 RequestDownload 请求。<br/>Build a RequestDownload request.
    /// </summary>
    /// <param name="dataFormatIdentifier">数据格式标识符。<br/>Data format identifier.</param>
    /// <param name="memoryAddress">内存地址。<br/>Memory address.</param>
    /// <param name="memorySize">内存大小。<br/>Memory size.</param>
    /// <param name="memoryAddressLength">地址字节数（1..8）。<br/>Address byte length (1..8).</param>
    /// <param name="memorySizeLength">大小字节数（1..8）。<br/>Size byte length (1..8).</param>
    /// <returns>请求字节数组。<br/>The request byte array.</returns>
    public static byte[] BuildRequest(
        byte dataFormatIdentifier,
        ulong memoryAddress,
        ulong memorySize,
        byte memoryAddressLength,
        byte memorySizeLength)
    {
        ValidateLength(memoryAddressLength, nameof(memoryAddressLength));
        ValidateLength(memorySizeLength, nameof(memorySizeLength));
        ValidateFits(memoryAddress, memoryAddressLength, nameof(memoryAddress));
        ValidateFits(memorySize, memorySizeLength, nameof(memorySize));

        var buf = new byte[3 + memoryAddressLength + memorySizeLength];
        buf[0] = (byte)UdsServiceId.RequestDownload;
        buf[1] = dataFormatIdentifier;
        buf[2] = (byte)((memorySizeLength << 4) | memoryAddressLength);
        WriteUnsignedBigEndian(memoryAddress, buf.AsSpan(3, memoryAddressLength));
        WriteUnsignedBigEndian(memorySize, buf.AsSpan(3 + memoryAddressLength, memorySizeLength));
        return buf;
    }

    /// <summary>
    /// 解析 RequestDownload 的肯定响应。<br/>Parse a positive RequestDownload response.
    /// </summary>
    /// <param name="response">响应数据。<br/>The response data.</param>
    /// <returns>解析后的请求下载响应。<br/>The parsed request download response.</returns>
    public static Response ParseResponse(ReadOnlySpan<byte> response)
    {
        if (response.Length < 3 || response[0] != 0x74)
            throw new ProtocolException("Not a positive RequestDownload response.");

        int length = response[1] >> 4;
        if (length == 0 || length > 8 || (response[1] & 0x0F) != 0)
            throw new FrameFormatException("Invalid RequestDownload length format identifier.");
        if (response.Length != 2 + length)
            throw new FrameFormatException("Truncated RequestDownload response.");

        ulong maxNumberOfBlockLength = ReadUnsignedBigEndian(response.Slice(2, length));
        if (maxNumberOfBlockLength > (ulong)int.MaxValue + 2)
            throw new FrameFormatException("RequestDownload max block length is too large.");

        int maxTransferDataPayloadLength = maxNumberOfBlockLength <= 2
            ? 0
            : checked((int)maxNumberOfBlockLength - 2);
        return new Response(maxNumberOfBlockLength, maxTransferDataPayloadLength);
    }

    /// <summary>
    /// 发送 RequestDownload 请求并返回解析后的响应。<br/>Send a RequestDownload request and return the parsed response.
    /// </summary>
    public static async Task<Response> InvokeAsync(
        IAsyncUdsClient client,
        byte dataFormatIdentifier,
        ulong memoryAddress,
        ulong memorySize,
        byte memoryAddressLength,
        byte memorySizeLength,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        var resp = await client.SendRequestAsync(
            BuildRequest(dataFormatIdentifier, memoryAddress, memorySize, memoryAddressLength, memorySizeLength),
            null,
            cancellationToken).ConfigureAwait(false);
        return ParseResponse(resp.Span);
    }

    private static void ValidateLength(byte length, string paramName)
    {
        if (length is < 1 or > 8)
            throw new ArgumentOutOfRangeException(paramName, length, "Length must be in the 1..8 range.");
    }

    private static void ValidateFits(ulong value, byte length, string paramName)
    {
        if (length == 8) return;
        ulong max = (1UL << (length * 8)) - 1;
        if (value > max)
            throw new ArgumentOutOfRangeException(paramName, value, $"Value does not fit in {length} bytes.");
    }

    private static void WriteUnsignedBigEndian(ulong value, Span<byte> destination)
    {
        for (int i = destination.Length - 1; i >= 0; i--)
        {
            destination[i] = (byte)value;
            value >>= 8;
        }
    }

    private static ulong ReadUnsignedBigEndian(ReadOnlySpan<byte> source)
    {
        ulong value = 0;
        foreach (var b in source)
            value = (value << 8) | b;
        return value;
    }
}
