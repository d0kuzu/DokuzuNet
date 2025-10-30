using DokuzuNet.Networking;
using DokuzuNet.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace DokuzuNet.Server
{
    public class ServerManager
    {
        private TcpListener? _listener;
        private bool _isRunning;

        public async Task StartAsync(int port, CancellationToken token = default)
        {
            if (_isRunning)
            {
                Logger.Info("Server is already running.");
                return;
            }

            _listener = new TcpListener(IPAddress.Any, port);
            _listener.Start();
            _isRunning = true;

            Logger.Info($"Server started on port {port}.");

            try
            {
                while (!token.IsCancellationRequested)
                {
                    var client = await _listener.AcceptTcpClientAsync(token);
                    _ = HandleClientAsync(client);
                }
            }
            catch (OperationCanceledException)
            {
                Logger.Info("Server stopped gracefully.");
            }
            finally
            {
                _listener.Stop();
                _isRunning = false;
            }
        }

        private async Task HandleClientAsync(TcpClient client)
        {
            var endpoint = client.Client.RemoteEndPoint?.ToString();
            Logger.Info($"Client connected: {endpoint}");

            var buffer = new byte[1024];
            using var stream = client.GetStream();

            try
            {
                while (true)
                {
                    int bytesRead = await stream.ReadAsync(buffer);
                    if (bytesRead == 0) break;

                    var msg = System.Text.Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    var packet = Packet.FromJson(msg);

                    if (packet != null)
                    {
                        Logger.Info($"Packet from {endpoint}: {packet.Type}");
                    }
                    else
                    {
                        Logger.Error($"Invalid packet from {endpoint}: {msg}");
                    }

                    byte[] response = System.Text.Encoding.UTF8.GetBytes($"Echo: {msg}");
                    await stream.WriteAsync(response);
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Client {endpoint} disconnected: {ex.Message}");
            }

            Logger.Info($"Client disconnected: {endpoint}");
        }
    }
}
