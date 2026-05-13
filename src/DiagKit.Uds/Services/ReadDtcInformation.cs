using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using DiagKit.Uds.Contracts;
using DiagKit.Uds.UdsLayer;

using DiagKit.Uds.Exceptions;

namespace DiagKit.Uds.Services;

/// <summary>
/// SID 0x19（ReadDTCInformation/读取诊断故障码信息）辅助方法。<br/>Helpers for SID 0x19 ReadDTCInformation.
/// </summary>
public static class ReadDtcInformation
{
    /// <summary>
    /// 构建 0x19/0x01（ReportNumberOfDtcByStatusMask）请求。<br/>Build a 0x19/0x01 ReportNumberOfDtcByStatusMask request.
    /// </summary>
    /// <param name="mask">DTC 状态掩码。<br/>The DTC status mask.</param>
    /// <returns>请求字节数组。<br/>The request byte array.</returns>
    public static byte[] BuildReportNumber(DtcStatus mask)
        => [(byte)UdsServiceId.ReadDtcInformation, (byte)ReadDtcSubFunction.ReportNumberOfDtcByStatusMask, (byte)mask];

    /// <summary>
    /// 构建 0x19/0x02（ReportDtcByStatusMask）请求。<br/>Build a 0x19/0x02 ReportDtcByStatusMask request.
    /// </summary>
    /// <param name="mask">DTC 状态掩码。<br/>The DTC status mask.</param>
    /// <returns>请求字节数组。<br/>The request byte array.</returns>
    public static byte[] BuildReportDtcByStatusMask(DtcStatus mask)
        => [(byte)UdsServiceId.ReadDtcInformation, (byte)ReadDtcSubFunction.ReportDtcByStatusMask, (byte)mask];

    /// <summary>
    /// 解析 0x19/0x02（ReportDtcByStatusMask）的肯定响应。<br/>Parse the positive response of 0x19/0x02 ReportDtcByStatusMask.
    /// </summary>
    /// <param name="response">响应数据。<br/>The response data.</param>
    /// <returns>解析后的 DTC 记录列表。<br/>The parsed list of DTC records.</returns>
    public static IReadOnlyList<DtcRecord> ParseDtcByStatusMask(ReadOnlySpan<byte> response)
    {
        if (response.Length < 3 || response[0] != 0x59 || response[1] != (byte)ReadDtcSubFunction.ReportDtcByStatusMask)
            throw new ProtocolException("Not a 0x19/0x02 positive response.");
        if ((response.Length - 3) % 4 != 0)
            throw new FrameFormatException("Truncated 0x19/0x02 DTC record.");
        var list = new List<DtcRecord>();
        int pos = 3; // skip SID, sub-function, statusAvailabilityMask
        while (pos + 4 <= response.Length)
        {
            uint code = (uint)((response[pos] << 16) | (response[pos + 1] << 8) | response[pos + 2]);
            var status = (DtcStatus)response[pos + 3];
            list.Add(new DtcRecord(code, status));
            pos += 4;
        }
        return list;
    }

    /// <summary>
    /// 发送 0x19/0x02（ReportDtcByStatusMask）请求并返回解析后的 DTC 记录列表。<br/>Send 0x19/0x02 ReportDtcByStatusMask and return parsed DTC records.
    /// </summary>
    /// <param name="client">UDS 异步客户端。<br/>The UDS async client.</param>
    /// <param name="mask">DTC 状态掩码。<br/>The DTC status mask.</param>
    /// <param name="cancellationToken">取消令牌。<br/>Cancellation token.</param>
    /// <returns>解析后的 DTC 记录列表。<br/>The parsed list of DTC records.</returns>
    public static async Task<IReadOnlyList<DtcRecord>> ReportDtcByStatusMaskAsync(
        IAsyncUdsClient client, DtcStatus mask, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        var resp = await client.SendRequestAsync(BuildReportDtcByStatusMask(mask), null, cancellationToken).ConfigureAwait(false);
        return ParseDtcByStatusMask(resp.Span);
    }
}
