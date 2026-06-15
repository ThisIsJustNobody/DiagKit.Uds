namespace DiagKit.Uds.Services;

/// <summary>
/// ECUReset 子功能。<br/>ECUReset sub-functions.
/// </summary>
public enum EcuResetType : byte
{
    /// <summary>硬复位。<br/>Hard reset (0x01).</summary>
    HardReset = 0x01,
    /// <summary>点火开关复位。<br/>Key off/on reset (0x02).</summary>
    KeyOffOnReset = 0x02,
    /// <summary>软复位。<br/>Soft reset (0x03).</summary>
    SoftReset = 0x03,
    /// <summary>启用快速断电。<br/>Enable rapid power shutdown (0x04).</summary>
    EnableRapidPowerShutdown = 0x04,
    /// <summary>禁用快速断电。<br/>Disable rapid power shutdown (0x05).</summary>
    DisableRapidPowerShutdown = 0x05,
}
