using System;
using System.Collections.Generic;
using System.Linq;

using DiagKit.Uds.DoCan;

namespace DiagKit.Uds.Tests;

[TestClass]
public class DoCanFramingTests
{
    [TestMethod]
    public void SingleFrame_ShortForm_RoundTrips()
    {
        var payload = new byte[] { 0x10, 0x03 };
        var frame = DoCanFraming.EncodeSingleFrame(payload, 8);
        Assert.HasCount(8, frame);
        Assert.AreEqual(0x02, frame[0]); // SF, length=2

        Assert.IsTrue(DoCanFraming.TryReadSingleFrame(frame, out var decoded));
        CollectionAssert.AreEqual(payload, decoded.ToArray());
    }

    [TestMethod]
    public void SingleFrame_LongForm_RoundTrips()
    {
        var payload = Enumerable.Range(0, 30).Select(i => (byte)i).ToArray();
        var frame = DoCanFraming.EncodeSingleFrame(payload, 32);
        Assert.HasCount(32, frame);
        Assert.AreEqual(0x00, frame[0]);
        Assert.AreEqual(30, frame[1]);

        Assert.IsTrue(DoCanFraming.TryReadSingleFrame(frame, out var decoded));
        CollectionAssert.AreEqual(payload, decoded.ToArray());
    }

    [TestMethod]
    [DataRow(8)]
    [DataRow(20)]
    [DataRow(30)]
    [DataRow(62)]
    public void SingleFrame_CanFdPayloadLengths_RoundTrip(int payloadLength)
    {
        var payload = Enumerable.Range(0, payloadLength).Select(i => (byte)i).ToArray();
        var frameLength = DoCanFraming.GetSuitableSfOrFfLength(payloadLength, 8, 15, out var requiresMoreFrames);
        Assert.IsFalse(requiresMoreFrames);

        var frame = DoCanFraming.EncodeSingleFrame(payload, frameLength);

        Assert.IsGreaterThan(8, frame.Length);
        Assert.AreEqual(0x00, frame[0]);
        Assert.AreEqual(payloadLength, frame[1]);
        Assert.IsTrue(DoCanFraming.TryReadSingleFrame(frame, out var decoded));
        CollectionAssert.AreEqual(payload, decoded.ToArray());
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(5)]
    [DataRow(7)]
    public void SingleFrame_ShortPayloadWithFdMinDlc_UsesCanDl8(int payloadLength)
    {
        var payload = Enumerable.Range(0, payloadLength).Select(i => (byte)i).ToArray();
        var frameLength = DoCanFraming.GetSuitableSfOrFfLength(payloadLength, 9, 15, out var requiresMoreFrames);
        Assert.IsFalse(requiresMoreFrames);
        Assert.AreEqual(8, frameLength);

        var frame = DoCanFraming.EncodeSingleFrame(payload, frameLength);

        Assert.HasCount(8, frame);
        Assert.AreEqual(payloadLength, frame[0]);
        Assert.IsTrue(DoCanFraming.TryReadSingleFrame(frame, out var decoded));
        CollectionAssert.AreEqual(payload, decoded.ToArray());
    }

    [TestMethod]
    public void GetSuitableSfOrFfLength_MinDlcGreaterThanMaxDlc_Throws()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            DoCanFraming.GetSuitableSfOrFfLength(12, 15, 8, out _));
    }

    [TestMethod]
    public void SingleFrame_ShortPayloadWithCanFdDlc_IsRejected()
    {
        var payload = new byte[] { 0x01, 0x02, 0x03 };
        Assert.ThrowsExactly<ArgumentException>(() => DoCanFraming.EncodeSingleFrame(payload, 12));
    }

    [TestMethod]
    public void SingleFrame_CanFdInvalidSfDl_IsRejected()
    {
        var classicZero = Enumerable.Repeat((byte)0xCC, 8).ToArray();
        classicZero[0] = 0x00;
        Assert.IsFalse(DoCanFraming.TryReadSingleFrame(classicZero, out _));

        var tooSmall = Enumerable.Repeat((byte)0xCC, 12).ToArray();
        tooSmall[0] = 0x00;
        tooSmall[1] = 0x07;
        Assert.IsFalse(DoCanFraming.TryReadSingleFrame(tooSmall, out _));

        var tooLarge = Enumerable.Repeat((byte)0xCC, 12).ToArray();
        tooLarge[0] = 0x00;
        tooLarge[1] = 0x0B;
        Assert.IsFalse(DoCanFraming.TryReadSingleFrame(tooLarge, out _));

        var tooLargeForFd64 = Enumerable.Repeat((byte)0xCC, 64).ToArray();
        tooLargeForFd64[0] = 0x00;
        tooLargeForFd64[1] = 0x3F;
        Assert.IsFalse(DoCanFraming.TryReadSingleFrame(tooLargeForFd64, out _));
    }

    [TestMethod]
    public void EmptyAndTruncatedInputs_AreRejected()
    {
        Assert.AreEqual(DoCanFrameType.Reserved, DoCanFraming.GetFrameType([]));
        Assert.IsFalse(DoCanFraming.TryReadSingleFrame([], out _));
        Assert.IsFalse(DoCanFraming.TryReadFirstFrame([], out _, out _));
        Assert.IsFalse(DoCanFraming.TryReadConsecutiveFrame([0x21], out _, out _));
        Assert.IsFalse(DoCanFraming.TryReadFlowControlFrame([0x30, 0x00], out _, out _, out _));
    }

    [TestMethod]
    public void EncodeSpanOverloads_RejectDestinationsShorterThanDlc()
    {
        var payload = Enumerable.Range(0, 20).Select(i => (byte)i).ToArray();

        Assert.ThrowsExactly<ArgumentException>(() =>
            DoCanFraming.EncodeSingleFrame(payload.AsSpan(0, 8), new byte[11], 12));
        Assert.ThrowsExactly<ArgumentException>(() =>
            DoCanFraming.EncodeFirstFrame(payload, new byte[7], 8, out _));
        Assert.ThrowsExactly<ArgumentException>(() =>
            DoCanFraming.EncodeConsecutiveFrame(payload.AsSpan(0, 4), new byte[7], 1, 8));
        Assert.ThrowsExactly<ArgumentException>(() =>
            DoCanFraming.EncodeFlowControlFrame(new byte[7], DoCanFlowStatus.Continue, 0, 0, 8));
    }

    [TestMethod]
    public void FirstFrame_Short_RoundTrips()
    {
        var payload = Enumerable.Range(0, 50).Select(i => (byte)i).ToArray();
        var ff = DoCanFraming.EncodeFirstFrame(payload, 8, out var consumed);
        Assert.AreEqual(6, consumed);
        Assert.AreEqual(0x10, ff[0]);
        Assert.AreEqual(50, ff[1]);

        Assert.IsTrue(DoCanFraming.TryReadFirstFrame(ff, out var len, out var head));
        Assert.AreEqual(50u, len);
        Assert.AreEqual(6, head.Length);
    }

    [TestMethod]
    public void FirstFrame_Long_RoundTrips()
    {
        var payload = new byte[5000];
        new Random(1).NextBytes(payload);
        var ff = DoCanFraming.EncodeFirstFrame(payload, 64, out var consumed);
        Assert.AreEqual(58, consumed);
        Assert.AreEqual(0x10, ff[0]);
        Assert.AreEqual(0x00, ff[1]);

        Assert.IsTrue(DoCanFraming.TryReadFirstFrame(ff, out var len, out var head));
        Assert.AreEqual(5000u, len);
        Assert.AreEqual(58, head.Length);
    }

    [TestMethod]
    [DataRow(7, false, 8)]
    [DataRow(8, false, 12)]
    [DataRow(62, false, 64)]
    [DataRow(63, true, 64)]
    [DataRow(4095, true, 64)]
    [DataRow(4096, true, 64)]
    public void SfOrFfLength_Boundaries(int payloadLength, bool expectedMoreFrames, int expectedLength)
    {
        var frameLength = DoCanFraming.GetSuitableSfOrFfLength(payloadLength, 8, 15, out var moreFrames);

        Assert.AreEqual(expectedMoreFrames, moreFrames);
        Assert.AreEqual(expectedLength, frameLength);
    }

    [TestMethod]
    public void ConsecutiveFrame_RoundTrips()
    {
        var payload = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
        var cf = DoCanFraming.EncodeConsecutiveFrame(payload, 5, 8);
        Assert.AreEqual(0x25, cf[0]);

        Assert.IsTrue(DoCanFraming.TryReadConsecutiveFrame(cf, out var sn, out var data));
        Assert.AreEqual(5, sn);
        CollectionAssert.AreEqual(payload, data[..4].ToArray());
    }

    [TestMethod]
    public void NextSequenceNumber_Wraps()
    {
        Assert.AreEqual(1, DoCanFraming.NextSequenceNumber(0));
        Assert.AreEqual(15, DoCanFraming.NextSequenceNumber(14));
        Assert.AreEqual(0, DoCanFraming.NextSequenceNumber(15));
    }

    [TestMethod]
    public void FlowControlFrame_RoundTrips()
    {
        var fc = DoCanFraming.EncodeFlowControlFrame(DoCanFlowStatus.Continue, 8, 0x14);
        Assert.AreEqual(0x30, fc[0]);
        Assert.AreEqual(8, fc[1]);
        Assert.AreEqual(0x14, fc[2]);

        Assert.IsTrue(DoCanFraming.TryReadFlowControlFrame(fc, out var status, out var bs, out var st));
        Assert.AreEqual(DoCanFlowStatus.Continue, status);
        Assert.AreEqual(8, bs);
        Assert.AreEqual(0x14, st);
    }

    [TestMethod]
    public void STmin_MillisecondEncoding()
    {
        Assert.AreEqual(TimeSpan.FromMilliseconds(20), DoCanFraming.STminToTimeSpan(0x14));
        Assert.AreEqual(0x14, DoCanFraming.TimeSpanToSTmin(TimeSpan.FromMilliseconds(20)));
    }

    [TestMethod]
    public void STmin_MicrosecondEncoding()
    {
        Assert.AreEqual(TimeSpan.FromMicroseconds(500), DoCanFraming.STminToTimeSpan(0xF5));
        Assert.AreEqual(0xF5, DoCanFraming.TimeSpanToSTmin(TimeSpan.FromMicroseconds(500)));
    }

    [TestMethod]
    [DataRow(0x80)]
    [DataRow(0xF0)]
    [DataRow(0xFA)]
    [DataRow(0xFF)]
    public void STmin_ReservedValues_MapToMaximumDelay(int reserved)
    {
        Assert.AreEqual(TimeSpan.FromMilliseconds(127), DoCanFraming.STminToTimeSpan((byte)reserved));
    }

    [TestMethod]
    public void Segment_SmallMessageProducesSingleFrame()
    {
        var data = new byte[] { 0x22, 0xF1, 0x90 };
        var frames = DoCanFraming.Segment(data, 8, 8).ToList();
        Assert.HasCount(1, frames);
        Assert.AreEqual(DoCanFrameType.SingleFrame, DoCanFraming.GetFrameType(frames[0]));
    }

    [TestMethod]
    public void Segment_LargeMessageProducesFirstAndConsecutiveFrames()
    {
        var data = new byte[100];
        new Random(2).NextBytes(data);
        var frames = DoCanFraming.Segment(data, 8, 8).ToList();
        Assert.IsGreaterThanOrEqualTo(2, frames.Count);
        Assert.AreEqual(DoCanFrameType.FirstFrame, DoCanFraming.GetFrameType(frames[0]));
        for (int i = 1; i < frames.Count; i++)
            Assert.AreEqual(DoCanFrameType.ConsecutiveFrame, DoCanFraming.GetFrameType(frames[i]));

        // Reconstitute payload from frames.
        Assert.IsTrue(DoCanFraming.TryReadFirstFrame(frames[0], out var totalLen, out var head));
        Assert.AreEqual(100u, totalLen);
        var rebuild = new List<byte>();
        rebuild.AddRange(head.ToArray());
        for (int i = 1; i < frames.Count; i++)
        {
            Assert.IsTrue(DoCanFraming.TryReadConsecutiveFrame(frames[i], out var sn, out var cf));
            Assert.AreEqual(i & 0x0F, sn);
            int take = Math.Min(cf.Length, 100 - rebuild.Count);
            rebuild.AddRange(cf[..take].ToArray());
        }
        CollectionAssert.AreEqual(data, rebuild);
    }

    [TestMethod]
    public void Segment_FixedDlc_UsesMaxDlcForAllSegmentedFrames()
    {
        var data = new byte[100];

        var frames = DoCanFraming.Segment(data, 8, 15, fixedDlc: true).ToList();

        Assert.IsGreaterThanOrEqualTo(2, frames.Count);
        Assert.IsTrue(frames.All(frame => frame.Length == 64));
    }

    [TestMethod]
    public void Segment_NonFixedDlc_AllowsLastConsecutiveFrameToShrink()
    {
        var data = new byte[100];

        var frames = DoCanFraming.Segment(data, 8, 15, fixedDlc: false).ToList();

        Assert.HasCount(64, frames[0]);
        Assert.IsLessThan(64, frames[^1].Length);
    }

    [TestMethod]
    public void GetFrameType_RecognisesAllFour()
    {
        Assert.AreEqual(DoCanFrameType.SingleFrame, DoCanFraming.GetFrameType([0x07]));
        Assert.AreEqual(DoCanFrameType.FirstFrame, DoCanFraming.GetFrameType([0x10, 0x14]));
        Assert.AreEqual(DoCanFrameType.ConsecutiveFrame, DoCanFraming.GetFrameType([0x21]));
        Assert.AreEqual(DoCanFrameType.FlowControlFrame, DoCanFraming.GetFrameType([0x30]));
    }
}
