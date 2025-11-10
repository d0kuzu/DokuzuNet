using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace DokuzuNet.Core.Connection
{
    public interface IConnection : IDisposable
    {
        IPEndPoint EndPoint { get; }
        bool IsConnected { get; }
        DateTime LastReceived { get; }
        DateTime LastSent { get; }

        ValueTask SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default);
        void Disconnect();
        void UpdateLastReceived();
        void UpdateLastSent();
    }
}
