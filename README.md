# MyNetEngine

A Unity multiplayer networking engine with a **Mirror-compatible API layer**. It combines Mirror-style ergonomics, FishNet-style client prediction, and Fusion-style tick-based simulation in a single, self-contained package.

---

## Table of Contents (English)

- [Overview](#overview)
- [Key Features](#key-features)
- [Architecture](#architecture)
- [Requirements](#requirements)
- [Installation](#installation)
- [Quick Start](#quick-start)
- [Writing Networked Scripts](#writing-networked-scripts)
- [Spawning Objects](#spawning-objects)
- [Client Prediction](#client-prediction)
- [SyncProperty Source Generator](#syncproperty-source-generator)
- [Migrating from Mirror](#migrating-from-mirror)
- [Editor Tools](#editor-tools)
- [Key Files](#key-files)
- [Design Philosophy](#design-philosophy)
- [Roadmap & Known Limitations](#roadmap--known-limitations)

---

## Overview

**MyNetEngine** is a server-authoritative, tick-driven networking stack for Unity. It is designed for action games that need predictable simulation, bandwidth efficiency, and responsive client-side movement without sacrificing server authority.

| Inspiration | What MyNetEngine borrows |
|-------------|--------------------------|
| **Mirror** | Familiar API (`SyncVar`, `Command`, `ClientRpc`) via compatibility layer |
| **FishNet** | Owner prediction + server reconciliation |
| **Photon Fusion** | Fixed tick simulation and snapshot replication |

The engine ships as a folder you drop into `Assets/`. Assembly Definition files (`.asmdef`) are included so Unity compiles everything automatically.

---

## Key Features

- **Tick-based simulation** — Default 30 Hz; configurable up to 120 Hz in the Inspector.
- **KCP reliable UDP** — ARQ retransmission over UDP for ordered, reliable delivery.
- **Delta snapshots + dirty tracking** — Only changed fields are sent after the initial full state.
- **Server authority by default** — Gameplay logic runs on the server; clients receive replicated state.
- **Owner prediction + reconciliation** — Local input is applied immediately; server corrections are merged smoothly.
- **Area of Interest (AOI)** — Objects outside the configured radius are not replicated to a connection.
- **Priority-based bandwidth scheduling** — Owner objects and nearby entities are sent before distant or stale ones.
- **GC-conscious design** — `ArrayPool`, struct-based messages, no boxing on hot paths.
- **Compression** — 32-bit quaternion packing, 16-bit position quantization, variable-length integers.
- **Mirror compatibility** — Existing Mirror attribute names work through `MyNetEngine.Compatibility.Mirror`.

---

## Architecture

```
Runtime/
  Core/          Tick, TimeSync, Buffers, Metrics, Serialization
  Transport/     ITransport + KCP / UDP / WebSocket adapters
  Replication/   Snapshot, Dirty, Delta, Priority
  Prediction/    InputBuffer, Reconciliation, Rollback hooks
  Interest/      SpatialHash, ObserverSet, Filters
  Objects/       NetworkObject, NetBehaviour, Spawn, Authority
  Messaging/     RPC, Router, Channels
  Unity/         NetworkTransform, Animator, PredictedController, SceneSync
Editor/          Debug Inspector, Bandwidth Profiler, SyncVar Analyzer
Compatibility/   MirrorAdapter
SourceGenerator~ Roslyn analyzer for [SyncProperty] codegen
Samples/         Example scripts
```

### Layer overview

| Layer | Responsibility |
|-------|----------------|
| `NetworkRunnerBehaviour` | Unity scene entry point (Inspector configuration) |
| `NetworkRunner` | Server / client lifecycle, tick advance, spawn orchestration |
| `Transport` | KCP (recommended), UdpLite (testing), WebSocket (WebGL stub) |
| `NetworkTickSystem` | Fixed-rate simulation loop |
| `ReplicationManager` | Snapshot build, delta encode, per-connection send budget |
| `PredictedBehaviour` / `PredictedController` | Client-side prediction and reconcile |
| `InterestManager` | Spatial hash grid + team/scene filters |
| `MirrorAdapter` | Drop-in attribute/name compatibility for Mirror projects |

---

## Requirements

- **Unity 2021.1+** (2021.2+ recommended if using the Roslyn Source Generator)
- **.NET Standard 2.1** (handled by Unity's scripting backend)
- No external package dependencies for core runtime

---

## Installation

1. Copy the entire `Unity Multy Engine` folder into your Unity project:

```
Assets/
  MyNetEngine/
    Runtime/
    Editor/
    Compatibility/
    Samples/
```

2. Wait for Unity to import and compile the assemblies (`MyNetEngine.Runtime`, `MyNetEngine.Editor`).

3. *(Optional)* Build and install the Source Generator — see [SyncProperty Source Generator](#syncproperty-source-generator).

---

## Quick Start

### Step 1 — Add the scene entry component

1. Create an empty GameObject in your scene.
2. Add **Network Runner Behaviour** (`MyNetEngine.Unity.NetworkRunnerBehaviour`).
3. Configure the Inspector:

| Field | Description | Default |
|-------|-------------|---------|
| **Mode** | `Server`, `Client`, or `Host` | `None` |
| **Host** | Server address (client mode) | `127.0.0.1` |
| **Port** | UDP / KCP port | `7777` |
| **Tick Rate** | Simulation ticks per second | `30` |
| **Kind** | Transport: `KCP`, `UdpLite`, `WebSocket` | `KCP` |
| **Aoi Radius** | Interest radius in meters | `60` |
| **Budget Per Tick Per Conn** | Max bytes sent per tick per connection | `8192` |

### Step 2 — Register network prefabs

In the **Network Prefabs** list on `NetworkRunnerBehaviour`:

| Hash | Prefab |
|------|--------|
| `1001` | `PlayerPrefab` |
| `1002` | `EnemyPrefab` |

**Important:** The `Hash` value must be **identical on server and every client**. Hashes must be unique within the list. You can assign them manually or derive them consistently, e.g. `(uint)"Player".GetHashCode()`.

### Step 3 — Attach `NetBehaviour` scripts to prefabs

Add a class that inherits `NetBehaviour` to any GameObject you want to replicate. See the next section for a minimal example.

### Step 4 — Run

- Set **Mode** to `Server` on one build, `Client` on another (or use two Editor instances).
- Press Play. The server listens; the client connects to `Host:Port`.

---

## Writing Networked Scripts

### Manual serialization

Implement `Serialize`, `Deserialize`, `IsDirty`, and `ClearDirty` on your `NetBehaviour` subclass:

```csharp
using MyNetEngine.Core.Serialization;
using MyNetEngine.Objects;

public class PlayerNet : NetBehaviour
{
    public int Hp = 100;
    private bool _dirty;

    public override bool IsDirty() => _dirty;
    public override void ClearDirty() => _dirty = false;

    public override void Serialize(NetWriter w, bool isInitial)
    {
        w.WriteVarInt(Hp);
    }

    public override void Deserialize(NetReader r, bool isInitial)
    {
        Hp = r.ReadVarInt();
    }

    public void TakeDamage(int dmg)
    {
        if (!IsServer) return;
        Hp -= dmg;
        _dirty = true; // replicated on the next tick
    }

    public override void OnNetworkTick()
    {
        if (IsOwner)
        {
            // Gather input, predict locally, send to server via RPC
        }
    }
}
```

### Lifecycle hooks

| Method | When it runs |
|--------|--------------|
| `OnNetworkStart()` | Immediately after the object is spawned on the network |
| `OnNetworkTick()` | Every simulation tick (server-authoritative logic) |
| `OnPredictedTick()` | Every tick on the owning client for prediction |
| `OnNetworkStop()` | Just before despawn |

### Authority flags

| Property | Meaning |
|----------|---------|
| `IsServer` | Running on the authoritative server |
| `IsClient` | Running on any client (including host's client side when implemented) |
| `IsOwner` | This connection owns the object (prediction / input) |

---

## Spawning Objects

Spawn from server-side code after clients are connected:

```csharp
var runner = FindObjectOfType<NetworkRunnerBehaviour>();

runner.SpawnObject(
    prefabHash: 1001,
    ownerConnId: connId,
    authority: AuthorityMode.OwnerPredictedServerValidated,
    pos: new Vector3(0f, 0f, 0f)
);
```

**What happens automatically:**

- **Server** — Creates a `NetworkObject`, registers it with the interest system, and sends a spawn message to relevant clients.
- **Client** — Instantiates the prefab matching `prefabHash` and waits for initial state snapshots.

---

## Client Prediction

### Tier 1 — Built-in kinematic controller

Inherit `PredictedController` for a ready-made owner-predicted character motor. Tune `MoveSpeed`, `JumpSpeed`, `Gravity`, and `GroundY` in the Inspector.

```csharp
using MyNetEngine.Unity;

public class MyController : PredictedController
{
    // Prediction and reconciliation are handled by the base class.
}
```

### Tier 2 — Custom prediction

For full control, inherit `PredictedBehaviour<TInput, TState>`:

```csharp
public class MyPredicted : PredictedBehaviour<MyInput, MyState>
{
    protected override void Simulate(ref MyInput input, ref MyState state, float dt)
    {
        // Identical logic on server and owning client
    }

    protected override bool NeedsReconcile(in MyState serverState, in MyState predictedState)
    {
        return Vector3.Distance(serverState.Position, predictedState.Position) > 0.1f;
    }
}
```

---

## SyncProperty Source Generator

Avoid hand-writing serialization by marking fields with `[SyncProperty]` and making the class `partial`.

### Build & install

```bash
cd SourceGenerator~/MyNetEngine.SourceGen
dotnet build -c Release
```

Copy the output DLL to `Assets/MyNetEngine/Plugins/` and label it **RoslynAnalyzer** in the Unity Inspector.

### Usage

```csharp
public partial class PlayerNet : NetBehaviour
{
    [SyncProperty(Priority = 5)] private int hp;
    [SyncProperty] private float posX;

    // Assign via generated Sync_* properties to auto-mark dirty:
    // Sync_hp = 100;
}
```

The generator emits `Serialize`, `Deserialize`, `IsDirty`, `ClearDirty`, and dirty-bit tracking automatically.

**Requires Unity 2021.2+** for Roslyn analyzer support.

---

## Migrating from Mirror

1. Replace namespaces:

```csharp
// Before
using Mirror;

// After
using MyNetEngine.Compatibility.Mirror;
```

2. Keep existing attributes — `[SyncVar]`, `[Command]`, `[ClientRpc]`, `[TargetRpc]` are mapped by `MirrorAdapter`.

3. Replace Mirror static APIs (`NetworkServer.Spawn`, `NetworkClient`, etc.) with `NetworkRunner` / `NetworkRunnerBehaviour` equivalents.

4. Swap `NetworkBehaviour` base class references if you move to native `NetBehaviour` for full feature access (prediction, dirty snapshots, AOI).

---

## Editor Tools

Unity menu → **MyNetEngine**

| Tool | Purpose |
|------|---------|
| **Bandwidth Profiler** | Per-connection, per-object, and per-RPC byte usage |
| **Tick Graph** | Tick timing and interpolation alpha visualization |
| **SyncVar Analyzer** | Statistics on `[SyncProperty]` fields across `NetBehaviour` types |

---

## Key Files

| Path | Description |
|------|-------------|
| `Runtime/NetworkRunner.cs` | Server/client lifecycle core |
| `Runtime/Core/Tick/NetworkTickSystem.cs` | Fixed tick loop |
| `Runtime/Core/Serialization/NetWriter.cs` | Binary writer (varint, quantize, compression) |
| `Runtime/Transport/Kcp/Kcp.cs` | KCP ARQ (ported from ikcp.c) |
| `Runtime/Transport/KcpTransport.cs` | KCP + raw UDP transport |
| `Runtime/Replication/ReplicationManager.cs` | Snapshot creation and delivery |
| `Runtime/Prediction/PredictedBehaviour.cs` | Generic prediction + reconcile |
| `Runtime/Interest/SpatialHashGrid.cs` | AOI spatial partitioning |
| `Runtime/Unity/NetworkRunnerBehaviour.cs` | Unity Inspector entry point |
| `Runtime/Unity/PredictedController.cs` | Tier-1 kinematic prediction motor |
| `Compatibility/MirrorAdapter.cs` | Mirror API name compatibility |
| `SourceGenerator~/` | Roslyn `[SyncProperty]` generator |
| `Samples/PlayerNetworkSample.cs` | Reference implementation |

---

## Design Philosophy

- **Server authority first** — The server is the source of truth; clients predict only where explicitly allowed.
- **Tick-based simulation** — Deterministic, fixed-step logic instead of frame-rate-dependent `Update` networking.
- **Delta snapshots with dirty tracking** — Minimize bandwidth by sending only what changed.
- **Zero GC on hot paths** — Pool buffers, prefer structs, avoid allocations in tick/replication loops.
- **AOI is not optional** — Spatial interest management is built in, not bolted on later.

---

## Roadmap

| Item | Status |
|------|--------|
| KCP multi-client `conv` management on server | In progress — currently single-conv per transport instance |
| Host mode loopback short-circuit | Not implemented (server starts; client loopback logs TODO) |
| Physics rollback (Tier 3) | Not implemented — requires multiple Unity physics scenes |
| Room / lobby / matchmaking | Not implemented |
| WebSocket transport | Stub — needs NativeWebSocket or similar integration |

Contributions and issue reports are welcome as these areas mature.

---

<br>

---

# MyNetEngine (한국어)

Unity 멀티플레이어 네트워크 엔진입니다. **Mirror API 호환 레이어**를 포함하며, Mirror의 쉬운 API, FishNet의 예측(prediction), Fusion의 틱 기반 시뮬레이션을 하나의 패키지로 통합했습니다.

---

## 목차

- [소개](#소개)
- [핵심 특징](#핵심-특징)
- [아키텍처](#아키텍처)
- [요구 사항](#요구-사항)
- [설치 방법](#설치-방법)
- [빠른 시작](#빠른-시작)
- [네트워크 스크립트 작성](#네트워크-스크립트-작성)
- [오브젝트 스폰](#오브젝트-스폰)
- [클라이언트 예측 (Prediction)](#클라이언트-예측-prediction)
- [SyncProperty 소스 생성기](#syncproperty-소스-생성기)
- [Mirror에서 마이그레이션](#mirror에서-마이그레이션)
- [에디터 도구](#에디터-도구)
- [주요 파일](#주요-파일)
- [설계 철학](#설계-철학)
- [로드맵 및 알려진 제한](#로드맵-및-알려진-제한)

---

## 소개

**MyNetEngine**은 Unity용 **서버 권한(Server Authoritative)** 기반, **틱(Tick) 구동** 네트워크 스택입니다. 액션 게임처럼 예측 가능한 시뮬레이션, 대역폭 효율, 서버 권한을 유지하면서도 반응성 있는 이동이 필요한 프로젝트를 대상으로 합니다.

| 참고 엔진 | MyNetEngine이 가져온 요소 |
|-----------|---------------------------|
| **Mirror** | `SyncVar`, `Command`, `ClientRpc` 등 익숙한 API (호환 레이어) |
| **FishNet** | 오너 예측 + 서버 보정(Reconciliation) |
| **Photon Fusion** | 고정 틱 시뮬레이션 및 스냅샷 복제 |

`Assets/` 아래에 폴더를 복사하는 방식으로 설치하며, `.asmdef`가 포함되어 Unity가 자동으로 컴파일합니다.

---

## 핵심 특징

- **틱 기반 시뮬레이션** — 기본 30Hz, Inspector에서 최대 120Hz까지 설정 가능
- **KCP 신뢰성 UDP** — ARQ 재전송으로 UDP 위에서 순서 보장·신뢰성 전달
- **델타 스냅샷 + Dirty 추적** — 초기 전체 상태 이후 변경된 필드만 전송
- **서버 권한 기본** — 게임플레이 로직은 서버에서 실행, 클라이언트는 복제 상태 수신
- **오너 예측 + 보정** — 로컬 입력 즉시 반영, 서버 보정값과 부드럽게 병합
- **AOI (관심 영역)** — 설정 반경 밖 오브젝트는 해당 연결에 전송하지 않음
- **우선순위 기반 대역폭 스케줄링** — 오너·근거리 오브젝트를 먼저 전송
- **GC 최소화** — `ArrayPool`, struct 메시지, 핫 패스에서 boxing 없음
- **압축** — 쿼터니언 32비트, 위치 16비트 양자화, 가변 길이 정수
- **Mirror 호환** — `MyNetEngine.Compatibility.Mirror`로 기존 attribute 이름 그대로 사용 가능

---

## 아키텍처

```
Runtime/
  Core/          Tick, TimeSync, Buffers, Metrics, Serialization
  Transport/     ITransport + KCP / UDP / WebSocket 어댑터
  Replication/   Snapshot, Dirty, Delta, Priority
  Prediction/    InputBuffer, Reconciliation, Rollback
  Interest/      SpatialHash, ObserverSet, Filters
  Objects/       NetworkObject, NetBehaviour, Spawn, Authority
  Messaging/     RPC, Router, Channels
  Unity/         NetworkTransform, Animator, PredictedController, SceneSync
Editor/          디버그 Inspector, Bandwidth Profiler, SyncVar Analyzer
Compatibility/   MirrorAdapter
SourceGenerator~ Roslyn [SyncProperty] 코드 생성기
Samples/         예제 스크립트
```

### 계층 요약

| 계층 | 역할 |
|------|------|
| `NetworkRunnerBehaviour` | Unity 씬 진입점 (Inspector 설정) |
| `NetworkRunner` | 서버/클라이언트 생명주기, 틱 진행, 스폰 조율 |
| `Transport` | KCP(권장), UdpLite(테스트), WebSocket(WebGL 스텁) |
| `NetworkTickSystem` | 고정 주기 시뮬레이션 루프 |
| `ReplicationManager` | 스냅샷 생성, 델타 인코딩, 연결별 전송 예산 |
| `PredictedBehaviour` / `PredictedController` | 클라이언트 예측 및 보정 |
| `InterestManager` | 공간 해시 그리드 + 팀/씬 필터 |
| `MirrorAdapter` | Mirror 프로젝트용 attribute/이름 호환 |

---

## 요구 사항

- **Unity 2021.1 이상** (Roslyn 소스 생성기 사용 시 **2021.2+** 권장)
- **.NET Standard 2.1** (Unity 스크립팅 백엔드)
- 코어 런타임에 외부 패키지 의존성 없음

---

## 설치 방법

1. `Unity Multy Engine` 폴더 전체를 Unity 프로젝트에 복사합니다.

```
Assets/
  MyNetEngine/
    Runtime/
    Editor/
    Compatibility/
    Samples/
```

2. Unity가 어셈블리(`MyNetEngine.Runtime`, `MyNetEngine.Editor`)를 자동 컴파일할 때까지 대기합니다.

3. *(선택)* 소스 생성기 빌드 및 설치 — [SyncProperty 소스 생성기](#syncproperty-소스-생성기) 참고.

---

## 빠른 시작

### 1단계 — 씬 진입 컴포넌트 추가

1. 씬에 빈 GameObject를 생성합니다.
2. **Network Runner Behaviour** (`MyNetEngine.Unity.NetworkRunnerBehaviour`) 컴포넌트를 추가합니다.
3. Inspector 설정:

| 항목 | 설명 | 기본값 |
|------|------|--------|
| **Mode** | `Server`, `Client`, `Host` | `None` |
| **Host** | 서버 주소 (클라이언트 모드) | `127.0.0.1` |
| **Port** | UDP / KCP 포트 | `7777` |
| **Tick Rate** | 초당 시뮬레이션 틱 수 | `30` |
| **Kind** | 전송 계층: `KCP`, `UdpLite`, `WebSocket` | `KCP` |
| **Aoi Radius** | 관심 영역 반경 (m) | `60` |
| **Budget Per Tick Per Conn** | 연결당 틱당 최대 전송 바이트 | `8192` |

### 2단계 — 네트워크 프리팹 등록

`NetworkRunnerBehaviour`의 **Network Prefabs** 리스트:

| Hash | Prefab |
|------|--------|
| `1001` | `PlayerPrefab` |
| `1002` | `EnemyPrefab` |

**주의:** `Hash`는 **서버와 모든 클라이언트에서 동일**해야 하며, 리스트 내에서 **중복 불가**입니다. 수동 지정 또는 `(uint)"Player".GetHashCode()`처럼 일관된 방식으로 계산할 수 있습니다.

### 3단계 — 프리팹에 `NetBehaviour` 스크립트 연결

복제할 GameObject에 `NetBehaviour`를 상속한 클래스를 추가합니다. 다음 섹션의 예제를 참고하세요.

### 4단계 — 실행

- 한 빌드는 **Mode = Server**, 다른 빌드는 **Client**로 설정합니다 (또는 에디터 두 개 실행).
- Play를 누르면 서버가 대기하고, 클라이언트가 `Host:Port`로 접속합니다.

---

## 네트워크 스크립트 작성

### 수동 직렬화

`NetBehaviour`에서 `Serialize`, `Deserialize`, `IsDirty`, `ClearDirty`를 구현합니다.

```csharp
using MyNetEngine.Core.Serialization;
using MyNetEngine.Objects;

public class PlayerNet : NetBehaviour
{
    public int Hp = 100;
    private bool _dirty;

    public override bool IsDirty() => _dirty;
    public override void ClearDirty() => _dirty = false;

    public override void Serialize(NetWriter w, bool isInitial)
    {
        w.WriteVarInt(Hp);
    }

    public override void Deserialize(NetReader r, bool isInitial)
    {
        Hp = r.ReadVarInt();
    }

    public void TakeDamage(int dmg)
    {
        if (!IsServer) return;
        Hp -= dmg;
        _dirty = true; // 다음 틱에 자동 복제
    }

    public override void OnNetworkTick()
    {
        if (IsOwner)
        {
            // 입력 수집, 로컬 예측, RPC로 서버에 전송
        }
    }
}
```

### 생명주기 훅

| 메서드 | 호출 시점 |
|--------|-----------|
| `OnNetworkStart()` | 네트워크 스폰 직후 |
| `OnNetworkTick()` | 매 시뮬레이션 틱 (서버 권한 로직) |
| `OnPredictedTick()` | 오너 클라이언트에서 예측용 매 틱 |
| `OnNetworkStop()` | 디스폰 직전 |

### 권한 플래그

| 속성 | 의미 |
|------|------|
| `IsServer` | 권한 있는 서버에서 실행 중 |
| `IsClient` | 클라이언트에서 실행 중 |
| `IsOwner` | 이 연결이 오브젝트 소유 (예측/입력) |

---

## 오브젝트 스폰

클라이언트 연결 후 서버 코드에서 스폰합니다.

```csharp
var runner = FindObjectOfType<NetworkRunnerBehaviour>();

runner.SpawnObject(
    prefabHash: 1001,
    ownerConnId: connId,
    authority: AuthorityMode.OwnerPredictedServerValidated,
    pos: new Vector3(0f, 0f, 0f)
);
```

**자동 처리:**

- **서버** — `NetworkObject` 생성, 관심 영역 등록, 관련 클라이언트에 스폰 메시지 전송
- **클라이언트** — `prefabHash`에 맞는 프리팹 인스턴스화 후 초기 상태 스냅샷 대기

---

## 클라이언트 예측 (Prediction)

### Tier 1 — 내장 키네마틱 컨트롤러

`PredictedController`를 상속하면 오너 예측 캐릭터 모터를 바로 사용할 수 있습니다. Inspector에서 `MoveSpeed`, `JumpSpeed`, `Gravity`, `GroundY`를 조정하세요.

```csharp
using MyNetEngine.Unity;

public class MyController : PredictedController
{
    // 예측 및 보정은 기본 클래스에서 처리
}
```

### Tier 2 — 커스텀 예측

`PredictedBehaviour<TInput, TState>`를 상속해 완전히 직접 구현할 수 있습니다.

```csharp
public class MyPredicted : PredictedBehaviour<MyInput, MyState>
{
    protected override void Simulate(ref MyInput input, ref MyState state, float dt)
    {
        // 서버와 오너 클라이언트에서 동일한 로직
    }

    protected override bool NeedsReconcile(in MyState serverState, in MyState predictedState)
    {
        return Vector3.Distance(serverState.Position, predictedState.Position) > 0.1f;
    }
}
```

---

## SyncProperty 소스 생성기

`[SyncProperty]`와 `partial` 클래스로 직렬화 코드를 자동 생성합니다.

### 빌드 및 설치

```bash
cd SourceGenerator~/MyNetEngine.SourceGen
dotnet build -c Release
```

출력 DLL을 `Assets/MyNetEngine/Plugins/`에 복사하고, Unity Inspector에서 **RoslynAnalyzer** 레이블을 지정합니다.

### 사용 예

```csharp
public partial class PlayerNet : NetBehaviour
{
    [SyncProperty(Priority = 5)] private int hp;
    [SyncProperty] private float posX;

    // 생성된 Sync_* 프로퍼티로 할당 시 dirty 자동 설정:
    // Sync_hp = 100;
}
```

`Serialize`, `Deserialize`, `IsDirty`, `ClearDirty` 및 dirty 비트 추적이 자동 생성됩니다.

**Unity 2021.2 이상**에서 Roslyn Analyzer 지원이 필요합니다.

---

## Mirror에서 마이그레이션

1. 네임스페이스 교체:

```csharp
// 변경 전
using Mirror;

// 변경 후
using MyNetEngine.Compatibility.Mirror;
```

2. 기존 attribute 유지 — `[SyncVar]`, `[Command]`, `[ClientRpc]`, `[TargetRpc]`는 `MirrorAdapter`가 매핑합니다.

3. Mirror 정적 API(`NetworkServer.Spawn` 등)는 `NetworkRunner` / `NetworkRunnerBehaviour`로 교체합니다.

4. 예측·AOI 등 전체 기능을 쓰려면 `NetBehaviour` 기반 API로 전환을 권장합니다.

---

## 에디터 도구

Unity 메뉴 → **MyNetEngine**

| 도구 | 용도 |
|------|------|
| **Bandwidth Profiler** | 연결별·오브젝트별·RPC별 전송량 |
| **Tick Graph** | 틱 주기 및 보간 alpha 시각화 |
| **SyncVar Analyzer** | `NetBehaviour`의 `[SyncProperty]` 필드 통계 |

---

## 주요 파일

| 경로 | 설명 |
|------|------|
| `Runtime/NetworkRunner.cs` | 서버/클라이언트 생명주기 핵심 |
| `Runtime/Core/Tick/NetworkTickSystem.cs` | 고정 틱 루프 |
| `Runtime/Core/Serialization/NetWriter.cs` | 바이너리 직렬화 (varint, 양자화, 압축) |
| `Runtime/Transport/Kcp/Kcp.cs` | KCP ARQ (ikcp.c 포팅) |
| `Runtime/Transport/KcpTransport.cs` | KCP + raw UDP 통합 |
| `Runtime/Replication/ReplicationManager.cs` | 스냅샷 생성 및 전달 |
| `Runtime/Prediction/PredictedBehaviour.cs` | 범용 예측 + 보정 |
| `Runtime/Interest/SpatialHashGrid.cs` | AOI 공간 분할 |
| `Runtime/Unity/NetworkRunnerBehaviour.cs` | Unity Inspector 진입점 |
| `Runtime/Unity/PredictedController.cs` | Tier-1 키네마틱 예측 모터 |
| `Compatibility/MirrorAdapter.cs` | Mirror API 이름 호환 |
| `SourceGenerator~/` | Roslyn `[SyncProperty]` 생성기 |
| `Samples/PlayerNetworkSample.cs` | 참고 구현 예제 |

---

## 설계 철학

- **서버 권한 우선** — 서버가 진실의 원천; 클라이언트는 명시적으로 허용된 경우에만 예측
- **틱 기반 시뮬레이션** — 프레임률에 의존하지 않는 고정 스텝 로직
- **델타 스냅샷 + Dirty 추적** — 변경된 값만 전송해 대역폭 절약
- **핫 패스 GC 0 지향** — 버퍼 풀링, struct 우선, 틱/복제 루프에서 할당 최소화
- **AOI 필수** — 공간 관심 영역 관리가 처음부터 내장

---

## 로드맵

| 항목 | 상태 |
|------|------|
| 서버 KCP 멀티 클라이언트 `conv` 관리 | 진행 중 — 현재 transport 인스턴스당 단일 conv |
| Host 모드 루프백 short-circuit | 미구현 (서버만 시작, 클라이언트 루프백 TODO 로그) |
| 물리 롤백 (Tier 3) | 미구현 — Unity 물리 씬 복수 필요 |
| 룸 / 로비 / 매치메이킹 | 미구현 |
| WebSocket transport | 스텁 — NativeWebSocket 등 연동 필요 |

이 영역은 향후 개선 예정이며, 기여와 이슈 제보를 환영합니다.
