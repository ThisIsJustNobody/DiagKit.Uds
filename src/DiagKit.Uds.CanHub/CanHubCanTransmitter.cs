using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

using DiagKit.Uds.Contracts;
using DiagKit.Uds.DoCan;

namespace DiagKit.Uds.CanHub;

/// <summary>
/// 将 CanHub <see cref="global::CanHub.ICanBus"/> 适配为 DiagKit.Uds CAN 帧传输器。<br/>
/// Adapts a CanHub <see cref="global::CanHub.ICanBus"/> to a DiagKit.Uds CAN frame transmitter.
/// </summary>
public sealed class CanHubCanTransmitter : IAsyncTransmitter<CanFrame>, IDisposable, IAsyncDisposable
{
    private readonly global::CanHub.ICanBus _bus;
    private readonly global::CanHub.ICanSubscription _subscription;
    private readonly global::CanHub.CanTransmitOptions? _transmitOptions;
    private readonly Channel<CanFrame> _receivedFrames = Channel.CreateUnbounded<CanFrame>();
    private readonly CancellationTokenSource _disposeCts = new();
    private readonly Task _pumpTask;
    private int _disposed;

    /// <summary>
    /// 创建 CanHub CAN 传输器。<br/>Creates a CanHub CAN transmitter.
    /// </summary>
    /// <param name="bus">已打开的 CanHub 总线。<br/>The opened CanHub bus.</param>
    /// <param name="subscriptionOptions">订阅选项；为 <see langword="null"/> 时使用 CanHub 默认值。<br/>Subscription options; CanHub defaults are used when <see langword="null"/>.</param>
    /// <param name="transmitOptions">发送选项；为 <see langword="null"/> 时使用 CanHub 默认值。<br/>Transmit options; CanHub defaults are used when <see langword="null"/>.</param>
    public CanHubCanTransmitter(
        global::CanHub.ICanBus bus,
        global::CanHub.CanSubscriptionOptions? subscriptionOptions = null,
        global::CanHub.CanTransmitOptions? transmitOptions = null)
    {
        _bus = bus ?? throw new ArgumentNullException(nameof(bus));
        _subscription = bus.Subscribe(subscriptionOptions ?? new global::CanHub.CanSubscriptionOptions());
        _transmitOptions = transmitOptions;
        _pumpTask = Task.Run(PumpAsync);
    }

    /// <inheritdoc/>
    public async Task SendAsync(CanFrame data, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var result = await _bus.SendAsync(
            CanHubFrameConverter.ToCanHub(data),
            _transmitOptions,
            cancellationToken).ConfigureAwait(false);
        if (!result.Accepted)
            throw new CanHubBridgeException(result);
    }

    /// <inheritdoc/>
    public async Task<CanFrame> ReceiveAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return await _receivedFrames.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public void ClearReceiveBuffer()
    {
        while (_receivedFrames.Reader.TryRead(out _)) { }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _disposeCts.Cancel();
        _subscription.Dispose();
        _disposeCts.Dispose();
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        Dispose();
        try
        {
            await _pumpTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task PumpAsync()
    {
        var disposeToken = _disposeCts.Token;
        try
        {
            while (!disposeToken.IsCancellationRequested)
            {
                var frameEvent = await _subscription.ReadAsync(disposeToken).ConfigureAwait(false);
                if (!CanHubFrameConverter.TryFromCanHubEvent(frameEvent, out var frame))
                    continue;
                await _receivedFrames.Writer.WriteAsync(frame, disposeToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (disposeToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _receivedFrames.Writer.TryComplete(ex);
            return;
        }

        _receivedFrames.Writer.TryComplete();
    }

    private void ThrowIfDisposed()
    {
        if (_disposed != 0)
            throw new ObjectDisposedException(nameof(CanHubCanTransmitter));
    }
}
