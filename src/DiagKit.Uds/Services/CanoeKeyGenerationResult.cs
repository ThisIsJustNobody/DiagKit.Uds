namespace DiagKit.Uds.Services;

/// <summary>
/// CANoe SeedKey DLL 返回的密钥生成结果。<br/>Result codes returned by a CANoe SeedKey DLL.
/// </summary>
public enum CanoeKeyGenerationResult
{
    /// <summary>密钥生成成功。<br/>The key was generated successfully.</summary>
    Ok = 0,

    /// <summary>调用方提供的 key 缓冲区太小。<br/>The caller-provided key buffer is too small.</summary>
    BufferTooSmall = 1,

    /// <summary>安全访问等级无效。<br/>The security access level is invalid.</summary>
    SecurityLevelInvalid = 2,

    /// <summary>变体名称无效。<br/>The variant name is invalid.</summary>
    VariantInvalid = 3,

    /// <summary>未指定的密钥生成错误。<br/>An unspecified key generation error occurred.</summary>
    UnspecifiedError = 4,
}
