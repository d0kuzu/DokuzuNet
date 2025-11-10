using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace DokuzuNet.Core.Connection
{
    public class UdpConnection : IConnection
    {
        private readonly UdpClient _udpClient;
        private readonly IPEndPoint _endPoint;
        private bool _isConnected = true;

        public IPEndPoint EndPoint => _endPoint;
        public bool IsConnected => _isConnected;
        public DateTime LastReceived { get; private set; }
        public DateTime LastSent { get; private set; }

        public UdpConnection(UdpClient udpClient, IPEndPoint endPoint)
        {
            _udpClient = udpClient ?? throw new ArgumentNullException(nameof(udpClient));
            _endPoint = endPoint ?? throw new ArgumentNullException(nameof(endPoint));
            LastReceived = LastSent = DateTime.UtcNow;
        }

        public async ValueTask SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
        {
            if (!_isConnected) throw new ObjectDisposedException(nameof(UdpConnection));

            try
            {
                await _udpClient.SendAsync(data, _endPoint, ct).ConfigureAwait(false);
                UpdateLastSent();
            }
            catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
            {
                Disconnect();
                throw;
            }
        }

        public void Disconnect()
        {
            if (!_isConnected) return;
            _isConnected = false;
        }

        public void UpdateLastReceived() => LastReceived = DateTime.UtcNow;
        public void UpdateLastSent() => LastSent = DateTime.UtcNow;

        public void Dispose() => Disconnect();

        public override string ToString() => $"{_endPoint} (Conn: {_isConnected})";
    }
}
