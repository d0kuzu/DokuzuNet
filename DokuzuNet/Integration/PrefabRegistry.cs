using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DokuzuNet.Integration
{
    public class PrefabRegistry
    {
        private readonly Dictionary<ushort, string> _idToPrefab = new();
        private readonly Dictionary<string, ushort> _prefabToId = new();
        private ushort _nextId = 1;

        public void Register(string prefabName)
        {
            if (_prefabToId.ContainsKey(prefabName)) return;

            var id = _nextId++;
            _prefabToId[prefabName] = id;
            _idToPrefab[id] = prefabName;
        }

        public ushort GetId(string prefabName) => _prefabToId.GetValueOrDefault(prefabName);
        public string GetPrefab(ushort id) => _idToPrefab.GetValueOrDefault(id, string.Empty);
    }
}
