using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Fdp.Core
{
    /// <summary>
    /// ⭐⭐⭐ <b><c>CE-2030</c> — the ONE runtime answer to <i>"how many bytes does this <see cref="Type"/> occupy?"</i></b>:
    /// <c>Unsafe.SizeOf&lt;T&gt;()</c>, the managed layout — the ECS chunk stride (<see cref="ComponentType{T}.Size"/>), the size
    /// a generated struct actually has, and what the build-time <c>RoslynStructLayout</c> computes for the same type
    /// (<c>CE-2027</c>, pinned against this very call). 📄 <c>DESIGN_Unified_Behaviour_Run.md</c> "S8e".
    ///
    /// <para>
    /// 🔴 It was written eight times: two exact copies in <c>Hrot.Diagnostics.Breakpoints</c> (<c>ComponentBytes.SizeOf</c>,
    /// <c>DataBreakpointManager.GetEcsComponentSize</c>), two inline ones (<c>FixedListFormatter</c>,
    /// <c>BlueprintDebugSession</c>), and four GUESSES — <c>DtoDiagnosticMapper</c>/<c>EntityJsonDumper.GetSizeOf</c> and the
    /// editor's <c>BlackboardBinPacker</c> (<c>Marshal.SizeOf</c>: a <c>bool</c> counts 4, an enum throws) and the blueprint
    /// editor's payload-size tables (any struct = 8).
    /// </para>
    ///
    /// <para>⛔ Not <c>Marshal.SizeOf</c> — that is the INTEROP layout, which no blackboard, component or generated struct uses.</para>
    /// </summary>
    public static class TypeLayout
    {
        private static readonly MethodInfo UnsafeSizeOf =
            typeof(Unsafe).GetMethod(nameof(Unsafe.SizeOf))!;

        private static readonly ConcurrentDictionary<Type, int> Cache = new ConcurrentDictionary<Type, int>();

        /// <summary>The managed size of <paramref name="type"/> (a reference type is one reference wide).</summary>
        /// <exception cref="ArgumentException">An open generic, a pointer, a by-ref or <c>void</c> — nothing has that size.</exception>
        public static int SizeOf(Type type)
        {
            if (type is null) throw new ArgumentNullException(nameof(type));
            return Cache.GetOrAdd(type, static t =>
            {
                if (t.ContainsGenericParameters || t.IsPointer || t.IsByRef || t == typeof(void))
                    throw new ArgumentException($"'{t}' has no managed size.", nameof(type));
                return (int)UnsafeSizeOf.MakeGenericMethod(t).Invoke(null, null)!;
            });
        }

        private static readonly MethodInfo ContainsRefsOpen =
            typeof(RuntimeHelpers).GetMethod(nameof(RuntimeHelpers.IsReferenceOrContainsReferences))!;
        private static readonly MethodInfo ReadOpen =
            typeof(TypeLayout).GetMethod(nameof(ReadCore), BindingFlags.NonPublic | BindingFlags.Static)!;
        private static readonly ConcurrentDictionary<Type, bool> RefsCache = new ConcurrentDictionary<Type, bool>();
        private static readonly ConcurrentDictionary<Type, Func<byte[], int, object>> ReaderCache =
            new ConcurrentDictionary<Type, Func<byte[], int, object>>();

        /// <summary>True when <paramref name="type"/> is a reference or holds one — its bytes are not a value to read.</summary>
        public static bool ContainsReferences(Type type)
            => RefsCache.GetOrAdd(type, static t => (bool)ContainsRefsOpen.MakeGenericMethod(t).Invoke(null, null)!);

        /// <summary>
        /// ⭐ <c>CE-2041</c> — the value of type <paramref name="type"/> whose MANAGED bytes start at <paramref name="offset"/>:
        /// <c>Unsafe.ReadUnaligned&lt;T&gt;</c>, the layout every blackboard, component and generated struct actually has.
        /// ⛔ Not <c>Marshal.PtrToStructure</c>, which reads the INTEROP layout (a <c>bool</c> 4 bytes, a <c>char</c> 1) and so
        /// mis-reads any struct where the two differ — measured: 15 of 518 shipped structs, every blueprint <c>Vars</c> with a bool.
        /// </summary>
        /// <exception cref="ArgumentException">A type with references (its bytes are not a value) or no managed size.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The value does not fit in <paramref name="bytes"/> at <paramref name="offset"/>.</exception>
        public static object Read(byte[] bytes, int offset, Type type)
        {
            if (bytes is null) throw new ArgumentNullException(nameof(bytes));
            int size = SizeOf(type);
            if (ContainsReferences(type)) throw new ArgumentException($"'{type}' holds references; its bytes are not a value.", nameof(type));
            if (offset < 0 || offset + size > bytes.Length) throw new ArgumentOutOfRangeException(nameof(offset));
            return ReaderCache.GetOrAdd(type, static t =>
                (Func<byte[], int, object>)ReadOpen.MakeGenericMethod(t).CreateDelegate(typeof(Func<byte[], int, object>)))(bytes, offset);
        }

        private static object ReadCore<T>(byte[] bytes, int offset) => Unsafe.ReadUnaligned<T>(ref bytes[offset])!;

        private static readonly ConcurrentDictionary<(Type, string), int> OffsetCache = new ConcurrentDictionary<(Type, string), int>();

        /// <summary>
        /// ⭐ <c>CE-2043</c> — the MANAGED byte offset of instance field <paramref name="fieldName"/> in struct <paramref name="type"/>:
        /// the address of the field minus the address of the struct, taken by IL (<c>ldflda</c>) on a local — exact for any
        /// layout. ⛔ Not <c>Marshal.OffsetOf</c>, the INTEROP offset: measured, 22 of 1545 shipped struct fields sit elsewhere
        /// in memory (every field after a <c>bool</c> in a struct without <c>[MarshalAs(I1)]</c> — e.g. <c>Fbt.RaycastResult.HitPoint</c>
        /// 4, not 8), so a write at the interop offset lands in the wrong field.
        /// </summary>
        /// <exception cref="ArgumentException">Not a struct, or no such instance field.</exception>
        public static int OffsetOf(Type type, string fieldName)
        {
            if (type is null) throw new ArgumentNullException(nameof(type));
            if (fieldName is null) throw new ArgumentNullException(nameof(fieldName));
            return OffsetCache.GetOrAdd((type, fieldName), static key =>
            {
                var (t, name) = key;
                if (!t.IsValueType || t.ContainsGenericParameters)
                    throw new ArgumentException($"'{t}' is not a closed struct.", nameof(type));
                var field = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    ?? throw new ArgumentException($"'{t}' has no instance field '{name}'.", nameof(fieldName));
                var dm = new System.Reflection.Emit.DynamicMethod("OffsetOf", typeof(int), Type.EmptyTypes, typeof(TypeLayout).Module, skipVisibility: true);
                var il = dm.GetILGenerator();
                var local = il.DeclareLocal(t);
                il.Emit(System.Reflection.Emit.OpCodes.Ldloca_S, local);
                il.Emit(System.Reflection.Emit.OpCodes.Ldflda, field);
                il.Emit(System.Reflection.Emit.OpCodes.Ldloca_S, local);
                il.Emit(System.Reflection.Emit.OpCodes.Sub);
                il.Emit(System.Reflection.Emit.OpCodes.Conv_I4);
                il.Emit(System.Reflection.Emit.OpCodes.Ret);
                return (int)dm.Invoke(null, null)!;
            });
        }

        /// <summary><see cref="SizeOf"/>, or <c>false</c> for a type that has no managed size.</summary>
        public static bool TrySizeOf(Type? type, out int size)
        {
            size = 0;
            if (type is null || type.ContainsGenericParameters || type.IsPointer || type.IsByRef || type == typeof(void))
                return false;
            size = SizeOf(type);
            return true;
        }
    }
}
