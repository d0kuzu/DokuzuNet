using DokuzuNet.Core;
using DokuzuNet.Core.Connection;
using DokuzuNet.Networking.Message.Messages;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DokuzuNet.Networking
{
    public class NetworkPlayer
    {
        public IConnection Connection { get; }
        public bool IsLocal => Connection == NetworkManager.Instance?.LocalConnection;
        public bool IsServer => NetworkManager.Instance?.IsServer == true;
        public bool IsHost => NetworkManager.Instance?.Mode == NetworkMode.Host;

        internal NetworkPlayer(IConnection connection)
        {
            Connection = connection;
        }

        public async ValueTask SendAsync<T>(T message) where T : IMessage
        {
            await NetworkManager.Instance!.SendToAsync(this, message);
        }

        public override string ToString() => $"{Connection.EndPoint} (Local: {IsLocal})";
    }
}
