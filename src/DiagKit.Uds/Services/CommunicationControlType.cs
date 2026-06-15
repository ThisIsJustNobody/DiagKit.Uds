namespace DiagKit.Uds.Services;

/// <summary>
/// CommunicationControl 子功能。<br/>CommunicationControl sub-functions.
/// </summary>
public enum CommunicationControlType : byte
{
    /// <summary>启用接收和发送。<br/>Enable Rx and Tx (0x00).</summary>
    EnableRxAndTx = 0x00,
    /// <summary>启用接收并禁用发送。<br/>Enable Rx and disable Tx (0x01).</summary>
    EnableRxAndDisableTx = 0x01,
    /// <summary>禁用接收并启用发送。<br/>Disable Rx and enable Tx (0x02).</summary>
    DisableRxAndEnableTx = 0x02,
    /// <summary>禁用接收和发送。<br/>Disable Rx and Tx (0x03).</summary>
    DisableRxAndTx = 0x03,
    /// <summary>带增强地址信息，启用接收并禁用发送。<br/>Enable Rx and disable Tx with enhanced address information (0x04).</summary>
    EnableRxAndDisableTxWithEnhancedAddressInformation = 0x04,
    /// <summary>带增强地址信息，启用接收和发送。<br/>Enable Rx and Tx with enhanced address information (0x05).</summary>
    EnableRxAndTxWithEnhancedAddressInformation = 0x05,
}
