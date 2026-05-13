namespace DiagKit.Uds.UdsLayer;

/// <summary>
/// UDS 服务标识符（ISO 14229-1）。<br/>UDS service identifiers (ISO 14229-1).
/// </summary>
public enum UdsServiceId : byte
{
    // ── Diagnostic & Communication Management ──

    /// <summary>诊断会话控制。<br/>Diagnostic session control (0x10).</summary>
    DiagnosticSessionControl = 0x10,
    /// <summary>ECU 复位。<br/>ECU reset (0x11).</summary>
    EcuReset = 0x11,
    /// <summary>安全访问（种子/密钥认证）。<br/>Security access - seed/key authentication (0x27).</summary>
    SecurityAccess = 0x27,
    /// <summary>通信控制（启用/禁用消息收发）。<br/>Communication control - enable/disable message transmission (0x28).</summary>
    CommunicationControl = 0x28,
    /// <summary>认证。<br/>Authentication (0x29).</summary>
    Authentication = 0x29,
    /// <summary>诊断仪在线保活。<br/>Tester present - keep-alive heartbeat (0x3E).</summary>
    TesterPresent = 0x3E,
    /// <summary>访问时序参数。<br/>Access timing parameters (0x83).</summary>
    AccessTimingParameter = 0x83,
    /// <summary>安全数据传输。<br/>Secured data transmission (0x84).</summary>
    SecuredDataTransmission = 0x84,
    /// <summary>DTC 设置控制（开/关）。<br/>Control DTC setting - on/off (0x85).</summary>
    ControlDtcSetting = 0x85,
    /// <summary>事件响应。<br/>Response on event (0x86).</summary>
    ResponseOnEvent = 0x86,
    /// <summary>链路控制。<br/>Link control (0x87).</summary>
    LinkControl = 0x87,

    // ── Data Transmission ──

    /// <summary>按标识符读取数据。<br/>Read data by identifier (0x22).</summary>
    ReadDataByIdentifier = 0x22,
    /// <summary>按地址读取内存。<br/>Read memory by address (0x23).</summary>
    ReadMemoryByAddress = 0x23,
    /// <summary>按标识符读取标定数据。<br/>Read scaling data by identifier (0x24).</summary>
    ReadScalingDataByIdentifier = 0x24,
    /// <summary>按周期性标识符读取数据。<br/>Read data by periodic identifier (0x2A).</summary>
    ReadDataByPeriodicIdentifier = 0x2A,
    /// <summary>动态定义数据标识符。<br/>Dynamically define data identifier (0x2C).</summary>
    DynamicallyDefineDataIdentifier = 0x2C,
    /// <summary>按标识符写入数据。<br/>Write data by identifier (0x2E).</summary>
    WriteDataByIdentifier = 0x2E,
    /// <summary>按地址写入内存。<br/>Write memory by address (0x3D).</summary>
    WriteMemoryByAddress = 0x3D,

    // ── Stored Data Transmission ──

    /// <summary>清除诊断信息（含 DTC）。<br/>Clear diagnostic information including DTCs (0x14).</summary>
    ClearDiagnosticInformation = 0x14,
    /// <summary>读取 DTC 信息。<br/>Read DTC information (0x19).</summary>
    ReadDtcInformation = 0x19,

    // ── I/O Control ──

    /// <summary>按标识符输入输出控制。<br/>Input/output control by identifier (0x2F).</summary>
    InputOutputControlByIdentifier = 0x2F,

    // ── Routine ──

    /// <summary>例程控制（启动/停止/获取结果）。<br/>Routine control - start/stop/request results (0x31).</summary>
    RoutineControl = 0x31,

    // ── Upload / Download ──

    /// <summary>请求下载。<br/>Request download (0x34).</summary>
    RequestDownload = 0x34,
    /// <summary>请求上传。<br/>Request upload (0x35).</summary>
    RequestUpload = 0x35,
    /// <summary>传输数据。<br/>Transfer data (0x36).</summary>
    TransferData = 0x36,
    /// <summary>请求退出传输。<br/>Request transfer exit (0x37).</summary>
    RequestTransferExit = 0x37,
    /// <summary>请求文件传输。<br/>Request file transfer (0x38).</summary>
    RequestFileTransfer = 0x38,

    // ── Negative response ──

    /// <summary>否定响应（0x7F）。<br/>Negative response (0x7F).</summary>
    NegativeResponse = 0x7F,
}
