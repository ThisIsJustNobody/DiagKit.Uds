using System;
using System.Threading;
using System.Threading.Tasks;

namespace DiagKit.Uds.Contracts;

/// <summary>
/// 异步 UDS 诊断服务器接口，可接收 UDS 请求并异步分派给处理程序。
/// <br/>Asynchronous UDS diagnostic server interface that can receive a UDS request and dispatch it to a handler asynchronously.
/// </summary>
/// <remarks>
/// 实现类负责从总线异步接收 UDS 请求帧，将其重组为完整的请求消息，分派给已注册的处理程序，
/// 并将处理结果作为响应数据发送回请求方。每次调用处理一个完整的请求-响应周期。
/// <br/>Implementing classes are responsible for asynchronously receiving UDS request frames
/// from the bus, reassembling them into a complete request message, dispatching to a
/// registered handler, and sending the handler result back to the requester as response
/// data. Each call processes one complete request-response cycle.
/// </remarks>
public interface IAsyncUdsServer
{
    /// <summary>
    /// 异步等待来自总线的下一个请求，将其分派给处理程序，并返回作为响应发送的字节。
    /// <br/>Asynchronously await the next request from the bus, dispatch it to a handler, and return the bytes that were sent in reply.
    /// </summary>
    /// <param name="cancellationToken">
    /// 用于取消等待操作的中止令牌。
    /// <br/>A cancellation token to cancel the wait operation.
    /// </param>
    /// <returns>
    /// 一个任务，其结果为作为响应发送到请求方的原始字节。
    /// <br/>A task whose result is the raw bytes that were sent to the requester as a reply.
    /// </returns>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> 被取消。
    /// <br/><paramref name="cancellationToken"/> was cancelled.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// 服务器未正确配置或未注册处理程序。
    /// <br/>The server is not properly configured or no handler is registered.
    /// </exception>
    Task<ReadOnlyMemory<byte>> ReceiveAndRespondAsync(CancellationToken cancellationToken = default);
}
