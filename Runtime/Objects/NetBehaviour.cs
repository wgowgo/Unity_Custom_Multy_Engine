using MyNetEngine.Core.Serialization;

namespace MyNetEngine.Objects
{
    /// <summary>
    /// 사용자 확장 포인트. Mirror의 NetworkBehaviour와 비슷한 감각.
    /// 게임 로직은 이 클래스를 상속해서 작성.
    /// </summary>
    public abstract class NetBehaviour
    {
        public NetworkObject NetworkObject { get; internal set; }
        public byte ComponentIndex { get; internal set; }
        public uint NetId => NetworkObject?.NetId ?? 0;

        public bool IsServer { get; internal set; }
        public bool IsClient { get; internal set; }
        public bool IsOwner { get; internal set; }

        /// <summary> tick 단위 시뮬레이션 훅. 서버 권한 로직. </summary>
        public virtual void OnNetworkTick() { }

        /// <summary> tick 단위로 owner prediction 실행. </summary>
        public virtual void OnPredictedTick() { }

        /// <summary> 객체가 네트워크에 스폰된 직후. </summary>
        public virtual void OnNetworkStart() { }

        /// <summary> 객체가 디스폰되기 직전. </summary>
        public virtual void OnNetworkStop() { }

        /// <summary>
        /// 상태 직렬화. isInitial=true면 전체, false면 dirty만.
        /// 사용자가 직접 구현할 수도, SyncProperty 기반 자동 생성 코드가 채울 수도 있음.
        /// </summary>
        public virtual void Serialize(NetWriter w, bool isInitial) { }

        /// <summary> 상태 역직렬화. </summary>
        public virtual void Deserialize(NetReader r, bool isInitial) { }

        /// <summary> dirty 플래그 리셋. 기본 구현 없음. </summary>
        public virtual void ClearDirty() { }

        /// <summary> 현재 dirty 상태인지. 기본은 false. </summary>
        public virtual bool IsDirty() => false;
    }
}
