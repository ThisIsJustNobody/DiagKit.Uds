using System;
using System.Threading;
using System.Threading.Tasks;

using DiagKit.Uds.Exceptions;
using DiagKit.Uds.UdsLayer;

namespace DiagKit.Uds.Contracts;

/// <summary>
/// 异步 UDS 诊断客户端接口，可发出诊断请求并异步等待响应。
/// <br/>Asynchronous UDS diagnostic client interface that can issue a diagnostic request and wait for a response asynchronously.
/// </summary>
/// <remarks>
/// 实现类负责通过底层传输器（如 ISO 15765 DoCAN 或 ISO 13400 DoIP）发送 UDS 请求消息，
/// 并异步等待对应的响应。该接口提供了 RequestPending（0x78）和 BusyRepeatRequest（0x21）
/// 状态机的自动处理。同一时间只允许一个请求在进行中，并发调用将抛出异常。
/// <br/>Implementing classes are responsible for sending UDS request messages through an
/// underlying transporter (e.g. ISO 15765 DoCAN or ISO 13400 DoIP) and asynchronously
/// waiting for the corresponding response. Automatic handling of RequestPending (0x78)
/// and BusyRepeatRequest (0x21) state machines is provided. Only one request is allowed
/// in flight at a time; concurrent calls will throw an exception.
/// </remarks>
public interface IAsyncUdsClient
{
    /// <summary>
    /// 获取 UDS 客户端配置选项的快照。修改返回的对象不会影响正在运行的客户端。
    /// <br/>Gets a snapshot of the UDS client configuration options. Modifying the returned object does not affect the running client.
    /// </summary>
    UdsOptions Options { get; }

    /// <summary>
    /// 异步发送一个 UDS 请求并返回原始响应字节。
    /// 当响应被抑制时返回空 <see cref="ReadOnlyMemory{T}"/>。
    /// <br/>Asynchronously send a UDS request and return the raw response bytes.
    /// Returns an empty <see cref="ReadOnlyMemory{T}"/> when the response is suppressed.
    /// </summary>
    /// <param name="request">
    /// 要发送的 UDS 请求消息字节。
    /// <br/>The UDS request message bytes to send.
    /// </param>
    /// <param name="suppressResponse">
    /// 若为 <see langword="true"/> 则抑制响应（抑制正响应位 SuppressPositiveResponse，SID 位 7）。
    /// 若为 <see langword="null"/> 则使用客户端自身的默认行为。
    /// <br/>If <see langword="true"/>, suppress the response (SuppressPositiveResponse flag, SID bit 7).
    /// If <see langword="null"/>, the client's own default behaviour is used.
    /// </param>
    /// <param name="cancellationToken">
    /// 用于取消请求操作的中止令牌。
    /// <br/>A cancellation token to cancel the request operation.
    /// </param>
    /// <returns>
    /// 一个任务，其结果为来自 ECU 的原始响应字节。如果响应被抑制，则结果为空 <see cref="ReadOnlyMemory{T}"/>。
    /// <br/>A task whose result is the raw response bytes from the ECU. Empty
    /// <see cref="ReadOnlyMemory{T}"/> if the response is suppressed.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="request"/> 为空。
    /// <br/><paramref name="request"/> is empty.
    /// </exception>
    /// <exception cref="TimeoutException">
    /// 在配置的超时时间内未收到响应。
    /// <br/>No response received within the configured timeout.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// 已有另一个请求在进行中（并发请求不被允许）。
    /// <br/>Another request is already in flight (concurrent requests are not allowed).
    /// </exception>
    /// <exception cref="NegativeResponseException">
    /// ECU 返回了否定响应（NRC）。
    /// <br/>The ECU returned a negative response (NRC).
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> 被取消。
    /// <br/><paramref name="cancellationToken"/> was cancelled.
    /// </exception>
    Task<ReadOnlyMemory<byte>> SendRequestAsync(ReadOnlyMemory<byte> request, bool? suppressResponse = null, CancellationToken cancellationToken = default);
}
