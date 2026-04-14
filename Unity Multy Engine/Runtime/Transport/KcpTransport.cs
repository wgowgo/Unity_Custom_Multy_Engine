using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using MyNetEngine.Core.Buffers;
using MyNetEngine.Core.Metrics;
using MyNetEngine.Transport.Kcp;

namespace MyNetEngine.Transport
{
    /// <summary>
    /// KCP reliable-over-UDP transport.
    /// - unreliable sequenced: 원시 UDP 전송 (상태 동기화용)
    /// - reliable ordered: KCP ARQ 경유 (스폰/이벤트/RPC용)
    /// </summary>
    public sealed class KcpTransport : ITransport
    {
        public int MaxPacketSize => Kcp.IKCP_MTU_DEF - Kcp.IKCP_OVERHEAD - 1; // 1byte channel
        public bool IsRunning { get; private set; }

        private Socket _socket;
        private Thread _rxThread;
        private volatile bool _running;

        private readonly ConcurrentQueue<NetEvent> _evtQueue = new ConcurrentQueue<NetEvent>();
        private readonly Dictionary<int, ConnState> _conns    = new Dictionary<int, ConnState>();
        private readonly Dictionary<IPEndPoint, int> _epToId  = new Dictionary<IPEndPoint, int>();
        private readonly object _lock = new object();
        private int _nextId = 1;
        private bool _isServer;
        private uint _clock; // ms

        private sealed class ConnState
        {
            public IPEndPoint Ep;
            public Kcp.Kcp Kcp;
            public uint KcpClock;
        }

        public void StartServer(int port)
        {
            _isServer = true;
            Bind(new IPEndPoint(IPAddress.Any, port));
        }

        public void StartClient(string host, int port)
        {
            _isServer = false;
            Bind(new IPEndPoint(IPAddress.Any, 0));
            var ep = new IPEndPoint(IPAddress.Parse(host), port);
            lock (_lock) CreateConn(ep); // 즉시 연결 수립 (서버는 첫 패킷 수신 시)
        }

        private void Bind(IPEndPoint ep)
        {
            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            _socket.Bind(ep);
            IsRunning = true;
            _running = true;
            _rxThread = new Thread(RxLoop) { IsBackground = true, Name = "KcpRx" };
            _rxThread.Start();
        }

        private int CreateConn(IPEndPoint ep)
        {
            int id = _nextId++;
            var conn = new ConnState { Ep = ep };
            conn.Kcp = new Kcp.Kcp((uint)id, (buf, len) =>
            {
                try { _socket.SendTo(buf, 0, len, SocketFlags.None, ep); NetMetrics.Global.RecordSent(len); }
                catch { }
            });
            conn.Kcp.SetNoDelay(true, 10, 2, true);
            conn.Kcp.SetWindowSize(128, 128);
            _conns[id] = conn;
            _epToId[ep] = id;
            return id;
        }

        private void RxLoop()
        {
            byte[] buf = new byte[2048];
            EndPoint remote = new IPEndPoint(IPAddress.Any, 0);
            while (_running)
            {
                try
                {
                    int n = _socket.ReceiveFrom(buf, 0, buf.Length, SocketFlags.None, ref remote);
                    if (n < 1) continue;
                    var ep = (IPEndPoint)remote;
                    byte channel = buf[0];
                    int connId;
                    bool isNew = false;
                    lock (_lock)
                    {
                        var snap = new IPEndPoint(ep.Address, ep.Port);
                        if (!_epToId.TryGetValue(snap, out connId))
                        {
                            if (!_isServer) continue;
                            connId = CreateConn(snap);
                            isNew = true;
                        }
                    }
                    if (isNew) _evtQueue.Enqueue(new NetEvent(NetEventType.Connected, connId));
                    NetMetrics.Global.RecordReceived(n);

                    if (channel == (byte)DeliveryChannel.ReliableOrdered)
                    {
                        // KCP 경로
                        uint clock = (uint)Environment.TickCount;
                        lock (_lock)
                        {
                            if (_conns.TryGetValue(connId, out var cs))
                            {
                                cs.Kcp.Input(buf, 1, n - 1);
                                cs.Kcp.Update(clock);
                                // peek all ready messages
                                byte[] tmp = new byte[MaxPacketSize + 64];
                                int rcvLen;
                                while ((rcvLen = cs.Kcp.Receive(tmp, tmp.Length)) > 0)
                                {
                                    byte[] copy = ByteBufferPool.Shared.Rent(rcvLen);
                                    Buffer.BlockCopy(tmp, 0, copy, 0, rcvLen);
                                    _evtQueue.Enqueue(new NetEvent(NetEventType.Data, connId,
                                        new ArraySegment<byte>(copy, 0, rcvLen),
                                        DeliveryChannel.ReliableOrdered));
                                }
                            }
                        }
                    }
                    else
                    {
                        // raw UDP (unreliable)
                        byte[] copy = ByteBufferPool.Shared.Rent(n - 1);
                        Buffer.BlockCopy(buf, 1, copy, 0, n - 1);
                        _evtQueue.Enqueue(new NetEvent(NetEventType.Data, connId,
                            new ArraySegment<byte>(copy, 0, n - 1),
                            DeliveryChannel.UnreliableSequenced));
                    }
                }
                catch (SocketException) { }
                catch (ObjectDisposedException) { return; }
                catch (Exception e) { _evtQueue.Enqueue(new NetEvent(NetEventType.Error, 0, error: e.Message)); }
            }
        }

        public bool PollEvent(out NetEvent evt)
        {
            // KCP flush 주기
            uint now = (uint)Environment.TickCount;
            lock (_lock)
            {
                foreach (var kv in _conns) kv.Value.Kcp.Update(now);
            }
            return _evtQueue.TryDequeue(out evt);
        }

        public void Send(int connectionId, ArraySegment<byte> data, DeliveryChannel channel)
        {
            if (!IsRunning) return;
            lock (_lock)
            {
                if (!_conns.TryGetValue(connectionId, out var cs)) return;
                byte[] outBuf = ByteBufferPool.Shared.Rent(data.Count + 1);
                outBuf[0] = (byte)channel;
                Buffer.BlockCopy(data.Array, data.Offset, outBuf, 1, data.Count);

                if (channel == DeliveryChannel.ReliableOrdered)
                {
                    // KCP 경유
                    cs.Kcp.Send(outBuf, data.Count + 1);
                    cs.Kcp.Update((uint)Environment.TickCount);
                }
                else
                {
                    // raw UDP
                    try { _socket.SendTo(outBuf, 0, data.Count + 1, SocketFlags.None, cs.Ep); NetMetrics.Global.RecordSent(data.Count + 1); }
                    catch { }
                }
                ByteBufferPool.Shared.Return(outBuf);
            }
        }

        public void Disconnect(int connectionId)
        {
            lock (_lock)
            {
                if (!_conns.TryGetValue(connectionId, out var cs)) return;
                _epToId.Remove(cs.Ep);
                _conns.Remove(connectionId);
            }
            _evtQueue.Enqueue(new NetEvent(NetEventType.Disconnected, connectionId));
        }

        public void Stop()
        {
            _running = false;
            IsRunning = false;
            try { _socket?.Close(); } catch { }
        }
    }
}
