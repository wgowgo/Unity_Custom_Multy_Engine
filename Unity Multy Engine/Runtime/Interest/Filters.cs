using System.Collections.Generic;
using MyNetEngine.Objects;

namespace MyNetEngine.Interest
{
    /// <summary>
    /// 같은 씬/월드/룸에 있는 객체만 관측 허용.
    /// </summary>
    public sealed class SceneFilter : IObserverFilter
    {
        private readonly Dictionary<int, int> _connScene = new Dictionary<int, int>();
        private readonly Dictionary<uint, int> _objScene = new Dictionary<uint, int>();

        public void SetConnectionScene(int connId, int sceneId) => _connScene[connId] = sceneId;
        public void SetObjectScene(uint netId, int sceneId) => _objScene[netId] = sceneId;
        public void RemoveObject(uint netId) => _objScene.Remove(netId);

        public bool IsObservable(int connectionId, NetworkObject obj)
        {
            if (!_connScene.TryGetValue(connectionId, out var cs)) return true;
            if (!_objScene.TryGetValue(obj.NetId, out var os)) return true;
            return cs == os;
        }
    }

    /// <summary>
    /// 같은 팀만 보이게 (예: 코옵, 파티 전용 객체).
    /// 객체 team==0이면 공개.
    /// </summary>
    public sealed class TeamFilter : IObserverFilter
    {
        private readonly Dictionary<int, int> _connTeam = new Dictionary<int, int>();
        private readonly Dictionary<uint, int> _objTeam = new Dictionary<uint, int>();

        public void SetConnectionTeam(int connId, int team) => _connTeam[connId] = team;
        public void SetObjectTeam(uint netId, int team) => _objTeam[netId] = team;

        public bool IsObservable(int connectionId, NetworkObject obj)
        {
            if (!_objTeam.TryGetValue(obj.NetId, out var ot) || ot == 0) return true;
            if (!_connTeam.TryGetValue(connectionId, out var ct)) return false;
            return ct == ot;
        }
    }
}
