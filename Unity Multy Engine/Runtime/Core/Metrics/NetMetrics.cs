using System;
using System.Collections.Generic;

namespace MyNetEngine.Core.Metrics
{
    /// <summary>
    /// 네트워크 프로파일링용 경량 메트릭스.
    /// per-object bandwidth, per-rpc cost, snapshot size 등.
    /// </summary>
    public sealed class NetMetrics
    {
        public static readonly NetMetrics Global = new NetMetrics();

        public long TotalBytesSent;
        public long TotalBytesReceived;
        public long TotalPacketsSent;
        public long TotalPacketsReceived;
        public long SnapshotsBuilt;
        public long SnapshotBytes;
        public long RpcsSent;
        public long RpcsReceived;
        public long Resends;

        private readonly Dictionary<uint, long> _perObjectBytes = new Dictionary<uint, long>();
        private readonly Dictionary<string, long> _perRpcBytes = new Dictionary<string, long>();

        public void RecordSent(int bytes)
        {
            TotalBytesSent += bytes;
            TotalPacketsSent++;
        }

        public void RecordReceived(int bytes)
        {
            TotalBytesReceived += bytes;
            TotalPacketsReceived++;
        }

        public void RecordObject(uint netId, int bytes)
        {
            _perObjectBytes.TryGetValue(netId, out var cur);
            _perObjectBytes[netId] = cur + bytes;
        }

        public void RecordRpc(string name, int bytes)
        {
            RpcsSent++;
            _perRpcBytes.TryGetValue(name, out var cur);
            _perRpcBytes[name] = cur + bytes;
        }

        public IReadOnlyDictionary<uint, long> PerObject => _perObjectBytes;
        public IReadOnlyDictionary<string, long> PerRpc => _perRpcBytes;

        public void Reset()
        {
            TotalBytesSent = TotalBytesReceived = 0;
            TotalPacketsSent = TotalPacketsReceived = 0;
            SnapshotsBuilt = SnapshotBytes = 0;
            RpcsSent = RpcsReceived = 0;
            Resends = 0;
            _perObjectBytes.Clear();
            _perRpcBytes.Clear();
        }
    }
}
