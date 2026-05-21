using System;

namespace DiagKit.Uds.Services;

/// <summary>
/// SecurityAccess OEM seed/key 交换中的种子上下文。<br/>Seed context for an OEM SecurityAccess seed/key exchange.
/// </summary>
/// <param name="RequestSeedLevel">requestSeed 子功能级别。<br/>The requestSeed sub-function level.</param>
/// <param name="SendKeyLevel">sendKey 子功能级别。<br/>The sendKey sub-function level.</param>
/// <param name="Seed">ECU 返回的种子。<br/>The seed returned by the ECU.</param>
/// <param name="RequestSeedParameterRecord">requestSeed 附加参数记录。<br/>The requestSeed parameter record.</param>
public readonly record struct SecurityAccessSeedContext(
    byte RequestSeedLevel,
    byte SendKeyLevel,
    ReadOnlyMemory<byte> Seed,
    ReadOnlyMemory<byte> RequestSeedParameterRecord);
