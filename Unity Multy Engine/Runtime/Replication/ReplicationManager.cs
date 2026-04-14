using System.Collections.Generic;
using MyNetEngine.Core.Buffers;
using MyNetEngine.Core.Serialization;
using MyNetEngine.Interest;
using MyNetEngine.Objects;
using MyNetEngine.Transport;

namespace MyNetEngine.Replication
{
    /// <summary>
    /// per-connection replication 상태 + tick별 snapshot 전송.
    /// - InterestManager로 observable 필터링
    /// - PriorityScheduler로 우선순위
    /// - bandwidth budget 내에서만 포함
    /// - 처음 보낸 오브젝트는 initial 전체 상태 포함
    /// </summary>
    public sealed class ReplicationManager
    {
        private sealed class ConnState
        {
            public readonly HashSet<uint> Known = new HashSet<uint>();
            public readonly HashSet<uint> PendingInitial = new HashSet<uint>();
            public readonly Dictionary<uint, uint> LastSentTick = new Dictionary<uint, uint>();
            public float ObserverX, ObserverY, ObserverZ;
            public int BandwidthBudgetPerTick = 8 * 1024; // 8KB/tick 기본
        }

        private readonly Dictionary<int, ConnState> _conns = new Dictionary<int, ConnState>();
        private readonly SnapshotBuilder _builder = new SnapshotBuilder();
        private readonly PriorityScheduler _scheduler = new PriorityScheduler();
        private readonly InterestManager _interest;
        private readonly SpawnManager _spawn;
        private readonly ITransport _transport;

        public ReplicationManager(ITransport transport, SpawnManager spawn, InterestManager interest)
        {
            _transport = transport;
            _spawn = spawn;
            _interest = interest;
        }

        public void AddConnection(int connId)
        {
            if (!_conns.ContainsKey(connId)) _conns[connId] = new ConnState();
        }

        public void RemoveConnection(int connId) => _conns.Remove(connId);

        public void SetObserverPosition(int connId, float x, float y, float z)
        {
            if (_conns.TryGetValue(connId, out var s))
            {
                s.ObserverX = x; s.ObserverY = y; s.ObserverZ = z;
            }
        }

        public void SetBudget(int connId, int bytesPerTick)
        {
            if (_conns.TryGetValue(connId, out var s)) s.BandwidthBudgetPerTick = bytesPerTick;
        }

        /// <summary>
        /// 서버에서 object가 새로 스폰되었을 때 모든 관심 있는 conn에 pending initial 등록.
        /// </summary>
        public void MarkAllPendingInitial(uint netId)
        {
            foreach (var kv in _conns) kv.Value.PendingInitial.Add(netId);
        }

        public void OnObjectDespawned(uint netId)
        {
            foreach (var kv in _conns)
            {
                kv.Value.Known.Remove(netId);
                kv.Value.PendingInitial.Remove(netId);
                kv.Value.LastSentTick.Remove(netId);
            }
        }

        /// <summary>
        /// 매 tick 호출. 각 conn에 snapshot을 만들어 전송.
        /// </summary>
        public void Tick(uint tick)
        {
            var working = new List<NetworkObject>(64);
            foreach (var kv in _conns)
            {
                int connId = kv.Key;
                var s = kv.Value;

                working.Clear();
                _interest.GatherObservable(connId, s.ObserverX, s.ObserverY, s.ObserverZ, _spawn, working);

                var scored = _scheduler.Score(working, connId, s.ObserverX, s.ObserverY, s.ObserverZ, s.LastSentTick, tick);

                // budget 기반 상위 선택
                var selected = new List<NetworkObject>(working.Count);
                var initials = new HashSet<uint>();
                int estimated = 32; // header + count 여유
                for (int i = 0; i < scored.Count; i++)
                {
                    var o = scored[i].Obj;
                    int approx = 32; // 대략적 per-object 예상 크기
                    if (s.PendingInitial.Contains(o.NetId))
                    {
                        initials.Add(o.NetId);
                        approx += 64;
                    }
                    if (estimated + approx > s.BandwidthBudgetPerTick) break;
                    estimated += approx;
                    selected.Add(o);
                }

                if (selected.Count == 0) continue;

                var buf = ByteBufferPool.Shared.Rent(s.BandwidthBudgetPerTick + 256);
                var w = new NetWriter(buf);
                _builder.Build(w, tick, selected, initials);
                _transport.Send(connId, w.ToSegment(), DeliveryChannel.UnreliableSequenced);
                ByteBufferPool.Shared.Return(buf);

                // 반영: known/pending/lastSent 업데이트
                for (int i = 0; i < selected.Count; i++)
                {
                    var o = selected[i];
                    s.Known.Add(o.NetId);
                    s.PendingInitial.Remove(o.NetId);
                    s.LastSentTick[o.NetId] = tick;
                }

                // 전송 후 객체 dirty 클리어는 replication-level이 아니라 시뮬레이션 말미에서 일괄
            }

            // 모든 객체에 대해 dirty clear 한 번만
            foreach (var kv in _spawn.AllObjects)
            {
                foreach (var b in kv.Value.Behaviours) b.ClearDirty();
            }
        }
    }
}
