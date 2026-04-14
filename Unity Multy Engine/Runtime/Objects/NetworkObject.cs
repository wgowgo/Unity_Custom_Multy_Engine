using System.Collections.Generic;

namespace MyNetEngine.Objects
{
    /// <summary>
    /// 네트워크 오브젝트 단위. 고유 NetId + prefab hash + authority + 위치 캐시.
    /// NetBehaviour 들을 묶고 Replication/Interest 에 노출.
    /// </summary>
    public sealed class NetworkObject
    {
        public uint NetId { get; internal set; }
        public uint PrefabHash { get; internal set; }
        public int OwnerConnectionId { get; internal set; }
        public AuthorityMode Authority { get; internal set; }
        public bool IsSceneObject { get; internal set; }

        /// <summary>
        /// Interest/AOI용 월드 좌표 캐시.
        /// </summary>
        public float PosX, PosY, PosZ;

        internal readonly List<NetBehaviour> Behaviours = new List<NetBehaviour>();

        public bool IsOwner(int connId) => OwnerConnectionId == connId;

        internal void Register(NetBehaviour b)
        {
            b.ComponentIndex = (byte)Behaviours.Count;
            Behaviours.Add(b);
            b.NetworkObject = this;
        }
    }
}
