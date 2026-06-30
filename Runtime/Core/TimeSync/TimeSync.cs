using System;

namespace MyNetEngine.Core.TimeSync
{
    /// <summary>
    /// 서버-클라 시각 동기화. Ping/Pong 기반 RTT 측정 + 부드러운 보정.
    /// </summary>
    public sealed class TimeSync
    {
        private const int SampleWindow = 16;
        private readonly double[] _rttSamples = new double[SampleWindow];
        private int _sampleCount;
        private int _sampleIndex;

        public double SmoothedRtt { get; private set; }
        public double Jitter { get; private set; }

        public void AddRttSample(double rttSeconds)
        {
            _rttSamples[_sampleIndex] = rttSeconds;
            _sampleIndex = (_sampleIndex + 1) % SampleWindow;
            if (_sampleCount < SampleWindow) _sampleCount++;

            double sum = 0;
            for (int i = 0; i < _sampleCount; i++) sum += _rttSamples[i];
            double avg = sum / _sampleCount;

            double var = 0;
            for (int i = 0; i < _sampleCount; i++)
            {
                double d = _rttSamples[i] - avg;
                var += d * d;
            }
            SmoothedRtt = avg;
            Jitter = Math.Sqrt(var / _sampleCount);
        }

        public double RecommendedInterpolationDelay()
        {
            // 2 tick buffer 가정 + jitter 여유
            return SmoothedRtt * 0.5 + Jitter * 2.0;
        }
    }
}
