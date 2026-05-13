using System;
using System.Threading;

namespace DiagKit.Uds.Internal;

/// <summary>
/// A disposable wrapper around a linked <see cref="CancellationTokenSource"/> with
/// an optional timeout. The outer external token passed to the constructor is never
/// cancelled by this type; only the internal timeout source is cancelled on disposal.
/// </summary>
internal sealed class LinkedCts : IDisposable
{
    private readonly CancellationTokenSource? _timeout;
    private readonly CancellationTokenSource _linked;

    public LinkedCts(TimeSpan? timeout, CancellationToken externalToken)
    {
        if (timeout.HasValue)
        {
            _timeout = new CancellationTokenSource(timeout.Value);
            _linked = CancellationTokenSource.CreateLinkedTokenSource(_timeout.Token, externalToken);
        }
        else
        {
            _linked = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
        }
    }

    public CancellationToken Token => _linked.Token;

    public void Dispose()
    {
        _linked.Dispose();
        _timeout?.Dispose();
    }
}
