using DokuzuNet.Utils;
using System;
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
        private IPEndPoint? _remoteEndPoint;

        private CancellationTokenSource? _cts;
        private Task? _receiveTask;

        private NetworkMode _mode = NetworkMode.None;
        private bool _isRunning = false;

        private const int DefaultPort = 11000;
        private readonly Encoding _encoding = Encoding.UTF8;

        // === Events ===
        public event EventHandler<string>? OnClientMessageReceived;
        public event EventHandler<string>? OnServerMessageReceived;
        public event EventHandler<IPEndPoint>? OnClientConnected;
        public event EventHandler<IPEndPoint>? OnClientDisconnected;
        public event EventHandler<Exception>? OnError;

        // === fields ===
        public NetworkMode Mode => _mode;
        public bool IsRunning => _isRunning;
        public int LocalPort => _localEndPoint?.Port ?? 0;
        public string? ServerIp => _remoteEndPoint?.Address.ToString();
        public int ServerPort => _remoteEndPoint?.Port ?? DefaultPort;

        // === Constructor ===
        public NetworkManager() { }

        // === Starting ===
        public async Task StartServerAsync(int port = DefaultPort)
        {
            if (_isRunning) throw new InvalidOperationException("NetworkManager already running.");

            _mode = NetworkMode.Server;
            await InitializeUdpAsync(new IPEndPoint(IPAddress.Any, port));
            Logger.Info($"Server started on port {port}");
        }

        public async Task StartClientAsync(string serverIp = "127.0.0.1", int serverPort = DefaultPort, int localPort = 0)
        {
            if (_isRunning) throw new InvalidOperationException("NetworkManager already running.");

            var serverIpAddress = IPAddress.Parse(serverIp);
            _remoteEndPoint = new IPEndPoint(serverIpAddress, serverPort);

            _mode = NetworkMode.Client;
            await InitializeUdpAsync(new IPEndPoint(IPAddress.Any, localPort));
            Logger.Info($"Client started, connecting to {serverIp}:{serverPort}");
        }

        public async Task StartHostAsync(int port = DefaultPort)
        {
            if (_isRunning) throw new InvalidOperationException("NetworkManager already running.");

            _mode = NetworkMode.Host;
            await InitializeUdpAsync(new IPEndPoint(IPAddress.Any, port));
            Logger.Info($"Host started on port {port} (Server + Client)");
        }

        private async Task InitializeUdpAsync(IPEndPoint localEndPoint)
        {
            _localEndPoint = localEndPoint;
            _udpClient = new UdpClient(_localEndPoint);
            _cts = new CancellationTokenSource();
            _isRunning = true;

            _receiveTask = ReceiveLoopAsync(_cts.Token);

            // Если это клиент или хост — можно сразу отправить "подключение"
            if (_mode == NetworkMode.Client || _mode == NetworkMode.Host)
            {
                await SendInternalAsync("CONNECT", _remoteEndPoint!, _cts.Token);
            }
        }

        // === Stoping ===
        public async Task StopAsync()
        {
            if (!_isRunning) return;

            _isRunning = false;
            _mode = NetworkMode.None;

            _cts?.Cancel();

            if (_mode == NetworkMode.Client || _mode == NetworkMode.Host)
            {
                await SendInternalAsync("DISCONNECT", _remoteEndPoint!, _cts!.Token).ContinueWith(_ => { }, TaskContinuationOptions.OnlyOnRanToCompletion);
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
                _udpClient?.Close();
                _udpClient?.Dispose();
                _cts?.Dispose();

                _udpClient = null;
                _cts = null;
                _receiveTask = null;

                Logger.Info("NetworkManager stopped.");
            }
        }

        // === Sending ===
        public async Task SendToServerAsync(string message)
        {
            if (_mode != NetworkMode.Client && _mode != NetworkMode.Host)
                throw new InvalidOperationException("Cannot send to server: not in Client or Host mode.");

            if (_remoteEndPoint == null)
                throw new InvalidOperationException("Remote endpoint not set.");

            await SendInternalAsync(message, _remoteEndPoint, _cts!.Token);
        }

        public async Task SendToClientAsync(string message, IPEndPoint clientEndPoint)
        {
            if (_mode != NetworkMode.Server && _mode != NetworkMode.Host)
                throw new InvalidOperationException("Cannot send to client: not in Server or Host mode.");

            await SendInternalAsync(message, clientEndPoint, _cts!.Token);
        }

        public async Task SendToAllClientsAsync(string message)
        {
            if (_mode != NetworkMode.Server && _mode != NetworkMode.Host)
                throw new InvalidOperationException("Cannot broadcast: not in Server or Host mode.");

            // В реальном проекте — хранить список клиентов
            // Пока просто шлём на remote (если есть)
            if (_remoteEndPoint != null)
                await SendInternalAsync(message, _remoteEndPoint, _cts!.Token);
        }

        private async Task SendInternalAsync(string message, IPEndPoint target, CancellationToken token)
        {
            if (_udpClient == null) return;

            try
            {
                var data = _encoding.GetBytes(message);
                await _udpClient.SendAsync(data, data.Length, target).ConfigureAwait(false);
                Logger.Info($"Sent to {target}: {message}");
            }
            catch (Exception ex) when (!token.IsCancellationRequested)
            {
                Logger.Error($"Send error to {target}: {ex.Message}");
                OnError?.Invoke(this, ex);
            }
        }

        // === Receiving ===
        private async Task ReceiveLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested && _udpClient != null)
            {
                try
                {
                    var result = await _udpClient.ReceiveAsync(token).ConfigureAwait(false);
                    var message = _encoding.GetString(result.Buffer);
                    var remote = result.RemoteEndPoint;

                    Logger.Info($"Received from {remote}: {message}");

                    await HandleMessageAsync(message, remote, token);
                }
                catch (OperationCanceledException) { break; }
                catch (ObjectDisposedException) { break; }
                catch (Exception ex)
                {
                    OnError?.Invoke(this, ex);
                }
            }
        }

        private Task HandleMessageAsync(string message, IPEndPoint remote, CancellationToken token)
        {
            switch (message)
            {
                case "CONNECT":
                    if (_mode == NetworkMode.Server || _mode == NetworkMode.Host)
                    {
                        _remoteEndPoint ??= remote;
                        OnClientConnected?.Invoke(this, remote);
                        return SendInternalAsync("WELCOME", remote, token);
                    }
                    break;

                case "DISCONNECT":
                    if (_mode == NetworkMode.Server || _mode == NetworkMode.Host)
                    {
                        OnClientDisconnected?.Invoke(this, remote);
                    }
                    break;

                case string m when m.StartsWith("WELCOME"):
                    if (_mode == NetworkMode.Client || _mode == NetworkMode.Host)
                    {
                        Logger.Info("Connected to server.");
                    }
                    break;
            }

            // Custom messages
            if (_mode == NetworkMode.Client || _mode == NetworkMode.Host)
            {
                OnClientMessageReceived?.Invoke(this, message);
            }
            else if (_mode == NetworkMode.Server)
            {
                OnServerMessageReceived?.Invoke(this, message);
            }

            return Task.CompletedTask;
        }

        // === Dispose ===
        public void Dispose()
        {
            StopAsync().Wait(2000);
        }
    }
}
