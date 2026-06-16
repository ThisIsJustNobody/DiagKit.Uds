using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

using DiagKit.Uds.DoCan;
using DiagKit.Uds.Exceptions;
using DiagKit.Uds.UdsLayer;

namespace DiagKit.Uds.Tests;

[TestClass]
public class UdsDoCanTimingTests
{
    [TestMethod]
    public async Task AsyncDoCanResponse_MayFinishAfterP2_WhenFirstFrameArrivesWithinP2()
    {
        var testerToEcu = Channel.CreateUnbounded<CanFrame>();
        var ecuToTester = Channel.CreateUnbounded<CanFrame>();
        var clientCan = new AsyncDoCanTransmitter(
            testerToEcu,
            ecuToTester,
            new DoCanOptions
            {
                RequestId = 0x7E0,
                ResponseId = 0x7E8,
                STmin = 0x14,
                TimeoutAr = TimeSpan.FromMilliseconds(100),
                TimeoutCr = TimeSpan.FromSeconds(1),
            });
        var client = new AsyncUdsClient(
            clientCan,
            new UdsOptions { P2Client = TimeSpan.FromSeconds(1) });
        var response = BuildSecuritySeedResponse(430);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var ecuTask = SimulateSegmentedAsyncEcuAsync(
            testerToEcu,
            ecuToTester,
            response,
            TimeSpan.FromMilliseconds(20),
            cts.Token);

        var received = await client.SendRequestAsync(new byte[] { 0x27, 0x01 }, false, cts.Token);

        CollectionAssert.AreEqual(response, received.ToArray());
        await ecuTask;
    }

    [TestMethod]
    public void SyncDoCanResponse_MayFinishAfterP2_WhenFirstFrameArrivesWithinP2()
    {
        var testerToEcu = new BlockingCollection<CanFrame>();
        var ecuToTester = new BlockingCollection<CanFrame>();
        var clientCan = new DoCanTransmitter(
            testerToEcu,
            ecuToTester,
            new DoCanOptions
            {
                RequestId = 0x7E0,
                ResponseId = 0x7E8,
                STmin = 0x14,
                TimeoutCr = TimeSpan.FromSeconds(1),
            });
        var client = new UdsClient(
            clientCan,
            new UdsOptions { P2Client = TimeSpan.FromSeconds(1) });
        var response = BuildSecuritySeedResponse(430);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var ecuTask = Task.Run(
            () => SimulateSegmentedSyncEcu(testerToEcu, ecuToTester, response, TimeSpan.FromMilliseconds(20), cts.Token),
            cts.Token);

        var received = client.SendRequest(new byte[] { 0x27, 0x01 }, false, cts.Token);

        CollectionAssert.AreEqual(response, received.ToArray());
        ecuTask.GetAwaiter().GetResult();
    }

    [TestMethod]
    public async Task AsyncDoCanResponse_AfterRc78MayFinishAfterP2Star_WhenFirstFrameArrivesWithinP2Star()
    {
        var testerToEcu = Channel.CreateUnbounded<CanFrame>();
        var ecuToTester = Channel.CreateUnbounded<CanFrame>();
        var clientCan = new AsyncDoCanTransmitter(
            testerToEcu,
            ecuToTester,
            new DoCanOptions
            {
                RequestId = 0x7E0,
                ResponseId = 0x7E8,
                STmin = 0x14,
                TimeoutCr = TimeSpan.FromSeconds(1),
            });
        var client = new AsyncUdsClient(
            clientCan,
            new UdsOptions
            {
                P2Client = TimeSpan.FromMilliseconds(200),
                P2ClientExtended = TimeSpan.FromSeconds(1),
                Rc78Handling = Rc78Handling.WaitForCompletion,
            });
        var response = BuildSecuritySeedResponse(430);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var ecuTask = Task.Run(async () =>
        {
            _ = await testerToEcu.Reader.ReadAsync(cts.Token);
            await WriteSingleFrameAsync(ecuToTester.Writer, new byte[] { 0x7F, 0x27, 0x78 }, cts.Token);
            await Task.Delay(TimeSpan.FromMilliseconds(250), cts.Token);
            await SimulateSegmentedResponseAsync(testerToEcu, ecuToTester, response, TimeSpan.FromMilliseconds(20), cts.Token);
        }, cts.Token);

        var received = await client.SendRequestAsync(new byte[] { 0x27, 0x01 }, false, cts.Token);

        CollectionAssert.AreEqual(response, received.ToArray());
        await ecuTask;
    }

    [TestMethod]
    public async Task AsyncDoCanResponse_TimesOutWithNCr_WhenConsecutiveFrameIsLate()
    {
        var testerToEcu = Channel.CreateUnbounded<CanFrame>();
        var ecuToTester = Channel.CreateUnbounded<CanFrame>();
        var clientCan = new AsyncDoCanTransmitter(
            testerToEcu,
            ecuToTester,
            new DoCanOptions
            {
                RequestId = 0x7E0,
                ResponseId = 0x7E8,
                STmin = 0x14,
                TimeoutCr = TimeSpan.FromMilliseconds(100),
            });
        var client = new AsyncUdsClient(
            clientCan,
            new UdsOptions { P2Client = TimeSpan.FromSeconds(2) });
        var response = BuildSecuritySeedResponse(64);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var ecuTask = SimulateSegmentedAsyncEcuAsync(
            testerToEcu,
            ecuToTester,
            response,
            TimeSpan.FromMilliseconds(250),
            cts.Token);

        var ex = await Assert.ThrowsExactlyAsync<ProtocolException>(() =>
            client.SendRequestAsync(new byte[] { 0x27, 0x01 }, false, cts.Token));

        StringAssert.Contains(ex.Message, "N_Cr");
        cts.Cancel();
        try
        {
            await ecuTask;
        }
        catch (OperationCanceledException)
        {
        }
    }

    [TestMethod]
    public async Task AsyncDoCanResponse_RespectsCallerCancellationAfterFirstFrame()
    {
        var testerToEcu = Channel.CreateUnbounded<CanFrame>();
        var ecuToTester = Channel.CreateUnbounded<CanFrame>();
        var clientCan = new AsyncDoCanTransmitter(
            testerToEcu,
            ecuToTester,
            new DoCanOptions
            {
                RequestId = 0x7E0,
                ResponseId = 0x7E8,
                STmin = 0x14,
                TimeoutCr = TimeSpan.FromSeconds(1),
            });
        var client = new AsyncUdsClient(
            clientCan,
            new UdsOptions { P2Client = TimeSpan.FromSeconds(1) });
        var response = BuildSecuritySeedResponse(64);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var requestCts = new CancellationTokenSource();
        var requestTask = client.SendRequestAsync(new byte[] { 0x27, 0x01 }, false, requestCts.Token);

        _ = await testerToEcu.Reader.ReadAsync(cts.Token);
        var firstFrame = DoCanFraming.EncodeFirstFrame(response, 8, out _);
        await ecuToTester.Writer.WriteAsync(new CanFrame { CanId = 0x7E8, Data = firstFrame }, cts.Token);
        _ = await testerToEcu.Reader.ReadAsync(cts.Token);
        requestCts.Cancel();

        try
        {
            await requestTask;
            Assert.Fail("Expected the in-flight UDS request to observe caller cancellation.");
        }
        catch (OperationCanceledException)
        {
        }
    }

    [TestMethod]
    public async Task AsyncDoCanReceive_SendsFlowControlUsingTimeoutAr()
    {
        var inbound = Channel.CreateUnbounded<CanFrame>();
        var payload = BuildSecuritySeedResponse(13);
        var firstFrame = DoCanFraming.EncodeFirstFrame(payload, 8, out var consumed);
        var consecutiveFrame = DoCanFraming.EncodeConsecutiveFrame(payload.AsSpan(consumed), 1, 8);
        var flowControlStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var receiver = new AsyncDoCanTransmitter(
            async (_, ct) =>
            {
                flowControlStarted.TrySetResult();
                await Task.Delay(TimeSpan.FromMilliseconds(100), ct).ConfigureAwait(false);
            },
            ct => inbound.Reader.ReadAsync(ct).AsTask(),
            options: new DoCanOptions
            {
                RequestId = 0x7E8,
                ResponseId = 0x7E0,
                TimeoutAs = TimeSpan.FromMilliseconds(10),
                TimeoutAr = TimeSpan.FromMilliseconds(500),
                TimeoutCr = TimeSpan.FromMilliseconds(500),
            });

        await inbound.Writer.WriteAsync(new CanFrame { CanId = 0x7E0, Data = firstFrame }, TestContext.CancellationToken);
        var receiveTask = receiver.ReceiveAsync(TestContext.CancellationToken);
        await flowControlStarted.Task.WaitAsync(TestContext.CancellationToken);
        await inbound.Writer.WriteAsync(new CanFrame { CanId = 0x7E0, Data = consecutiveFrame }, TestContext.CancellationToken);

        var received = await receiveTask;

        CollectionAssert.AreEqual(payload, received.ToArray());
    }

    private static byte[] BuildSecuritySeedResponse(int length)
    {
        var response = new byte[length];
        response[0] = 0x67;
        response[1] = 0x01;
        for (int i = 2; i < response.Length; i++)
            response[i] = (byte)i;
        return response;
    }

    private static async Task SimulateSegmentedAsyncEcuAsync(
        Channel<CanFrame> testerToEcu,
        Channel<CanFrame> ecuToTester,
        byte[] response,
        TimeSpan consecutiveFrameDelay,
        CancellationToken cancellationToken)
    {
        _ = await testerToEcu.Reader.ReadAsync(cancellationToken);
        await SimulateSegmentedResponseAsync(testerToEcu, ecuToTester, response, consecutiveFrameDelay, cancellationToken);
    }

    private static async Task SimulateSegmentedResponseAsync(
        Channel<CanFrame> testerToEcu,
        Channel<CanFrame> ecuToTester,
        byte[] response,
        TimeSpan consecutiveFrameDelay,
        CancellationToken cancellationToken)
    {
        var firstFrame = DoCanFraming.EncodeFirstFrame(response, 8, out var consumed);
        await ecuToTester.Writer.WriteAsync(new CanFrame { CanId = 0x7E8, Data = firstFrame }, cancellationToken);

        var flowControl = await testerToEcu.Reader.ReadAsync(cancellationToken);
        AssertFlowControl(flowControl);

        await WriteConsecutiveFramesAsync(ecuToTester.Writer, response, consumed, consecutiveFrameDelay, cancellationToken);
    }

    private static void SimulateSegmentedSyncEcu(
        BlockingCollection<CanFrame> testerToEcu,
        BlockingCollection<CanFrame> ecuToTester,
        byte[] response,
        TimeSpan consecutiveFrameDelay,
        CancellationToken cancellationToken)
    {
        _ = testerToEcu.Take(cancellationToken);
        var firstFrame = DoCanFraming.EncodeFirstFrame(response, 8, out var consumed);
        ecuToTester.Add(new CanFrame { CanId = 0x7E8, Data = firstFrame }, cancellationToken);

        var flowControl = testerToEcu.Take(cancellationToken);
        AssertFlowControl(flowControl);

        byte sequenceNumber = 0;
        int pos = consumed;
        while (pos < response.Length)
        {
            cancellationToken.WaitHandle.WaitOne(consecutiveFrameDelay);
            cancellationToken.ThrowIfCancellationRequested();
            sequenceNumber = DoCanFraming.NextSequenceNumber(sequenceNumber);
            var frame = new byte[8];
            var take = Math.Min(7, response.Length - pos);
            DoCanFraming.EncodeConsecutiveFrame(response.AsSpan(pos, take), frame, sequenceNumber, 8, 0xCC);
            ecuToTester.Add(new CanFrame { CanId = 0x7E8, Data = frame }, cancellationToken);
            pos += take;
        }
    }

    private static async Task WriteSingleFrameAsync(
        ChannelWriter<CanFrame> writer,
        byte[] payload,
        CancellationToken cancellationToken)
    {
        var frame = DoCanFraming.EncodeSingleFrame(payload, 8);
        await writer.WriteAsync(new CanFrame { CanId = 0x7E8, Data = frame }, cancellationToken);
    }

    private static async Task WriteConsecutiveFramesAsync(
        ChannelWriter<CanFrame> writer,
        byte[] response,
        int consumed,
        TimeSpan delay,
        CancellationToken cancellationToken)
    {
        byte sequenceNumber = 0;
        int pos = consumed;
        while (pos < response.Length)
        {
            await Task.Delay(delay, cancellationToken);
            sequenceNumber = DoCanFraming.NextSequenceNumber(sequenceNumber);
            var frame = new byte[8];
            var take = Math.Min(7, response.Length - pos);
            DoCanFraming.EncodeConsecutiveFrame(response.AsSpan(pos, take), frame, sequenceNumber, 8, 0xCC);
            await writer.WriteAsync(new CanFrame { CanId = 0x7E8, Data = frame }, cancellationToken);
            pos += take;
        }
    }

    private static void AssertFlowControl(CanFrame flowControl)
    {
        Assert.IsTrue(DoCanFraming.TryReadFlowControlFrame(flowControl.Data.Span, out var status, out var blockSize, out var stmin));
        Assert.AreEqual(DoCanFlowStatus.Continue, status);
        Assert.AreEqual((byte)0x00, blockSize);
        Assert.AreEqual((byte)0x14, stmin);
    }

    public TestContext TestContext { get; set; }
}
