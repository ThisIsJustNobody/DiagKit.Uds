using System;
using System.Threading;
using System.Threading.Tasks;

using DiagKit.Uds.Contracts;
using DiagKit.Uds.Exceptions;
using DiagKit.Uds.UdsLayer;

namespace DiagKit.Uds.Services;

/// <summary>
/// SID 0x85（ControlDTCSetting/DTC 设置控制）辅助方法。<br/>Helpers for SID 0x85 ControlDTCSetting.
/// </summary>
public static class ControlDtcSetting
{
    /// <summary>
    /// ControlDTCSetting 肯定响应。<br/>Parsed positive ControlDTCSetting response.
    /// </summary>
    public readonly record struct Response(DtcSettingType SettingType);

    /// <summary>
    /// 构建 ControlDTCSetting 请求。<br/>Build a ControlDTCSetting request.
    /// </summary>
    public static byte[] BuildRequest(
        DtcSettingType settingType,
        ReadOnlySpan<byte> dtcSettingControlOptionRecord = default,
        bool suppressPositiveResponse = false)
    {
        byte sub = (byte)settingType;
        if (suppressPositiveResponse) sub |= 0x80;
        var buf = new byte[2 + dtcSettingControlOptionRecord.Length];
        buf[0] = (byte)UdsServiceId.ControlDtcSetting;
        buf[1] = sub;
        dtcSettingControlOptionRecord.CopyTo(buf.AsSpan(2));
        return buf;
    }

    /// <summary>
    /// 解析 ControlDTCSetting 肯定响应。<br/>Parse a positive ControlDTCSetting response.
    /// </summary>
    public static Response ParseResponse(ReadOnlySpan<byte> response, DtcSettingType? expectedSettingType = null)
    {
        if (response.Length < 2 || response[0] != 0xC5)
            throw new ProtocolException("Not a positive ControlDTCSetting response.");
        if (response.Length != 2)
            throw new FrameFormatException("Invalid ControlDTCSetting response length.");

        var settingType = (DtcSettingType)response[1];
        if (expectedSettingType.HasValue && settingType != expectedSettingType.Value)
            throw new ProtocolException($"ControlDTCSetting echoed sub-function 0x{(byte)settingType:X2}, expected 0x{(byte)expectedSettingType.Value:X2}.");

        return new Response(settingType);
    }

    /// <summary>
    /// 发送 ControlDTCSetting 请求并返回解析后的响应。<br/>Send a ControlDTCSetting request and return the parsed response.
    /// </summary>
    public static Task<Response> InvokeAsync(
        IAsyncUdsClient client,
        DtcSettingType settingType,
        CancellationToken cancellationToken)
        => InvokeAsync(client, settingType, default, cancellationToken);

    /// <summary>
    /// 发送 ControlDTCSetting 请求并返回解析后的响应。<br/>Send a ControlDTCSetting request and return the parsed response.
    /// </summary>
    public static async Task<Response> InvokeAsync(
        IAsyncUdsClient client,
        DtcSettingType settingType,
        ReadOnlyMemory<byte> dtcSettingControlOptionRecord = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        var resp = await client.SendRequestAsync(
            BuildRequest(settingType, dtcSettingControlOptionRecord.Span),
            null,
            cancellationToken).ConfigureAwait(false);
        return ParseResponse(resp.Span, settingType);
    }
}
