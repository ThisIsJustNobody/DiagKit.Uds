using System;

namespace DiagKit.Uds.DoCan;

/// <summary>
/// 表示单个 CAN 或 CAN-FD 帧。<br/>Represents a single CAN or CAN-FD frame.
/// </summary>
/// <remarks>
/// 这是一个包装 CAN 标识符和载荷的值类型。载荷通过只读 API 暴露，但底层内存仍由调用方持有，
/// 除非使用 <see cref="CreateCopy"/> 创建帧。CAN 帧最多承载 8 字节数据；CAN-FD 帧最多承载 64 字节。
/// DLC（数据长度码）是总线上的 4 位编码值；使用 <see cref="LengthToDlc"/> 和 <see cref="DlcToLength"/>
/// 在 DLC 与实际字节长度之间转换。<br/>
/// This is a value-type wrapper for a CAN identifier and payload. The payload is exposed through a
/// read-only API, but the underlying memory is still owned by the caller unless the frame was created
/// with <see cref="CreateCopy"/>. CAN frames carry up to 8 bytes of data; CAN-FD frames carry up to
/// 64 bytes. The DLC (Data Length Code) is the 4-bit nibble on the wire; use <see cref="LengthToDlc"/>
/// and <see cref="DlcToLength"/> to convert between DLC and byte length.
/// </remarks>
public readonly record struct CanFrame
{
    /// <summary>
    /// 经典 CAN 的最大数据长度（8 字节）。<br/>Maximum data length for classic CAN (8 bytes).
    /// </summary>
    public const int ClassicMaxLength = 8;

    /// <summary>
    /// CAN-FD 的最大数据长度（64 字节）。<br/>Maximum data length for CAN-FD (64 bytes).
    /// </summary>
    public const int FdMaxLength = 64;

    /// <summary>
    /// CAN 标识符（11 位标准或 29 位扩展）。<br/>CAN identifier (11-bit standard or 29-bit extended).
    /// </summary>
    public uint CanId { get; init; }

    /// <summary>
    /// CAN 标识符是否为扩展帧（29 位）。<br/>Whether the CAN identifier is extended (29-bit).
    /// </summary>
    public bool ExtendedId { get; init; }

    /// <summary>
    /// 是否为 CAN-FD 帧。<br/>Whether this is a CAN-FD frame.
    /// </summary>
    public bool FdFlag { get; init; }

    /// <summary>
    /// 是否启用比特率切换（仅 CAN-FD）。<br/>Whether the bit rate switch is enabled (CAN-FD only).
    /// </summary>
    public bool BitRateSwitch { get; init; }

    /// <summary>
    /// 帧数据（CAN-FD 最多 64 字节，经典 CAN 最多 8 字节）。
    /// 底层内存的稳定性由所有者负责维护。需要帧持有不可变快照时请使用 <see cref="CreateCopy"/>。<br/>
    /// Frame data (up to 64 bytes for CAN-FD, 8 for classic CAN). The memory owner remains
    /// responsible for keeping the underlying bytes stable. Use <see cref="CreateCopy"/> when
    /// the frame must own an immutable snapshot.
    /// </summary>
    public ReadOnlyMemory<byte> Data { get; init; }

    /// <summary>
    /// 总线上的数据长度码（0-15）。<br/>Data length code on the wire (0-15).
    /// </summary>
    public byte DataLengthCode => LengthToDlc(Data.Length);

    /// <summary>
    /// 将字节长度转换为对应的 DLC（数据长度码）。<br/>Convert a byte length to its corresponding DLC (Data Length Code).
    /// </summary>
    /// <param name="length">字节长度（0..64）。<br/>Byte length (0..64).</param>
    /// <returns>对应的 DLC 值（0-15）。<br/>The corresponding DLC value (0-15).</returns>
    /// <exception cref="ArgumentOutOfRangeException">长度不在 0..64 范围内。Length is not in the 0..64 range.</exception>
    public static byte LengthToDlc(int length) => length switch
    {
        < 0 => throw new ArgumentOutOfRangeException(nameof(length), length, "Length must be 0..64."),
        <= 8 => (byte)length,
        <= 12 => 9,
        <= 16 => 10,
        <= 20 => 11,
        <= 24 => 12,
        <= 32 => 13,
        <= 48 => 14,
        <= 64 => 15,
        _ => throw new ArgumentOutOfRangeException(nameof(length), length, "Length must be ≤ 64."),
    };

    /// <summary>
    /// 将 DLC（0-15）转换为对应的总线字节长度。<br/>Convert a DLC (0-15) to the corresponding byte length on the wire.
    /// </summary>
    /// <param name="dlc">数据长度码（0..15）。<br/>Data length code (0..15).</param>
    /// <returns>对应的字节长度。<br/>The corresponding byte length.</returns>
    /// <exception cref="ArgumentOutOfRangeException">DLC 不在 0..15 范围内。DLC is not in the 0..15 range.</exception>
    public static byte DlcToLength(byte dlc) => dlc switch
    {
        <= 8 => dlc,
        9 => 12,
        10 => 16,
        11 => 20,
        12 => 24,
        13 => 32,
        14 => 48,
        15 => 64,
        _ => throw new ArgumentOutOfRangeException(nameof(dlc), dlc, "DLC must be 0..15."),
    };

    /// <summary>
    /// 使用给定的 CAN ID 和载荷创建 CAN 帧。返回的帧引用 <paramref name="data"/>；
    /// 需要持有载荷独立副本时请使用 <see cref="CreateCopy"/>。<br/>
    /// Create a CAN frame with the given ID and payload. The returned frame references
    /// <paramref name="data"/>; use <see cref="CreateCopy"/> to take ownership of a payload snapshot.
    /// </summary>
    /// <param name="canId">CAN 标识符。<br/>CAN identifier.</param>
    /// <param name="data">帧载荷数据。<br/>Frame payload data.</param>
    /// <param name="fd">是否为 CAN-FD 帧。<br/>Whether this is a CAN-FD frame.</param>
    /// <param name="brs">是否启用比特率切换（仅 CAN-FD）。<br/>Whether bit rate switch is enabled (CAN-FD only).</param>
    /// <param name="extendedId">CAN ID 是否为 29 位扩展标识符。<br/>Whether the CAN ID is a 29-bit extended identifier.</param>
    /// <returns>一个经过验证的 <see cref="CanFrame"/> 实例。<br/>A validated <see cref="CanFrame"/> instance.</returns>
    public static CanFrame Create(uint canId, ReadOnlyMemory<byte> data, bool fd = false, bool brs = false, bool extendedId = false)
    {
        var frame = new CanFrame
        {
            CanId = canId,
            ExtendedId = extendedId,
            FdFlag = fd,
            BitRateSwitch = brs,
            Data = data,
        };
        frame.Validate();
        return frame;
    }

    /// <summary>
    /// 创建 CAN 帧并持有给定载荷的独立副本。<br/>Create a CAN frame with an owned copy of the given payload.
    /// </summary>
    /// <param name="canId">CAN 标识符。<br/>CAN identifier.</param>
    /// <param name="data">帧载荷数据（将被复制）。<br/>Frame payload data (will be copied).</param>
    /// <param name="fd">是否为 CAN-FD 帧。<br/>Whether this is a CAN-FD frame.</param>
    /// <param name="brs">是否启用比特率切换（仅 CAN-FD）。<br/>Whether bit rate switch is enabled (CAN-FD only).</param>
    /// <param name="extendedId">CAN ID 是否为 29 位扩展标识符。<br/>Whether the CAN ID is a 29-bit extended identifier.</param>
    /// <returns>一个持有载荷独立副本的 <see cref="CanFrame"/> 实例。<br/>A <see cref="CanFrame"/> instance that owns a copy of the payload.</returns>
    public static CanFrame CreateCopy(uint canId, ReadOnlyMemory<byte> data, bool fd = false, bool brs = false, bool extendedId = false)
        => Create(canId, data.ToArray(), fd, brs, extendedId);

    /// <summary>
    /// 验证当前帧的标识符、标志位和载荷长度。<br/>Validate the frame identifier, flags, and payload length.
    /// </summary>
    public void Validate() => Validate(CanId, Data.Length, FdFlag, BitRateSwitch, ExtendedId);

    /// <summary>
    /// 在不创建帧的情况下验证 CAN 标识符、标志位和载荷长度。<br/>Validate CAN identifier, flags, and payload length without creating a frame.
    /// </summary>
    /// <param name="canId">CAN 标识符。<br/>CAN identifier.</param>
    /// <param name="dataLength">载荷数据长度。<br/>Payload data length.</param>
    /// <param name="fd">是否为 CAN-FD 帧。<br/>Whether this is a CAN-FD frame.</param>
    /// <param name="brs">是否启用比特率切换（仅 CAN-FD）。<br/>Whether bit rate switch is enabled (CAN-FD only).</param>
    /// <param name="extendedId">CAN ID 是否为 29 位扩展标识符。<br/>Whether the CAN ID is a 29-bit extended identifier.</param>
    public static void Validate(uint canId, int dataLength, bool fd = false, bool brs = false, bool extendedId = false)
    {
        if (extendedId)
        {
            if (canId > 0x1FFFFFFF)
                throw new ArgumentOutOfRangeException(nameof(canId), canId, "Extended CAN ID must be <= 0x1FFFFFFF.");
        }
        else if (canId > 0x7FF)
        {
            throw new ArgumentOutOfRangeException(nameof(canId), canId, "Standard CAN ID must be <= 0x7FF.");
        }

        if (dataLength < 0)
            throw new ArgumentOutOfRangeException(nameof(dataLength), dataLength, "Data length must be non-negative.");

        int maxLength = fd ? FdMaxLength : ClassicMaxLength;
        if (dataLength > maxLength)
            throw new ArgumentOutOfRangeException(nameof(dataLength), dataLength, $"Data length must be <= {maxLength} bytes.");

        if (brs && !fd)
            throw new ArgumentException("BitRateSwitch requires FdFlag to be true.", nameof(brs));
    }
}
