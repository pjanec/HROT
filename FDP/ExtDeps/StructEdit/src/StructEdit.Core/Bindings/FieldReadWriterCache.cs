using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace StructEdit.Core.Bindings;

/// <summary>
/// Per-type read/write helpers for native field spans. One instance per CLR type, cached.
/// </summary>
internal interface IFieldReadWriter
{
    object? Read(Span<byte> span);
    void Write(Span<byte> span, object? value);
}

/// <summary>
/// Closed-generic implementation that uses MemoryMarshal.Read/Write — no reflection per call.
/// </summary>
internal sealed class FieldReadWriter<T> : IFieldReadWriter where T : struct
{
    public static readonly FieldReadWriter<T> Instance = new();
    private FieldReadWriter() { }

    public object? Read(Span<byte> span) => MemoryMarshal.Read<T>(span);

    public void Write(Span<byte> span, object? value)
    {
        var v = (T)value!;
        MemoryMarshal.Write(span, in v);
    }
}

/// <summary>
/// Thread-safe cache of <see cref="IFieldReadWriter"/> instances keyed by CLR type.
/// </summary>
internal static class FieldReadWriterCache
{
    private static readonly ConcurrentDictionary<Type, IFieldReadWriter> _cache = new();

    public static IFieldReadWriter Get(Type t) => _cache.GetOrAdd(t, Build);

    private static IFieldReadWriter Build(Type t)
    {
        var implType = typeof(FieldReadWriter<>).MakeGenericType(t);
        var instanceField = implType.GetField("Instance")!;
        return (IFieldReadWriter)instanceField.GetValue(null)!;
    }
}
