using DokuzuNet.Core.Connection;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace DokuzuNet.Transprot
{
    public class UdpTransport : ITransport
    {
        private UdpClient? _udpClient;
        private IPEndPoint? _localEndPoint;
        private IPEndPoint? _serverEndPoint;

        private CancellationTokenSource? _cts;
        private Task? _receiveTask;

        private readonly ConcurrentDictionary<IPEndPoint, UdpConnection> _connections = new();
        private UdpConnection? _localClientConnection;

        public event Action<IConnection>? OnClientConnected;
        public event Action<IConnection>? OnClientDisconnected;
        public event Action<(IConnection connection, ReadOnlyMemory<byte> data)>? OnDataReceived;
        public event Action<Exception>? OnError;

        private bool _isServer = false;
        private bool _isClient = false;
        private bool _isRunning = false;

        // === START ===
        public Task StartServerAsync(int port, CancellationToken ct = default)
        {
            if (_isRunning) throw new InvalidOperationException("Transport already running.");

            _isServer = true;
            _localEndPoint = new IPEndPoint(IPAddress.Any, port);
            return InitializeAsync(ct);
        }

        public Task StartClientAsync(string serverIp, int serverPort, CancellationToken ct = default)
        {
            if (_isRunning && !_isServer) throw new InvalidOperationException("Transport already running.");

            _isClient = true;
            var ip = IPAddress.Parse(serverIp);
            _serverEndPoint = new IPEndPoint(ip, serverPort);

            if (!_isRunning)
            {
                _localEndPoint = new IPEndPoint(IPAddress.Any, 0);
                return InitializeAsync(ct);
            }
            else
            {
                _localClientConnection = new UdpConnection(_udpClient!, _serverEndPoint);
                return SendConnectAsync();
            }
        }

        private Task InitializeAsync(CancellationToken ct)
        {
            _udpClient = new UdpClient(_localEndPoint!);
            _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _isRunning = true;
            _receiveTask = ReceiveLoopAsync(_cts.Token);

            if (_isClient && _serverEndPoint != null)
            {
                _localClientConnection = new UdpConnection(_udpClient, _serverEndPoint);
                SendConnectAsync().GetAwaiter().GetResult();
            }

            return Task.CompletedTask;
        }

        private async Task SendConnectAsync()
        {
            if (_localClientConnection == null) return;
            var data = new byte[] { 0x01 }; // CONNECT
            await _localClientConnection.SendAsync(data, _cts!.Token);
        }

        // === SENDING ===
        public ValueTask SendToAsync(IConnection connection, ReadOnlyMemory<byte> data, CancellationToken ct = default)
        {
            if (connection is not UdpConnection udpConn)
                throw new ArgumentException("Invalid connection type.");

            return udpConn.SendAsync(data, ct);
        }

        public async ValueTask BroadcastAsync(ReadOnlyMemory<byte> data, bool includeLocalClient = true, CancellationToken ct = default)
        {
            if (!_isServer) throw new InvalidOperationException("Not in server mode.");

            var tasks = _connections.Values
                .Where(c => includeLocalClient || !IPAddress.IsLoopback(c.EndPoint.Address))
                .Select(c => c.SendAsync(data, ct).AsTask())
                .ToArray();

            if (tasks.Length > 0)
                await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        // === RECEIVING ===
        private Task ReceiveLoopAsync(CancellationToken token)
        {
            return Task.Run(async () =>
            {
                while (!token.IsCancellationRequested && _udpClient != null)
                {
                    try
                    {
                        var result = await _udpClient.ReceiveAsync(token).ConfigureAwait(false);
                        var remote = result.RemoteEndPoint;
                        var buffer = result.Buffer;

                        await HandlePacketAsync(buffer, remote, token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) { break; }
                    catch (ObjectDisposedException) { break; }
                    catch (Exception ex)
                    {
                        OnError?.Invoke(ex);
                    }
                }
            }, token);
        }

        private async Task HandlePacketAsync(byte[] buffer, IPEndPoint remote, CancellationToken token)
        {
            var memory = new ReadOnlyMemory<byte>(buffer);

            if (_isServer && _isClient && IPAddress.IsLoopback(remote.Address) && remote.Port == _localEndPoint!.Port)
            {
                // Self-packet — пропустить, чтобы избежать цикла
                return;
            }

            // CONNECT
            if (buffer.Length == 1 && buffer[0] == 0x01 && _isServer)
            {
                var conn = _connections.GetOrAdd(remote, _ => new UdpConnection(_udpClient!, remote));
                conn.UpdateLastReceived();
                OnClientConnected?.Invoke(conn);

                var welcome = new byte[] { 0x02 }; // WELCOME
                await conn.SendAsync(welcome, token);
                return;
            }

            // DISCONNECT
            if (buffer.Length == 1 && buffer[0] == 0x03 && _isServer)
            {
                if (_connections.TryRemove(remote, out var conn))
                {
                    conn.Disconnect();
                    OnClientDisconnected?.Invoke(conn);
                }
                return;
            }

            // WELCOME
            if (buffer.Length == 1 && buffer[0] == 0x02 && !_isServer && _localClientConnection?.EndPoint.Equals(remote) == true)
            {
                _localClientConnection.UpdateLastReceived();

                OnClientConnected?.Invoke(_localClientConnection);
                return;
            }

            // Activity update
            if (_isServer && _connections.TryGetValue(remote, out var serverConn))
            {
                serverConn.UpdateLastReceived();
            }

            if (!_isServer && _localClientConnection?.EndPoint.Equals(remote) == true)
            {
                _localClientConnection.UpdateLastReceived();
            }

            // Data
            var connection = _isServer
                ? _connections.GetOrAdd(remote, _ => new UdpConnection(_udpClient!, remote))
                : _localClientConnection!;

            OnDataReceived?.Invoke((connection, memory));
        }

        // === STOP ===
        public async Task StopAsync(CancellationToken ct = default)
        {
            if (!_isRunning) return;

            _isRunning = false;
            _cts?.Cancel();

            if (!_isServer && _localClientConnection != null)
            {
                var disconnect = new byte[] { 0x03 };
                _ = _localClientConnection.SendAsync(disconnect, ct);
            }

            try
            {
                if (_receiveTask != null)
                    await _receiveTask.ConfigureAwait(false);
            }
            catch { }

            foreach (var conn in _connections.Values)
                conn.Dispose();
            _connections.Clear();

            _localClientConnection?.Dispose();
            _localClientConnection = null;

            _udpClient?.Dispose();
            _cts?.Dispose();

            _udpClient = null;
            _cts = null;
            _receiveTask = null;
        }

        public void Dispose()
        {
            try { StopAsync().Wait(2000); }
            catch { }
        }

        // === UTILITY ===
        public IReadOnlyCollection<IConnection> GetConnections() => _connections.Values.ToList().AsReadOnly();
        public IConnection? GetLocalClientConnection() => _localClientConnection;
    }
}
