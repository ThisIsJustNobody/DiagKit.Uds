using System;

namespace DiagKit.Uds.CanHub;

/// <summary>
/// CanHub 桥接层异常。<br/>Exception raised by the CanHub bridge layer.
/// </summary>
public sealed class CanHubBridgeException : Exception
{
    /// <summary>
    /// 创建 CanHub 桥接层异常。<br/>Creates a CanHub bridge exception.
    /// </summary>
    public CanHubBridgeException()
    {
    }

    /// <summary>
    /// 使用错误消息创建 CanHub 桥接层异常。<br/>Creates a CanHub bridge exception with a message.
    /// </summary>
    public CanHubBridgeException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// 使用错误消息和内部异常创建 CanHub 桥接层异常。<br/>Creates a CanHub bridge exception with a message and inner exception.
    /// </summary>
    public CanHubBridgeException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// 使用 CanHub 发送提交结果创建异常。<br/>Creates an exception from a CanHub transmit submission result.
    /// </summary>
    public CanHubBridgeException(global::CanHub.CanTransmitSubmissionResult result)
        : base($"CanHub frame submission failed with status {result.Status}.")
    {
        CorrelationId = result.CorrelationId;
        Status = result.Status;
        NativeStatusCode = result.NativeStatusCode;
        NativeErrorCode = result.NativeErrorCode;
    }

    /// <summary>CanHub 发送关联 ID。<br/>CanHub transmit correlation ID.</summary>
    public ulong CorrelationId { get; }

    /// <summary>CanHub 发送提交状态。<br/>CanHub transmit submission status.</summary>
    public global::CanHub.CanTransmitSubmissionStatus Status { get; }

    /// <summary>原生状态码。<br/>Native status code.</summary>
    public uint NativeStatusCode { get; }

    /// <summary>原生错误码。<br/>Native error code.</summary>
    public uint NativeErrorCode { get; }
}
