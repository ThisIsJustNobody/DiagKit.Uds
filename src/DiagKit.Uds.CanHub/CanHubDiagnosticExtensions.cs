using DiagKit.Uds.DoCan;

namespace DiagKit.Uds.CanHub;

/// <summary>
/// CanHub 与 DiagKit.Uds 的便捷桥接扩展。<br/>Convenience bridge extensions for CanHub and DiagKit.Uds.
/// </summary>
public static class CanHubDiagnosticExtensions
{
    /// <summary>
    /// 将 CanHub 总线适配为 DiagKit.Uds CAN 帧传输器。<br/>Adapt a CanHub bus to a DiagKit.Uds CAN frame transmitter.
    /// </summary>
    public static CanHubCanTransmitter AsDiagKitCanTransmitter(
        this global::CanHub.ICanBus bus,
        global::CanHub.CanSubscriptionOptions? subscriptionOptions = null,
        global::CanHub.CanTransmitOptions? transmitOptions = null)
        => new(bus, subscriptionOptions, transmitOptions);

    /// <summary>
    /// 从 CanHub 总线创建 DoCAN 传输器。<br/>Create a DoCAN transport from a CanHub bus.
    /// </summary>
    public static CanHubDoCanTransport CreateDoCanTransport(
        this global::CanHub.ICanBus bus,
        DoCanOptions? doCanOptions = null,
        global::CanHub.CanSubscriptionOptions? subscriptionOptions = null,
        global::CanHub.CanTransmitOptions? transmitOptions = null)
        => new(bus, doCanOptions, subscriptionOptions, transmitOptions);
}
