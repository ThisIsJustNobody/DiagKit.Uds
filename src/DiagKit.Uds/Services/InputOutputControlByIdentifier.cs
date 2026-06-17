using System;
using System.Buffers.Binary;
using System.Threading;
using System.Threading.Tasks;

using DiagKit.Uds.Contracts;
using DiagKit.Uds.Exceptions;
using DiagKit.Uds.UdsLayer;

namespace DiagKit.Uds.Services;

/// <summary>
/// SID 0x2F（InputOutputControlByIdentifier/按标识符输入输出控制）辅助方法。<br/>Helpers for SID 0x2F InputOutputControlByIdentifier.
/// </summary>
public static class InputOutputControlByIdentifier
{
    /// <summary>
    /// InputOutputControlByIdentifier 肯定响应。<br/>Parsed positive InputOutputControlByIdentifier response.
    /// </summary>
    public readonly record struct Response(
        ushort DataIdentifier,
        InputOutputControlParameter ControlParameter,
        ReadOnlyMemory<byte> ControlStatusRecord);

    /// <summary>
    /// 构建 InputOutputControlByIdentifier 请求。<br/>Build an InputOutputControlByIdentifier request.
    /// </summary>
    public static byte[] BuildRequest(
        ushort did,
        InputOutputControlParameter controlParameter,
        ReadOnlySpan<byte> controlStateAndMaskRecord = default)
    {
        var buf = new byte[4 + controlStateAndMaskRecord.Length];
        buf[0] = (byte)UdsServiceId.InputOutputControlByIdentifier;
        BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(1, 2), did);
        buf[3] = (byte)controlParameter;
        controlStateAndMaskRecord.CopyTo(buf.AsSpan(4));
        return buf;
    }

    /// <summary>
    /// 解析 InputOutputControlByIdentifier 肯定响应。<br/>Parse a positive InputOutputControlByIdentifier response.
    /// </summary>
    public static Response ParseResponse(
        ReadOnlySpan<byte> response,
        ushort? expectedDid = null,
        InputOutputControlParameter? expectedControlParameter = null)
    {
        if (response.Length < 4 || response[0] != 0x6F)
            throw new ProtocolException("Not a positive InputOutputControlByIdentifier response.");

        ushort did = BinaryPrimitives.ReadUInt16BigEndian(response.Slice(1, 2));
        var controlParameter = (InputOutputControlParameter)response[3];
        if (expectedDid.HasValue && did != expectedDid.Value)
            throw new ProtocolException($"InputOutputControlByIdentifier echoed DID 0x{did:X4}, expected 0x{expectedDid.Value:X4}.");
        if (expectedControlParameter.HasValue && controlParameter != expectedControlParameter.Value)
            throw new ProtocolException($"InputOutputControlByIdentifier echoed control parameter 0x{(byte)controlParameter:X2}, expected 0x{(byte)expectedControlParameter.Value:X2}.");

        return new Response(did, controlParameter, response.Length > 4 ? response[4..].ToArray() : []);
    }

    /// <summary>
    /// 发送 InputOutputControlByIdentifier 请求并返回解析后的响应。<br/>Send an InputOutputControlByIdentifier request and return the parsed response.
    /// </summary>
    public static Task<Response> InvokeAsync(
        IAsyncUdsClient client,
        ushort did,
        InputOutputControlParameter controlParameter,
        CancellationToken cancellationToken)
        => InvokeAsync(client, did, controlParameter, default, cancellationToken);

    /// <summary>
    /// 发送 InputOutputControlByIdentifier 请求并返回解析后的响应。<br/>Send an InputOutputControlByIdentifier request and return the parsed response.
    /// </summary>
    public static async Task<Response> InvokeAsync(
        IAsyncUdsClient client,
        ushort did,
        InputOutputControlParameter controlParameter,
        ReadOnlyMemory<byte> controlStateAndMaskRecord = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        var resp = await client.SendRequestAsync(
            BuildRequest(did, controlParameter, controlStateAndMaskRecord.Span),
            null,
            cancellationToken).ConfigureAwait(false);
        return ParseResponse(resp.Span, did, controlParameter);
    }
}
