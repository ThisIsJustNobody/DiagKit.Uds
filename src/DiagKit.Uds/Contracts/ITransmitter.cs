using System.Threading;

namespace DiagKit.Uds.Contracts;

/// <summary>
/// 同步双向传输器接口，用于发送和接收 <typeparamref name="T"/> 类型的负载。
/// <br/>Synchronous bidirectional transmitter interface for sending and receiving payloads of type <typeparamref name="T"/>.
/// </summary>
/// <typeparam name="T">
/// 传输的负载类型（例如 CanFrame 或 DoIpMessage）。
/// <br/>The payload type transported (e.g. CanFrame or DoIpMessage).
/// </typeparam>
/// <remarks>
/// 此接口定义了同步传输器的基本契约。实现类应确保线程安全，
/// 通常通过 <see cref="SemaphoreSlim"/> 进行门控以保证同一时间只有一个会话在进行。
/// <br/>This interface defines the basic contract for a synchronous transmitter.
/// Implementations should ensure thread safety, typically gated by a
/// <see cref="SemaphoreSlim"/> to guarantee only one conversation in flight at a time.
/// </remarks>
public interface ITransmitter<T>
{
    /// <summary>
    /// 发送一个负载。
    /// <br/>Send a payload.
    /// </summary>
    /// <param name="data">
    /// 要发送的负载实例。
    /// <br/>The payload instance to send.
    /// </param>
    /// <param name="cancellationToken">
    /// 用于取消发送操作的中止令牌。
    /// <br/>A cancellation token to cancel the send operation.
    /// </param>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> 被取消。
    /// <br/><paramref name="cancellationToken"/> was cancelled.
    /// </exception>
    void Send(T data, CancellationToken cancellationToken = default);

    /// <summary>
    /// 接收下一个负载。此方法会阻塞直到有数据可用。
    /// <br/>Receive the next payload. This method blocks until data is available.
    /// </summary>
    /// <param name="cancellationToken">
    /// 用于取消接收操作的中止令牌。
    /// <br/>A cancellation token to cancel the receive operation.
    /// </param>
    /// <returns>
    /// 接收到的类型为 <typeparamref name="T"/> 的负载。
    /// <br/>The received payload of type <typeparamref name="T"/>.
    /// </returns>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> 被取消。
    /// <br/><paramref name="cancellationToken"/> was cancelled.
    /// </exception>
    T Receive(CancellationToken cancellationToken = default);

    /// <summary>
    /// 清空所有待处理的入站负载。
    /// <br/>Drain all pending inbound payloads.
    /// </summary>
    /// <remarks>
    /// 调用此方法后，之前排队但尚未被 <see cref="Receive"/> 取走的负载将被丢弃。
    /// 通常在会话切换或错误恢复时使用。
    /// <br/>After calling this method, any payloads that were queued but not yet consumed
    /// by <see cref="Receive"/> will be discarded. Typically used during session changes
    /// or error recovery.
    /// </remarks>
    void ClearReceiveBuffer();
}
