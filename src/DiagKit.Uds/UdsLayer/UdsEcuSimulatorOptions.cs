using System;

using DiagKit.Uds.Services;

namespace DiagKit.Uds.UdsLayer;

/// <summary>
/// ECU 仿真器配置。<br/>ECU simulator configuration.
/// </summary>
public sealed class UdsEcuSimulatorOptions
{
    /// <summary>启动时的诊断会话。默认 DefaultSession。<br/>The diagnostic session at startup. Default is DefaultSession.</summary>
    public DiagnosticSessionType InitialSession { get; set; } = DiagnosticSessionType.Default;

    /// <summary>P2 server 时间，编码到 0x10 正响应。默认 50 ms。<br/>P2 server time, encoded into the 0x10 positive response. Default 50 ms.</summary>
    public TimeSpan P2Server { get; set; } = TimeSpan.FromMilliseconds(50);

    /// <summary>P2* server 时间，编码到 0x10 正响应。默认 5000 ms。<br/>P2* server time, encoded into the 0x10 positive response. Default 5000 ms.</summary>
    public TimeSpan P2ServerExtended { get; set; } = TimeSpan.FromMilliseconds(5000);

    /// <summary>0x19 DTC 状态可用掩码。默认所有已定义状态位。<br/>0x19 DTC status availability mask. Default is all defined status bits.</summary>
    public DtcStatus DtcStatusAvailabilityMask { get; set; } = DtcStatus.All;

    /// <summary>0x19/0x01 DTC 格式标识。默认 0x01。<br/>0x19/0x01 DTC format identifier. Default 0x01.</summary>
    public byte DtcFormatIdentifier { get; set; } = 0x01;

    /// <summary>构造时是否注册常用内置服务。默认 true。<br/>Whether to register common built-in services on construction. Default true.</summary>
    public bool RegisterDefaultServices { get; set; } = true;

    /// <summary>创建可变副本。<br/>Create a mutable copy of these options.</summary>
    /// <returns>一个新的 <see cref="UdsEcuSimulatorOptions"/> 实例，值与此实例相同。<br/>A new <see cref="UdsEcuSimulatorOptions"/> instance with the same values.</returns>
    public UdsEcuSimulatorOptions Clone() => new()
    {
        InitialSession = InitialSession,
        P2Server = P2Server,
        P2ServerExtended = P2ServerExtended,
        DtcStatusAvailabilityMask = DtcStatusAvailabilityMask,
        DtcFormatIdentifier = DtcFormatIdentifier,
        RegisterDefaultServices = RegisterDefaultServices,
    };

    /// <summary>验证配置一致性。<br/>Validate option coherence. Throws if invalid.</summary>
    public void Validate()
    {
        if (!Enum.IsDefined(InitialSession))
            throw new ArgumentOutOfRangeException(nameof(InitialSession), InitialSession, "Invalid initial diagnostic session.");
        ValidatePositiveTimeout(P2Server, nameof(P2Server));
        ValidatePositiveTimeout(P2ServerExtended, nameof(P2ServerExtended));
        ValidateP2Encodable(P2Server, nameof(P2Server), scale: 1);
        ValidateP2Encodable(P2ServerExtended, nameof(P2ServerExtended), scale: 10);
    }

    private static void ValidatePositiveTimeout(TimeSpan value, string paramName)
    {
        if (value <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(paramName, value, "Timeout must be greater than zero.");
    }

    private static void ValidateP2Encodable(TimeSpan value, string paramName, int scale)
    {
        var encoded = Math.Ceiling(value.TotalMilliseconds / scale);
        if (encoded > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(paramName, value, "P2 timing value is too large to encode in a UDS response.");
    }
}
