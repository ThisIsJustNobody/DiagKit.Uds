namespace DiagKit.Uds.DoCan;

/// <summary>
/// ISO 15765-2 流控帧状态码。<br/>ISO 15765-2 flow status codes for flow-control frames.
/// </summary>
public enum DoCanFlowStatus : byte
{
    /// <summary>
    /// 继续发送 — 发送方可传输下一个数据块。<br/>ClearToSend — sender may transmit the next block.
    /// </summary>
    Continue = 0,

    /// <summary>
    /// 等待 — 发送方应等待下一个流控帧。<br/>Wait — sender should wait for another flow control frame.
    /// </summary>
    Wait = 1,

    /// <summary>
    /// 溢出/中止 — 消息超出接收方缓冲区容量。<br/>Overflow/Abort — message exceeds receiver's buffer capacity.
    /// </summary>
    Overflow = 2,

    /// <summary>
    /// 保留值。<br/>Reserved.
    /// </summary>
    Reserved = 0xFF,
}
