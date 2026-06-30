// KCP reliable-over-UDP core (C# port of ikcp.c by skywind3000)
// https://github.com/skywind3000/kcp/blob/master/ikcp.c
// License: MIT
using System;
using System.Collections.Generic;

namespace MyNetEngine.Transport.Kcp
{
    public sealed class Kcp
    {
        public const int IKCP_RTO_NDL    = 30;
        public const int IKCP_RTO_MIN    = 100;
        public const int IKCP_RTO_DEF    = 200;
        public const int IKCP_RTO_MAX    = 60000;
        public const int IKCP_CMD_PUSH   = 81;
        public const int IKCP_CMD_ACK    = 82;
        public const int IKCP_CMD_WASK   = 83;
        public const int IKCP_CMD_WINS   = 84;
        public const int IKCP_ASK_SEND   = 1;
        public const int IKCP_ASK_TELL   = 2;
        public const int IKCP_WND_SND    = 32;
        public const int IKCP_WND_RCV    = 128;
        public const int IKCP_MTU_DEF    = 1200;
        public const int IKCP_ACK_FAST   = 3;
        public const int IKCP_INTERVAL   = 100;
        public const int IKCP_OVERHEAD   = 24;
        public const int IKCP_DEADLINK   = 20;
        public const int IKCP_THRESH_INIT = 2;
        public const int IKCP_THRESH_MIN  = 2;
        public const int IKCP_PROBE_INIT  = 7000;
        public const int IKCP_PROBE_LIMIT = 120000;
        public const int IKCP_FASTACK_LIMIT = 5;

        private struct Segment
        {
            public uint conv, cmd, frg, wnd, ts, sn, una, len;
            public uint resendts, rto, fastack, xmit;
            public byte[] data;
        }

        private uint _conv, _mtu, _mss, _state;
        private uint _sndUna, _sndNxt, _rcvNxt;
        private uint _ssthresh;
        private int _rxRttval, _rxSrtt, _rxRto, _rxMinrto;
        private uint _sndWnd, _rcvWnd, _rmtWnd, _cwnd, _probe;
        private uint _current, _interval, _tsFlush, _xmit;
        private bool _nodelay, _updated;
        private uint _tsProbe, _probeWait;
        private uint _deadLink, _incr;
        private int _fastAck;

        private readonly List<Segment> _sndQueue  = new List<Segment>();
        private readonly List<Segment> _rcvQueue  = new List<Segment>();
        private readonly List<Segment> _sndBuf    = new List<Segment>();
        private readonly List<Segment> _rcvBuf    = new List<Segment>();
        private readonly List<(uint sn, uint ts)> _acklist = new List<(uint, uint)>();

        private byte[] _buffer;
        private int _fastResend;
        private bool _nocwnd;

        public Action<byte[], int> Output;
        public bool IsDeadLink => _state != 0;

        public Kcp(uint conv, Action<byte[], int> output)
        {
            _conv = conv;
            Output = output;
            _sndWnd = IKCP_WND_SND;
            _rcvWnd = IKCP_WND_RCV;
            _rmtWnd = IKCP_WND_RCV;
            _mtu = IKCP_MTU_DEF;
            _mss = _mtu - IKCP_OVERHEAD;
            _rxRto = IKCP_RTO_DEF;
            _rxMinrto = IKCP_RTO_MIN;
            _interval = IKCP_INTERVAL;
            _tsFlush = IKCP_INTERVAL;
            _ssthresh = IKCP_THRESH_INIT;
            _deadLink = IKCP_DEADLINK;
            _buffer = new byte[_mtu];
        }

        private static uint IMin(uint a, uint b) => a < b ? a : b;
        private static uint IMax(uint a, uint b) => a > b ? a : b;
        private static int IBound(int lower, int middle, int upper) => Math.Max(lower, Math.Min(middle, upper));
        private static bool ITimeDiff(uint later, uint earlier) => (int)(later - earlier) >= 0;

        public void SetNoDelay(bool nodelay, int interval = -1, int resend = 0, bool nocwnd = false)
        {
            _nodelay = nodelay;
            if (nodelay) _rxMinrto = IKCP_RTO_NDL;
            if (interval >= 0) _interval = (uint)Math.Max(10, Math.Min(5000, interval));
            if (resend >= 0) _fastResend = resend;
            _nocwnd = nocwnd;
        }

        public void SetWindowSize(int sndWnd, int rcvWnd)
        {
            if (sndWnd > 0) _sndWnd = (uint)sndWnd;
            if (rcvWnd > 0) _rcvWnd = (uint)Math.Max(rcvWnd, IKCP_WND_RCV);
        }

        public int Receive(byte[] buffer, int len)
        {
            if (_rcvQueue.Count == 0) return -1;
            int peekSize = PeekSize();
            if (peekSize < 0) return -2;
            if (peekSize > len) return -3;

            bool recover = _rcvQueue.Count >= _rcvWnd;
            int n = 0;
            for (int i = 0; i < _rcvQueue.Count; )
            {
                var seg = _rcvQueue[i];
                Array.Copy(seg.data, 0, buffer, n, (int)seg.len);
                n += (int)seg.len;
                _rcvQueue.RemoveAt(i);
                if (seg.frg == 0) break;
            }

            MoveReceive();
            if (recover && _rcvQueue.Count < _rcvWnd)
            {
                _probe |= IKCP_ASK_TELL;
            }
            return n;
        }

        private int PeekSize()
        {
            if (_rcvQueue.Count == 0) return -1;
            var head = _rcvQueue[0];
            if (head.frg == 0) return (int)head.len;
            if (_rcvQueue.Count < head.frg + 1) return -1;
            int length = 0;
            foreach (var seg in _rcvQueue)
            {
                length += (int)seg.len;
                if (seg.frg == 0) break;
            }
            return length;
        }

        public int Send(byte[] buffer, int len)
        {
            if (len < 0) return -1;
            int count = len <= (int)_mss ? 1 : (int)((len + _mss - 1) / _mss);
            if (count >= IKCP_WND_RCV) return -2;
            if (count == 0) count = 1;

            int offset = 0;
            for (int i = 0; i < count; i++)
            {
                int segLen = Math.Min(len, (int)_mss);
                var seg = new Segment
                {
                    conv = _conv,
                    cmd = IKCP_CMD_PUSH,
                    frg = (uint)(count - i - 1),
                    data = new byte[segLen],
                    len = (uint)segLen
                };
                Array.Copy(buffer, offset, seg.data, 0, segLen);
                offset += segLen;
                len -= segLen;
                _sndQueue.Add(seg);
            }
            return 0;
        }

        private void UpdateAck(int rtt)
        {
            if (_rxSrtt == 0)
            {
                _rxSrtt = rtt;
                _rxRttval = rtt / 2;
            }
            else
            {
                int delta = rtt - _rxSrtt;
                if (delta < 0) delta = -delta;
                _rxRttval = (3 * _rxRttval + delta) / 4;
                _rxSrtt = Math.Max(1, (7 * _rxSrtt + rtt) / 8);
            }
            int rto = _rxSrtt + Math.Max((int)_interval, 4 * _rxRttval);
            _rxRto = IBound(_rxMinrto, rto, IKCP_RTO_MAX);
        }

        private void ShrinkBuf()
        {
            _sndUna = _sndBuf.Count > 0 ? _sndBuf[0].sn : _sndNxt;
        }

        private void ParseAck(uint sn)
        {
            if (!ITimeDiff(sn, _sndUna) || !ITimeDiff(_sndNxt, sn)) return;
            for (int i = 0; i < _sndBuf.Count; i++)
            {
                var seg = _sndBuf[i];
                if (sn == seg.sn) { _sndBuf.RemoveAt(i); break; }
                if (ITimeDiff(sn, seg.sn)) break;
                seg.fastack++;
                _sndBuf[i] = seg;
            }
        }

        private void ParseUna(uint una)
        {
            for (int i = _sndBuf.Count - 1; i >= 0; i--)
            {
                if (ITimeDiff(una, _sndBuf[i].sn)) _sndBuf.RemoveAt(i);
                else break;
            }
        }

        private void ParseFastAck(uint sn, uint ts)
        {
            if (!ITimeDiff(sn, _sndUna) || !ITimeDiff(_sndNxt, sn)) return;
            for (int i = 0; i < _sndBuf.Count; i++)
            {
                var seg = _sndBuf[i];
                if (ITimeDiff(sn, seg.sn)) break;
                if (sn != seg.sn && ITimeDiff(seg.ts, ts))
                {
                    seg.fastack++;
                    _sndBuf[i] = seg;
                }
            }
        }

        private void AckPush(uint sn, uint ts) => _acklist.Add((sn, ts));

        private void ParseData(Segment newseg)
        {
            uint sn = newseg.sn;
            if (!ITimeDiff(sn, _rcvNxt) || ITimeDiff(sn, _rcvNxt + _rcvWnd)) return;
            bool repeat = false;
            for (int i = _rcvBuf.Count - 1; i >= 0; i--)
            {
                if (_rcvBuf[i].sn == sn) { repeat = true; break; }
                if (ITimeDiff(sn, _rcvBuf[i].sn)) { _rcvBuf.Insert(i + 1, newseg); goto inserted; }
            }
            if (!repeat) _rcvBuf.Insert(0, newseg);
            inserted:
            MoveReceive();
        }

        private void MoveReceive()
        {
            while (_rcvBuf.Count > 0)
            {
                var seg = _rcvBuf[0];
                if (seg.sn == _rcvNxt && _rcvQueue.Count < _rcvWnd)
                {
                    _rcvBuf.RemoveAt(0);
                    _rcvQueue.Add(seg);
                    _rcvNxt++;
                }
                else break;
            }
        }

        public int Input(byte[] data, int offset, int size)
        {
            uint prevUna = _sndUna;
            if (size < IKCP_OVERHEAD) return -1;

            while (true)
            {
                if (size < IKCP_OVERHEAD) break;
                uint conv = BitConverter.ToUInt32(data, offset);
                if (conv != _conv) return -1;
                uint cmd = data[offset + 4];
                uint frg = data[offset + 5];
                uint wnd = BitConverter.ToUInt16(data, offset + 6);
                uint ts  = BitConverter.ToUInt32(data, offset + 8);
                uint sn  = BitConverter.ToUInt32(data, offset + 12);
                uint una = BitConverter.ToUInt32(data, offset + 16);
                uint len = BitConverter.ToUInt32(data, offset + 20);
                offset += IKCP_OVERHEAD; size -= IKCP_OVERHEAD;
                if (size < len) return -2;
                if (cmd != IKCP_CMD_PUSH && cmd != IKCP_CMD_ACK && cmd != IKCP_CMD_WASK && cmd != IKCP_CMD_WINS)
                    return -3;
                _rmtWnd = wnd;
                ParseUna(una);
                ShrinkBuf();

                if (cmd == IKCP_CMD_ACK)
                {
                    if (ITimeDiff(_current, ts))
                        UpdateAck((int)(_current - ts));
                    ParseAck(sn);
                    ShrinkBuf();
                }
                else if (cmd == IKCP_CMD_PUSH)
                {
                    if (ITimeDiff(sn, _rcvNxt + _rcvWnd))
                    {
                        AckPush(sn, ts);
                        if (ITimeDiff(sn, _rcvNxt))
                        {
                            var seg = new Segment { conv = conv, cmd = cmd, frg = frg, wnd = wnd, ts = ts, sn = sn, una = una, len = len };
                            if (len > 0) { seg.data = new byte[len]; Array.Copy(data, offset, seg.data, 0, (int)len); }
                            ParseData(seg);
                        }
                    }
                }
                else if (cmd == IKCP_CMD_WASK)
                {
                    _probe |= IKCP_ASK_TELL;
                }
                else if (cmd == IKCP_CMD_WINS) { }

                offset += (int)len;
                size   -= (int)len;
            }

            if (ITimeDiff(_sndUna, prevUna))
            {
                if (_cwnd < _sndWnd)
                {
                    uint mss = _mss;
                    if (_cwnd < _ssthresh) { _cwnd++; _incr += mss; }
                    else
                    {
                        if (_incr < mss) _incr = mss;
                        _incr += mss * mss / _incr + mss / 16;
                        if ((_cwnd + 1) * mss <= _incr) { _cwnd = (_incr + mss - 1) / mss; }
                    }
                    if (_cwnd > _rmtWnd) { _cwnd = _rmtWnd; _incr = _cwnd * mss; }
                }
            }
            return 0;
        }

        private void Flush()
        {
            if (!_updated) return;
            uint current = _current;
            int offset = 0;
            bool change = false;
            bool lost = false;

            var seg = new Segment { conv = _conv, cmd = IKCP_CMD_ACK, wnd = (uint)Math.Max(0, (int)_rcvWnd - _rcvQueue.Count), una = _rcvNxt };

            foreach (var (sn, ts) in _acklist)
            {
                if (offset + IKCP_OVERHEAD > _mtu) { Output(_buffer, offset); offset = 0; }
                seg.sn = sn; seg.ts = ts;
                Encode(seg, _buffer, ref offset);
            }
            _acklist.Clear();

            if (_rmtWnd == 0)
            {
                if (_probeWait == 0) { _probeWait = IKCP_PROBE_INIT; _tsProbe = current + _probeWait; }
                else if (ITimeDiff(current, _tsProbe)) { _probeWait = IMax(_probeWait, 4000); _probeWait += _probeWait / 2; _probeWait = IMin(_probeWait, (uint)IKCP_PROBE_LIMIT); _tsProbe = current + _probeWait; _probe |= IKCP_ASK_SEND; }
            }
            else { _tsProbe = 0; _probeWait = 0; }

            if ((_probe & IKCP_ASK_SEND) != 0)
            {
                seg.cmd = IKCP_CMD_WASK;
                if (offset + IKCP_OVERHEAD > _mtu) { Output(_buffer, offset); offset = 0; }
                Encode(seg, _buffer, ref offset);
            }
            if ((_probe & IKCP_ASK_TELL) != 0)
            {
                seg.cmd = IKCP_CMD_WINS;
                if (offset + IKCP_OVERHEAD > _mtu) { Output(_buffer, offset); offset = 0; }
                Encode(seg, _buffer, ref offset);
            }
            _probe = 0;

            // congestion window
            uint cwnd = IMin(_sndWnd, _rmtWnd);
            if (!_nocwnd) cwnd = IMin(_cwnd, cwnd);
            while (ITimeDiff(_sndNxt, _sndUna + cwnd))
            {
                if (_sndQueue.Count == 0) break;
                var newseg = _sndQueue[0]; _sndQueue.RemoveAt(0);
                newseg.conv = _conv; newseg.cmd = IKCP_CMD_PUSH;
                newseg.wnd = seg.wnd; newseg.ts = current;
                newseg.sn = _sndNxt++; newseg.una = _rcvNxt;
                newseg.resendts = current; newseg.rto = (uint)_rxRto;
                newseg.fastack = 0; newseg.xmit = 0;
                _sndBuf.Add(newseg);
            }

            int resent = _fastResend > 0 ? _fastResend : int.MaxValue;
            uint rtoMin = _nodelay ? 0u : (uint)(_rxRto >> 3);

            for (int i = 0; i < _sndBuf.Count; i++)
            {
                var segment = _sndBuf[i];
                bool needSend = false;
                if (segment.xmit == 0) { segment.xmit++; segment.rto = (uint)_rxRto; segment.resendts = current + segment.rto + rtoMin; needSend = true; }
                else if (ITimeDiff(current, segment.resendts)) { needSend = true; segment.xmit++; _xmit++; if (!_nodelay) segment.rto += IMax(segment.rto, (uint)_rxRto); else segment.rto += (uint)_rxRto / 2; segment.resendts = current + segment.rto; lost = true; }
                else if (segment.fastack >= resent) { if (segment.xmit <= (uint)IKCP_FASTACK_LIMIT) { needSend = true; segment.xmit++; segment.fastack = 0; segment.resendts = current + segment.rto; change = true; } }

                if (needSend)
                {
                    segment.ts = current; segment.wnd = seg.wnd; segment.una = _rcvNxt;
                    int dataLen = (int)(segment.len);
                    if (offset + IKCP_OVERHEAD + dataLen > _mtu) { Output(_buffer, offset); offset = 0; }
                    Encode(segment, _buffer, ref offset);
                    if (dataLen > 0 && segment.data != null)
                    {
                        Buffer.BlockCopy(segment.data, 0, _buffer, offset, dataLen);
                        offset += dataLen;
                    }
                    if (segment.xmit >= _deadLink) _state = 0xDEAD;
                }
                _sndBuf[i] = segment;
            }
            if (offset > 0) { Output(_buffer, offset); offset = 0; }

            if (change)
            {
                uint inflight = _sndNxt - _sndUna;
                _ssthresh = IMax(inflight / 2, (uint)IKCP_THRESH_MIN);
                _cwnd = _ssthresh + (uint)resent;
                _incr = _cwnd * _mss;
            }
            if (lost)
            {
                _ssthresh = IMax(cwnd / 2, (uint)IKCP_THRESH_MIN);
                _cwnd = 1; _incr = _mss;
            }
            if (_cwnd < 1) { _cwnd = 1; _incr = _mss; }
        }

        private static void Encode(Segment seg, byte[] buf, ref int offset)
        {
            Buffer.BlockCopy(BitConverter.GetBytes(seg.conv), 0, buf, offset, 4); offset += 4;
            buf[offset++] = (byte)seg.cmd;
            buf[offset++] = (byte)seg.frg;
            Buffer.BlockCopy(BitConverter.GetBytes((ushort)seg.wnd), 0, buf, offset, 2); offset += 2;
            Buffer.BlockCopy(BitConverter.GetBytes(seg.ts),  0, buf, offset, 4); offset += 4;
            Buffer.BlockCopy(BitConverter.GetBytes(seg.sn),  0, buf, offset, 4); offset += 4;
            Buffer.BlockCopy(BitConverter.GetBytes(seg.una), 0, buf, offset, 4); offset += 4;
            Buffer.BlockCopy(BitConverter.GetBytes(seg.len), 0, buf, offset, 4); offset += 4;
        }

        public void Update(uint current)
        {
            _current = current;
            if (!_updated) { _updated = true; _tsFlush = _current; }
            int slap = (int)(_current - _tsFlush);
            if (slap >= 10000 || slap < -10000) { _tsFlush = _current; slap = 0; }
            if (slap >= 0) { _tsFlush += _interval; if (_current >= _tsFlush) _tsFlush = _current + _interval; Flush(); }
        }

        public uint Check(uint current)
        {
            if (!_updated) return current;
            uint tsFlush = _tsFlush;
            uint tmPacket = 0xFFFFFFFF;
            if ((int)(current - tsFlush) >= 10000 || (int)(current - tsFlush) < -10000)
                tsFlush = current;
            if ((int)(current - tsFlush) >= 0) return current;
            foreach (var seg in _sndBuf)
            {
                int diff = (int)(seg.resendts - current);
                if (diff <= 0) return current;
                if ((uint)diff < tmPacket) tmPacket = (uint)diff;
            }
            uint minimal = IMin(tmPacket, tsFlush - current);
            return current + IMin(minimal, _interval);
        }
    }
}
