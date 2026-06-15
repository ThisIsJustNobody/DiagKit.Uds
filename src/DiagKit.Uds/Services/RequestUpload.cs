using System;
using System.Threading;
using System.Threading.Tasks;

using DiagKit.Uds.Contracts;
using DiagKit.Uds.UdsLayer;

namespace DiagKit.Uds.Services;

/// <summary>
/// SID 0x35（RequestUpload/请求上传）辅助方法。<br/>Helpers for SID 0x35 RequestUpload.
/// </summary>
public static class RequestUpload
{
    /// <summary>
    /// RequestUpload 肯定响应的解析结果。<br/>Parsed positive RequestUpload response.
    /// </summary>
    public readonly record struct Response(ulong MaxNumberOfBlockLength, int MaxTransferDataPayloadLength);

    /// <summary>
    /// 构建 RequestUpload 请求。<br/>Build a RequestUpload request.
    /// </summary>
    public static byte[] BuildRequest(
        byte dataFormatIdentifier,
        ulong memoryAddress,
        ulong memorySize,
        byte memoryAddressLength,
        byte memorySizeLength)
        => MemoryParameterCodec.BuildAddressAndSizeRequest(
            (byte)UdsServiceId.RequestUpload,
            dataFormatIdentifier,
            memoryAddress,
            memorySize,
            memoryAddressLength,
            memorySizeLength);

    /// <summary>
    /// 解析 RequestUpload 的肯定响应。<br/>Parse a positive RequestUpload response.
    /// </summary>
    public static Response ParseResponse(ReadOnlySpan<byte> response)
    {
        const string serviceName = nameof(RequestUpload);
        ulong maxNumberOfBlockLength = MemoryParameterCodec.ParseMaxNumberOfBlockLength(response, 0x75, serviceName);
        int maxTransferDataPayloadLength = MemoryParameterCodec.GetMaxTransferDataPayloadLength(maxNumberOfBlockLength, serviceName);
        return new Response(maxNumberOfBlockLength, maxTransferDataPayloadLength);
    }

    /// <summary>
    /// 发送 RequestUpload 请求并返回解析后的响应。<br/>Send a RequestUpload request and return the parsed response.
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
}
