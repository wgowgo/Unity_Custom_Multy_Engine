using System;
using System.Text;

namespace MyNetEngine.Core.Serialization
{
    public sealed class NetReader
    {
        private byte[] _buf;
        private int _pos;
        private int _length;

        public int Position => _pos;
        public int Remaining => _length - _pos;

        public NetReader() { }

        public NetReader(byte[] buffer, int offset, int length)
        {
            Reset(buffer, offset, length);
        }

        public void Reset(byte[] buffer, int offset, int length)
        {
            _buf = buffer;
            _pos = offset;
            _length = offset + length;
        }

        public byte ReadByte()
        {
            if (_pos >= _length) throw new InvalidOperationException("NetReader EOF");
            return _buf[_pos++];
        }

        public bool ReadBool() => ReadByte() != 0;

        public ushort ReadUShort()
        {
            var a = ReadByte();
            var b = ReadByte();
            return (ushort)(a | (b << 8));
        }

        public short ReadShort() => (short)ReadUShort();

        public uint ReadUInt()
        {
            uint v = 0;
            v |= ReadByte();
            v |= (uint)ReadByte() << 8;
            v |= (uint)ReadByte() << 16;
            v |= (uint)ReadByte() << 24;
            return v;
        }

        public int ReadInt() => (int)ReadUInt();

        public ulong ReadULong()
        {
            ulong v = 0;
            for (int i = 0; i < 8; i++) v |= (ulong)ReadByte() << (i * 8);
            return v;
        }

        public long ReadLong() => (long)ReadULong();

        public unsafe float ReadFloat()
        {
            uint u = ReadUInt();
            return *(float*)&u;
        }

        public unsafe double ReadDouble()
        {
            ulong u = ReadULong();
            return *(double*)&u;
        }

        public uint ReadVarUInt()
        {
            uint result = 0;
            int shift = 0;
            while (true)
            {
                byte b = ReadByte();
                result |= (uint)(b & 0x7F) << shift;
                if ((b & 0x80) == 0) return result;
                shift += 7;
                if (shift > 35) throw new InvalidOperationException("VarUInt overflow");
            }
        }

        public int ReadVarInt()
        {
            uint zz = ReadVarUInt();
            return (int)((zz >> 1) ^ -(int)(zz & 1));
        }

        public ulong ReadVarULong()
        {
            ulong result = 0;
            int shift = 0;
            while (true)
            {
                byte b = ReadByte();
                result |= (ulong)(b & 0x7F) << shift;
                if ((b & 0x80) == 0) return result;
                shift += 7;
                if (shift > 70) throw new InvalidOperationException("VarULong overflow");
            }
        }

        public ArraySegment<byte> ReadBytes(int count)
        {
            if (_pos + count > _length) throw new InvalidOperationException("NetReader EOF");
            var seg = new ArraySegment<byte>(_buf, _pos, count);
            _pos += count;
            return seg;
        }

        public string ReadString()
        {
            uint raw = ReadVarUInt();
            if (raw == 0) return null;
            int len = (int)(raw - 1);
            if (_pos + len > _length) throw new InvalidOperationException("NetReader EOF");
            string s = Encoding.UTF8.GetString(_buf, _pos, len);
            _pos += len;
            return s;
        }

        public float ReadQuantizedFloat(float min, float max)
        {
            ushort q = ReadUShort();
            float t = q / 65535f;
            return min + t * (max - min);
        }
    }
}
