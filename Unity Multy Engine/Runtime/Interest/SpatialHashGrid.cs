using System.Collections.Generic;
using MyNetEngine.Objects;

namespace MyNetEngine.Interest
{
    /// <summary>
    /// 2D XZ 그리드 기반 spatial hash. Y 무시 (대부분 맵에 적합). 필요시 3D로 교체.
    /// </summary>
    public sealed class SpatialHashGrid
    {
        public readonly float CellSize;
        private readonly Dictionary<long, List<NetworkObject>> _cells = new Dictionary<long, List<NetworkObject>>();
        private readonly Dictionary<uint, long> _byId = new Dictionary<uint, long>();

        public SpatialHashGrid(float cellSize = 20f)
        {
            CellSize = cellSize;
        }

        private long Hash(int cx, int cz) => ((long)cx << 32) ^ (uint)cz;

        public void Insert(NetworkObject obj)
        {
            int cx = (int)(obj.PosX / CellSize);
            int cz = (int)(obj.PosZ / CellSize);
            long key = Hash(cx, cz);
            if (_byId.TryGetValue(obj.NetId, out var prev) && prev == key) return;
            if (_byId.ContainsKey(obj.NetId)) Remove(obj.NetId);
            if (!_cells.TryGetValue(key, out var list))
            {
                list = new List<NetworkObject>(8);
                _cells[key] = list;
            }
            list.Add(obj);
            _byId[obj.NetId] = key;
        }

        public void UpdatePosition(NetworkObject obj, float x, float y, float z)
        {
            obj.PosX = x; obj.PosY = y; obj.PosZ = z;
            Insert(obj);
        }

        public void Remove(uint netId)
        {
            if (!_byId.TryGetValue(netId, out var key)) return;
            if (_cells.TryGetValue(key, out var list))
            {
                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i].NetId == netId) { list.RemoveAt(i); break; }
                }
                if (list.Count == 0) _cells.Remove(key);
            }
            _byId.Remove(netId);
        }

        public void QueryRadius(float x, float z, float radius, List<NetworkObject> output)
        {
            int r = (int)(radius / CellSize) + 1;
            int ccx = (int)(x / CellSize);
            int ccz = (int)(z / CellSize);
            float r2 = radius * radius;
            for (int dx = -r; dx <= r; dx++)
            {
                for (int dz = -r; dz <= r; dz++)
                {
                    if (!_cells.TryGetValue(Hash(ccx + dx, ccz + dz), out var list)) continue;
                    for (int i = 0; i < list.Count; i++)
                    {
                        var o = list[i];
                        float ex = o.PosX - x, ez = o.PosZ - z;
                        if (ex * ex + ez * ez <= r2) output.Add(o);
                    }
                }
            }
        }
    }
}
