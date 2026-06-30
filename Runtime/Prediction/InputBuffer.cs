using System.Collections.Generic;

namespace MyNetEngine.Prediction
{
    /// <summary>
    /// owner 입력의 tick별 링버퍼.
    /// 서버: 입력 수신 후 tick에 맞춰 consume.
    /// 클라: 로컬 재시뮬레이션용으로 과거 입력 유지.
    /// </summary>
    public sealed class InputBuffer<TInput>
    {
        private readonly TInput[] _ring;
        private readonly int _capacity;
        private uint _minTick;
        private uint _maxTick;
        private bool _hasAny;

        public InputBuffer(int capacity = 64)
        {
            _capacity = capacity;
            _ring = new TInput[capacity];
        }

        public void Set(uint tick, in TInput input)
        {
            _ring[tick % _capacity] = input;
            if (!_hasAny || tick > _maxTick) _maxTick = tick;
            if (!_hasAny || tick < _minTick) _minTick = tick;
            _hasAny = true;
            if (_maxTick - _minTick >= (uint)_capacity)
                _minTick = _maxTick - (uint)_capacity + 1;
        }

        public bool TryGet(uint tick, out TInput input)
        {
            if (!_hasAny || tick < _minTick || tick > _maxTick)
            {
                input = default;
                return false;
            }
            input = _ring[tick % _capacity];
            return true;
        }

        public uint LatestTick => _maxTick;
        public uint OldestTick => _minTick;

        public void GetRange(uint fromTick, uint toTickInclusive, List<TInput> output)
        {
            output.Clear();
            for (uint t = fromTick; t <= toTickInclusive; t++)
            {
                if (TryGet(t, out var inp)) output.Add(inp);
            }
        }
    }
}
