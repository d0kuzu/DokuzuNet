using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DokuzuNet.Networking.Message.Messages
{
    public record RpcMessage(uint ObjectId, ushort BehaviourId, ushort RpcId, byte[] Args) : IMessage;
}
