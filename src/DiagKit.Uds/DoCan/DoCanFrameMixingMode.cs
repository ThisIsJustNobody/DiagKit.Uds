namespace DiagKit.Uds.DoCan;

/// <summary>
/// 同一总线上经典 CAN 与 CAN-FD 帧共存的混合模式。<br/>Mixing mode for coexistence of classic CAN and CAN-FD frames on the same bus.
/// </summary>
public enum DoCanFrameMixingMode
{
    /// <summary>
    /// 严格模式：丢弃 FD 标志与配置模式不匹配的帧。<br/>Strict: Discard frames whose FD flag does not match the configured mode.
    /// </summary>
    Strict,

    /// <summary>
    /// 接受模式：接受任意 FD 模式的帧；以配置模式回复。<br/>Accept: Accept frames of either FD mode; reply with the configured mode.
    /// </summary>
    Accept,

    /// <summary>
    /// 自适应模式：接受任意 FD 模式的帧；以最后接收帧的模式回复。<br/>Adapt: Accept frames of either FD mode; reply in the mode of the last received frame.
    /// </summary>
    Adapt,
}
