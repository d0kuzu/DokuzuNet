using DokuzuNet.Networking.Message;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DokuzuNet.Integration
{
    public record SpawnMessage(ushort PrefabId, uint NetworkId, uint OwnerId, float X, float Y, float Z) : IMessage;
}
