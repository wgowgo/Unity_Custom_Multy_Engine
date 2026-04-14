using System;

namespace MyNetEngine.Core.Tick
{
    /// <summary>
    /// 서버/클라 공통 tick 시뮬레이션 단위.
    /// 서버: authoritative 시뮬레이션 구동.
    /// 클라: 입력 전송 + rendering interpolation.
    /// </summary>
    public sealed class NetworkTickSystem
    {
        public int TickRate { get; }
        public double FixedDeltaTime => 1.0 / TickRate;

        public uint CurrentTick { get; private set; }
        public double LocalTime { get; private set; }
        public double ServerTimeOffset { get; private set; }
        public double ServerTime => LocalTime + ServerTimeOffset;

        private double _accumulator;

        public event Action<uint> OnTick;

        public NetworkTickSystem(int tickRate = 30)
        {
            if (tickRate <= 0) throw new ArgumentException("tickRate must be > 0");
            TickRate = tickRate;
        }

        /// <summary>
        /// Unity Update/FixedUpdate에서 호출. deltaTime 누적 후 tickRate 기반으로 OnTick 호출.
        /// </summary>
        public void Advance(double deltaTime)
        {
            LocalTime += deltaTime;
            _accumulator += deltaTime;
            double step = FixedDeltaTime;
            while (_accumulator >= step)
            {
                _accumulator -= step;
                CurrentTick++;
                OnTick?.Invoke(CurrentTick);
            }
        }

        /// <summary>
        /// 현재 tick 내에서 rendering interpolation alpha [0..1].
        /// </summary>
        public float InterpolationAlpha => (float)(_accumulator / FixedDeltaTime);

        /// <summary>
        /// 서버로부터 받은 time으로 offset 보정.
        /// </summary>
        public void SetServerTime(double serverTime, double measuredRtt)
        {
            // 단순 보정: 서버 시각 + 반 RTT
            double estimated = serverTime + measuredRtt * 0.5;
            ServerTimeOffset = estimated - LocalTime;
        }

        public uint TickAt(double time)
        {
            if (time < 0) return 0;
            return (uint)Math.Floor(time / FixedDeltaTime);
        }
    }
}
