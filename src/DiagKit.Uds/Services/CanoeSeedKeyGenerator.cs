using System;
using System.Runtime.InteropServices;

namespace DiagKit.Uds.Services;

/// <summary>
/// 使用 CANoe SeedKey DLL 的 GenerateKeyEx / GenerateKeyExOpt 入口生成 UDS SecurityAccess key。<br/>
/// Generates UDS SecurityAccess keys through a CANoe SeedKey DLL GenerateKeyEx / GenerateKeyExOpt entry point.
/// </summary>
public sealed class CanoeSeedKeyGenerator : IDisposable
{
    private const string GenerateKeyExEntryPoint = "GenerateKeyEx";
    private const string GenerateKeyExOptEntryPoint = "GenerateKeyExOpt";

    private readonly GenerateKeyExCallback? _generateKeyEx;
    private readonly GenerateKeyExOptCallback? _generateKeyExOpt;
    private readonly IntPtr _libraryHandle;
    private readonly bool _ownsLibraryHandle;
    private bool _disposed;

    /// <summary>
    /// 从指定 DLL 路径加载 CANoe SeedKey 生成器。<br/>Loads a CANoe SeedKey generator from the specified DLL path.
    /// </summary>
    /// <remarks>
    /// DLL 架构必须匹配当前进程架构；不匹配时由运行时抛出 <see cref="BadImageFormatException"/>。<br/>
    /// The DLL architecture must match the current process architecture; the runtime throws <see cref="BadImageFormatException"/> on mismatch.
    /// </remarks>
    /// <param name="dllPath">CANoe SeedKey DLL 路径。<br/>Path to the CANoe SeedKey DLL.</param>
    public CanoeSeedKeyGenerator(string dllPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dllPath);

        _libraryHandle = NativeLibrary.Load(dllPath);
        _ownsLibraryHandle = true;

        try
        {
            if (NativeLibrary.TryGetExport(_libraryHandle, GenerateKeyExEntryPoint, out var generateKeyExAddress))
            {
                _generateKeyEx = Marshal.GetDelegateForFunctionPointer<GenerateKeyExCallback>(generateKeyExAddress);
            }

            if (NativeLibrary.TryGetExport(_libraryHandle, GenerateKeyExOptEntryPoint, out var generateKeyExOptAddress))
            {
                _generateKeyExOpt = Marshal.GetDelegateForFunctionPointer<GenerateKeyExOptCallback>(generateKeyExOptAddress);
            }

            if (_generateKeyEx is null && _generateKeyExOpt is null)
                throw new EntryPointNotFoundException("The DLL does not export GenerateKeyEx or GenerateKeyExOpt.");
        }
        catch
        {
            NativeLibrary.Free(_libraryHandle);
            throw;
        }
    }

    internal CanoeSeedKeyGenerator(GenerateKeyExCallback? generateKeyEx, GenerateKeyExOptCallback? generateKeyExOpt)
    {
        _generateKeyEx = generateKeyEx;
        _generateKeyExOpt = generateKeyExOpt;
        if (_generateKeyEx is null && _generateKeyExOpt is null)
            throw new EntryPointNotFoundException("The DLL does not export GenerateKeyEx or GenerateKeyExOpt.");
    }

    /// <summary>
    /// 根据当前进程架构从 x86/x64 DLL 路径中选择并加载一个 CANoe SeedKey 生成器。<br/>
    /// Selects and loads a CANoe SeedKey generator from x86/x64 DLL paths according to the current process architecture.
    /// </summary>
    /// <param name="x86Path">x86 DLL 路径。<br/>Path to the x86 DLL.</param>
    /// <param name="x64Path">x64 DLL 路径。<br/>Path to the x64 DLL.</param>
    /// <returns>已加载的生成器。<br/>The loaded generator.</returns>
    public static CanoeSeedKeyGenerator LoadForCurrentProcess(string? x86Path, string? x64Path)
    {
        var path = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X86 => x86Path,
            Architecture.X64 => x64Path,
            var architecture => throw new PlatformNotSupportedException($"CANoe SeedKey DLL loading is not configured for {architecture}."),
        };

        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException($"A CANoe SeedKey DLL path for {RuntimeInformation.ProcessArchitecture} is required.");

        return new CanoeSeedKeyGenerator(path);
    }

    /// <summary>
    /// 使用默认选项生成 key；生成失败时抛出异常。<br/>Generates a key with default options; throws when generation fails.
    /// </summary>
    /// <param name="seed">ECU 返回的 seed。<br/>Seed returned by the ECU.</param>
    /// <param name="securityLevel">CANoe DLL 接收的安全访问等级。<br/>Security access level passed to the CANoe DLL.</param>
    /// <returns>生成的 key 字节。<br/>The generated key bytes.</returns>
    public byte[] GenerateKey(ReadOnlySpan<byte> seed, uint securityLevel)
        => GenerateKey(seed, securityLevel, null);

    /// <summary>
    /// 使用指定选项生成 key；生成失败时抛出异常。<br/>Generates a key with the specified options; throws when generation fails.
    /// </summary>
    /// <param name="seed">ECU 返回的 seed。<br/>Seed returned by the ECU.</param>
    /// <param name="securityLevel">CANoe DLL 接收的安全访问等级。<br/>Security access level passed to the CANoe DLL.</param>
    /// <param name="options">CANoe SeedKey DLL 调用选项。<br/>CANoe SeedKey DLL call options.</param>
    /// <returns>生成的 key 字节。<br/>The generated key bytes.</returns>
    public byte[] GenerateKey(ReadOnlySpan<byte> seed, uint securityLevel, CanoeSeedKeyOptions? options)
    {
        var result = TryGenerateKey(seed, securityLevel, options, out var key);
        if (result != CanoeKeyGenerationResult.Ok)
            throw new InvalidOperationException($"CANoe SeedKey DLL returned {result}.");
        return key;
    }

    /// <summary>
    /// 使用默认选项尝试生成 key。<br/>Attempts to generate a key with default options.
    /// </summary>
    /// <param name="seed">ECU 返回的 seed。<br/>Seed returned by the ECU.</param>
    /// <param name="securityLevel">CANoe DLL 接收的安全访问等级。<br/>Security access level passed to the CANoe DLL.</param>
    /// <param name="key">生成成功时返回 key；失败时为空数组。<br/>Returns the key on success, or an empty array on failure.</param>
    /// <returns>CANoe DLL 返回的结果码。<br/>The result code returned by the CANoe DLL.</returns>
    public CanoeKeyGenerationResult TryGenerateKey(ReadOnlySpan<byte> seed, uint securityLevel, out byte[] key)
        => TryGenerateKey(seed, securityLevel, null, out key);

    /// <summary>
    /// 使用指定选项尝试生成 key。<br/>Attempts to generate a key with the specified options.
    /// </summary>
    /// <param name="seed">ECU 返回的 seed。<br/>Seed returned by the ECU.</param>
    /// <param name="securityLevel">CANoe DLL 接收的安全访问等级。<br/>Security access level passed to the CANoe DLL.</param>
    /// <param name="options">CANoe SeedKey DLL 调用选项。<br/>CANoe SeedKey DLL call options.</param>
    /// <param name="key">生成成功时返回 key；失败时为空数组。<br/>Returns the key on success, or an empty array on failure.</param>
    /// <returns>CANoe DLL 返回的结果码。<br/>The result code returned by the CANoe DLL.</returns>
    public CanoeKeyGenerationResult TryGenerateKey(
        ReadOnlySpan<byte> seed,
        uint securityLevel,
        CanoeSeedKeyOptions? options,
        out byte[] key)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var callOptions = (options ?? new CanoeSeedKeyOptions()).Clone();
        callOptions.Validate();

        var seedArray = seed.ToArray();
        var variant = ToNullTerminated(callOptions.Variant);
        var algorithmOptions = ToNullTerminated(callOptions.AlgorithmOptions);
        var keyBufferSize = ResolveKeyBufferSize(seedArray.Length, callOptions.KeyBufferSize);
        var keyBuffer = new byte[keyBufferSize];
        var callbackKind = ResolveCallbackKind(callOptions);

        uint actualKeySize;
        var result = callbackKind == CallbackKind.GenerateKeyEx
            ? _generateKeyEx!(seedArray, (uint)seedArray.Length, securityLevel, variant, keyBuffer, (uint)keyBuffer.Length, out actualKeySize)
            : _generateKeyExOpt!(seedArray, (uint)seedArray.Length, securityLevel, variant, algorithmOptions, keyBuffer, (uint)keyBuffer.Length, out actualKeySize);

        if (result != CanoeKeyGenerationResult.Ok)
        {
            key = [];
            return result;
        }

        if (actualKeySize > keyBuffer.Length)
            throw new InvalidOperationException($"CANoe SeedKey DLL reported key size {actualKeySize}, but the buffer size is {keyBuffer.Length}.");

        key = keyBuffer.AsSpan(0, (int)actualKeySize).ToArray();
        return result;
    }

    /// <summary>
    /// 释放已加载的 native DLL 句柄。<br/>Releases the loaded native DLL handle.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_ownsLibraryHandle)
            NativeLibrary.Free(_libraryHandle);
    }

    private CallbackKind ResolveCallbackKind(CanoeSeedKeyOptions options)
        => options.Method switch
        {
            CanoeSeedKeyMethod.PreferGenerateKeyEx => ResolvePreferGenerateKeyEx(options.HasAlgorithmOptions),
            CanoeSeedKeyMethod.PreferGenerateKeyExOpt => ResolvePreferGenerateKeyExOpt(options.HasAlgorithmOptions),
            CanoeSeedKeyMethod.GenerateKeyExOnly => RequireGenerateKeyEx(),
            CanoeSeedKeyMethod.GenerateKeyExOptOnly => RequireGenerateKeyExOpt(),
            _ => throw new ArgumentOutOfRangeException(nameof(options), options.Method, "Invalid CANoe SeedKey method."),
        };

    private CallbackKind ResolvePreferGenerateKeyEx(bool hasAlgorithmOptions)
    {
        if (hasAlgorithmOptions)
            return RequireGenerateKeyExOpt();
        if (_generateKeyEx is not null)
            return CallbackKind.GenerateKeyEx;
        return RequireGenerateKeyExOpt();
    }

    private CallbackKind ResolvePreferGenerateKeyExOpt(bool hasAlgorithmOptions)
    {
        if (_generateKeyExOpt is not null)
            return CallbackKind.GenerateKeyExOpt;
        if (hasAlgorithmOptions)
            return RequireGenerateKeyExOpt();
        return RequireGenerateKeyEx();
    }

    private CallbackKind RequireGenerateKeyEx()
    {
        if (_generateKeyEx is null)
            throw new EntryPointNotFoundException("The DLL does not export GenerateKeyEx.");
        return CallbackKind.GenerateKeyEx;
    }

    private CallbackKind RequireGenerateKeyExOpt()
    {
        if (_generateKeyExOpt is null)
            throw new EntryPointNotFoundException("The DLL does not export GenerateKeyExOpt.");
        return CallbackKind.GenerateKeyExOpt;
    }

    private static int ResolveKeyBufferSize(int seedLength, int configuredSize)
        => configuredSize == 0 ? seedLength : configuredSize;

    private static byte[] ToNullTerminated(byte[]? bytes)
    {
        if (bytes is null || bytes.Length == 0)
            return [0];
        var copy = new byte[bytes[^1] == 0 ? bytes.Length : bytes.Length + 1];
        bytes.CopyTo(copy, 0);
        return copy;
    }

    private enum CallbackKind
    {
        GenerateKeyEx,
        GenerateKeyExOpt,
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate CanoeKeyGenerationResult GenerateKeyExCallback(
        [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] byte[] seed,
        uint seedSize,
        uint securityLevel,
        [MarshalAs(UnmanagedType.LPArray)] byte[] variant,
        [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 5)] byte[] key,
        uint keySize,
        out uint actualKeySize);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate CanoeKeyGenerationResult GenerateKeyExOptCallback(
        [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] byte[] seed,
        uint seedSize,
        uint securityLevel,
        [MarshalAs(UnmanagedType.LPArray)] byte[] variant,
        [MarshalAs(UnmanagedType.LPArray)] byte[] algorithmOptions,
        [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 6)] byte[] key,
        uint keySize,
        out uint actualKeySize);
}
