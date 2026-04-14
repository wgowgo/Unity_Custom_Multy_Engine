using System;
using System.Collections.Generic;
using MyNetEngine.Core.Serialization;

namespace MyNetEngine.Messaging
{
    /// <summary>
    /// 수신 측 메시지 디스패치. MessageType -> handler.
    /// </summary>
    public sealed class MessageRouter
    {
        public delegate void Handler(int connectionId, NetReader reader);

        private readonly Dictionary<MessageType, Handler> _handlers = new Dictionary<MessageType, Handler>();

        public void Register(MessageType type, Handler handler)
        {
            _handlers[type] = handler;
        }

        public void Unregister(MessageType type) => _handlers.Remove(type);

        public void Dispatch(int connectionId, ArraySegment<byte> payload)
        {
            if (payload.Count < 1) return;
            var reader = new NetReader(payload.Array, payload.Offset, payload.Count);
            MessageType type = (MessageType)reader.ReadByte();
            if (_handlers.TryGetValue(type, out var h))
            {
                h(connectionId, reader);
            }
        }
    }
}
