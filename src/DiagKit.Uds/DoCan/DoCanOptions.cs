using System;

namespace DiagKit.Uds.DoCan;

/// <summary>
/// DoCAN（ISO 15765-2）传输层的配置参数。<br/>Configuration parameters for the DoCAN (ISO 15765-2) transport layer.
/// </summary>
/// <remarks>
/// 当前 DoCAN 实现仅支持 ISO 15765-2 普通寻址。扩展寻址和混合寻址格式不在本配置范围内。<br/>
/// The current DoCAN implementation supports ISO 15765-2 normal addressing only.
/// Extended and mixed addressing formats are out of scope for these options.
/// </remarks>
public sealed class DoCanOptions
{
    // ─── Addressing ──────────────────────────────────────────────────────

    /// <summary>
    /// 物理请求帧的 CAN ID（测试工具 → ECU）。<br/>CAN-ID for physical request frames (tester → ECU).
    /// </summary>
    public uint RequestId { get; set; }

    /// <summary>
    /// <see cref="RequestId"/> 是否为 29 位扩展标识符。<br/>Whether <see cref="RequestId"/> is a 29-bit extended identifier.
    /// </summary>
    public bool RequestIdExtended { get; set; }

    /// <summary>
    /// 响应帧的 CAN ID（ECU → 测试工具）。<br/>CAN-ID for response frames (ECU → tester).
    /// </summary>
    public uint ResponseId { get; set; }

    /// <summary>
    /// <see cref="ResponseId"/> 是否为 29 位扩展标识符。<br/>Whether <see cref="ResponseId"/> is a 29-bit extended identifier.
    /// </summary>
    public bool ResponseIdExtended { get; set; }

    // ─── CAN-FD ─────────────────────────────────────────────────────────

    /// <summary>
    /// 是否发送 CAN-FD 帧（否则使用经典 CAN）。<br/>Whether to transmit CAN-FD frames (otherwise classic CAN).
    /// </summary>
    public bool UseFd { get; set; }

    /// <summary>
    /// 是否启用比特率切换（仅 CAN-FD）。<br/>Whether bit-rate switching is enabled (CAN-FD only).
    /// </summary>
    public bool BrsEnabled { get; set; } = true;

    /// <summary>
    /// 与不匹配 FD 标志的帧的共存模式。<br/>Coexistence mode with non-matching FD frames.
    /// </summary>
    public DoCanFrameMixingMode FrameMixingMode { get; set; } = DoCanFrameMixingMode.Strict;

    // ─── Framing ────────────────────────────────────────────────────────

    /// <summary>
    /// 用于填充短帧的填充字节（默认 0xCC，汽车行业标准）。<br/>Padding byte used to fill short frames (default 0xCC, automotive standard).
    /// </summary>
    public byte PaddingValue { get; set; } = 0xCC;

    /// <summary>
    /// 发送帧的最小 DLC（经典 CAN 为 8，CAN-FD 最高可到 15）。
    /// 短普通寻址单帧（载荷不超过 7 字节）仍使用 CAN_DL = 8，因为 CAN_DL > 8 需要
    /// 至少为 8 的转义 SF_DL 值。<br/>
    /// Minimum DLC for transmitted frames (8 for classic CAN, up to 15 for CAN-FD).
    /// Short normal-addressing single frames with payloads up to 7 bytes still use
    /// CAN_DL = 8 because CAN_DL > 8 requires an escape SF_DL value of at least 8.
    /// </summary>
    public byte MinDlc { get; set; } = 8;

    /// <summary>
    /// 发送帧的最大 DLC。默认为 8（经典 CAN）；当 <see cref="UseFd"/> 为 true 时可设为 15
    /// 以利用 CAN-FD 的 64 字节帧容量。<br/>
    /// Maximum DLC for transmitted frames. Defaults to 8 (classic CAN); raise to 15
    /// when <see cref="UseFd"/> is true to take advantage of CAN-FD's 64-byte frames.
    /// </summary>
    public byte MaxDlc { get; set; } = 8;

    /// <summary>
    /// 若为 true，分段消息的每一帧使用相同的 DLC；否则最后一帧可能缩小以适应剩余字节。<br/>
    /// If true, every frame in a segmented message uses the same DLC. Otherwise
    /// the last consecutive frame may shrink to fit the remaining bytes.
    /// </summary>
    public bool FixedDlc { get; set; }

    // ─── Flow-control behaviour (receiver side) ─────────────────────────

    /// <summary>
    /// 在发出的流控帧中设置的块大小（0 = 无限制）。<br/>Block size sent in outgoing flow control frames (0 = unlimited).
    /// </summary>
    public byte BlockSize { get; set; } = 0;

    /// <summary>
    /// 在发出的流控帧中设置的 STmin（最小间隔时间）。<br/>STmin (separation time) sent in outgoing flow control frames.
    /// </summary>
    public byte STmin { get; set; } = 0;

    // ─── Timing ─────────────────────────────────────────────────────────

    /// <summary>
    /// 帧发送超时（发送方）。ISO 参数：N_As。<br/>Timeout for frame transmission (sender side). ISO: N_As.
    /// </summary>
    public TimeSpan TimeoutAs { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Receiver-side CAN N-PDU transmission timeout, typically for flow-control frames. ISO: N_Ar.
    /// </summary>
    public TimeSpan TimeoutAr { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Standalone receive timeout for the first matching SF/FF. If null, <see cref="TimeoutAr"/> is used for backward compatibility.
    /// </summary>
    public TimeSpan? ReceiveStartTimeout { get; set; }

    /// <summary>
    /// 等待下一个流控帧超时。ISO 参数：N_Bs。<br/>Timeout waiting for the next flow control frame. ISO: N_Bs.
    /// </summary>
    public TimeSpan TimeoutBs { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// 发送本方流控帧前的延迟。ISO 参数：N_Br。<br/>Delay before emitting our own flow control frame. ISO: N_Br.
    /// </summary>
    public TimeSpan TimeBr { get; set; } = TimeSpan.Zero;

    /// <summary>
    /// 发送下一个连续帧前的延迟。ISO 参数：N_Cs。<br/>Delay before transmitting the next consecutive frame. ISO: N_Cs.
    /// </summary>
    public TimeSpan TimeCs { get; set; } = TimeSpan.Zero;

    /// <summary>
    /// 等待下一个连续帧超时。ISO 参数：N_Cr。<br/>Timeout waiting for the next consecutive frame. ISO: N_Cr.
    /// </summary>
    public TimeSpan TimeoutCr { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// 发送方收到 FlowStatus = Wait 后的回退等待间隔。<br/>Back-off interval while the sender responds with FlowStatus = Wait.
    /// </summary>
    public TimeSpan FlowControlWaitInterval { get; set; } = TimeSpan.FromMilliseconds(10);

    /// <summary>
    /// 在分段发送中止之前允许接收的最大 FlowStatus = Wait 帧数。<br/>Maximum FlowStatus = Wait frames accepted before aborting a segmented send.
    /// </summary>
    public int MaxFlowControlWaitFrames { get; set; } = 8;

    /// <summary>
    /// 从接收到的首帧中接受的最大分段消息长度。默认 4 MiB。<br/>Maximum segmented message length accepted from an incoming First Frame. Default 4 MiB.
    /// </summary>
    public int MaxSegmentedPayloadLength { get; set; } = 4 * 1024 * 1024;

    /// <summary>
    /// 创建这些选项的可变副本。<br/>Create a mutable copy of these options.
    /// </summary>
    /// <returns>当前选项的独立副本。<br/>An independent copy of the current options.</returns>
    public DoCanOptions Clone() => new()
    {
        RequestId = RequestId,
        RequestIdExtended = RequestIdExtended,
        ResponseId = ResponseId,
        ResponseIdExtended = ResponseIdExtended,
        UseFd = UseFd,
        BrsEnabled = BrsEnabled,
        FrameMixingMode = FrameMixingMode,
        PaddingValue = PaddingValue,
        MinDlc = MinDlc,
        MaxDlc = MaxDlc,
        FixedDlc = FixedDlc,
        BlockSize = BlockSize,
        STmin = STmin,
        TimeoutAs = TimeoutAs,
        TimeoutAr = TimeoutAr,
        ReceiveStartTimeout = ReceiveStartTimeout,
        TimeoutBs = TimeoutBs,
        TimeBr = TimeBr,
        TimeCs = TimeCs,
        TimeoutCr = TimeoutCr,
        FlowControlWaitInterval = FlowControlWaitInterval,
        MaxFlowControlWaitFrames = MaxFlowControlWaitFrames,
        MaxSegmentedPayloadLength = MaxSegmentedPayloadLength,
    };

    /// <summary>
    /// 验证选项的一致性。无效时抛出异常。<br/>Validate option coherence. Throws if invalid.
    /// </summary>
    public void Validate()
    {
        ValidateCanId(RequestId, RequestIdExtended, nameof(RequestId));
        ValidateCanId(ResponseId, ResponseIdExtended, nameof(ResponseId));

        if (MinDlc > MaxDlc) throw new ArgumentException($"MinDlc ({MinDlc}) > MaxDlc ({MaxDlc})");
        if (MinDlc < 8) throw new ArgumentException($"MinDlc must be ≥ 8 (was {MinDlc})");
        if (MaxDlc > 15) throw new ArgumentException($"MaxDlc must be ≤ 15 (was {MaxDlc})");
        if (!UseFd && MaxDlc > 8) throw new ArgumentException("Classic CAN does not support DLC > 8.");
        if (!Enum.IsDefined(FrameMixingMode))
            throw new ArgumentOutOfRangeException(nameof(FrameMixingMode), FrameMixingMode, "Invalid DoCAN frame mixing mode.");

        ValidatePositiveTimeout(TimeoutAs, nameof(TimeoutAs));
        ValidatePositiveTimeout(TimeoutAr, nameof(TimeoutAr));
        if (ReceiveStartTimeout.HasValue)
            ValidatePositiveTimeout(ReceiveStartTimeout.Value, nameof(ReceiveStartTimeout));
        ValidatePositiveTimeout(TimeoutBs, nameof(TimeoutBs));
        ValidatePositiveTimeout(TimeoutCr, nameof(TimeoutCr));
        ValidateNonNegative(TimeBr, nameof(TimeBr));
        ValidateNonNegative(TimeCs, nameof(TimeCs));
        ValidateNonNegative(FlowControlWaitInterval, nameof(FlowControlWaitInterval));
        if (MaxFlowControlWaitFrames < 0)
            throw new ArgumentOutOfRangeException(nameof(MaxFlowControlWaitFrames), MaxFlowControlWaitFrames, "MaxFlowControlWaitFrames must be non-negative.");
        if (MaxSegmentedPayloadLength <= 0)
            throw new ArgumentOutOfRangeException(nameof(MaxSegmentedPayloadLength), MaxSegmentedPayloadLength, "MaxSegmentedPayloadLength must be greater than zero.");
    }

    private static void ValidateCanId(uint canId, bool extendedId, string paramName)
    {
        try
        {
            CanFrame.Validate(canId, dataLength: 0, extendedId: extendedId);
        }
        catch (ArgumentOutOfRangeException ex) when (ex.ParamName == "canId")
        {
            throw new ArgumentOutOfRangeException(paramName, canId, ex.Message);
        }
    }

    private static void ValidatePositiveTimeout(TimeSpan value, string paramName)
    {
        if (value <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(paramName, value, "Timeout must be greater than zero.");
    }

    private static void ValidateNonNegative(TimeSpan value, string paramName)
    {
        if (value < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(paramName, value, "TimeSpan value must be non-negative.");
    }
}
