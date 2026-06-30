using System.Collections.Generic;

namespace MyNetEngine.Prediction
{
    /// <summary>
    /// 원격(비 owner) 객체 이동 품질 핵심.
    /// 2~3 snapshot 버퍼링 후 보간.
    /// 짧은 extrapolation 허용.
    /// 순간이동 감지.
    /// </summary>
    public sealed class SnapshotInterpolator<TState>
    {
        public delegate TState LerpFn(in TState a, in TState b, float t);
        public delegate float DistanceFn(in TState a, in TState b);

        private readonly LerpFn _lerp;
        private readonly DistanceFn _distance;

        private readonly LinkedList<Entry> _buffer = new LinkedList<Entry>();
        public double InterpDelaySeconds { get; set; } = 0.1;
        public float TeleportThreshold { get; set; } = 5f;

        private struct Entry
        {
            public double Time;
            public TState State;
        }

        public SnapshotInterpolator(LerpFn lerp, DistanceFn distance)
        {
            _lerp = lerp;
            _distance = distance;
        }

        public void Push(double serverTime, in TState state)
        {
            if (_buffer.Count > 0)
            {
                var tail = _buffer.Last.Value;
                if (_distance(tail.State, state) > TeleportThreshold)
                {
                    _buffer.Clear();
                }
            }
            _buffer.AddLast(new Entry { Time = serverTime, State = state });
            while (_buffer.Count > 8) _buffer.RemoveFirst();
        }

        public bool Sample(double currentServerTime, out TState output)
        {
            output = default;
            if (_buffer.Count == 0) return false;

            double target = currentServerTime - InterpDelaySeconds;
            var node = _buffer.First;
            while (node.Next != null && node.Next.Value.Time <= target) node = node.Next;

            if (node.Next == null)
            {
                // extrapolation (짧게)
                output = node.Value.State;
                return true;
            }
            var a = node.Value;
            var b = node.Next.Value;
            float dt = (float)(b.Time - a.Time);
            if (dt <= 0) { output = b.State; return true; }
            float t = (float)((target - a.Time) / dt);
            if (t < 0) t = 0; else if (t > 1) t = 1;
            output = _lerp(a.State, b.State, t);
            return true;
        }

        public void Clear() => _buffer.Clear();
    }
}
