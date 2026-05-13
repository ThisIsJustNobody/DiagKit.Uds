using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

using DiagKit.Uds.Contracts;
using DiagKit.Uds.Services;

namespace DiagKit.Uds.UdsLayer;

/// <summary>
/// 异步 UDS 客户端会话，带有 FIFO 请求队列和 S3 TesterPresent 管理。<br/>
/// Async UDS client session with a FIFO request queue and S3 TesterPresent management.
/// </summary>
/// <remarks>
/// 调用方可并发发送请求。会话将所有业务请求序列化到单个工作线程上，
/// 然后再调用包装的 <see cref="IAsyncUdsClient"/>。
/// 会话管理的 TesterPresent 仅在业务队列为空时发送。<br/>
/// Callers may issue requests concurrently. The session serializes all business
/// requests onto a single worker before invoking the wrapped <see cref="IAsyncUdsClient"/>.
/// Session-managed TesterPresent is sent only while the business queue is empty.
/// </remarks>
public sealed class UdsClientSession : IAsyncUdsClient, IAsyncDisposable
{
    private readonly IAsyncUdsClient _client;
    private readonly UdsClientSessionOptions _options;
    private readonly LinkedList<QueuedUdsRequest> _queue = new();
    private readonly SemaphoreSlim _queueCapacity;
    private readonly CancellationTokenSource _disposeCts;
    private readonly object _sync = new();
    private readonly Task _worker;
    private readonly TimeSpan _testerPresentInterval;
    private TaskCompletionSource? _queueChanged;
    private ExceptionDispatchInfo? _workerFault;
    private long _lastS3ActivityTimestamp;
    private int _disposed;

    /// <summary>
    /// 创建 UDS 客户端会话，带 FIFO 请求队列和可选的 S3 TesterPresent 保活。<br/>
    /// Creates a UDS client session with a FIFO request queue and optional S3 TesterPresent keep-alive.
    /// </summary>
    /// <param name="client">底层的异步 UDS 客户端。<br/>The underlying async UDS client.</param>
    /// <param name="options">会话参数（可选，使用默认值）。<br/>Optional session parameters.</param>
    public UdsClientSession(IAsyncUdsClient client, UdsClientSessionOptions? options = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _options = (options ?? new UdsClientSessionOptions()).Clone();
        _options.Validate();

        _testerPresentInterval = _options.TesterPresentInterval ?? _client.Options.S3Client;
        if (_options.TesterPresentEnabled && _testerPresentInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options), _testerPresentInterval, "Effective TesterPresent interval must be greater than zero.");

        SemaphoreSlim? queueCapacity = null;
        CancellationTokenSource? disposeCts = null;
        try
        {
            queueCapacity = new SemaphoreSlim(_options.MaxQueueLength, _options.MaxQueueLength);
            disposeCts = new CancellationTokenSource();
            _queueCapacity = queueCapacity;
            _disposeCts = disposeCts;
            _lastS3ActivityTimestamp = Stopwatch.GetTimestamp();
            _worker = Task.Run(RunWorkerAsync);
        }
        catch
        {
            queueCapacity?.Dispose();
            disposeCts?.Dispose();
            throw;
        }
    }

    /// <inheritdoc/>

    public UdsOptions Options => _client.Options.Clone();

    /// <summary>
    /// 会话选项快照。修改返回对象不会影响正在运行的会话。<br/>
    /// Snapshot of the session options. Modifying the returned object does not
    /// affect the running session.
    /// </summary>
    public UdsClientSessionOptions SessionOptions => _options.Clone();

    /// <summary>当会话工作线程停止时完成的任务；如果工作线程失败则进入故障状态。<br/>Task that completes when the session worker stops and faults if the worker fails.</summary>
    public Task Completion => _worker;

    /// <inheritdoc/>
    public async Task<ReadOnlyMemory<byte>> SendRequestAsync(
        ReadOnlyMemory<byte> request,
        bool? suppressResponse = null,
        CancellationToken cancellationToken = default)
    {
        if (request.IsEmpty) throw new ArgumentException("Request is empty.", nameof(request));
        ThrowIfDisposed();
        ThrowIfWorkerFaulted();
        cancellationToken.ThrowIfCancellationRequested();

        var item = new QueuedUdsRequest(this, request.ToArray(), suppressResponse, cancellationToken);

        var capacityTaken = false;
        var enqueued = false;
        try
        {
            await WaitForQueueCapacityAsync(cancellationToken).ConfigureAwait(false);
            capacityTaken = true;
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfWorkerFaulted();

            lock (_sync)
            {
                if (Volatile.Read(ref _disposed) != 0)
                    throw new ObjectDisposedException(nameof(UdsClientSession));
                ThrowIfWorkerFaulted();

                item.QueueNode = _queue.AddLast(item);
                enqueued = true;
                item.RegisterCancellation();
            }

            SignalQueueChanged();
            return await item.Completion.Task.ConfigureAwait(false);
        }
        catch
        {
            if (!enqueued)
            {
                item.DisposeCancellationRegistration();
                if (capacityTaken)
                    _queueCapacity.Release();
            }
            throw;
        }
        finally
        {
            if (enqueued)
                item.DisposeCancellationRegistration();
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _disposeCts.Cancel();
        CancelQueuedRequests(_disposeCts.Token);
        SignalQueueChanged();

        try { await _worker.ConfigureAwait(false); }
        finally
        {
            _queueCapacity.Dispose();
            _disposeCts.Dispose();
        }
    }

    private async Task WaitForQueueCapacityAsync(CancellationToken cancellationToken)
    {
        if (_options.QueueFullMode == UdsClientSessionQueueFullMode.Throw)
        {
            if (!_queueCapacity.Wait(0))
                throw new InvalidOperationException("The UDS client session request queue is full.");
            return;
        }

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _disposeCts.Token);
        try
        {
            await _queueCapacity.WaitAsync(linkedCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_disposeCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new ObjectDisposedException(nameof(UdsClientSession));
        }
    }

    private async Task RunWorkerAsync()
    {
        var cancellationToken = _disposeCts.Token;
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (TryDequeue(out var queued))
                {
                    await ProcessQueuedRequestAsync(queued, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (!_options.TesterPresentEnabled)
                {
                    await WaitForQueueChangeAsync(null, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                var delay = GetTesterPresentDelay();
                if (delay <= TimeSpan.Zero)
                {
                    if (TryDequeue(out queued))
                    {
                        await ProcessQueuedRequestAsync(queued, cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    await SendTesterPresentAsync(cancellationToken).ConfigureAwait(false);
                    continue;
                }

                await WaitForQueueChangeAsync(delay, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (_disposeCts.IsCancellationRequested)
        {
            CancelQueuedRequests(_disposeCts.Token);
        }
        catch (Exception ex)
        {
            lock (_sync)
                _workerFault ??= ExceptionDispatchInfo.Capture(ex);
            FailQueuedRequests(ex);
            throw;
        }
    }

    private async Task ProcessQueuedRequestAsync(QueuedUdsRequest queued, CancellationToken sessionToken)
    {
        if (queued.Completion.Task.IsCompleted || queued.CancellationToken.IsCancellationRequested)
        {
            queued.Completion.TrySetCanceled(queued.CancellationToken);
            return;
        }

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(queued.CancellationToken, sessionToken);
        try
        {
            ReadOnlyMemory<byte> response;
            try
            {
                NotifyS3ActivityForBusinessRequest();
                response = await _client.SendRequestAsync(queued.Request, queued.SuppressResponse, linkedCts.Token).ConfigureAwait(false);
            }
            finally
            {
                NotifyS3ActivityForBusinessRequest();
            }

            queued.Completion.TrySetResult(response);
        }
        catch (OperationCanceledException) when (sessionToken.IsCancellationRequested && !queued.CancellationToken.IsCancellationRequested)
        {
            queued.Completion.TrySetCanceled(sessionToken);
        }
        catch (OperationCanceledException) when (queued.CancellationToken.IsCancellationRequested)
        {
            queued.Completion.TrySetCanceled(queued.CancellationToken);
        }
        catch (Exception ex)
        {
            queued.Completion.TrySetException(ex);
        }
    }

    private async Task SendTesterPresentAsync(CancellationToken cancellationToken)
    {
        try
        {
            NotifyS3Activity();
            await _client.SendRequestAsync(
                TesterPresent.BuildRequest(_options.SuppressTesterPresentPositiveResponse),
                _options.SuppressTesterPresentPositiveResponse,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        finally
        {
            NotifyS3Activity();
        }
    }

    private bool TryDequeue(out QueuedUdsRequest queued)
    {
        lock (_sync)
        {
            var node = _queue.First;
            if (node is null)
            {
                queued = null!;
                return false;
            }

            queued = node.Value;
            _queue.RemoveFirst();
            queued.QueueNode = null;
            _queueCapacity.Release();
            return true;
        }
    }

    private async Task WaitForQueueChangeAsync(TimeSpan? timeout, CancellationToken cancellationToken)
    {
        TaskCompletionSource waiter;
        lock (_sync)
        {
            if (_queue.Count > 0)
                return;
            waiter = _queueChanged ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        try
        {
            if (timeout.HasValue)
                await waiter.Task.WaitAsync(timeout.Value, cancellationToken).ConfigureAwait(false);
            else
                await waiter.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            lock (_sync)
            {
                if (ReferenceEquals(_queueChanged, waiter))
                    _queueChanged = null;
            }
        }
    }

    private void SignalQueueChanged()
    {
        TaskCompletionSource? waiter;
        lock (_sync)
        {
            waiter = _queueChanged;
            _queueChanged = null;
        }

        waiter?.TrySetResult();
    }

    private void CancelQueuedRequests(CancellationToken cancellationToken)
    {
        List<QueuedUdsRequest> queued = [];
        TaskCompletionSource? waiter;

        lock (_sync)
        {
            while (_queue.Count > 0)
            {
                var item = _queue.First!.Value;
                _queue.RemoveFirst();
                item.QueueNode = null;
                queued.Add(item);
                _queueCapacity.Release();
            }

            waiter = _queueChanged;
            _queueChanged = null;
        }

        foreach (var item in queued)
        {
            item.DisposeCancellationRegistration();
            item.Completion.TrySetCanceled(cancellationToken);
        }

        waiter?.TrySetCanceled(cancellationToken);
    }

    private void FailQueuedRequests(Exception exception)
    {
        List<QueuedUdsRequest> queued = [];
        TaskCompletionSource? waiter;

        lock (_sync)
        {
            while (_queue.Count > 0)
            {
                var item = _queue.First!.Value;
                _queue.RemoveFirst();
                item.QueueNode = null;
                queued.Add(item);
                _queueCapacity.Release();
            }

            waiter = _queueChanged;
            _queueChanged = null;
        }

        foreach (var item in queued)
        {
            item.DisposeCancellationRegistration();
            item.Completion.TrySetException(exception);
        }

        waiter?.TrySetException(exception);
    }

    private void CancelQueuedRequest(QueuedUdsRequest item)
    {
        var removed = false;
        lock (_sync)
        {
            if (item.QueueNode is { } node)
            {
                _queue.Remove(node);
                item.QueueNode = null;
                removed = true;
            }
        }

        if (!removed)
            return;

        _queueCapacity.Release();
        item.Completion.TrySetCanceled(item.CancellationToken);
    }

    private TimeSpan GetTesterPresentDelay()
    {
        var elapsed = Stopwatch.GetElapsedTime(Interlocked.Read(ref _lastS3ActivityTimestamp));
        return elapsed >= _testerPresentInterval ? TimeSpan.Zero : _testerPresentInterval - elapsed;
    }

    private void NotifyS3ActivityForBusinessRequest()
    {
        if (_options.ResetS3OnAnyRequest)
            NotifyS3Activity();
    }

    private void NotifyS3Activity()
        => Interlocked.Exchange(ref _lastS3ActivityTimestamp, Stopwatch.GetTimestamp());

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
            throw new ObjectDisposedException(nameof(UdsClientSession));
    }

    private void ThrowIfWorkerFaulted()
        => Volatile.Read(ref _workerFault)?.Throw();

    private sealed class QueuedUdsRequest
    {
        private readonly UdsClientSession _owner;
        private CancellationTokenRegistration _cancellationRegistration;

        public QueuedUdsRequest(UdsClientSession owner, byte[] request, bool? suppressResponse, CancellationToken cancellationToken)
        {
            _owner = owner;
            Request = request;
            SuppressResponse = suppressResponse;
            CancellationToken = cancellationToken;
        }

        public LinkedListNode<QueuedUdsRequest>? QueueNode { get; set; }

        public ReadOnlyMemory<byte> Request { get; }

        public bool? SuppressResponse { get; }

        public CancellationToken CancellationToken { get; }

        public TaskCompletionSource<ReadOnlyMemory<byte>> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void RegisterCancellation()
        {
            if (CancellationToken.CanBeCanceled)
                _cancellationRegistration = CancellationToken.Register(static state => ((QueuedUdsRequest)state!).Cancel(), this);
        }

        public void DisposeCancellationRegistration()
            => _cancellationRegistration.Dispose();

        private void Cancel()
            => _owner.CancelQueuedRequest(this);
    }
}
