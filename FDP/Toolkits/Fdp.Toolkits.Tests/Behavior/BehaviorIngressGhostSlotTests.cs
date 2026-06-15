using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Fbt;
using Fbt.Runtime;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Behavior.Systems;
using Fdp.Toolkit.Blueprints.Components;
using Fdp.Toolkit.Blueprints.Partitioning;
using Xunit;

namespace Fdp.Toolkit.Behavior.Tests;

/// <summary>
/// S2-3 runtime tests: verify the ghost-slot-safe re-provisioning path in
/// <see cref="BehaviorIngressSystem"/> (BATCH-08 Task 1).
///
/// Tests:
/// <list type="number">
///   <item><c>HardReload_GrowsWorkingState_NoNeighborCorruption</c> — when a behavior is
///         re-assigned with a manifest whose slot grew in PayloadSize, the slot is correctly
///         re-provisioned at the new size and the adjacent slot's bytes are intact.</item>
///   <item><c>HardReload_SameSize_PreservesWorkingState</c> — when the manifest is unchanged
///         (same PayloadSize + StructureHash), the existing slot is left untouched (no detach/
///         reset), preserving working state across a no-op re-assign or soft reload.</item>
/// </list>
/// </summary>
public sealed unsafe class BehaviorIngressGhostSlotTests
{
    // ── Fixture helpers ───────────────────────────────────────────────────────────

    private static EntityRepository CreateWorld()
    {
        var world = TestWorldFactory.Create();
        world.RegisterComponent<BlueprintBlackboard1024>();
        world.RegisterComponent<BlueprintBlackboard4096>();
        world.RegisterComponent<BlueprintBlackboard16384>();
        return world;
    }

    private static (EntityRepository world, BehaviorIngressSystem sys, BehaviorRegistry registry)
        CreateFixture()
    {
        var world    = CreateWorld();
        var registry = new BehaviorRegistry();
        var sys      = new BehaviorIngressSystem(registry);
        return (world, sys, registry);
    }

    private static BehaviorDefinition MakeStatefulDefinition(string name, int id, IReadOnlyList<StatefulSlotInfo> slots)
    {
        var actionReg = new ActionRegistry<BrainBlackboard, BTreeContext>();
        var blob = new BehaviorTreeBlob
        {
            TreeName    = name,
            Nodes       = new[] { new NodeDefinition { Type = NodeType.Action, RawPayloadIndex = 0, SubtreeOffset = 1 } },
            MethodNames = new[] { "noop" },
            FloatParams = Array.Empty<float>(),
            IntParams   = Array.Empty<int>(),
        };
        var interpreter = new Interpreter<BrainBlackboard, BTreeContext>(blob, actionReg);
        return new BehaviorDefinition
        {
            Name                 = name,
            BrainTier            = BehaviorConstants.BrainTierBTree,
            BTreeInterpreter     = interpreter,
            StatefulWorkingSlots = slots,
        };
    }

    private static void FireAssignEvent(
        EntityRepository world, BehaviorIngressSystem sys,
        Entity entity, string behaviorName)
    {
        world.Bus.PublishManaged(new AssignBehaviorEvent
        {
            Entity       = entity,
            BehaviorName = behaviorName,
            JsonParams   = string.Empty,
        });
        world.Bus.SwapBuffers();
        sys.Execute(world, 0.016f);
    }

    // ── Test 1 ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// S2-3 ghost-slot fix: when a hard reload GROWS a WorkingState struct (PayloadSize
    /// increases), the provisioning path must detach the old smaller slot and re-attach at
    /// the manifest size. The ADJACENT slot's bytes must remain intact (no overflow corruption).
    ///
    /// Setup:
    ///   - Assign behavior V1 with [keyA=4, keyB=4].
    ///   - Write sentinel 0xDEADBEEF into keyB's payload.
    /// Simulate hard reload:
    ///   - Re-assign same behavior but with manifest V2: [keyA=32, different StructureHash; keyB=4, same].
    /// Assert:
    ///   (a) keyA's slot now has PayloadSize == 32 (aligned-up).
    ///   (b) keyB's sentinel bytes are intact (no overflow from keyA's growth).
    ///   (c) Both keys still resolve via TryGetSlotOffset.
    ///   (d) SlotCount == 2 (no leaked slots).
    /// </summary>
    [Fact]
    public void HardReload_GrowsWorkingState_NoNeighborCorruption()
    {
        const string BehaviorName = "GhostSlotTestBehavior";
        const int    BehaviorId   = 8301;

        var (world, sys, registry) = CreateFixture();

        var entity = world.CreateEntity();
        world.AddComponent(entity, new BehaviorState());
        world.AddComponent(entity, new BrainBlackboard());
        world.AddComponent(entity, new BrainBTreeState());

        // Choose distinct slot keys.
        int keyA = 0x1AA01;
        int keyB = 0x1BB02;

        const uint hashV1 = 0x11112222u; // arbitrary initial hash for keyA in V1
        const uint hashB  = 0x33334444u; // keyB hash (unchanged across reload)

        // ── V1 manifest: keyA=4 bytes, keyB=4 bytes ──────────────────────────────
        var slotsV1 = new StatefulSlotInfo[]
        {
            new StatefulSlotInfo(keyA, 4,  hashV1),
            new StatefulSlotInfo(keyB, 4,  hashB),
        };
        registry.Register(BehaviorId, BehaviorName, MakeStatefulDefinition(BehaviorName, BehaviorId, slotsV1));

        FireAssignEvent(world, sys, entity, BehaviorName);

        // Verify both slots attached and write sentinel into keyB's payload.
        if (world.HasComponent<BlueprintBlackboard1024>(entity))
        {
            ref var tier = ref world.GetComponentRW<BlueprintBlackboard1024>(entity);
            fixed (byte* mem = tier.Memory)
            {
                Assert.True(BlueprintBlackboardPartitions.TryGetSlotOffset(mem, keyA, out _), "V1: keyA must be attached");
                Assert.True(BlueprintBlackboardPartitions.TryGetSlotOffset(mem, keyB, out int offB), "V1: keyB must be attached");
                *(int*)(mem + offB) = unchecked((int)0xDEADBEEF);
            }
        }
        else if (world.HasComponent<BlueprintBlackboard4096>(entity))
        {
            ref var tier = ref world.GetComponentRW<BlueprintBlackboard4096>(entity);
            fixed (byte* mem = tier.Memory)
            {
                Assert.True(BlueprintBlackboardPartitions.TryGetSlotOffset(mem, keyA, out _), "V1: keyA must be attached");
                Assert.True(BlueprintBlackboardPartitions.TryGetSlotOffset(mem, keyB, out int offB), "V1: keyB must be attached");
                *(int*)(mem + offB) = unchecked((int)0xDEADBEEF);
            }
        }

        // ── Simulate hard reload: update the manifest in the registry ─────────────
        // V2: keyA grows to 32 bytes with a new StructureHash. keyB stays same.
        const uint hashV2A = 0x55556666u; // new hash for keyA after struct grew
        var slotsV2 = new StatefulSlotInfo[]
        {
            new StatefulSlotInfo(keyA, 32, hashV2A),
            new StatefulSlotInfo(keyB, 4,  hashB),
        };

        // Re-register the same behavior ID with updated manifest (simulates the registry
        // being updated by ApplyReload after hot-reload — Task 2 will fire the event).
        registry.Register(BehaviorId, BehaviorName, MakeStatefulDefinition(BehaviorName, BehaviorId, slotsV2));

        // Re-assign the same behavior (same entity, same behavior name) — this is exactly
        // what Task 2 (coordinator re-publish) will trigger.
        FireAssignEvent(world, sys, entity, BehaviorName);

        // ── Assert ────────────────────────────────────────────────────────────────
        // Check whichever tier component the entity has after the re-provision.
        void AssertResults(byte* mem)
        {
            // (a) keyA must now resolve and have the new larger PayloadSize (32, aligned to 32).
            Assert.True(BlueprintBlackboardPartitions.TryGetSlotOffset(mem, keyA, out _),
                "(a) keyA must still resolve after growth");
            ref var header = ref Unsafe.AsRef<BlueprintBlackboardHeader>(mem);
            byte* slotTable = mem + Unsafe.SizeOf<BlueprintBlackboardHeader>();
            ushort keyAPayloadSize = 0;
            for (int i = 0; i < header.SlotCount; i++)
            {
                ref var e = ref Unsafe.AsRef<BlueprintSlotEntry>(
                    slotTable + i * BlueprintBlackboardPartitions.SlotEntrySize);
                if (e.BlueprintId == keyA)
                {
                    keyAPayloadSize = e.PayloadSize;
                    break;
                }
            }
            // 32 bytes is already aligned to 8 → stored PayloadSize must be 32.
            Assert.Equal(32, (int)keyAPayloadSize);

            // (b) keyB's sentinel must be intact (no overflow from keyA's growth).
            Assert.True(BlueprintBlackboardPartitions.TryGetSlotOffset(mem, keyB, out int offB),
                "(b) keyB must still resolve");
            int sentinel = *(int*)(mem + offB);
            Assert.Equal(unchecked((int)0xDEADBEEF), sentinel);

            // (d) SlotCount == 2 (no leaked slots).
            Assert.Equal(2, (int)header.SlotCount);
        }

        if (world.HasComponent<BlueprintBlackboard16384>(entity))
        {
            ref var tier = ref world.GetComponentRW<BlueprintBlackboard16384>(entity);
            fixed (byte* mem = tier.Memory) AssertResults(mem);
        }
        else if (world.HasComponent<BlueprintBlackboard4096>(entity))
        {
            ref var tier = ref world.GetComponentRW<BlueprintBlackboard4096>(entity);
            fixed (byte* mem = tier.Memory) AssertResults(mem);
        }
        else
        {
            ref var tier = ref world.GetComponentRW<BlueprintBlackboard1024>(entity);
            fixed (byte* mem = tier.Memory) AssertResults(mem);
        }

        world.Dispose();
    }

    // ── Test 2 ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// S2-3 idempotent path: re-assigning the same behavior with an UNCHANGED manifest
    /// (same PayloadSize AND StructureHash for every slot) must NOT detach/reset any slot.
    /// Working-state bytes written before the re-assign must survive unchanged.
    ///
    /// This proves the "same size + same hash → idempotent leave-it" branch in
    /// <see cref="BehaviorIngressSystem"/> is taken and the slot is not reset.
    /// </summary>
    [Fact]
    public void HardReload_SameSize_PreservesWorkingState()
    {
        const string BehaviorName = "IdempotentReloadBehavior";
        const int    BehaviorId   = 8302;

        var (world, sys, registry) = CreateFixture();

        var entity = world.CreateEntity();
        world.AddComponent(entity, new BehaviorState());
        world.AddComponent(entity, new BrainBlackboard());
        world.AddComponent(entity, new BrainBTreeState());

        int slotKey = 0x2CC03;
        const uint hashV1 = 0xAAAABBBBu;

        var slotsV1 = new StatefulSlotInfo[]
        {
            new StatefulSlotInfo(slotKey, 8, hashV1),
        };
        registry.Register(BehaviorId, BehaviorName, MakeStatefulDefinition(BehaviorName, BehaviorId, slotsV1));

        // First assign.
        FireAssignEvent(world, sys, entity, BehaviorName);

        // Write working-state bytes and record InstanceVersion.
        const int WorkingStateMagic = 0x12FADE34;
        uint initialInstanceVersion = 0;

        // We know it'll be a 1024 or 4096 tier; check both.
        if (world.HasComponent<BlueprintBlackboard1024>(entity))
        {
            ref var tier = ref world.GetComponentRW<BlueprintBlackboard1024>(entity);
            fixed (byte* mem = tier.Memory)
            {
                Assert.True(BlueprintBlackboardPartitions.TryGetSlotOffset(mem, slotKey, out int off),
                    "Slot must be attached after first assign");
                *(int*)(mem + off) = WorkingStateMagic;

                ref var header = ref Unsafe.AsRef<BlueprintBlackboardHeader>(mem);
                byte* slotTable = mem + Unsafe.SizeOf<BlueprintBlackboardHeader>();
                for (int i = 0; i < header.SlotCount; i++)
                {
                    ref var e = ref Unsafe.AsRef<BlueprintSlotEntry>(
                        slotTable + i * BlueprintBlackboardPartitions.SlotEntrySize);
                    if (e.BlueprintId == slotKey) { initialInstanceVersion = e.InstanceVersion; break; }
                }
            }
        }
        else if (world.HasComponent<BlueprintBlackboard4096>(entity))
        {
            ref var tier = ref world.GetComponentRW<BlueprintBlackboard4096>(entity);
            fixed (byte* mem = tier.Memory)
            {
                Assert.True(BlueprintBlackboardPartitions.TryGetSlotOffset(mem, slotKey, out int off),
                    "Slot must be attached after first assign");
                *(int*)(mem + off) = WorkingStateMagic;

                ref var header = ref Unsafe.AsRef<BlueprintBlackboardHeader>(mem);
                byte* slotTable = mem + Unsafe.SizeOf<BlueprintBlackboardHeader>();
                for (int i = 0; i < header.SlotCount; i++)
                {
                    ref var e = ref Unsafe.AsRef<BlueprintSlotEntry>(
                        slotTable + i * BlueprintBlackboardPartitions.SlotEntrySize);
                    if (e.BlueprintId == slotKey) { initialInstanceVersion = e.InstanceVersion; break; }
                }
            }
        }

        // Re-assign the SAME behavior with the SAME manifest (simulates no-op reload).
        FireAssignEvent(world, sys, entity, BehaviorName);

        // Assert: slot was NOT reset (working-state bytes preserved; InstanceVersion unchanged).
        void AssertPreserved(byte* mem)
        {
            Assert.True(BlueprintBlackboardPartitions.TryGetSlotOffset(mem, slotKey, out int off),
                "Slot must still be attached after idempotent re-assign");

            // Working-state bytes must survive (idempotent path must NOT zero the payload).
            int actual = *(int*)(mem + off);
            Assert.Equal(WorkingStateMagic, actual);

            // InstanceVersion must NOT have been bumped (detach+reattach resets it to 1;
            // idempotent path leaves it at its current value).
            ref var header = ref Unsafe.AsRef<BlueprintBlackboardHeader>(mem);
            byte* slotTable = mem + Unsafe.SizeOf<BlueprintBlackboardHeader>();
            uint currentVersion = 0;
            for (int i = 0; i < header.SlotCount; i++)
            {
                ref var e = ref Unsafe.AsRef<BlueprintSlotEntry>(
                    slotTable + i * BlueprintBlackboardPartitions.SlotEntrySize);
                if (e.BlueprintId == slotKey) { currentVersion = e.InstanceVersion; break; }
            }
            Assert.Equal(initialInstanceVersion, currentVersion);
        }

        if (world.HasComponent<BlueprintBlackboard16384>(entity))
        {
            ref var tier = ref world.GetComponentRW<BlueprintBlackboard16384>(entity);
            fixed (byte* mem = tier.Memory) AssertPreserved(mem);
        }
        else if (world.HasComponent<BlueprintBlackboard4096>(entity))
        {
            ref var tier = ref world.GetComponentRW<BlueprintBlackboard4096>(entity);
            fixed (byte* mem = tier.Memory) AssertPreserved(mem);
        }
        else
        {
            ref var tier = ref world.GetComponentRW<BlueprintBlackboard1024>(entity);
            fixed (byte* mem = tier.Memory) AssertPreserved(mem);
        }

        world.Dispose();
    }
}
