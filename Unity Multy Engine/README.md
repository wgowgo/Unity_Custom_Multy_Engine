# MyNetEngine

Unity 네트워크 엔진. Mirror API 호환 레이어를 가진 독립 엔진.

## 구조

```
Runtime/
  Core/          Tick, TimeSync, Buffers, Metrics, Serialization
  Transport/     ITransport + KCP/UDP/WebSocket 어댑터
  Replication/   Snapshot, Dirty, Delta, Priority
  Prediction/    InputBuffer, Reconciliation, Rollback
  Interest/      SpatialHash, ObserverSet, Filters
  Objects/       NetworkObject, NetBehaviour, Spawn, Authority
  Messaging/     RPC, Router, Channels
  Unity/         NetworkTransform, Animator, PredictedController, SceneSync
Editor/          Debug Inspector, Bandwidth Profiler 등
Compatibility/   MirrorAdapter
```

## 철학
- 서버 권한 기본
- Tick 기반 시뮬레이션
- Delta snapshot + dirty tracking
- GC 0 지향
- AOI 필수

## 사용 예
```csharp
public class PlayerNetwork : NetBehaviour
{
    [SyncProperty] private int hp;
    [SyncProperty] private Vector3 syncedPosition;

    [ServerRpc] private void SubmitInput(PlayerInputData input) { }
    [ClientRpc] private void PlayHitEffect() { }

    public override void OnNetworkTick()
    {
        if (IsOwner)
        {
            var input = GatherInput();
            Predict(input);
            SubmitInput(input);
        }
    }
}
```
