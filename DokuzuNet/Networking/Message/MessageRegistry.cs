using DokuzuNet.Integration;
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

        public void Register<T>() where T : IMessage
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
            using var writer = new PacketWriter();
            writer.WriteUShort(_typeToId[typeof(T)]);

            switch (message)
            {
                case ChatMessage chat:
                    writer.WriteString(chat.Text);
                    break;

                case SpawnMessage spawn:
                    writer.WriteUShort(spawn.PrefabId);
                    writer.WriteUInt(spawn.NetworkId);
                    writer.WriteUInt(spawn.OwnerId);
                    writer.WriteFloat(spawn.X);
                    writer.WriteFloat(spawn.Y);
                    writer.WriteFloat(spawn.Z);
                    break;

                case SyncVarMessage sync:
                    writer.WriteUInt(sync.ObjectId);
                    writer.WriteUShort(sync.BehaviourId);
                    writer.WriteUShort(sync.VarId);
                    writer.WriteInt(sync.Value.Length);
                    writer.WriteBytes(sync.Value);
                    break;

                case RpcMessage rpc:
                    writer.WriteUInt(rpc.ObjectId);
                    writer.WriteUShort(rpc.BehaviourId);
                    writer.WriteUShort(rpc.RpcId);
                    writer.WriteInt(rpc.Args.Length);
                    writer.WriteBytes(rpc.Args);
                    break;
            }

            return writer.GetBuffer();
        }

        public IMessage? Deserialize(ReadOnlyMemory<byte> data)
        {
            var reader = new PacketReader(data);
            var id = reader.ReadUShort();

            if (!_idToType.TryGetValue(id, out var type)) return null;

            return type switch
            {
                // ChatMessage
                _ when type == typeof(ChatMessage) =>
                    new ChatMessage(reader.ReadString()),

                // SpawnMessage
                _ when type == typeof(SpawnMessage) =>
                    new SpawnMessage(
                        reader.ReadUShort(),
                        reader.ReadUInt(),
                        reader.ReadUInt(),
                        reader.ReadFloat(),
                        reader.ReadFloat(),
                        reader.ReadFloat()
                    ),

                // SyncVarMessage
                _ when type == typeof(SyncVarMessage) =>
                    new SyncVarMessage(
                        reader.ReadUInt(),
                        reader.ReadUShort(),
                        reader.ReadUShort(),
                        reader.ReadBytes(reader.ReadInt()).ToArray()
                    ),

                // RpcMessage
                _ when type == typeof(RpcMessage) =>
                    new RpcMessage(
                        reader.ReadUInt(),
                        reader.ReadUShort(),
                        reader.ReadUShort(),
                        reader.ReadBytes(reader.ReadInt()).ToArray()
                    ),

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
