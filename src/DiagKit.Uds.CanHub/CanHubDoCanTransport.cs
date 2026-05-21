using System;
using System.Threading;
using System.Threading.Tasks;

using DiagKit.Uds.Contracts;
using DiagKit.Uds.DoCan;

namespace DiagKit.Uds.CanHub;

/// <summary>
/// 基于 CanHub <see cref="global::CanHub.ICanBus"/> 的异步 DoCAN 传输器。<br/>
/// Async DoCAN transport backed by a CanHub <see cref="global::CanHub.ICanBus"/>.
/// </summary>
public sealed class CanHubDoCanTransport : IAsyncTransmitter<ReadOnlyMemory<byte>>, IDisposable, IAsyncDisposable
{
    private readonly CanHubCanTransmitter _canTransmitter;
    private readonly AsyncDoCanTransmitter _doCanTransmitter;

    /// <summary>
    /// 创建 CanHub DoCAN 传输器。<br/>Creates a CanHub DoCAN transport.
    /// </summary>
    public CanHubDoCanTransport(
        global::CanHub.ICanBus bus,
        DoCanOptions? doCanOptions = null,
        global::CanHub.CanSubscriptionOptions? subscriptionOptions = null,
        global::CanHub.CanTransmitOptions? transmitOptions = null)
    {
        _canTransmitter = new CanHubCanTransmitter(bus, subscriptionOptions, transmitOptions);
        _doCanTransmitter = new AsyncDoCanTransmitter(_canTransmitter, doCanOptions);
    }

    /// <inheritdoc/>
    public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
        => _doCanTransmitter.SendAsync(data, cancellationToken);

    /// <inheritdoc/>
    public Task<ReadOnlyMemory<byte>> ReceiveAsync(CancellationToken cancellationToken = default)
        => _doCanTransmitter.ReceiveAsync(cancellationToken);

    /// <inheritdoc/>
    public void ClearReceiveBuffer() => _doCanTransmitter.ClearReceiveBuffer();

    /// <inheritdoc/>
    public void Dispose() => _canTransmitter.Dispose();

    /// <inheritdoc/>
    public ValueTask DisposeAsync() => _canTransmitter.DisposeAsync();
}
