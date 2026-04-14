namespace MyNetEngine.Messaging
{
    /// <summary>
    /// 최상위 메시지 종류. 첫 1바이트로 라우팅.
    /// </summary>
    public enum MessageType : byte
    {
        // Core control
        Handshake = 1,
        Ping = 2,
        Pong = 3,
        TimeSync = 4,

        // Object lifecycle
        Spawn = 10,
        Despawn = 11,
        OwnershipChange = 12,

        // Replication
        Snapshot = 20,
        SnapshotAck = 21,

        // RPC / events
        ServerRpc = 30,
        ClientRpc = 31,
        TargetRpc = 32,
        UnreliableRpc = 33,

        // Input
        ClientInput = 40,

        // Scene
        SceneLoad = 50,
        SceneReady = 51,

        // Misc
        Custom = 200
    }
}
