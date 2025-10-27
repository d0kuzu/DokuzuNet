using DokuzuNet.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace DokuzuNet.Client
{
    public class ClientManager
    {
        private TcpClient? _client;
        private NetworkStream? _stream;
        private CancellationTokenSource? _cts;
        private bool _isConnected;

        public bool IsConnected => _isConnected;

        public async Task ConnectAsync(string host, int port)
        {
            if (_isConnected)
            {
                Logger.Info("Already connected to server.");
                return;
            }

            try
            {
                _client = new TcpClient();
                await _client.ConnectAsync(host, port);
                _stream = _client.GetStream();
                _cts = new CancellationTokenSource();
                _isConnected = true;

                Logger.Info($"Connected to server {host}:{port}");
                _ = ListenAsync(_cts.Token);
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to connect: {ex.Message}");
            }
        }

        public async Task SendAsync(string message)
        {
            if (!_isConnected || _stream == null)
            {
                Logger.Error("Not connected to server.");
                return;
            }

            try
            {
                byte[] data = Encoding.UTF8.GetBytes(message);
                await _stream.WriteAsync(data);
            }
            catch (Exception ex)
            {
                Logger.Error($"Error sending message: {ex.Message}");
            }
        }

        private async Task ListenAsync(CancellationToken token)
        {
            var buffer = new byte[1024];

            try
            {
                while (!token.IsCancellationRequested)
                {
                    int bytesRead = await _stream!.ReadAsync(buffer, token);
                    if (bytesRead == 0)
                    {
                        Logger.Info("Disconnected from server.");
                        Disconnect();
                        break;
                    }

                    string msg = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    Logger.Info($"Received from server: {msg}");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Connection error: {ex.Message}");
            }
        }

        public void Disconnect()
        {
            if (!_isConnected) return;

            _cts?.Cancel();
            _stream?.Close();
            _client?.Close();
            _isConnected = false;

            Logger.Info("Client disconnected.");
        }
    }
}
