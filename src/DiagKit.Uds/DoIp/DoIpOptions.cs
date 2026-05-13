using System;

namespace DiagKit.Uds.DoIp;

/// <summary>
/// DoIP（ISO 13400）客户端会话的配置参数。<br/>Configuration parameters for a DoIP (ISO 13400) client session.
/// </summary>
public sealed class DoIpOptions
{
    /// <summary>逻辑源地址（测试设备）。ISO 13400-2 Table 13: 0x0E00..0x0FFF。<br/>Logical source address (tester). ISO 13400-2 Table 13: 0x0E00..0x0FFF.</summary>
    public ushort SourceAddress { get; set; } = 0x0E00;

    /// <summary>逻辑目标地址（ECU）。必须显式配置。<br/>Logical target address (ECU). Must be configured explicitly.</summary>
    public ushort TargetAddress { get; set; }

    /// <summary>路由激活类型（ISO 13400-2 Table 46）。<br/>Routing activation type (ISO 13400-2 Table 46).</summary>
    public DoIpActivationType ActivationType { get; set; } = DoIpActivationType.Default;

    /// <summary>可选的路由激活 OEM 特定 4 字节字段。<br/>Optional OEM-specific 4-byte field for routing activation.</summary>
    public byte[]? OemSpecific { get; set; }

    /// <summary>
    /// 路由激活响应中期望的 DoIP 实体逻辑地址（可选）。
    /// 为 null 时仅验证测试设备地址，因为网关后的最终诊断目标地址可能不同于实体地址。<br/>
    /// Optional DoIP entity logical address expected in routing activation responses.
    /// When null, only the tester address is validated because the entity address may
    /// differ from the final diagnostic target behind a gateway.
    /// </summary>
    public ushort? EntityAddress { get; set; }

    /// <summary>最大可接受的 DoIP 载荷长度（字节），默认 4 MiB。<br/>Maximum accepted DoIP payload length in bytes. Default 4 MiB.</summary>
    public int MaxPayloadLength { get; set; } = 4 * 1024 * 1024;

    /// <summary>流式传输器可缓存的最大延迟入站消息数。<br/>Maximum number of deferred inbound messages stream transports may cache.</summary>
    public int InboxLimit { get; set; } = 16;

    /// <summary>
    /// 如果为 true，流式传输器将自动回复 AliveCheckRequest（0x0007），
    /// 回复携带 <see cref="SourceAddress"/> 的 AliveCheckResponse（0x0008）。<br/>
    /// If true, stream transports reply to AliveCheckRequest (0x0007) with an
    /// AliveCheckResponse (0x0008) carrying <see cref="SourceAddress"/>.
    /// </summary>
    public bool AutoRespondAliveCheck { get; set; } = true;

    // ─── 超时参数（ISO 13400-2 Table 17）<br/>Timeouts (ISO 13400-2 Table 17) ─────────────────────────────────

    /// <summary>T_TCP_General 通用超时（默认 5 秒）。<br/>T_TCP_General (default 5 s).</summary>
    public TimeSpan TcpGeneralTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>等待路由激活响应的超时时间，默认 2 秒。<br/>Timeout waiting for the routing-activation response. Default 2 s.</summary>
    public TimeSpan RoutingActivationTimeout { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>发送诊断消息后等待确认的超时时间，默认 2 秒。<br/>Timeout waiting for a diagnostic-message ack after sending. Default 2 s.</summary>
    public TimeSpan DiagnosticAckTimeout { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>等待实际诊断响应的超时时间，默认 5 秒。<br/>Timeout waiting for the actual diagnostic response. Default 5 s.</summary>
    public TimeSpan DiagnosticResponseTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// 如果为 true，<see cref="AsyncDoIpTransmitter"/> 将在 Send 返回前等待诊断确认（0x8002）。<br/>
    /// If true, <see cref="AsyncDoIpTransmitter"/> will wait for the diagnostic ack
    /// (0x8002) before returning from Send.
    /// </summary>
    public bool WaitForDiagnosticAck { get; set; } = true;

    /// <summary>
    /// 如果为 true，<see cref="AsyncDoIpTransmitter"/> 将在首次 Send/Receive 之前自动执行路由激活。<br/>
    /// If true, <see cref="AsyncDoIpTransmitter"/> will perform routing activation
    /// automatically before the first Send/Receive.
    /// </summary>
    public bool AutoActivate { get; set; } = true;

    /// <summary>创建这些选项的可变副本。<br/>Create a mutable copy of these options.</summary>
    public DoIpOptions Clone() => new()
    {
        SourceAddress = SourceAddress,
        TargetAddress = TargetAddress,
        ActivationType = ActivationType,
        OemSpecific = OemSpecific is null ? null : (byte[])OemSpecific.Clone(),
        EntityAddress = EntityAddress,
        MaxPayloadLength = MaxPayloadLength,
        InboxLimit = InboxLimit,
        AutoRespondAliveCheck = AutoRespondAliveCheck,
        TcpGeneralTimeout = TcpGeneralTimeout,
        RoutingActivationTimeout = RoutingActivationTimeout,
        DiagnosticAckTimeout = DiagnosticAckTimeout,
        DiagnosticResponseTimeout = DiagnosticResponseTimeout,
        WaitForDiagnosticAck = WaitForDiagnosticAck,
        AutoActivate = AutoActivate,
    };

    /// <summary>验证选项一致性，无效时抛出异常。<br/>Validate option coherence. Throws if invalid.</summary>
    public void Validate()
    {
        if (TargetAddress == 0)
            throw new ArgumentOutOfRangeException(nameof(TargetAddress), TargetAddress, "TargetAddress must be explicitly configured and non-zero.");
        if (SourceAddress == TargetAddress)
            throw new ArgumentException("SourceAddress and TargetAddress must differ.");
        if (SourceAddress < 0x0E00 || SourceAddress > 0x0FFF)
            throw new ArgumentOutOfRangeException(nameof(SourceAddress), SourceAddress, "SourceAddress must be in the tester range 0x0E00..0x0FFF.");
        if (!Enum.IsDefined(ActivationType))
            throw new ArgumentOutOfRangeException(nameof(ActivationType), ActivationType, "Invalid DoIP activation type.");
        if (OemSpecific is not null && OemSpecific.Length != 4)
            throw new ArgumentException("OemSpecific must be exactly 4 bytes when provided.", nameof(OemSpecific));
        if (MaxPayloadLength <= 0)
            throw new ArgumentOutOfRangeException(nameof(MaxPayloadLength), MaxPayloadLength, "MaxPayloadLength must be greater than zero.");
        if (InboxLimit <= 0)
            throw new ArgumentOutOfRangeException(nameof(InboxLimit), InboxLimit, "InboxLimit must be greater than zero.");

        ValidatePositiveTimeout(TcpGeneralTimeout, nameof(TcpGeneralTimeout));
        ValidatePositiveTimeout(RoutingActivationTimeout, nameof(RoutingActivationTimeout));
        ValidatePositiveTimeout(DiagnosticAckTimeout, nameof(DiagnosticAckTimeout));
        ValidatePositiveTimeout(DiagnosticResponseTimeout, nameof(DiagnosticResponseTimeout));
    }

    private static void ValidatePositiveTimeout(TimeSpan value, string paramName)
    {
        if (value <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(paramName, value, "Timeout must be greater than zero.");
    }
}
