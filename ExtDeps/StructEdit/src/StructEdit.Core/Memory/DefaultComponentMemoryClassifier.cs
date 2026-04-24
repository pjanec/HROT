using System.Runtime.InteropServices;

namespace StructEdit.Core.Memory;

/// <summary>
/// Default implementation of <see cref="IComponentMemoryClassifier"/>.
/// Uses GCHandle pinning to determine blittability.
/// </summary>
public sealed class DefaultComponentMemoryClassifier : IComponentMemoryClassifier
{
    /// <inheritdoc/>
    public ComponentMemoryKind Classify(Type type)
    {
        if (type is null) throw new ArgumentNullException(nameof(type));

        // Classes and abstract value types (interfaces via IsAbstract+IsValueType) → managed
        if (type.IsClass || (type.IsValueType && type.IsAbstract))
            return ComponentMemoryKind.ManagedReference;

        if (type.IsValueType)
        {
            return IsBlittable(type)
                ? ComponentMemoryKind.UnmanagedBlittableStruct
                : ComponentMemoryKind.NonBlittableStruct;
        }

        return ComponentMemoryKind.Unsupported;
    }

    private static bool IsBlittable(Type type)
    {
        // Use GCHandle pinning approach via the closed-generic cache
        var checkerType = typeof(BlittabilityChecker<>).MakeGenericType(type);
        var field = checkerType.GetField(nameof(BlittabilityChecker<int>.IsBlittable),
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        return (bool)field!.GetValue(null)!;
    }

    // Closed-generic helper so GCHandle.Alloc can work on T[] without extra allocations
    private static class BlittabilityChecker<T>
    {
        // ReSharper disable once StaticMemberInGenericType
        public static readonly bool IsBlittable = CheckBlittable();

        private static bool CheckBlittable()
        {
            // Cannot use GCHandle on types containing managed references or
            // non-blittable value types (e.g. bool on some runtimes, string fields).
            try
            {
                var handle = GCHandle.Alloc(new T[1], GCHandleType.Pinned);
                handle.Free();
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
