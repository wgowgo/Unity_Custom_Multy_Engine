using System.Collections.Generic;
using MyNetEngine.Objects;

namespace MyNetEngine.Replication
{
    /// <summary>
    /// 연결별로 오브젝트 전송 우선순위 산정.
    /// 높을수록 먼저 보낸다. bandwidth budget과 함께 사용.
    /// 기본 규칙:
    ///   1) 자기 오너 오브젝트: 최상위
    ///   2) 근거리: 거리 역비례
    ///   3) 최근 미전송: 누적 대기 시간 보너스
    /// </summary>
    public sealed class PriorityScheduler
    {
        public struct Scored
        {
            public NetworkObject Obj;
            public float Score;
        }

        private readonly List<Scored> _scratch = new List<Scored>(256);

        public IReadOnlyList<Scored> Score(
            IReadOnlyList<NetworkObject> observable,
            int connectionId,
            float observerX, float observerY, float observerZ,
            IReadOnlyDictionary<uint, uint> lastSentTick,
            uint currentTick)
        {
            _scratch.Clear();
            for (int i = 0; i < observable.Count; i++)
            {
                var o = observable[i];
                float score = 0f;

                if (o.OwnerConnectionId == connectionId) score += 1000f;

                float dx = o.PosX - observerX;
                float dy = o.PosY - observerY;
                float dz = o.PosZ - observerZ;
                float distSqr = dx * dx + dy * dy + dz * dz;
                // 가까울수록 +, 멀수록 감소. 100m 이내에서 의미있음.
                score += 500f / (1f + distSqr * 0.01f);

                if (lastSentTick != null && lastSentTick.TryGetValue(o.NetId, out var last))
                {
                    uint age = currentTick - last;
                    score += age * 2f; // 오래 안 보낸 오브젝트 보너스
                }
                else
                {
                    score += 250f; // 한 번도 보낸 적 없음
                }

                _scratch.Add(new Scored { Obj = o, Score = score });
            }
            _scratch.Sort((a, b) => b.Score.CompareTo(a.Score));
            return _scratch;
        }
    }
}
