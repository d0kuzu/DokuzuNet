using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DokuzuNet.Networking.Packet
{
    public sealed class PacketWriter : IDisposable
    {
        private const int DefaultCapacity = 256;
        private IMemoryOwner<byte> _owner;
        private Memory<byte> _buffer;
        private int _position;

        public int Position => _position;
        public int Capacity => _buffer.Length;
        public ReadOnlyMemory<byte> WrittenMemory => _buffer.Slice(0, _position);

        public PacketWriter(int capacity = DefaultCapacity)
        {
            _owner = MemoryPool<byte>.Shared.Rent(capacity);
            _buffer = _owner.Memory;
            _position = 0;
        }

        private void EnsureCapacity(int needed)
        {
            if (_position + needed > _buffer.Length)
            {
                int newSize = Math.Max(_buffer.Length * 2, _position + needed);
                var newOwner = MemoryPool<byte>.Shared.Rent(newSize);
                _buffer.Span.CopyTo(newOwner.Memory.Span);
                _owner.Dispose();
                _owner = newOwner;
                _buffer = _owner.Memory;
            }
        }

        public void WriteByte(byte value)
        {
            EnsureCapacity(1);
            _buffer.Span[_position++] = value;
        }

        public void WriteUShort(ushort value)
        {
            EnsureCapacity(2);
            BinaryPrimitives.WriteUInt16LittleEndian(_buffer.Span.Slice(_position), value);
            _position += 2;
        }

        public void WriteInt(int value)
        {
            EnsureCapacity(4);
            BinaryPrimitives.WriteInt32LittleEndian(_buffer.Span.Slice(_position), value);
            _position += 4;
        }

        public void WriteUInt(uint value)
        {
            EnsureCapacity(4);
            BinaryPrimitives.WriteUInt32LittleEndian(_buffer.Span.Slice(_position), value);
            _position += 4;
        }

        public void WriteFloat(float value)
        {
            EnsureCapacity(4);
            BinaryPrimitives.WriteInt32LittleEndian(_buffer.Span.Slice(_position), BitConverter.SingleToInt32Bits(value));
            _position += 4;
        }

        public void WriteString(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                WriteUShort(0);
                return;
            }

            int byteCount = Encoding.UTF8.GetByteCount(value);
            EnsureCapacity(2 + byteCount);

            WriteUShort((ushort)byteCount);
            Encoding.UTF8.GetBytes(value, _buffer.Span.Slice(_position));
            _position += byteCount;
        }

        public void WriteBytes(ReadOnlySpan<byte> data)
        {
            EnsureCapacity(data.Length);
            data.CopyTo(_buffer.Span.Slice(_position));
            _position += data.Length;
        }

        public ReadOnlyMemory<byte> GetBuffer() => _buffer.Slice(0, _position);

        public void Reset()
        {
            _position = 0;
        }

        public void Dispose()
        {
            _owner?.Dispose();
        }
    }
}
