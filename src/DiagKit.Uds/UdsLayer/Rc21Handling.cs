namespace DiagKit.Uds.UdsLayer;

/// <summary>
/// UDS 应用层对 RC 0x21（BusyRepeatRequest）的行为选择器。<br/>UDS application-layer behaviour selector for RC 0x21 (BusyRepeatRequest).
/// </summary>
public enum Rc21Handling
{
    /// <summary>将 RC 0x21 响应返回给调用方，不重试。<br/>Return the RC 0x21 response to the caller without retry.</summary>
    ReturnImmediately,

    /// <summary>重新发送原始请求（不超过 <see cref="UdsOptions.Rc21CompletionTimeout"/>）。<br/>Resend the original request (up to <see cref="UdsOptions.Rc21CompletionTimeout"/>).</summary>
    Retry,
}
