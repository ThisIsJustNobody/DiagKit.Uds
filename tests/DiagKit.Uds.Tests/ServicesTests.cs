using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using DiagKit.Uds.Contracts;
using DiagKit.Uds.Exceptions;
using DiagKit.Uds.Services;
using DiagKit.Uds.UdsLayer;

namespace DiagKit.Uds.Tests;

[TestClass]
public class ServicesTests
{
    [TestMethod]
    public async Task DiagnosticSessionControl_Invoke_UsesIAsyncUdsClient()
    {
        IAsyncUdsClient client = new StubAsyncUdsClient(new byte[] { 0x50, 0x03 });

        var response = await DiagnosticSessionControl.InvokeAsync(
            client,
            DiagnosticSessionType.ExtendedDiagnostic,
            TestContext.CancellationToken);

        Assert.AreEqual(DiagnosticSessionType.ExtendedDiagnostic, response.Session);
        CollectionAssert.AreEqual(new byte[] { 0x10, 0x03 }, ((StubAsyncUdsClient)client).Requests[0]);
    }

    [TestMethod]
    public async Task ReadDataByIdentifier_Invoke_UsesIAsyncUdsClient()
    {
        IAsyncUdsClient client = new StubAsyncUdsClient(new byte[] { 0x62, 0xF1, 0x90, 0xAA });

        var data = await ReadDataByIdentifier.InvokeAsync(client, 0xF190, TestContext.CancellationToken);

        CollectionAssert.AreEqual(new byte[] { 0xAA }, data.ToArray());
    }

    [TestMethod]
    public void ReadDataByIdentifier_ParseResponse_ReturnsKnownDidData()
    {
        var parsed = ReadDataByIdentifier.ParseResponse(
            [0x62, 0xF1, 0x90, 0xAA, 0xBB, 0xF1, 0x91, 0xCC],
            new Dictionary<ushort, int>
            {
                [0xF190] = 2,
                [0xF191] = 1,
            });

        CollectionAssert.AreEqual(new byte[] { 0xAA, 0xBB }, parsed[0xF190]);
        CollectionAssert.AreEqual(new byte[] { 0xCC }, parsed[0xF191]);
    }

    [TestMethod]
    public void ReadDataByIdentifier_ParseResponse_RejectsUnknownOrTruncatedDid()
    {
        Assert.ThrowsExactly<ProtocolException>(() =>
            ReadDataByIdentifier.ParseResponse([0x62, 0xF1, 0x90, 0xAA], new Dictionary<ushort, int>()));
        Assert.ThrowsExactly<FrameFormatException>(() =>
            ReadDataByIdentifier.ParseResponse([0x62, 0xF1, 0x90, 0xAA], new Dictionary<ushort, int> { [0xF190] = 2 }));
    }

    [TestMethod]
    public async Task EcuReset_BuildParseInvoke_ValidatesEcho()
    {
        IAsyncUdsClient client = new StubAsyncUdsClient(new byte[] { 0x51, 0x03 });

        var request = EcuReset.BuildRequest(EcuResetType.SoftReset, suppressPositiveResponse: true);
        var response = EcuReset.ParseResponse([0x51, 0x03], EcuResetType.SoftReset);
        var invoked = await EcuReset.InvokeAsync(
            client,
            EcuResetType.SoftReset,
            cancellationToken: TestContext.CancellationToken);

        CollectionAssert.AreEqual(new byte[] { 0x11, 0x83 }, request);
        Assert.AreEqual(EcuResetType.SoftReset, response.ResetType);
        Assert.IsNull(response.PowerDownTime);
        Assert.AreEqual(EcuResetType.SoftReset, invoked.ResetType);
        CollectionAssert.AreEqual(new byte[] { 0x11, 0x03 }, ((StubAsyncUdsClient)client).Requests[0]);
        Assert.ThrowsExactly<ProtocolException>(() =>
            EcuReset.ParseResponse([0x51, 0x01], EcuResetType.SoftReset));
        Assert.ThrowsExactly<ProtocolException>(() =>
            EcuReset.ParseResponse([0x51, 0x83], EcuResetType.SoftReset));
        Assert.ThrowsExactly<FrameFormatException>(() =>
            EcuReset.ParseResponse([0x51, 0x03, 0xAA], EcuResetType.SoftReset));
        Assert.AreEqual((byte)0xAA, EcuReset.ParseResponse([0x51, 0x04, 0xAA], EcuResetType.EnableRapidPowerShutdown).PowerDownTime);
    }

    [TestMethod]
    public async Task CommunicationControl_BuildParseInvoke_ValidatesEcho()
    {
        IAsyncUdsClient client = new StubAsyncUdsClient(new byte[] { 0x68, 0x03 });

        var request = CommunicationControl.BuildRequest(
            CommunicationControlType.DisableRxAndTx,
            communicationType: 0x02,
            communicationControlRecord: [0x12, 0x34],
            suppressPositiveResponse: true);
        var response = CommunicationControl.ParseResponse([0x68, 0x03], CommunicationControlType.DisableRxAndTx);
        var invoked = await CommunicationControl.InvokeAsync(
            client,
            CommunicationControlType.DisableRxAndTx,
            communicationType: 0x02,
            cancellationToken: TestContext.CancellationToken);

        CollectionAssert.AreEqual(new byte[] { 0x28, 0x83, 0x02, 0x12, 0x34 }, request);
        Assert.AreEqual(CommunicationControlType.DisableRxAndTx, response.ControlType);
        Assert.AreEqual(CommunicationControlType.DisableRxAndTx, invoked.ControlType);
        CollectionAssert.AreEqual(new byte[] { 0x28, 0x03, 0x02 }, ((StubAsyncUdsClient)client).Requests[0]);
        Assert.ThrowsExactly<ProtocolException>(() =>
            CommunicationControl.ParseResponse([0x68, 0x02], CommunicationControlType.DisableRxAndTx));
        Assert.ThrowsExactly<ProtocolException>(() =>
            CommunicationControl.ParseResponse([0x68, 0x83], CommunicationControlType.DisableRxAndTx));
        Assert.ThrowsExactly<FrameFormatException>(() =>
            CommunicationControl.ParseResponse([0x68, 0x03, 0x00], CommunicationControlType.DisableRxAndTx));
    }

    [TestMethod]
    public async Task CommunicationControl_Invoke_AllowsCancellationTokenWithoutRecord()
    {
        IAsyncUdsClient client = new StubAsyncUdsClient(new byte[] { 0x68, 0x00 });

        var response = await CommunicationControl.InvokeAsync(
            client,
            CommunicationControlType.EnableRxAndTx,
            communicationType: 0x01,
            TestContext.CancellationToken);

        Assert.AreEqual(CommunicationControlType.EnableRxAndTx, response.ControlType);
        CollectionAssert.AreEqual(new byte[] { 0x28, 0x00, 0x01 }, ((StubAsyncUdsClient)client).Requests[0]);
    }

    [TestMethod]
    public async Task ControlDtcSetting_BuildParseInvoke_ValidatesEcho()
    {
        IAsyncUdsClient client = new StubAsyncUdsClient(new byte[] { 0xC5, 0x02 });

        var request = ControlDtcSetting.BuildRequest(
            DtcSettingType.Off,
            dtcSettingControlOptionRecord: [0xAA],
            suppressPositiveResponse: true);
        var response = ControlDtcSetting.ParseResponse([0xC5, 0x02], DtcSettingType.Off);
        var invoked = await ControlDtcSetting.InvokeAsync(
            client,
            DtcSettingType.Off,
            cancellationToken: TestContext.CancellationToken);

        CollectionAssert.AreEqual(new byte[] { 0x85, 0x82, 0xAA }, request);
        Assert.AreEqual(DtcSettingType.Off, response.SettingType);
        Assert.AreEqual(DtcSettingType.Off, invoked.SettingType);
        CollectionAssert.AreEqual(new byte[] { 0x85, 0x02 }, ((StubAsyncUdsClient)client).Requests[0]);
        Assert.ThrowsExactly<ProtocolException>(() =>
            ControlDtcSetting.ParseResponse([0xC5, 0x01], DtcSettingType.Off));
        Assert.ThrowsExactly<ProtocolException>(() =>
            ControlDtcSetting.ParseResponse([0xC5, 0x82], DtcSettingType.Off));
        Assert.ThrowsExactly<FrameFormatException>(() =>
            ControlDtcSetting.ParseResponse([0xC5, 0x02, 0x00], DtcSettingType.Off));
    }

    [TestMethod]
    public async Task ClearDiagnosticInformation_BuildParseInvoke_UsesThreeByteGroup()
    {
        IAsyncUdsClient client = new StubAsyncUdsClient(new byte[] { 0x54 });

        var request = ClearDiagnosticInformation.BuildRequest(0x00FFAA);
        var response = ClearDiagnosticInformation.ParseResponse([0x54]);
        var invoked = await ClearDiagnosticInformation.InvokeAsync(
            client,
            0x00FFAA,
            TestContext.CancellationToken);

        CollectionAssert.AreEqual(new byte[] { 0x14, 0x00, 0xFF, 0xAA }, request);
        Assert.AreEqual(default(ClearDiagnosticInformation.Response), response);
        Assert.AreEqual(default(ClearDiagnosticInformation.Response), invoked);
        CollectionAssert.AreEqual(new byte[] { 0x14, 0x00, 0xFF, 0xAA }, ((StubAsyncUdsClient)client).Requests[0]);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            ClearDiagnosticInformation.BuildRequest(0x01000000));
        Assert.ThrowsExactly<ProtocolException>(() =>
            ClearDiagnosticInformation.ParseResponse([0x53]));
        Assert.ThrowsExactly<FrameFormatException>(() =>
            ClearDiagnosticInformation.ParseResponse([0x54, 0x12]));
    }

    [TestMethod]
    public async Task WriteDataByIdentifier_BuildParseInvoke_ValidatesEcho()
    {
        IAsyncUdsClient client = new StubAsyncUdsClient(new byte[] { 0x6E, 0xF1, 0x90 });

        var request = WriteDataByIdentifier.BuildRequest(0xF190, [0x12, 0x34]);
        var response = WriteDataByIdentifier.ParseResponse([0x6E, 0xF1, 0x90], 0xF190);
        var invoked = await WriteDataByIdentifier.InvokeAsync(
            client,
            0xF190,
            new byte[] { 0x12, 0x34 },
            TestContext.CancellationToken);

        CollectionAssert.AreEqual(new byte[] { 0x2E, 0xF1, 0x90, 0x12, 0x34 }, request);
        Assert.AreEqual(0xF190, response.DataIdentifier);
        Assert.AreEqual(0xF190, invoked.DataIdentifier);
        CollectionAssert.AreEqual(new byte[] { 0x2E, 0xF1, 0x90, 0x12, 0x34 }, ((StubAsyncUdsClient)client).Requests[0]);
        Assert.ThrowsExactly<ProtocolException>(() =>
            WriteDataByIdentifier.ParseResponse([0x6E, 0xF1, 0x91], 0xF190));
    }

    [TestMethod]
    public async Task InputOutputControlByIdentifier_BuildParseInvoke_ValidatesEcho()
    {
        IAsyncUdsClient client = new StubAsyncUdsClient(new byte[] { 0x6F, 0xF1, 0x90, 0x03, 0xAA });

        var request = InputOutputControlByIdentifier.BuildRequest(
            0xF190,
            InputOutputControlParameter.ShortTermAdjustment,
            controlStateAndMaskRecord: [0x12, 0x34]);
        var response = InputOutputControlByIdentifier.ParseResponse(
            [0x6F, 0xF1, 0x90, 0x03, 0xAA],
            0xF190,
            InputOutputControlParameter.ShortTermAdjustment);
        var invoked = await InputOutputControlByIdentifier.InvokeAsync(
            client,
            0xF190,
            InputOutputControlParameter.ShortTermAdjustment,
            new byte[] { 0x12, 0x34 },
            TestContext.CancellationToken);

        CollectionAssert.AreEqual(new byte[] { 0x2F, 0xF1, 0x90, 0x03, 0x12, 0x34 }, request);
        Assert.AreEqual(0xF190, response.DataIdentifier);
        Assert.AreEqual(InputOutputControlParameter.ShortTermAdjustment, response.ControlParameter);
        CollectionAssert.AreEqual(new byte[] { 0xAA }, response.ControlStatusRecord.ToArray());
        Assert.AreEqual(0xF190, invoked.DataIdentifier);
        Assert.AreEqual(InputOutputControlParameter.ShortTermAdjustment, invoked.ControlParameter);
        CollectionAssert.AreEqual(new byte[] { 0x2F, 0xF1, 0x90, 0x03, 0x12, 0x34 }, ((StubAsyncUdsClient)client).Requests[0]);
        Assert.ThrowsExactly<ProtocolException>(() =>
            InputOutputControlByIdentifier.ParseResponse([0x6F, 0xF1, 0x91, 0x03], 0xF190));
        Assert.ThrowsExactly<ProtocolException>(() =>
            InputOutputControlByIdentifier.ParseResponse(
                [0x6F, 0xF1, 0x90, 0x02],
                0xF190,
                InputOutputControlParameter.ShortTermAdjustment));
    }

    [TestMethod]
    public async Task InputOutputControlByIdentifier_Invoke_AllowsCancellationTokenWithoutRecord()
    {
        IAsyncUdsClient client = new StubAsyncUdsClient(new byte[] { 0x6F, 0xF1, 0x90, 0x00 });

        var response = await InputOutputControlByIdentifier.InvokeAsync(
            client,
            0xF190,
            InputOutputControlParameter.ReturnControlToEcu,
            TestContext.CancellationToken);

        Assert.AreEqual(InputOutputControlParameter.ReturnControlToEcu, response.ControlParameter);
        CollectionAssert.AreEqual(new byte[] { 0x2F, 0xF1, 0x90, 0x00 }, ((StubAsyncUdsClient)client).Requests[0]);
    }

    [TestMethod]
    public void TesterPresentBuildRequest_ReturnsFreshArrays()
    {
        var first = TesterPresent.BuildRequest();
        first[0] = 0x00;

        var second = TesterPresent.BuildRequest();

        CollectionAssert.AreEqual(new byte[] { 0x3E, 0x80 }, second);
    }

    [TestMethod]
    public async Task TesterPresentPingAsync_SendsExpectedRequest()
    {
        var client = new StubAsyncUdsClient(ReadOnlyMemory<byte>.Empty.ToArray());

        var response = await TesterPresent.PingAsync(client, suppressPositiveResponse: false, TestContext.CancellationToken);

        Assert.IsTrue(response.IsEmpty);
        CollectionAssert.AreEqual(new byte[] { 0x3E, 0x00 }, client.Requests[0]);
    }

    [TestMethod]
    public async Task TesterPresentStartKeepAlive_UsesClientS3ByDefault()
    {
        var client = new StubAsyncUdsClient
        {
            Options = new UdsOptions { S3Client = TimeSpan.FromMilliseconds(20) },
        };

        await using var keepAlive = TesterPresent.StartKeepAlive(client);

        await client.FirstRequest.Task.WaitAsync(TimeSpan.FromMilliseconds(250));
        CollectionAssert.AreEqual(new byte[] { 0x3E, 0x80 }, client.Requests[0]);
    }

    [TestMethod]
    public async Task TesterPresentKeepAlive_PingFailureFaultsCompletion()
    {
        var failure = new InvalidOperationException("keep-alive failed");
        var client = new StubAsyncUdsClient
        {
            Options = new UdsOptions { S3Client = TimeSpan.FromMilliseconds(20) },
            Failure = failure,
        };
        var keepAlive = TesterPresent.StartKeepAlive(client);

        await client.FirstRequest.Task.WaitAsync(TimeSpan.FromMilliseconds(250));
        var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            keepAlive.Completion.WaitAsync(TimeSpan.FromMilliseconds(250)));

        Assert.AreSame(failure, ex);
        await keepAlive.DisposeAsync();
    }

    [TestMethod]
    public async Task TesterPresentKeepAlive_DisposeDoesNotRethrowPingFailure()
    {
        var client = new StubAsyncUdsClient
        {
            Options = new UdsOptions { S3Client = TimeSpan.FromMilliseconds(20) },
            Failure = new InvalidOperationException("keep-alive failed"),
        };
        var keepAlive = TesterPresent.StartKeepAlive(client);

        await client.FirstRequest.Task.WaitAsync(TimeSpan.FromMilliseconds(250));
        Assert.IsTrue(keepAlive.Completion.IsFaulted || await CompletesAsFaultedAsync(keepAlive.Completion));

        keepAlive.Dispose();
    }

    [TestMethod]
    public async Task SecurityAccess_SeedEchoMismatch_Throws()
    {
        IAsyncUdsClient client = new StubAsyncUdsClient(new byte[] { 0x67, 0x03, 0x01 });

        await Assert.ThrowsExactlyAsync<ProtocolException>(() =>
            SecurityAccess.UnlockAsync(client, 0x01, seed => seed, TestContext.CancellationToken));
    }

    [TestMethod]
    public async Task SecurityAccess_KeyEchoMismatch_Throws()
    {
        IAsyncUdsClient client = new StubAsyncUdsClient(
            new byte[] { 0x67, 0x01, 0x12, 0x34 },
            new byte[] { 0x67, 0x04 });

        await Assert.ThrowsExactlyAsync<ProtocolException>(() =>
            SecurityAccess.UnlockAsync(client, 0x01, seed => seed, TestContext.CancellationToken));
    }

    [TestMethod]
    public async Task SecurityAccess_UnlockAsync_UsesCanoeSeedKeyGenerator()
    {
        var client = new StubAsyncUdsClient(
            new byte[] { 0x67, 0x01, 0x12, 0x34 },
            new byte[] { 0x67, 0x02 });
        var seenSecurityLevel = 0u;
        CanoeSeedKeyGenerator.GenerateKeyExCallback generateKeyEx = (
            byte[] seed,
            uint seedSize,
            uint securityLevel,
            byte[] _,
            byte[] key,
            uint keySize,
            out uint actualKeySize) =>
        {
            seenSecurityLevel = securityLevel;
            Assert.AreEqual(2u, seedSize);
            Assert.AreEqual(2u, keySize);
            CollectionAssert.AreEqual(new byte[] { 0x12, 0x34 }, seed);

            key[0] = 0xED;
            key[1] = 0xCB;
            actualKeySize = 2;
            return CanoeKeyGenerationResult.Ok;
        };
        using var generator = new CanoeSeedKeyGenerator(generateKeyEx, null);

        var ok = await SecurityAccess.UnlockAsync(
            client,
            0x01,
            generator,
            securityLevel: 0x05,
            cancellationToken: TestContext.CancellationToken);

        Assert.IsTrue(ok);
        Assert.AreEqual(0x05u, seenSecurityLevel);
        CollectionAssert.AreEqual(new byte[] { 0x27, 0x01 }, client.Requests[0]);
        CollectionAssert.AreEqual(new byte[] { 0x27, 0x02, 0xED, 0xCB }, client.Requests[1]);
    }

    [TestMethod]
    public void ReadDtcInformation_ParseDtcByStatusMask_ReturnsRecords()
    {
        var records = ReadDtcInformation.ParseDtcByStatusMask(
            [0x59, 0x02, 0xFF, 0x01, 0x02, 0x03, (byte)DtcStatus.ConfirmedDtc]);

        Assert.HasCount(1, records);
        Assert.AreEqual(0x010203u, records[0].DtcCode);
        Assert.AreEqual(DtcStatus.ConfirmedDtc, records[0].Status);
    }

    [TestMethod]
    public void ReadDtcInformation_ParseDtcByStatusMask_RejectsWrongOrTruncatedResponse()
    {
        Assert.ThrowsExactly<ProtocolException>(() => ReadDtcInformation.ParseDtcByStatusMask([0x58, 0x02, 0xFF]));
        Assert.ThrowsExactly<FrameFormatException>(() => ReadDtcInformation.ParseDtcByStatusMask([0x59, 0x02, 0xFF, 0x01]));
    }

    [TestMethod]
    public void RoutineControl_ParseResponse_ReturnsStatusRecord()
    {
        var response = RoutineControl.ParseResponse(
            [0x71, 0x01, 0x12, 0x34, 0xAA],
            RoutineControlType.StartRoutine,
            0x1234);

        Assert.AreEqual(RoutineControlType.StartRoutine, response.Type);
        Assert.AreEqual(0x1234, response.RoutineId);
        CollectionAssert.AreEqual(new byte[] { 0xAA }, response.StatusRecord.ToArray());
    }

    [TestMethod]
    public void RoutineControl_ParseResponse_RejectsMismatchedEcho()
    {
        Assert.ThrowsExactly<ProtocolException>(() =>
            RoutineControl.ParseResponse([0x71, 0x02, 0x12, 0x34], RoutineControlType.StartRoutine, 0x1234));
        Assert.ThrowsExactly<ProtocolException>(() =>
            RoutineControl.ParseResponse([0x71, 0x01, 0x12, 0x35], RoutineControlType.StartRoutine, 0x1234));
    }

    [TestMethod]
    public async Task RoutineControl_Invoke_ValidatesEcho()
    {
        IAsyncUdsClient client = new StubAsyncUdsClient(new byte[] { 0x71, 0x02, 0x12, 0x34 });

        await Assert.ThrowsExactlyAsync<ProtocolException>(() =>
            RoutineControl.InvokeAsync(
                client,
                RoutineControlType.StartRoutine,
                0x1234,
                cancellationToken: TestContext.CancellationToken));
    }

    [TestMethod]
    public void ProtocolExceptions_ExposeStandardConstructors()
    {
        var inner = new InvalidOperationException("inner");

        Assert.IsNotNull(new FrameFormatException());
        Assert.AreSame(inner, new FrameFormatException("bad frame", inner).InnerException);
        Assert.AreSame(inner, new DoCanOverflowException("overflow", inner).InnerException);
        Assert.IsNotNull(new NegativeResponseException());
        Assert.AreEqual("negative", new NegativeResponseException("negative").Message);
        Assert.AreSame(inner, new NegativeResponseException("negative", inner).InnerException);
    }

    private static async Task<bool> CompletesAsFaultedAsync(Task task)
    {
        try
        {
            await task.WaitAsync(TimeSpan.FromMilliseconds(250));
            return false;
        }
        catch
        {
            return task.IsFaulted;
        }
    }

    private sealed class StubAsyncUdsClient : IAsyncUdsClient
    {
        private readonly Queue<ReadOnlyMemory<byte>> _responses = new();
        private readonly TaskCompletionSource _firstRequest = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public StubAsyncUdsClient(params byte[][] responses)
        {
            foreach (var response in responses)
                _responses.Enqueue(response);
        }

        public List<byte[]> Requests { get; } = [];

        public UdsOptions Options { get; set; } = new();

        public TaskCompletionSource FirstRequest => _firstRequest;

        public Exception? Failure { get; init; }

        public Task<ReadOnlyMemory<byte>> SendRequestAsync(
            ReadOnlyMemory<byte> request,
            bool? suppressResponse = null,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request.ToArray());
            _firstRequest.TrySetResult();
            if (Failure is not null)
                return Task.FromException<ReadOnlyMemory<byte>>(Failure);
            return Task.FromResult(_responses.Count == 0 ? ReadOnlyMemory<byte>.Empty : _responses.Dequeue());
        }
    }

    public TestContext TestContext { get; set; }
}
