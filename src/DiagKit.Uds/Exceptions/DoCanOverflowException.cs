namespace DiagKit.Uds.Exceptions;

/// <summary>当流控制帧接收方指示缓冲区溢出时引发。<br/>Raised when the flow-control receiver signals Overflow (buffer too small).</summary>
public class DoCanOverflowException : ProtocolException
{
    /// <summary>创建默认的 DoCAN 溢出异常，使用预设消息 "DoCAN receiver signalled buffer overflow."。<br/>Creates a default DoCAN overflow exception with the preset message "DoCAN receiver signalled buffer overflow."</summary>
    public DoCanOverflowException() : base("DoCAN receiver signalled buffer overflow.") { }
    /// <inheritdoc/>
    public DoCanOverflowException(string message) : base(message) { }
    /// <inheritdoc/>
    public DoCanOverflowException(string message, System.Exception innerException) : base(message, innerException) { }
}
