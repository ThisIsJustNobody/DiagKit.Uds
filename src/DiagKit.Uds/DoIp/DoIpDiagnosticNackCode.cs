namespace DiagKit.Uds.DoIp;

/// <summary>
/// DoIP 诊断消息否定确认码（ISO 13400-2 §7.8）。<br/>Diagnostic message negative acknowledgment codes (ISO 13400-2 §7.8).
/// </summary>
public enum DoIpDiagnosticNackCode : byte
{
    /// <summary>无效源地址。<br/>Invalid source address.</summary>
    InvalidSourceAddress = 0x02,
    /// <summary>未知目标地址。<br/>Unknown target address.</summary>
    UnknownTargetAddress = 0x03,
    /// <summary>诊断消息过大。<br/>Diagnostic message too large.</summary>
    DiagnosticMessageTooLarge = 0x04,
    /// <summary>内存不足。<br/>Out of memory.</summary>
    OutOfMemory = 0x05,
    /// <summary>目标不可达。<br/>Target unreachable.</summary>
    TargetUnreachable = 0x06,
    /// <summary>未知网络。<br/>Unknown network.</summary>
    UnknownNetwork = 0x07,
    /// <summary>传输层协议错误。<br/>Transport protocol error.</summary>
    TransportProtocolError = 0x08,
}
