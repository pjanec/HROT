using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Presentation.Abstractions;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Blueprints.Components;
using Fdp.Toolkit.Blueprints.Partitioning;
using Hrot.Presentation.Renderers;
using Xunit;

namespace Hrot.Presentation.Tests.Behavior;

/// <summary>
/// ⭐⭐⭐ <c>P4</c>-③ — rails for <see cref="RootParamsProjection"/>, the inspector's params section.
///
/// <para>🔴 <b>Why these exist and what they replace.</b> <c>BrainBlackboardRendererTests</c> carried
/// six tests, and <b>every one of them stayed green while the feature was broken</b>: they handed the
/// renderer a <c>new BrainBlackboard()</c> and asserted only its REFUSALS, so they could not see that
/// since <c>P3</c> the component is attached-but-never-filled and the panel was drawing zeros
/// (<c>CE-312</c>). ⇒ ⭐ the refusal claims are re-homed here, and <b>a POSITIVE rail is added</b> —
/// the one shape that would have caught it.</para>
///
/// <para>⛔ Two of the six are NOT re-homed, deliberately: <c>GetSummary_ReturnsNonNull</c> and
/// <c>RenderValue_Object_ReturnsFalse</c> asserted the deleted renderer's own summary string and its
/// non-entity-aware fallback. ⚠ Those are claims about a PANEL that no longer exists, not about
/// params; the tier renderer has its own summary and its own fallback, both already covered.</para>
/// </summary>
public sealed class RootParamsProjectionTests
{
    /// <summary>A stand-in params DTO. Layout locked, so the offsets below are the real ones.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct TestParams
    {
        public float Speed;
        public int   Count;
    }

    private const int BehaviorHash = 4242;

    // ── The POSITIVE rail — the one the old suite never had ──────────────────

    /// <summary>
    /// ⭐⭐⭐ <b>The rail that would have caught <c>CE-312</c>.</b> Builds a REAL occurrence store,
    /// attaches the behaviour's root params slot under the key
    /// <see cref="RootParamsAccess.KeyForBehaviour"/> computes, writes known values, and asserts the
    /// projection finds that slot.
    ///
    /// <para>⛔ It asserts the payload READS BACK, not merely that a lookup returned true — zeros are
    /// exactly what the broken path produced, so "it resolved" is not evidence.</para>
    /// </summary>
    [Fact]
    public unsafe void TryResolve_FindsTheRootParamsSlot_AndThePayloadReadsBack()
    {
        byte* memory = stackalloc byte[BlueprintBlackboard1024.TotalSize];
        BlueprintBlackboardPartitions.Initialize(
            memory, BlueprintBlackboard1024.TotalSize, BlueprintBlackboard1024.MaxSlots);

        int key = RootParamsAccess.KeyForBehaviour(BehaviorHash);
        Assert.NotEqual(0, key);

        int payloadSize = Marshal.SizeOf<TestParams>();
        Assert.True(BlueprintBlackboardPartitions.TryAttach(
            memory, key, payloadSize, structureHash: 0, out int attachedOffset));

        ref var written = ref Unsafe.AsRef<TestParams>(memory + attachedOffset);
        written.Speed = 12.5f;
        written.Count = 7;

        var registry = NewRegistry(new BehaviorDefinition
        {
            Name                 = "T_RootParams",
            BrainTier            = BehaviorConstants.BrainTierBTree,
            BlackboardLayoutType = typeof(TestParams),
        });

        bool resolved = RootParamsProjection.TryResolve(
            new MockSession(hasBehaviorState: true, behaviorHash: BehaviorHash),
            new Entity(1, 1), registry, memory, out var def, out int payloadOffset);

        Assert.True(resolved, "the root params slot is attached under this behaviour's key");
        Assert.Same(typeof(TestParams), def!.BlackboardLayoutType);
        Assert.Equal(attachedOffset, payloadOffset);

        // ⭐ The values, not just the offset — a zero-filled region would pass an offset check.
        var decoded = (TestParams)Marshal.PtrToStructure((IntPtr)(memory + payloadOffset), typeof(TestParams))!;
        Assert.Equal(12.5f, decoded.Speed);
        Assert.Equal(7, decoded.Count);
    }

    // ── The re-homed refusal claims ──────────────────────────────────────────

    /// <summary>Re-homed from <c>RenderValue_ReturnsFalse_WhenNoBehaviorState</c>.</summary>
    [Fact]
    public unsafe void TryResolve_ReturnsFalse_WhenNoBehaviorState()
    {
        byte* memory = stackalloc byte[BlueprintBlackboard1024.TotalSize];
        BlueprintBlackboardPartitions.Initialize(
            memory, BlueprintBlackboard1024.TotalSize, BlueprintBlackboard1024.MaxSlots);

        Assert.False(RootParamsProjection.TryResolve(
            new MockSession(hasBehaviorState: false), new Entity(1, 1),
            new BehaviorRegistry(), memory, out _, out _));
    }

    /// <summary>Re-homed from <c>RenderValue_ReturnsFalse_WhenBehaviorNotRegistered</c>.</summary>
    [Fact]
    public unsafe void TryResolve_ReturnsFalse_WhenBehaviorNotRegistered()
    {
        byte* memory = stackalloc byte[BlueprintBlackboard1024.TotalSize];
        BlueprintBlackboardPartitions.Initialize(
            memory, BlueprintBlackboard1024.TotalSize, BlueprintBlackboard1024.MaxSlots);

        Assert.False(RootParamsProjection.TryResolve(
            new MockSession(hasBehaviorState: true, behaviorHash: 999), new Entity(1, 1),
            new BehaviorRegistry(), memory, out _, out _));
    }

    /// <summary>
    /// ⭐⭐ <b>A registered behaviour whose root slot is ABSENT must refuse, not project.</b>
    /// ⛔ This is the <c>CE-312</c> shape stated as an assertion: the store exists, the behaviour is
    /// known, and there is simply no params region — the answer is "render nothing", never
    /// "render offset 0", which is what projecting an unfilled buffer amounted to.
    /// </summary>
    [Fact]
    public unsafe void TryResolve_ReturnsFalse_WhenTheRootSlotIsAbsent()
    {
        byte* memory = stackalloc byte[BlueprintBlackboard1024.TotalSize];
        BlueprintBlackboardPartitions.Initialize(
            memory, BlueprintBlackboard1024.TotalSize, BlueprintBlackboard1024.MaxSlots);

        var registry = NewRegistry(new BehaviorDefinition
        {
            Name                 = "T_NoSlot",
            BrainTier            = BehaviorConstants.BrainTierBTree,
            BlackboardLayoutType = typeof(TestParams),
        });

        Assert.False(RootParamsProjection.TryResolve(
            new MockSession(hasBehaviorState: true, behaviorHash: BehaviorHash), new Entity(1, 1),
            registry, memory, out _, out int payloadOffset));
        Assert.Equal(0, payloadOffset);
    }

    /// <summary>
    /// Re-homed from <c>RenderValue_ReturnsFalse_WhenRegistryNull</c>. The null-registry guard lives
    /// in the render entry point, which returns before touching ImGui — so this is safe to call with
    /// no frame, and it is the whole claim: no registry, no section, no throw.
    /// </summary>
    [Fact]
    public unsafe void RenderRootParams_DrawsNothing_WhenRegistryNull()
    {
        var saved = BlueprintBlackboardRenderers.BehaviorRegistry;
        try
        {
            BlueprintBlackboardRenderers.BehaviorRegistry = null;
            byte* memory = stackalloc byte[BlueprintBlackboard1024.TotalSize];
            BlueprintBlackboardPartitions.Initialize(
                memory, BlueprintBlackboard1024.TotalSize, BlueprintBlackboard1024.MaxSlots);

            RootParamsProjection.RenderRootParams(
                new MockSession(hasBehaviorState: true, behaviorHash: BehaviorHash),
                new Entity(1, 1), memory, out string? path);

            Assert.Null(path);
        }
        finally
        {
            BlueprintBlackboardRenderers.BehaviorRegistry = saved;
        }
    }

    // ── Moved verbatim: never a renderer claim in the first place ────────────

    /// <summary>
    /// Moved from <c>BrainBlackboardRendererTests</c> unchanged. ⚠ It asserts
    /// <see cref="BehaviorDefinition.ManagedBlackboardVariables"/> round-trips and never touched a
    /// renderer — it was only filed there because that renderer was the reader.
    /// </summary>
    [Fact]
    public void BehaviorDefinition_StoreManagedBlackboardVariables_RoundTrips()
    {
        var vars = new[]
        {
            new ManagedBlackboardVariable("counter", typeof(int), 0),
            new ManagedBlackboardVariable("accum",   typeof(int), 8),
        };
        var def = new BehaviorDefinition
        {
            Name = "T10_MultiAction",
            BrainTier = BehaviorConstants.BrainTierBTree,
            ManagedBlackboardVariables = vars,
        };
        Assert.Equal(2, def.ManagedBlackboardVariables!.Count);
        Assert.Equal("counter", def.ManagedBlackboardVariables[0].Name);
        Assert.Equal(0, def.ManagedBlackboardVariables[0].ByteOffset);
        Assert.Equal("accum", def.ManagedBlackboardVariables[1].Name);
        Assert.Equal(8, def.ManagedBlackboardVariables[1].ByteOffset);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static BehaviorRegistry NewRegistry(BehaviorDefinition def)
    {
        var registry = new BehaviorRegistry();
        registry.Register(BehaviorHash, def.Name, def);
        return registry;
    }

    private sealed class MockSession : IInspectableSession
    {
        private readonly bool _hasBehaviorState;
        private readonly int _behaviorHash;
        public MockSession(bool hasBehaviorState, int behaviorHash = 0)
        {
            _hasBehaviorState = hasBehaviorState;
            _behaviorHash     = behaviorHash;
        }
        public bool IsReadOnly => true;
        public int EntityCount => 1;
        public IEnumerable<Entity> GetEntities() => Array.Empty<Entity>();
        public bool IsAlive(Entity e) => true;
        public IEnumerable<Type> GetAllComponentTypes() => Array.Empty<Type>();
        public bool HasComponent(Entity e, Type t) => t == typeof(BehaviorState) && _hasBehaviorState;
        public object? GetComponent(Entity e, Type t)
            => t == typeof(BehaviorState) && _hasBehaviorState
                ? (object)new BehaviorState { ActiveBehaviorHash = _behaviorHash }
                : null;
        public void SetComponent(Entity e, Type t, object v) { }
        public bool HasAuthority(Entity e, Type t) => false;
    }
}
