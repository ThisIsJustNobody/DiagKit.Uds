using System.Collections.Generic;
using System.Threading.Channels;

using CanHub;

using DiagKit.Uds.CanHub;
using DiagKit.Uds.DoCan;

using CanHubFrame = CanHub.CanFrame;
using DiagCanFrame = DiagKit.Uds.DoCan.CanFrame;

namespace DiagKit.Uds.CanHub.Tests;

[TestClass]
public class CanHubBridgeTests
{
    [TestMethod]
    public async Task SendAsync_ConvertsClassicCanFrame()
    {
        var bus = new StubCanBus();
        using var transmitter = new CanHubCanTransmitter(bus);

        await transmitter.SendAsync(DiagCanFrame.CreateCopy(0x7E0, new byte[] { 0x01, 0x02 }), TestContext.CancellationToken);

        Assert.HasCount(1, bus.SentFrames);
        var sent = bus.SentFrames[0];
        Assert.AreEqual(CanFrameKind.Data, sent.Kind);
        Assert.AreEqual(0x7E0u, sent.Id.Value);
        Assert.IsFalse(sent.Id.IsExtended);
        CollectionAssert.AreEqual(new byte[] { 0x01, 0x02 }, CopyPayload(sent));
    }

    [TestMethod]
    public async Task SendAsync_ConvertsExtendedFdBrsFrame()
    {
        var bus = new StubCanBus();
        using var transmitter = new CanHubCanTransmitter(bus);

        await transmitter.SendAsync(
            DiagCanFrame.CreateCopy(0x18DAF110, new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B, 0x0C }, fd: true, brs: true, extendedId: true),
            TestContext.CancellationToken);

        var sent = bus.SentFrames[0];
        Assert.AreEqual(0x18DAF110u, sent.Id.Value);
        Assert.IsTrue(sent.Id.IsExtended);
        Assert.AreNotEqual(CanFrameFlags.None, sent.Flags & CanFrameFlags.FD);
        Assert.AreNotEqual(CanFrameFlags.None, sent.Flags & CanFrameFlags.BRS);
        CollectionAssert.AreEqual(new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B, 0x0C }, CopyPayload(sent));
    }

    [TestMethod]
    public async Task SendAsync_RejectedSubmissionThrowsBridgeException()
    {
        var bus = new StubCanBus
        {
            SendResult = CanTransmitSubmissionResult.Failed(42, CanTransmitSubmissionStatus.QueueFull),
        };
        using var transmitter = new CanHubCanTransmitter(bus);

        await Assert.ThrowsExactlyAsync<CanHubBridgeException>(() =>
            transmitter.SendAsync(DiagCanFrame.CreateCopy(0x7E0, new byte[] { 0x01 }), TestContext.CancellationToken));
    }

    [TestMethod]
    public async Task ReceiveAsync_FiltersNonReceiveDataFrames()
    {
        var bus = new StubCanBus();
        using var transmitter = new CanHubCanTransmitter(bus);

        bus.Subscription.Publish(CanFrameEvent.CreateTransmitted(1, CanHubFrame.CreateData(CanId.Standard(0x7E0), [0x99]), CanTransmitOutcome.Transmitted));
        bus.Subscription.Publish(CanFrameEvent.CreateReceived(CanHubFrame.CreateRemote(CanId.Standard(0x7E8), 1), sequence: 2));
        bus.Subscription.Publish(CanFrameEvent.CreateReceived(CanHubFrame.CreateData(CanId.Standard(0x7E8), [0x62, 0xF1, 0x90]), sequence: 3));

        var received = await transmitter.ReceiveAsync(TestContext.CancellationToken);

        Assert.AreEqual(0x7E8u, received.CanId);
        Assert.IsFalse(received.ExtendedId);
        Assert.IsFalse(received.FdFlag);
        CollectionAssert.AreEqual(new byte[] { 0x62, 0xF1, 0x90 }, received.Data.ToArray());
    }

    [TestMethod]
    public async Task ClearReceiveBuffer_DrainsBufferedFrames()
    {
        var bus = new StubCanBus();
        using var transmitter = new CanHubCanTransmitter(bus);

        bus.Subscription.Publish(CanFrameEvent.CreateReceived(CanHubFrame.CreateData(CanId.Standard(0x100), [0x01]), sequence: 1));
        bus.Subscription.Publish(CanFrameEvent.CreateReceived(CanHubFrame.CreateData(CanId.Standard(0x101), [0x02]), sequence: 2));
        await bus.Subscription.WaitForReadCountAsync(2, TestContext.CancellationToken);

        transmitter.ClearReceiveBuffer();
        bus.Subscription.Publish(CanFrameEvent.CreateReceived(CanHubFrame.CreateData(CanId.Standard(0x102), [0x03]), sequence: 3));

        var received = await transmitter.ReceiveAsync(TestContext.CancellationToken);

        Assert.AreEqual(0x102u, received.CanId);
        CollectionAssert.AreEqual(new byte[] { 0x03 }, received.Data.ToArray());
    }

    [TestMethod]
    public void Dispose_DisposesSubscriptionButNotBus()
    {
        var bus = new StubCanBus();
        var transmitter = new CanHubCanTransmitter(bus);

        transmitter.Dispose();

        Assert.IsTrue(bus.Subscription.Disposed);
        Assert.IsFalse(bus.Disposed);
    }

    private static byte[] CopyPayload(CanHubFrame frame)
    {
        var payload = new byte[frame.PayloadLength];
        frame.CopyPayloadTo(payload);
        return payload;
    }

    private sealed class StubCanBus : ICanBus
    {
        public StubCanSubscription Subscription { get; } = new();

        public List<CanHubFrame> SentFrames { get; } = [];

        public CanTransmitSubmissionResult SendResult { get; init; } = CanTransmitSubmissionResult.AcceptedResult(1);

        public bool Disposed { get; private set; }

        public string DisplayName => "stub";

        public bool IsOpen => !Disposed;

        public event Action<CanStatusEvent>? StatusChanged;

        public ValueTask<CanTransmitSubmissionResult> SendAsync(CanHubFrame frame, CanTransmitOptions? options = null, CancellationToken ct = default)
        {
            SentFrames.Add(frame);
            return ValueTask.FromResult(SendResult);
        }

        public ValueTask<CanTransmitSubmissionResult[]> SendBatchAsync(ReadOnlyMemory<CanHubFrame> frames, CanTransmitOptions? options = null, CancellationToken ct = default)
            => ValueTask.FromResult(Array.Empty<CanTransmitSubmissionResult>());

        public ICanSubscription Subscribe(CanSubscriptionOptions options) => Subscription;

        public void Dispose() => Disposed = true;

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }

        public void RaiseStatusChanged(CanStatusEvent status) => StatusChanged?.Invoke(status);
    }

    private sealed class StubCanSubscription : ICanSubscription
    {
        private readonly Channel<CanFrameEvent> _frames = Channel.CreateUnbounded<CanFrameEvent>();
        private readonly TaskCompletionSource _readCountReached = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int ReadCount { get; private set; }

        public bool Disposed { get; private set; }

        public CanSubscriptionStatistics Statistics => default;

        public void Publish(CanFrameEvent frameEvent) => _frames.Writer.TryWrite(frameEvent);

        public async ValueTask<CanFrameEvent> ReadAsync(CancellationToken ct = default)
        {
            var frameEvent = await _frames.Reader.ReadAsync(ct);
            ReadCount++;
            if (ReadCount >= 2)
                _readCountReached.TrySetResult();
            return frameEvent;
        }

        public async IAsyncEnumerable<CanFrameEvent> ReadAllAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            while (await _frames.Reader.WaitToReadAsync(ct))
                while (_frames.Reader.TryRead(out var frameEvent))
                    yield return frameEvent;
        }

        public async Task WaitForReadCountAsync(int count, CancellationToken cancellationToken)
        {
            if (ReadCount >= count) return;
            await _readCountReached.Task.WaitAsync(cancellationToken);
        }

        public void Dispose() => Disposed = true;
    }

    public TestContext TestContext { get; set; }
}
