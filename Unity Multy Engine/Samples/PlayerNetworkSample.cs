using MyNetEngine.Core.Serialization;
using MyNetEngine.Messaging;
using MyNetEngine.Objects;

namespace MyNetEngine.Samples
{
    /// <summary>
    /// 사용자 API 샘플.
    ///   - SyncProperty로 hp/위치 선언
    ///   - ServerRpc로 입력 제출
    ///   - ClientRpc로 피격 이펙트
    ///   - OnNetworkTick에서 owner 입력 수집 + prediction
    /// </summary>
    public sealed class PlayerNetworkSample : NetBehaviour
    {
        [SyncProperty(Priority = 5)] public int Hp = 100;
        [SyncProperty] public float PosX;
        [SyncProperty] public float PosZ;

        private bool _dirty;

        public override bool IsDirty() => _dirty;
        public override void ClearDirty() => _dirty = false;

        public override void Serialize(NetWriter w, bool isInitial)
        {
            w.WriteVarInt(Hp);
            w.WriteFloat(PosX);
            w.WriteFloat(PosZ);
        }

        public override void Deserialize(NetReader r, bool isInitial)
        {
            Hp = r.ReadVarInt();
            PosX = r.ReadFloat();
            PosZ = r.ReadFloat();
        }

        public void TakeDamage(int dmg)
        {
            if (!IsServer) return;
            Hp -= dmg;
            _dirty = true;
            // PlayHitEffect()를 ClientRpc로 보내려면 NetworkRunner.Rpc 경로에서 핸들러 등록 + 송신.
        }

        public override void OnNetworkTick()
        {
            if (IsOwner)
            {
                // 가상의 입력 수집 + 즉시 예측
                // var input = ...;
                // Predict(input);
                // SubmitInput(input);  // ServerRpc
            }
        }
    }
}
