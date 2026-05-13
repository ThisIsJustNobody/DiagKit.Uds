namespace DiagKit.Uds.Services;

/// <summary>
/// 诊断会话类型（ISO 14229-1 §9.2.2.2）。<br/>Diagnostic session types (ISO 14229-1 §9.2.2.2).
/// </summary>
public enum DiagnosticSessionType : byte
{
    /// <summary>默认会话。<br/>Default session (0x01).</summary>
    Default = 0x01,
    /// <summary>编程会话（用于 ECU 刷写）。<br/>Programming session - used for ECU flashing (0x02).</summary>
    Programming = 0x02,
    /// <summary>扩展诊断会话。<br/>Extended diagnostic session (0x03).</summary>
    ExtendedDiagnostic = 0x03,
    /// <summary>安全系统诊断会话。<br/>Safety system diagnostic session (0x04).</summary>
    SafetySystemDiagnostic = 0x04,
}
