# MyNetEngine 설계 노트

## 계층 간 의존 방향
```
Unity  ->  Replication / Prediction / Interest
Unity  ->  Objects
Replication -> Objects -> Core.Serialization
Interest    -> Objects
Prediction  -> Objects -> Core.Serialization
Messaging   -> Core.Serialization -> Core.Buffers
Transport   -> Core.Buffers + Core.Metrics
```

Core가 모든 층의 공용 기반. Unity 층은 나머지 층을 소비만 한다.

## Tick 시뮬레이션 규약
- 서버: 매 tick authoritative simulation.
- 클라: 매 tick 입력 수집 + owner prediction + 원격 entity interpolation.
- 보낸 input의 tick 번호가 서버 기준과 일치해야 reconcile이 가능 → `TimeSync`로 보정.

## 메시지 포맷
- 기본 : `[MessageType:byte][body]`
- batching 시에는 `BatchSender` 포맷으로 감쌀 수 있으나, 이 경우 수신 측에서 첫 바이트가 `MessageType.Batch` 혹은
  별도 헤더임을 약속해야 한다. 초기 버전은 단일 메시지 규약만 사용하고, batch는 주기적 대량 전송 전용 옵션으로 제공.

## SyncProperty 최적화 로드맵
1. (v0) 사용자 직접 `Serialize/Deserialize` + `IsDirty` 구현.
2. (v1) Roslyn Source Generator가 `[SyncProperty]` 필드를 스캔하여
   - `DirtyMask` 생성
   - `Serialize(writer, initial)` / `Deserialize` 자동 구현
   - setter에 dirty flag 설정 주입
3. (v2) Delta snapshot: 마지막 ack된 snapshot 기준으로 값 diff만 전송.

## Delta Snapshot
- `ReplicationManager`는 `ConnState.Known` 으로 초기 여부 관리.
- 초기 전송 이후는 해당 NetBehaviour의 `Serialize(w, isInitial=false)` 호출.
  사용자/자동생성 구현이 dirty 값만 쓰도록 한다.
- 추가 계획: per-connection per-object `lastAckedSnapshotId` 추적 후 rewind-to-diff 적용.

## 우선순위/budget
- `PriorityScheduler.Score` → 정렬된 후보.
- `ReplicationManager.Tick`에서 `BandwidthBudgetPerTick` 도달까지만 직렬화.
- 남은 객체는 다음 tick으로 미뤄지며 `LastSentTick` 차이로 보너스 점수.

## Interest Management
- 기본 `SpatialHashGrid` (cell size = 20m, radius = 60m).
- `SceneFilter`, `TeamFilter` 등 IObserverFilter 스택.
- `InterestManager.GatherObservable`는 tick마다 호출되지만, 일부 연결만 갱신하도록 쉽게 확장할 수 있음 (rolling update).

## Prediction 티어
- Tier1: `PredictedController` 같은 kinematic motor (현 구현). deterministic.
- Tier2: 간단한 rigidbody-lite. 직접 속도/가속도 적분. 충돌은 sphere/plane 정도.
- Tier3: full physics rollback. Unity physics scene 복수 생성 필요 → 후순위.

## 보안/신뢰성 운영 포인트
- UDP lite는 가벼운 전송용이다. 실전은 KCP or 자체 reliable layer 필수.
- 패킷 최대 크기 = MTU(1200). snapshot이 초과할 경우 fragment 필요.
- 중요 메시지(Spawn/Despawn)는 반드시 reliable channel로.

## Mirror 호환성 경계
- `Compatibility.Mirror` 네임스페이스: 익숙한 이름(`NetworkBehaviour`, `[SyncVar]`, `[Command]`, `[ClientRpc]`).
- 완전 호환이 아니다. `NetworkIdentity`, `NetworkServer.Spawn` 등은 shim layer에서 차츰 덧붙이는 것을 권장.

## 다음 작업 제안
1. KCP 포팅 혹은 LiteNetLib wrapper 교체.
2. Roslyn SourceGenerator로 `[SyncProperty]` 자동 직렬화.
3. SnapshotAck 기반 per-field delta.
4. Unity Physics rollback(Tier3).
5. Room/Lobby/Matchmaking 모듈(Photon 스타일).
6. Host 모드(단일 프로세스 server+client 루프백).
