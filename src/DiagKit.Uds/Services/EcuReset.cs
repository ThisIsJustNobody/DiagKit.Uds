using System;
using System.Threading;
using System.Threading.Tasks;

using DiagKit.Uds.Contracts;
using DiagKit.Uds.Exceptions;
using DiagKit.Uds.UdsLayer;

namespace DiagKit.Uds.Services;

/// <summary>
/// SID 0x11（ECUReset/ECU 复位）辅助方法。<br/>Helpers for SID 0x11 ECUReset.
/// </summary>
public static class EcuReset
{
    /// <summary>
    /// ECUReset 肯定响应。<br/>Parsed positive ECUReset response.
    /// </summary>
    public readonly record struct Response(EcuResetType ResetType, byte? PowerDownTime);

    /// <summary>
    /// 构建 ECUReset 请求。<br/>Build an ECUReset request.
    /// </summary>
    public static byte[] BuildRequest(EcuResetType resetType, bool suppressPositiveResponse = false)
    {
        byte sub = (byte)resetType;
        if (suppressPositiveResponse) sub |= 0x80;
        return [(byte)UdsServiceId.EcuReset, sub];
    }

    /// <summary>
    /// 解析 ECUReset 肯定响应。<br/>Parse a positive ECUReset response.
    /// </summary>
    public static Response ParseResponse(ReadOnlySpan<byte> response, EcuResetType? expectedResetType = null)
    {
        if (response.Length < 2 || response[0] != 0x51)
            throw new ProtocolException("Not a positive ECUReset response.");

        var resetType = (EcuResetType)response[1];
        if (expectedResetType.HasValue && resetType != expectedResetType.Value)
            throw new ProtocolException($"ECUReset echoed sub-function 0x{(byte)resetType:X2}, expected 0x{(byte)expectedResetType.Value:X2}.");

        if (resetType == EcuResetType.EnableRapidPowerShutdown)
        {
            if (response.Length != 3)
                throw new FrameFormatException("Invalid ECUReset rapid power shutdown response length.");
            return new Response(resetType, response[2]);
        }

        if (response.Length != 2)
            throw new FrameFormatException("Invalid ECUReset response length.");

        return new Response(resetType, null);
    }

    /// <summary>
    /// 发送 ECUReset 请求并返回解析后的响应。<br/>Send an ECUReset request and return the parsed response.
    /// </summary>
    public static async Task<Response> InvokeAsync(
        IAsyncUdsClient client,
        EcuResetType resetType,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        var resp = await client.SendRequestAsync(BuildRequest(resetType), null, cancellationToken).ConfigureAwait(false);
        return ParseResponse(resp.Span, resetType);
    }
}
