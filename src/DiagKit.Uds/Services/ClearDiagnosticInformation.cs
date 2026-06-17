using System;
using System.Threading;
using System.Threading.Tasks;

using DiagKit.Uds.Contracts;
using DiagKit.Uds.Exceptions;
using DiagKit.Uds.UdsLayer;

namespace DiagKit.Uds.Services;

/// <summary>
/// SID 0x14（ClearDiagnosticInformation/清除诊断信息）辅助方法。<br/>Helpers for SID 0x14 ClearDiagnosticInformation.
/// </summary>
public static class ClearDiagnosticInformation
{
    /// <summary>
    /// ClearDiagnosticInformation 肯定响应。<br/>Parsed positive ClearDiagnosticInformation response.
    /// </summary>
    public readonly record struct Response;

    /// <summary>
    /// 构建 ClearDiagnosticInformation 请求。<br/>Build a ClearDiagnosticInformation request.
    /// </summary>
    public static byte[] BuildRequest(uint groupOfDtc)
    {
        if (groupOfDtc > 0x00FFFFFF)
            throw new ArgumentOutOfRangeException(nameof(groupOfDtc), groupOfDtc, "Group of DTC must fit in 24 bits.");

        return
        [
            (byte)UdsServiceId.ClearDiagnosticInformation,
            (byte)(groupOfDtc >> 16),
            (byte)(groupOfDtc >> 8),
            (byte)groupOfDtc,
        ];
    }

    /// <summary>
    /// 解析 ClearDiagnosticInformation 肯定响应。<br/>Parse a positive ClearDiagnosticInformation response.
    /// </summary>
    public static Response ParseResponse(ReadOnlySpan<byte> response)
    {
        if (response.Length == 0 || response[0] != 0x54)
            throw new ProtocolException("Not a positive ClearDiagnosticInformation response.");
        if (response.Length != 1)
            throw new FrameFormatException("Invalid ClearDiagnosticInformation response length.");

        return default;
    }

    /// <summary>
    /// 发送 ClearDiagnosticInformation 请求并返回解析后的响应。<br/>Send a ClearDiagnosticInformation request and return the parsed response.
    /// </summary>
    public static async Task<Response> InvokeAsync(
        IAsyncUdsClient client,
        uint groupOfDtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        var resp = await client.SendRequestAsync(BuildRequest(groupOfDtc), null, cancellationToken).ConfigureAwait(false);
        return ParseResponse(resp.Span);
    }
}
