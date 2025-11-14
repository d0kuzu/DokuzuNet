using DokuzuNet.Core.Connection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DokuzuNet.Transprot
{
    public interface ITransport : IDisposable
    {
        Task StartServerAsync(int port, CancellationToken ct = default);
        Task StartClientAsync(string serverIp, int serverPort, bool isHost = false, CancellationToken ct = default);
        ValueTask SendToAsync(IConnection connection, ReadOnlyMemory<byte> data, CancellationToken ct = default);
        ValueTask BroadcastAsync(ReadOnlyMemory<byte> data, bool includeLocalClient = true, CancellationToken ct = default);
        Task StopAsync(CancellationToken ct = default);

        event Action<IConnection>? OnClientConnected;
        event Action<IConnection>? OnClientDisconnected;
        event Action<(IConnection connection, ReadOnlyMemory<byte> data)>? OnDataReceived;
        event Action<Exception>? OnError;

        IReadOnlyCollection<IConnection> GetConnections();
        IConnection? GetLocalClientConnection();
    }
}
