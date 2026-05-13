using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

using DiagKit.Uds.Contracts;

namespace DiagKit.Uds.UdsLayer;

/// <summary>
/// 处理 UDS 请求并生成响应的委托。返回空内存表示响应被抑制。<br/>A delegate that handles a UDS request and produces a response.
/// Return an empty memory to indicate that the response is suppressed.
/// </summary>
/// <param name="request">UDS 请求载荷。<br/>The UDS request payload.</param>
/// <param name="cancellationToken">取消令牌。<br/>The cancellation token.</param>
public delegate Task<ReadOnlyMemory<byte>> UdsRequestHandlerAsync(ReadOnlyMemory<byte> request, CancellationToken cancellationToken);

/// <summary>
/// 异步 UDS 服务器：从入站传输接收请求，按服务 ID 分发到已注册的处理程序，并将响应写回。<br/>
/// Asynchronous UDS server: receives requests from an inbound transport, dispatches
/// them to service handlers keyed by service ID, and writes the response back.
/// </summary>
/// <remarks>
/// 服务器目前仅接收 UDS payload，不含物理/功能寻址元数据。因此采用类似物理寻址的响应处理：
/// suppressPositiveResponse 会抑制匹配的正响应，而功能寻址专用的 NRC 抑制在此未实现。<br/>
/// The server currently receives only the UDS payload and no physical/functional
/// addressing metadata. It therefore applies physical-like response handling:
/// suppressPositiveResponse suppresses matching positive responses, while
/// functional-addressing-specific NRC suppression is not implemented here.
/// </remarks>
public sealed class AsyncUdsServer : IAsyncUdsServer
{
    private readonly Func<ReadOnlyMemory<byte>, CancellationToken, Task> _sendAsync;
    private readonly Func<CancellationToken, Task<ReadOnlyMemory<byte>>> _receiveAsync;
    private readonly ConcurrentDictionary<byte, UdsRequestHandlerAsync> _handlers = new();

    /// <summary>当没有注册的服务匹配 SID 时调用的处理程序。<br/>Handler invoked when no registered service matches the SID.</summary>
    public UdsRequestHandlerAsync? DefaultHandler { get; set; }

    /// <summary>
    /// 使用原始发送/接收委托创建异步 UDS 服务器。<br/>Creates an async UDS server with raw send/receive delegates.
    /// </summary>
    /// <param name="sendAsync">异步发送 UDS 响应的委托。<br/>Delegate to send a UDS response asynchronously.</param>
    /// <param name="receiveAsync">异步接收 UDS 请求的委托。<br/>Delegate to receive a UDS request asynchronously.</param>
    public AsyncUdsServer(
        Func<ReadOnlyMemory<byte>, CancellationToken, Task> sendAsync,
        Func<CancellationToken, Task<ReadOnlyMemory<byte>>> receiveAsync)
    {
        _sendAsync = sendAsync ?? throw new ArgumentNullException(nameof(sendAsync));
        _receiveAsync = receiveAsync ?? throw new ArgumentNullException(nameof(receiveAsync));
    }

    /// <summary>
    /// 从异步传输器接口创建异步 UDS 服务器。<br/>Creates an async UDS server from an <see cref="IAsyncTransmitter{T}"/>.
    /// </summary>
    /// <param name="transport">异步 UDS 传输器接口。<br/>The async UDS transport interface.</param>
    public AsyncUdsServer(IAsyncTransmitter<ReadOnlyMemory<byte>> transport)
        : this(
            (data, ct) => transport.SendAsync(data, ct),
            transport.ReceiveAsync)
    {
    }

    /// <summary>注册指定 UDS 服务 ID 的处理程序。<br/>Register a handler for a UDS service ID.</summary>
    /// <param name="serviceId">UDS 服务标识符。<br/>The UDS service identifier.</param>
    /// <param name="handler">请求处理程序委托。<br/>The request handler delegate.</param>
    public void Register(byte serviceId, UdsRequestHandlerAsync handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _handlers[serviceId] = handler;
    }

    /// <summary>注册指定 UDS 服务 ID 的处理程序。<br/>Register a handler for a UDS service ID.</summary>
    /// <param name="serviceId">UDS 服务标识符。<br/>The UDS service identifier.</param>
    /// <param name="handler">请求处理程序委托。<br/>The request handler delegate.</param>
    public void Register(UdsServiceId serviceId, UdsRequestHandlerAsync handler) => Register((byte)serviceId, handler);

    /// <summary>移除之前注册的处理程序。<br/>Remove a previously registered handler.</summary>
    /// <param name="serviceId">要移除的 UDS 服务标识符。<br/>The UDS service identifier to remove.</param>
    /// <returns>如果找到并移除了处理程序则为 true。<br/>True if a handler was found and removed.</returns>
    public bool Unregister(byte serviceId) => _handlers.TryRemove(serviceId, out _);

    /// <inheritdoc/>
    public async Task<ReadOnlyMemory<byte>> ReceiveAndRespondAsync(CancellationToken cancellationToken = default)
    {
        var request = await _receiveAsync(cancellationToken).ConfigureAwait(false);
        if (request.Length == 0) return ReadOnlyMemory<byte>.Empty;

        byte sid = UdsMessage.GetServiceId(request.Span);
        bool suppress = UdsMessage.IsSuppressPositiveResponse(request.Span);

        ReadOnlyMemory<byte> response;
        if (_handlers.TryGetValue(sid, out var handler))
            response = await handler(request, cancellationToken).ConfigureAwait(false);
        else if (DefaultHandler is not null)
            response = await DefaultHandler(request, cancellationToken).ConfigureAwait(false);
        else
            response = BuildNegativeResponse(sid, NegativeResponseCode.ServiceNotSupported);

        if (response.Length == 0) return response;
        // Without addressing metadata this server cannot apply functional-addressing NRC suppression.
        // It only suppresses positive responses requested via suppressPositiveResponse.
        bool isPositive = response.Span[0] == sid + UdsMessage.PositiveResponseOffset;
        if (suppress && isPositive) return ReadOnlyMemory<byte>.Empty;

        await _sendAsync(response, cancellationToken).ConfigureAwait(false);
        return response;
    }

    /// <summary>构建否定响应的辅助方法。<br/>Helper for building a negative response.</summary>
    /// <param name="serviceId">被拒绝的服务标识符。<br/>The service identifier being rejected.</param>
    /// <param name="code">否定响应代码。<br/>The negative response code.</param>
    /// <returns>包含否定响应的字节序列 [0x7F, SID, NRC]。<br/>A byte array containing the negative response [0x7F, SID, NRC].</returns>
    public static ReadOnlyMemory<byte> BuildNegativeResponse(byte serviceId, NegativeResponseCode code)
        => new byte[] { 0x7F, serviceId, (byte)code };

    /// <summary>为给定请求构建正响应的辅助方法。<br/>Helper for building a positive response for the given request.</summary>
    /// <param name="serviceId">请求服务标识符。<br/>The request service identifier.</param>
    /// <param name="body">正响应体（不含 SID）。<br/>The positive response body (without the SID).</param>
    /// <returns>包含正响应的字节序列 [SID+0x40, body]。<br/>A byte array containing the positive response [SID+0x40, body].</returns>
    public static ReadOnlyMemory<byte> BuildPositiveResponse(byte serviceId, ReadOnlySpan<byte> body)
    {
        var buf = new byte[1 + body.Length];
        buf[0] = (byte)(serviceId + UdsMessage.PositiveResponseOffset);
        body.CopyTo(buf.AsSpan(1));
        return buf;
    }
}
