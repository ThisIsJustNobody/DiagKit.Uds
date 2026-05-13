using DiagKit.Uds.DoCan;

namespace DiagKit.Uds.Tests;

[TestClass]
public class CanFrameTests
{
    [TestMethod]
    public void DlcLengthRoundTrip()
    {
        for (byte d = 0; d <= 15; d++)
        {
            var len = CanFrame.DlcToLength(d);
            var dlc = CanFrame.LengthToDlc(len);
            Assert.AreEqual(d, dlc);
        }
    }

    [TestMethod]
    public void LengthToDlc_PicksSmallestFit()
    {
        Assert.AreEqual(9, CanFrame.LengthToDlc(9));
        Assert.AreEqual(9, CanFrame.LengthToDlc(12));
        Assert.AreEqual(10, CanFrame.LengthToDlc(13));
        Assert.AreEqual(15, CanFrame.LengthToDlc(64));
    }

    [TestMethod]
    public void LengthToDlc_NegativeLength_Throws()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => CanFrame.LengthToDlc(-1));
    }

    [TestMethod]
    public void Create_SetsExpectedFields()
    {
        var data = new byte[] { 1, 2, 3 };
        var frame = CanFrame.Create(0x7E0, data, fd: true, brs: true);
        Assert.AreEqual(0x7E0u, frame.CanId);
        Assert.IsTrue(frame.FdFlag);
        Assert.IsTrue(frame.BitRateSwitch);
        Assert.IsFalse(frame.ExtendedId);
        Assert.AreEqual(3, frame.Data.Length);
    }

    [TestMethod]
    public void CreateCopy_TakesPayloadSnapshot()
    {
        var data = new byte[] { 1, 2, 3 };
        var frame = CanFrame.CreateCopy(0x7E0, data);

        data[0] = 0xFF;

        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, frame.Data.ToArray());
    }

    [TestMethod]
    public void Create_BitRateSwitchWithoutFd_Throws()
    {
        Assert.ThrowsExactly<ArgumentException>(() => CanFrame.Create(0x7E0, new byte[] { 1 }, brs: true));
    }

    [TestMethod]
    public void Create_InvalidStandardId_Throws()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => CanFrame.Create(0x800, ReadOnlyMemory<byte>.Empty));
    }
}
