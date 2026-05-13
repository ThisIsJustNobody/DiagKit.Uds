using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;

using DiagKit.Uds.Exceptions;
using DiagKit.Uds.UdsLayer;

namespace DiagKit.Uds.Tests;

[TestClass]
public class UdsClientTests
{
    private static (UdsClient client, BlockingCollection<ReadOnlyMemory<byte>> outgoing, BlockingCollection<ReadOnlyMemory<byte>> incoming) BuildClient(UdsOptions? options = null)
    {
        var outgoing = new BlockingCollection<ReadOnlyMemory<byte>>();
        var incoming = new BlockingCollection<ReadOnlyMemory<byte>>();
        var client = new UdsClient(
            (data, ct) => outgoing.Add(data, ct),
            ct => incoming.Take(ct),
            null,
            options);
        return (client, outgoing, incoming);
    }

    [TestMethod]
    public void PositiveResponse_ReturnsBytes()
    {
        var (client, outgoing, incoming) = BuildClient();
        incoming.Add(new byte[] { 0x50, 0x03 });

        var resp = client.SendRequest(new byte[] { 0x10, 0x03 }, false, TestContext.CancellationToken);

        CollectionAssert.AreEqual(new byte[] { 0x50, 0x03 }, resp.ToArray());
        Assert.IsTrue(outgoing.TryTake(out var sent, TimeSpan.FromMilliseconds(100)));
        CollectionAssert.AreEqual(new byte[] { 0x10, 0x03 }, sent.ToArray());
    }

    [TestMethod]
    public void NegativeResponse_Throws()
    {
        var (client, _, incoming) = BuildClient();
        incoming.Add(new byte[] { 0x7F, 0x10, 0x22 });

        var ex = Assert.ThrowsExactly<NegativeResponseException>(
            () => client.SendRequest(new byte[] { 0x10, 0x03 }, false, TestContext.CancellationToken));

        Assert.AreEqual(NegativeResponseCode.ConditionsNotCorrect, ex.Code);
        Assert.AreEqual(0x10, ex.ServiceId);
    }

    [TestMethod]
    public void HighRequestSid_PositiveResponse_ReturnsBytes()
    {
        var (client, _, incoming) = BuildClient();
        incoming.Add(new byte[] { 0xC3, 0x01 });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var resp = client.SendRequest(new byte[] { 0x83, 0x01 }, false, cts.Token);

        CollectionAssert.AreEqual(new byte[] { 0xC3, 0x01 }, resp.ToArray());
    }

    [TestMethod]
    public void HighRequestSid_NegativeResponse_ThrowsForOriginalSid()
    {
        var (client, _, incoming) = BuildClient();
        incoming.Add(new byte[] { 0x7F, 0x85, 0x22 });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var ex = Assert.ThrowsExactly<NegativeResponseException>(
            () => client.SendRequest(new byte[] { 0x85, 0x02 }, false, cts.Token));

        Assert.AreEqual(NegativeResponseCode.ConditionsNotCorrect, ex.Code);
        Assert.AreEqual(0x85, ex.ServiceId);
    }

    [TestMethod]
    public void SuppressedRequest_Rc78WaitsForFinalPositiveResponse()
    {
        var options = new UdsOptions
        {
            P2Client = TimeSpan.FromMilliseconds(200),
            P2ClientExtended = TimeSpan.FromSeconds(2),
            Rc78Handling = Rc78Handling.WaitForCompletion,
            WaitWhileSuppressingResponse = true,
        };
        var (client, _, incoming) = BuildClient(options);
        incoming.Add(new byte[] { 0x7F, 0x83, 0x78 });
        incoming.Add(new byte[] { 0xC3, 0x01 });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var resp = client.SendRequest(new byte[] { 0x83, 0x81 }, cancellationToken: cts.Token);

        CollectionAssert.AreEqual(new byte[] { 0xC3, 0x01 }, resp.ToArray());
    }

    [TestMethod]
    public void SuppressedRequest_NonMatchingResponseIsDroppedByDefault()
    {
        var options = new UdsOptions
        {
            P2Client = TimeSpan.FromMilliseconds(50),
            WaitWhileSuppressingResponse = true,
            StrictServiceIdMatching = false,
        };
        var (client, _, incoming) = BuildClient(options);
        incoming.Add(new byte[] { 0x50, 0x03 });

        var resp = client.SendRequest(new byte[] { 0x3E, 0x80 }, cancellationToken: TestContext.CancellationToken);

        Assert.IsTrue(resp.IsEmpty);
    }

    [TestMethod]
    public void SuppressedRequest_NonMatchingResponseThrowsInStrictMode()
    {
        var options = new UdsOptions
        {
            P2Client = TimeSpan.FromMilliseconds(500),
            WaitWhileSuppressingResponse = true,
            StrictServiceIdMatching = true,
        };
        var (client, _, incoming) = BuildClient(options);
        incoming.Add(new byte[] { 0x50, 0x03 });

        Assert.ThrowsExactly<ProtocolException>(() =>
            client.SendRequest(new byte[] { 0x3E, 0x80 }, cancellationToken: TestContext.CancellationToken));
    }

    [TestMethod]
    public void NegativeResponseForDifferentSid_IsNotThrownAsRequestNrc()
    {
        var options = new UdsOptions
        {
            P2Client = TimeSpan.FromMilliseconds(50),
            StrictServiceIdMatching = false,
        };
        var (client, _, incoming) = BuildClient(options);
        incoming.Add(new byte[] { 0x7F, 0x10, 0x12 });

        Assert.ThrowsExactly<ProtocolException>(() =>
            client.SendRequest(new byte[] { 0x22, 0xF1, 0x90 }, false, TestContext.CancellationToken));
    }

    [TestMethod]
    public void Rc78CompletionTimeout_BoundsFinalWait()
    {
        var options = new UdsOptions
        {
            P2Client = TimeSpan.FromMilliseconds(200),
            P2ClientExtended = TimeSpan.FromSeconds(2),
            Rc78Handling = Rc78Handling.WaitForCompletion,
            Rc78CompletionTimeout = TimeSpan.FromMilliseconds(150),
        };
        var (client, _, incoming) = BuildClient(options);
        incoming.Add(new byte[] { 0x7F, 0x22, 0x78 });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var elapsed = Stopwatch.StartNew();
        Assert.ThrowsExactly<ProtocolException>(
            () => client.SendRequest(new byte[] { 0x22, 0xF1, 0x90 }, false, cts.Token));

        Assert.IsLessThan(TimeSpan.FromSeconds(1), elapsed.Elapsed, $"Elapsed {elapsed.Elapsed} should be bounded by Rc78CompletionTimeout, not P2*.");
    }

    [TestMethod]
    public void Rc78_ReturnImmediately_Throws()
    {
        var (client, _, incoming) = BuildClient(new UdsOptions { Rc78Handling = Rc78Handling.ReturnImmediately });
        incoming.Add(new byte[] { 0x7F, 0x22, 0x78 });

        var ex = Assert.ThrowsExactly<NegativeResponseException>(
            () => client.SendRequest(new byte[] { 0x22, 0xF1, 0x90 }, false, TestContext.CancellationToken));

        Assert.AreEqual(NegativeResponseCode.RequestCorrectlyReceivedResponsePending, ex.Code);
    }

    [TestMethod]
    public void Rc21_Retry_ResendsRequest()
    {
        var options = new UdsOptions
        {
            Rc21Handling = Rc21Handling.Retry,
            Rc21RetryInterval = TimeSpan.FromMilliseconds(20),
            Rc21CompletionTimeout = TimeSpan.FromSeconds(2),
            P2Client = TimeSpan.FromMilliseconds(500),
        };
        var (client, outgoing, incoming) = BuildClient(options);
        incoming.Add(new byte[] { 0x7F, 0x22, 0x21 });
        incoming.Add(new byte[] { 0x62, 0xF1, 0x90, 0x01 });

        var resp = client.SendRequest(new byte[] { 0x22, 0xF1, 0x90 }, false, TestContext.CancellationToken);

        CollectionAssert.AreEqual(new byte[] { 0x62, 0xF1, 0x90, 0x01 }, resp.ToArray());
        Assert.IsTrue(outgoing.TryTake(out var first, TimeSpan.FromMilliseconds(100)));
        Assert.IsTrue(outgoing.TryTake(out var second, TimeSpan.FromMilliseconds(100)));
        CollectionAssert.AreEqual(new byte[] { 0x22, 0xF1, 0x90 }, first.ToArray());
        CollectionAssert.AreEqual(new byte[] { 0x22, 0xF1, 0x90 }, second.ToArray());
    }

    [TestMethod]
    public void Rc21RetryCompletionTimeout_StopsRetrying()
    {
        var options = new UdsOptions
        {
            Rc21Handling = Rc21Handling.Retry,
            Rc21RetryInterval = TimeSpan.FromMilliseconds(100),
            Rc21CompletionTimeout = TimeSpan.FromMilliseconds(150),
            P2Client = TimeSpan.FromMilliseconds(500),
        };
        var (client, outgoing, incoming) = BuildClient(options);
        incoming.Add(new byte[] { 0x7F, 0x22, 0x21 });
        incoming.Add(new byte[] { 0x7F, 0x22, 0x21 });

        Assert.ThrowsExactly<ProtocolException>(
            () => client.SendRequest(new byte[] { 0x22, 0xF1, 0x90 }, false, TestContext.CancellationToken));

        Assert.IsTrue(outgoing.TryTake(out _, TimeSpan.FromMilliseconds(100)));
        Assert.IsTrue(outgoing.TryTake(out _, TimeSpan.FromMilliseconds(100)));
        Assert.IsFalse(outgoing.TryTake(out _));
    }

    [TestMethod]
    public void SuppressResponse_ReturnsEmpty()
    {
        var options = new UdsOptions
        {
            P2Client = TimeSpan.FromMilliseconds(50),
            WaitWhileSuppressingResponse = true,
        };
        var (client, _, _) = BuildClient(options);

        var resp = client.SendRequest(new byte[] { 0x3E, 0x80 }, cancellationToken: TestContext.CancellationToken);

        Assert.IsTrue(resp.IsEmpty);
    }

    [TestMethod]
    public void SuppressedRequest_ButNegativeArrives_Throws()
    {
        var options = new UdsOptions { P2Client = TimeSpan.FromMilliseconds(200) };
        var (client, _, incoming) = BuildClient(options);
        incoming.Add(new byte[] { 0x7F, 0x3E, 0x12 });

        Assert.ThrowsExactly<NegativeResponseException>(() =>
            client.SendRequest(new byte[] { 0x3E, 0x80 }, cancellationToken: TestContext.CancellationToken));
    }

    [TestMethod]
    public void NonMatchingResponse_DroppedByDefault()
    {
        var (client, _, incoming) = BuildClient(new UdsOptions
        {
            P2Client = TimeSpan.FromMilliseconds(500),
            StrictServiceIdMatching = false,
        });
        incoming.Add(new byte[] { 0x59, 0x02 });
        incoming.Add(new byte[] { 0x50, 0x03 });

        var resp = client.SendRequest(new byte[] { 0x10, 0x03 }, false, TestContext.CancellationToken);

        CollectionAssert.AreEqual(new byte[] { 0x50, 0x03 }, resp.ToArray());
    }

    [TestMethod]
    public void NonMatchingResponse_StrictThrows()
    {
        var (client, _, incoming) = BuildClient(new UdsOptions
        {
            P2Client = TimeSpan.FromMilliseconds(500),
            StrictServiceIdMatching = true,
        });
        incoming.Add(new byte[] { 0x59, 0x02 });

        Assert.ThrowsExactly<ProtocolException>(() =>
            client.SendRequest(new byte[] { 0x10, 0x03 }, false, TestContext.CancellationToken));
    }

    public TestContext TestContext { get; set; }
}
