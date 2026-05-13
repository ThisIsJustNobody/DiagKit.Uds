using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

using DiagKit.Uds.DoCan;
using DiagKit.Uds.UdsLayer;
using DiagKit.Uds.Services;

namespace DiagKit.Uds.Tests;

/// <summary>
/// End-to-end integration: UDS client ↔ DoCAN ↔ in-memory CAN bus ↔ DoCAN ↔ UDS server.
/// </summary>
[TestClass]
public class IntegrationTests
{
    private static (AsyncUdsClient client, AsyncUdsServer server, CancellationTokenSource serverLoop) BuildPair()
    {
        var clientToServer = Channel.CreateUnbounded<CanFrame>();
        var serverToClient = Channel.CreateUnbounded<CanFrame>();

        var clientCan = new AsyncDoCanTransmitter(clientToServer, serverToClient,
            new DoCanOptions { RequestId = 0x7E0, ResponseId = 0x7E8 });
        var serverCan = new AsyncDoCanTransmitter(serverToClient, clientToServer,
            new DoCanOptions { RequestId = 0x7E8, ResponseId = 0x7E0 });

        var client = new AsyncUdsClient(clientCan);
        var server = new AsyncUdsServer(serverCan);

        var loopCts = new CancellationTokenSource();
        return (client, server, loopCts);
    }

    [TestMethod]
    public async Task DiagnosticSessionControl_FullStack()
    {
        var (client, server, loop) = BuildPair();
        server.Register(UdsServiceId.DiagnosticSessionControl, (req, _) =>
            Task.FromResult(AsyncUdsServer.BuildPositiveResponse(0x10,
                [req.Span[1], 0x00, 0x32, 0x01, 0xF4])));

        var serverTask = Task.Run(async () =>
        {
            try { await server.ReceiveAndRespondAsync(loop.Token); }
            catch (OperationCanceledException) { }
        }, TestContext.CancellationToken);

        var resp = await DiagnosticSessionControl.InvokeAsync(client, DiagnosticSessionType.ExtendedDiagnostic, TestContext.CancellationToken);
        Assert.AreEqual(DiagnosticSessionType.ExtendedDiagnostic, resp.Session);
        Assert.AreEqual(TimeSpan.FromMilliseconds(50), resp.P2Server);
        Assert.AreEqual(TimeSpan.FromMilliseconds(5000), resp.P2ServerExtended);

        loop.Cancel();
        await serverTask;
    }

    [TestMethod]
    public async Task ReadDataByIdentifier_FullStack()
    {
        var (client, server, loop) = BuildPair();
        server.Register(UdsServiceId.ReadDataByIdentifier, (req, _) =>
        {
            // Echo DID + 4 byte payload
            var resp = new byte[] { 0x62, req.Span[1], req.Span[2], 0xDE, 0xAD, 0xBE, 0xEF };
            return Task.FromResult<ReadOnlyMemory<byte>>(resp);
        });

        var serverTask = Task.Run(async () =>
        {
            try { await server.ReceiveAndRespondAsync(loop.Token); }
            catch (OperationCanceledException) { }
        }, TestContext.CancellationToken);

        var data = await ReadDataByIdentifier.InvokeAsync(client, 0xF190, TestContext.CancellationToken);
        CollectionAssert.AreEqual(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }, data.ToArray());

        loop.Cancel();
        await serverTask;
    }

    [TestMethod]
    public async Task SecurityAccess_UnlocksWithProvidedSeedToKey()
    {
        var (client, server, loop) = BuildPair();
        var seed = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        var expectedKey = new byte[] { 0xFE, 0xFD, 0xFC, 0xFB };

        server.Register(UdsServiceId.SecurityAccess, (req, _) =>
        {
            byte sub = req.Span[1];
            if ((sub & 1) == 1)
            {
                // requestSeed
                var resp = new byte[] { 0x67, sub, seed[0], seed[1], seed[2], seed[3] };
                return Task.FromResult<ReadOnlyMemory<byte>>(resp);
            }
            // sendKey
            var key = req.Span[2..].ToArray();
            CollectionAssert.AreEqual(expectedKey, key);
            return Task.FromResult<ReadOnlyMemory<byte>>(new byte[] { 0x67, sub });
        });

        var serverTask = Task.Run(async () =>
        {
            try
            {
                while (!loop.IsCancellationRequested)
                    await server.ReceiveAndRespondAsync(loop.Token);
            }
            catch (OperationCanceledException) { }
        }, TestContext.CancellationToken);

        var ok = await SecurityAccess.UnlockAsync(client, 1, s =>
        {
            var k = new byte[s.Length];
            for (int i = 0; i < s.Length; i++) k[i] = (byte)~s[i];
            return k;
        }, TestContext.CancellationToken);
        Assert.IsTrue(ok);

        loop.Cancel();
        await serverTask;
    }

    public TestContext TestContext { get; set; }
}
