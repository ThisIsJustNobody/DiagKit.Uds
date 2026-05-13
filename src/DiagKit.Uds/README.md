# DiagKit.Uds

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](../LICENSE)
[![NuGet](https://img.shields.io/nuget/v/DiagKit.Uds?label=NuGet&color=orange)](https://www.nuget.org/packages/DiagKit.Uds)
[![.NET](https://img.shields.io/badge/.NET-10.0-purple.svg)](https://dotnet.microsoft.com/)

A self-contained .NET library implementing **UDS** (Unified Diagnostic Services,
ISO 14229) over **DoCAN** (Diagnostics on CAN, ISO 15765) and **DoIP**
(Diagnostics over IP, ISO 13400) for automotive ECU diagnostics.

## Highlights

- **No external NuGet dependencies** — pure .NET 10, single assembly.
- **Layered architecture** — `CanFrame` + `DoCan`/`DoIp` transport → `UdsLayer`
  application → `Services` helpers.
- **Async-first API** — DoCAN and the core UDS client keep sync/async variants;
  DoIP transport and service helpers are async-first.
- **Span-based, allocation-light** — framing routines work on
  `ReadOnlySpan<byte>`/`ReadOnlyMemory<byte>` with zero unnecessary copies.
- **CAN-FD ready** — DLC mapping for both classic CAN (≤8 bytes) and CAN-FD
  (≤64 bytes); strict / accept / adapt mixing modes.
- **Robust NRC handling** — automatic state machines for RC 0x78
  (ResponsePending) and RC 0x21 (BusyRepeatRequest), with configurable timeouts.
- **Pluggable transport** — accepts raw send/receive `Func`/`Action` delegates,
  `IAsyncTransmitter<T>`/`ITransmitter<T>` instances, or `Channel<T>` /
  `BlockingCollection<T>` directly.
- **Server side included** — `AsyncUdsServer` dispatches inbound requests to
  per-SID handlers with built-in NRC support.
- **Helpers for common services** — DiagnosticSessionControl, TesterPresent
  (with keep-alive), SecurityAccess (seed/key), ReadDataByIdentifier,
  RoutineControl, ReadDtcInformation.

## Architecture

```
┌────────────────────────────────────────────────────────────┐
│  Application                                               │
│  Services.DiagnosticSessionControl, TesterPresent,         │
│           SecurityAccess, ReadDataByIdentifier,            │
│           RoutineControl, ReadDtcInformation               │
├────────────────────────────────────────────────────────────┤
│  UDS application layer (UdsLayer)                          │
│  AsyncUdsClient / UdsClient        AsyncUdsServer          │
│  RC 0x78 / RC 0x21 / suppression / matching                │
├────────────────────────────────────────────────────────────┤
│  Transport layer (DoCan or DoIp)                           │
│  AsyncDoCanTransmitter / DoCanTransmitter                  │
│  AsyncDoIpStreamTransport / AsyncDoIpTransmitter           │
│  ISO 15765 SF/FF/CF/FC, STmin       ISO 13400 stream,     │
│                                     routing, ack/nack      │
├────────────────────────────────────────────────────────────┤
│  Link layer  (your CAN driver / TCP socket)                │
└────────────────────────────────────────────────────────────┘
```

## Installation

```bash
dotnet add package DiagKit.Uds
```

Or reference the project directly:

```xml
<ProjectReference Include="path/to/DiagKit.Uds/DiagKit.Uds.csproj" />
```

## Quick start

### 1. Create a DoCAN transport

```csharp
using DiagKit.Uds.DoCan;
using DiagKit.Uds.UdsLayer;
using System.Threading.Channels;

// In real code, sendChannel/receiveChannel come from your CAN driver.
var sendChannel    = Channel.CreateUnbounded<CanFrame>();
var receiveChannel = Channel.CreateUnbounded<CanFrame>();

var transport = new AsyncDoCanTransmitter(sendChannel, receiveChannel,
    new DoCanOptions
    {
        RequestId  = 0x7E0,   // tester → ECU
        ResponseId = 0x7E8,   // ECU → tester
        UseFd      = false,   // classic CAN; set true for CAN-FD
    });
```

### 2. Layer a UDS client session on top

```csharp
var udsClient = new AsyncUdsClient(transport, new UdsOptions
{
    P2Client         = TimeSpan.FromMilliseconds(150),
    P2ClientExtended = TimeSpan.FromSeconds(5),
    Rc78Handling     = Rc78Handling.WaitForCompletion,
});

// UdsClientSession accepts concurrent callers, executes requests FIFO on one
// worker, and can manage S3 TesterPresent without racing business requests.
await using var client = new UdsClientSession(udsClient, new UdsClientSessionOptions
{
    TesterPresentEnabled = true,
});
```

### 3. Issue requests

```csharp
// Raw request:
byte[] response = await client.SendRequestAsync(new byte[] { 0x10, 0x03 });

// Or use a helper:
using DiagKit.Uds.Services;

var session = await DiagnosticSessionControl.InvokeAsync(
    client, DiagnosticSessionType.ExtendedDiagnostic);
Console.WriteLine($"P2 = {session.P2Server}, P2* = {session.P2ServerExtended}");

byte[] vin = await ReadDataByIdentifier.InvokeAsync(client, did: 0xF190);
```

### 4. Security access

```csharp
bool unlocked = await SecurityAccess.UnlockAsync(client,
    requestSeedLevel: 0x01,
    seedToKey: seed =>
    {
        // Your OEM-specific algorithm (e.g. AES, lookup, etc.).
        return ComputeKey(seed);
    });
```

### 5. TesterPresent keep-alive

```csharp
// Prefer session-managed keep-alive:
await using var client = new UdsClientSession(udsClient, new UdsClientSessionOptions
{
    TesterPresentEnabled = true,
    // Optional; defaults to udsClient.Options.S3Client.
    TesterPresentInterval = TimeSpan.FromSeconds(2),
});
```

`TesterPresent.StartKeepAlive(...)` is a low-level helper for callers that own
request sequencing. Do not run it against the raw `AsyncUdsClient` while other
requests may be in flight, and do not enable it together with
`UdsClientSessionOptions.TesterPresentEnabled` for the same ECU session.

## Server side (simulator / ECU stub)

`UdsEcuSimulator` is the easiest way to build an ECU stand-in for tests. It
keeps ECU-like state and includes common services such as DiagnosticSessionControl,
TesterPresent, ReadDataByIdentifier, SecurityAccess, RoutineControl, and
ReadDTCInformation.

```csharp
var simulator = new UdsEcuSimulator(transport);
simulator.SetDataIdentifier(0xF190,
    "TESTVIN1234567890"u8.ToArray());
simulator.RegisterSecurityAccess(0x01,
    seed: new byte[] { 0x01, 0x02, 0x03, 0x04 },
    seedToKey: seed => seed.Select(b => (byte)~b).ToArray());
simulator.RegisterRoutine(0x1234,
    statusRecord: new byte[] { 0x00 });

await simulator.StartAsync(cts.Token);
```

`StartAsync` starts one background receive/respond loop. `StopAsync` cancels
that loop and waits for it to finish; after it stops, the simulator may be
started again. `Completion` exposes the current or most recent background loop
task, and transport failures fault that task. Awaiting `StopAsync` after such a
failure rethrows the transport exception.

Do not mix the background loop with manual `ReceiveAndRespondAsync` calls.
`ReceiveAndRespondAsync` is for single-step tests and rejects concurrent calls
or calls made while the background loop is running.

You can also register raw SID handlers or scripted responses for fault-injection
tests:

```csharp
simulator.RegisterScript(UdsServiceId.ReadDataByIdentifier,
    UdsServerResponse.Negative(0x22,
        NegativeResponseCode.RequestCorrectlyReceivedResponsePending),
    UdsServerResponse.Positive(0x22,
        new byte[] { 0xF1, 0x90, 0x12, 0x34 },
        delay: TimeSpan.FromMilliseconds(10)));
```

`AsyncUdsServer` remains available as the low-level raw dispatcher when you want
to handle every response payload yourself.

```csharp
var server = new AsyncUdsServer(transport);
server.Register(UdsServiceId.DiagnosticSessionControl, (req, _) =>
    Task.FromResult(AsyncUdsServer.BuildPositiveResponse(0x10,
        new byte[] { req.Span[1], 0x00, 0x32, 0x01, 0xF4 })));

while (!cts.IsCancellationRequested)
    await server.ReceiveAndRespondAsync(cts.Token);
```

Unknown SIDs automatically receive `0x7F SID 0x11` (ServiceNotSupported).
Handler exceptions propagate to the caller. Return an explicit negative
response when a service needs to reject a request with an NRC.

`AsyncUdsServer` currently receives only UDS payload bytes. It applies
physical-addressing-like response behavior and does not have enough addressing
metadata to implement ISO 14229 functional-addressing NRC suppression rules.

## DoIP transport

```csharp
using DiagKit.Uds.DoIp;
using System.Net.Sockets;

using var tcp = new TcpClient();
await tcp.ConnectAsync("192.168.0.10", 13400);

await using var doip = new AsyncDoIpStreamTransport(tcp.GetStream(),
    new DoIpOptions
    {
        SourceAddress = 0x0E00,
        TargetAddress = 0x1234,
        AutoActivate  = true,
    },
    leaveOpen: false);

var client = new AsyncUdsClient(doip);
byte[] vin = await ReadDataByIdentifier.InvokeAsync(client, 0xF190);
```

`AsyncDoIpStreamTransport` owns DoIP TCP_DATA stream framing: it reads the
8-byte generic header, applies `MaxPayloadLength`, performs routing activation,
validates diagnostic ACK/NACK addresses, responds to alive-check requests, and
caches non-target messages so final diagnostic responses are not lost.

For TLS, pass an already-authenticated `SslStream` to
`AsyncDoIpStreamTransport`; TLS-specific ISO 13400-2:2025 details still require
separate standards review before claiming end-to-end TLS compliance.

`AsyncDoIpTransmitter` remains available for advanced callers that already have
a reliable `DoIpMessage` framing layer. It uses the same DoIP control-message
handling rules but does not manage a TCP/TLS stream.

## Configuration

### `DoCanOptions`

| Property | Default | Notes |
| --- | --- | --- |
| `RequestId`/`ResponseId` | `0` | Physical addressing; set both. |
| `RequestIdExtended`/`ResponseIdExtended` | `false` | 29-bit IDs. |
| `UseFd` | `false` | CAN-FD framing. |
| `BrsEnabled` | `true` | Bit-rate switch (FD only). |
| `MinDlc`/`MaxDlc` | `8` / `8` | Raise `MaxDlc` to 15 for FD. |
| `PaddingValue` | `0xCC` | Standard automotive padding. |
| `BlockSize` | `0` | Receiver-side FC block size (0 = unbounded). |
| `STmin` | `0` | Receiver-side separation-time byte. |
| `TimeoutAs` / `Ar` / `Bs` / `Cr` | `1 s` | ISO 15765 timing budgets. |
| `FlowControlWaitInterval` | `10 ms` | Back-off while FC Wait is active. |
| `MaxFlowControlWaitFrames` | `8` | Abort segmented send after too many FC Wait frames. |
| `FrameMixingMode` | `Strict` | Coexistence with non-FD frames. |

### `UdsOptions`

| Property | Default | Notes |
| --- | --- | --- |
| `P2Client` | `150 ms` | Initial response timeout. |
| `P2ClientExtended` | `5 s` | After RC 0x78. |
| `Rc78Handling` | `WaitForCompletion` | Or `ReturnImmediately`. |
| `Rc78CompletionTimeout` | `25 s` | Total budget while RC 0x78 retries. |
| `Rc21Handling` | `ReturnImmediately` | Or `Retry`. |
| `Rc21RetryInterval` | `200 ms` | Delay between retries. |
| `WaitWhileSuppressingResponse` | `true` | Catch negative responses to suppressed requests. |
| `StrictServiceIdMatching` | `false` | Discard mismatched responses silently. |

## Building & testing

```bash
dotnet build  src/DiagKit.Uds/DiagKit.Uds.csproj
dotnet test   tests/DiagKit.Uds.Tests/DiagKit.Uds.Tests.csproj
```

The test suite is in-memory only (no real CAN hardware) and runs in ≈250 ms.

## Project layout

```
src/
└── DiagKit.Uds/
    ├── Contracts/   # ITransmitter<T>, IAsyncTransmitter<T>, IUdsClient, ...
    ├── DoCan/       # CanFrame, ISO 15765 framing, DoCAN transmitters
    ├── DoIp/        # ISO 13400 stream/message transports
    ├── Exceptions/  # UdsException hierarchy
    ├── Internal/    # LinkedCts (timeout-aware cancellation linker)
    ├── Services/    # Helpers for SID 0x10, 0x22, 0x27, 0x31, 0x3E, 0x19
    └── UdsLayer/    # UDS client/server/session/simulator
tests/
└── DiagKit.Uds.Tests/   # MSTest v4 suite
```

## License

MIT — see [LICENSE](LICENSE).
