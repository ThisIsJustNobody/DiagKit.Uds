using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

using DiagKit.Uds.Contracts;
using DiagKit.Uds.Services;
using DiagKit.Uds.UdsLayer;

namespace DiagKit.Uds.Tests;

[TestClass]
public class UdsClientSessionTests
{
    [TestMethod]
    public async Task ConcurrentRequests_AreExecutedSeriallyInFifoOrder()
    {
        var client = new RecordingAsyncUdsClient { AutoRespond = false };
        await using var session = new UdsClientSession(client);

        var first = session.SendRequestAsync(new byte[] { 0x22, 0x00, 0x01 }, false, TestContext.CancellationToken);
        var second = session.SendRequestAsync(new byte[] { 0x22, 0x00, 0x02 }, false, TestContext.CancellationToken);
        var third = session.SendRequestAsync(new byte[] { 0x22, 0x00, 0x03 }, false, TestContext.CancellationToken);

        CollectionAssert.AreEqual(new byte[] { 0x22, 0x00, 0x01 }, (await ReadInvocationAsync(client)).Request);
        Assert.IsNull(await TryReadInvocationAsync(client, TimeSpan.FromMilliseconds(40)));

        await client.RespondAsync(new byte[] { 0x62, 0x00, 0x01 }, TestContext.CancellationToken);
        CollectionAssert.AreEqual(new byte[] { 0x22, 0x00, 0x02 }, (await ReadInvocationAsync(client)).Request);

        await client.RespondAsync(new byte[] { 0x62, 0x00, 0x02 }, TestContext.CancellationToken);
        CollectionAssert.AreEqual(new byte[] { 0x22, 0x00, 0x03 }, (await ReadInvocationAsync(client)).Request);

        await client.RespondAsync(new byte[] { 0x62, 0x00, 0x03 }, TestContext.CancellationToken);

        CollectionAssert.AreEqual(new byte[] { 0x62, 0x00, 0x01 }, (await first).ToArray());
        CollectionAssert.AreEqual(new byte[] { 0x62, 0x00, 0x02 }, (await second).ToArray());
        CollectionAssert.AreEqual(new byte[] { 0x62, 0x00, 0x03 }, (await third).ToArray());
        Assert.AreEqual(1, client.MaxConcurrent);
        Assert.IsFalse(client.ConcurrentViolation);
    }

    [TestMethod]
    public async Task TesterPresentEnabled_SendsSuppressedTesterPresentWhenIdle()
    {
        var client = new RecordingAsyncUdsClient();
        await using var session = new UdsClientSession(
            client,
            new UdsClientSessionOptions
            {
                TesterPresentEnabled = true,
                TesterPresentInterval = TimeSpan.FromMilliseconds(20),
            });

        var invocation = await ReadInvocationAsync(client, TimeSpan.FromMilliseconds(250));

        CollectionAssert.AreEqual(new byte[] { 0x3E, 0x80 }, invocation.Request);
        Assert.IsTrue(invocation.SuppressResponse.GetValueOrDefault());
    }

    [TestMethod]
    public async Task BusinessRequestInFlight_DoesNotInsertTesterPresent()
    {
        var client = new RecordingAsyncUdsClient
        {
            AutoRespond = true,
            ResponseDelay = TimeSpan.FromMilliseconds(160),
        };
        await using var session = new UdsClientSession(
            client,
            new UdsClientSessionOptions
            {
                TesterPresentEnabled = true,
                TesterPresentInterval = TimeSpan.FromMilliseconds(30),
            });

        var requestTask = session.SendRequestAsync(new byte[] { 0x10, 0x03 }, false, TestContext.CancellationToken);
        CollectionAssert.AreEqual(new byte[] { 0x10, 0x03 }, (await ReadInvocationAsync(client)).Request);

        Assert.IsNull(await TryReadInvocationAsync(client, TimeSpan.FromMilliseconds(90)));

        await requestTask;
        Assert.AreEqual(1, client.MaxConcurrent);
        Assert.IsFalse(client.ConcurrentViolation);
    }

    [TestMethod]
    public async Task BusinessRequestsRefreshS3_NoTesterPresentUntilIdleIntervalElapses()
    {
        var client = new RecordingAsyncUdsClient();
        await using var session = new UdsClientSession(
            client,
            new UdsClientSessionOptions
            {
                TesterPresentEnabled = true,
                TesterPresentInterval = TimeSpan.FromMilliseconds(80),
            });

        await session.SendRequestAsync(new byte[] { 0x10, 0x03 }, false, TestContext.CancellationToken);
        CollectionAssert.AreEqual(new byte[] { 0x10, 0x03 }, (await ReadInvocationAsync(client)).Request);

        await Task.Delay(40, TestContext.CancellationToken);
        await session.SendRequestAsync(new byte[] { 0x22, 0xF1, 0x90 }, false, TestContext.CancellationToken);
        CollectionAssert.AreEqual(new byte[] { 0x22, 0xF1, 0x90 }, (await ReadInvocationAsync(client)).Request);

        Assert.IsNull(await TryReadInvocationAsync(client, TimeSpan.FromMilliseconds(45)));

        var testerPresent = await ReadInvocationAsync(client, TimeSpan.FromMilliseconds(160));
        CollectionAssert.AreEqual(new byte[] { 0x3E, 0x80 }, testerPresent.Request);
    }

    [TestMethod]
    public async Task QueuedRequestCancellation_IsObservedBeforeExecution()
    {
        var client = new RecordingAsyncUdsClient { AutoRespond = false };
        await using var session = new UdsClientSession(client);

        var first = session.SendRequestAsync(new byte[] { 0x22, 0x00, 0x01 }, false, TestContext.CancellationToken);
        await ReadInvocationAsync(client);

        using var queuedCts = new CancellationTokenSource();
        var second = session.SendRequestAsync(new byte[] { 0x22, 0x00, 0x02 }, false, queuedCts.Token);

        await queuedCts.CancelAsync();
        await Assert.ThrowsExactlyAsync<TaskCanceledException>(() => second);

        await client.RespondAsync(new byte[] { 0x62, 0x00, 0x01 }, TestContext.CancellationToken);
        await first;

        Assert.IsNull(await TryReadInvocationAsync(client, TimeSpan.FromMilliseconds(60)));
    }

    [TestMethod]
    public async Task QueuedRequestCancellation_ReleasesQueueCapacity()
    {
        var client = new RecordingAsyncUdsClient { AutoRespond = false };
        await using var session = new UdsClientSession(
            client,
            new UdsClientSessionOptions { MaxQueueLength = 1, QueueFullMode = UdsClientSessionQueueFullMode.Throw });

        var first = session.SendRequestAsync(new byte[] { 0x22, 0x00, 0x01 }, false, TestContext.CancellationToken);
        await ReadInvocationAsync(client);

        using var queuedCts = new CancellationTokenSource();
        var canceled = session.SendRequestAsync(new byte[] { 0x22, 0x00, 0x02 }, false, queuedCts.Token);
        await queuedCts.CancelAsync();
        await Assert.ThrowsExactlyAsync<TaskCanceledException>(() => canceled);

        var replacement = session.SendRequestAsync(new byte[] { 0x22, 0x00, 0x03 }, false, TestContext.CancellationToken);

        await client.RespondAsync(new byte[] { 0x62, 0x00, 0x01 }, TestContext.CancellationToken);
        await first;

        CollectionAssert.AreEqual(new byte[] { 0x22, 0x00, 0x03 }, (await ReadInvocationAsync(client)).Request);
        await client.RespondAsync(new byte[] { 0x62, 0x00, 0x03 }, TestContext.CancellationToken);
        await replacement;
    }

    [TestMethod]
    public async Task QueueFullModeThrow_RejectsWhenQueueIsFull()
    {
        var client = new RecordingAsyncUdsClient { AutoRespond = false };
        await using var session = new UdsClientSession(
            client,
            new UdsClientSessionOptions { MaxQueueLength = 1, QueueFullMode = UdsClientSessionQueueFullMode.Throw });

        var first = session.SendRequestAsync(new byte[] { 0x22, 0x00, 0x01 }, false, TestContext.CancellationToken);
        await ReadInvocationAsync(client);
        var second = session.SendRequestAsync(new byte[] { 0x22, 0x00, 0x02 }, false, TestContext.CancellationToken);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            session.SendRequestAsync(new byte[] { 0x22, 0x00, 0x03 }, false, TestContext.CancellationToken));

        await client.RespondAsync(new byte[] { 0x62, 0x00, 0x01 }, TestContext.CancellationToken);
        await ReadInvocationAsync(client);
        await client.RespondAsync(new byte[] { 0x62, 0x00, 0x02 }, TestContext.CancellationToken);

        await first;
        await second;
    }

    [TestMethod]
    public async Task QueueFullModeWait_WaitsForCapacity()
    {
        var client = new RecordingAsyncUdsClient { AutoRespond = false };
        await using var session = new UdsClientSession(
            client,
            new UdsClientSessionOptions { MaxQueueLength = 1, QueueFullMode = UdsClientSessionQueueFullMode.Wait });

        var first = session.SendRequestAsync(new byte[] { 0x22, 0x00, 0x01 }, false, TestContext.CancellationToken);
        await ReadInvocationAsync(client);
        var second = session.SendRequestAsync(new byte[] { 0x22, 0x00, 0x02 }, false, TestContext.CancellationToken);
        var third = session.SendRequestAsync(new byte[] { 0x22, 0x00, 0x03 }, false, TestContext.CancellationToken);

        Assert.IsNull(await TryReadInvocationAsync(client, TimeSpan.FromMilliseconds(50)));

        await client.RespondAsync(new byte[] { 0x62, 0x00, 0x01 }, TestContext.CancellationToken);
        CollectionAssert.AreEqual(new byte[] { 0x22, 0x00, 0x02 }, (await ReadInvocationAsync(client)).Request);
        Assert.IsFalse(third.IsCompleted);

        await client.RespondAsync(new byte[] { 0x62, 0x00, 0x02 }, TestContext.CancellationToken);
        CollectionAssert.AreEqual(new byte[] { 0x22, 0x00, 0x03 }, (await ReadInvocationAsync(client)).Request);
        await client.RespondAsync(new byte[] { 0x62, 0x00, 0x03 }, TestContext.CancellationToken);

        await first;
        await second;
        await third;
    }

    [TestMethod]
    public async Task QueueFullModeWait_CancelledWaiterCompletesImmediately()
    {
        var client = new RecordingAsyncUdsClient { AutoRespond = false };
        await using var session = new UdsClientSession(
            client,
            new UdsClientSessionOptions { MaxQueueLength = 1, QueueFullMode = UdsClientSessionQueueFullMode.Wait });

        var first = session.SendRequestAsync(new byte[] { 0x22, 0x00, 0x01 }, false, TestContext.CancellationToken);
        await ReadInvocationAsync(client);
        var second = session.SendRequestAsync(new byte[] { 0x22, 0x00, 0x02 }, false, TestContext.CancellationToken);

        using var waitingCts = new CancellationTokenSource();
        var waiting = session.SendRequestAsync(new byte[] { 0x22, 0x00, 0x03 }, false, waitingCts.Token);
        await Task.Delay(TimeSpan.FromMilliseconds(20), TestContext.CancellationToken);

        await waitingCts.CancelAsync();
        try
        {
            await waiting.WaitAsync(TimeSpan.FromMilliseconds(150));
            Assert.Fail("The request waiting for queue capacity should have been cancelled.");
        }
        catch (OperationCanceledException)
        {
        }

        await client.RespondAsync(new byte[] { 0x62, 0x00, 0x01 }, TestContext.CancellationToken);
        await first;
        CollectionAssert.AreEqual(new byte[] { 0x22, 0x00, 0x02 }, (await ReadInvocationAsync(client)).Request);

        await client.RespondAsync(new byte[] { 0x62, 0x00, 0x02 }, TestContext.CancellationToken);
        await second;
        Assert.IsNull(await TryReadInvocationAsync(client, TimeSpan.FromMilliseconds(60)));
    }

    [TestMethod]
    public async Task ServiceHelpers_CanUseUdsClientSession()
    {
        var client = new RecordingAsyncUdsClient();
        await using var session = new UdsClientSession(client);

        var response = await DiagnosticSessionControl.InvokeAsync(
            session,
            DiagnosticSessionType.ExtendedDiagnostic,
            TestContext.CancellationToken);

        Assert.AreEqual(DiagnosticSessionType.ExtendedDiagnostic, response.Session);
        CollectionAssert.AreEqual(new byte[] { 0x10, 0x03 }, (await ReadInvocationAsync(client)).Request);
    }

    [TestMethod]
    public async Task SessionOptions_AreClonedAtConstructionAndOnAccess()
    {
        var client = new RecordingAsyncUdsClient { AutoRespond = false };
        var options = new UdsClientSessionOptions
        {
            MaxQueueLength = 1,
            QueueFullMode = UdsClientSessionQueueFullMode.Throw,
        };
        await using var session = new UdsClientSession(client, options);

        options.MaxQueueLength = 3;
        session.SessionOptions.MaxQueueLength = 3;

        var first = session.SendRequestAsync(new byte[] { 0x22, 0x00, 0x01 }, false, TestContext.CancellationToken);
        await ReadInvocationAsync(client);
        var second = session.SendRequestAsync(new byte[] { 0x22, 0x00, 0x02 }, false, TestContext.CancellationToken);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            session.SendRequestAsync(new byte[] { 0x22, 0x00, 0x03 }, false, TestContext.CancellationToken));

        await client.RespondAsync(new byte[] { 0x62, 0x00, 0x01 }, TestContext.CancellationToken);
        await first;
        await ReadInvocationAsync(client);
        await client.RespondAsync(new byte[] { 0x62, 0x00, 0x02 }, TestContext.CancellationToken);
        await second;
    }

    [TestMethod]
    public async Task DisposeAsync_CancelsInFlightAndQueuedRequests()
    {
        var client = new RecordingAsyncUdsClient { AutoRespond = false };
        var session = new UdsClientSession(client);

        var first = session.SendRequestAsync(new byte[] { 0x22, 0x00, 0x01 }, false, TestContext.CancellationToken);
        await ReadInvocationAsync(client);
        var second = session.SendRequestAsync(new byte[] { 0x22, 0x00, 0x02 }, false, TestContext.CancellationToken);

        await session.DisposeAsync();

        await Assert.ThrowsExactlyAsync<TaskCanceledException>(() => first);
        await Assert.ThrowsExactlyAsync<TaskCanceledException>(() => second);
        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() =>
            session.SendRequestAsync(new byte[] { 0x22, 0x00, 0x03 }, false, TestContext.CancellationToken));
    }

    [TestMethod]
    public async Task DisposeAsync_RacingQueuedCancellation_DoesNotDeadlock()
    {
        for (var i = 0; i < 25; i++)
        {
            var client = new RecordingAsyncUdsClient { AutoRespond = false };
            var session = new UdsClientSession(client);

            var first = session.SendRequestAsync(new byte[] { 0x22, 0x00, 0x01 }, false, TestContext.CancellationToken);
            await ReadInvocationAsync(client);

            using var queuedCts = new CancellationTokenSource();
            var queued = session.SendRequestAsync(new byte[] { 0x22, 0x00, 0x02 }, false, queuedCts.Token);

            var cancelTask = Task.Run(async () => await queuedCts.CancelAsync(), TestContext.CancellationToken);
            var disposeTask = Task.Run(async () => await session.DisposeAsync(), TestContext.CancellationToken);

            await Task.WhenAll(cancelTask, disposeTask).WaitAsync(TimeSpan.FromSeconds(1), TestContext.CancellationToken);
            await AssertOperationCanceledAsync(first);
            await AssertOperationCanceledAsync(queued);
        }
    }

    [TestMethod]
    public async Task DisposeAsync_StopsTesterPresentLoop()
    {
        var client = new RecordingAsyncUdsClient();
        var session = new UdsClientSession(
            client,
            new UdsClientSessionOptions
            {
                TesterPresentEnabled = true,
                TesterPresentInterval = TimeSpan.FromMilliseconds(20),
            });

        await ReadInvocationAsync(client, TimeSpan.FromMilliseconds(250));
        await session.DisposeAsync();

        var countAfterDispose = client.InvocationCount;
        await Task.Delay(TimeSpan.FromMilliseconds(90), TestContext.CancellationToken);
        Assert.AreEqual(countAfterDispose, client.InvocationCount);
    }

    [TestMethod]
    public async Task TesterPresentFailure_FaultsCompletionAndFutureRequests()
    {
        var client = new RecordingAsyncUdsClient
        {
            TesterPresentFailure = new InvalidOperationException("tester present failed"),
        };
        var session = new UdsClientSession(
            client,
            new UdsClientSessionOptions
            {
                TesterPresentEnabled = true,
                TesterPresentInterval = TimeSpan.FromMilliseconds(20),
            });

        var invocation = await ReadInvocationAsync(client, TimeSpan.FromMilliseconds(250));
        CollectionAssert.AreEqual(new byte[] { 0x3E, 0x80 }, invocation.Request);

        var completionEx = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            session.Completion.WaitAsync(TimeSpan.FromMilliseconds(250)));
        Assert.AreEqual("tester present failed", completionEx.Message);

        var requestEx = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            session.SendRequestAsync(new byte[] { 0x22, 0xF1, 0x90 }, false, TestContext.CancellationToken));
        Assert.AreEqual("tester present failed", requestEx.Message);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => session.DisposeAsync().AsTask());
    }

    private async Task<ClientInvocation> ReadInvocationAsync(RecordingAsyncUdsClient client)
        => await ReadInvocationAsync(client, TimeSpan.FromMilliseconds(250));

    private async Task<ClientInvocation> ReadInvocationAsync(RecordingAsyncUdsClient client, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (client.Invocations.Reader.TryRead(out var invocation))
                return invocation;
            await Task.Delay(TimeSpan.FromMilliseconds(5), TestContext.CancellationToken);
        }

        Assert.Fail("Timed out waiting for a client invocation.");
        return null!;
    }

    private async Task<ClientInvocation?> TryReadInvocationAsync(RecordingAsyncUdsClient client, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (client.Invocations.Reader.TryRead(out var invocation))
                return invocation;
            await Task.Delay(TimeSpan.FromMilliseconds(5), TestContext.CancellationToken);
        }

        return null;
    }

    private static async Task AssertOperationCanceledAsync(Task task)
    {
        try
        {
            await task;
            Assert.Fail("Expected the task to be cancelled.");
        }
        catch (OperationCanceledException)
        {
        }
    }

    private sealed record ClientInvocation(byte[] Request, bool? SuppressResponse);

    private sealed class RecordingAsyncUdsClient : IAsyncUdsClient
    {
        private readonly Channel<ReadOnlyMemory<byte>> _responses = Channel.CreateUnbounded<ReadOnlyMemory<byte>>();
        private readonly object _sync = new();
        private int _active;

        public Channel<ClientInvocation> Invocations { get; } = Channel.CreateUnbounded<ClientInvocation>();

        public UdsOptions Options { get; init; } = new();

        public bool AutoRespond { get; init; } = true;

        public TimeSpan ResponseDelay { get; init; }

        public Exception? TesterPresentFailure { get; init; }

        public bool ConcurrentViolation { get; private set; }

        public int MaxConcurrent { get; private set; }

        public int InvocationCount { get; private set; }

        public async Task RespondAsync(ReadOnlyMemory<byte> response, CancellationToken cancellationToken)
            => await _responses.Writer.WriteAsync(response, cancellationToken);

        public async Task<ReadOnlyMemory<byte>> SendRequestAsync(
            ReadOnlyMemory<byte> request,
            bool? suppressResponse = null,
            CancellationToken cancellationToken = default)
        {
            var active = Interlocked.Increment(ref _active);
            lock (_sync)
            {
                MaxConcurrent = Math.Max(MaxConcurrent, active);
                if (active > 1)
                    ConcurrentViolation = true;
            }

            try
            {
                if (active > 1)
                    throw new InvalidOperationException("Underlying client was called concurrently.");

                await Invocations.Writer.WriteAsync(new ClientInvocation(request.ToArray(), suppressResponse), cancellationToken);
                lock (_sync)
                {
                    InvocationCount++;
                }

                if (TesterPresentFailure is not null && request.Span.Length > 0 && request.Span[0] == (byte)UdsServiceId.TesterPresent)
                    throw TesterPresentFailure;

                if (!AutoRespond)
                    return await _responses.Reader.ReadAsync(cancellationToken);

                if (ResponseDelay > TimeSpan.Zero)
                    await Task.Delay(ResponseDelay, cancellationToken);

                return suppressResponse == true ? ReadOnlyMemory<byte>.Empty : BuildPositiveResponse(request.Span);
            }
            finally
            {
                Interlocked.Decrement(ref _active);
            }
        }

        private static byte[] BuildPositiveResponse(ReadOnlySpan<byte> request)
        {
            var response = new byte[request.Length];
            response[0] = (byte)(request[0] + UdsMessage.PositiveResponseOffset);
            request[1..].CopyTo(response.AsSpan(1));
            return response;
        }
    }

    public TestContext TestContext { get; set; }
}
