namespace DiagKit.Uds.DoIp;

/// <summary>
/// DoIP 通用头部否定确认码（ISO 13400-2:2012 §7.1）。<br/>Generic DoIP header negative acknowledgment codes (ISO 13400-2:2012 §7.1).
/// </summary>
public enum DoIpGenericHeaderNackCode : byte
{
    /// <summary>错误的模式格式。<br/>Incorrect pattern format.</summary>
    IncorrectPatternFormat = 0x00,
    /// <summary>未知载荷类型。<br/>Unknown payload type.</summary>
    UnknownPayloadType = 0x01,
    /// <summary>消息过大。<br/>Message too large.</summary>
    MessageTooLarge = 0x02,
    /// <summary>内存不足。<br/>Out of memory.</summary>
    OutOfMemory = 0x03,
    /// <summary>无效的载荷长度。<br/>Invalid payload length.</summary>
    InvalidPayloadLength = 0x04,
}
