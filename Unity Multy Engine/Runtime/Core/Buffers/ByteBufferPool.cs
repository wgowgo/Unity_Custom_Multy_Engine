using System;
using System.Buffers;
using System.Collections.Concurrent;

namespace MyNetEngine.Core.Buffers
{
    /// <summary>
    /// 전송/직렬화 버퍼 풀. GC 0 지향.
    /// 기본은 ArrayPool 래퍼. 더 공격적인 커스텀 풀로 교체 가능.
    /// </summary>
    public sealed class ByteBufferPool
    {
        public static readonly ByteBufferPool Shared = new ByteBufferPool();

        private readonly ArrayPool<byte> _pool = ArrayPool<byte>.Shared;

        public byte[] Rent(int minLength) => _pool.Rent(minLength);
        public void Return(byte[] buffer, bool clear = false) => _pool.Return(buffer, clear);

        public PooledBuffer RentScoped(int minLength) => new PooledBuffer(this, _pool.Rent(minLength));
    }

    /// <summary>
    /// using으로 안전하게 반납되는 버퍼.
    /// </summary>
    public readonly struct PooledBuffer : IDisposable
    {
        private readonly ByteBufferPool _pool;
        public byte[] Array { get; }

        internal PooledBuffer(ByteBufferPool pool, byte[] arr)
        {
            _pool = pool;
            Array = arr;
        }

        public void Dispose()
        {
            if (Array != null) _pool.Return(Array);
        }
    }

    /// <summary>
    /// 메시지 struct 풀. boxing 피하려면 제너릭으로.
    /// </summary>
    public sealed class ObjectPool<T> where T : class, new()
    {
        private readonly ConcurrentBag<T> _bag = new ConcurrentBag<T>();
        private readonly Func<T> _factory;
        private readonly Action<T> _onReturn;

        public ObjectPool(Func<T> factory = null, Action<T> onReturn = null)
        {
            _factory = factory ?? (() => new T());
            _onReturn = onReturn;
        }

        public T Rent()
        {
            if (_bag.TryTake(out var item)) return item;
            return _factory();
        }

        public void Return(T item)
        {
            if (item == null) return;
            _onReturn?.Invoke(item);
            _bag.Add(item);
        }
    }
}
