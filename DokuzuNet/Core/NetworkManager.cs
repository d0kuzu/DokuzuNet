using DokuzuNet.Core.Connection;
using DokuzuNet.Integration;
using DokuzuNet.Networking;
using DokuzuNet.Networking.Message;
using DokuzuNet.Transprot;
using DokuzuNet.Utils;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace DokuzuNet.Core
{
    public enum NetworkMode { None, Server, Client, Host }

    public class NetworkManager
    {
        public static NetworkManager? Instance { get; private set; }

        private readonly ITransport _transport;
        private readonly MessageRegistry _registry = new();

        public NetworkMode Mode { get; private set; } = NetworkMode.None;
        public bool IsServer => Mode == NetworkMode.Server || Mode == NetworkMode.Host;
        public bool IsClient => Mode == NetworkMode.Client || Mode == NetworkMode.Host;
        public NetworkPlayer? LocalPlayer { get; private set; }
        public IConnection? LocalConnection => _transport.GetLocalClientConnection();

        private readonly Dictionary<IConnection, NetworkPlayer> _players = new();
        private readonly PrefabRegistry _prefabs = new();
        private readonly Dictionary<uint, NetworkObject> _objects = new();
        private uint _nextNetworkId = 1;

        public event Action<NetworkPlayer>? OnPlayerJoined;
        public event Action<NetworkPlayer>? OnPlayerLeft;

        public NetworkManager(ITransport transport)
        {
            if (Instance != null) throw new InvalidOperationException("NetworkManager already exists.");
            Instance = this;

            _transport = transport;
            _transport.OnClientConnected += HandleClientConnected;
            _transport.OnClientDisconnected += HandleClientDisconnected;
            _transport.OnDataReceived += HandleDataReceived;

            OnPlayerJoined += OnLocalPlayerJoined;

            // Messages registration
            _registry.Register<ChatMessage>();
            _registry.Register<SpawnMessage>();
            _registry.Register<SyncVarMessage>();
            _registry.Register<RpcMessage>();

            // Message handlers registration
            _registry.On<ChatMessage>(HandleChatMessage);
            _registry.On<SpawnMessage>(HandleSpawnMessage);
            _registry.On<SyncVarMessage>(HandleSyncVarMessage);
            _registry.On<RpcMessage>(HandleRpcMessage);

            // Prefab registration (example)
            _prefabs.Register("PlayerPrefab");
        }

        // === SUBSCRIPTION ===
        private void HandleChatMessage(NetworkPlayer player, ChatMessage msg)
        {
            Logger.Info($"{player.Connection.EndPoint}: {msg.Text}");

            if (IsServer)
            {
                _ = BroadcastAsync(msg);
            }
        }

        private void HandleSpawnMessage(NetworkPlayer player, SpawnMessage msg)
        {
            if (!IsServer)
            {
                var prefabName = _prefabs.GetPrefab(msg.PrefabId);
                if (string.IsNullOrEmpty(prefabName))
                {
                    Logger.Warn($"Unknown prefab ID: {msg.PrefabId}");
                    return;
                }

                var owner = _players.Values.FirstOrDefault(p => p.Connection.EndPoint.Port == (int)msg.OwnerId);
                if (owner == null)
                {
                    Logger.Warn($"Spawn owner not found: {msg.OwnerId}");
                    return;
                }

                var netObj = new NetworkObject();
                netObj.Initialize(msg.NetworkId, owner);

                _objects[msg.NetworkId] = netObj;

                Logger.Info($"{prefabName} (ID: {msg.NetworkId}) at ({msg.X:F1}, {msg.Y:F1}, {msg.Z:F1})");

                netObj.OnSpawn();
            }
        }

        private void HandleSyncVarMessage(NetworkPlayer player, SyncVarMessage msg)
        {
            if (_objects.TryGetValue(msg.ObjectId, out var netObj))
            {
                var behaviour = netObj.GetBehaviours().FirstOrDefault(); // TODO: get by BehaviourId

                if (behaviour != null)
                {
                    behaviour.ApplySyncVar(msg.VarId, msg.Value);
                    Logger.Debug($"[SYNCVAR] Object {msg.ObjectId} Var {msg.VarId} updated");
                }
            }
        }

        private void HandleRpcMessage(NetworkPlayer player, RpcMessage msg)
        {
            if (_objects.TryGetValue(msg.ObjectId, out var netObj))
            {
                var behaviour = netObj.GetBehaviours().FirstOrDefault();

                if (behaviour != null)
                {
                    behaviour.InvokeRpc(msg.RpcId, msg.Args);
                    Logger.Debug($"[RPC] Object {msg.ObjectId} Rpc {msg.RpcId} invoked");
                }
            }
        }

        // === START ===
        public async Task StartServerAsync(int port, CancellationToken ct = default)
        {
            if (Mode != NetworkMode.None) throw new InvalidOperationException("Already started.");
            Mode = NetworkMode.Server;
            await _transport.StartServerAsync(port, ct);
        }

        public async Task StartClientAsync(string ip, int port, CancellationToken ct = default)
        {
            if (Mode != NetworkMode.None) throw new InvalidOperationException("Already started.");
            Mode = NetworkMode.Client;
            await _transport.StartClientAsync(ip, port, false, ct);
        }

        public async Task StartHostAsync(int port, CancellationToken ct = default)
        {
            if (Mode != NetworkMode.None) throw new InvalidOperationException("Already started.");
            Mode = NetworkMode.Host;

            await _transport.StartServerAsync(port, ct);
            await _transport.StartClientAsync("127.0.0.1", port, true, ct);

            LocalPlayer = new NetworkPlayer(_transport.GetLocalClientConnection()!);
            _players[LocalPlayer.Connection] = LocalPlayer;
            OnPlayerJoined?.Invoke(LocalPlayer);
        }

        // === SENDING ===
        public async ValueTask SendToServerAsync<T>(T message, CancellationToken ct = default) where T : IMessage
        {
            if (!IsClient) throw new InvalidOperationException("Not a client.");
            var data = _registry.Serialize(message);
            var conn = _transport.GetLocalClientConnection() ?? throw new InvalidOperationException("No local connection.");
            await _transport.SendToAsync(conn, data, ct);
        }

        public async ValueTask SendToAsync<T>(NetworkPlayer player, T message, CancellationToken ct = default) where T : IMessage
        {
            if (!IsServer) throw new InvalidOperationException("Not a server.");
            var data = _registry.Serialize(message);
            await _transport.SendToAsync(player.Connection, data, ct);
        }

        public async ValueTask BroadcastAsync<T>(T message, CancellationToken ct = default) where T : IMessage
        {
            if (!IsServer) throw new InvalidOperationException("Not a server.");

            var data = _registry.Serialize(message);
            await _transport.BroadcastAsync(data, ct);

            if (Mode == NetworkMode.Host && LocalConnection != null)
            {
                await _transport.SendToAsync(LocalConnection, data, ct);
            }
        }

        // === PROCESSING ===
        private void OnLocalPlayerJoined(NetworkPlayer player)
        {
            if (IsClient && player.Connection == _transport.GetLocalClientConnection())
            {
                LocalPlayer = player;
                Logger.Info($"LocalPlayer setted: {player}");
            }
        }
        private void HandleClientConnected(IConnection connection)
        {
            var player = new NetworkPlayer(connection);
            _players[connection] = player;
            OnPlayerJoined?.Invoke(player);
        }

        private void HandleClientDisconnected(IConnection connection)
        {
            if (_players.TryGetValue(connection, out var player))
            {
                _players.Remove(connection);
                OnPlayerLeft?.Invoke(player);
            }
        }

        private void HandleDataReceived((IConnection connection, ReadOnlyMemory<byte> data) packet)
        {
            var msg = _registry.Deserialize(packet.data);
            if (msg == null) return;

            var player = _players.GetValueOrDefault(packet.connection);
            if (player == null) return;

            _registry.Dispatch(msg, player);
        }

        // === SPAWN ===
        public async Task<NetworkObject> SpawnAsync(string prefabName, NetworkPlayer owner, float x = 0, float y = 0, float z = 0)
        {
            if (!IsServer) throw new InvalidOperationException("Only server can spawn.");

            var prefabId = _prefabs.GetId(prefabName);
            if (prefabId == 0) throw new InvalidOperationException($"Prefab not registered: {prefabName}");

            var netId = _nextNetworkId++;
            var obj = new NetworkObject();
            obj.Initialize(netId, owner);

            _objects[netId] = obj;

            var msg = new SpawnMessage(prefabId, netId, (uint)owner.Connection.EndPoint.Port, x, y, z); // Fixed OwnerId
            await BroadcastAsync(msg);

            obj.OnSpawn();
            return obj;
        }

        // === STOP ===
        public async Task StopAsync(CancellationToken ct = default)
        {
            if (Mode == NetworkMode.None) return;

            await _transport.StopAsync(ct);
            _players.Clear();
            _objects.Clear();
            Mode = NetworkMode.None;
            Instance = null;
            LocalPlayer = null;
        }

        public IReadOnlyCollection<NetworkPlayer> Players => _players.Values.ToList().AsReadOnly();
        public NetworkPlayer? GetPlayer(IConnection conn) => _players.GetValueOrDefault(conn);
    }
}
