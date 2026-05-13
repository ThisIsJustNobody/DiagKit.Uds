using System;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

using DiagKit.Uds.DoCan;
using DiagKit.Uds.Exceptions;
using DiagKit.Uds.Services;
using DiagKit.Uds.UdsLayer;

namespace DiagKit.Uds.Tests;

[TestClass]
public class UdsEcuSimulatorTests
{
    [TestMethod]
    public async Task DiagnosticSessionControl_DefaultService_SwitchesSession()
    {
        var (client, simulator) = BuildPayloadPair();
        await using var _ = simulator;
        simulator.ConfigureSession(
            DiagnosticSessionType.ExtendedDiagnostic,
            TimeSpan.FromMilliseconds(75),
            TimeSpan.FromMilliseconds(2500));

        var serverTask = Task.Run(
            () => simulator.ReceiveAndRespondAsync(TestContext.CancellationToken),
            TestContext.CancellationToken);

        var response = await DiagnosticSessionControl.InvokeAsync(
            client,
            DiagnosticSessionType.ExtendedDiagnostic,
            TestContext.CancellationToken);

        await serverTask;
        Assert.AreEqual(DiagnosticSessionType.ExtendedDiagnostic, simulator.ActiveSession);
        Assert.AreEqual(TimeSpan.FromMilliseconds(75), response.P2Server);
        Assert.AreEqual(TimeSpan.FromMilliseconds(2500), response.P2ServerExtended);
    }

    [TestMethod]
    public async Task ReadDataByIdentifier_DefaultService_ReturnsMultipleDids()
    {
        var (client, simulator) = BuildPayloadPair();
        await using var _ = simulator;
        simulator.SetDataIdentifier(0xF190, new byte[] { 0x01, 0x02 });
        simulator.SetDataIdentifier(0xF191, new byte[] { 0x03 });

        await simulator.StartAsync(TestContext.CancellationToken);

        var response = await client.SendRequestAsync(
            ReadDataByIdentifier.BuildRequest(0xF190, 0xF191),
            false,
            TestContext.CancellationToken);

        CollectionAssert.AreEqual(
            new byte[] { 0x62, 0xF1, 0x90, 0x01, 0x02, 0xF1, 0x91, 0x03 },
            response.ToArray());
        await simulator.StopAsync(TestContext.CancellationToken);
    }

    [TestMethod]
    public async Task ReadDataByIdentifier_UnknownDid_ReturnsNrc31()
    {
        var (client, simulator) = BuildPayloadPair();
        await using var _ = simulator;
        await simulator.StartAsync(TestContext.CancellationToken);

        var ex = await Assert.ThrowsExactlyAsync<NegativeResponseException>(() =>
            ReadDataByIdentifier.InvokeAsync(client, 0xF190, TestContext.CancellationToken));

        Assert.AreEqual(NegativeResponseCode.RequestOutOfRange, ex.Code);
        await simulator.StopAsync(TestContext.CancellationToken);
    }

    [TestMethod]
    public async Task SecurityAccess_DefaultService_UnlocksAndTracksState()
    {
        var (client, simulator) = BuildPayloadPair();
        await using var _ = simulator;
        simulator.RegisterSecurityAccess(
            0x01,
            new byte[] { 0x01, 0x02, 0x03 },
            seed => [.. seed.Select(b => (byte)~b)]);

        await simulator.StartAsync(TestContext.CancellationToken);

        var unlocked = await SecurityAccess.UnlockAsync(
            client,
            0x01,
            seed => [.. seed.Select(b => (byte)~b)],
            TestContext.CancellationToken);
        var secondSeedResponse = await client.SendRequestAsync(
            SecurityAccess.BuildRequestSeed(0x01),
            false,
            TestContext.CancellationToken);

        Assert.IsTrue(unlocked);
        Assert.IsTrue(simulator.IsSecurityUnlocked(0x01));
        CollectionAssert.AreEqual(new byte[] { 0x67, 0x01, 0x00, 0x00, 0x00 }, secondSeedResponse.ToArray());
        Assert.IsTrue(simulator.LockSecurityAccess(0x01));
        Assert.IsFalse(simulator.IsSecurityUnlocked(0x01));
        await simulator.StopAsync(TestContext.CancellationToken);
    }

    [TestMethod]
    public async Task SecurityAccess_SendKeyBeforeSeed_ReturnsSequenceError()
    {
        var (client, simulator) = BuildPayloadPair();
        await using var _ = simulator;
        simulator.RegisterSecurityAccess(0x01, new byte[] { 0x12 }, seed => seed);

        await simulator.StartAsync(TestContext.CancellationToken);

        var ex = await Assert.ThrowsExactlyAsync<NegativeResponseException>(() =>
            client.SendRequestAsync(new byte[] { 0x27, 0x02, 0x12 }, false, TestContext.CancellationToken));

        Assert.AreEqual(NegativeResponseCode.RequestSequenceError, ex.Code);
        await simulator.StopAsync(TestContext.CancellationToken);
    }

    [TestMethod]
    public async Task RoutineControl_DefaultService_DispatchesRoutine()
    {
        var (client, simulator) = BuildPayloadPair();
        await using var _ = simulator;
        simulator.RegisterRoutine(0x1234, (type, routineId, data, _) =>
        {
            Assert.AreEqual(RoutineControlType.StartRoutine, type);
            Assert.AreEqual((ushort)0x1234, routineId);
            CollectionAssert.AreEqual(new byte[] { 0x55 }, data.ToArray());
            return Task.FromResult<ReadOnlyMemory<byte>>(new byte[] { 0xAA, 0xBB });
        });

        await simulator.StartAsync(TestContext.CancellationToken);

        var response = await RoutineControl.InvokeAsync(
            client,
            RoutineControlType.StartRoutine,
            0x1234,
            new byte[] { 0x55 },
            TestContext.CancellationToken);

        CollectionAssert.AreEqual(new byte[] { 0xAA, 0xBB }, response.StatusRecord.ToArray());
        await simulator.StopAsync(TestContext.CancellationToken);
    }

    [TestMethod]
    public async Task ReadDtcInformation_DefaultService_FiltersAndReportsCount()
    {
        var (client, simulator) = BuildPayloadPair();
        await using var _ = simulator;
        simulator.SetDtc(0x010203, DtcStatus.ConfirmedDtc);
        simulator.SetDtc(0xAABBCC, DtcStatus.PendingDtc);

        await simulator.StartAsync(TestContext.CancellationToken);

        var records = await ReadDtcInformation.ReportDtcByStatusMaskAsync(
            client,
            DtcStatus.ConfirmedDtc,
            TestContext.CancellationToken);
        var countResponse = await client.SendRequestAsync(
            ReadDtcInformation.BuildReportNumber(DtcStatus.ConfirmedDtc | DtcStatus.PendingDtc),
            false,
            TestContext.CancellationToken);

        Assert.HasCount(1, records);
        Assert.AreEqual(0x010203u, records[0].DtcCode);
        Assert.AreEqual(DtcStatus.ConfirmedDtc, records[0].Status);
        CollectionAssert.AreEqual(new byte[] { 0x59, 0x01, 0xFF, 0x01, 0x00, 0x02 }, countResponse.ToArray());
        await simulator.StopAsync(TestContext.CancellationToken);
    }

    [TestMethod]
    public async Task Register_OverridesBuiltInService()
    {
        var (client, simulator) = BuildPayloadPair();
        await using var _ = simulator;
        simulator.Register(
            UdsServiceId.TesterPresent,
            (_, _) => Task.FromResult<ReadOnlyMemory<byte>>(new byte[] { 0x7E, 0xAA }));

        await simulator.StartAsync(TestContext.CancellationToken);

        var response = await client.SendRequestAsync(new byte[] { 0x3E, 0x00 }, false, TestContext.CancellationToken);

        CollectionAssert.AreEqual(new byte[] { 0x7E, 0xAA }, response.ToArray());
        await simulator.StopAsync(TestContext.CancellationToken);
    }

    [TestMethod]
    public async Task RegisterScript_SendsRc78ThenFinalResponse()
    {
        var (client, simulator) = BuildPayloadPair(new UdsOptions
        {
            P2Client = TimeSpan.FromMilliseconds(200),
            P2ClientExtended = TimeSpan.FromSeconds(2),
            Rc78Handling = Rc78Handling.WaitForCompletion,
        });
        await using var _ = simulator;
        simulator.RegisterScript(
            UdsServiceId.ReadDataByIdentifier,
            UdsServerResponse.Negative(0x22, NegativeResponseCode.RequestCorrectlyReceivedResponsePending),
            UdsServerResponse.Positive(0x22, [0xF1, 0x90, 0xAB], TimeSpan.FromMilliseconds(10)));

        await simulator.StartAsync(TestContext.CancellationToken);

        var data = await ReadDataByIdentifier.InvokeAsync(client, 0xF190, TestContext.CancellationToken);

        CollectionAssert.AreEqual(new byte[] { 0xAB }, data.ToArray());
        await simulator.StopAsync(TestContext.CancellationToken);
    }

    [TestMethod]
    public async Task DoCanFullStack_ReadDataByIdentifier_WorksWithSimulator()
    {
        var clientToServer = Channel.CreateUnbounded<CanFrame>();
        var serverToClient = Channel.CreateUnbounded<CanFrame>();

        var clientCan = new AsyncDoCanTransmitter(clientToServer, serverToClient,
            new DoCanOptions { RequestId = 0x7E0, ResponseId = 0x7E8 });
        var serverCan = new AsyncDoCanTransmitter(serverToClient, clientToServer,
            new DoCanOptions { RequestId = 0x7E8, ResponseId = 0x7E0 });

        var client = new AsyncUdsClient(clientCan);
        await using var simulator = new UdsEcuSimulator(serverCan);
        simulator.SetDataIdentifier(0xF190, new byte[] { 0xDE, 0xAD, 0xBE, 0xEF });

        await simulator.StartAsync(TestContext.CancellationToken);

        var data = await ReadDataByIdentifier.InvokeAsync(client, 0xF190, TestContext.CancellationToken);

        CollectionAssert.AreEqual(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }, data.ToArray());
        await simulator.StopAsync(TestContext.CancellationToken);
    }

    [TestMethod]
    public async Task ReceiveAndRespondAsync_BackgroundRunning_Throws()
    {
        var (_, simulator) = BuildPayloadPair();
        await using var _ = simulator;

        await simulator.StartAsync(TestContext.CancellationToken);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            simulator.ReceiveAndRespondAsync(TestContext.CancellationToken));
        await simulator.StopAsync(TestContext.CancellationToken);
    }

    [TestMethod]
    public async Task ReceiveAndRespondAsync_ConcurrentCall_Throws()
    {
        var receiveEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var request = new TaskCompletionSource<ReadOnlyMemory<byte>>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var simulator = new UdsEcuSimulator(
            (_, _) => Task.CompletedTask,
            ct =>
            {
                receiveEntered.TrySetResult();
                return request.Task.WaitAsync(ct);
            });

        var first = simulator.ReceiveAndRespondAsync(TestContext.CancellationToken);
        await receiveEntered.Task.WaitAsync(TestContext.CancellationToken);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            simulator.ReceiveAndRespondAsync(TestContext.CancellationToken));

        request.SetResult(new byte[] { 0x3E, 0x00 });
        await first.WaitAsync(TestContext.CancellationToken);
    }

    [TestMethod]
    public async Task StartAsync_StopAsync_CanRestart()
    {
        var (client, simulator) = BuildPayloadPair();
        await using var _ = simulator;
        simulator.SetDataIdentifier(0xF190, new byte[] { 0xAA });

        await simulator.StartAsync(TestContext.CancellationToken);
        var first = await ReadDataByIdentifier.InvokeAsync(client, 0xF190, TestContext.CancellationToken);
        await simulator.StopAsync(TestContext.CancellationToken);

        await simulator.StartAsync(TestContext.CancellationToken);
        var second = await ReadDataByIdentifier.InvokeAsync(client, 0xF190, TestContext.CancellationToken);
        await simulator.StopAsync(TestContext.CancellationToken);

        CollectionAssert.AreEqual(new byte[] { 0xAA }, first.ToArray());
        CollectionAssert.AreEqual(new byte[] { 0xAA }, second.ToArray());
    }

    [TestMethod]
    public async Task StopAsync_BackgroundTransportFault_RethrowsAndFaultsCompletion()
    {
        var receiveEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var simulator = new UdsEcuSimulator(
            (_, _) => Task.CompletedTask,
            _ =>
            {
                receiveEntered.TrySetResult();
                throw new InvalidOperationException("transport failed");
            });

        await simulator.StartAsync(TestContext.CancellationToken);
        await receiveEntered.Task.WaitAsync(TestContext.CancellationToken);

        var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            simulator.StopAsync(TestContext.CancellationToken));

        Assert.AreEqual("transport failed", ex.Message);
        Assert.IsTrue(simulator.Completion.IsFaulted);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => simulator.DisposeAsync().AsTask());
    }

    [TestMethod]
    public async Task ReceiveAndRespondAsync_HandlerException_Propagates()
    {
        var inbound = Channel.CreateUnbounded<ReadOnlyMemory<byte>>();
        var outbound = Channel.CreateUnbounded<ReadOnlyMemory<byte>>();
        await using var simulator = new UdsEcuSimulator(
            (data, ct) => outbound.Writer.WriteAsync(data, ct).AsTask(),
            ct => inbound.Reader.ReadAsync(ct).AsTask());
        simulator.Register(UdsServiceId.DiagnosticSessionControl, (_, _) => throw new InvalidOperationException("handler failed"));

        await inbound.Writer.WriteAsync(new byte[] { 0x10, 0x03 }, TestContext.CancellationToken);

        var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            simulator.ReceiveAndRespondAsync(TestContext.CancellationToken));

        Assert.AreEqual("handler failed", ex.Message);
        Assert.IsFalse(outbound.Reader.TryRead(out _));
    }

    [TestMethod]
    public async Task DisposeAsync_BackgroundHandlerFault_RethrowsAndFaultsCompletion()
    {
        var receiveEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var simulator = new UdsEcuSimulator(
            (_, _) => Task.CompletedTask,
            _ =>
            {
                receiveEntered.TrySetResult();
                return Task.FromResult<ReadOnlyMemory<byte>>(new byte[] { 0x10, 0x03 });
            });
        simulator.Register(UdsServiceId.DiagnosticSessionControl, (_, _) => throw new InvalidOperationException("handler failed"));

        await simulator.StartAsync(TestContext.CancellationToken);
        await receiveEntered.Task.WaitAsync(TestContext.CancellationToken);

        var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => simulator.DisposeAsync().AsTask());

        Assert.AreEqual("handler failed", ex.Message);
        Assert.IsTrue(simulator.Completion.IsFaulted);
    }

    [TestMethod]
    public async Task ReadDtcInformation_UnsupportedSubFunction_ReturnsNrc12()
    {
        var (client, simulator) = BuildPayloadPair();
        await using var _ = simulator;
        await simulator.StartAsync(TestContext.CancellationToken);

        var ex = await Assert.ThrowsExactlyAsync<NegativeResponseException>(() =>
            client.SendRequestAsync(new byte[] { 0x19, 0x0A }, false, TestContext.CancellationToken));

        Assert.AreEqual(NegativeResponseCode.SubFunctionNotSupported, ex.Code);
        await simulator.StopAsync(TestContext.CancellationToken);
    }

    [TestMethod]
    public async Task CrudOperations_RemoveRegisteredState()
    {
        var (client, simulator) = BuildPayloadPair();
        await using var _ = simulator;
        simulator.ConfigureSession(DiagnosticSessionType.Programming);
        simulator.SetDataIdentifier(0xF190, new byte[] { 0xAA });
        simulator.RegisterRoutine(0x1234, new byte[] { 0x55 });
        simulator.SetDtc(0x010203, DtcStatus.ConfirmedDtc);
        simulator.Register(UdsServiceId.TesterPresent, (_, _) => Task.FromResult<ReadOnlyMemory<byte>>(new byte[] { 0x7E, 0x00 }));

        Assert.IsTrue(simulator.RemoveSession(DiagnosticSessionType.Programming));
        Assert.IsTrue(simulator.RemoveDataIdentifier(0xF190));
        Assert.IsTrue(simulator.UnregisterRoutine(0x1234));
        Assert.IsTrue(simulator.RemoveDtc(0x010203));
        Assert.IsTrue(simulator.Unregister(UdsServiceId.TesterPresent));

        await simulator.StartAsync(TestContext.CancellationToken);

        var sessionEx = await Assert.ThrowsExactlyAsync<NegativeResponseException>(() =>
            DiagnosticSessionControl.InvokeAsync(client, DiagnosticSessionType.Programming, TestContext.CancellationToken));
        var didEx = await Assert.ThrowsExactlyAsync<NegativeResponseException>(() =>
            ReadDataByIdentifier.InvokeAsync(client, 0xF190, TestContext.CancellationToken));
        var routineEx = await Assert.ThrowsExactlyAsync<NegativeResponseException>(() =>
            RoutineControl.InvokeAsync(client, RoutineControlType.StartRoutine, 0x1234, cancellationToken: TestContext.CancellationToken));
        var records = await ReadDtcInformation.ReportDtcByStatusMaskAsync(
            client,
            DtcStatus.ConfirmedDtc,
            TestContext.CancellationToken);
        var testerEx = await Assert.ThrowsExactlyAsync<NegativeResponseException>(() =>
            client.SendRequestAsync(new byte[] { 0x3E, 0x00 }, false, TestContext.CancellationToken));

        Assert.AreEqual(NegativeResponseCode.SubFunctionNotSupported, sessionEx.Code);
        Assert.AreEqual(NegativeResponseCode.RequestOutOfRange, didEx.Code);
        Assert.AreEqual(NegativeResponseCode.RequestOutOfRange, routineEx.Code);
        Assert.AreEqual(NegativeResponseCode.ServiceNotSupported, testerEx.Code);
        Assert.IsEmpty(records);
        await simulator.StopAsync(TestContext.CancellationToken);
    }

    [TestMethod]
    public async Task ClearDtcs_RemovesAllDtcRecords()
    {
        var (client, simulator) = BuildPayloadPair();
        await using var _ = simulator;
        simulator.SetDtc(0x010203, DtcStatus.ConfirmedDtc);
        simulator.SetDtc(0x040506, DtcStatus.PendingDtc);
        simulator.ClearDtcs();

        await simulator.StartAsync(TestContext.CancellationToken);

        var records = await ReadDtcInformation.ReportDtcByStatusMaskAsync(
            client,
            DtcStatus.All,
            TestContext.CancellationToken);

        Assert.IsEmpty(records);
        await simulator.StopAsync(TestContext.CancellationToken);
    }

    [TestMethod]
    public void Options_AreClonedAndValidated()
    {
        var options = new UdsEcuSimulatorOptions { P2Server = TimeSpan.FromMilliseconds(75) };
        var simulator = new UdsEcuSimulator(
            (_, _) => Task.CompletedTask,
            _ => Task.FromResult<ReadOnlyMemory<byte>>(ReadOnlyMemory<byte>.Empty),
            options);

        options.P2Server = TimeSpan.FromSeconds(3);
        simulator.Options.P2Server = TimeSpan.FromSeconds(4);

        Assert.AreEqual(TimeSpan.FromMilliseconds(75), simulator.Options.P2Server);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new UdsEcuSimulator(
                (_, _) => Task.CompletedTask,
                _ => Task.FromResult<ReadOnlyMemory<byte>>(ReadOnlyMemory<byte>.Empty),
                new UdsEcuSimulatorOptions { P2Server = TimeSpan.Zero }));
    }

    private static (AsyncUdsClient Client, UdsEcuSimulator Simulator) BuildPayloadPair(UdsOptions? clientOptions = null)
    {
        var testerToEcu = Channel.CreateUnbounded<ReadOnlyMemory<byte>>();
        var ecuToTester = Channel.CreateUnbounded<ReadOnlyMemory<byte>>();
        var client = new AsyncUdsClient(
            (data, ct) => testerToEcu.Writer.WriteAsync(data, ct).AsTask(),
            ct => ecuToTester.Reader.ReadAsync(ct).AsTask(),
            null,
            clientOptions);
        var simulator = new UdsEcuSimulator(
            (data, ct) => ecuToTester.Writer.WriteAsync(data, ct).AsTask(),
            ct => testerToEcu.Reader.ReadAsync(ct).AsTask());

        return (client, simulator);
    }

    public TestContext TestContext { get; set; }
}
