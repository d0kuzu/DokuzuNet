using DokuzuNet.Core;
using DokuzuNet.Networking;
using DokuzuNet.Networking.Message;
using DokuzuNet.Networking.Packet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace DokuzuNet.Integration
{
    public abstract class NetworkBehaviour
    {
        public NetworkObject? NetworkObject { get; internal set; }
        public NetworkPlayer? Owner => NetworkObject?.Owner;
        public bool IsLocalOwned => NetworkObject?.IsLocalOwned ?? false;

        protected virtual void OnSpawn() { }
        protected virtual void OnDespawn() { }

        protected async ValueTask SendToServerAsync<T>(T message) where T : IMessage
        {
            await NetworkManager.Instance!.SendToServerAsync(message);
        }

        protected async ValueTask SendToAsync<T>(NetworkPlayer player, T message) where T : IMessage
        {
            await NetworkManager.Instance!.SendToAsync(player, message);
        }

        protected async ValueTask BroadcastAsync<T>(T message, bool includeLocal = true)
        {
            await NetworkManager.Instance!.BroadcastAsync(message, includeLocal);
        }




        private readonly Dictionary<ushort, Action<byte[]>> _syncVarSetters = new();
        private ushort _nextVarId = 1;

        // === SyncVar ===
        protected SyncVar<T> CreateSyncVar<T>(T initialValue, Action<T>? onChanged = null) where T : struct
        {
            var varId = _nextVarId++;
            var syncVar = new SyncVar<T>(this, varId, initialValue, onChanged);

            _syncVarSetters[varId] = (data) =>
            {
                // Десериализация (пример для int/float, расширь)
                if (typeof(T) == typeof(int))
                {
                    syncVar.SetWithoutNotify(BitConverter.ToInt32(data));
                }
                else if (typeof(T) == typeof(float))
                {
                    syncVar.SetWithoutNotify(BitConverter.ToSingle(data));
                }
                // Добавь bool, Vector3, etc.
            };

            return syncVar;
        }

        internal async ValueTask SendSyncVarAsync<T>(ushort varId, T value) where T : struct
        {
            if (NetworkObject == null) return;

            // Сериализация (пример)
            byte[] rawValue = typeof(T) == typeof(int) ? BitConverter.GetBytes((int)(object)value)
                : typeof(T) == typeof(float) ? BitConverter.GetBytes((float)(object)value)
                : Array.Empty<byte>(); // Расширь

            var msg = new SyncVarMessage(NetworkObject.NetworkId, GetBehaviourId(), varId, rawValue);
            if (NetworkManager.Instance!.IsServer)
            {
                await BroadcastAsync(msg);
            }
            else
            {
                await SendToServerAsync(msg);
            }
        }

        private ushort GetBehaviourId()
        {
            // Присвой ID поведению (расширь NetworkObject для хранения ID behaviours)
            return 1; // Заглушка
        }

        internal void ApplySyncVar(ushort varId, byte[] value)
        {
            if (_syncVarSetters.TryGetValue(varId, out var setter))
                setter(value);
        }





        private readonly Dictionary<ushort, MethodInfo> _rpcMethods = new();
        private ushort _nextRpcId = 1;

        protected override void OnSpawn()
        {
            // Регистрация RPC методов
            var methods = GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            foreach (var method in methods)
            {
                if (method.GetCustomAttribute<ServerRpcAttribute>() != null || method.GetCustomAttribute<ClientRpcAttribute>() != null)
                {
                    var rpcId = _nextRpcId++;
                    _rpcMethods[rpcId] = method;
                }
            }
        }

        // Вызов ServerRpc
        protected async ValueTask CallServerRpc(string methodName, params object[] args)
        {
            if (!NetworkManager.Instance!.IsClient) return;

            var method = GetType().GetMethod(methodName);
            if (method?.GetCustomAttribute<ServerRpcAttribute>() == null) throw new InvalidOperationException("Not a ServerRpc.");

            var rpcId = GetRpcId(methodName); // Расширь для поиска ID по имени
            var argsData = SerializeArgs(args); // Реализуй сериализацию (ниже)

            var msg = new RpcMessage(NetworkObject!.NetworkId, GetBehaviourId(), rpcId, argsData);
            await SendToServerAsync(msg);
        }

        // Вызов ClientRpc
        protected async ValueTask CallClientRpc(string methodName, NetworkPlayer target, params object[] args)
        {
            if (!NetworkManager.Instance!.IsServer) return;

            var method = GetType().GetMethod(methodName);
            if (method?.GetCustomAttribute<ClientRpcAttribute>() == null) throw new InvalidOperationException("Not a ClientRpc.");

            var rpcId = GetRpcId(methodName);
            var argsData = SerializeArgs(args);

            var msg = new RpcMessage(NetworkObject!.NetworkId, GetBehaviourId(), rpcId, argsData);
            await SendToAsync(target, msg);
        }

        // Вызов ClientRpc на всех
        protected async ValueTask BroadcastClientRpc(string methodName, params object[] args)
        {
            if (!NetworkManager.Instance!.IsServer) return;

            var method = GetType().GetMethod(methodName);
            if (method?.GetCustomAttribute<ClientRpcAttribute>() == null) throw new InvalidOperationException("Not a ClientRpc.");

            var rpcId = GetRpcId(methodName);
            var argsData = SerializeArgs(args);

            var msg = new RpcMessage(NetworkObject!.NetworkId, GetBehaviourId(), rpcId, argsData);
            await BroadcastAsync(msg);
        }

        private ushort GetRpcId(string methodName)
        {
            // Реализуй поиск ID по имени (или используй хеш)
            return 1; // Заглушка
        }

        private byte[] SerializeArgs(object[] args)
        {
            using var writer = new PacketWriter();
            writer.WriteUShort((ushort)args.Length);
            foreach (var arg in args)
            {
                // Пример сериализации
                if (arg is int i) writer.WriteInt(i);
                else if (arg is float f) writer.WriteFloat(f);
                else if (arg is string s) writer.WriteString(s);
                // Расширь для других типов
            }
            return writer.GetBuffer().ToArray();
        }

        internal void InvokeRpc(ushort rpcId, byte[] argsData)
        {
            if (_rpcMethods.TryGetValue(rpcId, out var method))
            {
                var parameters = DeserializeArgs(method.GetParameters(), argsData);
                method.Invoke(this, parameters);
            }
        }

        private object[] DeserializeArgs(ParameterInfo[] paramInfos, byte[] argsData)
        {
            var reader = new PacketReader(new ReadOnlyMemory<byte>(argsData));
            var count = reader.ReadUShort();
            var args = new object[count];

            for (int i = 0; i < count; i++)
            {
                var type = paramInfos[i].ParameterType;
                args[i] = type switch
                {
                    Type t when t == typeof(int) => reader.ReadInt(),
                    Type t when t == typeof(float) => reader.ReadFloat(),
                    Type t when t == typeof(string) => reader.ReadString(),
                    _ => throw new NotSupportedException($"Type {type} not supported.")
                };
            }
            return args;
        }
    }
}
