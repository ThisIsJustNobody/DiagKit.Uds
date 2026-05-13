using System;
using System.Diagnostics;

using DiagKit.Uds.Exceptions;

namespace DiagKit.Uds.UdsLayer;

internal static class UdsClientResponseHandling
{
    public static ReadOnlyMemory<byte> HandlePossibleNrc(ReadOnlySpan<byte> request, ReadOnlyMemory<byte> response)
    {
        if (UdsMessage.IsNegativeResponse(response.Span))
        {
            if (!UdsMessage.IsNegativeResponseFor(request, response.Span))
                throw new ProtocolException($"Negative response references SID 0x{response.Span[1]:X2}, expected 0x{request[0]:X2}.");
            throw new NegativeResponseException(response.Span[1], (NegativeResponseCode)response.Span[2]);
        }

        return response;
    }

    public static bool IsMatchingResponse(ReadOnlySpan<byte> request, ReadOnlySpan<byte> response)
        => !response.IsEmpty
            && (UdsMessage.IsNegativeResponseFor(request, response)
                || UdsMessage.IsPositiveResponseFor(request, response));

    public static bool IsResponsePendingFor(ReadOnlySpan<byte> request, ReadOnlySpan<byte> response)
        => UdsMessage.IsNegativeResponseFor(request, response)
            && (NegativeResponseCode)response[2] == NegativeResponseCode.RequestCorrectlyReceivedResponsePending;

    public static bool IsBusyRepeatRequestFor(ReadOnlySpan<byte> request, ReadOnlySpan<byte> response)
        => UdsMessage.IsNegativeResponseFor(request, response)
            && (NegativeResponseCode)response[2] == NegativeResponseCode.BusyRepeatRequest;

    public static TimeSpan GetNextRc78Wait(UdsOptions options, Stopwatch overall)
    {
        var remaining = options.Rc78CompletionTimeout - overall.Elapsed;
        if (remaining <= TimeSpan.Zero)
            throw CreateRc78Exceeded(options);
        return remaining < options.P2ClientExtended ? remaining : options.P2ClientExtended;
    }

    public static ProtocolException CreateRc78Exceeded(UdsOptions options)
        => new($"RC 0x78 kept the ECU busy for over {options.Rc78CompletionTimeout.TotalMilliseconds:F0} ms.");

    public static ProtocolException CreateUnexpectedResponse(ReadOnlySpan<byte> request, ReadOnlySpan<byte> response, string context)
        => new($"Unexpected UDS response 0x{(!response.IsEmpty ? response[0] : 0):X2} {context} request 0x{request[0]:X2}.");
}
