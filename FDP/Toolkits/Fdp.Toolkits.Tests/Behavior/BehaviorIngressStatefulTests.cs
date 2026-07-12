using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
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
/// S2-2 runtime tests: verify that BehaviorIngressSystem synchronously provisions
/// BlueprintBlackboard* tier components and stateful working-state slots before the
/// first BTree tick of the same frame.
/// </summary>
public sealed unsafe class BehaviorIngressStatefulTests
{
    // ── World factory ────────────────────────────────────────────────────────────

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

    // ── Helper: build a BehaviorDefinition with a StatefulWorkingSlots manifest ─

    /// <summary>
    /// Builds a minimal BehaviorDefinition for BTree with StatefulWorkingSlots populated.
    /// Each slot uses 4 bytes (one int — DemoCursorState size).
    /// </summary>
    private static BehaviorDefinition MakeStatefulDefinition(
        string name, int id, IReadOnlyList<StatefulSlotInfo> slots)
    {
        // Build a trivial interpreter (no-op) just so BrainTier is BTree.
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
            Name              = name,
            BrainTier         = BehaviorConstants.BrainTierBTree,
            BTreeInterpreter  = interpreter,
            StatefulWorkingSlots = slots,
        };
    }

    // ── FNV-1a slot key (mirror of BTreeBridgeEmitCore.ComputeStatefulSlotKey) ──

    private static int MakeSlotKey(Guid assetId, Guid nodeId)
    {
        unchecked
        {
            uint h = 2166136261u;
            foreach (byte b in assetId.ToByteArray())   { h ^= b; h *= 16777619u; }
            foreach (byte b in nodeId.ToByteArray())    { h ^= b; h *= 16777619u; }
            return (int)(h & 0x7FFFFFFFu);
        }
    }

    // ── Test 1 ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// S2-2: an entity pre-carrying <see cref="BlueprintBlackboard1024"/> (with an existing
    /// slot occupying most of its payload) assigned a behavior whose manifest needs more space
    /// than the 1024 tier can provide must have:
    ///   (a) the larger tier present after the Input phase (before Simulation tick), and
    ///   (b) the old slot's bytes survived the copy (CopyToLargerTier correctness), and
    ///   (c) all new slots attached (TryGetSlotOffset returns true for each).
    /// </summary>
    [Fact]
    public void Assign_UpgradesTierSynchronously_BeforeFirstTick()
    {
        var (world, sys, registry) = CreateFixture();

        // Pre-condition: entity with BlueprintBlackboard1024 carrying an existing slot.
        var entity = world.CreateEntity();
        world.AddComponent(entity, new BehaviorState());
        world.AddComponent(entity, new BrainBlackboard());
        world.AddComponent(entity, new BrainBTreeState());
        world.AddComponent(entity, new BlueprintBlackboard1024());

        // Fill most of the 1024 tier's payload with an existing slot (900 bytes).
        // PayloadSize for 1024 = 928 bytes — a 900-byte slot leaves only 28 bytes free.
        const int existingSlotKey     = 0x1CAFE001;
        const int existingPayloadSize = 900; // nearly fills the 928-byte payload

        {
            ref var tier = ref world.GetComponentRW<BlueprintBlackboard1024>(entity);
            fixed (byte* mem = tier.Memory)
            {
                BlueprintBlackboardPartitions.Initialize(mem, BlueprintBlackboard1024.TotalSize, BlueprintBlackboard1024.MaxSlots);
                bool ok = BlueprintBlackboardPartitions.TryAttach(mem, existingSlotKey, existingPayloadSize, 0xDEADBEEFul, out int slotOff);
                Assert.True(ok, "Pre-existing slot must attach successfully");

                // Write a sentinel value into the existing slot's payload.
                *(int*)(mem + slotOff) = 0x12345678;
            }
        }

        // Build a manifest with 3 slots of 4 bytes each.
        // 3 × (8-aligned + 16 B entry) = 3 × 24 = 72 bytes > remaining 28 bytes → needs 4096.
        var assetId = Guid.NewGuid();
        var slot1Key = MakeSlotKey(assetId, Guid.NewGuid());
        var slot2Key = MakeSlotKey(assetId, Guid.NewGuid());
        var slot3Key = MakeSlotKey(assetId, Guid.NewGuid());

        var slots = new StatefulSlotInfo[]
        {
            new StatefulSlotInfo(slot1Key, 4, 0u),
            new StatefulSlotInfo(slot2Key, 4, 0u),
            new StatefulSlotInfo(slot3Key, 4, 0u),
        };

        const string behaviorName = "UpgradeTierBehavior";
        const int BehaviorId = 8101;
        registry.Register(BehaviorId, behaviorName, MakeStatefulDefinition(behaviorName, BehaviorId, slots));

        // Fire the assign event.
        world.Bus.PublishManaged(new AssignBehaviorEvent
        {
            Entity       = entity,
            BehaviorName = behaviorName,
            JsonParams   = string.Empty,
        });
        world.Bus.SwapBuffers();
        sys.Execute(world, 0.016f);

        // (a) Entity must now carry BlueprintBlackboard4096 (upgraded from 1024).
        Assert.True(world.HasComponent<BlueprintBlackboard4096>(entity),
            "Entity must have BlueprintBlackboard4096 after upgrade");
        Assert.False(world.HasComponent<BlueprintBlackboard1024>(entity),
            "BlueprintBlackboard1024 must be removed after upgrade");

        // (b) Existing slot's bytes must have survived the CopyToLargerTier.
        {
            ref var tier = ref world.GetComponentRW<BlueprintBlackboard4096>(entity);
            fixed (byte* mem = tier.Memory)
            {
                bool found = BlueprintBlackboardPartitions.TryGetSlotOffset(mem, existingSlotKey, out int slotOff);
                Assert.True(found, "Pre-existing slot must survive tier upgrade");
                int sentinel = *(int*)(mem + slotOff);
                Assert.Equal(0x12345678, sentinel);
            }
        }

        // (c) All new manifest slots must be attached.
        {
            ref var tier = ref world.GetComponentRW<BlueprintBlackboard4096>(entity);
            fixed (byte* mem = tier.Memory)
            {
                Assert.True(BlueprintBlackboardPartitions.TryGetSlotOffset(mem, slot1Key, out _),
                    "slot1 must be attached");
                Assert.True(BlueprintBlackboardPartitions.TryGetSlotOffset(mem, slot2Key, out _),
                    "slot2 must be attached");
                Assert.True(BlueprintBlackboardPartitions.TryGetSlotOffset(mem, slot3Key, out _),
                    "slot3 must be attached");
            }
        }

        world.Dispose();
    }

    // ── Test 2 ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// S2-2: provisioned/attached slot count equals the number of distinct stateful node
    /// instances in the manifest (not the executed subset). Build a manifest with ≥3 slots;
    /// assert all 3 are attached after assignment.
    /// </summary>
    [Fact]
    public void Assign_ProvisionsWorstCaseReachableStatefulNodes()
    {
        var (world, sys, registry) = CreateFixture();

        var entity = world.CreateEntity();
        world.AddComponent(entity, new BehaviorState());
        world.AddComponent(entity, new BrainBlackboard());
        world.AddComponent(entity, new BrainBTreeState());

        // Build a manifest with 3 distinct slots.
        var assetId = Guid.NewGuid();
        int keyA = MakeSlotKey(assetId, Guid.NewGuid());
        int keyB = MakeSlotKey(assetId, Guid.NewGuid());
        int keyC = MakeSlotKey(assetId, Guid.NewGuid());

        var slots = new StatefulSlotInfo[]
        {
            new StatefulSlotInfo(keyA, 4, 0u),
            new StatefulSlotInfo(keyB, 4, 0u),
            new StatefulSlotInfo(keyC, 4, 0u),
        };

        const string behaviorName = "ThreeSlotBehavior";
        const int BehaviorId = 8102;
        registry.Register(BehaviorId, behaviorName, MakeStatefulDefinition(behaviorName, BehaviorId, slots));

        world.Bus.PublishManaged(new AssignBehaviorEvent
        {
            Entity       = entity,
            BehaviorName = behaviorName,
            JsonParams   = string.Empty,
        });
        world.Bus.SwapBuffers();
        sys.Execute(world, 0.016f);

        // The entity must now carry a tier component.
        bool hasTier = world.HasComponent<BlueprintBlackboard1024>(entity)
                    || world.HasComponent<BlueprintBlackboard4096>(entity)
                    || world.HasComponent<BlueprintBlackboard16384>(entity);
        Assert.True(hasTier, "Entity must carry a BlueprintBlackboard* tier after assignment");

        // ALL 3 slots must be attached — including slots that may not execute this tick.
        void AssertAllSlotsAttached()
        {
            if (world.HasComponent<BlueprintBlackboard16384>(entity))
            {
                ref var t = ref world.GetComponentRW<BlueprintBlackboard16384>(entity);
                fixed (byte* mem = t.Memory)
                {
                    Assert.True(BlueprintBlackboardPartitions.TryGetSlotOffset(mem, keyA, out _), "keyA missing (16384)");
                    Assert.True(BlueprintBlackboardPartitions.TryGetSlotOffset(mem, keyB, out _), "keyB missing (16384)");
                    Assert.True(BlueprintBlackboardPartitions.TryGetSlotOffset(mem, keyC, out _), "keyC missing (16384)");
                    int slotCount = BlueprintBlackboardPartitions.GetSlotCount(mem);
                    Assert.Equal(3, slotCount);
                }
                return;
            }
            if (world.HasComponent<BlueprintBlackboard4096>(entity))
            {
                ref var t = ref world.GetComponentRW<BlueprintBlackboard4096>(entity);
                fixed (byte* mem = t.Memory)
                {
                    Assert.True(BlueprintBlackboardPartitions.TryGetSlotOffset(mem, keyA, out _), "keyA missing (4096)");
                    Assert.True(BlueprintBlackboardPartitions.TryGetSlotOffset(mem, keyB, out _), "keyB missing (4096)");
                    Assert.True(BlueprintBlackboardPartitions.TryGetSlotOffset(mem, keyC, out _), "keyC missing (4096)");
                    int slotCount = BlueprintBlackboardPartitions.GetSlotCount(mem);
                    Assert.Equal(3, slotCount);
                }
                return;
            }
            if (world.HasComponent<BlueprintBlackboard1024>(entity))
            {
                ref var t = ref world.GetComponentRW<BlueprintBlackboard1024>(entity);
                fixed (byte* mem = t.Memory)
                {
                    Assert.True(BlueprintBlackboardPartitions.TryGetSlotOffset(mem, keyA, out _), "keyA missing (1024)");
                    Assert.True(BlueprintBlackboardPartitions.TryGetSlotOffset(mem, keyB, out _), "keyB missing (1024)");
                    Assert.True(BlueprintBlackboardPartitions.TryGetSlotOffset(mem, keyC, out _), "keyC missing (1024)");
                    int slotCount = BlueprintBlackboardPartitions.GetSlotCount(mem);
                    Assert.Equal(3, slotCount);
                }
                return;
            }
            Assert.Fail("No tier component found");
        }

        AssertAllSlotsAttached();

        world.Dispose();
    }

    // ── S3-5: ClearBehaviorEvent detach ───────────────────────────────────────────

    /// <summary>Reads the entity's active-tier slot count (whichever tier it carries).</summary>
    private static int SlotCountOf(EntityRepository world, Entity entity)
    {
        if (world.HasComponent<BlueprintBlackboard16384>(entity))
        { ref var t = ref world.GetComponentRW<BlueprintBlackboard16384>(entity); fixed (byte* m = t.Memory) return BlueprintBlackboardPartitions.GetSlotCount(m); }
        if (world.HasComponent<BlueprintBlackboard4096>(entity))
        { ref var t = ref world.GetComponentRW<BlueprintBlackboard4096>(entity); fixed (byte* m = t.Memory) return BlueprintBlackboardPartitions.GetSlotCount(m); }
        if (world.HasComponent<BlueprintBlackboard1024>(entity))
        { ref var t = ref world.GetComponentRW<BlueprintBlackboard1024>(entity); fixed (byte* m = t.Memory) return BlueprintBlackboardPartitions.GetSlotCount(m); }
        return 0;
    }

    private static bool HasSlot(EntityRepository world, Entity entity, int key)
    {
        if (world.HasComponent<BlueprintBlackboard16384>(entity))
        { ref var t = ref world.GetComponentRW<BlueprintBlackboard16384>(entity); fixed (byte* m = t.Memory) return BlueprintBlackboardPartitions.TryGetSlotOffset(m, key, out _); }
        if (world.HasComponent<BlueprintBlackboard4096>(entity))
        { ref var t = ref world.GetComponentRW<BlueprintBlackboard4096>(entity); fixed (byte* m = t.Memory) return BlueprintBlackboardPartitions.TryGetSlotOffset(m, key, out _); }
        if (world.HasComponent<BlueprintBlackboard1024>(entity))
        { ref var t = ref world.GetComponentRW<BlueprintBlackboard1024>(entity); fixed (byte* m = t.Memory) return BlueprintBlackboardPartitions.TryGetSlotOffset(m, key, out _); }
        return false;
    }

    private static void Assign(EntityRepository world, BehaviorIngressSystem sys, Entity entity, string name)
    {
        world.Bus.PublishManaged(new AssignBehaviorEvent { Entity = entity, BehaviorName = name, JsonParams = string.Empty });
        world.Bus.SwapBuffers();
        sys.Execute(world, 0.016f);
    }

    /// <summary>
    /// S3-5: assigning a stateful behavior then publishing ClearBehaviorEvent must detach the
    /// stateful slots (no leak). The free space is reclaimed, so a subsequent re-assign of the
    /// same behavior re-provisions the same slot count in the same tier.
    /// </summary>
    [Fact]
    public void Clear_DetachesStatefulSlots_NoLeak()
    {
        var (world, sys, registry) = CreateFixture();

        var entity = world.CreateEntity();
        world.AddComponent(entity, new BehaviorState());
        world.AddComponent(entity, new BrainBlackboard());
        world.AddComponent(entity, new BrainBTreeState());

        var assetId = Guid.NewGuid();
        int k1 = MakeSlotKey(assetId, Guid.NewGuid());
        int k2 = MakeSlotKey(assetId, Guid.NewGuid());
        var slots = new StatefulSlotInfo[] { new StatefulSlotInfo(k1, 4, 0u), new StatefulSlotInfo(k2, 4, 0u) };

        const string name = "ClearNoLeakBehavior";
        const int BehaviorId = 8201;
        registry.Register(BehaviorId, name, MakeStatefulDefinition(name, BehaviorId, slots));

        Assign(world, sys, entity, name);
        Assert.Equal(2, SlotCountOf(world, entity));
        Assert.True(HasSlot(world, entity, k1) && HasSlot(world, entity, k2), "both slots provisioned after assign");

        // Clear: must detach the stateful slots.
        // ClearBehaviorEvent is an unmanaged struct → Publish (not PublishManaged, which is the
        // managed channel used by AssignBehaviorEvent's string payload).
        world.Bus.Publish(new ClearBehaviorEvent { Entity = entity });
        world.Bus.SwapBuffers();
        sys.Execute(world, 0.016f);

        Assert.Equal(0, SlotCountOf(world, entity));
        Assert.False(HasSlot(world, entity, k1), "slot k1 must be detached after clear (no leak)");
        Assert.False(HasSlot(world, entity, k2), "slot k2 must be detached after clear (no leak)");

        // Reclaimed space is reusable: re-assign re-provisions the same slots.
        Assign(world, sys, entity, name);
        Assert.Equal(2, SlotCountOf(world, entity));
        Assert.True(HasSlot(world, entity, k1) && HasSlot(world, entity, k2), "re-assign reuses the reclaimed space");

        world.Dispose();
    }

    /// <summary>
    /// S3-5 regression: the existing switch path (Assign B over A) still detaches A's slots and
    /// provisions B's — unchanged by the clear-path fix.
    /// </summary>
    [Fact]
    public void Switch_StillDetachesPreviousSlots()
    {
        var (world, sys, registry) = CreateFixture();

        var entity = world.CreateEntity();
        world.AddComponent(entity, new BehaviorState());
        world.AddComponent(entity, new BrainBlackboard());
        world.AddComponent(entity, new BrainBTreeState());

        var assetA = Guid.NewGuid();
        int a1 = MakeSlotKey(assetA, Guid.NewGuid());
        int a2 = MakeSlotKey(assetA, Guid.NewGuid());
        var assetB = Guid.NewGuid();
        int b1 = MakeSlotKey(assetB, Guid.NewGuid());
        int b2 = MakeSlotKey(assetB, Guid.NewGuid());

        const string nameA = "SwitchBehaviorA"; const int idA = 8202;
        const string nameB = "SwitchBehaviorB"; const int idB = 8203;
        registry.Register(idA, nameA, MakeStatefulDefinition(nameA, idA,
            new StatefulSlotInfo[] { new StatefulSlotInfo(a1, 4, 0u), new StatefulSlotInfo(a2, 4, 0u) }));
        registry.Register(idB, nameB, MakeStatefulDefinition(nameB, idB,
            new StatefulSlotInfo[] { new StatefulSlotInfo(b1, 4, 0u), new StatefulSlotInfo(b2, 4, 0u) }));

        Assign(world, sys, entity, nameA);
        Assert.True(HasSlot(world, entity, a1) && HasSlot(world, entity, a2), "A's slots provisioned");

        Assign(world, sys, entity, nameB);
        Assert.True(HasSlot(world, entity, b1) && HasSlot(world, entity, b2), "B's slots provisioned after switch");
        Assert.False(HasSlot(world, entity, a1), "A's slot a1 detached on switch");
        Assert.False(HasSlot(world, entity, a2), "A's slot a2 detached on switch");
        Assert.Equal(2, SlotCountOf(world, entity));

        world.Dispose();
    }
}
