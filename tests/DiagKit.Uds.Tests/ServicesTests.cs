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
