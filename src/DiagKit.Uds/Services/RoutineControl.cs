using System;
using System.Buffers.Binary;
using System.Threading;
using System.Threading.Tasks;

using DiagKit.Uds.Contracts;
using DiagKit.Uds.UdsLayer;

using DiagKit.Uds.Exceptions;

namespace DiagKit.Uds.Services;

/// <summary>
/// SID 0x31（RoutineControl/例程控制）辅助方法。<br/>Helpers for SID 0x31 RoutineControl.
/// </summary>
public static class RoutineControl
{
    /// <summary>
    /// 构建 RoutineControl 请求。<br/>Build a RoutineControl request.
    /// </summary>
    /// <param name="type">例程控制子功能类型。<br/>The routine control sub-function type.</param>
    /// <param name="routineId">例程标识符。<br/>The routine identifier.</param>
    /// <param name="routineData">可选的例程数据。<br/>Optional routine data.</param>
    /// <param name="suppressPositiveResponse">是否抑制肯定响应。<br/>Whether to suppress the positive response.</param>
    /// <returns>请求字节数组。<br/>The request byte array.</returns>
    public static byte[] BuildRequest(RoutineControlType type, ushort routineId, ReadOnlySpan<byte> routineData = default, bool suppressPositiveResponse = false)
    {
        byte sub = (byte)type;
        if (suppressPositiveResponse) sub |= 0x80;
        var buf = new byte[4 + routineData.Length];
        buf[0] = (byte)UdsServiceId.RoutineControl;
        buf[1] = sub;
        BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(2, 2), routineId);
        routineData.CopyTo(buf.AsSpan(4));
        return buf;
    }

    /// <summary>
    /// RoutineControl 肯定响应的解析结果：例程标识符 + 状态记录。<br/>Parsed positive RoutineControl response: routine ID + status bytes.
    /// </summary>
    public readonly record struct Response(RoutineControlType Type, ushort RoutineId, ReadOnlyMemory<byte> StatusRecord);

    /// <summary>
    /// 解析 RoutineControl 的肯定响应。<br/>Parse a positive RoutineControl response.
    /// </summary>
    /// <param name="response">响应数据。<br/>The response data.</param>
    /// <param name="expectedType">期望的子功能类型（可为 <see langword="null"/> 表示不校验）。<br/>The expected sub-function type (<see langword="null"/> to skip validation).</param>
    /// <param name="expectedRoutineId">期望的例程标识符（可为 <see langword="null"/> 表示不校验）。<br/>The expected routine identifier (<see langword="null"/> to skip validation).</param>
    /// <returns>解析后的例程控制响应。<br/>The parsed routine control response.</returns>
    public static Response ParseResponse(ReadOnlySpan<byte> response, RoutineControlType? expectedType = null, ushort? expectedRoutineId = null)
    {
        if (response.Length < 4 || response[0] != 0x71)
            throw new ProtocolException("Not a positive RoutineControl response.");
        var type = (RoutineControlType)(response[1] & 0x7F);
        var rid = BinaryPrimitives.ReadUInt16BigEndian(response.Slice(2, 2));
        if (expectedType.HasValue && type != expectedType.Value)
            throw new ProtocolException($"RoutineControl echoed sub-function 0x{(byte)type:X2}, expected 0x{(byte)expectedType.Value:X2}.");
        if (expectedRoutineId.HasValue && rid != expectedRoutineId.Value)
            throw new ProtocolException($"RoutineControl echoed routine ID 0x{rid:X4}, expected 0x{expectedRoutineId.Value:X4}.");
        var status = response.Length > 4 ? response[4..].ToArray() : [];
        return new Response(type, rid, status);
    }

    /// <summary>
    /// 发送 RoutineControl 请求并返回解析后的响应。<br/>Send a RoutineControl request and return the parsed response.
    /// </summary>
    /// <param name="client">UDS 异步客户端。<br/>The UDS async client.</param>
    /// <param name="type">例程控制子功能类型。<br/>The routine control sub-function type.</param>
    /// <param name="routineId">例程标识符。<br/>The routine identifier.</param>
    /// <param name="routineData">可选的例程数据。<br/>Optional routine data.</param>
    /// <param name="cancellationToken">取消令牌。<br/>Cancellation token.</param>
    /// <returns>解析后的例程控制响应。<br/>The parsed routine control response.</returns>
    public static async Task<Response> InvokeAsync(
        IAsyncUdsClient client, RoutineControlType type, ushort routineId,
        ReadOnlyMemory<byte> routineData = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        var resp = await client.SendRequestAsync(BuildRequest(type, routineId, routineData.Span), null, cancellationToken).ConfigureAwait(false);
        return ParseResponse(resp.Span, type, routineId);
    }
}
