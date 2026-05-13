using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using DiagKit.Uds.Contracts;
using DiagKit.Uds.UdsLayer;

using DiagKit.Uds.Exceptions;

namespace DiagKit.Uds.Services;

/// <summary>
/// SID 0x22（ReadDataByIdentifier/按标识符读取数据）辅助方法。<br/>Helpers for SID 0x22 ReadDataByIdentifier.
/// </summary>
public static class ReadDataByIdentifier
{
    /// <summary>
    /// 为一个或多个 DID 构建 ReadDataByIdentifier 请求。<br/>Build a ReadDataByIdentifier request for one or more DIDs.
    /// </summary>
    /// <param name="dids">一个或多个数据标识符。<br/>One or more data identifiers.</param>
    /// <returns>请求字节数组。<br/>The request byte array.</returns>
    public static byte[] BuildRequest(params ushort[] dids)
    {
        ArgumentNullException.ThrowIfNull(dids);
        if (dids.Length == 0) throw new ArgumentException("At least one DID is required.", nameof(dids));
        var buf = new byte[1 + dids.Length * 2];
        buf[0] = (byte)UdsServiceId.ReadDataByIdentifier;
        for (var i = 0; i < dids.Length; i++)
            BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(1 + i * 2, 2), dids[i]);
        return buf;
    }

    /// <summary>
    /// 解析 ReadDataByIdentifier 的肯定响应（所有 DID 的数据长度均已知）。<br/>Parse a positive ReadDataByIdentifier response whose DIDs are all known.
    /// </summary>
    /// <remarks>
    /// 调用方提供以 DID 为键的字节长度字典。返回 DID 到数据字节数组的映射。<br/>The caller provides the byte lengths keyed by DID. Returns a DID to data bytes mapping.
    /// </remarks>
    /// <param name="response">响应数据。<br/>The response data.</param>
    /// <param name="didLengths">以 DID 为键、期望数据长度为值的字典。<br/>A dictionary of DID to expected data length.</param>
    /// <returns>DID 到数据字节数组的映射字典。<br/>A dictionary mapping DID to data bytes.</returns>
    public static Dictionary<ushort, byte[]> ParseResponse(ReadOnlySpan<byte> response, IReadOnlyDictionary<ushort, int> didLengths)
    {
        if (response.Length < 3 || response[0] != 0x62)
            throw new ProtocolException("Not a positive ReadDataByIdentifier response.");
        var result = new Dictionary<ushort, byte[]>();
        int pos = 1;
        while (pos + 2 <= response.Length)
        {
            ushort did = BinaryPrimitives.ReadUInt16BigEndian(response.Slice(pos, 2));
            pos += 2;
            if (!didLengths.TryGetValue(did, out var len))
                throw new ProtocolException($"Unknown DID length for 0x{did:X4}.");
            if (pos + len > response.Length)
                throw new FrameFormatException($"Truncated ReadDataByIdentifier response for DID 0x{did:X4}.");
            result[did] = response.Slice(pos, len).ToArray();
            pos += len;
        }
        if (pos != response.Length)
            throw new FrameFormatException("Truncated ReadDataByIdentifier response.");
        return result;
    }

    /// <summary>
    /// 发送单个 DID 的 ReadDataByIdentifier 请求并返回对应的原始数据字节。<br/>Send a ReadDataByIdentifier request for a single DID and return the raw data bytes.
    /// </summary>
    /// <remarks>
    /// 返回 SID+DID 前缀之后的所有数据字节。<br/>Returns any bytes after the 3-byte SID+DID prefix.
    /// </remarks>
    /// <param name="client">UDS 异步客户端。<br/>The UDS async client.</param>
    /// <param name="did">数据标识符。<br/>The data identifier.</param>
    /// <param name="cancellationToken">取消令牌。<br/>Cancellation token.</param>
    /// <returns>DID 对应的原始数据。<br/>The raw data for the requested DID.</returns>
    public static async Task<ReadOnlyMemory<byte>> InvokeAsync(IAsyncUdsClient client, ushort did, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        var resp = await client.SendRequestAsync(BuildRequest(did), null, cancellationToken).ConfigureAwait(false);
        if (resp.Length < 3 || resp.Span[0] != 0x62)
            throw new ProtocolException("Not a positive ReadDataByIdentifier response.");
        ushort respDid = (ushort)((resp.Span[1] << 8) | resp.Span[2]);
        if (respDid != did)
            throw new ProtocolException($"ReadDataByIdentifier echoed DID 0x{respDid:X4}, expected 0x{did:X4}.");
        return resp[3..];
    }
}
