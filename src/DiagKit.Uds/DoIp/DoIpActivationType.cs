namespace DiagKit.Uds.DoIp;

/// <summary>
/// 路由激活类型码（ISO 13400-2 §7.1.4）。<br/>Routing activation type codes (ISO 13400-2 §7.1.4).
/// </summary>
public enum DoIpActivationType : byte
{
    /// <summary>默认激活。<br/>Default activation.</summary>
    Default = 0x00,

    /// <summary>WWH-OBD激活。<br/>WWH-OBD activation.</summary>
    WwhObd = 0x01,

    /// <summary>中央安全激活。<br/>Central security activation.</summary>
    CentralSecurity = 0xE0,
}
