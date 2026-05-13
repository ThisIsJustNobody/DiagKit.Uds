namespace DiagKit.Uds.DoIp;

/// <summary>
/// DoIP 载荷类型码（ISO 13400-2 §5.5.1.4）。<br/>DoIP payload type codes (ISO 13400-2 §5.5.1.4).
/// </summary>
public enum DoIpPayloadType : ushort
{
    /// <summary>通用头部否定确认。<br/>Generic DoIP header negative acknowledge.</summary>
    GenericHeaderNegativeAcknowledge = 0x0000,

    /// <summary>车辆识别请求。<br/>Vehicle identification request.</summary>
    VehicleIdentificationRequest = 0x0001,

    /// <summary>带EID的车辆识别请求。<br/>Vehicle identification request with EID.</summary>
    VehicleIdentificationRequestEid = 0x0002,

    /// <summary>带VIN的车辆识别请求。<br/>Vehicle identification request with VIN.</summary>
    VehicleIdentificationRequestVin = 0x0003,

    /// <summary>车辆公告/识别响应。<br/>Vehicle announcement / identification response.</summary>
    VehicleAnnouncementMessage = 0x0004,

    /// <summary>路由激活请求。<br/>Routing activation request.</summary>
    RoutingActivationRequest = 0x0005,

    /// <summary>路由激活响应。<br/>Routing activation response.</summary>
    RoutingActivationResponse = 0x0006,

    /// <summary>保活检查请求。<br/>Alive-check request.</summary>
    AliveCheckRequest = 0x0007,

    /// <summary>保活检查响应。<br/>Alive-check response.</summary>
    AliveCheckResponse = 0x0008,

    /// <summary>DoIP实体状态请求。<br/>DoIP entity status request.</summary>
    DoIpEntityStatusRequest = 0x4001,

    /// <summary>DoIP实体状态响应。<br/>DoIP entity status response.</summary>
    DoIpEntityStatusResponse = 0x4002,

    /// <summary>诊断电源模式信息请求。<br/>Diagnostic power-mode information request.</summary>
    DiagnosticPowerModeInformationRequest = 0x4003,

    /// <summary>诊断电源模式信息响应。<br/>Diagnostic power-mode information response.</summary>
    DiagnosticPowerModeInformationResponse = 0x4004,

    /// <summary>诊断消息（UDS负载）。<br/>Diagnostic message (UDS payload).</summary>
    DiagnosticMessage = 0x8001,

    /// <summary>诊断消息肯定确认。<br/>Diagnostic message positive acknowledgment.</summary>
    DiagnosticMessagePositiveAck = 0x8002,

    /// <summary>诊断消息否定确认。<br/>Diagnostic message negative acknowledgment.</summary>
    DiagnosticMessageNegativeAck = 0x8003,
}
