using DokuzuNet.Core.Connection;
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
        // === Синглтон ===
        public static NetworkManager? Instance { get; private set; }

        private readonly ITransport _transport;
        private readonly MessageRegistry _registry = new();
        private readonly Dictionary<Type, Delegate> _messageHandlers = new();

        public NetworkMode Mode { get; private set; } = NetworkMode.None;
        public bool IsServer => Mode == NetworkMode.Server || Mode == NetworkMode.Host;
        public bool IsClient => Mode == NetworkMode.Client || Mode == NetworkMode.Host;

        private readonly Dictionary<IConnection, NetworkPlayer> _players = new();

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
        }

        // === СТАРТ ===

        public async Task StartServerAsync(int port, CancellationToken ct = default)
        {
            if (Mode != NetworkMode.None) throw new InvalidOperationException("Already started.");
            Mode = NetworkMode.Server;
            await _transport.StartServerAsync(port, ct);
            Logger.Info("[NET] Server started");
        }

        public async Task StartClientAsync(string ip, int port, CancellationToken ct = default)
        {
            if (Mode != NetworkMode.None) throw new InvalidOperationException("Already started.");
            Mode = NetworkMode.Client;
            await _transport.StartClientAsync(ip, port, ct);
            Logger.Info("[NET] Client connected");
        }

        public async Task StartHostAsync(int port, CancellationToken ct = default)
        {
            if (Mode != NetworkMode.None) throw new InvalidOperationException("Already started.");
            Mode = NetworkMode.Host;

            await _transport.StartServerAsync(port, ct);
            await _transport.StartClientAsync("127.0.0.1", port, ct);

            // Локальный игрок
            var localConn = _transport.GetLocalClientConnection();
            if (localConn != null)
            {
                var player = new NetworkPlayer(localConn);
                _players[localConn] = player;
                OnPlayerJoined?.Invoke(player);
            }

            Logger.Info("[NET] Host started");
        }

        // === ОТПРАВКА ===

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

        public async ValueTask BroadcastAsync<T>(T message, bool includeLocal = true, CancellationToken ct = default) where T : IMessage
        {
            if (!IsServer) throw new InvalidOperationException("Not a server.");
            var data = _registry.Serialize(message);
            await _transport.BroadcastAsync(data, includeLocal, ct);
        }

        // === ОБРАБОТКА ===

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

            // Рассылаем по типу
            if (_messageHandlers.TryGetValue(msg.GetType(), out var handler))
            {
                handler.DynamicInvoke(player, msg);
            }
        }

        // === СТОП ===

        public async Task StopAsync(CancellationToken ct = default)
        {
            if (Mode == NetworkMode.None) return;

            await _transport.StopAsync(ct);
            _players.Clear();
            Mode = NetworkMode.None;
            Instance = null;
            Logger.Info("[NET] Stopped");
        }

        // === ПОДПИСКА НА СООБЩЕНИЯ ===
        public void AddHandler<T>(Action<NetworkPlayer, T> handler) where T : IMessage
        {
            var type = typeof(T);
            if (_messageHandlers.TryGetValue(type, out var existing))
            {
                _messageHandlers[type] = Delegate.Combine(existing, handler);
            }
            else
            {
                _messageHandlers[type] = handler;
            }
        }

        public void RemoveHandler<T>(Action<NetworkPlayer, T> handler) where T : IMessage
        {
            var type = typeof(T);
            if (_messageHandlers.TryGetValue(type, out var existing))
            {
                var newDelegate = Delegate.Remove(existing, handler);
                if (newDelegate == null)
                    _messageHandlers.Remove(type);
                else
                    _messageHandlers[type] = newDelegate;
            }
        }

        // === УТИЛИТЫ ===

        public IReadOnlyCollection<NetworkPlayer> Players => _players.Values;
        public NetworkPlayer? GetPlayer(IConnection conn) => _players.GetValueOrDefault(conn);
    }
}
