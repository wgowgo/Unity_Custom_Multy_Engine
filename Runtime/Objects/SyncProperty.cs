using System;

namespace MyNetEngine.Objects
{
    /// <summary>
    /// 필드/프로퍼티를 네트워크 상태로 표시.
    /// source generator 또는 IL post-process가 dirty tracking 코드를 주입하는 것이 이상적.
    /// 현재는 런타임 SyncVar&lt;T&gt; 래퍼로 대체 가능.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public sealed class SyncPropertyAttribute : Attribute
    {
        /// <summary> 0: default (tick 마다), N: N tick에 한번 </summary>
        public int SendRate { get; set; } = 0;

        /// <summary> true면 delta compression 허용, false면 항상 full </summary>
        public bool AllowDelta { get; set; } = true;

        /// <summary> 0..7 우선순위. 클수록 먼저 </summary>
        public byte Priority { get; set; } = 3;
    }

    /// <summary>
    /// 수동 래퍼. Value 세터가 dirty 플래그를 set.
    /// </summary>
    public sealed class SyncVar<T>
    {
        private T _value;
        private bool _dirty;
        public event Action<T, T> OnChanged;

        public T Value
        {
            get => _value;
            set
            {
                if (!EqualityComparer<T>.Default.Equals(_value, value))
                {
                    T old = _value;
                    _value = value;
                    _dirty = true;
                    OnChanged?.Invoke(old, value);
                }
            }
        }

        public bool IsDirty => _dirty;
        public void ClearDirty() => _dirty = false;

        public SyncVar() { }
        public SyncVar(T initial) { _value = initial; }
    }

    internal static class EqualityComparer<T>
    {
        public static System.Collections.Generic.EqualityComparer<T> Default =>
            System.Collections.Generic.EqualityComparer<T>.Default;
    }
}
