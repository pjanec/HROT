using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fbt;
using Fbt.Runtime;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Blueprints.Components;
using Fdp.Toolkit.Blueprints.Partitioning;
using Hrot.AI.Behaviors.Brains;
using Xunit;

namespace Fdp.Toolkit.Behavior.Tests;

/// <summary>
/// S2-1 runtime tests: verify that the stateful partition-slot WorkingState adapter
/// persists working state across ticks and that two nodes with the same method but
/// distinct VisualIds occupy independent slots.
///
/// These tests manually reproduce the adapter thunk that BTreeBridgeEmitCore would emit
/// for a ThreeParamReusableStateful binding.
/// </summary>
public sealed unsafe class StatefulPrimitiveTests
{
    // ── World factory helpers ─────────────────────────────────────────────────────

    /// <summary>
    /// Creates a world that includes BlueprintBlackboard1024 (needed for stateful slot tests).
    /// </summary>
    private static EntityRepository CreateWorld()
    {
        var world = TestWorldFactory.Create(); // registers BrainBlackboard etc.
        world.RegisterComponent<BlueprintBlackboard1024>();
        return world;
    }

    // ── FNV-1a slot key computation (must match BTreeBridgeEmitCore.ComputeStatefulSlotKey) ─

    private static int ComputeSlotKey(Guid assetId, Guid nodeVisualId)
    {
        unchecked
        {
            uint hash = 2166136261u;
            foreach (byte b in assetId.ToByteArray())   { hash ^= b; hash *= 16777619u; }
            foreach (byte b in nodeVisualId.ToByteArray()) { hash ^= b; hash *= 16777619u; }
            return (int)(hash & 0x7FFFFFFFu);
        }
    }

    // ── Helper: attach a slot and return its offset ───────────────────────────────

    private static int AttachSlot(ref BlueprintBlackboard1024 tier, int slotKey, int payloadSize)
    {
        fixed (byte* mem = tier.Memory)
        {
            BlueprintBlackboardPartitions.Initialize(mem, BlueprintBlackboard1024.TotalSize, BlueprintBlackboard1024.MaxSlots);
            bool ok = BlueprintBlackboardPartitions.TryAttach(mem, slotKey, payloadSize, 0ul, out int wsOff);
            Assert.True(ok, $"TryAttach must succeed for slotKey={slotKey}, payloadSize={payloadSize}");
            return wsOff;
        }
    }

    // ── Test 1 ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The adapter thunk persists working state across multiple ticks.
    /// Cursor must increment each tick and be readable back from the partition slot.
    /// </summary>
    [Fact]
    public void StatefulPrimitive_WorkingStatePersistsAcrossTicks()
    {
        // --- Arrange ---
        var assetId  = Guid.NewGuid();
        var nodeId   = Guid.NewGuid();
        int slotKey  = ComputeSlotKey(assetId, nodeId);

        var world = CreateWorld();
        var entity = world.CreateEntity();
        world.AddComponent(entity, new BrainBlackboard());
        world.AddComponent(entity, new BlueprintBlackboard1024());
        world.AddComponent(entity, new BehaviorState());

        // Initialize the tier and attach one slot for DemoCursorState.
        const int wsPayloadSize = 4; // DemoCursorState = { int Cursor } = 4 bytes
        {
            ref var tier = ref world.GetComponentRW<BlueprintBlackboard1024>(entity);
            AttachSlot(ref tier, slotKey, wsPayloadSize);
        }

        // Build an action blob with one action node keyed by the stateful thunk key.
        const int paramOffset = 0;
        string thunkKey = $"StatefulTest.Action_AdvanceCursor@{paramOffset}@{slotKey}";

        var actionReg = new ActionRegistry<BrainBlackboard, BTreeContext>();

        // Register the adapter thunk (mirror of what BTreeBridgeEmitCore would emit).
        actionReg.Register(thunkKey,
            (ref BrainBlackboard bb, ref BehaviorTreeState st, ref BTreeContext ctx, int pi) =>
            {
                unsafe
                {
                    // Project Params from BrainBlackboard at offset 0.
                    ref var p = ref Unsafe.As<byte, DemoCounterNodes.DemoCursorParams>(
                        ref Unsafe.AddByteOffset(ref bb.BehaviorParameters[0], (nint)paramOffset));

                    // Dispatch to BlueprintBlackboard1024 (only tier in this test).
                    if (ctx.World.HasComponent<BlueprintBlackboard1024>(ctx.Self))
                    {
                        ref var tier = ref ctx.World.GetComponentRW<BlueprintBlackboard1024>(ctx.Self);
                        fixed (byte* mem = tier.Memory)
                        {
                            if (!BlueprintBlackboardPartitions.TryGetSlotOffset(mem, slotKey, out int wsOff))
                            {
                                System.Diagnostics.Debug.Assert(false, $"S2-1: slot {slotKey} missing");
                                return NodeStatus.Failure;
                            }
                            ref var ws = ref Unsafe.AsRef<DemoCounterNodes.DemoCursorState>(mem + wsOff);
                            return DemoCounterNodes.Action_AdvanceCursor(ref p, ref ws, ref st, ref ctx);
                        }
                    }
                    System.Diagnostics.Debug.Assert(false, "S2-1: no tier component");
                    return NodeStatus.Failure;
                }
            });

        // Build a simple 1-action blob.
        var blob = new BehaviorTreeBlob
        {
            TreeName    = "StatefulTest",
            Nodes       = new[] { new NodeDefinition { Type = NodeType.Action, RawPayloadIndex = 0, SubtreeOffset = 1 } },
            MethodNames = new[] { thunkKey },
            FloatParams = Array.Empty<float>(),
            IntParams   = Array.Empty<int>(),
        };

        var interpreter = new Interpreter<BrainBlackboard, BTreeContext>(blob, actionReg);

        // Set Limit=5 in BrainBlackboard params.
        ref var bb = ref world.GetComponentRW<BrainBlackboard>(entity);
        unsafe
        {
            ref var pParams = ref Unsafe.As<byte, DemoCounterNodes.DemoCursorParams>(
                ref Unsafe.AddByteOffset(ref bb.BehaviorParameters[0], (nint)paramOffset));
            pParams.Limit = 5;
        }

        var ctx = new BTreeContext { Self = entity, World = world };

        // --- Act: tick 3 times ---
        for (int tick = 0; tick < 3; tick++)
        {
            var state = new BehaviorTreeState();
            interpreter.Tick(ref bb, ref state, ref ctx);
        }

        // --- Assert: cursor persisted and advanced to 3 ---
        {
            ref var tier = ref world.GetComponentRW<BlueprintBlackboard1024>(entity);
            fixed (byte* mem = tier.Memory)
            {
                bool found = BlueprintBlackboardPartitions.TryGetSlotOffset(mem, slotKey, out int wsOff);
                Assert.True(found, "Slot must be present after ticks");

                ref var ws = ref Unsafe.AsRef<DemoCounterNodes.DemoCursorState>(mem + wsOff);
                // Cursor was incremented by Action_AdvanceCursor 3 times.
                Assert.Equal(3, ws.Cursor);
            }
        }

        world.Dispose();
    }

    // ── Test 2 ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Two nodes using the same method but different VisualIds produce distinct slot keys
    /// and thus independent working state — advancing node A's cursor does not affect node B.
    /// </summary>
    [Fact]
    public void SameStatefulPrimitive_TwoNodes_IndependentSlots()
    {
        // --- Arrange ---
        var assetId  = Guid.NewGuid();
        var nodeIdA  = Guid.NewGuid();
        var nodeIdB  = Guid.NewGuid();
        int slotKeyA = ComputeSlotKey(assetId, nodeIdA);
        int slotKeyB = ComputeSlotKey(assetId, nodeIdB);

        // Keys must differ (astronomically unlikely to collide with real GUIDs).
        Assert.NotEqual(slotKeyA, slotKeyB);

        var world = CreateWorld();
        var entity = world.CreateEntity();
        world.AddComponent(entity, new BrainBlackboard());
        world.AddComponent(entity, new BlueprintBlackboard1024());
        world.AddComponent(entity, new BehaviorState());

        // Attach two slots.
        const int wsPayloadSize = 4;
        {
            ref var tier = ref world.GetComponentRW<BlueprintBlackboard1024>(entity);
            fixed (byte* mem = tier.Memory)
            {
                BlueprintBlackboardPartitions.Initialize(mem, BlueprintBlackboard1024.TotalSize, BlueprintBlackboard1024.MaxSlots);
                BlueprintBlackboardPartitions.TryAttach(mem, slotKeyA, wsPayloadSize, 0ul, out _);
                BlueprintBlackboardPartitions.TryAttach(mem, slotKeyB, wsPayloadSize, 0ul, out _);
            }
        }

        const int paramOffset = 0;
        string thunkKeyA = $"IndSlotTest.NodeA@{paramOffset}@{slotKeyA}";
        string thunkKeyB = $"IndSlotTest.NodeB@{paramOffset}@{slotKeyB}";

        // Helper: build an adapter for a given slot key.
        Func<int, NodeLogicDelegate<BrainBlackboard, BTreeContext>> makeThunk = (sk) =>
            (ref BrainBlackboard bb, ref BehaviorTreeState st, ref BTreeContext ctx, int pi) =>
            {
                unsafe
                {
                    ref var p = ref Unsafe.As<byte, DemoCounterNodes.DemoCursorParams>(
                        ref Unsafe.AddByteOffset(ref bb.BehaviorParameters[0], (nint)0));
                    if (ctx.World.HasComponent<BlueprintBlackboard1024>(ctx.Self))
                    {
                        ref var tier = ref ctx.World.GetComponentRW<BlueprintBlackboard1024>(ctx.Self);
                        fixed (byte* mem = tier.Memory)
                        {
                            if (!BlueprintBlackboardPartitions.TryGetSlotOffset(mem, sk, out int wsOff))
                                return NodeStatus.Failure;
                            ref var ws = ref Unsafe.AsRef<DemoCounterNodes.DemoCursorState>(mem + wsOff);
                            return DemoCounterNodes.Action_AdvanceCursor(ref p, ref ws, ref st, ref ctx);
                        }
                    }
                    return NodeStatus.Failure;
                }
            };

        // Set Limit=100 so all nodes keep returning Running (cursor < Limit).
        ref var bb = ref world.GetComponentRW<BrainBlackboard>(entity);
        unsafe
        {
            ref var pParams = ref Unsafe.As<byte, DemoCounterNodes.DemoCursorParams>(
                ref Unsafe.AddByteOffset(ref bb.BehaviorParameters[0], (nint)0));
            pParams.Limit = 100;
        }

        var ctx = new BTreeContext { Self = entity, World = world };

        // Build a NodeA-only blob and a NodeB-only blob so we can advance them independently.
        // The Sequence-with-Limit=100 approach cannot run both nodes because NodeA keeps returning
        // Running (cursor < Limit), so Sequence stays at NodeA and never advances to NodeB.
        var regA = new ActionRegistry<BrainBlackboard, BTreeContext>();
        regA.Register(thunkKeyA, makeThunk(slotKeyA));
        var blobA = new BehaviorTreeBlob
        {
            TreeName    = "NodeAOnly",
            Nodes       = new[] { new NodeDefinition { Type = NodeType.Action, RawPayloadIndex = 0, SubtreeOffset = 1 } },
            MethodNames = new[] { thunkKeyA },
            FloatParams = Array.Empty<float>(),
            IntParams   = Array.Empty<int>(),
        };
        var interpA = new Interpreter<BrainBlackboard, BTreeContext>(blobA, regA);

        var regB = new ActionRegistry<BrainBlackboard, BTreeContext>();
        regB.Register(thunkKeyB, makeThunk(slotKeyB));
        var blobB = new BehaviorTreeBlob
        {
            TreeName    = "NodeBOnly",
            Nodes       = new[] { new NodeDefinition { Type = NodeType.Action, RawPayloadIndex = 0, SubtreeOffset = 1 } },
            MethodNames = new[] { thunkKeyB },
            FloatParams = Array.Empty<float>(),
            IntParams   = Array.Empty<int>(),
        };
        var interpB = new Interpreter<BrainBlackboard, BTreeContext>(blobB, regB);

        // --- Act: advance NodeA 4 times, NodeB 2 times ---
        for (int tick = 0; tick < 4; tick++)
        {
            var state = new BehaviorTreeState();
            interpA.Tick(ref bb, ref state, ref ctx);
        }
        for (int tick = 0; tick < 2; tick++)
        {
            var state = new BehaviorTreeState();
            interpB.Tick(ref bb, ref state, ref ctx);
        }

        // --- Assert: NodeA at 4, NodeB at 2 (independent slots, no cross-talk) ---
        {
            ref var tier = ref world.GetComponentRW<BlueprintBlackboard1024>(entity);
            fixed (byte* mem = tier.Memory)
            {
                BlueprintBlackboardPartitions.TryGetSlotOffset(mem, slotKeyA, out int wsOffA);
                BlueprintBlackboardPartitions.TryGetSlotOffset(mem, slotKeyB, out int wsOffB);

                ref var wsA = ref Unsafe.AsRef<DemoCounterNodes.DemoCursorState>(mem + wsOffA);
                ref var wsB = ref Unsafe.AsRef<DemoCounterNodes.DemoCursorState>(mem + wsOffB);

                // NodeA advanced 4 times; NodeB only 2 times — independent slots.
                Assert.Equal(4, wsA.Cursor);
                Assert.Equal(2, wsB.Cursor);
            }
        }

        // --- Act: advance NodeA 2 more times ---
        for (int tick = 0; tick < 2; tick++)
        {
            var state = new BehaviorTreeState();
            interpA.Tick(ref bb, ref state, ref ctx);
        }

        // --- Assert: NodeA at 6, NodeB still at 2 (NodeB's slot untouched by NodeA) ---
        {
            ref var tier = ref world.GetComponentRW<BlueprintBlackboard1024>(entity);
            fixed (byte* mem = tier.Memory)
            {
                BlueprintBlackboardPartitions.TryGetSlotOffset(mem, slotKeyA, out int wsOffA);
                BlueprintBlackboardPartitions.TryGetSlotOffset(mem, slotKeyB, out int wsOffB);

                ref var wsA = ref Unsafe.AsRef<DemoCounterNodes.DemoCursorState>(mem + wsOffA);
                ref var wsB = ref Unsafe.AsRef<DemoCounterNodes.DemoCursorState>(mem + wsOffB);

                Assert.Equal(6, wsA.Cursor); // NodeA advanced 2 more
                Assert.Equal(2, wsB.Cursor); // NodeB unchanged — independent slot, no cross-talk
            }
        }

        world.Dispose();
    }
}
