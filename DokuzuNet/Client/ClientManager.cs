using DokuzuNet.Networking;
using DokuzuNet.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace DokuzuNet.Client
{
    public class ClientManager : IDisposable
    {
        private UdpClient _udpClient;
        private IPEndPoint _remoteEndPoint;
        private CancellationTokenSource _cts;
        private Task _receiveTask;
        private bool _isRunning;

        private const int DefaultPort = 11000;
        private readonly Encoding _encoding = Encoding.UTF8;

        public event EventHandler<string>? MessageReceived;
        public event EventHandler<Exception>? ReceiveError;

        public ClientManager(string serverIp = "127.0.0.1", int serverPort = DefaultPort, int localPort = 0)
        {
            var serverIpAddress = IPAddress.Parse(serverIp);
            _remoteEndPoint = new IPEndPoint(serverIpAddress, serverPort);

            _udpClient = new UdpClient(localPort);

            _cts = new CancellationTokenSource();
            _receiveTask = Task.CompletedTask;
            _isRunning = false;
        }

        public Task StartAsync()
        {
            if (_isRunning)
            {
                Logger.Info("Client already running.");
                return Task.CompletedTask;
            }

            _isRunning = true;
            _receiveTask = ReceiveLoopAsync(_cts.Token);
            Logger.Info($"Client started, listening for messages from {_remoteEndPoint}");
            return Task.CompletedTask;
        }

        public async Task StopAsync()
        {
            if (!_isRunning)
            {
                Logger.Info("Client already stopped.");
                return;
            }

            _isRunning = false;
            _cts.Cancel();

            try
            {
                await _receiveTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                ReceiveError?.Invoke(this, ex);
            }
            finally
            {
                Logger.Info("Client stopped.");
            }
        }
        
        public async Task SendAsync(string message)
        {
            if (string.IsNullOrEmpty(message))
                throw new ArgumentException("Message cannot be null or empty.", nameof(message));

            var data = _encoding.GetBytes(message);
            await _udpClient.SendAsync(data, data.Length, _remoteEndPoint);
            Logger.Info($"Client sent: {message}");
        }

        public async Task SendAsync(byte[] data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (data.Length == 0) throw new ArgumentException("Data cannot be empty.", nameof(data));

            await _udpClient.SendAsync(data, data.Length, _remoteEndPoint);
            Logger.Info($"Client sent {data.Length} bytes");
        }

        private async Task ReceiveLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var result = await _udpClient.ReceiveAsync(token).ConfigureAwait(false);
                    var message = _encoding.GetString(result.Buffer);

                    MessageReceived?.Invoke(this, message);

                    Logger.Info($"Client received: {message} from {result.RemoteEndPoint}");
                }
                catch (OperationCanceledException) {  }
                catch (SocketException ex) when (ex.SocketErrorCode == SocketError.OperationAborted)
                {
                    break;
                }
                catch (Exception ex)
                {
                    ReceiveError?.Invoke(this, ex);
                }
            }
        }

        public void Dispose()
        {
            _cts?.Cancel();
            _udpClient?.Dispose();
            _cts?.Dispose();
        }
    }
}
