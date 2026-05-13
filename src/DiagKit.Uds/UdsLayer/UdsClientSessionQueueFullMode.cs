namespace DiagKit.Uds.UdsLayer;

/// <summary>
/// <see cref="UdsClientSession"/> 请求队列满时的行为。<br/>Behaviour when the <see cref="UdsClientSession"/> request queue is full.
/// </summary>
public enum UdsClientSessionQueueFullMode
{
    /// <summary>立即拒绝新请求。<br/>Reject the new request immediately.</summary>
    Throw,

    /// <summary>等待直到队列有空闲容量。<br/>Wait until queue capacity is available.</summary>
    Wait,
}
