using DokuzuNet.Core;
using DokuzuNet.Networking;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DokuzuNet.Integration
{
    public class NetworkObject
    {
        public uint NetworkId { get; private set; }
        public NetworkPlayer? Owner { get; private set; }
        public bool IsSpawned { get; private set; }
        public bool IsLocalOwned => Owner == NetworkManager.Instance?.LocalPlayer;

        private readonly List<NetworkBehaviour> _behaviours = new();
        private ushort _nextBehaviourId = 1;

        internal void Initialize(uint id, NetworkPlayer owner)
        {
            NetworkId = id;
            Owner = owner;
            IsSpawned = true;
        }

        public T? GetBehaviour<T>() where T : NetworkBehaviour
        {
            foreach (var b in _behaviours)
            {
                if (b is T t) return t;
            }
            return null;
        }

        public IEnumerable<NetworkBehaviour> GetBehaviours()
        {
            return _behaviours;
        }

        public void AddBehaviour(NetworkBehaviour behaviour)
        {
            behaviour.NetworkObject = this;
            behaviour.BehaviourId = _nextBehaviourId++;
            _behaviours.Add(behaviour);
            if (IsSpawned)
                behaviour.InvokeOnSpawn();
        }

        public NetworkBehaviour? GetBehaviourById(ushort id)
        {
            return _behaviours.FirstOrDefault(b => b.BehaviourId == id);
        }

        internal void OnSpawn()
        {
            foreach (var b in _behaviours)
            {
                b.InvokeOnSpawn();
            }
        }

        internal void OnDespawn()
        {
            foreach (var b in _behaviours)
            {
                b.InvokeOnDespawn();
            }
            IsSpawned = false;
        }
    }
}
