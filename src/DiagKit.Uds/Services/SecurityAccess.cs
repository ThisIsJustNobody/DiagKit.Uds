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
/// 调用方提供种子到密钥的转换委托，不执行任何动态编译。<br/>The caller supplies a seed-to-key transformation delegate; no dynamic compilation is performed.
/// </remarks>
public static class SecurityAccess
{
    /// <summary>
    /// 为指定级别构建 requestSeed 请求（级别必须为奇数：1、3、5……）。<br/>Build a requestSeed request for the given level (must be odd: 1, 3, 5, ...).
    /// </summary>
    /// <param name="level">安全访问级别（必须为奇数）。<br/>The security access level (must be odd).</param>
    /// <returns>requestSeed 请求字节数组。<br/>The requestSeed request byte array.</returns>
    public static byte[] BuildRequestSeed(byte level)
    {
        if ((level & 1) == 0) throw new ArgumentException("Seed-request sub-function must be odd.", nameof(level));
        return [(byte)UdsServiceId.SecurityAccess, level];
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

    private static bool IsAllZero(ReadOnlySpan<byte> buf)
    {
        foreach (var b in buf) if (b != 0) return false;
        return buf.Length > 0;
    }
}
