using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using DiagKit.Uds.Contracts;
using DiagKit.Uds.Services;
using DiagKit.Uds.UdsLayer;

namespace DiagKit.Uds.Tests;

[TestClass]
public class RoutineControlCompletionTests
{
    [TestMethod]
    public async Task StartAndExpectCompletedAsync_PollsResultsUntilPredicateMatches()
    {
        var client = new StubAsyncUdsClient(
            [0x71, 0x01, 0x12, 0x34, 0x01],
            [0x71, 0x03, 0x12, 0x34, 0x01],
            [0x71, 0x03, 0x12, 0x34, 0x00]);

        var response = await RoutineControl.StartAndExpectCompletedAsync(
            client,
            routineId: 0x1234,
            isCompleted: r => r.StatusRecord.Length == 1 && r.StatusRecord.Span[0] == 0x00,
            routineData: new byte[] { 0xAA },
            pollInterval: TimeSpan.Zero,
            timeout: TimeSpan.FromSeconds(1),
            cancellationToken: TestContext.CancellationToken);

        Assert.AreEqual(RoutineControlType.RequestRoutineResults, response.Type);
        CollectionAssert.AreEqual(new byte[] { 0x31, 0x01, 0x12, 0x34, 0xAA }, client.Requests[0]);
        CollectionAssert.AreEqual(new byte[] { 0x31, 0x03, 0x12, 0x34 }, client.Requests[1]);
        CollectionAssert.AreEqual(new byte[] { 0x31, 0x03, 0x12, 0x34 }, client.Requests[2]);
    }

    [TestMethod]
    public async Task StartAndExpectCompletedAsync_ReturnsStartResponseWhenAlreadyComplete()
    {
        var client = new StubAsyncUdsClient([0x71, 0x01, 0x12, 0x34, 0x00]);

        var response = await RoutineControl.StartAndExpectCompletedAsync(
            client,
            routineId: 0x1234,
            isCompleted: r => r.StatusRecord.Length == 1 && r.StatusRecord.Span[0] == 0x00,
            pollInterval: TimeSpan.Zero,
            timeout: TimeSpan.FromSeconds(1),
            cancellationToken: TestContext.CancellationToken);

        Assert.AreEqual(RoutineControlType.StartRoutine, response.Type);
        Assert.HasCount(1, client.Requests);
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
