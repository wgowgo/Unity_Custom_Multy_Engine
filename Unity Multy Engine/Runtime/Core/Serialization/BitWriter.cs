using System;

namespace MyNetEngine.Core.Serialization
{
    /// <summary>
    /// bit 단위 writer. dirty bitmap, packed flags, 좁은 범위 int에 사용.
    /// </summary>
    public sealed class BitWriter
    {
        private readonly NetWriter _writer;
        private ulong _scratch;
        private int _scratchBits;

        public BitWriter(NetWriter writer) { _writer = writer; }

        public void WriteBits(uint value, int bits)
        {
            if (bits <= 0 || bits > 32) throw new ArgumentOutOfRangeException(nameof(bits));
            ulong masked = value & ((1UL << bits) - 1);
            _scratch |= masked << _scratchBits;
            _scratchBits += bits;
            while (_scratchBits >= 8)
            {
                _writer.WriteByte((byte)(_scratch & 0xFF));
                _scratch >>= 8;
                _scratchBits -= 8;
            }
        }

        public void WriteBool(bool v) => WriteBits(v ? 1u : 0u, 1);

        /// <summary>
        /// 현재 scratch를 byte 경계로 flush.
        /// </summary>
        public void Flush()
        {
            if (_scratchBits > 0)
            {
                _writer.WriteByte((byte)(_scratch & 0xFF));
                _scratch = 0;
                _scratchBits = 0;
            }
        }
    }

    public sealed class BitReader
    {
        private readonly NetReader _reader;
        private ulong _scratch;
        private int _scratchBits;

        public BitReader(NetReader reader) { _reader = reader; }

        public uint ReadBits(int bits)
        {
            if (bits <= 0 || bits > 32) throw new ArgumentOutOfRangeException(nameof(bits));
            while (_scratchBits < bits)
            {
                ulong b = _reader.ReadByte();
                _scratch |= b << _scratchBits;
                _scratchBits += 8;
            }
            uint result = (uint)(_scratch & ((1UL << bits) - 1));
            _scratch >>= bits;
            _scratchBits -= bits;
            return result;
        }

        public bool ReadBool() => ReadBits(1) != 0;

        /// <summary>
        /// 현재 byte 경계로 정렬. 사용 안 한 scratch bit 버림.
        /// </summary>
        public void AlignToByte()
        {
            _scratch = 0;
            _scratchBits = 0;
        }
    }
}
