using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Blueprints.Components;

namespace Fdp.Toolkit.Blueprints.Partitioning;

/// <summary>
/// Slice 2a-1 (design: <c>Blueprint_SharedState_GetShared_Design.md</c> §5): by-value, fail-safe
/// accessor for an ENTITY-scoped shared working-state slot, reusing the existing partition rail
/// (<see cref="BlueprintBlackboardPartitions"/>) and the existing scope-keyed slot math
/// (<see cref="StatefulBTreeActionBinder.ComputeStatefulSlotKey"/>).
///
/// <para><b>By-value, not ref-returning</b> — <see cref="TryGetShared{T}"/> copies the slot bytes
/// out via <c>out T</c> and <see cref="TrySetShared{T}"/> copies a value in via <c>in T</c>. Neither
/// hands back a pointer/ref into partition memory, so there is no dangling-pointer risk if the
/// entity's tier is later swapped for a larger one (<c>BlueprintMaintenanceSystem</c> tier upgrade)
/// or the slot is detached.</para>
///
/// <para><b>Entity scope only</b> — the slot key is computed with
/// <c>ComputeStatefulSlotKey(Guid.Empty, StatefulSlotScope.Entity, Guid.Empty, variableId)</c>, which
/// (per <see cref="StatefulBTreeActionBinder.ComputeStatefulSlotKey"/>) hashes <paramref name="variableId"/>
/// only — no assetId, no nodeVisualId — so an owner and a member entity agree on the key from the
/// variable name alone, and the slot key does not depend on which behavior/asset is running.</para>
///
/// <para><b>Tier probe</b> mirrors the inline probe in the composed-node stateful thunk
/// (<see cref="StatefulBTreeActionBinder.RegisterStatefulThunk{TBB,TParams,TWorkingState}"/>): tries
/// <c>BlueprintBlackboard16384</c>, then <c>BlueprintBlackboard4096</c>, then <c>BlueprintBlackboard1024</c>
/// on <c>self</c> via <c>HasComponent</c> → <c>GetComponentRW</c> → <c>fixed (byte* mem = tier.Memory)</c>.
/// An entity carries at most one tier at a time, so the first matching tier is authoritative.</para>
///
/// <para><b>StructureHash guard</b> (architect-mandated, mirrors the composed-Params drift guard):
/// the expected hash for <typeparamref name="T"/> is computed the SAME way the manifest does at
/// provisioning time — <c>unchecked(ComputeTypeNameHash(typeof(T).FullName) ^ (uint)Marshal.SizeOf&lt;T&gt;())</c>
/// — by calling <see cref="StatefulBTreeActionBinder.ComputeTypeNameHash"/> directly (not
/// reimplementing FNV), so this is bit-identical to
/// <c>StatefulBTreeActionBinder.RegisterStatefulThunk</c>'s <c>structureHash:</c> argument and to
/// <c>BTreeBridgeEmitCore.ComputeTypeNameHash</c> / <c>EmitStatefulWorkingSlotsArray</c>'s emitted
/// <c>StructureHash</c> expression. If the slot's stored <c>StructureHash</c>
/// (<see cref="BlueprintSlotEntry.StructureHash"/>) doesn't match, this is treated as NOT a match:
/// both accessors return <c>false</c> and fire <see cref="System.Diagnostics.Debug.Assert"/> (loud in
/// debug builds, a safe <c>false</c> in release). This catches reader/owner layout drift (the struct
/// bound to <typeparamref name="T"/> here doesn't match what was provisioned) as well as accidental
/// <paramref name="variableId"/> collisions between unrelated features.</para>
/// </summary>
public static unsafe class BlueprintSharedState
{
    /// <summary>
    /// Reads the ENTITY-scoped shared working-state slot named <paramref name="variableId"/> off
    /// <paramref name="self"/>, copying the value out via <paramref name="value"/>. Returns
    /// <c>false</c> (never throws) when: <paramref name="self"/> has no <c>BlueprintBlackboard*</c>
    /// tier component; the tier has no slot for this <paramref name="variableId"/> (not-ready — e.g.
    /// the owner hasn't provisioned it yet this frame); or the slot's stored <c>StructureHash</c>
    /// doesn't match the expected hash for <typeparamref name="T"/> (layout drift / key collision).
    /// </summary>
    public static bool TryGetShared<T>(EntityRepository world, Entity self, string variableId, out T value)
        where T : unmanaged
    {
        if (world == null) throw new ArgumentNullException(nameof(world));
        if (string.IsNullOrEmpty(variableId)) throw new ArgumentException("variableId is required.", nameof(variableId));

        int slotKey = StatefulBTreeActionBinder.ComputeStatefulSlotKey(
            Guid.Empty, StatefulSlotScope.Entity, Guid.Empty, variableId);
        uint expectedHash = ExpectedStructureHash<T>();

        if (world.HasComponent<BlueprintBlackboard16384>(self))
        {
            ref var tier = ref world.GetComponentRW<BlueprintBlackboard16384>(self);
            fixed (byte* mem = tier.Memory)
                return TryReadFromTier(mem, slotKey, expectedHash, out value);
        }
        if (world.HasComponent<BlueprintBlackboard4096>(self))
        {
            ref var tier = ref world.GetComponentRW<BlueprintBlackboard4096>(self);
            fixed (byte* mem = tier.Memory)
                return TryReadFromTier(mem, slotKey, expectedHash, out value);
        }
        if (world.HasComponent<BlueprintBlackboard1024>(self))
        {
            ref var tier = ref world.GetComponentRW<BlueprintBlackboard1024>(self);
            fixed (byte* mem = tier.Memory)
                return TryReadFromTier(mem, slotKey, expectedHash, out value);
        }

        value = default;
        return false;
    }

    /// <summary>
    /// Writes <paramref name="value"/> (copied in) into the ENTITY-scoped shared working-state slot
    /// named <paramref name="variableId"/> on <paramref name="self"/>. Returns <c>false</c> (never
    /// throws) under the same not-ready / drift conditions as <see cref="TryGetShared{T}"/>; on
    /// <c>false</c> no write occurs.
    /// </summary>
    public static bool TrySetShared<T>(EntityRepository world, Entity self, string variableId, in T value)
        where T : unmanaged
    {
        if (world == null) throw new ArgumentNullException(nameof(world));
        if (string.IsNullOrEmpty(variableId)) throw new ArgumentException("variableId is required.", nameof(variableId));

        int slotKey = StatefulBTreeActionBinder.ComputeStatefulSlotKey(
            Guid.Empty, StatefulSlotScope.Entity, Guid.Empty, variableId);
        uint expectedHash = ExpectedStructureHash<T>();

        if (world.HasComponent<BlueprintBlackboard16384>(self))
        {
            ref var tier = ref world.GetComponentRW<BlueprintBlackboard16384>(self);
            fixed (byte* mem = tier.Memory)
                return TryWriteToTier(mem, slotKey, expectedHash, in value);
        }
        if (world.HasComponent<BlueprintBlackboard4096>(self))
        {
            ref var tier = ref world.GetComponentRW<BlueprintBlackboard4096>(self);
            fixed (byte* mem = tier.Memory)
                return TryWriteToTier(mem, slotKey, expectedHash, in value);
        }
        if (world.HasComponent<BlueprintBlackboard1024>(self))
        {
            ref var tier = ref world.GetComponentRW<BlueprintBlackboard1024>(self);
            fixed (byte* mem = tier.Memory)
                return TryWriteToTier(mem, slotKey, expectedHash, in value);
        }

        return false;
    }

    /// <summary>
    /// Writes a SINGLE field (<paramref name="value"/>, <typeparamref name="TField"/>) into the
    /// ENTITY-scoped shared slot named <paramref name="variableId"/> on <paramref name="self"/>, at byte
    /// <paramref name="fieldOffset"/> within the struct — a true per-field write that touches only that
    /// field's bytes, leaving every other field untouched (multi-pin SetShared: unwired fields are
    /// preserved, never reset). <typeparamref name="TStruct"/> is the whole shared struct, used ONLY to
    /// validate the slot's <c>StructureHash</c> and bound the write — no whole-struct read/write occurs.
    /// Returns <c>false</c> (never throws, no write) under the same not-ready / drift conditions as
    /// <see cref="TrySetShared{T}"/>, or if the field would fall outside the struct (defensive bounds).
    /// Self-only by construction (same as <see cref="TrySetShared{T}"/>; cross-entity write is out of scope).
    /// </summary>
    public static bool TrySetSharedField<TStruct, TField>(
        EntityRepository world, Entity self, string variableId, int fieldOffset, in TField value)
        where TStruct : unmanaged
        where TField : unmanaged
    {
        if (world == null) throw new ArgumentNullException(nameof(world));
        if (string.IsNullOrEmpty(variableId)) throw new ArgumentException("variableId is required.", nameof(variableId));

        // Defensive: the field must lie fully within the struct. Guards a stale baked offset (layout
        // drift with a coincidentally-matching hash) from ever writing past the slot into a neighbour.
        if (fieldOffset < 0 || fieldOffset + Unsafe.SizeOf<TField>() > Marshal.SizeOf<TStruct>())
            return false;

        int slotKey = StatefulBTreeActionBinder.ComputeStatefulSlotKey(
            Guid.Empty, StatefulSlotScope.Entity, Guid.Empty, variableId);
        uint expectedHash = ExpectedStructureHash<TStruct>();

        if (world.HasComponent<BlueprintBlackboard16384>(self))
        {
            ref var tier = ref world.GetComponentRW<BlueprintBlackboard16384>(self);
            fixed (byte* mem = tier.Memory)
                return TryWriteFieldToTier(mem, slotKey, expectedHash, fieldOffset, in value);
        }
        if (world.HasComponent<BlueprintBlackboard4096>(self))
        {
            ref var tier = ref world.GetComponentRW<BlueprintBlackboard4096>(self);
            fixed (byte* mem = tier.Memory)
                return TryWriteFieldToTier(mem, slotKey, expectedHash, fieldOffset, in value);
        }
        if (world.HasComponent<BlueprintBlackboard1024>(self))
        {
            ref var tier = ref world.GetComponentRW<BlueprintBlackboard1024>(self);
            fixed (byte* mem = tier.Memory)
                return TryWriteFieldToTier(mem, slotKey, expectedHash, fieldOffset, in value);
        }

        return false;
    }

    private static bool TryWriteFieldToTier<TField>(
        byte* mem, int slotKey, uint expectedHash, int fieldOffset, in TField value)
        where TField : unmanaged
    {
        if (!BlueprintBlackboardPartitions.TryGetSlotOffset(mem, slotKey, out int offset, out uint storedHash))
            return false;
        if (storedHash != expectedHash)
        {
            System.Diagnostics.Debug.Assert(false,
                $"BlueprintSharedState.TrySetSharedField: StructureHash mismatch for entity-scoped slot " +
                $"{slotKey} (stored=0x{storedHash:X8}, expected=0x{expectedHash:X8}). Reader/owner layout " +
                "drift or a variableId collision.");
            return false;
        }
        // Per-field write: only this field's bytes are touched (offset validated by the caller's bounds check).
        Unsafe.WriteUnaligned(mem + offset + fieldOffset, value);
        return true;
    }

    /// <summary>
    /// Expected <c>StructureHash</c> for <typeparamref name="T"/>, computed IDENTICALLY to
    /// provisioning time: <c>unchecked(ComputeTypeNameHash(typeName) ^ (uint)Marshal.SizeOf&lt;T&gt;())</c>.
    /// Calls <see cref="StatefulBTreeActionBinder.ComputeTypeNameHash"/> directly (the same public
    /// method <c>RegisterStatefulThunk</c> uses to build <c>StatefulSlotInfo.StructureHash</c>) rather
    /// than reimplementing FNV-1a-32, so this is guaranteed bit-identical rather than merely intended
    /// to be.
    /// </summary>
    private static uint ExpectedStructureHash<T>() where T : unmanaged
        => unchecked(StatefulBTreeActionBinder.ComputeTypeNameHash(typeof(T).FullName ?? string.Empty)
                      ^ (uint)Marshal.SizeOf<T>());

    private static bool TryReadFromTier<T>(byte* mem, int slotKey, uint expectedHash, out T value)
        where T : unmanaged
    {
        if (!BlueprintBlackboardPartitions.TryGetSlotOffset(mem, slotKey, out int offset, out uint storedHash))
        {
            value = default;
            return false;
        }

        if (storedHash != expectedHash)
        {
            System.Diagnostics.Debug.Assert(false,
                $"BlueprintSharedState.TryGetShared<{typeof(T).FullName}>: StructureHash mismatch for " +
                $"entity-scoped slot {slotKey} (stored=0x{storedHash:X8}, expected=0x{expectedHash:X8}). " +
                "This indicates reader/owner layout drift (T doesn't match the type provisioned for this " +
                "slot) or an accidental variableId collision between unrelated features.");
            value = default;
            return false;
        }

        // By-value copy out -- no ref/pointer into partition memory escapes this method.
        value = Unsafe.AsRef<T>(mem + offset);
        return true;
    }

    private static bool TryWriteToTier<T>(byte* mem, int slotKey, uint expectedHash, in T value)
        where T : unmanaged
    {
        if (!BlueprintBlackboardPartitions.TryGetSlotOffset(mem, slotKey, out int offset, out uint storedHash))
            return false;

        if (storedHash != expectedHash)
        {
            System.Diagnostics.Debug.Assert(false,
                $"BlueprintSharedState.TrySetShared<{typeof(T).FullName}>: StructureHash mismatch for " +
                $"entity-scoped slot {slotKey} (stored=0x{storedHash:X8}, expected=0x{expectedHash:X8}). " +
                "This indicates reader/owner layout drift (T doesn't match the type provisioned for this " +
                "slot) or an accidental variableId collision between unrelated features.");
            return false;
        }

        // By-value copy in -- the write is a bounded struct-sized copy, no ref escapes.
        Unsafe.AsRef<T>(mem + offset) = value;
        return true;
    }
}
