using System;
using System.Collections.Generic;
using System.Text;

namespace DiagKit.Uds.Services
{
    /// <summary>
    /// CommunicationControl 通信类型。<br/>CommunicationControl communication types.
    /// </summary>
    public enum CommunicationType: byte
    {
        /// <summary>
        /// 应用层通信<br/>Application layer communication
        /// </summary>
        App = 1,

        /// <summary>
        /// 网络通信<br/>Network communication
        /// </summary>
        Net = 2,

        /// <summary>
        /// 应用层和网络通信<br/>Application layer and network communication
        /// </summary>
        AppAndNet = 3,
    }
}
