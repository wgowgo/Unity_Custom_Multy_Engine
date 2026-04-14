using System;
using System.Collections.Generic;
using MyNetEngine.Core.Serialization;
using MyNetEngine.Core.Tick;
using MyNetEngine.Interest;
using MyNetEngine.Messaging;
using MyNetEngine.Objects;
using MyNetEngine.Replication;
using MyNetEngine.Transport;

namespace MyNetEngine
{
    /// <summary>
    /// 엔진 facade. 서버/클라 공통 lifecycle을 한 곳에서 조립.
    /// Unity에서는 MonoBehaviour wrapper가 이 Runner를 들고 Advance를 호출.
    /// </summary>
    public sealed class NetworkRunner
    {
        public enum Mode { None, Server, Client }

        public Mode CurrentMode { get; private set; }
        public NetworkTickSystem Tick { get; }
        public ITransport Transport { get; }
        public MessageRouter Router { get; }
        public RpcSystem Rpc { get; }
        public SpawnManager Spawn { get; }
        public InterestManager Interest { get; }
        public ReplicationManager Replication { get; private set; }
        public BatchSender Batcher { get; }

        public int LocalConnectionId { get; private set; }

        private readonly List<int> _connections = new List<int>();

        public event Action<int> OnConnected;
        public event Action<int> OnDisconnected;

        public NetworkRunner(ITransport transport, int tickRate = 30)
        {
            Transport = transport;
            Tick = new NetworkTickSystem(tickRate);
            Router = new MessageRouter();
            Rpc = new RpcSystem();
            Spawn = new SpawnManager();
            Interest = new InterestManager();
            Batcher = new BatchSender(transport);

            Router.Register(MessageType.Snapshot, (conn, r) =>
            {
                SnapshotReader.Apply(r, Spawn);
            });
            Router.Register(MessageType.ServerRpc, (conn, r) => Rpc.Invoke(conn, r));
            Router.Register(MessageType.ClientRpc, (conn, r) => Rpc.Invoke(conn, r));
            Router.Register(MessageType.TargetRpc, (conn, r) => Rpc.Invoke(conn, r));
            Router.Register(MessageType.UnreliableRpc, (conn, r) => Rpc.Invoke(conn, r));
            Router.Register(MessageType.Spawn, HandleSpawn);
            Router.Register(MessageType.Despawn, HandleDespawn);

            Tick.OnTick += OnNetworkTick;
        }

        public void StartServer(int port)
        {
            CurrentMode = Mode.Server;
            Transport.StartServer(port);
            Replication = new ReplicationManager(Transport, Spawn, Interest);
        }

        public void StartClient(string host, int port)
        {
            CurrentMode = Mode.Client;
            Transport.StartClient(host, port);
        }

        public void Stop()
        {
            Transport.Stop();
            CurrentMode = Mode.None;
            Spawn.Clear();
        }

        /// <summary>
        /// Unity Update에서 호출. transport 이벤트 처리 + tick 누적.
        /// </summary>
        public void Advance(double deltaTime)
        {
            while (Transport.PollEvent(out var evt))
            {
                switch (evt.Type)
                {
                    case NetEventType.Connected:
                        _connections.Add(evt.ConnectionId);
                        if (CurrentMode == Mode.Client) LocalConnectionId = evt.ConnectionId;
                        if (CurrentMode == Mode.Server) Replication?.AddConnection(evt.ConnectionId);
                        OnConnected?.Invoke(evt.ConnectionId);
                        break;
                    case NetEventType.Data:
                        // batched 가능: 첫 바이트를 보고 batch인지 단일인지 구분하는 규약이 필요하나
                        // 여기서는 단일 메시지 규약을 기본으로 함.
                        Router.Dispatch(evt.ConnectionId, evt.Data);
                        break;
                    case NetEventType.Disconnected:
                        _connections.Remove(evt.ConnectionId);
                        if (CurrentMode == Mode.Server) Replication?.RemoveConnection(evt.ConnectionId);
                        OnDisconnected?.Invoke(evt.ConnectionId);
                        break;
                }
            }

            Tick.Advance(deltaTime);
        }

        private void OnNetworkTick(uint tick)
        {
            // 서버: 각 NetBehaviour.OnNetworkTick -> replication 전송
            if (CurrentMode == Mode.Server)
            {
                foreach (var kv in Spawn.AllObjects)
                {
                    foreach (var b in kv.Value.Behaviours) b.OnNetworkTick();
                }
                Replication?.Tick(tick);
                Batcher.FlushAll();
            }
            else if (CurrentMode == Mode.Client)
            {
                foreach (var kv in Spawn.AllObjects)
                {
                    foreach (var b in kv.Value.Behaviours)
                    {
                        if (b.IsOwner) b.OnPredictedTick();
                    }
                }
            }
        }

        // ---- Spawn/Despawn 전송 ----
        public void ServerSpawnAndReplicate(uint prefabHash, int owner, AuthorityMode authority, float x, float y, float z)
        {
            if (CurrentMode != Mode.Server) return;
            var obj = Spawn.SpawnServer(prefabHash, owner, authority);
            obj.PosX = x; obj.PosY = y; obj.PosZ = z;
            Interest.Grid.Insert(obj);
            Replication?.MarkAllPendingInitial(obj.NetId);

            // 모든 observer에게 spawn 알림
            foreach (var conn in _connections)
            {
                var w = new NetWriter(64);
                w.WriteByte((byte)MessageType.Spawn);
                w.WriteVarUInt(prefabHash);
                w.WriteVarUInt(obj.NetId);
                w.WriteVarInt(owner);
                w.WriteByte((byte)authority);
                w.WriteFloat(x); w.WriteFloat(y); w.WriteFloat(z);
                Transport.Send(conn, w.ToSegment(), DeliveryChannel.ReliableOrdered);
            }
        }

        public void ServerDespawn(uint netId)
        {
            if (CurrentMode != Mode.Server) return;
            Interest.Grid.Remove(netId);
            Replication?.OnObjectDespawned(netId);
            foreach (var conn in _connections)
            {
                var w = new NetWriter(16);
                w.WriteByte((byte)MessageType.Despawn);
                w.WriteVarUInt(netId);
                Transport.Send(conn, w.ToSegment(), DeliveryChannel.ReliableOrdered);
            }
            Spawn.Despawn(netId);
        }

        private void HandleSpawn(int conn, NetReader r)
        {
            uint prefabHash = r.ReadVarUInt();
            uint netId = r.ReadVarUInt();
            int owner = r.ReadVarInt();
            var auth = (AuthorityMode)r.ReadByte();
            float x = r.ReadFloat(), y = r.ReadFloat(), z = r.ReadFloat();
            var obj = Spawn.SpawnClient(prefabHash, netId, owner, auth, LocalConnectionId);
            if (obj != null) { obj.PosX = x; obj.PosY = y; obj.PosZ = z; }
        }

        private void HandleDespawn(int conn, NetReader r)
        {
            uint netId = r.ReadVarUInt();
            Spawn.Despawn(netId);
        }
    }
}
