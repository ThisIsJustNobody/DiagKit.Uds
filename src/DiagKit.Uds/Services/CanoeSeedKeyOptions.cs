using System;

namespace DiagKit.Uds.Services;

/// <summary>
/// CANoe SeedKey DLL 调用选项。<br/>Options for calling a CANoe SeedKey DLL.
/// </summary>
public sealed class CanoeSeedKeyOptions
{
    /// <summary>
    /// 创建默认的 CANoe SeedKey DLL 调用选项。<br/>Creates default CANoe SeedKey DLL call options.
    /// </summary>
    public CanoeSeedKeyOptions() { }

    /// <summary>
    /// 入口函数选择策略。默认值优先使用 GenerateKeyEx。<br/>Entry point selection strategy. The default prefers GenerateKeyEx.
    /// </summary>
    public CanoeSeedKeyMethod Method { get; set; } = CanoeSeedKeyMethod.PreferGenerateKeyEx;

    /// <summary>
    /// 输出 key 缓冲区大小；0 表示使用 seed 长度。<br/>Output key buffer size; 0 means the seed length is used.
    /// </summary>
    public int KeyBufferSize { get; set; }

    /// <summary>
    /// CANoe 变体名称的原始字节；调用 native 函数前会自动补充结尾零。<br/>
    /// Raw bytes for the CANoe variant name; a trailing zero is appended before calling the native function.
    /// </summary>
    public byte[]? Variant { get; set; }

    /// <summary>
    /// GenerateKeyExOpt 的算法选项原始字节；调用 native 函数前会自动补充结尾零。<br/>
    /// Raw algorithm option bytes for GenerateKeyExOpt; a trailing zero is appended before calling the native function.
    /// </summary>
    public byte[]? AlgorithmOptions { get; set; }

    /// <summary>
    /// 创建当前选项的深拷贝。<br/>Creates a deep copy of the current options.
    /// </summary>
    /// <returns>选项副本。<br/>The copied options.</returns>
    public CanoeSeedKeyOptions Clone()
        => new()
        {
            Method = Method,
            KeyBufferSize = KeyBufferSize,
            Variant = Variant?.ToArray(),
            AlgorithmOptions = AlgorithmOptions?.ToArray(),
        };

    /// <summary>
    /// 校验当前选项。<br/>Validates the current options.
    /// </summary>
    public void Validate()
    {
        if (!Enum.IsDefined(Method))
            throw new ArgumentOutOfRangeException(nameof(Method), Method, "Invalid CANoe SeedKey method.");
        if (KeyBufferSize < 0)
            throw new ArgumentOutOfRangeException(nameof(KeyBufferSize), KeyBufferSize, "Key buffer size cannot be negative.");
        if (Method == CanoeSeedKeyMethod.GenerateKeyExOnly && HasAlgorithmOptions)
            throw new ArgumentException("GenerateKeyEx cannot receive algorithm options.", nameof(AlgorithmOptions));
    }

    internal bool HasAlgorithmOptions => AlgorithmOptions is { Length: > 0 };
}
