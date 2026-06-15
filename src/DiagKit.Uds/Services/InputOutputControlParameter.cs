namespace DiagKit.Uds.Services;

/// <summary>
/// InputOutputControlByIdentifier 控制参数。<br/>InputOutputControlByIdentifier control parameters.
/// </summary>
public enum InputOutputControlParameter : byte
{
    /// <summary>将控制权交回 ECU。<br/>Return control to ECU (0x00).</summary>
    ReturnControlToEcu = 0x00,
    /// <summary>重置为默认值。<br/>Reset to default (0x01).</summary>
    ResetToDefault = 0x01,
    /// <summary>冻结当前状态。<br/>Freeze current state (0x02).</summary>
    FreezeCurrentState = 0x02,
    /// <summary>短期调整。<br/>Short term adjustment (0x03).</summary>
    ShortTermAdjustment = 0x03,
}
