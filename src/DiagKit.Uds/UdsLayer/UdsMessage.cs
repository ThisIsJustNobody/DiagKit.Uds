using System;

namespace DiagKit.Uds.UdsLayer;

/// <summary>
/// UDS 消息布局的静态辅助方法。<br/>Static helpers for UDS message layout.
/// </summary>
public static class UdsMessage
{
    /// <summary>正响应偏移量（服务ID + 0x40）。<br/>The positive-response offset (service ID + 0x40).</summary>
    public const byte PositiveResponseOffset = 0x40;

    /// <summary>
    /// 如果请求携带 suppressPositiveResponse 标志（子功能字节的第 7 位），则返回 true。
    /// 仅对实际具有子功能字节的服务有效；基于 DID 的服务（0x22、0x2E 等）始终返回 false。
    /// <br/>Return true if the request carries the suppressPositiveResponse flag (bit 7
    /// of the sub-function byte). Only considered for services that actually have
    /// a sub-function byte; DID-based services (0x22, 0x2E, etc.) always return false.
    /// </summary>
    /// <param name="request">UDS 请求字节序列。<br/>The UDS request bytes.</param>
    /// <returns>如果请求抑制正响应则为 true。<br/>True if the request suppresses positive response.</returns>
    public static bool IsSuppressPositiveResponse(ReadOnlySpan<byte> request)
        => request.Length >= 2 && HasSubFunction(request[0]) && (request[1] & 0x80) != 0;

    /// <summary>如果给定的服务 ID 在偏移量 1 处具有子功能字节，则返回 true。<br/>True if the given service ID has a sub-function byte at offset 1.</summary>
    /// <param name="serviceId">UDS 服务标识符。<br/>The UDS service identifier.</param>
    /// <returns>如果该服务具有子功能字节则为 true。<br/>True if the service has a sub-function byte.</returns>
    public static bool HasSubFunction(byte serviceId) => serviceId switch
    {
        // Only services whose byte 1 is a true sub-function may carry suppressPositiveResponse.
        // Services such as 0x14, 0x2F, and 0x34-0x37 use byte 1 for other parameters.
        0x10 or 0x11 or 0x27 or 0x28 or 0x29 or 0x2C or 0x3E or 0x83 or 0x85 or 0x86 or 0x87 or 0x31 or 0x19 => true,
        _ => false,
    };

    /// <summary>返回请求或响应中的原始服务标识符字节。<br/>Return the raw service identifier byte from a request or response.</summary>
    /// <param name="message">UDS 消息字节序列。<br/>The UDS message bytes.</param>
    /// <returns>服务标识符字节；如果消息为空则返回 0。<br/>The service ID byte, or 0 if the message is empty.</returns>
    public static byte GetServiceId(ReadOnlySpan<byte> message)
        => message.IsEmpty ? (byte)0 : message[0];

    /// <summary>
    /// 尝试读取请求子功能值，已移除第 7 位 suppressPositiveResponse 标志。<br/>Try to read the request sub-function value with suppressPositiveResponse bit 7 removed.
    /// </summary>
    /// <param name="request">UDS 请求字节序列。<br/>The UDS request bytes.</param>
    /// <param name="subFunction">输出：移除第 7 位后的子功能值。<br/>Out: the sub-function value with bit 7 removed.</param>
    /// <returns>如果成功读取子功能则为 true。<br/>True if the sub-function was successfully read.</returns>
    public static bool TryGetSubFunction(ReadOnlySpan<byte> request, out byte subFunction)
    {
        subFunction = 0;
        if (request.Length < 2 || !HasSubFunction(request[0])) return false;
        subFunction = (byte)(request[1] & 0x7F);
        return true;
    }

    /// <summary>如果给定响应为否定响应，则返回 true。<br/>Return true if the given response is a negative response.</summary>
    /// <param name="response">UDS 响应字节序列。<br/>The UDS response bytes.</param>
    /// <returns>如果响应为否定响应（SID=0x7F 且长度>=3）则为 true。<br/>True if the response is a negative response (SID=0x7F and length>=3).</returns>
    public static bool IsNegativeResponse(ReadOnlySpan<byte> response)
        => response.Length >= 3 && response[0] == 0x7F;

    /// <summary>如果响应服务 ID 是请求服务 ID 的正响应对应值，则返回 true。<br/>Return true if the response service ID is the positive counterpart of the request service ID.</summary>
    /// <param name="request">UDS 请求字节序列。<br/>The UDS request bytes.</param>
    /// <param name="response">UDS 响应字节序列。<br/>The UDS response bytes.</param>
    /// <returns>如果响应 SID 等于请求 SID + 0x40 则为 true。<br/>True if the response SID equals request SID + 0x40.</returns>
    public static bool IsPositiveResponseFor(ReadOnlySpan<byte> request, ReadOnlySpan<byte> response)
    {
        if (request.IsEmpty || response.IsEmpty) return false;
        int expected = request[0] + PositiveResponseOffset;
        return expected <= byte.MaxValue && response[0] == expected;
    }

    /// <summary>如果否定响应引用的是给定请求的服务 ID，则返回 true。<br/>Return true if the negative response references the service ID of the given request.</summary>
    /// <param name="request">UDS 请求字节序列。<br/>The UDS request bytes.</param>
    /// <param name="response">UDS 响应字节序列。<br/>The UDS response bytes.</param>
    /// <returns>如果响应为否定响应且字节 1 匹配请求 SID 则为 true。<br/>True if the response is a negative response and byte 1 matches the request SID.</returns>
    public static bool IsNegativeResponseFor(ReadOnlySpan<byte> request, ReadOnlySpan<byte> response)
    {
        if (request.IsEmpty || response.Length < 3 || response[0] != 0x7F) return false;
        return response[1] == request[0];
    }
}
