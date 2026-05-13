using System;
using System.Threading;
using System.Threading.Tasks;

using DiagKit.Uds.Contracts;
using DiagKit.Uds.UdsLayer;

namespace DiagKit.Uds.Services;

/// <summary>
/// SID 0x3E（TesterPresent/测试设备在线）及其保活循环辅助方法。<br/>Helpers for SID 0x3E TesterPresent and its keep-alive loop.
/// </summary>
public static class TesterPresent
{
    /// <summary>
    /// 构建 TesterPresent 请求（子功能 0x00 = zeroSubFunction）。<br/>Build a TesterPresent request (sub-function 0x00 = zeroSubFunction).
    /// </summary>
    /// <param name="suppressPositiveResponse">是否抑制肯定响应。<br/>Whether to suppress the positive response.</param>
    public static byte[] BuildRequest(bool suppressPositiveResponse = true)
        => [(byte)UdsServiceId.TesterPresent, suppressPositiveResponse ? (byte)0x80 : (byte)0x00];

    /// <summary>
    /// 发送单次 TesterPresent 请求。<br/>Send a single TesterPresent request.
    /// </summary>
    /// <remarks>
    /// 当收到响应（或抑制响应的超时到期）后返回。<br/>Returns when the response (or the timeout for suppressed responses) has been processed.
    /// </remarks>
    /// <param name="client">UDS 异步客户端。<br/>The UDS async client.</param>
    /// <param name="suppressPositiveResponse">是否抑制肯定响应。<br/>Whether to suppress the positive response.</param>
    /// <param name="cancellationToken">取消令牌。<br/>Cancellation token.</param>
    /// <returns>响应数据。<br/>The response data.</returns>
    public static Task<ReadOnlyMemory<byte>> PingAsync(IAsyncUdsClient client, bool suppressPositiveResponse = true, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        return client.SendRequestAsync(BuildRequest(suppressPositiveResponse), suppressPositiveResponse, cancellationToken);
    }

    /// <summary>
    /// 启动后台保活循环，按指定间隔发送 TesterPresent。<br/>Run a background keep-alive loop that issues TesterPresent at the specified interval.
    /// </summary>
    /// <remarks>
    /// 释放返回的对象即可停止循环。<br/>Disposing the returned object stops the loop.
    /// </remarks>
    /// <param name="client">UDS 异步客户端。<br/>The UDS async client.</param>
    /// <param name="interval">保活间隔（为 <see langword="null"/> 时使用客户端的 S3 参数）。<br/>The keep-alive interval (uses the client's S3 parameter when <see langword="null"/>).</param>
    /// <param name="suppressPositiveResponse">是否抑制肯定响应。<br/>Whether to suppress the positive response.</param>
    /// <returns>用于控制保活循环的 <see cref="KeepAlive"/> 句柄。<br/>A <see cref="KeepAlive"/> handle to control the keep-alive loop.</returns>
    public static KeepAlive StartKeepAlive(IAsyncUdsClient client, TimeSpan? interval = null, bool suppressPositiveResponse = true)
    {
        ArgumentNullException.ThrowIfNull(client);
        var period = interval ?? client.Options.S3Client;
        if (period <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(interval), period, "Keep-alive interval must be greater than zero.");

        var cts = new CancellationTokenSource();
        var task = Task.Run(async () =>
        {
            try
            {
                while (!cts.IsCancellationRequested)
                {
                    await Task.Delay(period, cts.Token).ConfigureAwait(false);
                    await PingAsync(client, suppressPositiveResponse, cts.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
        });
        return new KeepAlive(cts, task);
    }

    /// <summary>
    /// 正在运行的 TesterPresent 保活循环句柄，释放即可停止。<br/>Handle to a running TesterPresent keep-alive loop. Dispose to stop.
    /// </summary>
    public sealed class KeepAlive : IDisposable, IAsyncDisposable
    {
        private readonly CancellationTokenSource _cts;
        private readonly Task _task;

        internal KeepAlive(CancellationTokenSource cts, Task task) { _cts = cts; _task = task; }

        /// <summary>
        /// 保活循环停止时完成的 <see cref="Task"/>，若某次 Ping 失败则转为故障状态。<br/><see cref="Task"/> that completes when the keep-alive loop stops and faults if a ping fails.
        /// </summary>
        public Task Completion => _task;

        /// <inheritdoc/>
        public void Dispose()
        {
            _cts.Cancel();
            try { _task.GetAwaiter().GetResult(); }
            catch (OperationCanceledException) when (_cts.IsCancellationRequested) { }
            catch (Exception) { }
            finally { _cts.Dispose(); }
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            _cts.Cancel();
            try { await _task.ConfigureAwait(false); }
            catch (OperationCanceledException) when (_cts.IsCancellationRequested) { }
            catch (Exception) { }
            finally { _cts.Dispose(); }
        }
    }
}
