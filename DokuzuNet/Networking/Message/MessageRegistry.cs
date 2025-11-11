using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DokuzuNet.Networking.Message
{
    public class MessageRegistry
    {
        private readonly Dictionary<Type, Action<object, NetworkPlayer>> _handlers = new();
        private readonly Dictionary<Type, ushort> _typeToId = new();
        private readonly Dictionary<ushort, Type> _idToType = new();
        private ushort _nextId = 1;

        public void Register<T>() where T : IMessage
        {
            var type = typeof(T);
            var id = _nextId++;
            _typeToId[type] = id;
            _idToType[id] = type;
        }

        public void On<T>(Action<NetworkPlayer, T> handler) where T : IMessage
        {
            var del = (Action<object, NetworkPlayer>)((obj, player) => handler(player, (T)obj));
            _handlers[typeof(T)] = del;
        }

        public ReadOnlyMemory<byte> Serialize<T>(T msg) where T : IMessage
        {
            // PacketWriter + MessagePack
            return default;
        }

        public object? Deserialize(ReadOnlyMemory<byte> data)
        {
            // PacketReader + MessagePack
            return null;
        }

        internal void Dispatch(object msg, NetworkPlayer player)
        {
            if (_handlers.TryGetValue(msg.GetType(), out var handler))
                handler(msg, player);
        }

        internal Action<NetworkPlayer, T>? GetHandler<T>() where T : IMessage => null;
    }
}
