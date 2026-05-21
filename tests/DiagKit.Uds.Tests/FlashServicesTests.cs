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
public class FlashServicesTests
{
    [TestMethod]
    public void RequestDownload_BuildRequest_EncodesAddressAndSize()
    {
        var request = RequestDownload.BuildRequest(
            dataFormatIdentifier: 0x00,
            memoryAddress: 0x1000,
            memorySize: 0x0200,
            memoryAddressLength: 4,
            memorySizeLength: 2);

        CollectionAssert.AreEqual(
            new byte[] { 0x34, 0x00, 0x24, 0x00, 0x00, 0x10, 0x00, 0x02, 0x00 },
            request);
    }

    [TestMethod]
    public void RequestDownload_ParseResponse_ReturnsBlockLengths()
    {
        var response = RequestDownload.ParseResponse([0x74, 0x20, 0x10, 0x02]);

        Assert.AreEqual(0x1002UL, response.MaxNumberOfBlockLength);
        Assert.AreEqual(0x1000, response.MaxTransferDataPayloadLength);
    }

    [TestMethod]
    public void RequestDownload_RejectsMalformedLengths()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            RequestDownload.BuildRequest(0x00, 0x1000, 0x200, 0, 2));

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            RequestDownload.BuildRequest(0x00, 0x1_0000, 0x200, 2, 2));

        Assert.ThrowsExactly<FrameFormatException>(() =>
            RequestDownload.ParseResponse([0x74, 0x20, 0x10]));
    }

    [TestMethod]
    public async Task RequestDownload_InvokeAsync_ValidatesPositiveResponse()
    {
        IAsyncUdsClient client = new StubAsyncUdsClient([0x74, 0x20, 0x10, 0x02]);

        var response = await RequestDownload.InvokeAsync(
            client,
            dataFormatIdentifier: 0x00,
            memoryAddress: 0x1000,
            memorySize: 0x0200,
            memoryAddressLength: 4,
            memorySizeLength: 2,
            cancellationToken: TestContext.CancellationToken);

        Assert.AreEqual(0x1002UL, response.MaxNumberOfBlockLength);
        CollectionAssert.AreEqual(
            new byte[] { 0x34, 0x00, 0x24, 0x00, 0x00, 0x10, 0x00, 0x02, 0x00 },
            ((StubAsyncUdsClient)client).Requests[0]);
    }

    [TestMethod]
    public void TransferData_BuildAndParse_ValidatesBlockSequenceCounter()
    {
        var request = TransferData.BuildRequest(0x7F, [0xAA, 0xBB]);

        CollectionAssert.AreEqual(new byte[] { 0x36, 0x7F, 0xAA, 0xBB }, request);

        var response = TransferData.ParseResponse([0x76, 0x7F, 0x01], expectedBlockSequenceCounter: 0x7F);

        Assert.AreEqual(0x7F, response.BlockSequenceCounter);
        CollectionAssert.AreEqual(new byte[] { 0x01 }, response.TransferResponseParameterRecord.ToArray());
        Assert.ThrowsExactly<ProtocolException>(() =>
            TransferData.ParseResponse([0x76, 0x80], expectedBlockSequenceCounter: 0x7F));
    }

    [TestMethod]
    public async Task TransferData_SendBlocksAsync_SplitsPayloadAndWrapsCounter()
    {
        var client = new StubAsyncUdsClient(
            [0x76, 0xFE],
            [0x76, 0xFF],
            [0x76, 0x00]);

        var responses = await TransferData.SendBlocksAsync(
            client,
            data: new byte[] { 0xAA, 0xBB, 0xCC, 0xDD, 0xEE },
            maxTransferDataPayloadLength: 2,
            firstBlockSequenceCounter: 0xFE,
            cancellationToken: TestContext.CancellationToken);

        Assert.HasCount(3, responses);
        CollectionAssert.AreEqual(new byte[] { 0x36, 0xFE, 0xAA, 0xBB }, client.Requests[0]);
        CollectionAssert.AreEqual(new byte[] { 0x36, 0xFF, 0xCC, 0xDD }, client.Requests[1]);
        CollectionAssert.AreEqual(new byte[] { 0x36, 0x00, 0xEE }, client.Requests[2]);
    }

    [TestMethod]
    public void RequestTransferExit_BuildAndParse_SupportsParameterRecords()
    {
        var request = RequestTransferExit.BuildRequest([0x12, 0x34]);

        CollectionAssert.AreEqual(new byte[] { 0x37, 0x12, 0x34 }, request);

        var response = RequestTransferExit.ParseResponse([0x77, 0xAB, 0xCD]);

        CollectionAssert.AreEqual(new byte[] { 0xAB, 0xCD }, response.TransferResponseParameterRecord.ToArray());
        Assert.ThrowsExactly<ProtocolException>(() =>
            RequestTransferExit.ParseResponse([0x76]));
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
