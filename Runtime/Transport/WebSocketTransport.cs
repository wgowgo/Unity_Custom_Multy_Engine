using System;

namespace MyNetEngine.Transport
{
    /// <summary>
    /// WebSocket transport (WebGL/브라우저 지원용 placeholder).
    /// 실제 구현은 System.Net.WebSockets 또는 NativeWebSocket으로.
    /// </summary>
    public sealed class WebSocketTransport : ITransport
    {
        public bool IsRunning { get; private set; }
        public int MaxPacketSize => 64 * 1024;

        public void StartServer(int port) => throw new NotImplementedException("WebSocketTransport stub.");
        public void StartClient(string host, int port) => throw new NotImplementedException("WebSocketTransport stub.");
        public bool PollEvent(out NetEvent evt) { evt = default; return false; }
        public void Send(int connectionId, ArraySegment<byte> data, DeliveryChannel channel) { }
        public void Disconnect(int connectionId) { }
        public void Stop() { IsRunning = false; }
    }
}
