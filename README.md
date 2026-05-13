# DiagKit.Uds

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![NuGet](https://img.shields.io/badge/NuGet-3.0.0-orange.svg)](https://www.nuget.org/packages/DiagKit.Uds)
[![.NET](https://img.shields.io/badge/.NET-10.0-purple.svg)](https://dotnet.microsoft.com/)

A self-contained .NET 10 library implementing **UDS** (ISO 14229) over **DoCAN**
(ISO 15765) and **DoIP** (ISO 13400) for automotive ECU diagnostics.
Zero external NuGet dependencies.

> [中文文档](README.zh-CN.md)

## Highlights

- **No external NuGet dependencies** — pure .NET 10, single assembly
- **Layered protocol stack** — CAN frame → DoCAN / DoIP transport → UDS client/server → service helpers
- **Async-first API** — sync/async variants for DoCAN; DoIP and services are async-first
- **CAN-FD ready** — DLC mapping for classic CAN and CAN-FD with configurable mixing modes
- **Robust NRC handling** — automatic state machines for RC 0x78 (ResponsePending) and RC 0x21 (BusyRepeatRequest)
- **Pluggable transport** — accepts delegates, `Channel<T>`, `BlockingCollection<T>`, or custom transmitters
- **Server side included** — `AsyncUdsServer` dispatcher and `UdsEcuSimulator` for testing
- **Helpers for common services** — SessionControl, TesterPresent, SecurityAccess, ReadDataByIdentifier, RoutineControl, ReadDtcInformation

## Installation

```bash
dotnet add package DiagKit.Uds --version 3.0.0
```

## Quick start

```csharp
using DiagKit.Uds.DoCan;
using DiagKit.Uds.UdsLayer;
using System.Threading.Channels;

var sendChannel    = Channel.CreateUnbounded<CanFrame>();
var receiveChannel = Channel.CreateUnbounded<CanFrame>();

var transport = new AsyncDoCanTransmitter(sendChannel, receiveChannel,
    new DoCanOptions { RequestId = 0x7E0, ResponseId = 0x7E8 });

var client = new AsyncUdsClient(transport);
byte[] response = await client.SendRequestAsync(new byte[] { 0x10, 0x03 });
```

See the [package README](src/DiagKit.Uds/README.md) for detailed API documentation, server-side usage, DoIP transport, and full configuration reference.

## Build

```powershell
dotnet restore "DiagKit.Uds.slnx"
dotnet build "DiagKit.Uds.slnx" -c Release --no-restore
dotnet test tests\DiagKit.Uds.Tests\DiagKit.Uds.Tests.csproj
dotnet pack src\DiagKit.Uds\DiagKit.Uds.csproj -c Release --no-build -o artifacts\packages
```

## License

MIT — see [LICENSE](LICENSE).
