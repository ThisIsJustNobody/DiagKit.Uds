namespace DiagKit.Uds.DoCan;

/// <summary>
/// ISO 15765-2 协议控制信息帧类型。<br/>ISO 15765-2 protocol control information frame types.
/// </summary>
public enum DoCanFrameType : byte
{
    /// <summary>
    /// 单帧（PCI = 0x0）。<br/>Single frame (PCI = 0x0).
    /// </summary>
    SingleFrame = 0,

    /// <summary>
    /// 首帧（PCI = 0x1），多帧分段消息的起始帧。<br/>First frame (PCI = 0x1), start of a multi-frame segmented message.
    /// </summary>
    FirstFrame = 1,

    /// <summary>
    /// 连续帧（PCI = 0x2），分段消息的后续帧。<br/>Consecutive frame (PCI = 0x2), continuation of a segmented message.
    /// </summary>
    ConsecutiveFrame = 2,

    /// <summary>
    /// 流控帧（PCI = 0x3），接收方到发送方的反压控制。<br/>Flow control frame (PCI = 0x3), receiver-to-sender backpressure.
    /// </summary>
    FlowControlFrame = 3,

    /// <summary>
    /// 保留/无效帧类型。<br/>Reserved / invalid frame type.
    /// </summary>
    Reserved = 0xFF,
}
