using System;

namespace DiagKit.Uds.UdsLayer;

/// <summary>
/// <see cref="UdsClient"/> / <see cref="AsyncUdsClient"/> 的配置。<br/>Configuration for <see cref="UdsClient"/> / <see cref="AsyncUdsClient"/>.
/// </summary>
public sealed class UdsOptions
{
    // ─── Timing (ISO 14229-1 §7.2) ──────────────────────────────────────

    /// <summary>P2 client：等待响应的默认超时时间。<br/>P2 client: default timeout waiting for a response. (150 ms)</summary>
    public TimeSpan P2Client { get; set; } = TimeSpan.FromMilliseconds(150);

    /// <summary>P2* client：RC 0x78 后的扩展超时时间。<br/>P2* client: extended timeout after RC 0x78. (5000 ms)</summary>
    public TimeSpan P2ClientExtended { get; set; } = TimeSpan.FromMilliseconds(5000);

    /// <summary>S3 client：TesterPresent 保活周期。<br/>S3 client: TesterPresent keep-alive period. (2000 ms)</summary>
    public TimeSpan S3Client { get; set; } = TimeSpan.FromMilliseconds(2000);

    // ─── Behaviour flags ────────────────────────────────────────────────

    /// <summary>ECU 返回 RC 0x78 时的处理策略（默认：等待完成）。<br/>Strategy when the ECU returns RC 0x78 (default: wait).</summary>
    public Rc78Handling Rc78Handling { get; set; } = Rc78Handling.WaitForCompletion;

    /// <summary>重试 RC 0x78 的总时间预算，超时则放弃。默认 25 秒。<br/>Total time budget while retrying RC 0x78 before giving up. Default 25 s.</summary>
    public TimeSpan Rc78CompletionTimeout { get; set; } = TimeSpan.FromSeconds(25);

    /// <summary>ECU 返回 RC 0x21 时的处理策略（默认：返回给调用方）。<br/>Strategy when the ECU returns RC 0x21 (default: return to caller).</summary>
    public Rc21Handling Rc21Handling { get; set; } = Rc21Handling.ReturnImmediately;

    /// <summary>RC 0x21 重试模式下重新发送前的延迟。<br/>Delay before resending after RC 0x21 (Retry mode).</summary>
    public TimeSpan Rc21RetryInterval { get; set; } = TimeSpan.FromMilliseconds(200);

    /// <summary>重试 RC 0x21 的总时间预算，超时则放弃。<br/>Total time budget while retrying RC 0x21 before giving up.</summary>
    public TimeSpan Rc21CompletionTimeout { get; set; } = TimeSpan.FromMilliseconds(1300);

    /// <summary>
    /// 当请求要求 ECU 抑制正响应时，客户端是否仍应短暂等待以捕获否定响应？<br/>
    /// When a request asks the ECU to suppress the positive response, should the
    /// client still wait briefly to catch a negative response?
    /// </summary>
    public bool WaitWhileSuppressingResponse { get; set; } = true;

    /// <summary>
    /// 如果为 true，服务 ID 与请求不匹配的响应被视为协议错误。如果为 false，则静默丢弃
    /// 并继续等待匹配的响应。<br/>
    /// If true, responses whose service-ID does not match the request are treated
    /// as a protocol error. If false, they are silently discarded and the client
    /// continues to wait for a matching response.
    /// </summary>
    public bool StrictServiceIdMatching { get; set; } = false;

    /// <summary>
    /// 每次 UDS 请求发送前是否自动清空底层接收缓存。<br/>
    /// Whether to clear the underlying receive buffer before each UDS request. Default true.
    /// </summary>
    public bool ClearReceiveBufferBeforeRequest { get; set; } = true;

    /// <summary>
    /// UDS 请求生命周期钩子。参数 true 表示请求事务开始，false 表示结束/清理。<br/>
    /// Request lifecycle hook. true means transaction start; false means transaction end/cleanup.
    /// </summary>
    public Action<bool>? InitializeOrClearUpAction { get; set; }

    /// <summary>创建这些选项的可变副本。<br/>Create a mutable copy of these options.</summary>
    /// <returns>一个新的 <see cref="UdsOptions"/> 实例，值与此实例相同。<br/>A new <see cref="UdsOptions"/> instance with the same values.</returns>
    public UdsOptions Clone() => new()
    {
        P2Client = P2Client,
        P2ClientExtended = P2ClientExtended,
        S3Client = S3Client,
        Rc78Handling = Rc78Handling,
        Rc78CompletionTimeout = Rc78CompletionTimeout,
        Rc21Handling = Rc21Handling,
        Rc21RetryInterval = Rc21RetryInterval,
        Rc21CompletionTimeout = Rc21CompletionTimeout,
        WaitWhileSuppressingResponse = WaitWhileSuppressingResponse,
        StrictServiceIdMatching = StrictServiceIdMatching,
        ClearReceiveBufferBeforeRequest = ClearReceiveBufferBeforeRequest,
        InitializeOrClearUpAction = InitializeOrClearUpAction,
    };

    /// <summary>验证选项一致性。如果无效则抛出异常。<br/>Validate option coherence. Throws if invalid.</summary>
    public void Validate()
    {
        ValidatePositiveTimeout(P2Client, nameof(P2Client));
        ValidatePositiveTimeout(P2ClientExtended, nameof(P2ClientExtended));
        ValidatePositiveTimeout(S3Client, nameof(S3Client));
        ValidatePositiveTimeout(Rc78CompletionTimeout, nameof(Rc78CompletionTimeout));
        ValidateNonNegative(Rc21RetryInterval, nameof(Rc21RetryInterval));
        ValidatePositiveTimeout(Rc21CompletionTimeout, nameof(Rc21CompletionTimeout));

        if (P2ClientExtended < P2Client)
            throw new ArgumentException("P2ClientExtended must be greater than or equal to P2Client.");
        if (!Enum.IsDefined(Rc78Handling))
            throw new ArgumentOutOfRangeException(nameof(Rc78Handling), Rc78Handling, "Invalid RC 0x78 handling mode.");
        if (!Enum.IsDefined(Rc21Handling))
            throw new ArgumentOutOfRangeException(nameof(Rc21Handling), Rc21Handling, "Invalid RC 0x21 handling mode.");
        if (Rc21Handling == Rc21Handling.Retry && Rc21RetryInterval >= Rc21CompletionTimeout)
            throw new ArgumentException("Rc21RetryInterval must be shorter than Rc21CompletionTimeout in retry mode.");
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
