using System;

using DiagKit.Uds.UdsLayer;

namespace DiagKit.Uds.Exceptions;

/// <summary>当 ECU 返回否定响应（0x7F）时引发。<br/>Raised when the ECU returns a negative response (0x7F).</summary>
public class NegativeResponseException : UdsException
{
    /// <summary>被拒绝的服务 ID。<br/>The service that was rejected.</summary>
    public byte ServiceId { get; }

    /// <summary>否定响应码（NRC）。<br/>The negative response code.</summary>
    public NegativeResponseCode Code { get; }

    /// <summary>创建默认的否定响应异常。<br/>Creates a default negative response exception.</summary>
    public NegativeResponseException() { }

    /// <inheritdoc/>
    public NegativeResponseException(string message) : base(message) { }

    /// <inheritdoc/>
    public NegativeResponseException(string message, Exception innerException) : base(message, innerException) { }

    /// <summary>使用被拒绝的服务 ID 和 NRC 创建否定响应异常。<br/>Creates a negative response exception from the rejected service ID and NRC.</summary>
    /// <param name="serviceId">被拒绝的服务 ID。<br/>The service ID that was rejected.</param>
    /// <param name="code">否定响应码。<br/>The negative response code.</param>
    public NegativeResponseException(byte serviceId, NegativeResponseCode code)
        : base($"Service 0x{serviceId:X2} rejected with NRC 0x{(byte)code:X2} ({code}).")
    {
        ServiceId = serviceId;
        Code = code;
    }

    /// <summary>如果响应字节序列是 7F 否定响应，则直接抛出异常；否则不执行任何操作。<br/>If <paramref name="response"/> is a negative response, throw; otherwise do nothing.</summary>
    /// <param name="response">待检查的 UDS 响应字节序列。<br/>The UDS response bytes to check.</param>
    public static void ThrowIfNegative(ReadOnlySpan<byte> response)
    {
        if (response.Length >= 3 && response[0] == 0x7F)
            throw new NegativeResponseException(response[1], (NegativeResponseCode)response[2]);
    }
}
