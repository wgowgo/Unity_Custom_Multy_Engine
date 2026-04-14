using System;
using System.Collections.Generic;
using MyNetEngine.Core.Metrics;
using MyNetEngine.Core.Serialization;
using MyNetEngine.Transport;

namespace MyNetEngine.Messaging
{
    /// <summary>
    /// 상태 동기화와 명확히 구분된 이벤트/명령 계층.
    /// - ServerRpc: 클라 -> 서버
    /// - ClientRpc: 서버 -> 모든 옵저버
    /// - TargetRpc: 서버 -> 특정 연결
    /// - UnreliableRpc: 빈번하고 손실 허용 이벤트
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class ServerRpcAttribute : Attribute
    {
        public DeliveryChannel Channel { get; set; } = DeliveryChannel.ReliableOrdered;
    }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class ClientRpcAttribute : Attribute
    {
        public DeliveryChannel Channel { get; set; } = DeliveryChannel.ReliableOrdered;
        public bool ExcludeOwner { get; set; }
    }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class TargetRpcAttribute : Attribute
    {
        public DeliveryChannel Channel { get; set; } = DeliveryChannel.ReliableOrdered;
    }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class UnreliableRpcAttribute : Attribute
    {
        public DeliveryChannel Channel { get; set; } = DeliveryChannel.UnreliableSequenced;
    }

    /// <summary>
    /// RPC 레지스트리. source generator 없이도 동작하도록 런타임 등록 지원.
    /// 각 RPC는 (NetReader) => void 로 래핑되며, 핸들러 쪽에서 인자 역직렬화.
    /// </summary>
    public sealed class RpcSystem
    {
        public delegate void RpcHandler(int senderConn, uint netId, byte rpcId, NetReader r);

        private readonly Dictionary<(uint netId, byte rpcId), RpcHandler> _handlers
            = new Dictionary<(uint, byte), RpcHandler>();

        public void Register(uint netId, byte rpcId, RpcHandler handler)
        {
            _handlers[(netId, rpcId)] = handler;
        }

        public void Unregister(uint netId)
        {
            var keys = new List<(uint, byte)>();
            foreach (var k in _handlers.Keys) if (k.netId == netId) keys.Add(k);
            foreach (var k in keys) _handlers.Remove(k);
        }

        public void Invoke(int senderConn, NetReader reader)
        {
            uint netId = reader.ReadVarUInt();
            byte rpcId = reader.ReadByte();
            if (_handlers.TryGetValue((netId, rpcId), out var h))
            {
                NetMetrics.Global.RpcsReceived++;
                h(senderConn, netId, rpcId, reader);
            }
        }

        public static void WriteHeader(NetWriter w, MessageType type, uint netId, byte rpcId)
        {
            w.WriteByte((byte)type);
            w.WriteVarUInt(netId);
            w.WriteByte(rpcId);
        }
    }
}
