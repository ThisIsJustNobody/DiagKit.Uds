using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

using DiagKit.Uds.Exceptions;
using DiagKit.Uds.UdsLayer;

namespace DiagKit.Uds.Tests;

[TestClass]
public class AsyncUdsClientTests
{
    private static (AsyncUdsClient client, Channel<ReadOnlyMemory<byte>> outgoing, Channel<ReadOnlyMemory<byte>> incoming) BuildClient(
        UdsOptions? options = null,
        Action? clearReceiveBuffer = null)
    {
        var outgoing = Channel.CreateUnbounded<ReadOnlyMemory<byte>>();
        var incoming = Channel.CreateUnbounded<ReadOnlyMemory<byte>>();
        var client = new AsyncUdsClient(
            (data, ct) => outgoing.Writer.WriteAsync(data, ct).AsTask(),
            ct => incoming.Reader.ReadAsync(ct).AsTask(),
            clearReceiveBuffer,
            options);
        return (client, outgoing, incoming);
    }

    [TestMethod]
    public async Task PositiveResponse_ReturnsBytes()
    {
        var (client, outgoing, incoming) = BuildClient();
        await incoming.Writer.WriteAsync(new byte[] { 0x50, 0x03 }, TestContext.CancellationToken);
        var resp = await client.SendRequestAsync(new byte[] { 0x10, 0x03 }, false, TestContext.CancellationToken);
        CollectionAssert.AreEqual(new byte[] { 0x50, 0x03 }, resp.ToArray());

        var sent = await outgoing.Reader.ReadAsync(TestContext.CancellationToken);
        CollectionAssert.AreEqual(new byte[] { 0x10, 0x03 }, sent.ToArray());
    }

    [TestMethod]
    public async Task RequestBufferMutation_DoesNotAffectMatchingOrSentBytes()
    {
        var sent = new TaskCompletionSource<ReadOnlyMemory<byte>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var incoming = new TaskCompletionSource<ReadOnlyMemory<byte>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new AsyncUdsClient(
            (data, _) =>
            {
                sent.SetResult(data);
                return Task.CompletedTask;
            },
            ct => incoming.Task.WaitAsync(ct),
            null,
            new UdsOptions { StrictServiceIdMatching = true });
        var request = new byte[] { 0x22, 0xF1, 0x90 };

        var responseTask = client.SendRequestAsync(request, false, TestContext.CancellationToken);
        var sentSnapshot = await sent.Task.WaitAsync(TestContext.CancellationToken);
        request[0] = 0x10;
        request[1] = 0x03;
        request[2] = 0x00;
        incoming.SetResult(new byte[] { 0x62, 0xF1, 0x90, 0x12 });

        var response = await responseTask;

        CollectionAssert.AreEqual(new byte[] { 0x22, 0xF1, 0x90 }, sentSnapshot.ToArray());
        CollectionAssert.AreEqual(new byte[] { 0x62, 0xF1, 0x90, 0x12 }, response.ToArray());
    }

    [TestMethod]
    public async Task ClearReceiveBufferBeforeRequest_DrainsStalePayloadBeforeFirstSend()
    {
        var outgoing = Channel.CreateUnbounded<ReadOnlyMemory<byte>>();
        var incoming = Channel.CreateUnbounded<ReadOnlyMemory<byte>>();
        await incoming.Writer.WriteAsync(new byte[] { 0x62, 0xF1, 0x90, 0xAA }, TestContext.CancellationToken);
        var clearCalls = 0;
        void Clear()
        {
            clearCalls++;
            while (incoming.Reader.TryRead(out _))
            {
            }
        }

        var client = new AsyncUdsClient(
            (data, ct) => outgoing.Writer.WriteAsync(data, ct).AsTask(),
            ct => incoming.Reader.ReadAsync(ct).AsTask(),
            Clear,
            new UdsOptions { P2Client = TimeSpan.FromMilliseconds(500) });

        var responseTask = client.SendRequestAsync(new byte[] { 0x22, 0xF1, 0x90 }, false, TestContext.CancellationToken);
        var sent = await outgoing.Reader.ReadAsync(TestContext.CancellationToken);
        await incoming.Writer.WriteAsync(new byte[] { 0x62, 0xF1, 0x90, 0xBB }, TestContext.CancellationToken);

        var response = await responseTask;

        Assert.AreEqual(1, clearCalls);
        CollectionAssert.AreEqual(new byte[] { 0x22, 0xF1, 0x90 }, sent.ToArray());
        CollectionAssert.AreEqual(new byte[] { 0x62, 0xF1, 0x90, 0xBB }, response.ToArray());
    }

    [TestMethod]
    public async Task ClearReceiveBufferBeforeRequest_False_DoesNotDrainStalePayload()
    {
        var clearCalls = 0;
        var options = new UdsOptions
        {
            ClearReceiveBufferBeforeRequest = false,
            P2Client = TimeSpan.FromMilliseconds(500),
        };
        var (client, outgoing, incoming) = BuildClient(options, () => clearCalls++);
        await incoming.Writer.WriteAsync(new byte[] { 0x62, 0xF1, 0x90, 0xAA }, TestContext.CancellationToken);

        var response = await client.SendRequestAsync(new byte[] { 0x22, 0xF1, 0x90 }, false, TestContext.CancellationToken);
        var sent = await outgoing.Reader.ReadAsync(TestContext.CancellationToken);

        Assert.AreEqual(0, clearCalls);
        CollectionAssert.AreEqual(new byte[] { 0x22, 0xF1, 0x90 }, sent.ToArray());
        CollectionAssert.AreEqual(new byte[] { 0x62, 0xF1, 0x90, 0xAA }, response.ToArray());
    }

    [TestMethod]
    public async Task HighRequestSid_PositiveResponse_ReturnsBytes()
    {
        var (client, _, incoming) = BuildClient();
        await incoming.Writer.WriteAsync(new byte[] { 0xC3, 0x01 }, TestContext.CancellationToken);

        var resp = await client.SendRequestAsync(new byte[] { 0x83, 0x01 }, false, TestContext.CancellationToken);

        CollectionAssert.AreEqual(new byte[] { 0xC3, 0x01 }, resp.ToArray());
    }

    [TestMethod]
    public async Task NegativeResponse_Throws()
    {
        var (client, _, incoming) = BuildClient();
        await incoming.Writer.WriteAsync(new byte[] { 0x7F, 0x10, 0x22 }, TestContext.CancellationToken); // ConditionsNotCorrect
        var ex = await Assert.ThrowsExactlyAsync<NegativeResponseException>(
            () => client.SendRequestAsync(new byte[] { 0x10, 0x03 }, false, TestContext.CancellationToken));
        Assert.AreEqual(NegativeResponseCode.ConditionsNotCorrect, ex.Code);
        Assert.AreEqual(0x10, ex.ServiceId);
    }

    [TestMethod]
    public async Task HighRequestSid_NegativeResponse_ThrowsForOriginalSid()
    {
        var (client, _, incoming) = BuildClient();
        await incoming.Writer.WriteAsync(new byte[] { 0x7F, 0x85, 0x22 }, TestContext.CancellationToken);

        var ex = await Assert.ThrowsExactlyAsync<NegativeResponseException>(
            () => client.SendRequestAsync(new byte[] { 0x85, 0x02 }, false, TestContext.CancellationToken));

        Assert.AreEqual(NegativeResponseCode.ConditionsNotCorrect, ex.Code);
        Assert.AreEqual(0x85, ex.ServiceId);
    }

    [TestMethod]
    public async Task Rc78_WaitsForFinalResponse()
    {
        var options = new UdsOptions
        {
            P2Client = TimeSpan.FromMilliseconds(200),
            P2ClientExtended = TimeSpan.FromSeconds(2),
            Rc78Handling = Rc78Handling.WaitForCompletion,
        };
        var (client, _, incoming) = BuildClient(options);

        // First the ECU sends RC 0x78 several times, then a positive response.
        await incoming.Writer.WriteAsync(new byte[] { 0x7F, 0x22, 0x78 }, TestContext.CancellationToken);
        await incoming.Writer.WriteAsync(new byte[] { 0x7F, 0x22, 0x78 }, TestContext.CancellationToken);
        await incoming.Writer.WriteAsync(new byte[] { 0x62, 0xF1, 0x90, 0xAB, 0xCD }, TestContext.CancellationToken);

        var resp = await client.SendRequestAsync(new byte[] { 0x22, 0xF1, 0x90 }, false, TestContext.CancellationToken);
        CollectionAssert.AreEqual(new byte[] { 0x62, 0xF1, 0x90, 0xAB, 0xCD }, resp.ToArray());
    }

    [TestMethod]
    public async Task Rc78CompletionTimeout_BoundsFinalWait()
    {
        var options = new UdsOptions
        {
            P2Client = TimeSpan.FromMilliseconds(200),
            P2ClientExtended = TimeSpan.FromSeconds(5),
            Rc78Handling = Rc78Handling.WaitForCompletion,
            Rc78CompletionTimeout = TimeSpan.FromMilliseconds(500),
        };
        var (client, _, incoming) = BuildClient(options);
        await incoming.Writer.WriteAsync(new byte[] { 0x7F, 0x22, 0x78 }, TestContext.CancellationToken);

        var elapsed = Stopwatch.StartNew();
        var ex = await Assert.ThrowsExactlyAsync<ProtocolException>(
            () => client.SendRequestAsync(new byte[] { 0x22, 0xF1, 0x90 }, false, CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(6), TestContext.CancellationToken));

        StringAssert.Contains(ex.Message, "RC 0x78 kept");
        Assert.IsLessThan(TimeSpan.FromSeconds(2), elapsed.Elapsed, $"Elapsed {elapsed.Elapsed} should be bounded by Rc78CompletionTimeout, not P2*.");
    }

    [TestMethod]
    public async Task Rc78_ReturnImmediately_Throws()
    {
        var options = new UdsOptions { Rc78Handling = Rc78Handling.ReturnImmediately };
        var (client, _, incoming) = BuildClient(options);
        await incoming.Writer.WriteAsync(new byte[] { 0x7F, 0x22, 0x78 }, TestContext.CancellationToken);
        await Assert.ThrowsExactlyAsync<NegativeResponseException>(
            () => client.SendRequestAsync(new byte[] { 0x22, 0xF1, 0x90 }, false, TestContext.CancellationToken));
    }

    [TestMethod]
    public async Task Rc21_Retry_ResendsRequest()
    {
        var options = new UdsOptions
        {
            Rc21Handling = Rc21Handling.Retry,
            Rc21RetryInterval = TimeSpan.FromMilliseconds(20),
            Rc21CompletionTimeout = TimeSpan.FromSeconds(2),
            P2Client = TimeSpan.FromMilliseconds(500),
        };
        var (client, outgoing, incoming) = BuildClient(options);

        await incoming.Writer.WriteAsync(new byte[] { 0x7F, 0x22, 0x21 }, TestContext.CancellationToken); // Busy
        await incoming.Writer.WriteAsync(new byte[] { 0x62, 0xF1, 0x90, 0x01 }, TestContext.CancellationToken); // success

        var resp = await client.SendRequestAsync(new byte[] { 0x22, 0xF1, 0x90 }, false, TestContext.CancellationToken);
        CollectionAssert.AreEqual(new byte[] { 0x62, 0xF1, 0x90, 0x01 }, resp.ToArray());

        // Should have sent the request twice.
        var first = await outgoing.Reader.ReadAsync(TestContext.CancellationToken);
        var second = await outgoing.Reader.ReadAsync(TestContext.CancellationToken);
        CollectionAssert.AreEqual(new byte[] { 0x22, 0xF1, 0x90 }, first.ToArray());
        CollectionAssert.AreEqual(new byte[] { 0x22, 0xF1, 0x90 }, second.ToArray());
    }

    [TestMethod]
    public async Task Rc21Retry_ClearsReceiveBufferAndRunsLifecycleOnce()
    {
        var lifecycle = new List<string>();
        var clearCalls = 0;
        var options = new UdsOptions
        {
            Rc21Handling = Rc21Handling.Retry,
            Rc21RetryInterval = TimeSpan.FromMilliseconds(1),
            Rc21CompletionTimeout = TimeSpan.FromSeconds(2),
            P2Client = TimeSpan.FromMilliseconds(500),
            InitializeOrClearUpAction = started => lifecycle.Add(started ? "start" : "end"),
        };
        var (client, outgoing, incoming) = BuildClient(options, () => clearCalls++);

        var responseTask = client.SendRequestAsync(new byte[] { 0x22, 0xF1, 0x90 }, false, TestContext.CancellationToken);
        var first = await outgoing.Reader.ReadAsync(TestContext.CancellationToken);
        await incoming.Writer.WriteAsync(new byte[] { 0x7F, 0x22, 0x21 }, TestContext.CancellationToken);
        var second = await outgoing.Reader.ReadAsync(TestContext.CancellationToken);
        await incoming.Writer.WriteAsync(new byte[] { 0x62, 0xF1, 0x90, 0x01 }, TestContext.CancellationToken);

        var response = await responseTask;

        Assert.AreEqual(1, clearCalls);
        CollectionAssert.AreEqual(new[] { "start", "end" }, lifecycle);
        CollectionAssert.AreEqual(new byte[] { 0x22, 0xF1, 0x90 }, first.ToArray());
        CollectionAssert.AreEqual(new byte[] { 0x22, 0xF1, 0x90 }, second.ToArray());
        CollectionAssert.AreEqual(new byte[] { 0x62, 0xF1, 0x90, 0x01 }, response.ToArray());
    }

    [TestMethod]
    public async Task LifecycleHook_RunsEndOnTimeout()
    {
        var lifecycle = new List<string>();
        var options = new UdsOptions
        {
            P2Client = TimeSpan.FromMilliseconds(20),
            InitializeOrClearUpAction = started => lifecycle.Add(started ? "start" : "end"),
        };
        var (client, _, _) = BuildClient(options);

        await Assert.ThrowsExactlyAsync<ProtocolException>(
            () => client.SendRequestAsync(new byte[] { 0x22, 0xF1, 0x90 }, false, TestContext.CancellationToken));

        CollectionAssert.AreEqual(new[] { "start", "end" }, lifecycle);
    }

    [TestMethod]
    public async Task Rc21RetryCompletionTimeout_StopsRetrying()
    {
        var options = new UdsOptions
        {
            Rc21Handling = Rc21Handling.Retry,
            Rc21RetryInterval = TimeSpan.FromMilliseconds(100),
            Rc21CompletionTimeout = TimeSpan.FromMilliseconds(150),
            P2Client = TimeSpan.FromMilliseconds(500),
        };
        var (client, outgoing, incoming) = BuildClient(options);

        await incoming.Writer.WriteAsync(new byte[] { 0x7F, 0x22, 0x21 }, TestContext.CancellationToken);
        await incoming.Writer.WriteAsync(new byte[] { 0x7F, 0x22, 0x21 }, TestContext.CancellationToken);

        await Assert.ThrowsExactlyAsync<ProtocolException>(
            () => client.SendRequestAsync(new byte[] { 0x22, 0xF1, 0x90 }, false, TestContext.CancellationToken));

        _ = await outgoing.Reader.ReadAsync(TestContext.CancellationToken);
        _ = await outgoing.Reader.ReadAsync(TestContext.CancellationToken);
        Assert.IsFalse(outgoing.Reader.TryRead(out _));
    }

    [TestMethod]
    public async Task SuppressResponse_ReturnsEmpty()
    {
        var options = new UdsOptions
        {
            P2Client = TimeSpan.FromMilliseconds(50),
            WaitWhileSuppressingResponse = true,
        };
        var (client, _, _) = BuildClient(options);
        // suppress bit on (0x80) → service 0x3E sub 0x80
        var resp = await client.SendRequestAsync(new byte[] { 0x3E, 0x80 }, cancellationToken: TestContext.CancellationToken);
        Assert.IsTrue(resp.IsEmpty);
    }

    [TestMethod]
    public async Task SuppressedRequest_ButNegativeArrives_Throws()
    {
        var options = new UdsOptions { P2Client = TimeSpan.FromMilliseconds(200) };
        var (client, _, incoming) = BuildClient(options);
        await incoming.Writer.WriteAsync(new byte[] { 0x7F, 0x3E, 0x12 }, TestContext.CancellationToken);
        await Assert.ThrowsExactlyAsync<NegativeResponseException>(
            () => client.SendRequestAsync(new byte[] { 0x3E, 0x80 }, cancellationToken: TestContext.CancellationToken));
    }

    [TestMethod]
    public async Task SuppressedRequest_Rc78WaitsForFinalPositiveResponse()
    {
        var options = new UdsOptions
        {
            P2Client = TimeSpan.FromMilliseconds(200),
            P2ClientExtended = TimeSpan.FromSeconds(2),
            Rc78Handling = Rc78Handling.WaitForCompletion,
            WaitWhileSuppressingResponse = true,
        };
        var (client, _, incoming) = BuildClient(options);

        await incoming.Writer.WriteAsync(new byte[] { 0x7F, 0x83, 0x78 }, TestContext.CancellationToken);
        await incoming.Writer.WriteAsync(new byte[] { 0xC3, 0x01 }, TestContext.CancellationToken);

        var resp = await client.SendRequestAsync(new byte[] { 0x83, 0x81 }, cancellationToken: TestContext.CancellationToken);

        CollectionAssert.AreEqual(new byte[] { 0xC3, 0x01 }, resp.ToArray());
    }

    [TestMethod]
    public async Task SuppressedRequest_NonMatchingResponseIsDroppedByDefault()
    {
        var options = new UdsOptions
        {
            P2Client = TimeSpan.FromMilliseconds(50),
            WaitWhileSuppressingResponse = true,
            StrictServiceIdMatching = false,
        };
        var (client, _, incoming) = BuildClient(options);

        await incoming.Writer.WriteAsync(new byte[] { 0x50, 0x03 }, TestContext.CancellationToken);

        var resp = await client.SendRequestAsync(new byte[] { 0x3E, 0x80 }, cancellationToken: TestContext.CancellationToken);

        Assert.IsTrue(resp.IsEmpty);
    }

    [TestMethod]
    public async Task SuppressedRequest_NonMatchingResponsesDoNotExtendP2Window()
    {
        var options = new UdsOptions
        {
            P2Client = TimeSpan.FromMilliseconds(50),
            WaitWhileSuppressingResponse = true,
            StrictServiceIdMatching = false,
        };
        var receives = 0;
        var client = new AsyncUdsClient(
            (_, _) => Task.CompletedTask,
            ct =>
            {
                if (Interlocked.Increment(ref receives) == 1)
                    return Task.FromResult<ReadOnlyMemory<byte>>(new byte[] { 0x50, 0x03 });
                ct.WaitHandle.WaitOne();
                ct.ThrowIfCancellationRequested();
                return Task.FromResult<ReadOnlyMemory<byte>>(new byte[] { 0x50, 0x03 });
            },
            options: options);

        var sw = Stopwatch.StartNew();
        var resp = await client.SendRequestAsync(new byte[] { 0x3E, 0x80 }, cancellationToken: CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(3), TestContext.CancellationToken);

        Assert.IsTrue(resp.IsEmpty);
        Assert.IsGreaterThanOrEqualTo(2, receives);
        Assert.IsTrue(sw.Elapsed < TimeSpan.FromMilliseconds(250));
    }

    [TestMethod]
    public async Task SuppressedRequest_NonMatchingResponseThrowsInStrictMode()
    {
        var options = new UdsOptions
        {
            P2Client = TimeSpan.FromMilliseconds(500),
            WaitWhileSuppressingResponse = true,
            StrictServiceIdMatching = true,
        };
        var (client, _, incoming) = BuildClient(options);

        await incoming.Writer.WriteAsync(new byte[] { 0x50, 0x03 }, TestContext.CancellationToken);

        await Assert.ThrowsExactlyAsync<ProtocolException>(() =>
            client.SendRequestAsync(new byte[] { 0x3E, 0x80 }, cancellationToken: TestContext.CancellationToken));
    }

    [TestMethod]
    public async Task NegativeResponseForDifferentSid_IsNotThrownAsRequestNrc()
    {
        var options = new UdsOptions
        {
            P2Client = TimeSpan.FromMilliseconds(50),
            StrictServiceIdMatching = false,
        };
        var (client, _, incoming) = BuildClient(options);

        await incoming.Writer.WriteAsync(new byte[] { 0x7F, 0x10, 0x12 }, TestContext.CancellationToken);

        await Assert.ThrowsExactlyAsync<ProtocolException>(() =>
            client.SendRequestAsync(new byte[] { 0x22, 0xF1, 0x90 }, false, TestContext.CancellationToken));
    }

    [TestMethod]
    public async Task Timeout_RaisesProtocolException()
    {
        var options = new UdsOptions { P2Client = TimeSpan.FromMilliseconds(50) };
        var (client, _, _) = BuildClient(options);
        await Assert.ThrowsExactlyAsync<ProtocolException>(
            () => client.SendRequestAsync(new byte[] { 0x10, 0x03 }, false, TestContext.CancellationToken));
    }

    [TestMethod]
    public async Task NonMatchingResponse_DroppedByDefault()
    {
        var (client, _, incoming) = BuildClient(new UdsOptions
        {
            P2Client = TimeSpan.FromMilliseconds(500),
            StrictServiceIdMatching = false,
        });

        // First send a different SID's response (should be discarded), then the right one.
        await incoming.Writer.WriteAsync(new byte[] { 0x59, 0x02 }, TestContext.CancellationToken); // ReadDtcInformation response
        await incoming.Writer.WriteAsync(new byte[] { 0x50, 0x03 }, TestContext.CancellationToken); // expected
        var resp = await client.SendRequestAsync(new byte[] { 0x10, 0x03 }, false, TestContext.CancellationToken);
        CollectionAssert.AreEqual(new byte[] { 0x50, 0x03 }, resp.ToArray());
    }

    [TestMethod]
    public async Task NonMatchingResponse_StrictThrows()
    {
        var (client, _, incoming) = BuildClient(new UdsOptions
        {
            P2Client = TimeSpan.FromMilliseconds(500),
            StrictServiceIdMatching = true,
        });
        await incoming.Writer.WriteAsync(new byte[] { 0x59, 0x02 }, TestContext.CancellationToken);
        await Assert.ThrowsExactlyAsync<ProtocolException>(
            () => client.SendRequestAsync(new byte[] { 0x10, 0x03 }, false, TestContext.CancellationToken));
    }

    public TestContext TestContext { get; set; }
}
