using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

using DiagKit.Uds.DoCan;
using DiagKit.Uds.Exceptions;

namespace DiagKit.Uds.Tests;

[TestClass]
public class DoCanTransmitterTests
{
    private static (AsyncDoCanTransmitter sender, AsyncDoCanTransmitter receiver) BuildPair(DoCanOptions? senderOpts = null, DoCanOptions? receiverOpts = null)
    {
        var sToR = Channel.CreateUnbounded<CanFrame>();
        var rToS = Channel.CreateUnbounded<CanFrame>();

        senderOpts ??= new DoCanOptions { RequestId = 0x7E0, ResponseId = 0x7E8 };
        receiverOpts ??= new DoCanOptions { RequestId = 0x7E8, ResponseId = 0x7E0 };

        var sender = new AsyncDoCanTransmitter(sToR, rToS, senderOpts);
        var receiver = new AsyncDoCanTransmitter(rToS, sToR, receiverOpts);
        return (sender, receiver);
    }

    [TestMethod]
    public async Task SingleFrame_RoundTrip()
    {
        var (sender, receiver) = BuildPair();
        var msg = new byte[] { 0x22, 0xF1, 0x90 };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var sendTask = sender.SendAsync(msg, cts.Token);
        var recvTask = receiver.ReceiveAsync(cts.Token);
        await sendTask;
        var received = await recvTask;
        CollectionAssert.AreEqual(msg, received.ToArray());
    }

    [TestMethod]
    [DataRow(8)]
    [DataRow(20)]
    [DataRow(30)]
    [DataRow(62)]
    public async Task CanFdSingleFrame_RoundTrip(int payloadLength)
    {
        var senderOpts = new DoCanOptions { RequestId = 0x7E0, ResponseId = 0x7E8, UseFd = true, MaxDlc = 15 };
        var receiverOpts = new DoCanOptions { RequestId = 0x7E8, ResponseId = 0x7E0, UseFd = true, MaxDlc = 15 };
        var (sender, receiver) = BuildPair(senderOpts, receiverOpts);
        var msg = Enumerable.Range(0, payloadLength).Select(i => (byte)i).ToArray();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var sendTask = sender.SendAsync(msg, cts.Token);
        var recvTask = receiver.ReceiveAsync(cts.Token);
        await Task.WhenAll(sendTask, recvTask);

        CollectionAssert.AreEqual(msg, recvTask.Result.ToArray());
    }

    [TestMethod]
    public async Task CanFdShortSingleFrame_WithFdMinDlc_RoundTrip()
    {
        var senderOpts = new DoCanOptions { RequestId = 0x7E0, ResponseId = 0x7E8, UseFd = true, MinDlc = 9, MaxDlc = 15 };
        var receiverOpts = new DoCanOptions { RequestId = 0x7E8, ResponseId = 0x7E0, UseFd = true, MinDlc = 9, MaxDlc = 15 };
        var (sender, receiver) = BuildPair(senderOpts, receiverOpts);
        var msg = new byte[] { 0x22, 0xF1, 0x90, 0x01, 0x02 };

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var sendTask = sender.SendAsync(msg, cts.Token);
        var recvTask = receiver.ReceiveAsync(cts.Token);
        await Task.WhenAll(sendTask, recvTask);

        CollectionAssert.AreEqual(msg, recvTask.Result.ToArray());
    }

    [TestMethod]
    [DataRow(8)]
    [DataRow(20)]
    [DataRow(30)]
    [DataRow(62)]
    public void SyncCanFdSingleFrame_RoundTrip(int payloadLength)
    {
        var sToR = new BlockingCollection<CanFrame>();
        var rToS = new BlockingCollection<CanFrame>();
        var senderOpts = new DoCanOptions { RequestId = 0x7E0, ResponseId = 0x7E8, UseFd = true, MaxDlc = 15 };
        var receiverOpts = new DoCanOptions { RequestId = 0x7E8, ResponseId = 0x7E0, UseFd = true, MaxDlc = 15 };
        var sender = new DoCanTransmitter(sToR, rToS, senderOpts);
        var receiver = new DoCanTransmitter(rToS, sToR, receiverOpts);
        var msg = Enumerable.Range(0, payloadLength).Select(i => (byte)i).ToArray();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        sender.Send(msg, cts.Token);
        var received = receiver.Receive(cts.Token);

        CollectionAssert.AreEqual(msg, received.ToArray());
    }

    [TestMethod]
    public async Task SegmentedTransfer_RoundTrip()
    {
        var (sender, receiver) = BuildPair();
        var msg = new byte[200];
        new Random(42).NextBytes(msg);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var sendTask = sender.SendAsync(msg, cts.Token);
        var recvTask = receiver.ReceiveAsync(cts.Token);
        await Task.WhenAll(sendTask, recvTask);
        CollectionAssert.AreEqual(msg, recvTask.Result.ToArray());
    }

    [TestMethod]
    public async Task SegmentedTransfer_LargePayload_RoundTrip()
    {
        var senderOpts = new DoCanOptions { RequestId = 0x7E0, ResponseId = 0x7E8, UseFd = true, MaxDlc = 15 };
        var receiverOpts = new DoCanOptions { RequestId = 0x7E8, ResponseId = 0x7E0, UseFd = true, MaxDlc = 15 };
        var (sender, receiver) = BuildPair(senderOpts, receiverOpts);
        var msg = new byte[2000];
        new Random(7).NextBytes(msg);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var sendTask = sender.SendAsync(msg, cts.Token);
        var recvTask = receiver.ReceiveAsync(cts.Token);
        await Task.WhenAll(sendTask, recvTask);
        CollectionAssert.AreEqual(msg, recvTask.Result.ToArray());
    }

    [TestMethod]
    public async Task SegmentedTransfer_WithNonZeroBlockSize_RoundTrip()
    {
        var senderOpts = new DoCanOptions { RequestId = 0x7E0, ResponseId = 0x7E8 };
        var receiverOpts = new DoCanOptions { RequestId = 0x7E8, ResponseId = 0x7E0, BlockSize = 2 };
        var (sender, receiver) = BuildPair(senderOpts, receiverOpts);
        var msg = new byte[80];
        new Random(11).NextBytes(msg);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var sendTask = sender.SendAsync(msg, cts.Token);
        var recvTask = receiver.ReceiveAsync(cts.Token);
        await Task.WhenAll(sendTask, recvTask);

        CollectionAssert.AreEqual(msg, recvTask.Result.ToArray());
    }

    [TestMethod]
    public async Task SyncReceiveSegmented_RoundTrips()
    {
        var sToR = new BlockingCollection<CanFrame>();
        var rToS = new BlockingCollection<CanFrame>();
        var sender = new DoCanTransmitter(
            sToR,
            rToS,
            new DoCanOptions { RequestId = 0x7E0, ResponseId = 0x7E8 });
        var receiver = new DoCanTransmitter(
            rToS,
            sToR,
            new DoCanOptions { RequestId = 0x7E8, ResponseId = 0x7E0, BlockSize = 3 });
        var msg = new byte[120];
        new Random(19).NextBytes(msg);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var sendTask = Task.Run(() => sender.Send(msg, cts.Token), cts.Token);
        var received = receiver.Receive(cts.Token);
        await sendTask;

        CollectionAssert.AreEqual(msg, received.ToArray());
    }

    [TestMethod]
    public async Task FlowStatusWait_DelaysUntilContinue()
    {
        var sToR = Channel.CreateUnbounded<CanFrame>();
        var rToS = Channel.CreateUnbounded<CanFrame>();
        var sender = new AsyncDoCanTransmitter(
            sToR,
            rToS,
            new DoCanOptions
            {
                RequestId = 0x100,
                ResponseId = 0x101,
                FlowControlWaitInterval = TimeSpan.FromMilliseconds(20),
            });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var sendTask = sender.SendAsync(new byte[80], cts.Token);

        var first = await sToR.Reader.ReadAsync(cts.Token);
        Assert.AreEqual(DoCanFrameType.FirstFrame, DoCanFraming.GetFrameType(first.Data.Span));

        var waitFc = DoCanFraming.EncodeFlowControlFrame(DoCanFlowStatus.Wait, 0, 0);
        await rToS.Writer.WriteAsync(new CanFrame { CanId = 0x101, Data = waitFc }, cts.Token);
        await Task.Delay(TimeSpan.FromMilliseconds(60), cts.Token);
        Assert.IsFalse(sToR.Reader.TryRead(out _), "Consecutive frames must not be sent while FC Wait is active.");

        var continueFc = DoCanFraming.EncodeFlowControlFrame(DoCanFlowStatus.Continue, 0, 0);
        await rToS.Writer.WriteAsync(new CanFrame { CanId = 0x101, Data = continueFc }, cts.Token);

        await sendTask;
        Assert.IsTrue(sToR.Reader.TryRead(out var cf));
        Assert.AreEqual(DoCanFrameType.ConsecutiveFrame, DoCanFraming.GetFrameType(cf.Data.Span));
    }

    [TestMethod]
    public async Task FlowStatusWait_AllowsExactlyConfiguredLimit()
    {
        var sToR = Channel.CreateUnbounded<CanFrame>();
        var rToS = Channel.CreateUnbounded<CanFrame>();
        var sender = new AsyncDoCanTransmitter(
            sToR,
            rToS,
            new DoCanOptions
            {
                RequestId = 0x100,
                ResponseId = 0x101,
                FlowControlWaitInterval = TimeSpan.FromMilliseconds(1),
                MaxFlowControlWaitFrames = 1,
            });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var sendTask = sender.SendAsync(new byte[80], cts.Token);

        var first = await sToR.Reader.ReadAsync(cts.Token);
        Assert.AreEqual(DoCanFrameType.FirstFrame, DoCanFraming.GetFrameType(first.Data.Span));

        var waitFc = DoCanFraming.EncodeFlowControlFrame(DoCanFlowStatus.Wait, 0, 0);
        await rToS.Writer.WriteAsync(new CanFrame { CanId = 0x101, Data = waitFc }, cts.Token);
        var continueFc = DoCanFraming.EncodeFlowControlFrame(DoCanFlowStatus.Continue, 0, 0);
        await rToS.Writer.WriteAsync(new CanFrame { CanId = 0x101, Data = continueFc }, cts.Token);

        await sendTask;
    }

    [TestMethod]
    public async Task FlowStatusWait_ZeroLimitRejectsFirstWaitFrame()
    {
        var sToR = Channel.CreateUnbounded<CanFrame>();
        var rToS = Channel.CreateUnbounded<CanFrame>();
        var sender = new AsyncDoCanTransmitter(
            sToR,
            rToS,
            new DoCanOptions
            {
                RequestId = 0x100,
                ResponseId = 0x101,
                FlowControlWaitInterval = TimeSpan.FromMilliseconds(1),
                MaxFlowControlWaitFrames = 0,
            });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var sendTask = sender.SendAsync(new byte[80], cts.Token);

        var first = await sToR.Reader.ReadAsync(cts.Token);
        Assert.AreEqual(DoCanFrameType.FirstFrame, DoCanFraming.GetFrameType(first.Data.Span));

        var waitFc = DoCanFraming.EncodeFlowControlFrame(DoCanFlowStatus.Wait, 0, 0);
        await rToS.Writer.WriteAsync(new CanFrame { CanId = 0x101, Data = waitFc }, cts.Token);

        await Assert.ThrowsExactlyAsync<ProtocolException>(() => sendTask);
    }

    [TestMethod]
    public async Task FlowStatusWait_ExceedingLimit_Throws()
    {
        var sToR = Channel.CreateUnbounded<CanFrame>();
        var rToS = Channel.CreateUnbounded<CanFrame>();
        var sender = new AsyncDoCanTransmitter(
            sToR,
            rToS,
            new DoCanOptions
            {
                RequestId = 0x100,
                ResponseId = 0x101,
                FlowControlWaitInterval = TimeSpan.FromMilliseconds(1),
                MaxFlowControlWaitFrames = 1,
            });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var sendTask = sender.SendAsync(new byte[80], cts.Token);

        var first = await sToR.Reader.ReadAsync(cts.Token);
        Assert.AreEqual(DoCanFrameType.FirstFrame, DoCanFraming.GetFrameType(first.Data.Span));

        var waitFc = DoCanFraming.EncodeFlowControlFrame(DoCanFlowStatus.Wait, 0, 0);
        await rToS.Writer.WriteAsync(new CanFrame { CanId = 0x101, Data = waitFc }, cts.Token);
        await rToS.Writer.WriteAsync(new CanFrame { CanId = 0x101, Data = waitFc }, cts.Token);

        await Assert.ThrowsExactlyAsync<ProtocolException>(() => sendTask);
    }

    [TestMethod]
    public async Task SyncFlowStatusWait_ExceedingLimit_Throws()
    {
        var sToR = new BlockingCollection<CanFrame>();
        var rToS = new BlockingCollection<CanFrame>();
        var sender = new DoCanTransmitter(
            sToR,
            rToS,
            new DoCanOptions
            {
                RequestId = 0x100,
                ResponseId = 0x101,
                FlowControlWaitInterval = TimeSpan.FromMilliseconds(1),
                MaxFlowControlWaitFrames = 1,
            });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var sendTask = Task.Run(() => sender.Send(new byte[80], cts.Token), cts.Token);

        var first = sToR.Take(cts.Token);
        Assert.AreEqual(DoCanFrameType.FirstFrame, DoCanFraming.GetFrameType(first.Data.Span));

        var waitFc = DoCanFraming.EncodeFlowControlFrame(DoCanFlowStatus.Wait, 0, 0);
        rToS.Add(new CanFrame { CanId = 0x101, Data = waitFc }, cts.Token);
        rToS.Add(new CanFrame { CanId = 0x101, Data = waitFc }, cts.Token);

        await Assert.ThrowsExactlyAsync<ProtocolException>(async () => await sendTask);
    }

    [TestMethod]
    public async Task ReceiveSegmented_ConsecutiveFrameSequenceMismatch_Throws()
    {
        var inbound = Channel.CreateUnbounded<CanFrame>();
        var outbound = Channel.CreateUnbounded<CanFrame>();
        var receiver = new AsyncDoCanTransmitter(
            outbound,
            inbound,
            new DoCanOptions { RequestId = 0x7E8, ResponseId = 0x7E0 });
        var msg = Enumerable.Range(0, 20).Select(i => (byte)i).ToArray();
        var firstFrame = DoCanFraming.EncodeFirstFrame(msg, 8, out var consumed);
        var wrongConsecutiveFrame = DoCanFraming.EncodeConsecutiveFrame(msg.AsSpan(consumed), 2, 8);

        await inbound.Writer.WriteAsync(new CanFrame { CanId = 0x7E0, Data = firstFrame }, TestContext.CancellationToken);
        await inbound.Writer.WriteAsync(new CanFrame { CanId = 0x7E0, Data = wrongConsecutiveFrame }, TestContext.CancellationToken);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await Assert.ThrowsExactlyAsync<FrameFormatException>(() => receiver.ReceiveAsync(cts.Token));
    }

    [TestMethod]
    public async Task ReceiveSegmented_LengthOverLimit_SendsOverflowAndThrowsBeforeAllocation()
    {
        var inbound = Channel.CreateUnbounded<CanFrame>();
        var outbound = Channel.CreateUnbounded<CanFrame>();
        var receiver = new AsyncDoCanTransmitter(
            outbound,
            inbound,
            new DoCanOptions
            {
                RequestId = 0x7E8,
                ResponseId = 0x7E0,
                MaxSegmentedPayloadLength = 10,
            });
        var payload = Enumerable.Range(0, 20).Select(i => (byte)i).ToArray();
        var firstFrame = DoCanFraming.EncodeFirstFrame(payload, 8, out _);

        await inbound.Writer.WriteAsync(new CanFrame { CanId = 0x7E0, Data = firstFrame }, TestContext.CancellationToken);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await Assert.ThrowsExactlyAsync<ProtocolException>(() => receiver.ReceiveAsync(cts.Token));
        var flowControl = await outbound.Reader.ReadAsync(cts.Token);

        Assert.IsTrue(DoCanFraming.TryReadFlowControlFrame(flowControl.Data.Span, out var status, out _, out _));
        Assert.AreEqual(DoCanFlowStatus.Overflow, status);
    }

    [TestMethod]
    public async Task MismatchedCanIds_AreDiscarded()
    {
        var sToR = Channel.CreateUnbounded<CanFrame>();
        var rToS = Channel.CreateUnbounded<CanFrame>();
        var senderOpts = new DoCanOptions { RequestId = 0x100, ResponseId = 0x101 };
        var receiverOpts = new DoCanOptions { RequestId = 0x101, ResponseId = 0x100 };
        var s = new AsyncDoCanTransmitter(sToR, rToS, senderOpts);
        var r = new AsyncDoCanTransmitter(rToS, sToR, receiverOpts);

        // Inject noise frame on the receiver's input that should be ignored.
        await sToR.Writer.WriteAsync(new CanFrame { CanId = 0x999, Data = new byte[] { 0xFF } }, TestContext.CancellationToken);

        var msg = new byte[] { 0x10, 0x01 };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var sendTask = s.SendAsync(msg, cts.Token);
        var recvTask = r.ReceiveAsync(cts.Token);
        await Task.WhenAll(sendTask, recvTask);
        CollectionAssert.AreEqual(msg, recvTask.Result.ToArray());
    }

    [TestMethod]
    [DataRow(DoCanFrameMixingMode.Accept)]
    [DataRow(DoCanFrameMixingMode.Adapt)]
    public async Task FrameMixingMode_AcceptAndAdapt_AcceptFdMismatch(DoCanFrameMixingMode mode)
    {
        var inbound = Channel.CreateUnbounded<CanFrame>();
        var outbound = Channel.CreateUnbounded<CanFrame>();
        var receiver = new AsyncDoCanTransmitter(
            outbound,
            inbound,
            new DoCanOptions
            {
                RequestId = 0x101,
                ResponseId = 0x100,
                UseFd = false,
                FrameMixingMode = mode,
            });
        var payload = new byte[] { 0x22, 0xF1, 0x90 };
        var frame = DoCanFraming.EncodeSingleFrame(payload, 8);

        await inbound.Writer.WriteAsync(
            CanFrame.CreateCopy(0x100, frame, fd: true),
            TestContext.CancellationToken);

        var received = await receiver.ReceiveAsync(TestContext.CancellationToken);

        CollectionAssert.AreEqual(payload, received.ToArray());
    }

    [TestMethod]
    public async Task ConcurrentSendRejected()
    {
        var sToR = Channel.CreateUnbounded<CanFrame>();
        var rToS = Channel.CreateUnbounded<CanFrame>();
        var t = new AsyncDoCanTransmitter(sToR, rToS, new DoCanOptions
        {
            RequestId = 0x100,
            ResponseId = 0x101,
            TimeoutBs = TimeSpan.FromMilliseconds(200),
        });

        using var firstCts = new CancellationTokenSource();
        var first = t.SendAsync(new byte[200], firstCts.Token);

        // Wait until the first frame (FF) has been emitted so the gate is held.
        await sToR.Reader.ReadAsync(TestContext.CancellationToken);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            t.SendAsync(new byte[] { 0x01, 0x02 }, CancellationToken.None));

        firstCts.Cancel();
        try { await first; } catch { /* expected: cancelled or timed out */ }
    }

    [TestMethod]
    public async Task Overflow_FromReceiver_RaisesException()
    {
        var sToR = Channel.CreateUnbounded<CanFrame>();
        var rToS = Channel.CreateUnbounded<CanFrame>();
        var senderOpts = new DoCanOptions { RequestId = 0x100, ResponseId = 0x101 };
        var s = new AsyncDoCanTransmitter(sToR, rToS, senderOpts);

        // Push a fake Overflow flow control on the receive channel.
        var fc = DoCanFraming.EncodeFlowControlFrame(DoCanFlowStatus.Overflow, 0, 0);
        await rToS.Writer.WriteAsync(new CanFrame { CanId = 0x101, Data = fc }, TestContext.CancellationToken);

        await Assert.ThrowsExactlyAsync<DoCanOverflowException>(() =>
            s.SendAsync(new byte[200], CancellationToken.None));
    }

    public TestContext TestContext { get; set; }
}
