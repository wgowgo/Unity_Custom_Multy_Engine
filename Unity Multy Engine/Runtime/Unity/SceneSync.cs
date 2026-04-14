using System.Collections.Generic;
using MyNetEngine.Core.Serialization;
using MyNetEngine.Messaging;
using MyNetEngine.Transport;

namespace MyNetEngine.Unity
{
    /// <summary>
    /// 씬 단위 sync: 서버가 클라에 SceneLoad 지시 -> 클라가 SceneReady 응답.
    /// 씬 오브젝트(사전 배치)는 별도 레지스트리 필요 (prefab과 구분).
    /// </summary>
    public sealed class SceneSync
    {
        public delegate void SceneReadyHandler(int connectionId, string sceneName);

        private readonly ITransport _transport;
        private readonly Dictionary<int, string> _connScene = new Dictionary<int, string>();

        public event SceneReadyHandler OnSceneReady;

        public SceneSync(ITransport transport, MessageRouter router)
        {
            _transport = transport;
            router.Register(MessageType.SceneReady, (conn, r) =>
            {
                string name = r.ReadString();
                _connScene[conn] = name;
                OnSceneReady?.Invoke(conn, name);
            });
        }

        public void LoadScene(int connectionId, string sceneName)
        {
            var w = new NetWriter(64);
            w.WriteByte((byte)MessageType.SceneLoad);
            w.WriteString(sceneName);
            _transport.Send(connectionId, w.ToSegment(), DeliveryChannel.ReliableOrdered);
        }

        public void NotifyReady(int connectionId, string sceneName)
        {
            var w = new NetWriter(64);
            w.WriteByte((byte)MessageType.SceneReady);
            w.WriteString(sceneName);
            _transport.Send(connectionId, w.ToSegment(), DeliveryChannel.ReliableOrdered);
        }

        public bool TryGetScene(int connId, out string scene) => _connScene.TryGetValue(connId, out scene);
    }
}
