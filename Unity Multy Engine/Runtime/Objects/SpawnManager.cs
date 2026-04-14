using System;
using System.Collections.Generic;

namespace MyNetEngine.Objects
{
    /// <summary>
    /// 서버가 netId 할당 + 스폰/디스폰 관리.
    /// prefab hash로 런타임 생성 매핑.
    /// pooling은 PrefabFactory 델리게이트로 주입.
    /// </summary>
    public sealed class SpawnManager
    {
        public delegate NetworkObject PrefabFactory(uint prefabHash, int ownerConn);
        public delegate void PrefabDestroyer(NetworkObject obj);

        private readonly Dictionary<uint, NetworkObject> _objects = new Dictionary<uint, NetworkObject>();
        private readonly Dictionary<uint, PrefabFactory> _factories = new Dictionary<uint, PrefabFactory>();
        private readonly Dictionary<uint, PrefabDestroyer> _destroyers = new Dictionary<uint, PrefabDestroyer>();
        private uint _nextNetId = 1;

        public IReadOnlyDictionary<uint, NetworkObject> AllObjects => _objects;

        public void RegisterPrefab(uint hash, PrefabFactory factory, PrefabDestroyer destroyer = null)
        {
            _factories[hash] = factory;
            if (destroyer != null) _destroyers[hash] = destroyer;
        }

        /// <summary>
        /// 서버측 spawn. 새 netId 할당.
        /// </summary>
        public NetworkObject SpawnServer(uint prefabHash, int ownerConn, AuthorityMode authority)
        {
            if (!_factories.TryGetValue(prefabHash, out var factory))
                throw new InvalidOperationException($"No prefab factory for hash {prefabHash}");
            var obj = factory(prefabHash, ownerConn);
            obj.NetId = _nextNetId++;
            obj.PrefabHash = prefabHash;
            obj.OwnerConnectionId = ownerConn;
            obj.Authority = authority;
            _objects[obj.NetId] = obj;
            foreach (var b in obj.Behaviours)
            {
                b.IsServer = true;
                b.OnNetworkStart();
            }
            return obj;
        }

        /// <summary>
        /// 클라측 spawn. 서버가 할당한 netId 사용.
        /// </summary>
        public NetworkObject SpawnClient(uint prefabHash, uint netId, int ownerConn, AuthorityMode authority, int localConnId)
        {
            if (!_factories.TryGetValue(prefabHash, out var factory)) return null;
            var obj = factory(prefabHash, ownerConn);
            obj.NetId = netId;
            obj.PrefabHash = prefabHash;
            obj.OwnerConnectionId = ownerConn;
            obj.Authority = authority;
            _objects[netId] = obj;
            foreach (var b in obj.Behaviours)
            {
                b.IsClient = true;
                b.IsOwner = ownerConn == localConnId;
                b.OnNetworkStart();
            }
            return obj;
        }

        public bool TryGet(uint netId, out NetworkObject obj) => _objects.TryGetValue(netId, out obj);

        public void Despawn(uint netId)
        {
            if (!_objects.TryGetValue(netId, out var obj)) return;
            foreach (var b in obj.Behaviours) b.OnNetworkStop();
            _objects.Remove(netId);
            if (_destroyers.TryGetValue(obj.PrefabHash, out var destroyer))
                destroyer(obj);
        }

        public void Clear()
        {
            foreach (var kv in _objects)
                foreach (var b in kv.Value.Behaviours) b.OnNetworkStop();
            _objects.Clear();
        }
    }
}
