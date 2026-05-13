using System;
using System.Buffers.Binary;

using DiagKit.Uds.Exceptions;

namespace DiagKit.Uds.DoIp;

internal static class DoIpMessageValidation
{
    public static void ValidateRoutingActivationResponse(DoIpMessage message, DoIpOptions options)
    {
        var payload = message.Payload.Span;
        if (payload.Length < 9)
            throw new FrameFormatException("Routing activation response too short.");

        var testerAddress = BinaryPrimitives.ReadUInt16BigEndian(payload[..2]);
        if (testerAddress != options.SourceAddress)
            throw new ProtocolException($"Routing activation response tester address 0x{testerAddress:X4} does not match source address 0x{options.SourceAddress:X4}.");

        var entityAddress = BinaryPrimitives.ReadUInt16BigEndian(payload.Slice(2, 2));
        if (options.EntityAddress.HasValue && entityAddress != options.EntityAddress.Value)
            throw new ProtocolException($"Routing activation response entity address 0x{entityAddress:X4} does not match expected address 0x{options.EntityAddress.Value:X4}.");

        var code = (DoIpActivationResponseCode)payload[4];
        if (code != DoIpActivationResponseCode.Success && code != DoIpActivationResponseCode.SuccessConfirmationRequired)
            throw new ProtocolException($"Routing activation rejected: 0x{(byte)code:X2} ({code}).");
    }

    public static void ValidateDiagnosticMessage(DoIpMessage message, DoIpOptions options)
    {
        var payload = message.Payload.Span;
        if (payload.Length < 4)
            throw new FrameFormatException("Diagnostic message too short.");
        ValidateReversedAddresses(payload, options, "Diagnostic message");
    }

    public static void ValidateDiagnosticAck(DoIpMessage message, DoIpOptions options)
    {
        var payload = message.Payload.Span;
        if (payload.Length < 5)
            throw new FrameFormatException("Diagnostic ACK too short.");
        ValidateReversedAddresses(payload, options, "Diagnostic ACK");

        var code = payload[4];
        if (code != 0x00)
            throw new ProtocolException($"DoIP diagnostic ACK code was 0x{code:X2}, expected 0x00.");
    }

    public static void ThrowDiagnosticNack(DoIpMessage message, DoIpOptions options)
    {
        var payload = message.Payload.Span;
        if (payload.Length < 5)
            throw new FrameFormatException("Diagnostic NACK too short.");
        ValidateReversedAddresses(payload, options, "Diagnostic NACK");

        var code = payload[4];
        throw new ProtocolException($"DoIP diagnostic NACK: 0x{code:X2} ({(DoIpDiagnosticNackCode)code}).");
    }

    public static void ThrowGenericHeaderNack(DoIpMessage message)
    {
        var code = message.Payload.Length > 0 ? message.Payload.Span[0] : (byte)0xFF;
        throw new ProtocolException($"DoIP generic header NACK: 0x{code:X2} ({(DoIpGenericHeaderNackCode)code}).");
    }

    public static void EnsurePayloadWithinLimit(int payloadLength, DoIpOptions options)
    {
        if (payloadLength > options.MaxPayloadLength)
            throw new ProtocolException($"DoIP payload length {payloadLength} exceeds MaxPayloadLength {options.MaxPayloadLength}.");
    }

    public static bool IsExpected(DoIpPayloadType payloadType, ReadOnlySpan<DoIpPayloadType> expected)
    {
        foreach (var type in expected)
            if (payloadType == type)
                return true;
        return false;
    }

    private static void ValidateReversedAddresses(ReadOnlySpan<byte> payload, DoIpOptions options, string label)
    {
        var source = BinaryPrimitives.ReadUInt16BigEndian(payload[..2]);
        var target = BinaryPrimitives.ReadUInt16BigEndian(payload.Slice(2, 2));

        if (source != options.TargetAddress || target != options.SourceAddress)
            throw new ProtocolException($"{label} addresses 0x{source:X4}->0x{target:X4} do not match expected 0x{options.TargetAddress:X4}->0x{options.SourceAddress:X4}.");
    }
}
