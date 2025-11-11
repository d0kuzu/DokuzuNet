using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DokuzuNet.Networking.Message
{
    public interface IMessage
    {
        /// <summary>
        /// Уникальный ID сообщения (авто-генерируется или вручную).
        /// </summary>
        ushort MessageId { get; }

        /// <summary>
        /// Канал доставки (Reliable, Unreliable, etc.)
        /// </summary>
        MessageChannel Channel { get; }
    }

    public enum MessageChannel : byte
    {
        ReliableOrdered,
        Unreliable
    }
}
