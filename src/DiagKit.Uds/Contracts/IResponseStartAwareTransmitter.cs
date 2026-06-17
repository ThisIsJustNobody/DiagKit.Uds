using System.Threading;
using System.Threading.Tasks;

namespace DiagKit.Uds.Contracts;

/// <summary>
/// Synchronous transmitter that can distinguish response-start timing from whole-message cancellation.
/// </summary>
/// <typeparam name="T">The payload type transported.</typeparam>
public interface IResponseStartAwareTransmitter<T> : ITransmitter<T>
{
    /// <summary>
    /// Receive the next payload while applying a separate cancellation token to the start of the response.
    /// </summary>
    /// <param name="responseStartCancellationToken">Token that bounds waiting for the first response frame.</param>
    /// <param name="cancellationToken">Token that cancels the whole receive operation.</param>
    /// <returns>The received payload.</returns>
    T Receive(CancellationToken responseStartCancellationToken, CancellationToken cancellationToken = default);
}

/// <summary>
/// Asynchronous transmitter that can distinguish response-start timing from whole-message cancellation.
/// </summary>
/// <typeparam name="T">The payload type transported.</typeparam>
public interface IAsyncResponseStartAwareTransmitter<T> : IAsyncTransmitter<T>
{
    /// <summary>
    /// Receive the next payload while applying a separate cancellation token to the start of the response.
    /// </summary>
    /// <param name="responseStartCancellationToken">Token that bounds waiting for the first response frame.</param>
    /// <param name="cancellationToken">Token that cancels the whole receive operation.</param>
    /// <returns>A task whose result is the received payload.</returns>
    Task<T> ReceiveAsync(CancellationToken responseStartCancellationToken, CancellationToken cancellationToken = default);
}
