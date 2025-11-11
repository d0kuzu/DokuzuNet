using DokuzuNet.Networking.Packet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DokuzuNet.Networking.Message
{
    public class MessageRegistry
    {
        private readonly Dictionary<Type, ushort> _typeToId = new();
        private readonly Dictionary<ushort, Type> _idToType = new();
        private readonly Dictionary<Type, Delegate> _handlers = new();
        private ushort _nextId = 1;

        public void Register<T>() where T : IMessage, new()
        {
            var type = typeof(T);
            if (_typeToId.ContainsKey(type)) return;

            var id = _nextId++;
            _typeToId[type] = id;
            _idToType[id] = type;
        }

        public void On<T>(Action<NetworkPlayer, T> handler) where T : IMessage
        {
            var type = typeof(T);
            if (_handlers.TryGetValue(type, out var existing))
            {
                _handlers[type] = Delegate.Combine(existing, handler);
            }
            else
            {
                _handlers[type] = handler;
            }
        }

        public ReadOnlyMemory<byte> Serialize<T>(T message) where T : IMessage
        {
            using var writer = new Networking.Packet.PacketWriter();
            writer.WriteUShort(_typeToId[typeof(T)]);

            if (message is ChatMessage chat)
            {
                writer.WriteString(chat.Text);
            }

            return writer.GetBuffer();
        }

        public IMessage? Deserialize(ReadOnlyMemory<byte> data)
        {
            var reader = new Networking.Packet.PacketReader(data);
            var id = reader.ReadUShort();

            if (!_idToType.TryGetValue(id, out var type)) return null;

            return type switch
            {
                var t when t == typeof(ChatMessage) => new ChatMessage(reader.ReadString()) as IMessage,
                _ => null
            };
        }

        internal void Dispatch(IMessage message, NetworkPlayer player)
        {
            var type = message.GetType();
            if (_handlers.TryGetValue(type, out var handler))
            {
                handler.DynamicInvoke(player, message);
            }
        }
    }
}
