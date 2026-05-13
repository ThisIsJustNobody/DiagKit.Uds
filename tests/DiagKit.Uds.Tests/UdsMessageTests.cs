using DiagKit.Uds.Exceptions;
using DiagKit.Uds.UdsLayer;

namespace DiagKit.Uds.Tests;

[TestClass]
public class UdsMessageTests
{
    [TestMethod]
    public void IsSuppressPositiveResponse_DetectsBit7OnSubFunctionedServices()
    {
        Assert.IsTrue(UdsMessage.IsSuppressPositiveResponse([0x10, 0x83]));
        Assert.IsTrue(UdsMessage.IsSuppressPositiveResponse([0x29, 0x81]));
        Assert.IsTrue(UdsMessage.IsSuppressPositiveResponse([0x2C, 0x81]));
        Assert.IsFalse(UdsMessage.IsSuppressPositiveResponse([0x10, 0x03]));
        // DID-based services (0x22) do not have a sub-function: high-bit of DID must not be misread.
        Assert.IsFalse(UdsMessage.IsSuppressPositiveResponse([0x22, 0xF1, 0x90]));
    }

    [TestMethod]
    [DataRow(0x14)]
    [DataRow(0x2F)]
    [DataRow(0x34)]
    [DataRow(0x35)]
    [DataRow(0x36)]
    [DataRow(0x37)]
    public void IsSuppressPositiveResponse_DoesNotTreatParameterBytesAsSubFunctions(int serviceId)
    {
        var sid = (byte)serviceId;
        Assert.IsFalse(UdsMessage.HasSubFunction(sid));
        Assert.IsFalse(UdsMessage.IsSuppressPositiveResponse([sid, 0x80]));
    }

    [TestMethod]
    [DataRow(0x2C)]
    [DataRow(0x31)]
    [DataRow(0x83)]
    [DataRow(0x85)]
    public void IsSuppressPositiveResponse_TreatsBoundaryServicesAsSubFunctioned(int serviceId)
    {
        var sid = (byte)serviceId;
        Assert.IsTrue(UdsMessage.HasSubFunction(sid));
        Assert.IsTrue(UdsMessage.IsSuppressPositiveResponse([sid, 0x80]));
        Assert.IsTrue(UdsMessage.TryGetSubFunction([sid, 0x80], out var subFunction));
        Assert.AreEqual(0x00, subFunction);
    }

    [TestMethod]
    public void GetServiceId_PreservesRawSid()
    {
        Assert.AreEqual(0xC3, UdsMessage.GetServiceId([0xC3]));
        Assert.AreEqual(0x83, UdsMessage.GetServiceId([0x83, 0x81]));
        Assert.AreEqual(0x10, UdsMessage.GetServiceId([0x10]));
    }

    [TestMethod]
    public void TryGetSubFunction_RemovesSuppressBitWithoutChangingSid()
    {
        Assert.IsTrue(UdsMessage.TryGetSubFunction([0x83, 0x81], out var subFunction));
        Assert.AreEqual(0x01, subFunction);
        Assert.AreEqual(0x83, UdsMessage.GetServiceId([0x83, 0x81]));

        Assert.IsTrue(UdsMessage.TryGetSubFunction([0x2C, 0x83], out var dynamicallyDefineSubFunction));
        Assert.AreEqual(0x03, dynamicallyDefineSubFunction);

        Assert.IsFalse(UdsMessage.TryGetSubFunction([0x22, 0xF1, 0x90], out _));
    }

    [TestMethod]
    public void IsPositiveResponseFor_Matches()
    {
        Assert.IsTrue(UdsMessage.IsPositiveResponseFor([0x22, 0xF1, 0x90], [0x62, 0xF1, 0x90]));
        Assert.IsTrue(UdsMessage.IsPositiveResponseFor([0x83, 0x01], [0xC3, 0x01]));
        Assert.IsFalse(UdsMessage.IsPositiveResponseFor([0x22], [0x59]));
    }

    [TestMethod]
    public void IsNegativeResponseFor_Matches()
    {
        Assert.IsTrue(UdsMessage.IsNegativeResponseFor([0x22], [0x7F, 0x22, 0x12]));
        Assert.IsTrue(UdsMessage.IsNegativeResponseFor([0x85, 0x02], [0x7F, 0x85, 0x22]));
        Assert.IsFalse(UdsMessage.IsNegativeResponseFor([0x10], [0x7F, 0x22, 0x12]));
    }

    [TestMethod]
    public void NegativeResponseException_ThrowIfNegative_Triggers()
    {
        Assert.ThrowsExactly<NegativeResponseException>(() =>
            NegativeResponseException.ThrowIfNegative([0x7F, 0x22, 0x31]));
    }

    [TestMethod]
    public void NegativeResponseException_ThrowIfNegative_PassesPositive()
    {
        NegativeResponseException.ThrowIfNegative([0x62, 0xF1, 0x90]);
    }
}
