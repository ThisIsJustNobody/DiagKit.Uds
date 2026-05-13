using System;

namespace DiagKit.Uds.UdsLayer;

/// <summary>
/// ECU 仿真器响应步骤。可用于脚本化多响应、延迟响应或无响应。<br/>ECU simulator response step. Can be used to script multi-step responses, delayed responses, or no-response.
/// </summary>
public readonly record struct UdsServerResponse
{
    /// <summary>要发送的完整 UDS payload；空表示本步骤不发送。<br/>The full UDS payload to send; empty means no payload is sent in this step.</summary>
    public ReadOnlyMemory<byte> Payload { get; }

    /// <summary>发送前延迟。<br/>Delay before sending.</summary>
    public TimeSpan Delay { get; }

    /// <summary>
    /// 创建一个 UDS 服务器响应步骤。<br/>Creates a UDS server response step.
    /// </summary>
    /// <param name="payload">要发送的完整 UDS payload。<br/>The full UDS payload to send.</param>
    /// <param name="delay">发送前的可选延迟。<br/>Optional delay before sending.</param>
    public UdsServerResponse(ReadOnlyMemory<byte> payload, TimeSpan delay = default)
    {
        if (delay < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(delay), delay, "Delay must be non-negative.");
        Payload = payload.ToArray();
        Delay = delay;
    }

    /// <summary>创建无响应步骤。<br/>Create a no-response step.</summary>
    /// <param name="delay">等待延迟。<br/>Delay to wait.</param>
    /// <returns>一个不发送任何 payload 的响应步骤。<br/>A response step that sends no payload.</returns>
    public static UdsServerResponse NoResponse(TimeSpan delay = default) => new(ReadOnlyMemory<byte>.Empty, delay);

    /// <summary>创建完整 payload 响应步骤。<br/>Create a full-payload response step.</summary>
    /// <param name="payload">要发送的 UDS payload。<br/>The UDS payload to send.</param>
    /// <param name="delay">发送前的可选延迟。<br/>Optional delay before sending.</param>
    /// <returns>一个发送指定 payload 的响应步骤。<br/>A response step that sends the specified payload.</returns>
    public static UdsServerResponse Send(ReadOnlyMemory<byte> payload, TimeSpan delay = default) => new(payload, delay);

    /// <summary>创建负响应步骤。<br/>Create a negative response step.</summary>
    /// <param name="serviceId">被拒绝的服务标识符。<br/>The service identifier being rejected.</param>
    /// <param name="code">否定响应代码。<br/>The negative response code.</param>
    /// <param name="delay">发送前的可选延迟。<br/>Optional delay before sending.</param>
    /// <returns>一个发送否定响应的响应步骤。<br/>A response step that sends a negative response.</returns>
    public static UdsServerResponse Negative(byte serviceId, NegativeResponseCode code, TimeSpan delay = default)
        => new(AsyncUdsServer.BuildNegativeResponse(serviceId, code), delay);

    /// <summary>创建正响应步骤。<br/>Create a positive response step.</summary>
    /// <param name="serviceId">被响应的服务标识符。<br/>The service identifier being responded to.</param>
    /// <param name="body">正响应体（不含 SID）。<br/>The positive response body (without the SID).</param>
    /// <param name="delay">发送前的可选延迟。<br/>Optional delay before sending.</param>
    /// <returns>一个发送正响应的响应步骤。<br/>A response step that sends a positive response.</returns>
    public static UdsServerResponse Positive(byte serviceId, ReadOnlySpan<byte> body, TimeSpan delay = default)
        => new(AsyncUdsServer.BuildPositiveResponse(serviceId, body), delay);
}
