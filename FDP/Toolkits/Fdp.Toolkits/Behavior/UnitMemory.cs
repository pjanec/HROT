using System;
using System.Runtime.CompilerServices;
using Fdp.Core;
using Fdp.Toolkit.Blueprints.Partitioning;

namespace Fdp.Toolkit.Behavior
{
    /// <summary>
    /// ⭐⭐⭐ <c>CE-3137</c> U-1 (<c>Q87</c>, <c>R-237</c>) — UNIT MEMORY: a <c>[UnitMemory]</c> struct shared by every behaviour of
    /// one unit. One slot of kind <see cref="OccurrenceKind.UnitMemory"/> in the unit's blackboard store, keyed by the struct's
    /// TYPE, created on the first <see cref="Ref{T}"/> / <see cref="Set{T}"/> with <c>new T()</c> (the declared defaults), and
    /// never swept by a behaviour switch.
    ///
    /// <para>⭐ <b>Fully lazy</b> (Q87 C): nothing is declared ahead. The first touch attaches in place — in a block with room,
    /// else in a block the store APPENDS mid-tick (U-0, <c>R-236</c>): nothing moves, so a ref taken here stays valid until the
    /// unit is destroyed. ⛔ A <see cref="Get{T}"/> of a type nobody wrote returns <c>new T()</c> and creates nothing (Q87 E).</para>
    ///
    /// <para>⚠ <b>Layout guard</b> (Q87 B, F): the slot stores <c>TypeNameHash(FullName) ^ size</c>. A changed struct meeting old
    /// bytes (a hot reload, an older recording) is re-initialised to <c>new T()</c>, never reinterpreted. ⛔ A slot of ANOTHER
    /// kind under the key is a key collision and throws.</para>
    ///
    /// <para>Self only (Q87 G): the unit owns it, any of its behaviours may read and write it, last writer in tick order wins
    /// (the brain ticks on the main thread, <c>CgfLogicPack</c> is synchronous).</para>
    /// </summary>
    public static unsafe class UnitMemory
    {
        /// <summary>The slot key of <typeparamref name="T"/> — FNV-1a of its full name, positive.</summary>
        public static int Key<T>() where T : unmanaged => Info<T>.Key;

        /// <summary>The layout guard stored with the slot: <c>TypeNameHash(FullName) ^ sizeof(T)</c>.</summary>
        public static uint StructureHash<T>() where T : unmanaged => Info<T>.Hash;

        /// <summary><see langword="true"/> when the unit already carries <typeparamref name="T"/> with the current layout.</summary>
        public static bool Has<T>(EntityRepository world, Entity unit) where T : unmanaged
            => OccurrenceStoreAccess.TryFindSlotReadOnly(world, unit, Info<T>.Key, out byte* block, out _, out uint hash)
               && IsUnitMemory(block, Info<T>.Key) && hash == Info<T>.Hash;

        /// <summary>
        /// The unit's <typeparamref name="T"/>, or <c>new T()</c> (the declared defaults) when nobody has written it — ⭐ a read
        /// creates nothing. ⚠ A stored value of an older layout also reads as <c>new T()</c> (and is left for the next write to
        /// re-initialise).
        /// </summary>
        public static T Get<T>(EntityRepository world, Entity unit) where T : unmanaged
        {
            if (OccurrenceStoreAccess.TryFindSlotReadOnly(world, unit, Info<T>.Key, out byte* block, out int offset, out uint hash))
            {
                ThrowIfCollision(block, Info<T>.Key, typeof(T));
                if (hash == Info<T>.Hash) return *(T*)(block + offset);
            }
            return new T();
        }

        /// <summary>
        /// ⭐ A ref to the unit's <typeparamref name="T"/>, created on first touch with <c>new T()</c>. Valid until the unit is
        /// destroyed (the store never moves a slot). ⛔ Throws when every block the ladder offers is full — a unit memory
        /// is never silently dropped.
        /// </summary>
        public static ref T Ref<T>(EntityRepository world, Entity unit) where T : unmanaged
        {
            int key = Info<T>.Key;
            if (OccurrenceStoreAccess.TryFindSlot(world, unit, key, out byte* block, out int offset, out uint hash))
            {
                ThrowIfCollision(block, key, typeof(T));
                if (hash == Info<T>.Hash) return ref Unsafe.AsRef<T>(block + offset);

                // A changed layout: the old bytes are not this type's (Q87 F) — start over from the declared defaults.
                OccurrenceStoreAccess.TryDetachSlot(world, unit, key);
            }

            if (!OccurrenceStoreAccess.TryAttachSlot(
                    world, unit, key, sizeof(T), Info<T>.Hash, OccurrenceKind.UnitMemory, out block, out offset))
                throw new InvalidOperationException(
                    $"Unit memory {typeof(T).FullName} ({sizeof(T)} B) cannot be created on {unit}: every block of its " +
                    "blackboard store is full and every tier is already carried (the whole ladder). A unit memory is never " +
                    "dropped silently — reduce the struct or the unit's slot demand.");

            ref T value = ref Unsafe.AsRef<T>(block + offset);
            value = new T();
            return ref value;
        }

        /// <summary>Writes the whole <typeparamref name="T"/> (creating it first if absent).</summary>
        public static void Set<T>(EntityRepository world, Entity unit, in T value) where T : unmanaged
            => Ref<T>(world, unit) = value;

        private static bool IsUnitMemory(byte* block, int key)
            => BlueprintBlackboardPartitions.GetKindOf(block, key) == OccurrenceKind.UnitMemory;

        private static void ThrowIfCollision(byte* block, int key, Type type)
        {
            var kind = BlueprintBlackboardPartitions.GetKindOf(block, key);
            if (kind != OccurrenceKind.UnitMemory)
                throw new InvalidOperationException(
                    $"Unit memory {type.FullName}: its key 0x{key:X8} is held by a slot of kind {kind} — a key collision " +
                    "between a unit memory type name and another occurrence. Rename the struct.");
        }

        private static class Info<T> where T : unmanaged
        {
            public static readonly int Key = (int)(StatefulBTreeActionBinder.ComputeTypeNameHash(Name) & 0x7FFFFFFFu);
            public static readonly uint Hash = unchecked(StatefulBTreeActionBinder.ComputeTypeNameHash(Name) ^ (uint)sizeof(T));
            private static string Name => typeof(T).FullName ?? typeof(T).Name;
        }
    }
}
