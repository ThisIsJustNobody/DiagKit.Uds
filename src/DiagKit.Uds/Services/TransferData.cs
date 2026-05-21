using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using DiagKit.Uds.Contracts;
using DiagKit.Uds.Exceptions;
using DiagKit.Uds.UdsLayer;

namespace DiagKit.Uds.Services;

/// <summary>
/// SID 0x36（TransferData/传输数据）辅助方法。<br/>Helpers for SID 0x36 TransferData.
/// </summary>
public static class TransferData
{
    /// <summary>
    /// TransferData 肯定响应的解析结果。<br/>Parsed positive TransferData response.
    /// </summary>
    public readonly record struct Response(byte BlockSequenceCounter, ReadOnlyMemory<byte> TransferResponseParameterRecord);

    /// <summary>
    /// 构建 TransferData 请求。<br/>Build a TransferData request.
    /// </summary>
    public static byte[] BuildRequest(byte blockSequenceCounter, ReadOnlySpan<byte> transferRequestParameterRecord = default)
    {
        var buf = new byte[2 + transferRequestParameterRecord.Length];
        buf[0] = (byte)UdsServiceId.TransferData;
        buf[1] = blockSequenceCounter;
        transferRequestParameterRecord.CopyTo(buf.AsSpan(2));
        return buf;
    }

    /// <summary>
    /// 解析 TransferData 的肯定响应。<br/>Parse a positive TransferData response.
    /// </summary>
    public static Response ParseResponse(ReadOnlySpan<byte> response, byte? expectedBlockSequenceCounter = null)
    {
        if (response.Length < 2 || response[0] != 0x76)
            throw new ProtocolException("Not a positive TransferData response.");
        byte blockSequenceCounter = response[1];
        if (expectedBlockSequenceCounter.HasValue && blockSequenceCounter != expectedBlockSequenceCounter.Value)
            throw new ProtocolException($"TransferData echoed block sequence counter 0x{blockSequenceCounter:X2}, expected 0x{expectedBlockSequenceCounter.Value:X2}.");
        return new Response(blockSequenceCounter, response.Length > 2 ? response[2..].ToArray() : []);
    }

    /// <summary>
    /// 发送 TransferData 请求并返回解析后的响应。<br/>Send a TransferData request and return the parsed response.
    /// </summary>
    public static async Task<Response> InvokeAsync(
        IAsyncUdsClient client,
        byte blockSequenceCounter,
        ReadOnlyMemory<byte> transferRequestParameterRecord = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        var resp = await client.SendRequestAsync(
            BuildRequest(blockSequenceCounter, transferRequestParameterRecord.Span),
            null,
            cancellationToken).ConfigureAwait(false);
        return ParseResponse(resp.Span, blockSequenceCounter);
    }

    /// <summary>
    /// 将一段数据按最大 TransferData 载荷长度分块发送。<br/>Send data split into TransferData blocks.
    /// </summary>
    public static async Task<IReadOnlyList<Response>> SendBlocksAsync(
        IAsyncUdsClient client,
        ReadOnlyMemory<byte> data,
        int maxTransferDataPayloadLength,
        byte firstBlockSequenceCounter = 1,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        if (maxTransferDataPayloadLength <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxTransferDataPayloadLength), maxTransferDataPayloadLength, "Payload length must be positive.");

        var responses = new List<Response>();
        byte blockSequenceCounter = firstBlockSequenceCounter;
        for (int offset = 0; offset < data.Length; offset += maxTransferDataPayloadLength)
        {
            int count = Math.Min(maxTransferDataPayloadLength, data.Length - offset);
            var response = await InvokeAsync(
                client,
                blockSequenceCounter,
                data.Slice(offset, count),
                cancellationToken).ConfigureAwait(false);
            responses.Add(response);
            blockSequenceCounter++;
        }

        return responses;
    }
}
