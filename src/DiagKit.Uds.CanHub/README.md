# DiagKit.Uds.CanHub

Bridge package for connecting `DiagKit.Uds` DoCAN transport to an opened CanHub `ICanBus`.

```csharp
using DiagKit.Uds.CanHub;
using DiagKit.Uds.DoCan;
using DiagKit.Uds.UdsLayer;

await using ICanBus bus = await registry.OpenAsync("vector://VN16XX?channelIndex=0");
await using var transport = bus.CreateDoCanTransport(new DoCanOptions
{
    RequestId = 0x7E0,
    ResponseId = 0x7E8,
});

var client = new AsyncUdsClient(transport);
```

`DiagKit.Uds.CanHub` owns the CanHub subscription it creates, but it does not dispose the caller-provided `ICanBus`.
