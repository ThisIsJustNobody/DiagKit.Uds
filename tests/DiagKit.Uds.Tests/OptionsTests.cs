using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

using DiagKit.Uds.DoCan;
using DiagKit.Uds.DoIp;
using DiagKit.Uds.UdsLayer;

namespace DiagKit.Uds.Tests;

[TestClass]
public class OptionsTests
{
    [TestMethod]
    public async Task DoCanTransmitter_ClonesOptionsAtConstruction()
    {
        var outbound = Channel.CreateUnbounded<CanFrame>();
        var inbound = Channel.CreateUnbounded<CanFrame>();
        var options = new DoCanOptions { RequestId = 0x100, ResponseId = 0x101 };
        var transmitter = new AsyncDoCanTransmitter(outbound, inbound, options);

        options.RequestId = 0x200;
        transmitter.Options.RequestId = 0x300;

        await transmitter.SendAsync(new byte[] { 0x22 }, TestContext.CancellationToken);
        var frame = await outbound.Reader.ReadAsync(TestContext.CancellationToken);

        Assert.AreEqual(0x100u, frame.CanId);
    }

    [TestMethod]
    public void SyncDoCanTransmitter_ClonesOptionsAtConstruction()
    {
        var outbound = new BlockingCollection<CanFrame>();
        var inbound = new BlockingCollection<CanFrame>();
        var options = new DoCanOptions { RequestId = 0x120, ResponseId = 0x121 };
        var transmitter = new DoCanTransmitter(outbound, inbound, options);

        options.RequestId = 0x220;
        transmitter.Options.RequestId = 0x320;

        transmitter.Send(new byte[] { 0x22 }, TestContext.CancellationToken);
        var frame = outbound.Take(TestContext.CancellationToken);

        Assert.AreEqual(0x120u, frame.CanId);
    }

    [TestMethod]
    public async Task DoIpTransmitter_ClonesOptionsAtConstruction()
    {
        var outbound = Channel.CreateUnbounded<DoIpMessage>();
        var inbound = Channel.CreateUnbounded<DoIpMessage>();
        var options = new DoIpOptions
        {
            SourceAddress = 0x0E00,
            TargetAddress = 0x1234,
            EntityAddress = 0x0A00,
            OemSpecific = [1, 2, 3, 4],
            AutoRespondAliveCheck = false,
            AutoActivate = false,
            WaitForDiagnosticAck = false,
        };
        var transmitter = new AsyncDoIpTransmitter(outbound, inbound, options);

        options.TargetAddress = 0x5678;
        options.OemSpecific[0] = 0xFF;
        transmitter.Options.TargetAddress = 0x9999;

        await transmitter.SendAsync(new byte[] { 0x22, 0xF1, 0x90 }, TestContext.CancellationToken);
        var message = await outbound.Reader.ReadAsync(TestContext.CancellationToken);

        Assert.AreEqual(DoIpPayloadType.DiagnosticMessage, message.PayloadType);
        Assert.AreEqual(0x0E00, BinaryPrimitives.ReadUInt16BigEndian(message.Payload.Span[..2]));
        Assert.AreEqual(0x1234, BinaryPrimitives.ReadUInt16BigEndian(message.Payload.Span.Slice(2, 2)));
    }

    [TestMethod]
    public void AsyncUdsClient_ClonesOptionsAtConstruction()
    {
        var options = new UdsOptions { P2Client = TimeSpan.FromMilliseconds(75) };
        var client = new AsyncUdsClient(
            (_, _) => Task.CompletedTask,
            _ => Task.FromResult<ReadOnlyMemory<byte>>(ReadOnlyMemory<byte>.Empty),
            null,
            options);

        options.P2Client = TimeSpan.FromSeconds(3);
        client.Options.P2Client = TimeSpan.FromSeconds(4);

        Assert.AreEqual(TimeSpan.FromMilliseconds(75), client.Options.P2Client);
    }

    [TestMethod]
    public void SyncUdsClient_ClonesOptionsAtConstruction()
    {
        var options = new UdsOptions { P2Client = TimeSpan.FromMilliseconds(80) };
        var client = new UdsClient(
            (_, _) => { },
            _ => ReadOnlyMemory<byte>.Empty,
            null,
            options);

        options.P2Client = TimeSpan.FromSeconds(3);
        client.Options.P2Client = TimeSpan.FromSeconds(4);

        Assert.AreEqual(TimeSpan.FromMilliseconds(80), client.Options.P2Client);
    }

    [TestMethod]
    public void UdsOptionsClone_CopiesRequestLifecycleOptions()
    {
        Action<bool> lifecycle = _ => { };
        var options = new UdsOptions
        {
            ClearReceiveBufferBeforeRequest = false,
            InitializeOrClearUpAction = lifecycle,
        };

        var clone = options.Clone();

        Assert.IsFalse(clone.ClearReceiveBufferBeforeRequest);
        Assert.AreSame(lifecycle, clone.InitializeOrClearUpAction);
    }

    [TestMethod]
    public void InvalidDoCanOptions_ThrowDuringConstruction()
    {
        var outbound = Channel.CreateUnbounded<CanFrame>();
        var inbound = Channel.CreateUnbounded<CanFrame>();
        var options = new DoCanOptions { RequestId = 0x800, ResponseId = 0x101 };

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new AsyncDoCanTransmitter(outbound, inbound, options));
    }

    [TestMethod]
    public void InvalidDoCanOptions_NegativeTiming_ThrowsDuringConstruction()
    {
        var outbound = Channel.CreateUnbounded<CanFrame>();
        var inbound = Channel.CreateUnbounded<CanFrame>();
        var options = new DoCanOptions { RequestId = 0x100, ResponseId = 0x101, TimeCs = TimeSpan.FromMilliseconds(-1) };

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new AsyncDoCanTransmitter(outbound, inbound, options));
    }

    [TestMethod]
    public void DoCanOptions_CloneCopiesReceiveStartTimeout()
    {
        var options = new DoCanOptions
        {
            RequestId = 0x100,
            ResponseId = 0x101,
            ReceiveStartTimeout = TimeSpan.FromMilliseconds(250),
        };

        var clone = options.Clone();

        Assert.AreEqual(TimeSpan.FromMilliseconds(250), clone.ReceiveStartTimeout);
    }

    [TestMethod]
    public void InvalidDoCanOptions_ReceiveStartTimeout_ThrowsDuringConstruction()
    {
        var outbound = Channel.CreateUnbounded<CanFrame>();
        var inbound = Channel.CreateUnbounded<CanFrame>();
        var options = new DoCanOptions { RequestId = 0x100, ResponseId = 0x101, ReceiveStartTimeout = TimeSpan.Zero };

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new AsyncDoCanTransmitter(outbound, inbound, options));
    }

    [TestMethod]
    public void InvalidDoCanOptions_InvalidEnum_ThrowsDuringConstruction()
    {
        var outbound = Channel.CreateUnbounded<CanFrame>();
        var inbound = Channel.CreateUnbounded<CanFrame>();
        var options = new DoCanOptions { RequestId = 0x100, ResponseId = 0x101, FrameMixingMode = (DoCanFrameMixingMode)99 };

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new AsyncDoCanTransmitter(outbound, inbound, options));
    }

    [TestMethod]
    public void InvalidDoCanOptions_NegativeWaitFrameLimit_ThrowsDuringConstruction()
    {
        var outbound = Channel.CreateUnbounded<CanFrame>();
        var inbound = Channel.CreateUnbounded<CanFrame>();
        var options = new DoCanOptions { RequestId = 0x100, ResponseId = 0x101, MaxFlowControlWaitFrames = -1 };

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new AsyncDoCanTransmitter(outbound, inbound, options));
    }

    [TestMethod]
    public void InvalidDoCanOptions_MaxSegmentedPayloadLength_ThrowsDuringConstruction()
    {
        var outbound = Channel.CreateUnbounded<CanFrame>();
        var inbound = Channel.CreateUnbounded<CanFrame>();
        var options = new DoCanOptions { RequestId = 0x100, ResponseId = 0x101, MaxSegmentedPayloadLength = 0 };

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new AsyncDoCanTransmitter(outbound, inbound, options));
    }

    [TestMethod]
    public void InvalidDoIpOptions_ThrowDuringConstruction()
    {
        var outbound = Channel.CreateUnbounded<DoIpMessage>();
        var inbound = Channel.CreateUnbounded<DoIpMessage>();
        var options = new DoIpOptions
        {
            TargetAddress = 0x1234,
            OemSpecific = [1, 2, 3],
        };

        Assert.ThrowsExactly<ArgumentException>(() => new AsyncDoIpTransmitter(outbound, inbound, options));
    }

    [TestMethod]
    public void InvalidDoIpOptions_MaxPayloadLength_ThrowsDuringConstruction()
    {
        var outbound = Channel.CreateUnbounded<DoIpMessage>();
        var inbound = Channel.CreateUnbounded<DoIpMessage>();
        var options = new DoIpOptions { TargetAddress = 0x1234, MaxPayloadLength = 0 };

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new AsyncDoIpTransmitter(outbound, inbound, options));
    }

    [TestMethod]
    public void InvalidDoIpOptions_InboxLimit_ThrowsDuringConstruction()
    {
        var outbound = Channel.CreateUnbounded<DoIpMessage>();
        var inbound = Channel.CreateUnbounded<DoIpMessage>();
        var options = new DoIpOptions { TargetAddress = 0x1234, InboxLimit = 0 };

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new AsyncDoIpTransmitter(outbound, inbound, options));
    }

    [TestMethod]
    public void InvalidDoIpOptions_SourceTargetEquality_ThrowsDuringConstruction()
    {
        var outbound = Channel.CreateUnbounded<DoIpMessage>();
        var inbound = Channel.CreateUnbounded<DoIpMessage>();
        var options = new DoIpOptions { SourceAddress = 0x0E00, TargetAddress = 0x0E00 };

        Assert.ThrowsExactly<ArgumentException>(() => new AsyncDoIpTransmitter(outbound, inbound, options));
    }

    [TestMethod]
    public void InvalidDoIpOptions_DefaultTargetAddress_ThrowsDuringConstruction()
    {
        var outbound = Channel.CreateUnbounded<DoIpMessage>();
        var inbound = Channel.CreateUnbounded<DoIpMessage>();
        var options = new DoIpOptions();

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new AsyncDoIpTransmitter(outbound, inbound, options));
    }

    [TestMethod]
    public void InvalidDoIpOptions_InvalidEnum_ThrowsDuringConstruction()
    {
        var outbound = Channel.CreateUnbounded<DoIpMessage>();
        var inbound = Channel.CreateUnbounded<DoIpMessage>();
        var options = new DoIpOptions { TargetAddress = 0x1234, ActivationType = (DoIpActivationType)0x7F };

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new AsyncDoIpTransmitter(outbound, inbound, options));
    }

    [TestMethod]
    public void InvalidUdsOptions_ThrowDuringConstruction()
    {
        var options = new UdsOptions { P2Client = TimeSpan.Zero };

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new AsyncUdsClient(
            (_, _) => Task.CompletedTask,
            _ => Task.FromResult<ReadOnlyMemory<byte>>(ReadOnlyMemory<byte>.Empty),
            null,
            options));
    }

    [TestMethod]
    public void InvalidUdsOptions_RetryBudget_ThrowsDuringConstruction()
    {
        var options = new UdsOptions
        {
            Rc21Handling = Rc21Handling.Retry,
            Rc21RetryInterval = TimeSpan.FromMilliseconds(100),
            Rc21CompletionTimeout = TimeSpan.FromMilliseconds(100),
        };

        Assert.ThrowsExactly<ArgumentException>(() => new AsyncUdsClient(
            (_, _) => Task.CompletedTask,
            _ => Task.FromResult<ReadOnlyMemory<byte>>(ReadOnlyMemory<byte>.Empty),
            null,
            options));
    }

    [TestMethod]
    public void InvalidUdsOptions_InvalidEnum_ThrowsDuringConstruction()
    {
        var options = new UdsOptions { Rc78Handling = (Rc78Handling)99 };

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new AsyncUdsClient(
            (_, _) => Task.CompletedTask,
            _ => Task.FromResult<ReadOnlyMemory<byte>>(ReadOnlyMemory<byte>.Empty),
            null,
            options));
    }

    [TestMethod]
    public void DoIpOptionsClone_CopiesOemSpecific()
    {
        var options = new DoIpOptions { TargetAddress = 0x1234, OemSpecific = [1, 2, 3, 4] };
        var clone = options.Clone();

        options.OemSpecific[0] = 0xFF;

        Assert.AreEqual(1, clone.OemSpecific![0]);
    }

    [TestMethod]
    public void DoIpOptionsClone_CopiesStreamTransportOptions()
    {
        var options = new DoIpOptions
        {
            TargetAddress = 0x1234,
            EntityAddress = 0x0A00,
            AutoRespondAliveCheck = false,
        };

        var clone = options.Clone();

        Assert.AreEqual((ushort)0x0A00, clone.EntityAddress);
        Assert.IsFalse(clone.AutoRespondAliveCheck);
    }

    public TestContext TestContext { get; set; }
}
