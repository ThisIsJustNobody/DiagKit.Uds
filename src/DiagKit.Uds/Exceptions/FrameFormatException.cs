namespace DiagKit.Uds.Exceptions;

/// <summary>当接收的帧无法解析为有效的 ISO 15765 PDU 时引发。<br/>Raised when a received frame does not parse into a valid ISO 15765 PDU.</summary>
public class FrameFormatException : ProtocolException
{
    /// <summary>创建默认的帧格式异常，使用预设消息 "Received frame has an invalid format."。<br/>Creates a default frame format exception with the preset message "Received frame has an invalid format."</summary>
    public FrameFormatException() : base("Received frame has an invalid format.") { }
    /// <inheritdoc/>
    public FrameFormatException(string message) : base(message) { }
    /// <inheritdoc/>
    public FrameFormatException(string message, System.Exception innerException) : base(message, innerException) { }
}
