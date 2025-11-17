using DokuzuNet.Networking.Message.Messages;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace DokuzuNet.Integration
{
    public record SyncVarMessage(uint ObjectId, ushort BehaviourId, ushort VarId, byte[] Value) : IMessage;
    public class SyncVar<T> where T : struct
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
                if (EqualityComparer<T>.Default.Equals(_value, value)) return;

                _value = value;
                _onChanged?.Invoke(value);

                // Отправляем только если мы владелец (клиент или сервер)
                if (_behaviour.NetworkObject?.IsLocalOwned == true)
                {
                    _behaviour.SendSyncVarAsync(_varId, value).ConfigureAwait(false);
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

        // Внутренний метод для десериализации
        internal void SetFromBytes(byte[] data)
        {
            if (typeof(T) == typeof(int))
            {
                SetWithoutNotify(Unsafe.As<byte, T>(ref data[0]));
            }
            else if (typeof(T) == typeof(float))
            {
                SetWithoutNotify(Unsafe.As<byte, T>(ref data[0]));
            }
            else if (typeof(T) == typeof(uint))
            {
                SetWithoutNotify(Unsafe.As<byte, T>(ref data[0]));
            }
            else
            {
                throw new NotSupportedException($"SyncVar type {typeof(T)} not supported.");
            }
        }
    }
}
