using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace Fdp.Core.Layout
{
    /// <summary>
    /// ⭐⭐ <c>CE-2044</c> — the ONE formula for "where do this struct's bytes really sit": its MANAGED size and each field's
    /// MANAGED offset. Compiled into <c>Fdp.Core</c> (behind the public <see cref="TypeLayout"/>) and LINKED, as source, into the
    /// vendored <c>StructEdit.Core</c> and <c>Fbt.Compiler</c>, which cannot reference <c>Fdp.Core</c> (R-48: co-evolved source,
    /// no stable ABI). 📄 <c>DESIGN_Unified_Behaviour_Run.md</c> "S8h".
    /// <para>⛔ Not <c>Marshal.SizeOf</c>/<c>Marshal.OffsetOf</c>, the INTEROP layout: measured, 22 of 1545 shipped struct fields sit
    /// elsewhere in memory (a <c>bool</c> counts 4 there, a <c>char</c> 1).</para>
    /// </summary>
    internal static class ManagedLayout
    {
        private static readonly MethodInfo UnsafeSizeOf = typeof(Unsafe).GetMethod(nameof(Unsafe.SizeOf))!;
        private static readonly ConcurrentDictionary<Type, int> Sizes = new ConcurrentDictionary<Type, int>();
        private static readonly ConcurrentDictionary<(Type, string), int> Offsets = new ConcurrentDictionary<(Type, string), int>();

        /// <summary><c>Unsafe.SizeOf&lt;T&gt;()</c> for <paramref name="type"/>.</summary>
        /// <exception cref="ArgumentException">An open generic, a pointer, a by-ref or <c>void</c>.</exception>
        public static int SizeOf(Type type)
        {
            if (type is null) throw new ArgumentNullException(nameof(type));
            return Sizes.GetOrAdd(type, static t =>
            {
                if (t.ContainsGenericParameters || t.IsPointer || t.IsByRef || t == typeof(void))
                    throw new ArgumentException($"'{t}' has no managed size.", nameof(type));
                return (int)UnsafeSizeOf.MakeGenericMethod(t).Invoke(null, null)!;
            });
        }

        /// <summary>
        /// The byte offset of instance field <paramref name="fieldName"/> inside struct <paramref name="type"/>: the field's address
        /// minus the struct's, taken by IL (<c>ldflda</c>) on a local — exact for any layout.
        /// </summary>
        /// <exception cref="ArgumentException">Not a closed struct, or no such instance field.</exception>
        public static int OffsetOf(Type type, string fieldName)
        {
            if (type is null) throw new ArgumentNullException(nameof(type));
            if (fieldName is null) throw new ArgumentNullException(nameof(fieldName));
            return Offsets.GetOrAdd((type, fieldName), static key =>
            {
                var (t, name) = key;
                if (!t.IsValueType || t.ContainsGenericParameters)
                    throw new ArgumentException($"'{t}' is not a closed struct.", nameof(type));
                var field = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    ?? throw new ArgumentException($"'{t}' has no instance field '{name}'.", nameof(fieldName));
                var dm = new DynamicMethod("OffsetOf", typeof(int), Type.EmptyTypes, typeof(ManagedLayout).Module, skipVisibility: true);
                var il = dm.GetILGenerator();
                var local = il.DeclareLocal(t);
                il.Emit(OpCodes.Ldloca_S, local);
                il.Emit(OpCodes.Ldflda, field);
                il.Emit(OpCodes.Ldloca_S, local);
                il.Emit(OpCodes.Sub);
                il.Emit(OpCodes.Conv_I4);
                il.Emit(OpCodes.Ret);
                return (int)dm.Invoke(null, null)!;
            });
        }
    }
}
