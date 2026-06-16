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
/// BATCH-10 Feature A — unit tests for <see cref="StatefulWorkingStateProjection.TryProjectSlot"/>.
///
/// Tests the headless decode seam by:
///   1. Initializing a <see cref="BlueprintBlackboard1024"/> in-memory buffer.
///   2. Attaching a slot for a known cursor-shaped struct.
///   3. Writing a known Cursor value into the payload.
///   4. Calling <c>TryProjectSlot</c> and asserting the decoded Cursor value is correct.
///
/// No ImGui is called — tests verify the projection logic directly.
/// </summary>
public sealed class StatefulWorkingStateProjectionTests
{
    // ── Local test struct (DemoCursorState-shaped) ────────────────────────────
    // A copy so this test has no dependency on Hrot.AI.Behaviors at compile time,
    // and its layout is locked by the [StructLayout] attribute.
    [StructLayout(LayoutKind.Sequential)]
    private struct TestCursorState
    {
        public int Cursor;
    }

    // ── Test 1: Correct decode of a known Cursor value ────────────────────────

    /// <summary>
    /// Allocates a 1024-byte blueprint blackboard, attaches a slot for
    /// <see cref="TestCursorState"/>, writes Cursor=42, and asserts
    /// <c>TryProjectSlot</c> decodes Cursor==42.
    /// </summary>
    [Fact]
    public unsafe void TryProjectSlot_DecodesKnownCursorValue()
    {
        const int SlotKey  = 0x1234ABCD; // arbitrary stable test key
        const int Cursor42 = 42;

        // Allocate an unmanaged 1024-byte buffer (matches BlueprintBlackboard1024.TotalSize).
        byte* memory = stackalloc byte[BlueprintBlackboard1024.TotalSize];
        BlueprintBlackboardPartitions.Initialize(memory, BlueprintBlackboard1024.TotalSize, BlueprintBlackboard1024.MaxSlots);

        int payloadSize = Marshal.SizeOf<TestCursorState>();
        bool attached = BlueprintBlackboardPartitions.TryAttach(
            memory, SlotKey, payloadSize, structureHash: 0, out int payloadOffset);
        Assert.True(attached, "TryAttach must succeed for a fresh 1024-byte buffer");
        Assert.True(payloadOffset > 0, "Payload offset must be positive after attach");

        // Write Cursor=42 into the slot payload.
        ref var ws = ref Unsafe.AsRef<TestCursorState>(memory + payloadOffset);
        ws.Cursor = Cursor42;

        // Build a StatefulSlotInfo with WorkingStateType set.
        var slotInfo = new StatefulSlotInfo(
            SlotKey,
            payloadSize,
            StructureHash: 0,
            WorkingStateType: typeof(TestCursorState),
            NodeLabel: "TestCursor");

        // ── Decode via the seam ───────────────────────────────────────────────
        var result = StatefulWorkingStateProjection.TryProjectSlot(memory, slotInfo, out object? boxed);

        Assert.Equal(StatefulWorkingStateProjection.SlotProjectionResult.Ok, result);
        Assert.NotNull(boxed);
        Assert.IsType<TestCursorState>(boxed);

        var decoded = (TestCursorState)boxed!;
        Assert.Equal(Cursor42, decoded.Cursor);
    }

    // ── Test 2: Returns SlotNotFound when slot is not attached ────────────────

    [Fact]
    public unsafe void TryProjectSlot_ReturnsSlotNotFound_WhenSlotAbsent()
    {
        byte* memory = stackalloc byte[BlueprintBlackboard1024.TotalSize];
        BlueprintBlackboardPartitions.Initialize(memory, BlueprintBlackboard1024.TotalSize, BlueprintBlackboard1024.MaxSlots);

        var slotInfo = new StatefulSlotInfo(
            SlotKey: unchecked((int)0xDEADBEEF),
            PayloadSize: 4,
            StructureHash: 0,
            WorkingStateType: typeof(TestCursorState),
            NodeLabel: "MissingSlot");

        var result = StatefulWorkingStateProjection.TryProjectSlot(memory, slotInfo, out object? boxed);

        Assert.Equal(StatefulWorkingStateProjection.SlotProjectionResult.SlotNotFound, result);
        Assert.Null(boxed);
    }

    // ── Test 3: Returns NoType when WorkingStateType is null ──────────────────

    [Fact]
    public unsafe void TryProjectSlot_ReturnsNoType_WhenWorkingStateTypeIsNull()
    {
        byte* memory = stackalloc byte[BlueprintBlackboard1024.TotalSize];
        BlueprintBlackboardPartitions.Initialize(memory, BlueprintBlackboard1024.TotalSize, BlueprintBlackboard1024.MaxSlots);

        // 3-arg construction (PREREQ back-compat): WorkingStateType stays null.
        var slotInfo = new StatefulSlotInfo(SlotKey: 1, PayloadSize: 4, StructureHash: 0);

        var result = StatefulWorkingStateProjection.TryProjectSlot(memory, slotInfo, out object? boxed);

        Assert.Equal(StatefulWorkingStateProjection.SlotProjectionResult.NoType, result);
        Assert.Null(boxed);
    }

    // ── Test 4: BehaviorDefinition round-trips StatefulWorkingSlots with new fields ──

    [Fact]
    public void BehaviorDefinition_StatefulWorkingSlots_CarriesWorkingStateTypeAndNodeLabel()
    {
        var slots = new[]
        {
            new StatefulSlotInfo(
                SlotKey: 0x1000,
                PayloadSize: 4,
                StructureHash: 0xABCD,
                WorkingStateType: typeof(TestCursorState),
                NodeLabel: "AdvanceCursor"),
        };

        var def = new BehaviorDefinition
        {
            Name = "T20_StatefulDemo",
            BrainTier = BehaviorConstants.BrainTierBTree,
            StatefulWorkingSlots = slots,
        };

        Assert.NotNull(def.StatefulWorkingSlots);
        Assert.Single(def.StatefulWorkingSlots);

        var s = def.StatefulWorkingSlots![0];
        Assert.Equal(0x1000, s.SlotKey);
        Assert.Equal(4, s.PayloadSize);
        Assert.Equal((uint)0xABCD, s.StructureHash);
        Assert.Equal(typeof(TestCursorState), s.WorkingStateType);
        Assert.Equal("AdvanceCursor", s.NodeLabel);
    }

    // ── Test 5: Back-compat — existing 3-arg construction still compiles + null fields ──

    [Fact]
    public void StatefulSlotInfo_ThreeArgConstruction_HasNullOptionalFields()
    {
        var s = new StatefulSlotInfo(SlotKey: 7, PayloadSize: 8, StructureHash: 42u);

        Assert.Equal(7, s.SlotKey);
        Assert.Equal(8, s.PayloadSize);
        Assert.Equal(42u, s.StructureHash);
        Assert.Null(s.WorkingStateType);
        Assert.Null(s.NodeLabel);
    }

    // ── Minimal MockSession (mirrors BrainBlackboardRendererTests) ────────────
    private sealed class MockSession : IInspectableSession
    {
        private readonly bool _hasBehaviorState;
        private readonly int  _behaviorHash;

        public MockSession(bool hasBehaviorState, int behaviorHash = 0)
        {
            _hasBehaviorState = hasBehaviorState;
            _behaviorHash     = behaviorHash;
        }

        public bool IsReadOnly   => true;
        public int  EntityCount  => 1;

        public IEnumerable<Entity> GetEntities() => Array.Empty<Entity>();
        public bool IsAlive(Entity e) => true;
        public IEnumerable<Type> GetAllComponentTypes() => Array.Empty<Type>();

        public bool HasComponent(Entity e, Type t)
            => t == typeof(BehaviorState) && _hasBehaviorState;

        public object? GetComponent(Entity e, Type t)
            => t == typeof(BehaviorState) && _hasBehaviorState
                ? (object)new BehaviorState { ActiveBehaviorHash = _behaviorHash }
                : null;

        public void SetComponent(Entity e, Type t, object v) { }
        public bool HasAuthority(Entity e, Type t) => false;
    }
}
