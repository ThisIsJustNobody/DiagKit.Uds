using System;
using System.Threading;
using System.Threading.Tasks;

using DiagKit.Uds.Contracts;
using DiagKit.Uds.Exceptions;
using DiagKit.Uds.UdsLayer;

namespace DiagKit.Uds.Services;

/// <summary>
/// SID 0x27（SecurityAccess/安全访问）辅助方法。<br/>Helpers for SID 0x27 SecurityAccess.
/// </summary>
/// <remarks>
/// 调用方可提供种子到密钥的转换委托，或使用 CANoe SeedKey DLL 生成 key；不执行任何动态编译。<br/>
/// The caller can provide a seed-to-key transformation delegate or use a CANoe SeedKey DLL; no dynamic compilation is performed.
/// </remarks>
public static class SecurityAccess
{
    /// <summary>
    /// 为指定级别构建 requestSeed 请求（级别必须为奇数：1、3、5……）。<br/>Build a requestSeed request for the given level (must be odd: 1, 3, 5, ...).
    /// </summary>
    /// <param name="level">安全访问级别（必须为奇数）。<br/>The security access level (must be odd).</param>
    /// <returns>requestSeed 请求字节数组。<br/>The requestSeed request byte array.</returns>
    public static byte[] BuildRequestSeed(byte level)
        => BuildRequestSeed(level, default);

    /// <summary>
    /// 为指定级别构建带附加参数记录的 requestSeed 请求。<br/>Build a requestSeed request with an additional parameter record.
    /// </summary>
    /// <param name="level">安全访问级别（必须为奇数）。<br/>The security access level (must be odd).</param>
    /// <param name="securityAccessDataRecord">OEM 附加参数记录。<br/>OEM-specific additional parameter record.</param>
    /// <returns>requestSeed 请求字节数组。<br/>The requestSeed request byte array.</returns>
    public static byte[] BuildRequestSeed(byte level, ReadOnlySpan<byte> securityAccessDataRecord)
    {
        if ((level & 1) == 0) throw new ArgumentException("Seed-request sub-function must be odd.", nameof(level));
        var buf = new byte[2 + securityAccessDataRecord.Length];
        buf[0] = (byte)UdsServiceId.SecurityAccess;
        buf[1] = level;
        securityAccessDataRecord.CopyTo(buf.AsSpan(2));
        return buf;
    }

    /// <summary>
    /// 为指定级别构建 sendKey 请求（级别必须为偶数：2、4、6……）。<br/>Build a sendKey request for the given level (must be even: 2, 4, 6, ...).
    /// </summary>
    /// <param name="level">安全访问级别（必须为偶数）。<br/>The security access level (must be even).</param>
    /// <param name="key">密钥字节序列。<br/>The key bytes.</param>
    /// <returns>sendKey 请求字节数组。<br/>The sendKey request byte array.</returns>
    public static byte[] BuildSendKey(byte level, ReadOnlySpan<byte> key)
    {
        if ((level & 1) == 1) throw new ArgumentException("Send-key sub-function must be even.", nameof(level));
        var buf = new byte[2 + key.Length];
        buf[0] = (byte)UdsServiceId.SecurityAccess;
        buf[1] = level;
        key.CopyTo(buf.AsSpan(2));
        return buf;
    }

    /// <summary>
    /// 执行完整的 requestSeed / sendKey 交换流程。<br/>Perform the full requestSeed / sendKey exchange.
    /// </summary>
    /// <remarks>
    /// 成功时返回 <see langword="true"/>。被拒绝时抛出 <see cref="NegativeResponseException"/>。<br/>Returns <see langword="true"/> on success. Throws <see cref="NegativeResponseException"/> on rejection.
    /// </remarks>
    /// <param name="client">UDS 异步客户端。<br/>The UDS async client.</param>
    /// <param name="requestSeedLevel">requestSeed 子功能级别（必须为奇数）。<br/>The requestSeed sub-function level (must be odd).</param>
    /// <param name="seedToKey">将种子转换为密钥的委托（seed → key）。<br/>A delegate that transforms the seed into the key (seed -> key).</param>
    /// <param name="cancellationToken">取消令牌。<br/>Cancellation token.</param>
    /// <returns>解锁成功时返回 <see langword="true"/>。<br/><see langword="true"/> when unlocked successfully.</returns>
    public static async Task<bool> UnlockAsync(
        IAsyncUdsClient client,
        byte requestSeedLevel,
        Func<byte[], byte[]> seedToKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(seedToKey);
        if ((requestSeedLevel & 1) == 0)
            throw new ArgumentException("Seed-request sub-function must be odd.", nameof(requestSeedLevel));

        var seedResp = await client.SendRequestAsync(BuildRequestSeed(requestSeedLevel), null, cancellationToken).ConfigureAwait(false);
        if (seedResp.Length < 2 || seedResp.Span[0] != 0x67 || seedResp.Span[1] != requestSeedLevel)
            throw new ProtocolException("Unexpected SecurityAccess seed response.");

        var seed = seedResp.Span[2..].ToArray();
        // An all-zero seed means the ECU is already unlocked at this level.
        if (IsAllZero(seed)) return true;

        var key = seedToKey(seed);
        byte sendKeyLevel = (byte)(requestSeedLevel + 1);
        var keyResp = await client.SendRequestAsync(BuildSendKey(sendKeyLevel, key), null, cancellationToken).ConfigureAwait(false);
        if (keyResp.Length < 2 || keyResp.Span[0] != 0x67 || keyResp.Span[1] != sendKeyLevel)
            throw new ProtocolException("Unexpected SecurityAccess key response.");
        return true;
    }

    /// <summary>
    /// 执行支持 OEM requestSeed 参数记录和自定义 sendKey 载荷的 SecurityAccess 解锁流程。<br/>
    /// Perform a SecurityAccess unlock flow with OEM requestSeed parameters and custom sendKey payload.
    /// </summary>
    /// <param name="client">UDS 异步客户端。<br/>The UDS async client.</param>
    /// <param name="requestSeedLevel">requestSeed 子功能级别（必须为奇数）。<br/>The requestSeed sub-function level (must be odd).</param>
    /// <param name="requestSeedParameterRecord">requestSeed 附加参数记录。<br/>Additional requestSeed parameter record.</param>
    /// <param name="keyParameterRecordBuilder">根据种子上下文构建 sendKey 参数记录。<br/>Builds the sendKey parameter record from seed context.</param>
    /// <param name="sendKeyLevel">可选的 sendKey 子功能级别；默认使用 requestSeedLevel + 1。<br/>Optional sendKey sub-function level; defaults to requestSeedLevel + 1.</param>
    /// <param name="cancellationToken">取消令牌。<br/>Cancellation token.</param>
    /// <returns>解锁成功时返回 <see langword="true"/>。<br/><see langword="true"/> when unlocked successfully.</returns>
    public static async Task<bool> UnlockAsync(
        IAsyncUdsClient client,
        byte requestSeedLevel,
        ReadOnlyMemory<byte> requestSeedParameterRecord,
        Func<SecurityAccessSeedContext, byte[]> keyParameterRecordBuilder,
        byte? sendKeyLevel = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(keyParameterRecordBuilder);
        if ((requestSeedLevel & 1) == 0)
            throw new ArgumentException("Seed-request sub-function must be odd.", nameof(requestSeedLevel));

        byte actualSendKeyLevel = sendKeyLevel ?? checked((byte)(requestSeedLevel + 1));
        byte[] requestSeedParameterRecordCopy = requestSeedParameterRecord.ToArray();

        var seedResp = await client.SendRequestAsync(
            BuildRequestSeed(requestSeedLevel, requestSeedParameterRecordCopy),
            null,
            cancellationToken).ConfigureAwait(false);
        if (seedResp.Length < 2 || seedResp.Span[0] != 0x67 || seedResp.Span[1] != requestSeedLevel)
            throw new ProtocolException("Unexpected SecurityAccess seed response.");

        var seed = seedResp.Span[2..].ToArray();
        // An all-zero seed means the ECU is already unlocked at this level.
        if (IsAllZero(seed)) return true;

        var context = new SecurityAccessSeedContext(
            requestSeedLevel,
            actualSendKeyLevel,
            seed,
            requestSeedParameterRecordCopy);
        var keyParameterRecord = keyParameterRecordBuilder(context);
        var keyResp = await client.SendRequestAsync(
            BuildSendKeyUnchecked(actualSendKeyLevel, keyParameterRecord),
            null,
            cancellationToken).ConfigureAwait(false);
        if (keyResp.Length < 2 || keyResp.Span[0] != 0x67 || keyResp.Span[1] != actualSendKeyLevel)
            throw new ProtocolException("Unexpected SecurityAccess key response.");
        return true;
    }

    /// <summary>
    /// 使用 CANoe SeedKey DLL 执行完整的 requestSeed / sendKey 交换流程。<br/>
    /// Performs the full requestSeed / sendKey exchange using a CANoe SeedKey DLL.
    /// </summary>
    /// <param name="client">UDS 异步客户端。<br/>The UDS async client.</param>
    /// <param name="requestSeedLevel">requestSeed 子功能级别（必须为奇数）。<br/>The requestSeed sub-function level (must be odd).</param>
    /// <param name="keyGenerator">CANoe SeedKey DLL 生成器。<br/>CANoe SeedKey DLL generator.</param>
    /// <param name="cancellationToken">取消令牌。<br/>Cancellation token.</param>
    /// <returns>解锁成功时返回 <see langword="true"/>。<br/><see langword="true"/> when unlocked successfully.</returns>
    public static Task<bool> UnlockAsync(
        IAsyncUdsClient client,
        byte requestSeedLevel,
        CanoeSeedKeyGenerator keyGenerator,
        CancellationToken cancellationToken = default)
        => UnlockAsync(client, requestSeedLevel, keyGenerator, (uint)requestSeedLevel, null, cancellationToken);

    /// <summary>
    /// 使用 CANoe SeedKey DLL 与指定调用选项执行完整的 requestSeed / sendKey 交换流程。<br/>
    /// Performs the full requestSeed / sendKey exchange using a CANoe SeedKey DLL and the specified call options.
    /// </summary>
    /// <param name="client">UDS 异步客户端。<br/>The UDS async client.</param>
    /// <param name="requestSeedLevel">requestSeed 子功能级别（必须为奇数）。<br/>The requestSeed sub-function level (must be odd).</param>
    /// <param name="keyGenerator">CANoe SeedKey DLL 生成器。<br/>CANoe SeedKey DLL generator.</param>
    /// <param name="keyOptions">CANoe SeedKey DLL 调用选项。<br/>CANoe SeedKey DLL call options.</param>
    /// <param name="cancellationToken">取消令牌。<br/>Cancellation token.</param>
    /// <returns>解锁成功时返回 <see langword="true"/>。<br/><see langword="true"/> when unlocked successfully.</returns>
    public static Task<bool> UnlockAsync(
        IAsyncUdsClient client,
        byte requestSeedLevel,
        CanoeSeedKeyGenerator keyGenerator,
        CanoeSeedKeyOptions? keyOptions,
        CancellationToken cancellationToken = default)
        => UnlockAsync(client, requestSeedLevel, keyGenerator, (uint)requestSeedLevel, keyOptions, cancellationToken);

    /// <summary>
    /// 使用 CANoe SeedKey DLL、显式安全等级与指定调用选项执行完整的 requestSeed / sendKey 交换流程。<br/>
    /// Performs the full requestSeed / sendKey exchange using a CANoe SeedKey DLL, explicit security level, and call options.
    /// </summary>
    /// <param name="client">UDS 异步客户端。<br/>The UDS async client.</param>
    /// <param name="requestSeedLevel">requestSeed 子功能级别（必须为奇数）。<br/>The requestSeed sub-function level (must be odd).</param>
    /// <param name="keyGenerator">CANoe SeedKey DLL 生成器。<br/>CANoe SeedKey DLL generator.</param>
    /// <param name="securityLevel">传给 CANoe DLL 的安全访问等级。<br/>Security access level passed to the CANoe DLL.</param>
    /// <param name="keyOptions">CANoe SeedKey DLL 调用选项。<br/>CANoe SeedKey DLL call options.</param>
    /// <param name="cancellationToken">取消令牌。<br/>Cancellation token.</param>
    /// <returns>解锁成功时返回 <see langword="true"/>。<br/><see langword="true"/> when unlocked successfully.</returns>
    public static Task<bool> UnlockAsync(
        IAsyncUdsClient client,
        byte requestSeedLevel,
        CanoeSeedKeyGenerator keyGenerator,
        uint securityLevel,
        CanoeSeedKeyOptions? keyOptions = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(keyGenerator);
        return UnlockAsync(client, requestSeedLevel, seed => keyGenerator.GenerateKey(seed, securityLevel, keyOptions), cancellationToken);
    }

    private static bool IsAllZero(ReadOnlySpan<byte> buf)
    {
        foreach (var b in buf) if (b != 0) return false;
        return buf.Length > 0;
    }

    private static byte[] BuildSendKeyUnchecked(byte level, ReadOnlySpan<byte> key)
    {
        var buf = new byte[2 + key.Length];
        buf[0] = (byte)UdsServiceId.SecurityAccess;
        buf[1] = level;
        key.CopyTo(buf.AsSpan(2));
        return buf;
    }
}
