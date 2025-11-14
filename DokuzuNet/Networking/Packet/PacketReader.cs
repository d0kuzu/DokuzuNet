using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DokuzuNet.Networking.Packet
{
    public sealed class PacketReader
    {
        private readonly ReadOnlyMemory<byte> _data;
        private int _position;

        public int Position => _position;
        public int Remaining => _data.Length - _position;
        public bool EndOfData => _position >= _data.Length;

        public PacketReader(ReadOnlyMemory<byte> data)
        {
            _data = data;
            _position = 0;
        }

        private void EnsureAvailable(int bytes)
        {
            if (_position + bytes > _data.Length)
                throw new InvalidOperationException("Not enough data to read.");
        }

        public byte ReadByte()
        {
            EnsureAvailable(1);
            return _data.Span[_position++];
        }

        public ushort ReadUShort()
        {
            EnsureAvailable(2);
            var value = BinaryPrimitives.ReadUInt16LittleEndian(_data.Span.Slice(_position));
            _position += 2;
            return value;
        }

        public int ReadInt()
        {
            EnsureAvailable(4);
            var value = BinaryPrimitives.ReadInt32LittleEndian(_data.Span.Slice(_position));
            _position += 4;
            return value;
        }

        public uint ReadUInt()
        {
            EnsureAvailable(4);
            var value = BinaryPrimitives.ReadUInt32LittleEndian(_data.Span.Slice(_position));
            _position += 4;
            return value;
        }

        public float ReadFloat()
        {
            EnsureAvailable(4);
            var bits = BinaryPrimitives.ReadInt32LittleEndian(_data.Span.Slice(_position));
            _position += 4;
            return BitConverter.Int32BitsToSingle(bits);
        }

        public string ReadString()
        {
            var length = ReadUShort();
            if (length == 0) return string.Empty;

            EnsureAvailable(length);
            var str = Encoding.UTF8.GetString(_data.Span.Slice(_position, length));
            _position += length;
            return str;
        }

        public ReadOnlyMemory<byte> ReadBytes(int count)
        {
            EnsureAvailable(count);
            var slice = _data.Slice(_position, count);
            _position += count;
            return slice;
        }

        public void Skip(int count)
        {
            EnsureAvailable(count);
            _position += count;
        }
    }
}
