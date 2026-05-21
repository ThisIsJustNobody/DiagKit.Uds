using DiagKit.Uds.DoCan;

namespace DiagKit.Uds.CanHub;

internal static class CanHubFrameConverter
{
    public static global::CanHub.CanFrame ToCanHub(CanFrame frame)
    {
        var id = frame.ExtendedId
            ? global::CanHub.CanId.Extended(frame.CanId)
            : global::CanHub.CanId.Standard(frame.CanId);

        return frame.FdFlag
            ? global::CanHub.CanFrame.CreateFdData(id, frame.Data.Span, frame.BitRateSwitch)
            : global::CanHub.CanFrame.CreateData(id, frame.Data.Span);
    }

    public static bool TryFromCanHubEvent(global::CanHub.CanFrameEvent frameEvent, out CanFrame frame)
    {
        frame = default;
        if (frameEvent.Direction != global::CanHub.CanFrameDirection.Receive)
            return false;
        if (frameEvent.Frame.Kind != global::CanHub.CanFrameKind.Data)
            return false;

        var source = frameEvent.Frame;
        var payload = new byte[source.PayloadLength];
        source.CopyPayloadTo(payload);
        bool fd = (source.Flags & global::CanHub.CanFrameFlags.FD) != 0;
        bool brs = (source.Flags & global::CanHub.CanFrameFlags.BRS) != 0;
        frame = CanFrame.Create(
            source.Id.Value,
            payload,
            fd,
            brs,
            source.Id.IsExtended);
        return true;
    }
}
