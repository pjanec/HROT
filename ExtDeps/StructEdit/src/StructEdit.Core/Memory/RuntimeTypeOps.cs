using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace StructEdit.Core.Memory;

/// <summary>
/// Closed-generic <see cref="IRuntimeTypeOps"/> implementation for unmanaged structs.
/// </summary>
internal static class RuntimeTypeOps<T> where T : unmanaged
{
    public static readonly IRuntimeTypeOps Instance = new Impl();

    private sealed unsafe class Impl : IRuntimeTypeOps
    {
        public int SizeOf => Unsafe.SizeOf<T>();

        public void CopyObjectToNative(object boxed, void* destination)
            => Unsafe.Write(destination, (T)boxed);

        public object BoxFromNative(void* source)
            => Unsafe.Read<T>(source)!;
    }
}

/// <summary>
/// Thread-safe factory that creates and caches one <see cref="IRuntimeTypeOps"/> per <see cref="Type"/>.
/// Only call for types classified as <see cref="ComponentMemoryKind.UnmanagedBlittableStruct"/>.
/// </summary>
public static class RuntimeTypeOpsFactory
{
    private static readonly ConcurrentDictionary<Type, IRuntimeTypeOps> _cache = new();
    private static readonly Type _openGeneric = typeof(RuntimeTypeOps<>);

    public static IRuntimeTypeOps Get(Type type)
    {
        if (type is null) throw new ArgumentNullException(nameof(type));
        return _cache.GetOrAdd(type, CreateOps);
    }

    private static IRuntimeTypeOps CreateOps(Type type)
    {
        var closedType = _openGeneric.MakeGenericType(type);
        var field = closedType.GetField(
            nameof(RuntimeTypeOps<int>.Instance),
            BindingFlags.Public | BindingFlags.Static)!;
        return (IRuntimeTypeOps)field.GetValue(null)!;
    }
}
