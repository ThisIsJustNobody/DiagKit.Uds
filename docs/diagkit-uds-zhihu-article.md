# 用 C# 实现 UDS / DoCAN / DoIP 的汽车 ECU 诊断通信

摘要：DiagKit.Uds 是一个面向 .NET 10 的 UDS 诊断通信库，覆盖 ISO 14229 应用层、ISO 15765 DoCAN 传输层和 ISO 13400 DoIP 传输层。它的核心价值不是再封装一个固定厂商设备，而是把“UDS 请求/响应、分段传输、NRC 处理、常用诊断服务、ECU 仿真测试”整理成可直接复用的 C# API。下面先用一个无需硬件的完整 Demo 跑通诊断会话、读取 VIN DID 和 SecurityAccess，再说明如何接入真实 CAN 设备或 DoIP ECU。

## 一、先跑一个 UDS over DoCAN 闭环 Demo

搜索 UDS 库时，最先需要确认的是：安装后能否快速跑起来、API 是否符合预期、基础诊断流程是否可验证。下面的 Demo 不依赖 CAN 卡或真实 ECU，使用两个 `Channel<CanFrame>` 模拟 CAN 总线双向收发：

- 测试设备侧：`AsyncUdsClient` + `AsyncDoCanTransmitter`
- ECU 侧：`UdsEcuSimulator` + `AsyncDoCanTransmitter`
- 验证内容：`DiagnosticSessionControl(0x10)`、`ReadDataByIdentifier(0x22)`、`SecurityAccess(0x27)`

创建控制台项目并安装包：

```bash
dotnet new console -n DiagKitUdsQuickStart -f net10.0
cd DiagKitUdsQuickStart
dotnet add package DiagKit.Uds
```

替换 `Program.cs`：

```csharp
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using DiagKit.Uds.DoCan;
using DiagKit.Uds.Services;
using DiagKit.Uds.UdsLayer;

// ====== Demo 参数区：真实项目中通常来自配置文件或命令行 ======
const uint TesterRequestId = 0x7E0;   // 测试设备发送到 ECU 的物理请求 CAN ID
const uint EcuResponseId = 0x7E8;     // ECU 返回给测试设备的响应 CAN ID
const ushort VinDid = 0xF190;         // 常见 VIN DID，仅用于演示
byte[] vinBytes = Encoding.ASCII.GetBytes("TESTVIN1234567890");
byte[] seedBytes = { 0x12, 0x34, 0x56, 0x78 };

// 使用两个 Channel<CanFrame> 模拟一条 CAN 总线的双向收发。
// testerToEcu：测试设备发出的 CAN 帧；ecuToTester：ECU 返回的 CAN 帧。
var testerToEcu = Channel.CreateUnbounded<CanFrame>();
var ecuToTester = Channel.CreateUnbounded<CanFrame>();

// 测试设备侧 DoCAN：请求 ID = 0x7E0，响应 ID = 0x7E8。
var testerTransport = new AsyncDoCanTransmitter(testerToEcu, ecuToTester, new DoCanOptions
{
    RequestId = TesterRequestId,
    ResponseId = EcuResponseId,
    UseFd = false
});

// ECU 侧 DoCAN：从 ECU 视角看，发送 ID 和接收 ID 与测试设备侧相反。
var ecuTransport = new AsyncDoCanTransmitter(ecuToTester, testerToEcu, new DoCanOptions
{
    RequestId = EcuResponseId,
    ResponseId = TesterRequestId,
    UseFd = false
});

var udsClient = new AsyncUdsClient(testerTransport);
await using var ecu = new UdsEcuSimulator(ecuTransport);

// 配置 ECU 仿真器：写入一个 VIN DID，并注册一个简单的 SecurityAccess seed/key 算法。
ecu.SetDataIdentifier(VinDid, vinBytes);
ecu.RegisterSecurityAccess(0x01, seedBytes, seed => seed.Select(b => (byte)~b).ToArray());

await ecu.StartAsync(CancellationToken.None);

try
{
    Console.WriteLine("开始执行 UDS over DoCAN 闭环 Demo");

    var session = await DiagnosticSessionControl.InvokeAsync(
        udsClient,
        DiagnosticSessionType.ExtendedDiagnostic,
        CancellationToken.None);

    Console.WriteLine(
        $"诊断会话切换成功：{session.Session}，P2={session.P2Server.TotalMilliseconds:N0}ms，P2*={session.P2ServerExtended.TotalMilliseconds:N0}ms");

    var vin = await ReadDataByIdentifier.InvokeAsync(udsClient, VinDid, CancellationToken.None);
    Console.WriteLine($"读取 DID 0x{VinDid:X4} 成功：{Encoding.ASCII.GetString(vin.Span)}");

    var unlocked = await SecurityAccess.UnlockAsync(
        udsClient,
        requestSeedLevel: 0x01,
        seedToKey: seed => seed.Select(b => (byte)~b).ToArray(),
        CancellationToken.None);

    Console.WriteLine($"安全访问解锁结果：{unlocked}");
}
finally
{
    await ecu.StopAsync(CancellationToken.None);
}
```

运行：

```bash
dotnet run
```

预期输出类似：

```text
开始执行 UDS over DoCAN 闭环 Demo
诊断会话切换成功：ExtendedDiagnostic，P2=50ms，P2*=5,000ms
读取 DID 0xF190 成功：TESTVIN1234567890
安全访问解锁结果：True
```

这个 Demo 的意义在于先验证库本身：UDS 客户端、DoCAN 分段传输、ECU 仿真器、服务辅助类和异步调用链都已经跑通。接下来只需要把内存 `Channel<CanFrame>` 换成真实 CAN 驱动或 DoIP TCP 连接。

## 二、换成真实 CAN 设备

DiagKit.Uds 不强绑定任何 CAN 厂商 SDK。真实 CAN 通道由上层注入，DiagKit.Uds 只要求能够发送和接收 `CanFrame`。如果项目已经使用 CanHub 打开 Vector、ZLG 等设备，可以通过桥接包直接创建 DoCAN 传输器。

安装可选桥接包：

```bash
dotnet add package DiagKit.Uds.CanHub
```

下面示例展示从已打开的 CanHub 总线创建 UDS 客户端。参数区应优先调整为项目真实配置：

```csharp
using System.Text;
using CanHub;
using CanHub.Adapter.Vector;
using DiagKit.Uds.CanHub;
using DiagKit.Uds.DoCan;
using DiagKit.Uds.Services;
using DiagKit.Uds.UdsLayer;

// ====== 真实 CAN 参数区：按设备、通道和 ECU 地址修改 ======
const string CanEndpoint = "vector://VN1630A?channelIndex=0&bitrate=500000";
const uint TesterRequestId = 0x7E0;
const uint EcuResponseId = 0x7E8;
const ushort VinDid = 0xF190;

// 如果使用 ZLG，可将适配器包和 Endpoint 换成：
// dotnet add package CanHub.Adapter.Zlg
// const string CanEndpoint = "zlg://USBCANFD_200U?deviceIndex=0&channelIndex=0&bitrate=500000";
// var registry = CanHubRegistry.CreateDefault().AddZlgAdapter();

var registry = CanHubRegistry.CreateDefault().AddVectorAdapter();

await using var bus = await registry.OpenAsync(CanEndpoint, CancellationToken.None);
await using var transport = bus.CreateDoCanTransport(new DoCanOptions
{
    RequestId = TesterRequestId,
    ResponseId = EcuResponseId,
    UseFd = false,
});

var udsClient = new AsyncUdsClient(transport);

var vin = await ReadDataByIdentifier.InvokeAsync(udsClient, VinDid, CancellationToken.None);
Console.WriteLine($"读取 DID 0x{VinDid:X4} 成功：{Encoding.ASCII.GetString(vin.Span)}");
```

这类接入方式适合已有 CAN 驱动抽象的项目。Vector、ZLG 的设备打开、通道参数、波特率、CAN FD 选项交给 CanHub 或现有驱动层；DiagKit.Uds 专注处理 UDS 和 DoCAN，包括单帧/首帧/连续帧/流控帧、P2/P2* 超时和 NRC 状态机。

如果不使用 CanHub，也可以直接用委托适配自研或第三方 CAN 驱动：

```csharp
var transport = new AsyncDoCanTransmitter(
    sendAsync: async (frame, ct) =>
    {
        // 将 DiagKit.Uds.DoCan.CanFrame 转成实际驱动的发送结构。
        await driver.SendAsync(
            canId: frame.CanId,
            payload: frame.Data.ToArray(),
            isFd: frame.FdFlag,
            cancellationToken: ct);
    },
    receiveAsync: async ct =>
    {
        // 将实际驱动收到的帧转成 DiagKit.Uds.DoCan.CanFrame。
        var raw = await driver.ReceiveAsync(ct);
        return CanFrame.CreateCopy(
            raw.CanId,
            raw.Payload,
            fd: raw.IsFd,
            brs: raw.BitRateSwitch,
            extendedId: raw.IsExtendedId);
    },
    clearReceiveBuffer: () => driver.ClearReceiveBuffer(),
    options: new DoCanOptions
    {
        RequestId = 0x7E0,
        ResponseId = 0x7E8,
        UseFd = false,
    });

var udsClient = new AsyncUdsClient(transport);
```

这也是该库比较实用的一点：硬件驱动层和 UDS 协议层没有绑死。项目可以保留现有驱动、日志、调度和线程模型，只把诊断协议栈替换为 DiagKit.Uds。

## 三、换成 DoIP 真实 ECU

对于以太网诊断，DiagKit.Uds 提供 `AsyncDoIpStreamTransport`。它基于 `Stream` 工作，因此可以接 `NetworkStream`，也可以接已经完成认证的 `SslStream`。

下面是直接连接 DoIP ECU 并读取 VIN 的完整结构：

```csharp
using System.Net.Sockets;
using System.Text;
using DiagKit.Uds.DoIp;
using DiagKit.Uds.Services;
using DiagKit.Uds.UdsLayer;

// ====== DoIP 参数区：按实际 ECU 或网关配置修改 ======
const string DoIpHost = "192.168.0.10";
const int DoIpPort = 13400;
const ushort TesterLogicalAddress = 0x0E00;
const ushort EcuLogicalAddress = 0x1234;
const ushort VinDid = 0xF190;

using var tcp = new TcpClient();
await tcp.ConnectAsync(DoIpHost, DoIpPort);

await using var doip = new AsyncDoIpStreamTransport(tcp.GetStream(), new DoIpOptions
{
    SourceAddress = TesterLogicalAddress,
    TargetAddress = EcuLogicalAddress,
    AutoActivate = true,
});

var udsClient = new AsyncUdsClient(doip);

var vin = await ReadDataByIdentifier.InvokeAsync(udsClient, VinDid, CancellationToken.None);
Console.WriteLine($"读取 DID 0x{VinDid:X4} 成功：{Encoding.ASCII.GetString(vin.Span)}");
```

`AsyncDoIpStreamTransport` 会处理 DoIP 通用头、TCP_DATA 组帧、路由激活、诊断 ACK/NACK、AliveCheck 响应和目标消息筛选。业务侧仍然只面对 `AsyncUdsClient` 和 UDS 服务辅助类。

## 四、常用诊断服务封装

DiagKit.Uds 的服务辅助类覆盖了常见诊断工具的主路径：

- `DiagnosticSessionControl`：进入默认会话、扩展会话、编程会话。
- `TesterPresent`：维持诊断会话。
- `ReadDataByIdentifier` / `WriteDataByIdentifier`：读取或写入 DID。
- `SecurityAccess`：执行 requestSeed / sendKey，并支持自定义 SeedKey 算法或 CANoe SeedKey DLL。
- `RoutineControl`：启动、停止或查询 Routine。
- `ReadDtcInformation` / `ClearDiagnosticInformation` / `ControlDtcSetting`：DTC 读取、清除和设置控制。
- `RequestDownload` / `TransferData` / `RequestTransferExit`：刷写下载链路的基础服务。

典型调用方式保持在较高层级，不需要每次手写 UDS 原始字节：

```csharp
var session = await DiagnosticSessionControl.InvokeAsync(
    udsClient,
    DiagnosticSessionType.Programming,
    CancellationToken.None);

var unlocked = await SecurityAccess.UnlockAsync(
    udsClient,
    requestSeedLevel: 0x01,
    seedToKey: seed => seed.Select(b => (byte)~b).ToArray(),
    CancellationToken.None);

var vin = await ReadDataByIdentifier.InvokeAsync(
    udsClient,
    did: 0xF190,
    CancellationToken.None);
```

对于诊断工具、刷写工具和自动化测试平台来说，这种封装能减少大量重复代码：请求构造、肯定响应解析、否定响应异常、P2 超时、RC 0x78 等待、RC 0x21 重试，都由库内部统一处理。

## 五、ECU 仿真器适合做自动化测试

`UdsEcuSimulator` 是这个库很适合工程化落地的一个能力。它可以快速注册 DID、SecurityAccess、Routine、DTC 和原始 SID 响应，用来在没有真实 ECU 的情况下测试上层业务逻辑。

```csharp
await using var ecu = new UdsEcuSimulator(ecuTransport);

ecu.SetDataIdentifier(0xF190, Encoding.ASCII.GetBytes("TESTVIN1234567890"));

ecu.RegisterSecurityAccess(
    requestSeedLevel: 0x01,
    seed: new byte[] { 0x12, 0x34, 0x56, 0x78 },
    seedToKey: seed => seed.Select(b => (byte)~b).ToArray());

ecu.RegisterRoutine(
    routineId: 0x0203,
    statusRecord: new byte[] { 0x00 });

await ecu.StartAsync(CancellationToken.None);
```

如果需要故障注入，也可以脚本化响应，例如先返回 `0x78 ResponsePending`，再返回最终肯定响应。这对于验证 P2/P2*、重试策略和上层状态机很有价值。

## 六、架构设计要点

从接口形态看，DiagKit.Uds 是一个分层协议栈：

```text
真实 CAN / TCP / 自定义驱动
        ↓
DoCAN / DoIP 传输层
        ↓
AsyncUdsClient / AsyncUdsServer / UdsEcuSimulator
        ↓
DiagnosticSessionControl / ReadDataByIdentifier / SecurityAccess / RoutineControl / DTC / Flashing
```

几个设计点值得关注：

- 异步优先：诊断链路天然依赖超时、等待和设备响应，异步 API 更适合 GUI 工具、自动化平台和后台服务。
- 链路层可插拔：CAN 硬件、TCP 连接和自定义收发器通过委托或传输接口注入。
- DoCAN 支持 CAN / CAN FD：`DoCanOptions` 可配置 `UseFd`、`MaxDlc`、`BrsEnabled`、`BlockSize`、`STmin` 和 ISO 15765 超时参数。
- DoIP 面向真实 TCP 流：`AsyncDoIpStreamTransport` 负责 DoIP 消息边界、路由激活和诊断消息确认。
- 服务层避免重复造轮子：常用 UDS 服务有明确的请求构造、响应解析和异常处理。
- 测试友好：`Channel<T>` 和 `UdsEcuSimulator` 让协议、业务和边界场景可以在 CI 中无硬件验证。

## 七、适用场景与边界

适合场景：

- C# / .NET 诊断工具、产线工具、售后工具。
- 需要同时支持 DoCAN 和 DoIP 的 ECU 诊断项目。
- 需要把 Vector、ZLG 或其他 CAN 驱动接入统一 UDS 层的项目。
- 需要用内存 ECU 仿真器做自动化测试和故障注入的项目。
- 需要在刷写、DTC、DID、Routine、SecurityAccess 等场景中减少重复协议代码的项目。

需要明确的边界：

- 它不是 CAN 硬件驱动库；真实 CAN 设备需要 CanHub、厂商 SDK 或自研驱动提供收发能力。
- 物理寻址、功能寻址、CAN ID、DoIP 逻辑地址、会话时序和 SeedKey 算法仍需要按项目或 ECU 规范配置。
- 刷写流程中的文件格式、擦写策略、分块大小、完整性校验和 OEM 安全规则属于业务层，需要在服务辅助类之上实现。

## 八、总结

DiagKit.Uds 的价值在于把 UDS 诊断通信中最容易重复、最容易出错的部分下沉为稳定的 .NET API：DoCAN/DoIP 传输、UDS 请求响应、NRC 状态机、常用服务封装和 ECU 仿真测试。

如果目标是快速做一个 C# 诊断客户端，第一步可以先跑通本文开头的内存闭环 Demo；第二步把 `Channel<CanFrame>` 换成 CanHub、厂商 SDK 或 DoIP TCP 连接；第三步再在服务辅助类之上组织业务流程。这样从验证库可用，到连接真实设备，再到构建诊断工具，路径比较直接。
