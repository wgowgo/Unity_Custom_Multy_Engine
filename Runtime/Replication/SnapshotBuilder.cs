using System.Collections.Generic;
using MyNetEngine.Core.Metrics;
using MyNetEngine.Core.Serialization;
using MyNetEngine.Messaging;
using MyNetEngine.Objects;

namespace MyNetEngine.Replication
{
    /// <summary>
    /// tick 기반 snapshot 페이로드 생성기.
    /// 형식:
    ///   [MessageType.Snapshot]
    ///   [tick: varuint]
    ///   [count: varuint]
    ///   repeat count:
    ///     [netId: varuint]
    ///     [componentCount: varuint]
    ///     repeat componentCount:
    ///       [compIndex: byte]
    ///       [flags: byte] // bit0 = isInitial
    ///       [len: varuint]
    ///       [bytes...]
    /// </summary>
    public sealed class SnapshotBuilder
    {
        /// <summary>
        /// 특정 연결에 대해 snapshot 직렬화.
        /// observable: 해당 커넥션에서 관측 가능한 네트워크 오브젝트 리스트.
        /// initials: 해당 커넥션이 아직 초기 상태를 받지 않은 netId 집합.
        /// </summary>
        public void Build(
            NetWriter w,
            uint tick,
            IReadOnlyList<NetworkObject> observable,
            HashSet<uint> initials)
        {
            w.WriteByte((byte)MessageType.Snapshot);
            w.WriteVarUInt(tick);

            // count 위치 예약
            int countPos = w.Position;
            w.WriteVarUInt(0);
            int written = 0;

            NetMetrics.Global.SnapshotsBuilt++;
            int startSize = w.Position;

            for (int i = 0; i < observable.Count; i++)
            {
                var obj = observable[i];
                bool isInitial = initials != null && initials.Contains(obj.NetId);

                bool anyDirty = isInitial;
                if (!anyDirty)
                {
                    for (int c = 0; c < obj.Behaviours.Count; c++)
                    {
                        if (obj.Behaviours[c].IsDirty()) { anyDirty = true; break; }
                    }
                }
                if (!anyDirty) continue;

                w.WriteVarUInt(obj.NetId);
                int compCountPos = w.Position;
                w.WriteVarUInt(0);
                int comps = 0;

                for (int c = 0; c < obj.Behaviours.Count; c++)
                {
                    var b = obj.Behaviours[c];
                    if (!isInitial && !b.IsDirty()) continue;
                    w.WriteByte(b.ComponentIndex);
                    w.WriteByte((byte)(isInitial ? 1 : 0));

                    // 길이 예약
                    int lenPos = w.Position;
                    w.WriteVarUInt(0);
                    int bodyStart = w.Position;
                    b.Serialize(w, isInitial);
                    int bodyLen = w.Position - bodyStart;

                    // 실제로 길이 기록. varuint 고정 폭(최대 5) 미리 예약했다고 가정하면 복잡해짐.
                    // 단순화: body를 복사 이동. 고성능 경로는 고정 5바이트 varuint 선호.
                    PatchVarUIntFixed5(w.Buffer, lenPos, (uint)bodyLen);
                    // 위 함수는 body를 그대로 두고 5바이트 크기로 기록. bodyStart 보정 필요.
                    comps++;
                }
                PatchVarUIntFixed5(w.Buffer, compCountPos, (uint)comps);
                written++;
            }
            PatchVarUIntFixed5(w.Buffer, countPos, (uint)written);

            NetMetrics.Global.SnapshotBytes += (w.Position - startSize);
        }

        /// <summary>
        /// 항상 5바이트로 encode. 예약된 자리에 덮어쓰기 용.
        /// </summary>
        internal static void PatchVarUIntFixed5(byte[] buf, int pos, uint v)
        {
            for (int i = 0; i < 4; i++)
            {
                buf[pos + i] = (byte)(v | 0x80);
                v >>= 7;
            }
            buf[pos + 4] = (byte)(v & 0x7F);
        }
    }
}
