using System;

namespace MyNetEngine.Compatibility.Mirror
{
    /// <summary>
    /// Mirror의 기본 어노테이션 이름을 재현하여 기존 프로젝트 리팩토링 부담 완화.
    /// 내부는 MyNetEngine의 SyncProperty / ServerRpc / ClientRpc로 매핑된다.
    /// 완전 호환이 아님: Mirror 고유 API(NetworkIdentity, NetworkServer.Spawn 등)는
    /// 별도 어댑터 class에서 래핑 제공해야 한다.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public sealed class SyncVarAttribute : Attribute
    {
        public string hook;
    }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class CommandAttribute : Attribute
    {
        public int channel;
        public bool requiresAuthority = true;
    }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class ClientRpcAttribute : Attribute
    {
        public int channel;
        public bool includeOwner = true;
    }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class TargetRpcAttribute : Attribute
    {
        public int channel;
    }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class ServerAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class ClientAttribute : Attribute { }

    /// <summary>
    /// Mirror의 NetworkBehaviour와 동일한 이름으로 진입 장벽 완화.
    /// 실제 기능은 MyNetEngine.Objects.NetBehaviour에 매핑.
    /// </summary>
    public abstract class NetworkBehaviour : MyNetEngine.Objects.NetBehaviour
    {
        // Mirror 네이밍
        public bool isServer => IsServer;
        public bool isClient => IsClient;
        public bool isOwned => IsOwner;
        public uint netId => NetId;

        public virtual void OnStartServer() => OnNetworkStart();
        public virtual void OnStartClient() => OnNetworkStart();
        public virtual void OnStopServer() => OnNetworkStop();
        public virtual void OnStopClient() => OnNetworkStop();
    }
}
