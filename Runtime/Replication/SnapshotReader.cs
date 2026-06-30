using MyNetEngine.Core.Serialization;
using MyNetEngine.Objects;

namespace MyNetEngine.Replication
{
    /// <summary>
    /// SnapshotBuilder 포맷을 읽어 적용.
    /// NetId가 없는 경우 (아직 Spawn 미수신) 해당 엔트리 skip.
    /// </summary>
    public static class SnapshotReader
    {
        public static uint Apply(NetReader r, SpawnManager spawn)
        {
            uint tick = r.ReadVarUInt();
            uint count = r.ReadVarUInt();
            for (uint i = 0; i < count; i++)
            {
                uint netId = r.ReadVarUInt();
                uint compCount = r.ReadVarUInt();
                bool has = spawn.TryGet(netId, out var obj);
                for (uint c = 0; c < compCount; c++)
                {
                    byte compIndex = r.ReadByte();
                    byte flags = r.ReadByte();
                    bool isInitial = (flags & 1) != 0;
                    uint bodyLen = r.ReadVarUInt();

                    if (has && compIndex < obj.Behaviours.Count)
                    {
                        var b = obj.Behaviours[compIndex];
                        // 본문만 감싼 sub reader
                        var sub = new NetReader();
                        // NetReader 내부 필드 접근을 public하게 하려면 확장 필요.
                        // 여기서는 현재 stream에서 직접 읽게 함: 바이트 길이 기반 forward.
                        int start = r.Position;
                        b.Deserialize(r, isInitial);
                        int consumed = r.Position - start;
                        if (consumed < (int)bodyLen) r.ReadBytes((int)bodyLen - consumed);
                    }
                    else
                    {
                        r.ReadBytes((int)bodyLen);
                    }
                }
            }
            return tick;
        }
    }
}
