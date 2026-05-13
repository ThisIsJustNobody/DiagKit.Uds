namespace DiagKit.Uds.Services;

/// <summary>
/// ReadDTCInformation 常用子功能（ISO 14229-1 §11.3）。<br/>Common ReadDTCInformation sub-functions (ISO 14229-1 §11.3).
/// </summary>
public enum ReadDtcSubFunction : byte
{
    /// <summary>按状态掩码报告 DTC 数量。<br/>Report number of DTCs by status mask (0x01).</summary>
    ReportNumberOfDtcByStatusMask = 0x01,
    /// <summary>按状态掩码报告 DTC 列表。<br/>Report DTCs by status mask (0x02).</summary>
    ReportDtcByStatusMask = 0x02,
    /// <summary>报告 DTC 快照标识。<br/>Report DTC snapshot identification (0x03).</summary>
    ReportDtcSnapshotIdentification = 0x03,
    /// <summary>按 DTC 编号报告 DTC 快照记录。<br/>Report DTC snapshot record by DTC number (0x04).</summary>
    ReportDtcSnapshotRecordByDtcNumber = 0x04,
    /// <summary>报告支持的 DTC。<br/>Report supported DTCs (0x0A).</summary>
    ReportSupportedDtc = 0x0A,
}
