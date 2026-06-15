using System;
using System.Buffers.Binary;
using System.Threading;
using System.Threading.Tasks;

using DiagKit.Uds.Contracts;
using DiagKit.Uds.Exceptions;
using DiagKit.Uds.UdsLayer;

namespace DiagKit.Uds.Services;

/// <summary>
/// SID 0x2E（WriteDataByIdentifier/按标识符写入数据）辅助方法。<br/>Helpers for SID 0x2E WriteDataByIdentifier.
/// </summary>
public static class WriteDataByIdentifier
{
    /// <summary>
    /// WriteDataByIdentifier 肯定响应。<br/>Parsed positive WriteDataByIdentifier response.
    /// </summary>
    public readonly record struct Response(ushort DataIdentifier);

    /// <summary>
    /// 构建 WriteDataByIdentifier 请求。<br/>Build a WriteDataByIdentifier request.
    /// </summary>
    public static byte[] BuildRequest(ushort did, ReadOnlySpan<byte> dataRecord)
    {
        var buf = new byte[3 + dataRecord.Length];
        buf[0] = (byte)UdsServiceId.WriteDataByIdentifier;
        BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(1, 2), did);
        dataRecord.CopyTo(buf.AsSpan(3));
        return buf;
    }

    /// <summary>
    /// 解析 WriteDataByIdentifier 肯定响应。<br/>Parse a positive WriteDataByIdentifier response.
    /// </summary>
    public static Response ParseResponse(ReadOnlySpan<byte> response, ushort? expectedDid = null)
    {
        if (response.Length != 3 || response[0] != 0x6E)
            throw new ProtocolException("Not a positive WriteDataByIdentifier response.");

        ushort did = BinaryPrimitives.ReadUInt16BigEndian(response.Slice(1, 2));
        if (expectedDid.HasValue && did != expectedDid.Value)
            throw new ProtocolException($"WriteDataByIdentifier echoed DID 0x{did:X4}, expected 0x{expectedDid.Value:X4}.");

        return new Response(did);
    }

    /// <summary>
    /// 发送 WriteDataByIdentifier 请求并返回解析后的响应。<br/>Send a WriteDataByIdentifier request and return the parsed response.
    /// </summary>
    public static async Task<Response> InvokeAsync(
        IAsyncUdsClient client,
        ushort did,
        ReadOnlyMemory<byte> dataRecord,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        var resp = await client.SendRequestAsync(BuildRequest(did, dataRecord.Span), null, cancellationToken).ConfigureAwait(false);
        return ParseResponse(resp.Span, did);
    }
}
