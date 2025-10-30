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
        private Task? _receiveTask;
        private bool _isRunning;

        private const int DefaultPort = 11000;

        public ServerManager(int port = DefaultPort)
        {
            _localEndPoint = new IPEndPoint(IPAddress.Any, port);
            _udpServer = new UdpClient(_localEndPoint);
            _cts = new CancellationTokenSource();
            _isRunning = false;
        }

        public async Task StartAsync()
        {
            if (_isRunning)
            {
                Logger.Info("UDP сервер уже запущен.");
                return;
            }

            _isRunning = true;
            _receiveTask = ReceiveLoopAsync(_cts.Token);

            Logger.Info($"UDP сервер запущен на порту {_localEndPoint.Port}");
            await Task.CompletedTask;
        }

        public async Task StopAsync()
        {
            if (!_isRunning)
            {
                Logger.Info("UDP сервер уже остановлен.");
                return;
            }

            _isRunning = false;
            _cts.Cancel();

            try
            {
                await _receiveTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Ожидаемое поведение
            }
            catch (Exception ex)
            {
                Logger.Error($"Ошибка при остановке: {ex.Message}");
            }
            finally
            {
                _udpServer?.Close();
                Logger.Info("UDP сервер остановлен.");
            }
        }

        private async Task ReceiveLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    UdpReceiveResult result = await _udpServer.ReceiveAsync()
                        .WithCancellation(token)
                        .ConfigureAwait(false);

                    string message = Encoding.UTF8.GetString(result.Buffer);
                    IPEndPoint remote = result.RemoteEndPoint;

                    Logger.Info($"Получено от {remote}: {message}");

                    await SendResponseAsync($"Эхо: {message}", remote, token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (SocketException ex) when (ex.SocketErrorCode == SocketError.OperationAborted)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Logger.Error($"Ошибка приёма UDP: {ex.Message}");
                }
            }
        }

        private async Task SendResponseAsync(string response, IPEndPoint remote, CancellationToken token)
        {
            try
            {
                byte[] data = Encoding.UTF8.GetBytes(response);
                await _udpServer.SendAsync(data, data.Length, remote)
                    .WithCancellation(token)
                    .ConfigureAwait(false);

                Logger.Info($"Отправлено на {remote}: {response}");
            }
            catch (OperationCanceledException)
            {
                // Игнорируем при отмене
            }
            catch (Exception ex)
            {
                Logger.Error($"Ошибка отправки на {remote}: {ex.Message}");
            }
        }

        public void Dispose()
        {
            _cts?.Cancel();
            try
            {
                _receiveTask?.Wait(TimeSpan.FromSeconds(2));
            }
            catch { /* игнорируем */ }

            _udpServer?.Dispose();
            _cts?.Dispose();
        }
    }

    public static class TaskExtensions
    {
        public static async Task<T> WithCancellation<T>(this Task<T> task, CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<bool>();
            using (cancellationToken.Register(s => ((TaskCompletionSource<bool>)s).TrySetResult(true), tcs))
            {
                if (task != await Task.WhenAny(task, tcs.Task).ConfigureAwait(false))
                    throw new OperationCanceledException(cancellationToken);
            }
            return await task.ConfigureAwait(false);
        }
    }
}