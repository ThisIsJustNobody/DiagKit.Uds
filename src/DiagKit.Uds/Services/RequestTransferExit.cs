using System;
using System.Threading;
using System.Threading.Tasks;

using DiagKit.Uds.Contracts;
using DiagKit.Uds.Exceptions;
using DiagKit.Uds.UdsLayer;

namespace DiagKit.Uds.Services;

/// <summary>
/// SID 0x37（RequestTransferExit/请求退出传输）辅助方法。<br/>Helpers for SID 0x37 RequestTransferExit.
/// </summary>
public static class RequestTransferExit
{
    /// <summary>
    /// RequestTransferExit 肯定响应的解析结果。<br/>Parsed positive RequestTransferExit response.
    /// </summary>
    public readonly record struct Response(ReadOnlyMemory<byte> TransferResponseParameterRecord);

    /// <summary>
    /// 构建 RequestTransferExit 请求。<br/>Build a RequestTransferExit request.
    /// </summary>
    public static byte[] BuildRequest(ReadOnlySpan<byte> transferRequestParameterRecord = default)
    {
        var buf = new byte[1 + transferRequestParameterRecord.Length];
        buf[0] = (byte)UdsServiceId.RequestTransferExit;
        transferRequestParameterRecord.CopyTo(buf.AsSpan(1));
        return buf;
    }

    /// <summary>
    /// 解析 RequestTransferExit 的肯定响应。<br/>Parse a positive RequestTransferExit response.
    /// </summary>
    public static Response ParseResponse(ReadOnlySpan<byte> response)
    {
        if (response.Length < 1 || response[0] != 0x77)
            throw new ProtocolException("Not a positive RequestTransferExit response.");
        return new Response(response.Length > 1 ? response[1..].ToArray() : []);
    }

    /// <summary>
    /// 发送 RequestTransferExit 请求并返回解析后的响应。<br/>Send a RequestTransferExit request and return the parsed response.
    /// </summary>
    public static async Task<Response> InvokeAsync(
        IAsyncUdsClient client,
        ReadOnlyMemory<byte> transferRequestParameterRecord = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        var resp = await client.SendRequestAsync(
            BuildRequest(transferRequestParameterRecord.Span),
            null,
            cancellationToken).ConfigureAwait(false);
        return ParseResponse(resp.Span);
    }
}
