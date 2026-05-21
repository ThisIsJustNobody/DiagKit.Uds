using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using DiagKit.Uds.Contracts;
using DiagKit.Uds.Services;
using DiagKit.Uds.UdsLayer;

namespace DiagKit.Uds.Tests;

[TestClass]
public class SecurityAccessOemTests
{
    [TestMethod]
    public void BuildRequestSeed_WithPayload_AppendsSecurityAccessDataRecord()
    {
        var request = SecurityAccess.BuildRequestSeed(0x01, [0xAA, 0x55]);

        CollectionAssert.AreEqual(new byte[] { 0x27, 0x01, 0xAA, 0x55 }, request);
    }

    [TestMethod]
    public async Task UnlockAsync_WithOemContext_SendsCustomSeedAndKeyPayloads()
    {
        var client = new StubAsyncUdsClient(
            [0x67, 0x01, 0x12, 0x34],
            [0x67, 0x05]);
        SecurityAccessSeedContext? seenContext = null;

        var unlocked = await SecurityAccess.UnlockAsync(
            client,
            requestSeedLevel: 0x01,
            requestSeedParameterRecord: new byte[] { 0xAA, 0x55 },
            keyParameterRecordBuilder: context =>
            {
                seenContext = context;
                return [context.SendKeyLevel, context.Seed.Span[0], context.RequestSeedParameterRecord.Span[1]];
            },
            sendKeyLevel: 0x05,
            cancellationToken: TestContext.CancellationToken);

        Assert.IsTrue(unlocked);
        Assert.IsTrue(seenContext.HasValue);
        Assert.AreEqual(0x01, seenContext.Value.RequestSeedLevel);
        Assert.AreEqual(0x05, seenContext.Value.SendKeyLevel);
        CollectionAssert.AreEqual(new byte[] { 0x12, 0x34 }, seenContext.Value.Seed.ToArray());
        CollectionAssert.AreEqual(new byte[] { 0xAA, 0x55 }, seenContext.Value.RequestSeedParameterRecord.ToArray());
        CollectionAssert.AreEqual(new byte[] { 0x27, 0x01, 0xAA, 0x55 }, client.Requests[0]);
        CollectionAssert.AreEqual(new byte[] { 0x27, 0x05, 0x05, 0x12, 0x55 }, client.Requests[1]);
    }

    [TestMethod]
    public async Task UnlockAsync_WithOemContext_AllZeroSeedDoesNotSendKey()
    {
        var client = new StubAsyncUdsClient([0x67, 0x03, 0x00, 0x00]);
        var called = false;

        var unlocked = await SecurityAccess.UnlockAsync(
            client,
            requestSeedLevel: 0x03,
            requestSeedParameterRecord: ReadOnlyMemory<byte>.Empty,
            keyParameterRecordBuilder: _ =>
            {
                called = true;
                return [];
            },
            cancellationToken: TestContext.CancellationToken);

        Assert.IsTrue(unlocked);
        Assert.IsFalse(called);
        Assert.HasCount(1, client.Requests);
        CollectionAssert.AreEqual(new byte[] { 0x27, 0x03 }, client.Requests[0]);
    }

    private sealed class StubAsyncUdsClient : IAsyncUdsClient
    {
        private readonly Queue<ReadOnlyMemory<byte>> _responses = new();

        public StubAsyncUdsClient(params byte[][] responses)
        {
            foreach (var response in responses)
                _responses.Enqueue(response);
        }

        public List<byte[]> Requests { get; } = [];

        public UdsOptions Options { get; } = new();

        public Task<ReadOnlyMemory<byte>> SendRequestAsync(
            ReadOnlyMemory<byte> request,
            bool? suppressResponse = null,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request.ToArray());
            return Task.FromResult(_responses.Count == 0 ? ReadOnlyMemory<byte>.Empty : _responses.Dequeue());
        }
    }

    public TestContext TestContext { get; set; }
}
