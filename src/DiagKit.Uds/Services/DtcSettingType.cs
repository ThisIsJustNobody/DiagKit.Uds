namespace DiagKit.Uds.Services;

/// <summary>
/// ControlDTCSetting 子功能。<br/>ControlDTCSetting sub-functions.
/// </summary>
public enum DtcSettingType : byte
{
    /// <summary>开启 DTC 设置。<br/>Turn DTC setting on (0x01).</summary>
    On = 0x01,
    /// <summary>关闭 DTC 设置。<br/>Turn DTC setting off (0x02).</summary>
    Off = 0x02,
}
