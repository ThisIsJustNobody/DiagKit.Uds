using System;
using System.Threading;
using System.Threading.Tasks;

using DiagKit.Uds.Contracts;
using DiagKit.Uds.UdsLayer;

using DiagKit.Uds.Exceptions;

namespace DiagKit.Uds.Services;

/// <summary>
/// SID 0x10（DiagnosticSessionControl/诊断会话控制）辅助方法。<br/>Helpers for SID 0x10 DiagnosticSessionControl.
/// </summary>
public static class DiagnosticSessionControl
{
    /// <summary>
    /// 构建 DiagnosticSessionControl 请求。<br/>Build a DiagnosticSessionControl request.
    /// </summary>
    /// <param name="session">目标诊断会话类型。<br/>The target diagnostic session type.</param>
    /// <param name="suppressPositiveResponse">是否抑制肯定响应。<br/>Whether to suppress the positive response.</param>
    /// <returns>请求字节数组。<br/>The request byte array.</returns>
    public static byte[] BuildRequest(DiagnosticSessionType session, bool suppressPositiveResponse = false)
    {
        byte sub = (byte)session;
        if (suppressPositiveResponse) sub |= 0x80;
        return [(byte)UdsServiceId.DiagnosticSessionControl, sub];
    }

    /// <summary>
    /// DiagnosticSessionControl 请求的解析后响应。<br/>Parsed response from a DiagnosticSessionControl request.
    /// </summary>
    public readonly record struct Response(DiagnosticSessionType Session, TimeSpan P2Server, TimeSpan P2ServerExtended);

    /// <summary>
    /// 解码 DiagnosticSessionControl 的肯定响应。<br/>Decode a positive DiagnosticSessionControl response.
    /// </summary>
    /// <remarks>
    /// 响应格式为：[0x50, sub, P2[hi][lo], P2*[hi][lo]]。<br/>The response format is: [0x50, sub, P2[hi][lo], P2*[hi][lo]].
    /// </remarks>
    /// <param name="response">响应数据。<br/>The response data.</param>
    /// <param name="expectedSession">期望的会话类型（可为 <see langword="null"/> 表示不校验）。<br/>The expected session type (<see langword="null"/> to skip validation).</param>
    /// <returns>解析后的会话控制响应。<br/>The parsed session control response.</returns>
    public static Response ParseResponse(ReadOnlySpan<byte> response, DiagnosticSessionType? expectedSession = null)
    {
        if (response.Length < 2 || response[0] != 0x50)
            throw new ProtocolException("Not a positive DiagnosticSessionControl response.");
        var session = (DiagnosticSessionType)(response[1] & 0x7F);
        if (expectedSession.HasValue && session != expectedSession.Value)
            throw new ProtocolException($"DiagnosticSessionControl echoed session 0x{(byte)session:X2}, expected 0x{(byte)expectedSession.Value:X2}.");
        TimeSpan p2 = TimeSpan.Zero, p2Star = TimeSpan.Zero;
        if (response.Length >= 6)
        {
            p2 = TimeSpan.FromMilliseconds((response[2] << 8) | response[3]);
            p2Star = TimeSpan.FromMilliseconds(10 * ((response[4] << 8) | response[5]));
        }
        return new Response(session, p2, p2Star);
    }

    /// <summary>
    /// 发送 DiagnosticSessionControl 请求并返回解析后的响应。<br/>Send a DiagnosticSessionControl request and return the parsed response.
    /// </summary>
    /// <param name="client">UDS 异步客户端。<br/>The UDS async client.</param>
    /// <param name="session">目标诊断会话类型。<br/>The target diagnostic session type.</param>
    /// <param name="cancellationToken">取消令牌。<br/>Cancellation token.</param>
    /// <returns>解析后的会话控制响应。<br/>The parsed session control response.</returns>
    public static async Task<Response> InvokeAsync(
        IAsyncUdsClient client, DiagnosticSessionType session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        var resp = await client.SendRequestAsync(BuildRequest(session), null, cancellationToken).ConfigureAwait(false);
        return ParseResponse(resp.Span, session);
    }
}
