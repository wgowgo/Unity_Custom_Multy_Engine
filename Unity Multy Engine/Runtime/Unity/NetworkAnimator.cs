using MyNetEngine.Core.Serialization;
using MyNetEngine.Objects;

namespace MyNetEngine.Unity
{
    /// <summary>
    /// 애니메이션 파라미터 동기화. float 4개 + bool 8개 + int 4개 슬롯 예시.
    /// 실제 통합에서는 Unity Animator.hash 매핑 테이블과 연결.
    /// </summary>
    public sealed class NetworkAnimator : NetBehaviour
    {
        public const int FloatSlots = 4;
        public const int BoolSlots = 8;
        public const int IntSlots = 4;

        public readonly float[] Floats = new float[FloatSlots];
        public readonly bool[] Bools = new bool[BoolSlots];
        public readonly int[] Ints = new int[IntSlots];

        private uint _dirtyMask;

        public void SetFloat(int slot, float v)
        {
            if (Floats[slot] != v) { Floats[slot] = v; _dirtyMask |= 1u << slot; }
        }
        public void SetBool(int slot, bool v)
        {
            if (Bools[slot] != v) { Bools[slot] = v; _dirtyMask |= 1u << (FloatSlots + slot); }
        }
        public void SetInt(int slot, int v)
        {
            if (Ints[slot] != v) { Ints[slot] = v; _dirtyMask |= 1u << (FloatSlots + BoolSlots + slot); }
        }

        public override bool IsDirty() => _dirtyMask != 0;
        public override void ClearDirty() => _dirtyMask = 0;

        public override void Serialize(NetWriter w, bool isInitial)
        {
            uint mask = isInitial ? (uint)((1 << (FloatSlots + BoolSlots + IntSlots)) - 1) : _dirtyMask;
            w.WriteVarUInt(mask);
            int bit = 0;
            for (int i = 0; i < FloatSlots; i++, bit++)
                if ((mask & (1u << bit)) != 0) w.WriteFloat(Floats[i]);
            for (int i = 0; i < BoolSlots; i++, bit++)
                if ((mask & (1u << bit)) != 0) w.WriteBool(Bools[i]);
            for (int i = 0; i < IntSlots; i++, bit++)
                if ((mask & (1u << bit)) != 0) w.WriteVarInt(Ints[i]);
        }

        public override void Deserialize(NetReader r, bool isInitial)
        {
            uint mask = r.ReadVarUInt();
            int bit = 0;
            for (int i = 0; i < FloatSlots; i++, bit++)
                if ((mask & (1u << bit)) != 0) Floats[i] = r.ReadFloat();
            for (int i = 0; i < BoolSlots; i++, bit++)
                if ((mask & (1u << bit)) != 0) Bools[i] = r.ReadBool();
            for (int i = 0; i < IntSlots; i++, bit++)
                if ((mask & (1u << bit)) != 0) Ints[i] = r.ReadVarInt();
        }
    }
}
