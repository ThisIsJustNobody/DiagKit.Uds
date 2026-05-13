namespace DiagKit.Uds.UdsLayer;

/// <summary>
/// UDS 否定响应代码（ISO 14229-1 §A.1）。<br/>UDS negative response codes (ISO 14229-1 §A.1).
/// </summary>
public enum NegativeResponseCode : byte
{
    /// <summary>通用拒绝。<br/>General reject (0x10).</summary>
    GeneralReject = 0x10,

    /// <summary>服务不被服务器支持。<br/>Service is not supported by the server (0x11).</summary>
    ServiceNotSupported = 0x11,

    /// <summary>子功能不被支持。<br/>Sub-function is not supported (0x12).</summary>
    SubFunctionNotSupported = 0x12,

    /// <summary>消息长度错误或格式无效。<br/>Incorrect message length or invalid format (0x13).</summary>
    IncorrectMessageLengthOrInvalidFormat = 0x13,

    /// <summary>响应将超过最大消息长度。<br/>Response would exceed the maximum message length (0x14).</summary>
    ResponseTooLong = 0x14,

    /// <summary>忙——请重复请求。<br/>Busy — repeat request (0x21).</summary>
    BusyRepeatRequest = 0x21,

    /// <summary>当前条件不允许执行该服务。<br/>Current conditions do not permit the service (0x22).</summary>
    ConditionsNotCorrect = 0x22,

    /// <summary>请求序列错误。<br/>Request sequence error (0x24).</summary>
    RequestSequenceError = 0x24,

    /// <summary>子网组件无响应。<br/>No response from subnet component (0x25).</summary>
    NoResponseFromSubnetComponent = 0x25,

    /// <summary>故障阻止执行请求的操作。<br/>Failure prevents execution of the requested action (0x26).</summary>
    FailurePreventsExecutionOfRequestedAction = 0x26,

    /// <summary>参数超出允许范围。<br/>Parameter is out of the permitted range (0x31).</summary>
    RequestOutOfRange = 0x31,

    /// <summary>安全访问被拒绝。<br/>Security access denied (0x33).</summary>
    SecurityAccessDenied = 0x33,

    /// <summary>安全访问序列中提供的密钥无效。<br/>Invalid key provided in the security-access sequence (0x35).</summary>
    InvalidKey = 0x35,

    /// <summary>超出允许的安全访问尝试次数。<br/>Exceeded the number of allowed security-access attempts (0x36).</summary>
    ExceedNumberOfAttempts = 0x36,

    /// <summary>所需时间延迟后另一次安全访问尝试才被允许。<br/>Required time delay before another security-access attempt is permitted (0x37).</summary>
    RequiredTimeDelayNotExpired = 0x37,

    /// <summary>上传/下载不被接受。<br/>Upload/download not accepted (0x70).</summary>
    UploadDownloadNotAccepted = 0x70,

    /// <summary>数据传输已暂停。<br/>Transfer data suspended (0x71).</summary>
    TransferDataSuspended = 0x71,

    /// <summary>通用编程故障。<br/>General programming failure (0x72).</summary>
    GeneralProgrammingFailure = 0x72,

    /// <summary>传输中块序列计数器错误。<br/>Wrong block sequence counter in the transfer (0x73).</summary>
    WrongBlockSequenceCounter = 0x73,

    /// <summary>请求已正确接收——响应待处理。<br/>Request correctly received — response pending (0x78).</summary>
    RequestCorrectlyReceivedResponsePending = 0x78,

    /// <summary>当前会话不支持子功能。<br/>Sub-function not supported in the active session (0x7E).</summary>
    SubFunctionNotSupportedInActiveSession = 0x7E,

    /// <summary>当前会话不支持服务。<br/>Service not supported in the active session (0x7F).</summary>
    ServiceNotSupportedInActiveSession = 0x7F,
}
