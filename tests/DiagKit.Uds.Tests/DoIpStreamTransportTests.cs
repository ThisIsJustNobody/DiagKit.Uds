using System;
using System.Buffers.Binary;
using System.IO;
using System.Net.Security;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

using DiagKit.Uds.DoIp;
using DiagKit.Uds.Exceptions;

namespace DiagKit.Uds.Tests;

[TestClass]
public class DoIpStreamTransportTests
{
    [TestMethod]
    public async Task Codec_ReadMessageAsync_FragmentedStream_RoundTrips()
    {
        var message = new DoIpMessage(DoIpPayloadType.DiagnosticMessage, new byte[] { 0x10, 0x01, 0x22, 0xF1, 0x90 });
        await using var stream = new FragmentedReadStream(message.ToBytes(), maxChunkSize: 2);

        var parsed = await DoIpStreamCodec.ReadMessageAsync(stream, maxPayloadLength: 64, TestContext.CancellationToken);

        Assert.AreEqual(DoIpPayloadType.DiagnosticMessage, parsed.PayloadType);
        CollectionAssert.AreEqual(message.Payload.ToArray(), parsed.Payload.ToArray());
    }

    [TestMethod]
    public async Task Codec_ReadMessageAsync_PayloadLengthOverLimit_Throws()
    {
        var header = new byte[] { 0x02, 0xFD, 0x80, 0x01, 0x00, 0x00, 0x00, 0x05 };
        await using var stream = new FragmentedReadStream(header, maxChunkSize: 8);

        await Assert.ThrowsExactlyAsync<ProtocolException>(
            () => DoIpStreamCodec.ReadMessageAsync(stream, maxPayloadLength: 4, TestContext.CancellationToken));
    }

    [TestMethod]
    public async Task RoutingActivation_Success_IsSentOnlyOnce()
    {
        var (clientStream, serverStream) = DuplexStreamPair.Create();
        await using var transport = new AsyncDoIpStreamTransport(clientStream, TestOptions(), leaveOpen: false);
        await using var server = serverStream;

        var serverTask = Task.Run(async () =>
        {
            var request = await DoIpStreamCodec.ReadMessageAsync(server, 1024, TestContext.CancellationToken);
            Assert.AreEqual(DoIpPayloadType.RoutingActivationRequest, request.PayloadType);
            Assert.AreEqual(7, request.Payload.Length);
            Assert.AreEqual(0x0E00, BinaryPrimitives.ReadUInt16BigEndian(request.Payload.Span[..2]));
            Assert.AreEqual((byte)DoIpActivationType.Default, request.Payload.Span[2]);

            await DoIpStreamCodec.WriteMessageAsync(server, RoutingActivationResponse(), 1024, TestContext.CancellationToken);

            using var noSecondRequest = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
            await Assert.ThrowsExactlyAsync<OperationCanceledException>(
                () => DoIpStreamCodec.ReadMessageAsync(server, 1024, noSecondRequest.Token));
        }, TestContext.CancellationToken);

        await transport.ActivateRoutingAsync(TestContext.CancellationToken);
        await transport.ActivateRoutingAsync(TestContext.CancellationToken);
        await serverTask;
    }

    [TestMethod]
    public async Task RoutingActivation_Rejection_Throws()
    {
        var (clientStream, serverStream) = DuplexStreamPair.Create();
        await using var transport = new AsyncDoIpStreamTransport(clientStream, TestOptions(), leaveOpen: false);
        await using var server = serverStream;

        var serverTask = Task.Run(async () =>
        {
            await DoIpStreamCodec.ReadMessageAsync(server, 1024, TestContext.CancellationToken);
            await DoIpStreamCodec.WriteMessageAsync(
                server,
                RoutingActivationResponse(DoIpActivationResponseCode.UnknownSourceAddress),
                1024,
                TestContext.CancellationToken);
        }, TestContext.CancellationToken);

        await Assert.ThrowsExactlyAsync<ProtocolException>(() => transport.ActivateRoutingAsync(TestContext.CancellationToken));
        await serverTask;
    }

    [TestMethod]
    public async Task RoutingActivation_WrongTesterAddress_Throws()
    {
        var (clientStream, serverStream) = DuplexStreamPair.Create();
        await using var transport = new AsyncDoIpStreamTransport(clientStream, TestOptions(), leaveOpen: false);
        await using var server = serverStream;

        var serverTask = Task.Run(async () =>
        {
            await DoIpStreamCodec.ReadMessageAsync(server, 1024, TestContext.CancellationToken);
            await DoIpStreamCodec.WriteMessageAsync(
                server,
                RoutingActivationResponse(testerAddress: 0x0E01),
                1024,
                TestContext.CancellationToken);
        }, TestContext.CancellationToken);

        await Assert.ThrowsExactlyAsync<ProtocolException>(() => transport.ActivateRoutingAsync(TestContext.CancellationToken));
        await serverTask;
    }

    [TestMethod]
    public async Task RoutingActivation_WrongEntityAddress_Throws()
    {
        var (clientStream, serverStream) = DuplexStreamPair.Create();
        await using var transport = new AsyncDoIpStreamTransport(clientStream, TestOptions(), leaveOpen: false);
        await using var server = serverStream;

        var serverTask = Task.Run(async () =>
        {
            await DoIpStreamCodec.ReadMessageAsync(server, 1024, TestContext.CancellationToken);
            await DoIpStreamCodec.WriteMessageAsync(
                server,
                RoutingActivationResponse(entityAddress: 0x0A01),
                1024,
                TestContext.CancellationToken);
        }, TestContext.CancellationToken);

        await Assert.ThrowsExactlyAsync<ProtocolException>(() => transport.ActivateRoutingAsync(TestContext.CancellationToken));
        await serverTask;
    }

    [TestMethod]
    public async Task RoutingActivation_Timeout_Throws()
    {
        var (clientStream, serverStream) = DuplexStreamPair.Create();
        await using var transport = new AsyncDoIpStreamTransport(clientStream, TestOptions(routingActivationTimeout: TimeSpan.FromMilliseconds(50)), leaveOpen: false);
        await using var server = serverStream;

        var serverTask = Task.Run(
            () => DoIpStreamCodec.ReadMessageAsync(server, 1024, TestContext.CancellationToken),
            TestContext.CancellationToken);

        await Assert.ThrowsExactlyAsync<ProtocolException>(() => transport.ActivateRoutingAsync(TestContext.CancellationToken));
        await serverTask;
    }

    [TestMethod]
    public async Task SendAsync_DiagnosticPositiveAck_Returns()
    {
        var (clientStream, serverStream) = DuplexStreamPair.Create();
        await using var transport = new AsyncDoIpStreamTransport(clientStream, TestOptions(autoActivate: false), leaveOpen: false);
        await using var server = serverStream;

        var serverTask = Task.Run(async () =>
        {
            var request = await DoIpStreamCodec.ReadMessageAsync(server, 1024, TestContext.CancellationToken);
            Assert.AreEqual(DoIpPayloadType.DiagnosticMessage, request.PayloadType);
            CollectionAssert.AreEqual(new byte[] { 0x22, 0xF1, 0x90 }, request.Payload[4..].ToArray());

            await DoIpStreamCodec.WriteMessageAsync(server, DiagnosticAck(), 1024, TestContext.CancellationToken);
        }, TestContext.CancellationToken);

        await transport.SendAsync(new byte[] { 0x22, 0xF1, 0x90 }, TestContext.CancellationToken);
        await serverTask;
    }

    [TestMethod]
    public async Task SendAsync_DiagnosticAckWrongCode_Throws()
    {
        var (clientStream, serverStream) = DuplexStreamPair.Create();
        await using var transport = new AsyncDoIpStreamTransport(clientStream, TestOptions(autoActivate: false), leaveOpen: false);
        await using var server = serverStream;

        var serverTask = Task.Run(async () =>
        {
            await DoIpStreamCodec.ReadMessageAsync(server, 1024, TestContext.CancellationToken);
            await DoIpStreamCodec.WriteMessageAsync(server, DiagnosticAck(code: 0x01), 1024, TestContext.CancellationToken);
        }, TestContext.CancellationToken);

        await Assert.ThrowsExactlyAsync<ProtocolException>(() => transport.SendAsync(new byte[] { 0x22 }, TestContext.CancellationToken));
        await serverTask;
    }

    [TestMethod]
    public async Task SendAsync_DiagnosticAckWrongAddress_Throws()
    {
        var (clientStream, serverStream) = DuplexStreamPair.Create();
        await using var transport = new AsyncDoIpStreamTransport(clientStream, TestOptions(autoActivate: false), leaveOpen: false);
        await using var server = serverStream;

        var serverTask = Task.Run(async () =>
        {
            await DoIpStreamCodec.ReadMessageAsync(server, 1024, TestContext.CancellationToken);
            await DoIpStreamCodec.WriteMessageAsync(server, DiagnosticAck(sourceAddress: 0x1002), 1024, TestContext.CancellationToken);
        }, TestContext.CancellationToken);

        await Assert.ThrowsExactlyAsync<ProtocolException>(() => transport.SendAsync(new byte[] { 0x22 }, TestContext.CancellationToken));
        await serverTask;
    }

    [TestMethod]
    public async Task SendAsync_DiagnosticNack_Throws()
    {
        var (clientStream, serverStream) = DuplexStreamPair.Create();
        await using var transport = new AsyncDoIpStreamTransport(clientStream, TestOptions(autoActivate: false), leaveOpen: false);
        await using var server = serverStream;

        var serverTask = Task.Run(async () =>
        {
            await DoIpStreamCodec.ReadMessageAsync(server, 1024, TestContext.CancellationToken);
            await DoIpStreamCodec.WriteMessageAsync(server, DiagnosticNack(DoIpDiagnosticNackCode.TargetUnreachable), 1024, TestContext.CancellationToken);
        }, TestContext.CancellationToken);

        await Assert.ThrowsExactlyAsync<ProtocolException>(() => transport.SendAsync(new byte[] { 0x22 }, TestContext.CancellationToken));
        await serverTask;
    }

    [TestMethod]
    public async Task SendAsync_DiagnosticAckTimeout_Throws()
    {
        var (clientStream, serverStream) = DuplexStreamPair.Create();
        await using var transport = new AsyncDoIpStreamTransport(
            clientStream,
            TestOptions(autoActivate: false, diagnosticAckTimeout: TimeSpan.FromMilliseconds(50)),
            leaveOpen: false);
        await using var server = serverStream;

        var serverTask = Task.Run(
            () => DoIpStreamCodec.ReadMessageAsync(server, 1024, TestContext.CancellationToken),
            TestContext.CancellationToken);

        await Assert.ThrowsExactlyAsync<ProtocolException>(() =>
            transport.SendAsync(new byte[] { 0x22, 0xF1, 0x90 }, TestContext.CancellationToken));
        await serverTask;
    }

    [TestMethod]
    public async Task ReceiveAsync_GenericHeaderNack_Throws()
    {
        var (clientStream, serverStream) = DuplexStreamPair.Create();
        await using var transport = new AsyncDoIpStreamTransport(clientStream, TestOptions(autoActivate: false), leaveOpen: false);
        await using var server = serverStream;

        var receiveTask = transport.ReceiveAsync(TestContext.CancellationToken);
        await DoIpStreamCodec.WriteMessageAsync(
            server,
            new DoIpMessage(DoIpPayloadType.GenericHeaderNegativeAcknowledge, new byte[] { (byte)DoIpGenericHeaderNackCode.UnknownPayloadType }),
            1024,
            TestContext.CancellationToken);

        await Assert.ThrowsExactlyAsync<ProtocolException>(() => receiveTask);
    }

    [TestMethod]
    public async Task ReceiveAsync_DiagnosticResponseWrongAddress_Throws()
    {
        var (clientStream, serverStream) = DuplexStreamPair.Create();
        await using var transport = new AsyncDoIpStreamTransport(clientStream, TestOptions(autoActivate: false), leaveOpen: false);
        await using var server = serverStream;

        var receiveTask = transport.ReceiveAsync(TestContext.CancellationToken);
        await DoIpStreamCodec.WriteMessageAsync(server, DiagnosticMessage([0x62, 0xF1, 0x90], sourceAddress: 0x1002), 1024, TestContext.CancellationToken);

        await Assert.ThrowsExactlyAsync<ProtocolException>(() => receiveTask);
    }

    [TestMethod]
    public async Task ReceiveAsync_DiagnosticResponseTimeout_Throws()
    {
        var (clientStream, serverStream) = DuplexStreamPair.Create();
        await using var transport = new AsyncDoIpStreamTransport(
            clientStream,
            TestOptions(autoActivate: false, diagnosticResponseTimeout: TimeSpan.FromMilliseconds(50)),
            leaveOpen: false);
        await using var server = serverStream;

        await Assert.ThrowsExactlyAsync<ProtocolException>(() => transport.ReceiveAsync(TestContext.CancellationToken));
    }

    [TestMethod]
    public async Task ReceiveAsync_InboxLimitExceeded_Throws()
    {
        var (clientStream, serverStream) = DuplexStreamPair.Create();
        await using var transport = new AsyncDoIpStreamTransport(clientStream, TestOptions(autoActivate: false, inboxLimit: 1), leaveOpen: false);
        await using var server = serverStream;

        var receiveTask = transport.ReceiveAsync(TestContext.CancellationToken);
        await DoIpStreamCodec.WriteMessageAsync(server, new DoIpMessage(DoIpPayloadType.DoIpEntityStatusResponse, new byte[] { 0x00 }), 1024, TestContext.CancellationToken);
        await DoIpStreamCodec.WriteMessageAsync(server, new DoIpMessage(DoIpPayloadType.DiagnosticPowerModeInformationResponse, new byte[] { 0x00 }), 1024, TestContext.CancellationToken);

        await Assert.ThrowsExactlyAsync<ProtocolException>(() => receiveTask);
    }

    [TestMethod]
    public async Task ReceiveAsync_AliveCheckRequest_AutoRespondsAndContinues()
    {
        var (clientStream, serverStream) = DuplexStreamPair.Create();
        await using var transport = new AsyncDoIpStreamTransport(clientStream, TestOptions(autoActivate: false), leaveOpen: false);
        await using var server = serverStream;

        var receiveTask = transport.ReceiveAsync(TestContext.CancellationToken);
        await DoIpStreamCodec.WriteMessageAsync(server, new DoIpMessage(DoIpPayloadType.AliveCheckRequest, ReadOnlyMemory<byte>.Empty), 1024, TestContext.CancellationToken);

        var aliveResponse = await DoIpStreamCodec.ReadMessageAsync(server, 1024, TestContext.CancellationToken);
        Assert.AreEqual(DoIpPayloadType.AliveCheckResponse, aliveResponse.PayloadType);
        Assert.AreEqual(0x0E00, BinaryPrimitives.ReadUInt16BigEndian(aliveResponse.Payload.Span));

        await DoIpStreamCodec.WriteMessageAsync(server, DiagnosticMessage([0x62, 0xF1, 0x90, 0x12]), 1024, TestContext.CancellationToken);

        var response = await receiveTask;
        CollectionAssert.AreEqual(new byte[] { 0x62, 0xF1, 0x90, 0x12 }, response.ToArray());
    }

    [TestMethod]
    public async Task SendAsync_DiagnosticArrivesBeforeAck_IsCachedForReceive()
    {
        var (clientStream, serverStream) = DuplexStreamPair.Create();
        await using var transport = new AsyncDoIpStreamTransport(clientStream, TestOptions(autoActivate: false), leaveOpen: false);
        await using var server = serverStream;

        var serverTask = Task.Run(async () =>
        {
            await DoIpStreamCodec.ReadMessageAsync(server, 1024, TestContext.CancellationToken);
            await DoIpStreamCodec.WriteMessageAsync(server, DiagnosticMessage([0x62, 0xF1, 0x90, 0x55]), 1024, TestContext.CancellationToken);
            await DoIpStreamCodec.WriteMessageAsync(server, DiagnosticAck(), 1024, TestContext.CancellationToken);
        }, TestContext.CancellationToken);

        await transport.SendAsync(new byte[] { 0x22, 0xF1, 0x90 }, TestContext.CancellationToken);
        var response = await transport.ReceiveAsync(TestContext.CancellationToken);

        CollectionAssert.AreEqual(new byte[] { 0x62, 0xF1, 0x90, 0x55 }, response.ToArray());
        await serverTask;
    }

    [TestMethod]
    public async Task Constructor_AcceptsSslStream()
    {
        using var inner = new MemoryStream();
        using var ssl = new SslStream(inner, leaveInnerStreamOpen: true);

        await using var transport = new AsyncDoIpStreamTransport(ssl, TestOptions(autoActivate: false), leaveOpen: true);

        Assert.AreEqual(0x0E00, transport.Options.SourceAddress);
    }

    [TestMethod]
    public async Task DisposeAsync_LeaveOpenFalse_DisposesStream()
    {
        using var stream = new MemoryStream();
        var transport = new AsyncDoIpStreamTransport(stream, TestOptions(autoActivate: false), leaveOpen: false);

        await transport.DisposeAsync();

        Assert.ThrowsExactly<ObjectDisposedException>(() => stream.WriteByte(0x01));
    }

    [TestMethod]
    public async Task DisposeAsync_LeaveOpenTrue_KeepsStreamOpen()
    {
        using var stream = new MemoryStream();
        var transport = new AsyncDoIpStreamTransport(stream, TestOptions(autoActivate: false), leaveOpen: true);

        await transport.DisposeAsync();
        stream.WriteByte(0x01);

        Assert.AreEqual(1, stream.Length);
    }

    [TestMethod]
    public async Task DisposeAsync_CancelsInFlightReceiveBeforeDisposingGates()
    {
        var stream = new BlockingReadStream();
        var transport = new AsyncDoIpStreamTransport(stream, TestOptions(autoActivate: false), leaveOpen: false);
        var receiveTask = transport.ReceiveAsync(CancellationToken.None);

        await stream.ReadStarted.Task.WaitAsync(TestContext.CancellationToken);
        await transport.DisposeAsync();

        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(async () => await receiveTask);
        Assert.IsTrue(stream.Disposed);
    }

    private static DoIpOptions TestOptions(
        bool autoActivate = true,
        int inboxLimit = 16,
        TimeSpan? routingActivationTimeout = null,
        TimeSpan? diagnosticAckTimeout = null,
        TimeSpan? diagnosticResponseTimeout = null) => new()
    {
        SourceAddress = 0x0E00,
        TargetAddress = 0x1001,
        EntityAddress = 0x0A00,
        InboxLimit = inboxLimit,
        AutoActivate = autoActivate,
        WaitForDiagnosticAck = true,
        TcpGeneralTimeout = TimeSpan.FromSeconds(2),
        RoutingActivationTimeout = routingActivationTimeout ?? TimeSpan.FromSeconds(2),
        DiagnosticAckTimeout = diagnosticAckTimeout ?? TimeSpan.FromSeconds(2),
        DiagnosticResponseTimeout = diagnosticResponseTimeout ?? TimeSpan.FromSeconds(2),
    };

    private static DoIpMessage RoutingActivationResponse(
        DoIpActivationResponseCode code = DoIpActivationResponseCode.Success,
        ushort testerAddress = 0x0E00,
        ushort entityAddress = 0x0A00)
    {
        var payload = new byte[9];
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(0, 2), testerAddress);
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(2, 2), entityAddress);
        payload[4] = (byte)code;
        return new DoIpMessage(DoIpPayloadType.RoutingActivationResponse, payload);
    }

    private static DoIpMessage DiagnosticMessage(
        ReadOnlySpan<byte> uds,
        ushort sourceAddress = 0x1001,
        ushort targetAddress = 0x0E00)
    {
        var payload = new byte[4 + uds.Length];
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(0, 2), sourceAddress);
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(2, 2), targetAddress);
        uds.CopyTo(payload.AsSpan(4));
        return new DoIpMessage(DoIpPayloadType.DiagnosticMessage, payload);
    }

    private static DoIpMessage DiagnosticAck(byte code = 0x00, ushort sourceAddress = 0x1001, ushort targetAddress = 0x0E00)
    {
        var payload = new byte[5];
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(0, 2), sourceAddress);
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(2, 2), targetAddress);
        payload[4] = code;
        return new DoIpMessage(DoIpPayloadType.DiagnosticMessagePositiveAck, payload);
    }

    private static DoIpMessage DiagnosticNack(DoIpDiagnosticNackCode code)
    {
        var payload = new byte[5];
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(0, 2), 0x1001);
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(2, 2), 0x0E00);
        payload[4] = (byte)code;
        return new DoIpMessage(DoIpPayloadType.DiagnosticMessageNegativeAck, payload);
    }

    public TestContext TestContext { get; set; }

    private sealed class BlockingReadStream : Stream
    {
        public TaskCompletionSource ReadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool Disposed { get; private set; }

        public override bool CanRead => !Disposed;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            ReadStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            return 0;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }

        public override ValueTask DisposeAsync()
        {
            Disposed = true;
            GC.SuppressFinalize(this);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FragmentedReadStream : Stream
    {
        private readonly byte[] _data;
        private readonly int _maxChunkSize;
        private int _position;

        public FragmentedReadStream(byte[] data, int maxChunkSize)
        {
            _data = data;
            _maxChunkSize = maxChunkSize;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_position >= _data.Length)
                return ValueTask.FromResult(0);

            var count = Math.Min(buffer.Length, Math.Min(_maxChunkSize, _data.Length - _position));
            _data.AsMemory(_position, count).CopyTo(buffer);
            _position += count;
            return ValueTask.FromResult(count);
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class DuplexStreamPair
    {
        public static (Stream Client, Stream Server) Create()
        {
            var clientToServer = Channel.CreateUnbounded<byte>();
            var serverToClient = Channel.CreateUnbounded<byte>();
            return (
                new ChannelDuplexStream(serverToClient.Reader, clientToServer.Writer),
                new ChannelDuplexStream(clientToServer.Reader, serverToClient.Writer));
        }

        private sealed class ChannelDuplexStream : Stream
        {
            private readonly ChannelReader<byte> _reader;
            private readonly ChannelWriter<byte> _writer;
            private bool _disposed;

            public ChannelDuplexStream(ChannelReader<byte> reader, ChannelWriter<byte> writer)
            {
                _reader = reader;
                _writer = writer;
            }

            public override bool CanRead => !_disposed;
            public override bool CanSeek => false;
            public override bool CanWrite => !_disposed;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

            public override void Flush() { }
            public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

            public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            {
                if (buffer.IsEmpty) return 0;

                byte first;
                try
                {
                    first = await _reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (ChannelClosedException)
                {
                    return 0;
                }

                buffer.Span[0] = first;
                var count = 1;
                while (count < buffer.Length && _reader.TryRead(out var next))
                    buffer.Span[count++] = next;
                return count;
            }

            public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
            {
                foreach (var value in buffer.ToArray())
                    await _writer.WriteAsync(value, cancellationToken).ConfigureAwait(false);
            }

            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

            protected override void Dispose(bool disposing)
            {
                if (disposing && !_disposed)
                    _writer.TryComplete();
                _disposed = true;
                base.Dispose(disposing);
            }

            public override ValueTask DisposeAsync()
            {
                Dispose(disposing: true);
                GC.SuppressFinalize(this);
                return ValueTask.CompletedTask;
            }
        }
    }
}
