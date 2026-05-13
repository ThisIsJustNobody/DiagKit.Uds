using System;

namespace DiagKit.Uds.Exceptions;

/// <summary>UDS / DoCAN / DoIP 异常的基类。<br/>Base class for UDS / DoCAN / DoIP errors raised by this library.</summary>
public class UdsException : Exception
{
    /// <summary>创建默认的 UDS 异常实例。<br/>Creates a default UDS exception.</summary>
    public UdsException() { }
    /// <inheritdoc/>
    public UdsException(string message) : base(message) { }
    /// <inheritdoc/>
    public UdsException(string message, Exception innerException) : base(message, innerException) { }
}
