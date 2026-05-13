using System;

using DiagKit.Uds.Services;

namespace DiagKit.Uds.Tests;

[TestClass]
public class CanoeSeedKeyGeneratorTests
{
    [TestMethod]
    public void GenerateKey_UsesGenerateKeyExByDefault()
    {
        var generateKeyExCalls = 0;
        var generateKeyExOptCalls = 0;
        CanoeSeedKeyGenerator.GenerateKeyExCallback generateKeyEx = (
            byte[] seed,
            uint seedSize,
            uint securityLevel,
            byte[] variant,
            byte[] key,
            uint keySize,
            out uint actualKeySize) =>
        {
            generateKeyExCalls++;
            Assert.AreEqual(2u, seedSize);
            Assert.AreEqual(0x01u, securityLevel);
            Assert.AreEqual(2u, keySize);
            CollectionAssert.AreEqual(new byte[] { 0x12, 0x34 }, seed);
            CollectionAssert.AreEqual(new byte[] { (byte)'A', 0x00 }, variant);

            key[0] = 0xAB;
            key[1] = 0xCD;
            actualKeySize = 2;
            return CanoeKeyGenerationResult.Ok;
        };
        CanoeSeedKeyGenerator.GenerateKeyExOptCallback generateKeyExOpt = (
            byte[] _,
            uint _,
            uint _,
            byte[] _,
            byte[] _,
            byte[] _,
            uint _,
            out uint actualKeySize) =>
        {
            generateKeyExOptCalls++;
            actualKeySize = 0;
            return CanoeKeyGenerationResult.UnspecifiedError;
        };
        using var generator = new CanoeSeedKeyGenerator(generateKeyEx, generateKeyExOpt);

        var key = generator.GenerateKey([0x12, 0x34], 0x01, new CanoeSeedKeyOptions
        {
            Variant = [(byte)'A'],
        });

        CollectionAssert.AreEqual(new byte[] { 0xAB, 0xCD }, key);
        Assert.AreEqual(1, generateKeyExCalls);
        Assert.AreEqual(0, generateKeyExOptCalls);
    }

    [TestMethod]
    public void GenerateKey_UsesGenerateKeyExOptWhenAlgorithmOptionsArePresent()
    {
        var generateKeyExCalls = 0;
        var generateKeyExOptCalls = 0;
        CanoeSeedKeyGenerator.GenerateKeyExCallback generateKeyEx = (
            byte[] _,
            uint _,
            uint _,
            byte[] _,
            byte[] _,
            uint _,
            out uint actualKeySize) =>
        {
            generateKeyExCalls++;
            actualKeySize = 0;
            return CanoeKeyGenerationResult.UnspecifiedError;
        };
        CanoeSeedKeyGenerator.GenerateKeyExOptCallback generateKeyExOpt = (
            byte[] seed,
            uint seedSize,
            uint securityLevel,
            byte[] variant,
            byte[] algorithmOptions,
            byte[] key,
            uint keySize,
            out uint actualKeySize) =>
        {
            generateKeyExOptCalls++;
            Assert.AreEqual(2u, seedSize);
            Assert.AreEqual(0x03u, securityLevel);
            Assert.AreEqual(3u, keySize);
            CollectionAssert.AreEqual(new byte[] { 0x56, 0x78 }, seed);
            CollectionAssert.AreEqual(new byte[] { 0x00 }, variant);
            CollectionAssert.AreEqual(new byte[] { 0x9A, 0x00 }, algorithmOptions);

            key[0] = 0x01;
            key[1] = 0x02;
            key[2] = 0x03;
            actualKeySize = 3;
            return CanoeKeyGenerationResult.Ok;
        };
        using var generator = new CanoeSeedKeyGenerator(generateKeyEx, generateKeyExOpt);

        var key = generator.GenerateKey([0x56, 0x78], 0x03, new CanoeSeedKeyOptions
        {
            AlgorithmOptions = [0x9A],
            KeyBufferSize = 3,
        });

        CollectionAssert.AreEqual(new byte[] { 0x01, 0x02, 0x03 }, key);
        Assert.AreEqual(0, generateKeyExCalls);
        Assert.AreEqual(1, generateKeyExOptCalls);
    }

    [TestMethod]
    public void GenerateKey_FallsBackToGenerateKeyExOptWhenGenerateKeyExIsMissing()
    {
        CanoeSeedKeyGenerator.GenerateKeyExOptCallback generateKeyExOpt = (
            byte[] _,
            uint _,
            uint _,
            byte[] _,
            byte[] _,
            byte[] key,
            uint _,
            out uint actualKeySize) =>
        {
            key[0] = 0xEE;
            actualKeySize = 1;
            return CanoeKeyGenerationResult.Ok;
        };
        using var generator = new CanoeSeedKeyGenerator(null, generateKeyExOpt);

        var key = generator.GenerateKey([0x01], 0x01);

        CollectionAssert.AreEqual(new byte[] { 0xEE }, key);
    }

    [TestMethod]
    public void TryGenerateKey_DoesNotFallbackWhenGenerateKeyExReturnsError()
    {
        var generateKeyExOptCalls = 0;
        CanoeSeedKeyGenerator.GenerateKeyExCallback generateKeyEx = (
            byte[] _,
            uint _,
            uint _,
            byte[] _,
            byte[] _,
            uint _,
            out uint actualKeySize) =>
        {
            actualKeySize = 0;
            return CanoeKeyGenerationResult.SecurityLevelInvalid;
        };
        CanoeSeedKeyGenerator.GenerateKeyExOptCallback generateKeyExOpt = (
            byte[] _,
            uint _,
            uint _,
            byte[] _,
            byte[] _,
            byte[] key,
            uint _,
            out uint actualKeySize) =>
        {
            generateKeyExOptCalls++;
            key[0] = 0xAA;
            actualKeySize = 1;
            return CanoeKeyGenerationResult.Ok;
        };
        using var generator = new CanoeSeedKeyGenerator(generateKeyEx, generateKeyExOpt);

        var result = generator.TryGenerateKey([0x01], 0x01, out var key);

        Assert.AreEqual(CanoeKeyGenerationResult.SecurityLevelInvalid, result);
        Assert.HasCount(0, key);
        Assert.AreEqual(0, generateKeyExOptCalls);
    }

    [TestMethod]
    public void GenerateKey_RequiresGenerateKeyExOptWhenAlgorithmOptionsArePresent()
    {
        CanoeSeedKeyGenerator.GenerateKeyExCallback generateKeyEx = (
            byte[] _,
            uint _,
            uint _,
            byte[] _,
            byte[] _,
            uint _,
            out uint actualKeySize) =>
        {
            actualKeySize = 0;
            return CanoeKeyGenerationResult.Ok;
        };
        using var generator = new CanoeSeedKeyGenerator(generateKeyEx, null);

        Assert.ThrowsExactly<EntryPointNotFoundException>(() =>
            generator.GenerateKey([0x01], 0x01, new CanoeSeedKeyOptions
            {
                AlgorithmOptions = [0x01],
            }));
    }

    [TestMethod]
    public void GenerateKey_ThrowsOnNonOkResult()
    {
        CanoeSeedKeyGenerator.GenerateKeyExCallback generateKeyEx = (
            byte[] _,
            uint _,
            uint _,
            byte[] _,
            byte[] _,
            uint _,
            out uint actualKeySize) =>
        {
            actualKeySize = 0;
            return CanoeKeyGenerationResult.BufferTooSmall;
        };
        using var generator = new CanoeSeedKeyGenerator(generateKeyEx, null);

        var ex = Assert.ThrowsExactly<InvalidOperationException>(() => generator.GenerateKey([0x01], 0x01));

        StringAssert.Contains(ex.Message, nameof(CanoeKeyGenerationResult.BufferTooSmall));
    }
}
