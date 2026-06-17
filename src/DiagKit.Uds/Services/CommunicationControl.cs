using System;
using System.Threading;
using System.Threading.Tasks;

using DiagKit.Uds.Contracts;
using DiagKit.Uds.Exceptions;
using DiagKit.Uds.UdsLayer;

namespace DiagKit.Uds.Services;

/// <summary>
/// SID 0x28（CommunicationControl/通信控制）辅助方法。<br/>Helpers for SID 0x28 CommunicationControl.
/// </summary>
public static class CommunicationControl
{
    /// <summary>
    /// CommunicationControl 肯定响应。<br/>Parsed positive CommunicationControl response.
    /// </summary>
    public readonly record struct Response(CommunicationControlType ControlType);

    /// <summary>
    /// 构建 CommunicationControl 请求。<br/>Build a CommunicationControl request.
    /// </summary>
    public static byte[] BuildRequest(
        CommunicationControlType controlType,
        byte communicationType,
        ReadOnlySpan<byte> communicationControlRecord = default,
        bool suppressPositiveResponse = false)
    {
        byte sub = (byte)controlType;
        if (suppressPositiveResponse) sub |= 0x80;
        var buf = new byte[3 + communicationControlRecord.Length];
        buf[0] = (byte)UdsServiceId.CommunicationControl;
        buf[1] = sub;
        buf[2] = communicationType;
        communicationControlRecord.CopyTo(buf.AsSpan(3));
        return buf;
    }

    /// <summary>
    /// 解析 CommunicationControl 肯定响应。<br/>Parse a positive CommunicationControl response.
    /// </summary>
    public static Response ParseResponse(ReadOnlySpan<byte> response, CommunicationControlType? expectedControlType = null)
    {
        if (response.Length < 2 || response[0] != 0x68)
            throw new ProtocolException("Not a positive CommunicationControl response.");
        if (response.Length != 2)
            throw new FrameFormatException("Invalid CommunicationControl response length.");

        var controlType = (CommunicationControlType)response[1];
        if (expectedControlType.HasValue && controlType != expectedControlType.Value)
            throw new ProtocolException($"CommunicationControl echoed sub-function 0x{(byte)controlType:X2}, expected 0x{(byte)expectedControlType.Value:X2}.");

        return new Response(controlType);
    }

    /// <summary>
    /// 发送 CommunicationControl 请求并返回解析后的响应。<br/>Send a CommunicationControl request and return the parsed response.
    /// </summary>
    public static Task<Response> InvokeAsync(
        IAsyncUdsClient client,
        CommunicationControlType controlType,
        byte communicationType,
        CancellationToken cancellationToken)
        => InvokeAsync(client, controlType, communicationType, default, cancellationToken);

    /// <summary>
    /// 发送 CommunicationControl 请求并返回解析后的响应。<br/>Send a CommunicationControl request and return the parsed response.
    /// </summary>
    public static async Task<Response> InvokeAsync(
        IAsyncUdsClient client,
        CommunicationControlType controlType,
        byte communicationType,
        ReadOnlyMemory<byte> communicationControlRecord = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        var resp = await client.SendRequestAsync(
            BuildRequest(controlType, communicationType, communicationControlRecord.Span),
            null,
            cancellationToken).ConfigureAwait(false);
        return ParseResponse(resp.Span, controlType);
    }
}
