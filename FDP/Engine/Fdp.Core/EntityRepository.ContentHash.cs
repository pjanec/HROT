using System;

namespace Fdp.Core
{
    public sealed partial class EntityRepository
    {
        /// <summary>
        /// ⭐ N-11 (CE-4473) — a 64-bit FNV-1a digest of the world's CONTENT: every live entity (index, generation, component mask) and the bytes
        /// of every UNMANAGED component it holds, in entity-index then type-id order. Two worlds that ran the same steps from the same start agree
        /// on it exactly when their unmanaged state is identical — the determinism gate (same hash at 1 / 2 / 4 / 8 threads, across runs).
        /// ⚠ Managed components count by PRESENCE only (their content is objects, not bytes). Not a hot-path call: it reads the whole world.
        /// ⭐ M-7 (CE-4486) — only a component's FIELD bytes count, never its padding (<see cref="SignificantBytes"/>).
        /// </summary>
        public ulong ContentHash() => ContentHash(null);

        /// <summary><see cref="ContentHash()"/>, reporting each row's own digest to <paramref name="perRow"/> (entity index, type id, digest; type id
        /// -1 = the entity's header) — a determinism rail diffs two runs with it to name the first entity and component that differ.</summary>
        public unsafe ulong ContentHash(Action<int, int, ulong>? perRow)
        {
            const ulong Prime = 1099511628211UL;
            ulong h = 14695981039346656037UL;
            void Mix(ulong v) { for (int b = 0; b < 8; b++) { h ^= (byte)(v >> (b * 8)); h *= Prime; } }

            int max = _entityIndex.MaxIssuedIndex;
            var tables = _tableCache;
            for (int i = 0; i <= max; i++)
            {
                var e = new Entity(i, _entityIndex.GetMetadataUnsafe(i).Generation);
                if (!IsAlive(e)) continue;
                Mix((ulong)i); Mix(e.Generation);
                perRow?.Invoke(i, -1, e.Generation);
                ref var mask = ref _entityIndex.GetComponentMask(i);
                for (int id = 0; id < tables.Length && id < 512; id++)
                {
                    if (!mask.IsSet(id)) continue;
                    Mix((ulong)id);
                    if (tables[id] is FlightRecorder.IUnmanagedComponentTable && tables[id] is IComponentTable t)
                    {
                        byte* p = (byte*)GetComponentPointer(e, id);   // a read: the raw row, no version stamp
                        ulong row = 14695981039346656037UL;
                        var significant = SignificantBytes(id, t.ComponentSize);
                        for (int k = 0; k < t.ComponentSize; k++)
                        {
                            if (!significant[k]) continue;   // ⭐ M-7 — padding: whatever the slot held before (history, not state)
                            h ^= p[k]; h *= Prime; row ^= p[k]; row *= Prime;
                        }
                        perRow?.Invoke(i, id, row);
                    }
                }
            }
            return h;
        }

        // ═══ ⭐ M-7 (CE-4486) — which bytes of a component row are FIELD bytes ═══
        // 📐 Measured: two identical stepped 1 000-man runs hashed ScenarioClock differently, the rows differing ONLY at bytes 10–12 — the padding
        //   after its long and its short, holding a leftover of a pointer. A padding byte is not written by a field assignment, so it keeps
        //   whatever the slot held: history, not state. The determinism gate compares STATE.
        // The layout is the MANAGED one, found by writing it (⛔ not Marshal.OffsetOf: that is the marshalled layout — a bool is 4 bytes there):
        //   each leaf field of a zeroed boxed instance is set to all-ones through reflection, and the bytes that change are that field's. A fixed
        //   buffer counts whole. Any layout this cannot read counts every byte (as before). Once per type; not the hot path.
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, bool[]> s_significant = new();

        internal static bool[] SignificantBytes(int typeId, int size)
            => s_significant.GetOrAdd(typeId, id => FieldBytesOf(ComponentTypeRegistry.GetType(id), size));

        /// <summary>⭐ M-7 — the field bytes of <paramref name="type"/>'s managed layout (<paramref name="size"/> bytes); every byte when it cannot be read.</summary>
        internal static bool[] FieldBytesOf(Type? type, int size)
        {
            var mask = new bool[size];
            try
            {
                if (type == null || !type.IsValueType) { Array.Fill(mask, true); return mask; }
                var box = Activator.CreateInstance(type)!;
                MarkFields(type, box, new System.Collections.Generic.List<System.Reflection.FieldInfo>(), mask);
            }
            catch (Exception) { Array.Fill(mask, true); }
            return mask;
        }

        private const System.Reflection.BindingFlags InstanceFields =
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;

        private sealed class RawBox { public byte Data; }   // the layout of a boxed value: the payload starts at the first field

        private static void MarkFields(Type type, object box, System.Collections.Generic.List<System.Reflection.FieldInfo> chain, bool[] mask)
        {
            foreach (var f in type.GetFields(InstanceFields))
            {
                var fixedBuf = (System.Runtime.CompilerServices.FixedBufferAttribute?)Attribute.GetCustomAttribute(f, typeof(System.Runtime.CompilerServices.FixedBufferAttribute));
                if (fixedBuf != null)
                {
                    // its element field marks the buffer's start; the buffer is Length elements of ElementType
                    var element = f.FieldType.GetFields(InstanceFields)[0];
                    chain.Add(f);
                    int start = FirstChanged(box, chain, element, mask.Length);
                    chain.RemoveAt(chain.Count - 1);
                    int n = fixedBuf.Length * SizeOf(fixedBuf.ElementType);
                    for (int k = start; k >= 0 && k < start + n && k < mask.Length; k++) mask[k] = true;
                    continue;
                }
                var ft = f.FieldType;
                if (ft.IsPrimitive || ft.IsEnum || ft.IsPointer || ft == typeof(IntPtr) || ft == typeof(UIntPtr))
                {
                    foreach (int k in Changed(box, chain, f, mask.Length)) mask[k] = true;
                    continue;
                }
                if (!ft.IsValueType) throw new NotSupportedException(ft.FullName);   // a reference in an unmanaged row cannot be: count every byte
                chain.Add(f);
                MarkFields(ft, box, chain, mask);
                chain.RemoveAt(chain.Count - 1);
            }
        }

        // the bytes of the payload that change when the leaf <f> (under <chain>) is set to all-ones
        private static System.Collections.Generic.List<int> Changed(object box, System.Collections.Generic.List<System.Reflection.FieldInfo> chain, System.Reflection.FieldInfo f, int size)
        {
            var before = Payload(box, size);
            Set(box, chain, f, AllOnes(f.FieldType));
            var after = Payload(box, size);
            Set(box, chain, f, Activator.CreateInstance(f.FieldType)!);   // back to zero for the next field
            var changed = new System.Collections.Generic.List<int>();
            for (int k = 0; k < size; k++) if (before[k] != after[k]) changed.Add(k);
            return changed;
        }

        private static int FirstChanged(object box, System.Collections.Generic.List<System.Reflection.FieldInfo> chain, System.Reflection.FieldInfo f, int size)
        {
            var changed = Changed(box, chain, f, size);
            return changed.Count > 0 ? changed[0] : -1;
        }

        private static void Set(object box, System.Collections.Generic.List<System.Reflection.FieldInfo> chain, System.Reflection.FieldInfo f, object value)
        {
            if (chain.Count == 0) { f.SetValue(box, value); return; }
            var tr = TypedReference.MakeTypedReference(box, chain.ToArray());
            f.SetValueDirect(tr, value);
        }

        private static unsafe byte[] Payload(object box, int size)
        {
            var bytes = new byte[size];
            ref byte start = ref System.Runtime.CompilerServices.Unsafe.As<RawBox>(box).Data;
            for (int k = 0; k < size; k++) bytes[k] = System.Runtime.CompilerServices.Unsafe.Add(ref start, k);
            return bytes;
        }

        private static object AllOnes(Type t)
        {
            if (t.IsEnum) return Enum.ToObject(t, AllOnes(Enum.GetUnderlyingType(t)));
            return Type.GetTypeCode(t) switch
            {
                TypeCode.Boolean => true,
                TypeCode.Byte => byte.MaxValue, TypeCode.SByte => (sbyte)-1,
                TypeCode.Int16 => (short)-1, TypeCode.UInt16 => ushort.MaxValue, TypeCode.Char => (char)0xFFFF,
                TypeCode.Int32 => -1, TypeCode.UInt32 => uint.MaxValue,
                TypeCode.Int64 => -1L, TypeCode.UInt64 => ulong.MaxValue,
                TypeCode.Single => BitConverter.Int32BitsToSingle(-1), TypeCode.Double => BitConverter.Int64BitsToDouble(-1L),
                _ => t == typeof(IntPtr) ? (object)new IntPtr(-1) : t == typeof(UIntPtr) ? new UIntPtr(ulong.MaxValue) : throw new NotSupportedException(t.FullName),
            };
        }

        private static int SizeOf(Type t)
            => (int)typeof(System.Runtime.CompilerServices.Unsafe).GetMethod(nameof(System.Runtime.CompilerServices.Unsafe.SizeOf))!.MakeGenericMethod(t).Invoke(null, null)!;
    }
}
