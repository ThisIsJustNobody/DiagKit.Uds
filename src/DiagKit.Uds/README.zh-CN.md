# DiagKit.Uds

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](../LICENSE)
[![NuGet](https://img.shields.io/nuget/v/DiagKit.Uds?label=NuGet&color=orange)](https://www.nuget.org/packages/DiagKit.Uds)
[![.NET](https://img.shields.io/badge/.NET-10.0-purple.svg)](https://dotnet.microsoft.com/)

完全独立的 .NET 库，实现 **UDS**（Unified Diagnostic Services，ISO 14229）over
**DoCAN**（Diagnostics on CAN，ISO 15765）与 **DoIP**（Diagnostics over IP，
ISO 13400）协议栈的汽车 ECU 诊断通信，无外部依赖。

## 特性

- **零外部 NuGet 依赖** — 纯 .NET 10，单一程序集。
- **分层架构** — `CanFrame` + `DoCan`/`DoIp` 传输层 → `UdsLayer` 应用层 → `Services` 辅助类。
- **异步优先 API** — DoCAN 与核心 UDS 客户端保留同步/异步双版本；DoIP 传输与服务辅助类仅异步。
- **基于 Span、低分配** — 组帧逻辑基于 `ReadOnlySpan<byte>`/`ReadOnlyMemory<byte>`，零额外拷贝。
- **CAN-FD 兼容** — 经典 CAN（≤8 字节）与 CAN-FD（≤64 字节）DLC 映射；支持严格/接受/自适应混合模式。
- **NRC 自动处理** — RC 0x78（ResponsePending）与 RC 0x21（BusyRepeatRequest）内建状态机，超时可配置。
- **可插拔传输层** — 支持原始发送/接收 `Func`/`Action` 委托、`IAsyncTransmitter<T>`/`ITransmitter<T>` 实例，或直接传入 `Channel<T>` / `BlockingCollection<T>`。
- **内置服务端** — `AsyncUdsServer` 将入站请求分发到各 SID 处理器，内建 NRC 支持。
- **常用服务辅助类** — DiagnosticSessionControl、TesterPresent（含心跳保活）、SecurityAccess（种子/密钥）、ReadDataByIdentifier、RoutineControl、ReadDtcInformation。

## 架构

```
┌────────────────────────────────────────────────────────────┐
│  应用层                                                     │
│  Services.DiagnosticSessionControl, TesterPresent,         │
│           SecurityAccess, ReadDataByIdentifier,            │
│           RoutineControl, ReadDtcInformation               │
├────────────────────────────────────────────────────────────┤
│  UDS 应用层（UdsLayer）                                     │
│  AsyncUdsClient / UdsClient        AsyncUdsServer          │
│  RC 0x78 / RC 0x21 / 抑制 / 匹配                            │
├────────────────────────────────────────────────────────────┤
│  传输层（DoCan 或 DoIp）                                     │
│  AsyncDoCanTransmitter / DoCanTransmitter                  │
│  AsyncDoIpStreamTransport / AsyncDoIpTransmitter           │
│  ISO 15765 SF/FF/CF/FC, STmin       ISO 13400 流式,       │
│                                     路由, ack/nack         │
├────────────────────────────────────────────────────────────┤
│  链路层（用户的 CAN 驱动 / TCP socket）                       │
└────────────────────────────────────────────────────────────┘
```

## 安装

```bash
dotnet add package DiagKit.Uds
```

或直接引用项目：

```xml
<ProjectReference Include="path/to/DiagKit.Uds/DiagKit.Uds.csproj" />
```

## 快速开始

### 1. 创建 DoCAN 传输

```csharp
using DiagKit.Uds.DoCan;
using DiagKit.Uds.UdsLayer;
using System.Threading.Channels;

// 实际代码中，sendChannel/receiveChannel 来自你的 CAN 驱动。
var sendChannel    = Channel.CreateUnbounded<CanFrame>();
var receiveChannel = Channel.CreateUnbounded<CanFrame>();

var transport = new AsyncDoCanTransmitter(sendChannel, receiveChannel,
    new DoCanOptions
    {
        RequestId  = 0x7E0,   // 诊断仪 → ECU
        ResponseId = 0x7E8,   // ECU → 诊断仪
        UseFd      = false,   // 经典 CAN；CAN-FD 设为 true
    });
```

### 2. 叠加 UDS 客户端会话

```csharp
var udsClient = new AsyncUdsClient(transport, new UdsOptions
{
    P2Client         = TimeSpan.FromMilliseconds(150),
    P2ClientExtended = TimeSpan.FromSeconds(5),
    Rc78Handling     = Rc78Handling.WaitForCompletion,
});

// UdsClientSession 接受并发调用方，以 FIFO 单工作线程执行请求，
// 可在不干扰业务请求的前提下管理 S3 TesterPresent。
await using var client = new UdsClientSession(udsClient, new UdsClientSessionOptions
{
    TesterPresentEnabled = true,
});
```

### 3. 发送请求

```csharp
// 原始请求：
byte[] response = await client.SendRequestAsync(new byte[] { 0x10, 0x03 });

// 或使用辅助类：
using DiagKit.Uds.Services;

var session = await DiagnosticSessionControl.InvokeAsync(
    client, DiagnosticSessionType.ExtendedDiagnostic);
Console.WriteLine($"P2 = {session.P2Server}, P2* = {session.P2ServerExtended}");

byte[] vin = await ReadDataByIdentifier.InvokeAsync(client, did: 0xF190);
```

### 4. 安全访问

```csharp
bool unlocked = await SecurityAccess.UnlockAsync(client,
    requestSeedLevel: 0x01,
    seedToKey: seed =>
    {
        // 你的 OEM 特定算法（如 AES、查表等）。
        return ComputeKey(seed);
    });
```

对于导出 `GenerateKeyEx` 或 `GenerateKeyExOpt` 的 CANoe SeedKey DLL，可直接加载，
不需要动态编译：

```csharp
using var keyGenerator = new CanoeSeedKeyGenerator(@"C:\SeedKey\SeedKey.dll");
bool unlocked = await SecurityAccess.UnlockAsync(client, 0x01, keyGenerator);

// 或让库按当前进程架构选择 DLL 路径。
using var archGenerator = CanoeSeedKeyGenerator.LoadForCurrentProcess(
    x86Path: @"C:\SeedKey\x86\SeedKey.dll",
    x64Path: @"C:\SeedKey\x64\SeedKey.dll");
```

### 5. TesterPresent 心跳保活

```csharp
// 推荐使用会话管理的心跳：
await using var client = new UdsClientSession(udsClient, new UdsClientSessionOptions
{
    TesterPresentEnabled = true,
    // 可选；默认为 udsClient.Options.S3Client。
    TesterPresentInterval = TimeSpan.FromSeconds(2),
});
```

`TesterPresent.StartKeepAlive(...)` 是面向自行控制请求时序的调用方的低级辅助方法。不要在可能有其他请求正在进行的 `AsyncUdsClient` 上直接运行它，也不要对同一 ECU 会话同时启用它与 `UdsClientSessionOptions.TesterPresentEnabled`。

## 服务端（模拟器 / ECU 桩）

`UdsEcuSimulator` 是构建 ECU 测试替身的最简便方式。它维护 ECU 风格的状态，并内建常用服务：DiagnosticSessionControl、TesterPresent、ReadDataByIdentifier、SecurityAccess、RoutineControl、ReadDTCInformation。

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

`StartAsync` 启动一个后台接收/响应循环。`StopAsync` 取消该循环并等待其结束；停止后可再次启动。`Completion` 暴露当前或最近一次后台循环任务，传输层失败会使该任务进入故障状态。在此类故障后 await `StopAsync` 会重新抛出传输异常。

不要将后台循环与手动 `ReceiveAndRespondAsync` 调用混用。`ReceiveAndRespondAsync` 用于单步测试，会拒绝并发调用或后台循环运行期间的调用。

也可以注册原始 SID 处理器或脚本化响应用于故障注入测试：

```csharp
simulator.RegisterScript(UdsServiceId.ReadDataByIdentifier,
    UdsServerResponse.Negative(0x22,
        NegativeResponseCode.RequestCorrectlyReceivedResponsePending),
    UdsServerResponse.Positive(0x22,
        new byte[] { 0xF1, 0x90, 0x12, 0x34 },
        delay: TimeSpan.FromMilliseconds(10)));
```

`AsyncUdsServer` 仍然可用，适合需要自行处理每个响应负载的低级原始分发场景。

```csharp
var server = new AsyncUdsServer(transport);
server.Register(UdsServiceId.DiagnosticSessionControl, (req, _) =>
    Task.FromResult(AsyncUdsServer.BuildPositiveResponse(0x10,
        new byte[] { req.Span[1], 0x00, 0x32, 0x01, 0xF4 })));

while (!cts.IsCancellationRequested)
    await server.ReceiveAndRespondAsync(cts.Token);
```

未知 SID 会自动收到 `0x7F SID 0x11`（ServiceNotSupported）。处理器异常会传播给调用方。当服务需要拒绝请求并返回 NRC 时，返回显式的否定响应。

`AsyncUdsServer` 目前仅接收 UDS 负载字节。它采用类似物理寻址的响应行为，没有足够的寻址元数据来实现 ISO 14229 功能寻址的 NRC 抑制规则。

## DoIP 传输

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

`AsyncDoIpStreamTransport` 负责 DoIP TCP_DATA 流式组帧：读取 8 字节通用头部、应用 `MaxPayloadLength`、执行路由激活、校验诊断 ACK/NACK 地址、响应保活检查请求，并缓存非目标消息，确保最终诊断响应不会丢失。

对于 TLS，将已认证的 `SslStream` 传入 `AsyncDoIpStreamTransport`；TLS 相关的 ISO 13400-2:2025 细节在声称端到端 TLS 合规前仍需单独对照标准审查。

`AsyncDoIpTransmitter` 仍然可用，适用于已经拥有可靠 `DoIpMessage` 组帧层的高级调用方。它使用相同的 DoIP 控制消息处理规则，但不负责 TCP/TLS 流的管理。

## 配置

### `DoCanOptions`

| 属性 | 默认值 | 说明 |
| --- | --- | --- |
| `RequestId`/`ResponseId` | `0` | 物理寻址；二者均需设置。 |
| `RequestIdExtended`/`ResponseIdExtended` | `false` | 29 位 ID。 |
| `UseFd` | `false` | CAN-FD 组帧。 |
| `BrsEnabled` | `true` | 比特率切换（仅 FD）。 |
| `MinDlc`/`MaxDlc` | `8` / `8` | FD 时将 `MaxDlc` 调至 15。 |
| `PaddingValue` | `0xCC` | 标准汽车行业填充值。 |
| `BlockSize` | `0` | 接收方 FC 块大小（0 = 无限制）。 |
| `STmin` | `0` | 接收方最小帧间隔。 |
| `TimeoutAs` / `Ar` / `Bs` / `Cr` | `1 s` | ISO 15765 时序预算。 |
| `FlowControlWaitInterval` | `10 ms` | FC Wait 期间回退间隔。 |
| `MaxFlowControlWaitFrames` | `8` | 过多 FC Wait 帧后中止分段发送。 |
| `FrameMixingMode` | `Strict` | 与非 FD 帧的共存策略。 |

### `UdsOptions`

| 属性 | 默认值 | 说明 |
| --- | --- | --- |
| `P2Client` | `150 ms` | 初始响应超时。 |
| `P2ClientExtended` | `5 s` | RC 0x78 后的扩展超时。 |
| `Rc78Handling` | `WaitForCompletion` | 或 `ReturnImmediately`。 |
| `Rc78CompletionTimeout` | `25 s` | RC 0x78 重试总预算。 |
| `Rc21Handling` | `ReturnImmediately` | 或 `Retry`。 |
| `Rc21RetryInterval` | `200 ms` | 重试间隔。 |
| `WaitWhileSuppressingResponse` | `true` | 捕获被抑制请求的否定响应。 |
| `StrictServiceIdMatching` | `false` | 静默丢弃不匹配的响应。 |

## 构建与测试

```bash
dotnet build  src/DiagKit.Uds/DiagKit.Uds.csproj
dotnet test   tests/DiagKit.Uds.Tests/DiagKit.Uds.Tests.csproj
```

测试套件为纯内存测试（无需真实 CAN 硬件），运行耗时约 250 ms。

## 项目结构

```
src/
└── DiagKit.Uds/
    ├── Contracts/   # ITransmitter<T>、IAsyncTransmitter<T>、IUdsClient 等
    ├── DoCan/       # CanFrame、ISO 15765 组帧、DoCAN 传输器
    ├── DoIp/        # ISO 13400 流式/消息传输
    ├── Exceptions/  # UdsException 层级
    ├── Internal/    # LinkedCts（超时感知取消链接器）
    ├── Services/    # SID 0x10、0x22、0x27、0x31、0x3E、0x19 辅助类
    └── UdsLayer/    # UDS 客户端/服务端/会话/模拟器
tests/
└── DiagKit.Uds.Tests/   # MSTest v4 套件
```

## 许可证

MIT — 详见 [LICENSE](LICENSE)。
