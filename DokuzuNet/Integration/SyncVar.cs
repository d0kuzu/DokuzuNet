using DokuzuNet.Networking.Message;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DokuzuNet.Integration
{
    public record SyncVar(uint ObjectId, ushort BehaviourId, ushort VarId, byte[] Value) : IMessage;

    public class SyncVar<T> where T : struct // Для простоты — value types (int, float, bool, etc.)
    {
        private T _value;
        private readonly NetworkBehaviour _behaviour;
        private readonly ushort _varId;
        private readonly Action<T>? _onChanged;

        public T Value
        {
            get => _value;
            set
            {
                if (Equals(_value, value)) return;

                _value = value;
                _onChanged?.Invoke(value);

                if (_behaviour.NetworkObject?.IsLocalOwned == true)
                {
                    // Отправка на сервер (или сразу броадкаст, если сервер)
                    _behaviour.SendSyncVarAsync(_varId, _value);
                }
            }
        }

        internal SyncVar(NetworkBehaviour behaviour, ushort varId, T initialValue, Action<T>? onChanged = null)
        {
            _behaviour = behaviour;
            _varId = varId;
            _value = initialValue;
            _onChanged = onChanged;
        }

        internal void SetWithoutNotify(T value)
        {
            _value = value;
        }
    }
}
