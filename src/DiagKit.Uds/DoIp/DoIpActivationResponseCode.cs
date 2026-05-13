namespace DiagKit.Uds.DoIp;

/// <summary>
/// DoIP 路由激活响应码（ISO 13400-2 §7.1.5）。<br/>Routing activation response codes (ISO 13400-2 §7.1.5).
/// </summary>
public enum DoIpActivationResponseCode : byte
{
    /// <summary>未知源地址。<br/>Unknown source address.</summary>
    UnknownSourceAddress = 0x00,
    /// <summary>无可用套接字。<br/>No socket available.</summary>
    NoSocketAvailable = 0x01,
    /// <summary>源地址不匹配（与已注册的不同）。<br/>Source address differs from already registered.</summary>
    SourceAddressDifferent = 0x02,
    /// <summary>源地址已注册。<br/>Source address already registered.</summary>
    SourceAddressAlreadyRegistered = 0x03,
    /// <summary>缺少认证。<br/>Missing authentication.</summary>
    MissingAuthentication = 0x04,
    /// <summary>拒绝确认。<br/>Rejected confirmation.</summary>
    RejectedConfirmation = 0x05,
    /// <summary>不支持的激活类型。<br/>Unsupported activation type.</summary>
    UnsupportedActivationType = 0x06,
    /// <summary>激活成功。<br/>Activation successful.</summary>
    Success = 0x10,
    /// <summary>激活成功，需要确认。<br/>Activation successful, confirmation required.</summary>
    SuccessConfirmationRequired = 0x11,
}
