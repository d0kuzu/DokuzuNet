using DokuzuNet.Utils;
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DokuzuNet.Server
{
    public class ServerManager : IDisposable
    {
        private UdpClient _udpServer;
        private IPEndPoint _localEndPoint;
        private CancellationTokenSource _cts;
        private Task _receiveTask;
        private bool _isRunning;

        private const int DefaultPort = 11000;

        public ServerManager(int port = DefaultPort)
        {
            _localEndPoint = new IPEndPoint(IPAddress.Any, port);
            _udpServer = new UdpClient(_localEndPoint);
            _cts = new CancellationTokenSource();
        }

        public async Task StartAsync()
        {
            if (_isRunning)
            {
                Logger.Info("UDP server already started.");
                return;
            }
            _isRunning = true;

            _receiveTask = ReceiveLoopAsync(_cts.Token);

            Logger.Info($"UDP server started on port {_localEndPoint.Port}");
        }

        public async Task StopAsync()
        {
            if (!_isRunning)
            {
                Logger.Info("UDP server already stopped.");
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
                Logger.Error($"Error on server stop: {ex.Message}");
            }
            finally
            {
                _udpServer.Close();
                Logger.Info("UDP server stopped.");
            }
        }

        private async Task ReceiveLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var result = await _udpServer.ReceiveAsync(token).ConfigureAwait(false);
                    var message = Encoding.UTF8.GetString(result.Buffer);
                    var remote = result.RemoteEndPoint;

                    Logger.Info($"Получено от {remote}: {message}");

                    await SendResponseAsync($"Эхо: {message}", remote, token);
                }
                catch (OperationCanceledException) { break; }
                catch (ObjectDisposedException) { break; }
                catch (Exception ex)
                {
                    Logger.Error($"Ошибка приёма: {ex.Message}");
                }
            }
        }

        private async Task SendResponseAsync(string response, IPEndPoint remote, CancellationToken token)
        {
            try
            {
                var data = Encoding.UTF8.GetBytes(response);
                var memory = new ReadOnlyMemory<byte>(data);

                await _udpServer.SendAsync(memory, remote, token).ConfigureAwait(false);

                Logger.Info($"Отправлено на {remote}: {response}");
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Logger.Error($"Ошибка отправки на {remote}: {ex.Message}");
            }
        }

        public void Dispose()
        {
            _cts?.Cancel();
            try { _receiveTask?.Wait(2000); } catch { }
            _udpServer?.Dispose();
            _cts?.Dispose();
        }
    }
}