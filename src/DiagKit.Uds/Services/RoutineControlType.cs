namespace DiagKit.Uds.Services;

/// <summary>
/// 例程控制子功能（ISO 14229-1 §10.5）。<br/>Routine control sub-functions (ISO 14229-1 §10.5).
/// </summary>
public enum RoutineControlType : byte
{
    /// <summary>启动例程。<br/>Start routine (0x01).</summary>
    StartRoutine = 0x01,
    /// <summary>停止例程。<br/>Stop routine (0x02).</summary>
    StopRoutine = 0x02,
    /// <summary>请求例程执行结果。<br/>Request routine results (0x03).</summary>
    RequestRoutineResults = 0x03,
}
