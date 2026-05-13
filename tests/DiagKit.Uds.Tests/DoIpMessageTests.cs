using System;

using DiagKit.Uds.DoIp;
using DiagKit.Uds.Exceptions;

namespace DiagKit.Uds.Tests;

[TestClass]
public class DoIpMessageTests
{
    [TestMethod]
    public void EncodeDecode_RoundTrips()
    {
        var payload = new byte[] { 0x0E, 0x00, 0x10, 0x01, 0x22, 0xF1, 0x90 };
        var msg = new DoIpMessage(DoIpPayloadType.DiagnosticMessage, payload);
        var bytes = msg.ToBytes();
        Assert.HasCount(8 + payload.Length, bytes);
        Assert.AreEqual(0x02, bytes[0]);
        Assert.AreEqual(0xFD, bytes[1]);
        Assert.AreEqual(0x80, bytes[2]);
        Assert.AreEqual(0x01, bytes[3]);

        var parsed = DoIpMessage.Parse(bytes);
        Assert.AreEqual(DoIpPayloadType.DiagnosticMessage, parsed.PayloadType);
        CollectionAssert.AreEqual(payload, parsed.Payload.ToArray());
    }

    [TestMethod]
    public void Parse_BadVersion_Throws()
    {
        var bytes = new byte[] { 0x01, 0xFE, 0x80, 0x01, 0, 0, 0, 0 };
        Assert.ThrowsExactly<FrameFormatException>(() => DoIpMessage.Parse(bytes));
    }

    [TestMethod]
    public void Parse_TruncatedPayload_Throws()
    {
        var bytes = new byte[] { 0x02, 0xFD, 0x80, 0x01, 0, 0, 0, 10, 1, 2 };
        Assert.ThrowsExactly<FrameFormatException>(() => DoIpMessage.Parse(bytes));
    }

    [TestMethod]
    public void Parse_PayloadLengthOverDefaultLimit_Throws()
    {
        var bytes = new byte[] { 0x02, 0xFD, 0x80, 0x01, 0, 0x40, 0x00, 0x01 };

        Assert.ThrowsExactly<ProtocolException>(() => DoIpMessage.Parse(bytes));
    }

    [TestMethod]
    public void Parse_PayloadLengthOverExplicitLimit_Throws()
    {
        var bytes = new byte[] { 0x02, 0xFD, 0x80, 0x01, 0, 0, 0, 5, 1, 2, 3, 4, 5 };

        Assert.ThrowsExactly<ProtocolException>(() => DoIpMessage.Parse(bytes, maxPayloadLength: 4));
    }

    [TestMethod]
    public void Parse_MaxPayloadLengthMustBePositive()
    {
        var bytes = new byte[] { 0x02, 0xFD, 0x80, 0x01, 0, 0, 0, 0 };

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => DoIpMessage.Parse(bytes, maxPayloadLength: 0));
    }

    [TestMethod]
    public void TryParseHeader_ReturnsLength()
    {
        var bytes = new byte[] { 0x02, 0xFD, 0x80, 0x01, 0x00, 0x00, 0x00, 0x05 };
        Assert.IsTrue(DoIpMessage.TryParseHeader(bytes, out var type, out var len));
        Assert.AreEqual(DoIpPayloadType.DiagnosticMessage, type);
        Assert.AreEqual(5u, len);
    }

    [TestMethod]
    public void EncodeDiagnosticMessage_DestinationTooSmall_ThrowsArgumentException()
    {
        var destination = new byte[DoIpMessage.HeaderSize + 3];

        Assert.ThrowsExactly<ArgumentException>(
            () => DoIpMessage.EncodeDiagnosticMessage(destination, [0x22], 0x0E00, 0x1234));
    }
}
