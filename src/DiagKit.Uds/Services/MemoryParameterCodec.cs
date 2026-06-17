using System;

namespace DiagKit.Uds.Services;

internal static class MemoryParameterCodec
{
    public static byte[] BuildAddressAndSizeRequest(
        byte serviceId,
        byte dataFormatIdentifier,
        ulong memoryAddress,
        ulong memorySize,
        byte memoryAddressLength,
        byte memorySizeLength)
    {
        ValidateLength(memoryAddressLength, nameof(memoryAddressLength));
        ValidateLength(memorySizeLength, nameof(memorySizeLength));
        ValidateFits(memoryAddress, memoryAddressLength, nameof(memoryAddress));
        ValidateFits(memorySize, memorySizeLength, nameof(memorySize));

        var buf = new byte[3 + memoryAddressLength + memorySizeLength];
        buf[0] = serviceId;
        buf[1] = dataFormatIdentifier;
        buf[2] = (byte)((memorySizeLength << 4) | memoryAddressLength);
        WriteUnsignedBigEndian(memoryAddress, buf.AsSpan(3, memoryAddressLength));
        WriteUnsignedBigEndian(memorySize, buf.AsSpan(3 + memoryAddressLength, memorySizeLength));
        return buf;
    }

    public static ulong ParseMaxNumberOfBlockLength(
        ReadOnlySpan<byte> response,
        byte positiveResponseServiceId,
        string serviceName)
    {
        if (response.Length < 3 || response[0] != positiveResponseServiceId)
            throw new Exceptions.ProtocolException($"Not a positive {serviceName} response.");

        int length = response[1] >> 4;
        if (length == 0 || length > 8 || (response[1] & 0x0F) != 0)
            throw new Exceptions.FrameFormatException($"Invalid {serviceName} length format identifier.");
        if (response.Length != 2 + length)
            throw new Exceptions.FrameFormatException($"Truncated {serviceName} response.");

        return ReadUnsignedBigEndian(response.Slice(2, length));
    }

    public static int GetMaxTransferDataPayloadLength(ulong maxNumberOfBlockLength, string serviceName)
    {
        if (maxNumberOfBlockLength > (ulong)int.MaxValue + 2)
            throw new Exceptions.FrameFormatException($"{serviceName} max block length is too large.");

        return maxNumberOfBlockLength <= 2
            ? 0
            : checked((int)maxNumberOfBlockLength - 2);
    }

    private static void ValidateLength(byte length, string paramName)
    {
        if (length is < 1 or > 8)
            throw new ArgumentOutOfRangeException(paramName, length, "Length must be in the 1..8 range.");
    }

    private static void ValidateFits(ulong value, byte length, string paramName)
    {
        if (length == 8) return;
        ulong max = (1UL << (length * 8)) - 1;
        if (value > max)
            throw new ArgumentOutOfRangeException(paramName, value, $"Value does not fit in {length} bytes.");
    }

    private static void WriteUnsignedBigEndian(ulong value, Span<byte> destination)
    {
        for (int i = destination.Length - 1; i >= 0; i--)
        {
            destination[i] = (byte)value;
            value >>= 8;
        }
    }

    private static ulong ReadUnsignedBigEndian(ReadOnlySpan<byte> source)
    {
        ulong value = 0;
        foreach (var b in source)
            value = (value << 8) | b;
        return value;
    }
}
