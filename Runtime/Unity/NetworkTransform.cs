using MyNetEngine.Core.Serialization;
using MyNetEngine.Objects;

namespace MyNetEngine.Unity
{
    /// <summary>
    /// 위치/회전 상태 동기화 컴포넌트.
    /// - 서버 권한: 서버에서 값 갱신 -> dirty 설정 -> replication
    /// - 클라(비 owner): snapshot interpolator로 부드럽게 보간
    /// - 클라(owner + prediction): PredictedController 사용
    ///
    /// 엔진 비의존: Vector3/Quaternion 대신 primitive 필드. Unity 래퍼에서 접어 사용.
    /// </summary>
    public sealed class NetworkTransform : NetBehaviour
    {
        public float Px, Py, Pz;
        public float Qx, Qy, Qz, Qw = 1f;

        private float _lastPx, _lastPy, _lastPz;
        private float _lastQx, _lastQy, _lastQz, _lastQw = 1f;
        private bool _dirty;

        public float PositionMinRange = -1024f;
        public float PositionMaxRange = 1024f;

        public void SetPosition(float x, float y, float z)
        {
            if (x != Px || y != Py || z != Pz)
            {
                Px = x; Py = y; Pz = z; _dirty = true;
            }
        }

        public void SetRotation(float x, float y, float z, float w)
        {
            Qx = x; Qy = y; Qz = z; Qw = w; _dirty = true;
        }

        public override bool IsDirty() => _dirty;
        public override void ClearDirty()
        {
            _dirty = false;
            _lastPx = Px; _lastPy = Py; _lastPz = Pz;
            _lastQx = Qx; _lastQy = Qy; _lastQz = Qz; _lastQw = Qw;
        }

        public override void Serialize(NetWriter w, bool isInitial)
        {
            w.WriteQuantizedFloat(Px, PositionMinRange, PositionMaxRange);
            w.WriteQuantizedFloat(Py, PositionMinRange, PositionMaxRange);
            w.WriteQuantizedFloat(Pz, PositionMinRange, PositionMaxRange);
            Compression.WriteCompressedQuaternion(w, Qx, Qy, Qz, Qw);
        }

        public override void Deserialize(NetReader r, bool isInitial)
        {
            Px = r.ReadQuantizedFloat(PositionMinRange, PositionMaxRange);
            Py = r.ReadQuantizedFloat(PositionMinRange, PositionMaxRange);
            Pz = r.ReadQuantizedFloat(PositionMinRange, PositionMaxRange);
            Compression.ReadCompressedQuaternion(r, out Qx, out Qy, out Qz, out Qw);
            if (NetworkObject != null)
            {
                NetworkObject.PosX = Px; NetworkObject.PosY = Py; NetworkObject.PosZ = Pz;
            }
        }
    }
}
