using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using MyNetEngine.Core.Buffers;
using MyNetEngine.Core.Metrics;

namespace MyNetEngine.Transport
{
    /// <summary>
    /// 경량 UDP transport. 실제 production에서는 reliable layer(KCP 등) 권장.
    /// - fragmentation/ack/resend는 최소한으로 shim.
    /// - 내부 스레드에서 receive, 메인 스레드에서 PollEvent로 수거.
    /// </summary>
    public sealed class UdpLiteTransport : ITransport
    {
        public int MaxPacketSize => 1200; // MTU 안전값

        private Socket _socket;
        private Thread _rxThread;
        private volatile bool _running;
        private readonly ConcurrentQueue<NetEvent> _eventQueue = new ConcurrentQueue<NetEvent>();
        private readonly Dictionary<IPEndPoint, int> _endpointToConn = new Dictionary<IPEndPoint, int>();
        private readonly Dictionary<int, IPEndPoint> _connToEndpoint = new Dictionary<int, IPEndPoint>();
        private int _nextConnId = 1;
        private readonly object _mapLock = new object();
        private bool _isServer;
        private IPEndPoint _clientServerEp;

        public bool IsRunning => _running;

        public void StartServer(int port)
        {
            _isServer = true;
            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            _socket.Bind(new IPEndPoint(IPAddress.Any, port));
            StartRx();
        }

        public void StartClient(string host, int port)
        {
            _isServer = false;
            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            _socket.Bind(new IPEndPoint(IPAddress.Any, 0));
            _clientServerEp = new IPEndPoint(IPAddress.Parse(host), port);
            // client 접속 이벤트는 즉시 발행 (UDP 접속 개념상 단순화)
            lock (_mapLock)
            {
                int id = _nextConnId++;
                _endpointToConn[_clientServerEp] = id;
                _connToEndpoint[id] = _clientServerEp;
                _eventQueue.Enqueue(new NetEvent(NetEventType.Connected, id));
            }
            StartRx();
        }

        private void StartRx()
        {
            _running = true;
            _rxThread = new Thread(RxLoop) { IsBackground = true, Name = "MyNet.UdpRx" };
            _rxThread.Start();
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
                    if (n <= 0) continue;
                    var ep = (IPEndPoint)remote;
                    int connId;
                    bool newConn = false;
                    lock (_mapLock)
                    {
                        if (!_endpointToConn.TryGetValue(ep, out connId))
                        {
                            connId = _nextConnId++;
                            var snap = new IPEndPoint(ep.Address, ep.Port);
                            _endpointToConn[snap] = connId;
                            _connToEndpoint[connId] = snap;
                            newConn = true;
                        }
                    }
                    if (newConn && _isServer)
                        _eventQueue.Enqueue(new NetEvent(NetEventType.Connected, connId));

                    // 데이터 복사 (pool 사용)
                    byte[] copy = ByteBufferPool.Shared.Rent(n);
                    Buffer.BlockCopy(buf, 0, copy, 0, n);
                    _eventQueue.Enqueue(new NetEvent(NetEventType.Data, connId,
                        new ArraySegment<byte>(copy, 0, n)));
                    NetMetrics.Global.RecordReceived(n);
                }
                catch (SocketException) { /* drop */ }
                catch (ObjectDisposedException) { return; }
                catch (Exception e)
                {
                    _eventQueue.Enqueue(new NetEvent(NetEventType.Error, 0, default, DeliveryChannel.UnreliableSequenced, e.Message));
                }
            }
        }

        public bool PollEvent(out NetEvent evt) => _eventQueue.TryDequeue(out evt);

        public void Send(int connectionId, ArraySegment<byte> data, DeliveryChannel channel)
        {
            if (!_running) return;
            IPEndPoint ep;
            lock (_mapLock)
            {
                if (!_connToEndpoint.TryGetValue(connectionId, out ep)) return;
            }
            try
            {
                _socket.SendTo(data.Array, data.Offset, data.Count, SocketFlags.None, ep);
                NetMetrics.Global.RecordSent(data.Count);
            }
            catch { /* drop */ }
        }

        public void Disconnect(int connectionId)
        {
            lock (_mapLock)
            {
                if (_connToEndpoint.TryGetValue(connectionId, out var ep))
                {
                    _connToEndpoint.Remove(connectionId);
                    _endpointToConn.Remove(ep);
                }
            }
            _eventQueue.Enqueue(new NetEvent(NetEventType.Disconnected, connectionId));
        }

        public void Stop()
        {
            _running = false;
            try { _socket?.Close(); } catch { }
            _socket = null;
            _rxThread = null;
        }
    }
}
