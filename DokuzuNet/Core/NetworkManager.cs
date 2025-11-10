using DokuzuNet.Core.Connection;
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
    public enum NetworkMode
    {
        None,
        Server,
        Client,
        Host
    }

    public class NetworkManager : IDisposable
    {
        private UdpClient? _udpClient;
        private IPEndPoint? _localEndPoint;
        private IPEndPoint? _serverEndPoint; // для клиента

        private CancellationTokenSource? _cts;
        private Task? _receiveTask;

        private NetworkMode _mode = NetworkMode.None;
        private bool _isRunning = false;

        private const int DefaultPort = 11000;
        private readonly Encoding _encoding = Encoding.UTF8;

        // === Подключения (только на сервере/хосте) ===
        private readonly ConcurrentDictionary<IPEndPoint, UdpConnection> _connections = new();

        // === Клиентское подключение (для Client/Host) ===
        private UdpConnection? _clientConnection;

        // === События ===
        public event EventHandler<UdpConnection>? OnClientConnected;
        public event EventHandler<UdpConnection>? OnClientDisconnected;
        public event EventHandler<(UdpConnection conn, ReadOnlyMemory<byte> data)>? OnMessageReceived;
        public event EventHandler<Exception>? OnError;

        // === Свойства ===
        public NetworkMode Mode => _mode;
        public bool IsRunning => _isRunning;
        public int LocalPort => _localEndPoint?.Port ?? 0;
        public bool IsClient => _mode == NetworkMode.Client || _mode == NetworkMode.Host;
        public bool IsServer => _mode == NetworkMode.Server || _mode == NetworkMode.Host;

        public NetworkManager() { }

        // === СТАРТ ===

        public async Task StartServerAsync(int port = DefaultPort)
        {
            if (_isRunning) throw new InvalidOperationException("Already running.");

            _mode = NetworkMode.Server;
            _localEndPoint = new IPEndPoint(IPAddress.Any, port);

            await InitializeAsync();
            Logger.Info($"[SERVER] Started on port {port}");
        }

        public async Task StartClientAsync(string serverIp = "127.0.0.1", int serverPort = DefaultPort, int localPort = 0)
        {
            if (_isRunning) throw new InvalidOperationException("Already running.");

            var serverIpAddress = IPAddress.Parse(serverIp);
            _serverEndPoint = new IPEndPoint(serverIpAddress, serverPort);
            _localEndPoint = new IPEndPoint(IPAddress.Any, localPort);

            _mode = NetworkMode.Client;

            await InitializeAsync();

            _clientConnection = new UdpConnection(_udpClient!, _serverEndPoint);
            await SendConnectAsync();
            Logger.Info($"[CLIENT] Connecting to {serverIp}:{serverPort}");
        }

        public async Task StartHostAsync(int port = DefaultPort)
        {
            if (_isRunning) throw new InvalidOperationException("Already running.");

            _mode = NetworkMode.Host;
            _localEndPoint = new IPEndPoint(IPAddress.Any, port);

            await InitializeAsync();

            // Локальный клиент в хосте
            _serverEndPoint = new IPEndPoint(IPAddress.Loopback, port);
            _clientConnection = new UdpConnection(_udpClient!, _serverEndPoint);

            Logger.Info($"[HOST] Started on port {port} (Server + Local Client)");
        }

        private async Task InitializeAsync()
        {
            _udpClient = new UdpClient(_localEndPoint!);
            _cts = new CancellationTokenSource();
            _isRunning = true;
            _receiveTask = ReceiveLoopAsync(_cts.Token);
        }

        private async Task SendConnectAsync()
        {
            if (_clientConnection == null) return;
            var data = _encoding.GetBytes("CONNECT");
            await _clientConnection.SendAsync(data, _cts!.Token);
        }

        // === ОТПРАВКА ===

        public async ValueTask SendToServerAsync(ReadOnlyMemory<byte> data)
        {
            if (!IsClient || _clientConnection == null)
                throw new InvalidOperationException("Not in Client or Host mode.");

            await _clientConnection.SendAsync(data, _cts!.Token).ConfigureAwait(false);
        }

        public async ValueTask SendToAsync(UdpConnection connection, ReadOnlyMemory<byte> data)
        {
            if (!IsServer) throw new InvalidOperationException("Not in Server or Host mode.");
            if (connection == null) throw new ArgumentNullException(nameof(connection));

            await connection.SendAsync(data, _cts!.Token).ConfigureAwait(false);
        }

        public async ValueTask BroadcastAsync(ReadOnlyMemory<byte> data, bool includeLocalClient = true)
        {
            if (!IsServer) throw new InvalidOperationException("Not in Server or Host mode.");

            foreach (var conn in _connections.Values)
            {
                if (!includeLocalClient && IPAddress.IsLoopback(conn.EndPoint.Address)) continue;
                await conn.SendAsync(data, _cts!.Token).ConfigureAwait(false);
            }
        }

        // === ПРИЁМ ===

        private async Task ReceiveLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested && _udpClient != null)
            {
                try
                {
                    var result = await _udpClient.ReceiveAsync(token).ConfigureAwait(false);
                    var remote = result.RemoteEndPoint;
                    var data = result.Buffer;

                    Logger.Info($"[RECV] ← {remote}: {data.Length} bytes");

                    await HandlePacketAsync(data, remote, token);
                }
                catch (OperationCanceledException) { break; }
                catch (ObjectDisposedException) { break; }
                catch (Exception ex)
                {
                    OnError?.Invoke(this, ex);
                }
            }
        }

        private async Task HandlePacketAsync(byte[] data, IPEndPoint remote, CancellationToken token)
        {
            var message = _encoding.GetString(data);

            // === Подключение ===
            if (message == "CONNECT" && IsServer)
            {
                var conn = _connections.GetOrAdd(remote, _ => new UdpConnection(_udpClient!, remote));
                conn.UpdateLastReceived();
                OnClientConnected?.Invoke(this, conn);

                var welcome = _encoding.GetBytes("WELCOME");
                await conn.SendAsync(welcome, token);
                return;
            }

            // === Отключение ===
            if (message == "DISCONNECT" && IsServer)
            {
                if (_connections.TryRemove(remote, out var conn))
                {
                    conn.Disconnect();
                    OnClientDisconnected?.Invoke(this, conn);
                }
                return;
            }

            // === Обновление активности ===
            if (IsServer && _connections.TryGetValue(remote, out var serverConn))
            {
                serverConn.UpdateLastReceived();
            }

            if (IsClient && _clientConnection?.EndPoint.Equals(remote) == true)
            {
                _clientConnection.UpdateLastReceived();
            }

            // === Пользовательские данные ===
            var memory = new ReadOnlyMemory<byte>(data);
            if (IsClient || _mode == NetworkMode.Host)
            {
                OnMessageReceived?.Invoke(this, (_clientConnection!, memory));
            }
            if (IsServer)
            {
                if (_connections.TryGetValue(remote, out var conn))
                {
                    OnMessageReceived?.Invoke(this, (conn, memory));
                }
            }
        }

        // === СТОП ===

        public async Task StopAsync()
        {
            if (!_isRunning) return;

            _isRunning = false;
            bool wasClient = _mode == NetworkMode.Client;

            _mode = NetworkMode.None;
            _cts?.Cancel();

            // Только настоящий клиент шлёт DISCONNECT
            if (wasClient && _clientConnection != null)
            {
                var data = _encoding.GetBytes("DISCONNECT");
                _ = _clientConnection.SendAsync(data, _cts!.Token);
            }

            try
            {
                if (_receiveTask != null)
                    await _receiveTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                OnError?.Invoke(this, ex);
            }
            finally
            {
                foreach (var conn in _connections.Values)
                {
                    conn.Dispose();
                }
                _connections.Clear();

                _clientConnection?.Dispose();
                _clientConnection = null;

                _udpClient?.Close();
                _udpClient?.Dispose();
                _cts?.Dispose();

                _udpClient = null;
                _cts = null;
                _receiveTask = null;
                _localEndPoint = null;
                _serverEndPoint = null;

                Logger.Info("NetworkManager stopped.");
            }
        }

        public void Dispose()
        {
            try { StopAsync().Wait(2000); }
            catch { }
        }

        // === Утилиты ===

        public IReadOnlyCollection<UdpConnection> GetConnections()
            => _connections.Values;

        public UdpConnection? GetClientConnection()
            => _clientConnection;
    }
}
