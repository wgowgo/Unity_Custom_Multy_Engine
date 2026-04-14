using System;
using System.Runtime.CompilerServices;
using System.Text;

namespace MyNetEngine.Core.Serialization
{
    /// <summary>
    /// 바이트 기반 writer. 내부 버퍼 grow. 필요시 외부 버퍼로 초기화.
    /// 헤더 최소화 위해 varint 제공.
    /// </summary>
    public sealed class NetWriter
    {
        private byte[] _buf;
        private int _pos;
        private readonly bool _ownsBuffer;

        public int Position => _pos;
        public byte[] Buffer => _buf;
        public ArraySegment<byte> ToSegment() => new ArraySegment<byte>(_buf, 0, _pos);

        public NetWriter(int capacity = 1024)
        {
            _buf = new byte[capacity];
            _ownsBuffer = true;
        }

        public NetWriter(byte[] external)
        {
            _buf = external;
            _ownsBuffer = false;
        }

        public void Reset() => _pos = 0;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void EnsureCapacity(int extra)
        {
            int need = _pos + extra;
            if (need <= _buf.Length) return;
            if (!_ownsBuffer) throw new InvalidOperationException("Writer backed by external buffer overflowed.");
            int newSize = Math.Max(_buf.Length * 2, need);
            Array.Resize(ref _buf, newSize);
        }

        public void WriteByte(byte v)
        {
            EnsureCapacity(1);
            _buf[_pos++] = v;
        }

        public void WriteBool(bool v) => WriteByte(v ? (byte)1 : (byte)0);

        public void WriteUShort(ushort v)
        {
            EnsureCapacity(2);
            _buf[_pos++] = (byte)v;
            _buf[_pos++] = (byte)(v >> 8);
        }

        public void WriteShort(short v) => WriteUShort((ushort)v);

        public void WriteUInt(uint v)
        {
            EnsureCapacity(4);
            _buf[_pos++] = (byte)v;
            _buf[_pos++] = (byte)(v >> 8);
            _buf[_pos++] = (byte)(v >> 16);
            _buf[_pos++] = (byte)(v >> 24);
        }

        public void WriteInt(int v) => WriteUInt((uint)v);

        public void WriteULong(ulong v)
        {
            EnsureCapacity(8);
            for (int i = 0; i < 8; i++) _buf[_pos++] = (byte)(v >> (i * 8));
        }

        public void WriteLong(long v) => WriteULong((ulong)v);

        public unsafe void WriteFloat(float v)
        {
            uint u = *(uint*)&v;
            WriteUInt(u);
        }

        public unsafe void WriteDouble(double v)
        {
            ulong u = *(ulong*)&v;
            WriteULong(u);
        }

        /// <summary>
        /// Zig-zag + varint. 작은 정수에 매우 효율적.
        /// </summary>
        public void WriteVarInt(int v)
        {
            uint zz = (uint)((v << 1) ^ (v >> 31));
            WriteVarUInt(zz);
        }

        public void WriteVarUInt(uint v)
        {
            while (v >= 0x80)
            {
                WriteByte((byte)(v | 0x80));
                v >>= 7;
            }
            WriteByte((byte)v);
        }

        public void WriteVarULong(ulong v)
        {
            while (v >= 0x80)
            {
                WriteByte((byte)(v | 0x80));
                v >>= 7;
            }
            WriteByte((byte)v);
        }

        public void WriteBytes(ReadOnlySpan<byte> data)
        {
            EnsureCapacity(data.Length);
            data.CopyTo(_buf.AsSpan(_pos));
            _pos += data.Length;
        }

        public void WriteString(string s)
        {
            if (s == null) { WriteVarUInt(0); return; }
            int byteCount = Encoding.UTF8.GetByteCount(s);
            WriteVarUInt((uint)(byteCount + 1));
            EnsureCapacity(byteCount);
            Encoding.UTF8.GetBytes(s, 0, s.Length, _buf, _pos);
            _pos += byteCount;
        }

        /// <summary>
        /// quantized float [min..max]. 16bit 정밀도.
        /// </summary>
        public void WriteQuantizedFloat(float v, float min, float max)
        {
            float t = (v - min) / (max - min);
            if (t < 0) t = 0; else if (t > 1) t = 1;
            ushort q = (ushort)(t * 65535f);
            WriteUShort(q);
        }
    }
}
