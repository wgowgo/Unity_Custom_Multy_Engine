using System.Collections.Generic;
using MyNetEngine.Objects;

namespace MyNetEngine.Interest
{
    public interface IObserverFilter
    {
        bool IsObservable(int connectionId, NetworkObject obj);
    }

    /// <summary>
    /// 연결별로 관심 오브젝트 집합을 뽑아내는 관리자.
    /// SpatialHashGrid + team/scene/custom 필터 조합.
    /// tick budgeted: 매 tick 전수 재계산 금지, 일부 연결만 재갱신 권장.
    /// </summary>
    public sealed class InterestManager
    {
        public float DefaultRadius { get; set; } = 60f;
        public SpatialHashGrid Grid { get; }

        private readonly List<IObserverFilter> _filters = new List<IObserverFilter>();

        public InterestManager(float cellSize = 20f)
        {
            Grid = new SpatialHashGrid(cellSize);
        }

        public void AddFilter(IObserverFilter f) => _filters.Add(f);

        public void GatherObservable(
            int connectionId,
            float x, float y, float z,
            SpawnManager spawn,
            List<NetworkObject> output)
        {
            output.Clear();
            Grid.QueryRadius(x, z, DefaultRadius, output);

            if (_filters.Count == 0) return;
            for (int i = output.Count - 1; i >= 0; i--)
            {
                var o = output[i];
                bool pass = true;
                for (int f = 0; f < _filters.Count; f++)
                {
                    if (!_filters[f].IsObservable(connectionId, o)) { pass = false; break; }
                }
                if (!pass) output.RemoveAt(i);
            }
        }
    }
}
