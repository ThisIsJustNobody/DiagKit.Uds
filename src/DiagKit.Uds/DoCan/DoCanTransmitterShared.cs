using DiagKit.Uds.Exceptions;

namespace DiagKit.Uds.DoCan;

internal static class DoCanTransmitterShared
{
    public static void AcceptFlowControlWaitFrame(ref int waitFrames, int limit)
    {
        waitFrames++;
        if (waitFrames > limit)
            throw new ProtocolException($"Flow-control wait frame limit exceeded ({limit}).");
    }

    public static int GetConsecutiveFrameLength(DoCanOptions options, int defaultFrameLength, int cfCapacity, int remaining)
    {
        if (options.FixedDlc || remaining >= cfCapacity)
            return defaultFrameLength;

        int minRequired = 1 + remaining;
        for (var d = options.MinDlc; d <= options.MaxDlc; d++)
        {
            int length = CanFrame.DlcToLength(d);
            if (length >= minRequired)
                return length;
        }

        return defaultFrameLength;
    }

    public static bool FrameMatches(CanFrame frame, DoCanOptions options)
    {
        if (frame.CanId != options.ResponseId) return false;
        if (frame.ExtendedId != options.ResponseIdExtended) return false;
        return options.FrameMixingMode switch
        {
            DoCanFrameMixingMode.Strict => frame.FdFlag == options.UseFd,
            DoCanFrameMixingMode.Accept => true,
            DoCanFrameMixingMode.Adapt => true,
            _ => false,
        };
    }
}
