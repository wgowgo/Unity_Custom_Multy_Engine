using System;

namespace MyNetEngine.Transport
{
    public enum NetEventType
    {
        Connected,
        Data,
        Disconnected,
        Error
    }

    public enum DeliveryChannel : byte
    {
        UnreliableSequenced = 0,
        ReliableOrdered = 1,
        Unreliable = 2
    }

    public readonly struct NetEvent
    {
        public readonly NetEventType Type;
        public readonly int ConnectionId;
        public readonly ArraySegment<byte> Data;
        public readonly DeliveryChannel Channel;
        public readonly string Error;

        public NetEvent(NetEventType type, int connId, ArraySegment<byte> data = default, DeliveryChannel ch = DeliveryChannel.UnreliableSequenced, string error = null)
        {
            Type = type;
            ConnectionId = connId;
            Data = data;
            Channel = ch;
            Error = error;
        }
    }

    /// <summary>
    /// 모든 transport 구현이 따르는 공통 interface.
    /// Poll-based: 프레임/tick 당 PollEvent 반복 호출.
    /// </summary>
    public interface ITransport
    {
        bool IsRunning { get; }
        int MaxPacketSize { get; }

        void StartServer(int port);
        void StartClient(string host, int port);

        bool PollEvent(out NetEvent evt);

        void Send(int connectionId, ArraySegment<byte> data, DeliveryChannel channel);
        void Disconnect(int connectionId);

        void Stop();
    }
}
