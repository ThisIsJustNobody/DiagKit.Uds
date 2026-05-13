using System;
using System.Buffers.Binary;
using System.Threading.Channels;
using System.Threading.Tasks;

using DiagKit.Uds.DoIp;
using DiagKit.Uds.Exceptions;

namespace DiagKit.Uds.Tests;

[TestClass]
public class DoIpTransmitterTests
{
    [TestMethod]
    public async Task RoutingActivation_Success_IsSentOnlyOnce()
    {
        var (transmitter, outbound, inbound) = BuildTransmitter(autoActivate: true);

        var firstActivation = transmitter.ActivateRoutingAsync(TestContext.CancellationToken);
        var request = await outbound.Reader.ReadAsync(TestContext.CancellationToken);
        Assert.AreEqual(DoIpPayloadType.RoutingActivationRequest, request.PayloadType);

        await inbound.Writer.WriteAsync(RoutingActivationResponse(), TestContext.CancellationToken);
        await firstActivation;

        await transmitter.ActivateRoutingAsync(TestContext.CancellationToken);

        using var noSecondRequest = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(
            () => outbound.Reader.ReadAsync(noSecondRequest.Token).AsTask());
    }

    [TestMethod]
    public async Task ActivateRoutingAsync_ConcurrentSendRejected()
    {
        var (transmitter, outbound, inbound) = BuildTransmitter(autoActivate: true);

        var activation = transmitter.ActivateRoutingAsync(TestContext.CancellationToken);
        Assert.AreEqual(DoIpPayloadType.RoutingActivationRequest, (await outbound.Reader.ReadAsync(TestContext.CancellationToken)).PayloadType);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => transmitter.SendAsync(new byte[] { 0x22, 0xF1, 0x90 }, TestContext.CancellationToken));

        await inbound.Writer.WriteAsync(RoutingActivationResponse(), TestContext.CancellationToken);
        await activation;
    }

    [TestMethod]
    public async Task SendAsync_DiagnosticNack_Throws()
    {
        var (transmitter, outbound, inbound) = BuildTransmitter();

        var sendTask = transmitter.SendAsync(new byte[] { 0x22, 0xF1, 0x90 }, TestContext.CancellationToken);
        Assert.AreEqual(DoIpPayloadType.DiagnosticMessage, (await outbound.Reader.ReadAsync(TestContext.CancellationToken)).PayloadType);

        await inbound.Writer.WriteAsync(DiagnosticNack(DoIpDiagnosticNackCode.TargetUnreachable), TestContext.CancellationToken);

        await Assert.ThrowsExactlyAsync<ProtocolException>(() => sendTask);
    }

    [TestMethod]
    public async Task SendAsync_DiagnosticArrivesBeforeAck_IsCachedForReceive()
    {
        var (transmitter, outbound, inbound) = BuildTransmitter();

        var sendTask = transmitter.SendAsync(new byte[] { 0x22, 0xF1, 0x90 }, TestContext.CancellationToken);
        Assert.AreEqual(DoIpPayloadType.DiagnosticMessage, (await outbound.Reader.ReadAsync(TestContext.CancellationToken)).PayloadType);

        await inbound.Writer.WriteAsync(DiagnosticMessage([0x62, 0xF1, 0x90, 0x55]), TestContext.CancellationToken);
        await inbound.Writer.WriteAsync(DiagnosticAck(), TestContext.CancellationToken);

        await sendTask;
        var response = await transmitter.ReceiveAsync(TestContext.CancellationToken);

        CollectionAssert.AreEqual(new byte[] { 0x62, 0xF1, 0x90, 0x55 }, response.ToArray());
    }

    [TestMethod]
    public async Task ReceiveAsync_GenericHeaderNack_Throws()
    {
        var (transmitter, _, inbound) = BuildTransmitter();

        var receiveTask = transmitter.ReceiveAsync(TestContext.CancellationToken);
        await inbound.Writer.WriteAsync(
            new DoIpMessage(DoIpPayloadType.GenericHeaderNegativeAcknowledge, new byte[] { (byte)DoIpGenericHeaderNackCode.UnknownPayloadType }),
            TestContext.CancellationToken);

        await Assert.ThrowsExactlyAsync<ProtocolException>(() => receiveTask);
    }

    [TestMethod]
    public async Task ReceiveAsync_AliveCheckRequest_AutoRespondsAndContinues()
    {
        var (transmitter, outbound, inbound) = BuildTransmitter();

        var receiveTask = transmitter.ReceiveAsync(TestContext.CancellationToken);
        await inbound.Writer.WriteAsync(new DoIpMessage(DoIpPayloadType.AliveCheckRequest, ReadOnlyMemory<byte>.Empty), TestContext.CancellationToken);

        var aliveResponse = await outbound.Reader.ReadAsync(TestContext.CancellationToken);
        Assert.AreEqual(DoIpPayloadType.AliveCheckResponse, aliveResponse.PayloadType);
        Assert.AreEqual(0x0E00, BinaryPrimitives.ReadUInt16BigEndian(aliveResponse.Payload.Span));

        await inbound.Writer.WriteAsync(DiagnosticMessage([0x62, 0xF1, 0x90]), TestContext.CancellationToken);

        var response = await receiveTask;
        CollectionAssert.AreEqual(new byte[] { 0x62, 0xF1, 0x90 }, response.ToArray());
    }

    [TestMethod]
    public async Task ReceiveAsync_InboxLimitExceeded_Throws()
    {
        var outbound = Channel.CreateUnbounded<DoIpMessage>();
        var inbound = Channel.CreateUnbounded<DoIpMessage>();
        var transmitter = new AsyncDoIpTransmitter(outbound, inbound, TestOptions(inboxLimit: 1));

        var receiveTask = transmitter.ReceiveAsync(TestContext.CancellationToken);
        await inbound.Writer.WriteAsync(new DoIpMessage(DoIpPayloadType.DoIpEntityStatusResponse, new byte[] { 0x00 }), TestContext.CancellationToken);
        await inbound.Writer.WriteAsync(new DoIpMessage(DoIpPayloadType.DiagnosticPowerModeInformationResponse, new byte[] { 0x00 }), TestContext.CancellationToken);

        await Assert.ThrowsExactlyAsync<ProtocolException>(() => receiveTask);
    }

    private static (AsyncDoIpTransmitter Transmitter, Channel<DoIpMessage> Outbound, Channel<DoIpMessage> Inbound) BuildTransmitter(bool autoActivate = false)
    {
        var outbound = Channel.CreateUnbounded<DoIpMessage>();
        var inbound = Channel.CreateUnbounded<DoIpMessage>();
        return (new AsyncDoIpTransmitter(outbound, inbound, TestOptions(autoActivate: autoActivate)), outbound, inbound);
    }

    private static DoIpOptions TestOptions(int inboxLimit = 16, bool autoActivate = false) => new()
    {
        SourceAddress = 0x0E00,
        TargetAddress = 0x1001,
        EntityAddress = 0x0A00,
        AutoActivate = autoActivate,
        WaitForDiagnosticAck = true,
        InboxLimit = inboxLimit,
    };

    private static DoIpMessage DiagnosticMessage(ReadOnlySpan<byte> uds)
    {
        var payload = new byte[4 + uds.Length];
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(0, 2), 0x1001);
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(2, 2), 0x0E00);
        uds.CopyTo(payload.AsSpan(4));
        return new DoIpMessage(DoIpPayloadType.DiagnosticMessage, payload);
    }

    private static DoIpMessage DiagnosticAck()
    {
        var payload = new byte[5];
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(0, 2), 0x1001);
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(2, 2), 0x0E00);
        return new DoIpMessage(DoIpPayloadType.DiagnosticMessagePositiveAck, payload);
    }

    private static DoIpMessage DiagnosticNack(DoIpDiagnosticNackCode code)
    {
        var payload = new byte[5];
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(0, 2), 0x1001);
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(2, 2), 0x0E00);
        payload[4] = (byte)code;
        return new DoIpMessage(DoIpPayloadType.DiagnosticMessageNegativeAck, payload);
    }

    private static DoIpMessage RoutingActivationResponse(
        DoIpActivationResponseCode code = DoIpActivationResponseCode.Success,
        ushort testerAddress = 0x0E00,
        ushort entityAddress = 0x0A00)
    {
        var payload = new byte[9];
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(0, 2), testerAddress);
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(2, 2), entityAddress);
        payload[4] = (byte)code;
        return new DoIpMessage(DoIpPayloadType.RoutingActivationResponse, payload);
    }

    public TestContext TestContext { get; set; }
}
