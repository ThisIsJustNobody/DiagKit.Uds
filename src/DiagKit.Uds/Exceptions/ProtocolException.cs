using System;

namespace DiagKit.Uds.Exceptions;

/// <summary>当底层协议状态机处理失败时引发。<br/>Raised when the underlying protocol state machine fails.</summary>
public class ProtocolException : UdsException
{
    /// <inheritdoc/>
    public ProtocolException(string message) : base(message) { }
    /// <inheritdoc/>
    public ProtocolException(string message, Exception innerException) : base(message, innerException) { }
}
