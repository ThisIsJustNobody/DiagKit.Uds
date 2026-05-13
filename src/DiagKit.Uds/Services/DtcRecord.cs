namespace DiagKit.Uds.Services;

/// <summary>
/// 从 0x19/0x02 响应解码出的一条 DTC 记录。<br/>One DTC record decoded from a 0x19/0x02 response.
/// </summary>
public readonly record struct DtcRecord(uint DtcCode, DtcStatus Status)
{
    /// <summary>
    /// 将 24 位 DTC 码格式化为 SAE J2012 字符串（前缀字母 + 5 位十六进制数字 + 故障类型字节），例如 "P0123-FF"。<br/>Format the 24-bit DTC code as a SAE J2012 string (letter + 5 hex digits + failure-type byte), e.g. "P0123-FF".
    /// </summary>
    /// <remarks>
    /// 首字节的高两位用于选择系统字母（P/C/B/U）。<br/>The first byte's top two bits select the system letter (P/C/B/U).
    /// </remarks>
    /// <returns>SAE J2012 格式的 DTC 字符串。<br/>The DTC string in SAE J2012 format.</returns>
    public string FormatJ2012()
    {
        uint code = DtcCode & 0x00FFFFFF;
        byte b1 = (byte)((code >> 16) & 0xFF);
        byte b2 = (byte)((code >> 8) & 0xFF);
        byte ftb = (byte)(code & 0xFF);
        char prefix = ((b1 >> 6) & 0b11) switch
        {
            0b00 => 'P', 0b01 => 'C', 0b10 => 'B', _ => 'U',
        };
        int digit1 = (b1 >> 4) & 0b11;
        int digit2 = b1 & 0x0F;
        return $"{prefix}{digit1:X1}{digit2:X1}{b2:X2}-{ftb:X2}";
    }
}
