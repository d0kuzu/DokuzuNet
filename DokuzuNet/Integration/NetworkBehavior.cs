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

        // === SyncVar ===
        private readonly Dictionary<ushort, Action<byte[]>> _syncVarSetters = new();
        private ushort _nextVarId = 1;

        protected SyncVar<T> CreateSyncVar<T>(T initialValue, Action<T>? onChanged = null) where T : struct
        {
            var varId = _nextVarId++;
            var syncVar = new SyncVar<T>(this, varId, initialValue, onChanged);

            _syncVarSetters[varId] = data =>
            {
                syncVar.SetFromBytes(data);
            };

            return syncVar;
        }

        internal async ValueTask SendSyncVarAsync<T>(ushort varId, T value) where T : struct
        {
            if (NetworkObject == null) return;

            byte[] rawValue = typeof(T) switch
            {
                var t when t == typeof(int) => BitConverter.GetBytes((int)(object)value),
                var t when t == typeof(float) => BitConverter.GetBytes((float)(object)value),
                var t when t == typeof(uint) => BitConverter.GetBytes((uint)(object)value),
                _ => Array.Empty<byte>()
            };

            var msg = new SyncVarMessage(NetworkObject.NetworkId, GetBehaviourId(), varId, rawValue);

            if (NetworkManager.Instance!.IsServer)
            {
                await NetworkManager.Instance.BroadcastAsync(msg);
            }
            else
            {
                await NetworkManager.Instance.SendToServerAsync(msg);
            }
        }

        internal void ApplySyncVar(ushort varId, byte[] value)
        {
            if (_syncVarSetters.TryGetValue(varId, out var setter))
            {
                setter(value);
            }
        }

        // === RPC ===
        private readonly Dictionary<ushort, MethodInfo> _rpcMethods = new();
        private ushort _nextRpcId = 1;

        // === ЖИЗНЕННЫЙ ЦИКЛ ===
        protected virtual void OnSpawn()
        {
            RegisterRpcMethods();
        }

        protected virtual void OnDespawn() { }

        internal void InvokeOnSpawn() => OnSpawn();
        internal void InvokeOnDespawn() => OnDespawn();

        private void RegisterRpcMethods()
        {
            var methods = GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            foreach (var method in methods)
            {
                if (method.GetCustomAttribute<ServerRpcAttribute>() != null ||
                    method.GetCustomAttribute<ClientRpcAttribute>() != null)
                {
                    _rpcMethods[_nextRpcId++] = method;
                }
            }
        }

        // === RPC ВЫЗОВЫ ===
        protected async ValueTask CallServerRpc(string methodName, params object[] args)
        {
            if (!NetworkManager.Instance!.IsClient) return;

            var method = GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (method?.GetCustomAttribute<ServerRpcAttribute>() == null)
                throw new InvalidOperationException("Not a ServerRpc.");

            var rpcId = GetRpcId(method);
            var argsData = SerializeArgs(args);

            var msg = new RpcMessage(NetworkObject!.NetworkId, GetBehaviourId(), rpcId, argsData);
            await NetworkManager.Instance.SendToServerAsync(msg);
        }

        protected async ValueTask CallClientRpc(NetworkPlayer target, string methodName, params object[] args)
        {
            if (!NetworkManager.Instance!.IsServer) return;

            var method = GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (method?.GetCustomAttribute<ClientRpcAttribute>() == null)
                throw new InvalidOperationException("Not a ClientRpc.");

            var rpcId = GetRpcId(method);
            var argsData = SerializeArgs(args);

            var msg = new RpcMessage(NetworkObject!.NetworkId, GetBehaviourId(), rpcId, argsData);
            await NetworkManager.Instance.SendToAsync(target, msg);
        }

        protected async ValueTask BroadcastClientRpc(string methodName, params object[] args)
        {
            if (!NetworkManager.Instance!.IsServer) return;

            var method = GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (method?.GetCustomAttribute<ClientRpcAttribute>() == null)
                throw new InvalidOperationException("Not a ClientRpc.");

            var rpcId = GetRpcId(method);
            var argsData = SerializeArgs(args);

            var msg = new RpcMessage(NetworkObject!.NetworkId, GetBehaviourId(), rpcId, argsData);
            await NetworkManager.Instance.BroadcastAsync(msg);
        }

        private ushort GetRpcId(MethodInfo method)
        {
            foreach (var kvp in _rpcMethods)
            {
                if (kvp.Value == method) return kvp.Key;
            }
            throw new InvalidOperationException("RPC method not registered.");
        }

        private byte[] SerializeArgs(object[] args)
        {
            using var writer = new PacketWriter();
            writer.WriteUShort((ushort)args.Length);
            foreach (var arg in args)
            {
                switch (arg)
                {
                    case int i: writer.WriteInt(i); break;
                    case float f: writer.WriteFloat(f); break;
                    case string s: writer.WriteString(s); break;
                    case uint u: writer.WriteUInt(u); break;
                    default: throw new NotSupportedException($"Type {arg.GetType()} not supported in RPC.");
                }
            }
            return writer.GetBuffer().ToArray();
        }

        internal void InvokeRpc(ushort rpcId, byte[] argsData)
        {
            if (_rpcMethods.TryGetValue(rpcId, out var method))
            {
                var parameters = method.GetParameters();
                var args = DeserializeArgs(parameters, argsData);
                method.Invoke(this, args);
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
                    Type t when t == typeof(uint) => reader.ReadUInt(),
                    _ => throw new NotSupportedException($"Type {type} not supported.")
                };
            }
            return args;
        }

        // === Вспомогательные ===
        private ushort GetBehaviourId()
        {
            return 1;
        }
    }
}
