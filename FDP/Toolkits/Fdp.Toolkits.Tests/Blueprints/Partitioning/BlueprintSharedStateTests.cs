using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Blueprints.Components;
using Xunit;

namespace Fdp.Toolkit.Blueprints.Partitioning.Tests;

/// <summary>
/// Slice 2a-1 (design: <c>Blueprint_SharedState_GetShared_Design.md</c> §5): unit tests for
/// <see cref="BlueprintSharedState"/> — the by-value, fail-safe accessor for an ENTITY-scoped
/// shared working-state slot — and for the new 4-arg <see cref="BlueprintBlackboardPartitions.TryGetSlotOffset(byte*, int, out int, out uint)"/>
/// overload it relies on.
///
/// <para>Provisioning mirrors <c>CodeBuiltStatefulActionTests.ProvisionSlots</c>: slots are attached
/// directly via <see cref="BlueprintBlackboardPartitions.TryAttach"/> on the entity's
/// <see cref="BlueprintBlackboard1024"/> tier, keyed with
/// <see cref="StatefulBTreeActionBinder.ComputeStatefulSlotKey"/> at <see cref="StatefulSlotScope.Entity"/>
/// (the same path <c>BehaviorIngressSystem.ProvisionStatefulSlots</c> would use for an Entity-scoped
/// host variable).</para>
/// </summary>
public sealed unsafe class BlueprintSharedStateTests
{
    [StructLayout(LayoutKind.Sequential)]
    private struct TestSharedState
    {
        public int Counter;
        public float Value;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct OtherLayoutState
    {
        public long Big;
        public byte Flag;
    }

    // ── Fixture helpers ──────────────────────────────────────────────────────────

    private static EntityRepository CreateWorld()
    {
        var world = new EntityRepository();
        world.RegisterComponent<BlueprintBlackboard1024>();
        return world;
    }

    /// <summary>Same formula production uses at registration time (mirrors <c>RegisterStatefulThunk</c>'s
    /// <c>structureHash:</c> argument and the emitter's <c>EmitStatefulWorkingSlotsArray</c> expression);
    /// calls the shared public <see cref="StatefulBTreeActionBinder.ComputeTypeNameHash"/> rather than
    /// reimplementing FNV, so this is bit-identical to what <see cref="BlueprintSharedState"/> itself
    /// computes.</summary>
    private static uint ExpectedHash<T>() where T : unmanaged
        => unchecked(StatefulBTreeActionBinder.ComputeTypeNameHash(typeof(T).FullName ?? string.Empty)
                      ^ (uint)Marshal.SizeOf<T>());

    private static int EntityKey(string variableId)
        => StatefulBTreeActionBinder.ComputeStatefulSlotKey(
            Guid.Empty, StatefulSlotScope.Entity, Guid.Empty, variableId);

    /// <summary>Attaches an Entity-scoped slot for <paramref name="variableId"/> with an explicit
    /// (possibly deliberately wrong) StructureHash, mirroring how <c>BehaviorIngressSystem</c>
    /// provisions a manifest slot.</summary>
    private static void ProvisionEntitySlot(
        EntityRepository world, Entity entity, string variableId, int payloadSize, uint structureHash)
    {
        int slotKey = EntityKey(variableId);
        ref var tier = ref world.GetComponentRW<BlueprintBlackboard1024>(entity);
        fixed (byte* mem = tier.Memory)
        {
            BlueprintBlackboardPartitions.Initialize(mem, BlueprintBlackboard1024.TotalSize, BlueprintBlackboard1024.MaxSlots);
            bool ok = BlueprintBlackboardPartitions.TryAttach(mem, slotKey, payloadSize, structureHash, out _);
            Assert.True(ok, $"TryAttach must succeed for variableId={variableId} (slotKey={slotKey})");
        }
    }

    // ── Test 1: round trip ───────────────────────────────────────────────────────

    /// <summary>Provisioned Entity-scoped slot: TrySetShared writes, TryGetShared reads the same
    /// value back — both return true, and the guard does not fire (stored hash matches expected).</summary>
    [Fact]
    public void RoundTrip_ProvisionedSlot_SetThenGet_ReturnsTrueAndValueMatches()
    {
        const string variableId = "roundTripVar";
        var world = CreateWorld();
        var entity = world.CreateEntity();
        world.AddComponent(entity, new BlueprintBlackboard1024());

        ProvisionEntitySlot(world, entity, variableId, Marshal.SizeOf<TestSharedState>(), ExpectedHash<TestSharedState>());

        var written = new TestSharedState { Counter = 42, Value = 3.5f };
        bool setOk = BlueprintSharedState.TrySetShared(world, entity, variableId, in written);
        Assert.True(setOk, "TrySetShared must succeed on a freshly-provisioned, hash-matching slot");

        bool getOk = BlueprintSharedState.TryGetShared<TestSharedState>(world, entity, variableId, out var readBack);
        Assert.True(getOk, "TryGetShared must succeed on a freshly-provisioned, hash-matching slot");
        Assert.Equal(written.Counter, readBack.Counter);
        Assert.Equal(written.Value, readBack.Value);

        world.Dispose();
    }

    // ── Test 2: not provisioned ──────────────────────────────────────────────────

    /// <summary>A tier component is present on the entity, but no slot was ever attached for this
    /// variableId (e.g. the owner hasn't provisioned it yet this frame) — TryGetShared returns
    /// false, never throws, and leaves <c>out value</c> at its default.</summary>
    [Fact]
    public void NotProvisioned_TryGetShared_ReturnsFalse_NoThrow()
    {
        var world = CreateWorld();
        var entity = world.CreateEntity();
        world.AddComponent(entity, new BlueprintBlackboard1024());

        bool ok = BlueprintSharedState.TryGetShared<TestSharedState>(world, entity, "neverProvisioned", out var value);

        Assert.False(ok);
        Assert.Equal(default(TestSharedState).Counter, value.Counter);
        Assert.Equal(default(TestSharedState).Value, value.Value);

        world.Dispose();
    }

    /// <summary>No <c>BlueprintBlackboard*</c> tier component at all on the entity (no-tier case) —
    /// TryGetShared/TrySetShared return false, never throw.</summary>
    [Fact]
    public void NoTierComponent_ReturnsFalse_NoThrow()
    {
        var world = CreateWorld();
        var entity = world.CreateEntity(); // no BlueprintBlackboard1024 added

        bool getOk = BlueprintSharedState.TryGetShared<TestSharedState>(world, entity, "anyVar", out var value);
        Assert.False(getOk);
        Assert.Equal(default, value);

        bool setOk = BlueprintSharedState.TrySetShared(world, entity, "anyVar", new TestSharedState { Counter = 1 });
        Assert.False(setOk);

        world.Dispose();
    }

    // ── Test 3: StructureHash guard (layout drift / collision) ──────────────────

    /// <summary>The slot is attached with a StructureHash that does NOT match the expected hash for
    /// <c>TestSharedState</c> (simulating reader/owner layout drift, or an accidental variableId
    /// collision with an unrelated feature using a differently-shaped type). The guard must treat
    /// this as NOT a match: TryGetShared/TrySetShared return false.
    ///
    /// <para><see cref="Debug.Assert"/> is <c>[Conditional("DEBUG")]</c> — in a Debug build (as this
    /// test assembly is by default) the call executes, and on this runtime a failing
    /// <see cref="Debug.Assert"/> with no custom listener escalates to <c>Environment.FailFast</c>
    /// (process abort), not a catchable exception. To observe the guard firing loudly without
    /// aborting the test host, this test temporarily clears <see cref="Trace.Listeners"/> (restored
    /// in <c>finally</c>) — the assertion below is the RETURN VALUE, which is <c>false</c> regardless
    /// of build configuration or listener setup.</para>
    /// </summary>
    [Fact]
    public void HashMismatch_DifferentStructureHash_ReturnsFalse()
    {
        const string variableId = "driftVar";
        var world = CreateWorld();
        var entity = world.CreateEntity();
        world.AddComponent(entity, new BlueprintBlackboard1024());

        // Deliberately wrong StructureHash: the hash for a completely differently-shaped struct,
        // attached under TestSharedState's own size so PayloadSize alone wouldn't catch the drift.
        uint wrongHash = ExpectedHash<OtherLayoutState>();
        Assert.NotEqual(ExpectedHash<TestSharedState>(), wrongHash);
        ProvisionEntitySlot(world, entity, variableId, Marshal.SizeOf<TestSharedState>(), wrongHash);

        var originalListeners = new TraceListener[Trace.Listeners.Count];
        Trace.Listeners.CopyTo(originalListeners, 0);
        try
        {
            Trace.Listeners.Clear(); // prevent Debug.Assert(false, ...) from FailFast-ing the test host

            bool getOk = BlueprintSharedState.TryGetShared<TestSharedState>(world, entity, variableId, out var value);
            Assert.False(getOk, "TryGetShared must return false when the stored StructureHash mismatches");
            Assert.Equal(default, value);

            bool setOk = BlueprintSharedState.TrySetShared(world, entity, variableId, new TestSharedState { Counter = 99 });
            Assert.False(setOk, "TrySetShared must return false when the stored StructureHash mismatches");
        }
        finally
        {
            Trace.Listeners.Clear();
            Trace.Listeners.AddRange(originalListeners);
        }

        // The mismatched slot's payload must be untouched by the rejected TrySetShared (still zeroed).
        ref var tier = ref world.GetComponentRW<BlueprintBlackboard1024>(entity);
        fixed (byte* mem = tier.Memory)
        {
            Assert.True(BlueprintBlackboardPartitions.TryGetSlotOffset(mem, EntityKey(variableId), out int off));
            Assert.Equal(0, *(int*)(mem + off));
        }

        world.Dispose();
    }

    // ── Test 4: distinct variableIds do not collide ──────────────────────────────

    /// <summary>Two different variableIds provision two independent slots — writing one does not
    /// affect the other, and each reads back its own value.</summary>
    [Fact]
    public void DistinctVariableIds_AreIndependentSlots_NoCollision()
    {
        var world = CreateWorld();
        var entity = world.CreateEntity();
        world.AddComponent(entity, new BlueprintBlackboard1024());

        int keyA = EntityKey("varA");
        int keyB = EntityKey("varB");
        Assert.NotEqual(keyA, keyB);

        ProvisionEntitySlot(world, entity, "varA", Marshal.SizeOf<TestSharedState>(), ExpectedHash<TestSharedState>());
        ProvisionEntitySlot(world, entity, "varB", Marshal.SizeOf<TestSharedState>(), ExpectedHash<TestSharedState>());

        Assert.True(BlueprintSharedState.TrySetShared(world, entity, "varA", new TestSharedState { Counter = 1, Value = 1f }));
        Assert.True(BlueprintSharedState.TrySetShared(world, entity, "varB", new TestSharedState { Counter = 2, Value = 2f }));

        Assert.True(BlueprintSharedState.TryGetShared<TestSharedState>(world, entity, "varA", out var a));
        Assert.True(BlueprintSharedState.TryGetShared<TestSharedState>(world, entity, "varB", out var b));

        Assert.Equal(1, a.Counter);
        Assert.Equal(2, b.Counter);

        world.Dispose();
    }

    // ── Test 5: TryGetSlotOffset 4-arg overload + 3-arg byte-compat ─────────────

    /// <summary>The new 4-arg <see cref="BlueprintBlackboardPartitions.TryGetSlotOffset(byte*, int, out int, out uint)"/>
    /// overload returns the stored StructureHash correctly, and the pre-existing 3-arg overload
    /// (which now delegates to it) still returns the same offset and success result.</summary>
    [Fact]
    public void TryGetSlotOffset_FourArgOverload_ReturnsStoredHash_ThreeArgOverloadUnchanged()
    {
        var world = CreateWorld();
        var entity = world.CreateEntity();
        world.AddComponent(entity, new BlueprintBlackboard1024());

        const string variableId = "hashProbeVar";
        uint hash = ExpectedHash<TestSharedState>();
        ProvisionEntitySlot(world, entity, variableId, Marshal.SizeOf<TestSharedState>(), hash);

        int slotKey = EntityKey(variableId);
        ref var tier = ref world.GetComponentRW<BlueprintBlackboard1024>(entity);
        fixed (byte* mem = tier.Memory)
        {
            bool ok3 = BlueprintBlackboardPartitions.TryGetSlotOffset(mem, slotKey, out int offset3);
            bool ok4 = BlueprintBlackboardPartitions.TryGetSlotOffset(mem, slotKey, out int offset4, out uint storedHash);

            Assert.True(ok3);
            Assert.True(ok4);
            Assert.Equal(offset3, offset4);
            Assert.Equal(hash, storedHash);

            // Nonexistent key: both overloads report failure; 4-arg zeroes its extra out param.
            bool missing3 = BlueprintBlackboardPartitions.TryGetSlotOffset(mem, slotKey + 1, out int missingOffset3);
            bool missing4 = BlueprintBlackboardPartitions.TryGetSlotOffset(mem, slotKey + 1, out int missingOffset4, out uint missingHash);
            Assert.False(missing3);
            Assert.False(missing4);
            Assert.Equal(0, missingOffset3);
            Assert.Equal(0, missingOffset4);
            Assert.Equal(0u, missingHash);
        }

        world.Dispose();
    }
}
