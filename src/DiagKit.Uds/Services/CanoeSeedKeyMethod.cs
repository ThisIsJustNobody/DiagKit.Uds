namespace DiagKit.Uds.Services;

/// <summary>
/// 选择 CANoe SeedKey DLL 入口函数的策略。<br/>Strategy for selecting a CANoe SeedKey DLL entry point.
/// </summary>
public enum CanoeSeedKeyMethod
{
    /// <summary>
    /// 默认策略：无算法选项时优先使用 GenerateKeyEx；存在算法选项时使用 GenerateKeyExOpt。<br/>
    /// Default strategy: prefer GenerateKeyEx without algorithm options; use GenerateKeyExOpt when algorithm options are present.
    /// </summary>
    PreferGenerateKeyEx = 0,

    /// <summary>
    /// 优先使用 GenerateKeyExOpt；若入口缺失且没有算法选项，则回退到 GenerateKeyEx。<br/>
    /// Prefer GenerateKeyExOpt; fall back to GenerateKeyEx only when the entry point is missing and no algorithm options are present.
    /// </summary>
    PreferGenerateKeyExOpt = 1,

    /// <summary>
    /// 只允许使用 GenerateKeyEx。<br/>Use GenerateKeyEx only.
    /// </summary>
    GenerateKeyExOnly = 2,

    /// <summary>
    /// 只允许使用 GenerateKeyExOpt。<br/>Use GenerateKeyExOpt only.
    /// </summary>
    GenerateKeyExOptOnly = 3,
}
