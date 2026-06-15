# DiagKit.Uds

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![NuGet](https://img.shields.io/badge/NuGet-3.0.0-orange.svg)](https://www.nuget.org/packages/DiagKit.Uds)
[![.NET](https://img.shields.io/badge/.NET-10.0-purple.svg)](https://dotnet.microsoft.com/)

完全独立的 .NET 10 库，实现 **UDS**（ISO 14229）over **DoCAN**（ISO 15765）与
**DoIP**（ISO 13400）协议栈的汽车 ECU 诊断通信，无外部 NuGet 依赖。

> [English docs](README.md)

## 特性

- **零外部依赖** — 纯 .NET 10，单一程序集
- **分层协议栈** — CAN 帧 → DoCAN / DoIP 传输层 → UDS 客户端/服务端 → 服务辅助类
- **异步优先** — DoCAN 同步/异步双版本；DoIP 与服务辅助类仅异步
- **CAN-FD 兼容** — 经典 CAN 与 CAN-FD DLC 映射，支持严格/接受/自适应混合模式
- **NRC 自动处理** — RC 0x78（ResponsePending）等待与 RC 0x21（BusyRepeatRequest）重试状态机
- **可插拔传输层** — 支持委托、`Channel<T>`、`BlockingCollection<T>` 或自定义收发器注入
- **内置服务端** — `AsyncUdsServer` 分发器与 `UdsEcuSimulator` 模拟器，方便测试
- **常用服务辅助类** — 会话控制、ECU 复位、TesterPresent、安全访问、按 ID 读/写数据、例程控制、DTC 服务、IO 控制、RequestDownload/Upload、TransferData、RequestTransferExit

## 安装

```bash
dotnet add package DiagKit.Uds --version 3.0.0
```

可选 CanHub 桥接包：

```bash
dotnet add package DiagKit.Uds.CanHub
```

## 快速开始

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

CanHub 用户可从已打开的总线直接创建 DoCAN 传输：

```csharp
using DiagKit.Uds.CanHub;

await using ICanBus bus = await registry.OpenAsync("vector://VN16XX?channelIndex=0");
await using var transport = bus.CreateDoCanTransport(new DoCanOptions
{
    RequestId = 0x7E0,
    ResponseId = 0x7E8,
});
```

详细 API 文档、服务端用法、DoIP 传输、完整配置参数请参见 [包 README（中文）](src/DiagKit.Uds/README.zh-CN.md)。

## 构建

```powershell
dotnet restore "DiagKit.Uds.slnx"
dotnet build "DiagKit.Uds.slnx" -c Release --no-restore
dotnet test tests\DiagKit.Uds.Tests\DiagKit.Uds.Tests.csproj
dotnet test tests\DiagKit.Uds.CanHub.Tests\DiagKit.Uds.CanHub.Tests.csproj
dotnet pack src\DiagKit.Uds\DiagKit.Uds.csproj -c Release --no-build -o artifacts\packages
dotnet pack src\DiagKit.Uds.CanHub\DiagKit.Uds.CanHub.csproj -c Release --no-build -o artifacts\packages
```

## 许可证

MIT — 详见 [LICENSE](LICENSE)。
