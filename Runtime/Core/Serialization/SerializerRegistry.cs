using System;
using System.Collections.Generic;

namespace MyNetEngine.Core.Serialization
{
    /// <summary>
    /// 타입별 writer/reader 델리게이트 등록소.
    /// reflection 최소화. source generator 또는 IL post-process로 채워지는 것이 이상적.
    /// 현재는 수동 등록 + primitive 기본 제공.
    /// </summary>
    public static class SerializerRegistry
    {
        public delegate void WriteFn<T>(NetWriter w, in T value);
        public delegate T ReadFn<T>(NetReader r);

        private static class Cache<T>
        {
            public static WriteFn<T> Write;
            public static ReadFn<T> Read;
        }

        public static void Register<T>(WriteFn<T> write, ReadFn<T> read)
        {
            Cache<T>.Write = write;
            Cache<T>.Read = read;
        }

        public static WriteFn<T> GetWriter<T>() => Cache<T>.Write
            ?? throw new InvalidOperationException($"No writer for {typeof(T).Name}. Register one via SerializerRegistry.Register.");

        public static ReadFn<T> GetReader<T>() => Cache<T>.Read
            ?? throw new InvalidOperationException($"No reader for {typeof(T).Name}. Register one via SerializerRegistry.Register.");

        public static bool Has<T>() => Cache<T>.Write != null;

        static SerializerRegistry()
        {
            Register<byte>((NetWriter w, in byte v) => w.WriteByte(v), r => r.ReadByte());
            Register<bool>((NetWriter w, in bool v) => w.WriteBool(v), r => r.ReadBool());
            Register<short>((NetWriter w, in short v) => w.WriteShort(v), r => r.ReadShort());
            Register<ushort>((NetWriter w, in ushort v) => w.WriteUShort(v), r => r.ReadUShort());
            Register<int>((NetWriter w, in int v) => w.WriteVarInt(v), r => r.ReadVarInt());
            Register<uint>((NetWriter w, in uint v) => w.WriteVarUInt(v), r => r.ReadVarUInt());
            Register<long>((NetWriter w, in long v) => w.WriteLong(v), r => r.ReadLong());
            Register<ulong>((NetWriter w, in ulong v) => w.WriteULong(v), r => r.ReadULong());
            Register<float>((NetWriter w, in float v) => w.WriteFloat(v), r => r.ReadFloat());
            Register<double>((NetWriter w, in double v) => w.WriteDouble(v), r => r.ReadDouble());
            Register<string>((NetWriter w, in string v) => w.WriteString(v), r => r.ReadString());
        }
    }
}
