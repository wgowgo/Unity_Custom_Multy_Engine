using System;
using System.Collections.Generic;
using MyNetEngine.Core.Buffers;
using MyNetEngine.Core.Serialization;

namespace MyNetEngine.Transport
{
    /// <summary>
    /// tick 동안 보낼 작은 메시지를 하나의 패킷으로 coalesce하는 레이어.
    /// [count:varuint][ (len:varuint)(bytes) ... ]
    /// MTU 초과 시 자동 flush.
    /// </summary>
    public sealed class BatchSender
    {
        private readonly ITransport _transport;
        private readonly int _mtu;
        private readonly Dictionary<(int conn, DeliveryChannel ch), Batch> _batches
            = new Dictionary<(int, DeliveryChannel), Batch>();

        private sealed class Batch
        {
            public NetWriter Writer = new NetWriter(1200);
            public int Count;
        }

        public BatchSender(ITransport t)
        {
            _transport = t;
            _mtu = t.MaxPacketSize;
        }

        public void Queue(int conn, DeliveryChannel ch, ArraySegment<byte> msg)
        {
            var key = (conn, ch);
            if (!_batches.TryGetValue(key, out var batch))
            {
                batch = new Batch();
                _batches[key] = batch;
            }

            // 대충 예상 크기 검사: (varuint<=5) + payload
            int predicted = batch.Writer.Position + 5 + msg.Count;
            if (predicted > _mtu - 4)
            {
                FlushBatch(conn, ch, batch);
            }

            batch.Writer.WriteVarUInt((uint)msg.Count);
            batch.Writer.WriteBytes(msg);
            batch.Count++;
        }

        public void FlushAll()
        {
            foreach (var kv in _batches)
            {
                if (kv.Value.Count > 0)
                    FlushBatch(kv.Key.conn, kv.Key.ch, kv.Value);
            }
        }

        private void FlushBatch(int conn, DeliveryChannel ch, Batch batch)
        {
            if (batch.Count == 0) return;
            // header writer
            var pool = ByteBufferPool.Shared.Rent(batch.Writer.Position + 8);
            var outw = new NetWriter(pool);
            outw.WriteVarUInt((uint)batch.Count);
            outw.WriteBytes(batch.Writer.ToSegment());
            _transport.Send(conn, outw.ToSegment(), ch);
            ByteBufferPool.Shared.Return(pool);
            batch.Writer.Reset();
            batch.Count = 0;
        }

        /// <summary>
        /// 수신 측 unpack. 개별 메시지 span 순회.
        /// </summary>
        public static void Unpack(ArraySegment<byte> packet, Action<ArraySegment<byte>> onMessage)
        {
            var r = new NetReader(packet.Array, packet.Offset, packet.Count);
            uint count = r.ReadVarUInt();
            for (uint i = 0; i < count; i++)
            {
                uint len = r.ReadVarUInt();
                var seg = r.ReadBytes((int)len);
                onMessage(seg);
            }
        }
    }
}
