using System;

namespace DiagKit.Uds.Services;

/// <summary>
/// DTC 状态掩码（ISO 14229-1 表 232）。<br/>DTC status masks (ISO 14229-1 Table 232).
/// </summary>
[Flags]
public enum DtcStatus : byte
{
    /// <summary>无状态位。<br/>No status bits set.</summary>
    None = 0,
    /// <summary>本次测试失败。<br/>Test failed (bit 0 - testFailed).</summary>
    TestFailed = 0x01,
    /// <summary>本操作周期内测试失败。<br/>Test failed this operation cycle (bit 1 - testFailedThisOperationCycle).</summary>
    TestFailedThisOperationCycle = 0x02,
    /// <summary>待定 DTC（上次测试失败但尚未确认）。<br/>Pending DTC - last test failed but not yet confirmed (bit 2 - pendingDTC).</summary>
    PendingDtc = 0x04,
    /// <summary>已确认 DTC。<br/>Confirmed DTC (bit 3 - confirmedDTC).</summary>
    ConfirmedDtc = 0x08,
    /// <summary>上次清除后测试未完成。<br/>Test not completed since last clear (bit 4 - testNotCompletedSinceLastClear).</summary>
    TestNotCompletedSinceLastClear = 0x10,
    /// <summary>上次清除后测试失败。<br/>Test failed since last clear (bit 5 - testFailedSinceLastClear).</summary>
    TestFailedSinceLastClear = 0x20,
    /// <summary>本操作周期内测试未完成。<br/>Test not completed this operation cycle (bit 6 - testNotCompletedThisOperationCycle).</summary>
    TestNotCompletedThisOperationCycle = 0x40,
    /// <summary>警告指示灯请求点亮。<br/>Warning indicator requested (bit 7 - warningIndicatorRequested).</summary>
    WarningIndicatorRequested = 0x80,
    /// <summary>所有状态位。<br/>All status bits.</summary>
    All = 0xFF,
}
