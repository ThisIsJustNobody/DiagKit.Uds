namespace DiagKit.Uds.UdsLayer;

/// <summary>
/// UDS 应用层对 RC 0x78（ResponsePending）的行为选择器。<br/>UDS application-layer behaviour selector for RC 0x78 (ResponsePending).
/// </summary>
public enum Rc78Handling
{
    /// <summary>立即将 RC 0x78 响应返回给调用方，不再等待。<br/>Return the RC 0x78 response to the caller without further waiting.</summary>
    ReturnImmediately,

    /// <summary>继续等待（不超过 <see cref="UdsOptions.Rc78CompletionTimeout"/>）直到最终响应。<br/>Keep waiting (up to <see cref="UdsOptions.Rc78CompletionTimeout"/>) for the final response.</summary>
    WaitForCompletion,
}
