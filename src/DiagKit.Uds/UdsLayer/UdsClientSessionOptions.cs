using System;

namespace DiagKit.Uds.UdsLayer;

/// <summary>
/// <see cref="UdsClientSession"/> 的配置。<br/>Configuration for <see cref="UdsClientSession"/>.
/// </summary>
public sealed class UdsClientSessionOptions
{
    private TimeSpan? _testerPresentInterval;

    /// <summary>启用会话管理的 SID 0x3E TesterPresent 保活。默认 false。<br/>Enable session-managed SID 0x3E TesterPresent keep-alive. Default false.</summary>
    public bool TesterPresentEnabled { get; set; }

    /// <summary>
    /// S3 client / TesterPresent 周期。为 null 时使用包装客户端的 <see cref="UdsOptions.S3Client"/>。<br/>
    /// S3 client / TesterPresent period. When null, the wrapped client's
    /// <see cref="UdsOptions.S3Client"/> is used.
    /// </summary>
    public TimeSpan? TesterPresentInterval
    {
        get => _testerPresentInterval;
        set => _testerPresentInterval = value;
    }

    /// <summary>
    /// <see cref="TesterPresentInterval"/> 的别名。<br/>Alias for <see cref="TesterPresentInterval"/>.
    /// </summary>
    public TimeSpan? S3Client
    {
        get => _testerPresentInterval;
        set => _testerPresentInterval = value;
    }

    /// <summary>对会话管理的 TesterPresent 设置 suppressPositiveResponse。默认 true。<br/>Set suppressPositiveResponse on session-managed TesterPresent. Default true.</summary>
    public bool SuppressTesterPresentPositiveResponse { get; set; } = true;

    /// <summary>FIFO 队列中等待的最大业务请求数。默认 1024。<br/>Maximum number of business requests waiting in the FIFO queue. Default 1024.</summary>
    public int MaxQueueLength { get; set; } = 1024;

    /// <summary>达到 <see cref="MaxQueueLength"/> 时的行为。默认 Throw。<br/>Behaviour when <see cref="MaxQueueLength"/> is reached. Default Throw.</summary>
    public UdsClientSessionQueueFullMode QueueFullMode { get; set; } = UdsClientSessionQueueFullMode.Throw;

    /// <summary>
    /// 每当业务请求被提交到包装客户端时，刷新本地 S3 客户端时钟。默认 true。<br/>
    /// Refresh the local S3 client clock whenever a business request is handed to
    /// the wrapped client. Default true.
    /// </summary>
    public bool ResetS3OnAnyRequest { get; set; } = true;

    /// <summary>创建这些选项的可变副本。<br/>Create a mutable copy of these options.</summary>
    /// <returns>一个新的 <see cref="UdsClientSessionOptions"/> 实例，值与此实例相同。<br/>A new <see cref="UdsClientSessionOptions"/> instance with the same values.</returns>
    public UdsClientSessionOptions Clone() => new()
    {
        TesterPresentEnabled = TesterPresentEnabled,
        TesterPresentInterval = TesterPresentInterval,
        SuppressTesterPresentPositiveResponse = SuppressTesterPresentPositiveResponse,
        MaxQueueLength = MaxQueueLength,
        QueueFullMode = QueueFullMode,
        ResetS3OnAnyRequest = ResetS3OnAnyRequest,
    };

    /// <summary>验证选项一致性。如果无效则抛出异常。<br/>Validate option coherence. Throws if invalid.</summary>
    public void Validate()
    {
        if (MaxQueueLength <= 0)
            throw new ArgumentOutOfRangeException(nameof(MaxQueueLength), MaxQueueLength, "MaxQueueLength must be greater than zero.");
        if (TesterPresentInterval is { } interval && interval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(TesterPresentInterval), interval, "TesterPresentInterval must be greater than zero.");
        if (!Enum.IsDefined(QueueFullMode))
            throw new ArgumentOutOfRangeException(nameof(QueueFullMode), QueueFullMode, "Invalid queue-full mode.");
    }
}
