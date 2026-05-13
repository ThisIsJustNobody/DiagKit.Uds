using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

using DiagKit.Uds.UdsLayer;

namespace DiagKit.Uds.Tests;

[TestClass]
public class AsyncUdsServerTests
{
    [TestMethod]
    public async Task DispatchesRegisteredHandler()
    {
        var inbound = Channel.CreateUnbounded<ReadOnlyMemory<byte>>();
        var outbound = Channel.CreateUnbounded<ReadOnlyMemory<byte>>();
        var server = new AsyncUdsServer(
            (data, ct) => outbound.Writer.WriteAsync(data, ct).AsTask(),
            ct => inbound.Reader.ReadAsync(ct).AsTask());

        server.Register(UdsServiceId.DiagnosticSessionControl,
            (req, ct) => Task.FromResult(AsyncUdsServer.BuildPositiveResponse(0x10, [req.Span[1]])));

        await inbound.Writer.WriteAsync(new byte[] { 0x10, 0x03 }, TestContext.CancellationToken);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var sent = await server.ReceiveAndRespondAsync(cts.Token);
        CollectionAssert.AreEqual(new byte[] { 0x50, 0x03 }, sent.ToArray());

        var written = await outbound.Reader.ReadAsync(cts.Token);
        CollectionAssert.AreEqual(new byte[] { 0x50, 0x03 }, written.ToArray());
    }

    [TestMethod]
    public async Task DispatchesRegisteredHighRequestSidHandler()
    {
        var inbound = Channel.CreateUnbounded<ReadOnlyMemory<byte>>();
        var outbound = Channel.CreateUnbounded<ReadOnlyMemory<byte>>();
        var server = new AsyncUdsServer(
            (data, ct) => outbound.Writer.WriteAsync(data, ct).AsTask(),
            ct => inbound.Reader.ReadAsync(ct).AsTask());

        server.Register(0x83,
            (req, ct) => Task.FromResult(AsyncUdsServer.BuildPositiveResponse(0x83, [req.Span[1]])));

        await inbound.Writer.WriteAsync(new byte[] { 0x83, 0x01 }, TestContext.CancellationToken);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var sent = await server.ReceiveAndRespondAsync(cts.Token);
        CollectionAssert.AreEqual(new byte[] { 0xC3, 0x01 }, sent.ToArray());

        var written = await outbound.Reader.ReadAsync(cts.Token);
        CollectionAssert.AreEqual(new byte[] { 0xC3, 0x01 }, written.ToArray());
    }

    [TestMethod]
    public async Task UnknownService_ReturnsNRC11()
    {
        var inbound = Channel.CreateUnbounded<ReadOnlyMemory<byte>>();
        var outbound = Channel.CreateUnbounded<ReadOnlyMemory<byte>>();
        var server = new AsyncUdsServer(
            (data, ct) => outbound.Writer.WriteAsync(data, ct).AsTask(),
            ct => inbound.Reader.ReadAsync(ct).AsTask());

        await inbound.Writer.WriteAsync(new byte[] { 0x10, 0x03 }, TestContext.CancellationToken);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var sent = await server.ReceiveAndRespondAsync(cts.Token);
        CollectionAssert.AreEqual(new byte[] { 0x7F, 0x10, 0x11 }, sent.ToArray());
    }

    [TestMethod]
    public async Task SuppressedPositive_NotSent()
    {
        var inbound = Channel.CreateUnbounded<ReadOnlyMemory<byte>>();
        var outbound = Channel.CreateUnbounded<ReadOnlyMemory<byte>>();
        var server = new AsyncUdsServer(
            (data, ct) => outbound.Writer.WriteAsync(data, ct).AsTask(),
            ct => inbound.Reader.ReadAsync(ct).AsTask());

        server.Register(UdsServiceId.TesterPresent,
            (req, ct) => Task.FromResult(AsyncUdsServer.BuildPositiveResponse(0x3E, [0x00])));

        await inbound.Writer.WriteAsync(new byte[] { 0x3E, 0x80 }, TestContext.CancellationToken); // suppress bit

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var sent = await server.ReceiveAndRespondAsync(cts.Token);
        Assert.IsTrue(sent.IsEmpty);
        Assert.IsFalse(outbound.Reader.TryRead(out _));
    }

    [TestMethod]
    public async Task HandlerException_Propagates()
    {
        var inbound = Channel.CreateUnbounded<ReadOnlyMemory<byte>>();
        var outbound = Channel.CreateUnbounded<ReadOnlyMemory<byte>>();
        var server = new AsyncUdsServer(
            (data, ct) => outbound.Writer.WriteAsync(data, ct).AsTask(),
            ct => inbound.Reader.ReadAsync(ct).AsTask());

        server.Register(UdsServiceId.DiagnosticSessionControl, (_, _) => throw new InvalidOperationException("boom"));
        await inbound.Writer.WriteAsync(new byte[] { 0x10, 0x03 }, TestContext.CancellationToken);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => server.ReceiveAndRespondAsync(cts.Token));

        Assert.AreEqual("boom", ex.Message);
        Assert.IsFalse(outbound.Reader.TryRead(out _));
    }

    public TestContext TestContext { get; set; }
}
