#if UNITY_2021_1_OR_NEWER
using System;
using System.Collections.Generic;
using UnityEngine;
using MyNetEngine.Objects;
using MyNetEngine.Transport;

namespace MyNetEngine.Unity
{
    // Inspector에서 prefab 등록하는 엔트리
    [Serializable]
    public sealed class NetworkPrefabEntry
    {
        [Tooltip("고유 해시. 서버/클라 동일해야 함. 자동 생성 or 수동 입력")]
        public uint Hash;
        [Tooltip("스폰할 프리팹 GameObject")]
        public GameObject Prefab;
    }

    /// <summary>
    /// Unity 씬 진입 컴포넌트. Inspector에서 아래를 설정:
    ///   - Mode: Server / Client / Host
    ///   - Port / Host / TickRate / Transport Kind
    ///   - NetworkPrefabs: 서버-클라 공유 prefab 해시 리스트
    /// </summary>
    public sealed class NetworkRunnerBehaviour : MonoBehaviour
    {
        public enum StartMode { None, Server, Client, Host }
        public enum TransportKind { KCP, UdpLite, WebSocket }

        [Header("Mode")]
        public StartMode Mode = StartMode.None;
        public string Host = "127.0.0.1";
        public int Port = 7777;

        [Header("Tick")]
        [Range(10, 120)]
        public int TickRate = 30;

        [Header("Transport")]
        public TransportKind Kind = TransportKind.KCP;

        [Header("Prefabs")]
        [Tooltip("Hash는 서버-클라 동일해야 함. 중복 불가.")]
        public List<NetworkPrefabEntry> NetworkPrefabs = new List<NetworkPrefabEntry>();

        [Header("Interest")]
        [Min(1f)]
        public float AoiRadius = 60f;
        [Min(5f)]
        public float GridCellSize = 20f;

        [Header("Bandwidth (bytes/tick per conn)")]
        public int BudgetPerTickPerConn = 8192;

        public NetworkRunner Runner { get; private set; }

        private void Awake()
        {
            ITransport t = Kind switch
            {
                TransportKind.KCP        => new KcpTransport(),
                TransportKind.WebSocket  => new WebSocketTransport(),
                _                        => new UdpLiteTransport()
            };
            Runner = new NetworkRunner(t, TickRate);
            Runner.Interest.DefaultRadius = AoiRadius;

            // Inspector prefab 등록
            var seen = new HashSet<uint>();
            foreach (var entry in NetworkPrefabs)
            {
                if (entry.Prefab == null) continue;
                if (!seen.Add(entry.Hash))
                {
                    Debug.LogWarning($"[MyNetEngine] Duplicate prefab hash {entry.Hash} — skipped.");
                    continue;
                }
                uint hash = entry.Hash;
                GameObject prefab = entry.Prefab;

                Runner.Spawn.RegisterPrefab(
                    hash,
                    factory: (h, owner) =>
                    {
                        var go = Instantiate(prefab);
                        var netObj = new NetworkObject { PrefabHash = hash };
                        // NetBehaviour 컴포넌트 자동 수집
                        foreach (var nb in go.GetComponentsInChildren<NetBehaviour>(true))
                            netObj.Register(nb);
                        return netObj;
                    },
                    destroyer: obj =>
                    {
                        // NetBehaviour의 첫 번째 항목에서 GameObject를 추적하는 방법 필요.
                        // 실제 연동 시 NetBehaviour에 gameObject 참조 추가 권장.
                        // 여기서는 단순 로그.
                        Debug.Log($"[MyNetEngine] Despawn netId={obj.NetId}");
                    }
                );
            }
        }

        private void Start()
        {
            switch (Mode)
            {
                case StartMode.Server:
                    Runner.StartServer(Port);
                    Debug.Log($"[MyNetEngine] Server started on :{Port}, tick={TickRate}");
                    break;
                case StartMode.Client:
                    Runner.StartClient(Host, Port);
                    Debug.Log($"[MyNetEngine] Client connecting to {Host}:{Port}");
                    break;
                case StartMode.Host:
                    Runner.StartServer(Port);
                    // Host: 루프백으로 자기 자신에 연결 (별도 Runner 없이 내부 short-circuit 권장. 현재는 로그만)
                    Debug.Log($"[MyNetEngine] Host mode — server started. Loopback client TODO.");
                    break;
            }
        }

        private void Update() => Runner?.Advance(Time.deltaTime);

        private void OnDestroy() => Runner?.Stop();

        // 유틸: 런타임 스폰 편의 메서드
        public void SpawnObject(uint prefabHash, int ownerConnId,
            AuthorityMode authority, Vector3 pos)
        {
            if (Mode != StartMode.Server && Mode != StartMode.Host) return;
            Runner.ServerSpawnAndReplicate(prefabHash, ownerConnId, authority, pos.x, pos.y, pos.z);
        }
    }
}
#endif
