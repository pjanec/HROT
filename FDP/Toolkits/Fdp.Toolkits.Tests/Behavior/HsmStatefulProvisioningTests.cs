using System;
using System.Collections.Generic;
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
/// ⭐⭐⭐ <b><c>E2</c> — an HSM-tier behaviour's slot manifest is provisioned by the SAME production
/// ingress that serves BTree, and the slots start zeroed.</b>
///
/// <para>
/// ⭐⭐ <b>The design claim being pinned: there is no second provisioner.</b>
/// <c>BehaviorIngressSystem:142-154</c> reads <c>def.StatefulWorkingSlots</c> and provisions
/// <b>without ever consulting <c>BrainTier</c></b>. ⇒ <c>E2</c> is satisfied the moment <c>E1</c>
/// emits the manifest — nothing HSM-specific was written, and this file is the evidence for that
/// rather than a promise about it.
/// </para>
///
/// <para>
/// ⛔ <b>Why the emission test alone is not enough.</b> <c>HsmStatefulSlotEmissionTests</c> asserts on
/// emitted SOURCE TEXT. That the runtime then does something useful with the array is a separate
/// claim, and it is the one that would silently rot if the ingress ever grew a
/// <c>if (def.BrainTier == BrainTierBTree)</c> guard around provisioning.
/// </para>
/// </summary>
public sealed unsafe class HsmStatefulProvisioningTests
{
    private static EntityRepository CreateWorld()
    {
        var world = TestWorldFactory.Create();
        world.RegisterComponent<BlueprintBlackboard1024>();
        world.RegisterComponent<BlueprintBlackboard4096>();
        world.RegisterComponent<BlueprintBlackboard16384>();
        return world;
    }

    /// <remarks>
    /// ⚠ No interpreter and no <c>HsmDefinition</c> blob: neither is consulted on the assign path, and
    /// leaving them null is what makes this a test of PROVISIONING rather than of HSM execution.
    /// </remarks>
    private static BehaviorDefinition MakeHsmDefinition(string name, IReadOnlyList<StatefulSlotInfo> slots)
        => new BehaviorDefinition
        {
            Name                 = name,
            BrainTier            = BehaviorConstants.BrainTierHsm,
            StatefulWorkingSlots = slots,
        };

    private static Entity MakeBrainEntity(EntityRepository world)
    {
        var entity = world.CreateEntity();
        world.AddComponent(entity, new BehaviorState());
        world.AddComponent(entity, new BrainBlackboard());
        return entity;
    }

    private static void Assign(EntityRepository world, BehaviorIngressSystem sys, Entity entity, string name)
    {
        world.Bus.PublishManaged(new AssignBehaviorEvent
        {
            Entity       = entity,
            BehaviorName = name,
            JsonParams   = string.Empty,
        });
        world.Bus.SwapBuffers();
        sys.Execute(world, 0.016f);
    }

    /// <summary>Reads the entity's active tier and runs <paramref name="probe"/> over its memory.</summary>
    private static void WithTierMemory(EntityRepository world, Entity entity, Action<IntPtr> probe)
    {
        if (world.HasComponent<BlueprintBlackboard16384>(entity))
        {
            ref var t = ref world.GetComponentRW<BlueprintBlackboard16384>(entity);
            fixed (byte* m = t.Memory) { probe((IntPtr)m); return; }
        }
        if (world.HasComponent<BlueprintBlackboard4096>(entity))
        {
            ref var t = ref world.GetComponentRW<BlueprintBlackboard4096>(entity);
            fixed (byte* m = t.Memory) { probe((IntPtr)m); return; }
        }
        if (world.HasComponent<BlueprintBlackboard1024>(entity))
        {
            ref var t = ref world.GetComponentRW<BlueprintBlackboard1024>(entity);
            fixed (byte* m = t.Memory) { probe((IntPtr)m); return; }
        }
        Assert.Fail("Entity carries no BlueprintBlackboard* tier after assignment.");
    }

    // ── The rail ────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐ <b>N authored state variables ⇒ N slots, through the production ingress.</b> The manifest
    /// shape is exactly what <c>HsmBridgeEmitCore</c> now emits: one entry per <c>Role = State</c>
    /// variable, keyed by <c>ComputeStatefulSlotKey</c>.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(8)]
    public void NStateVariablesOnAnHsmBehavior_ProvisionNSlots(int n)
    {
        var world    = CreateWorld();
        var registry = new BehaviorRegistry();
        var sys      = new BehaviorIngressSystem(registry);
        var entity   = MakeBrainEntity(world);

        var keys  = new int[n];
        var slots = new StatefulSlotInfo[n];
        for (int i = 0; i < n; i++)
        {
            keys[i]  = unchecked(0x51070000 + i);
            slots[i] = new StatefulSlotInfo(keys[i], 4, 0u);
        }

        const string name = "HsmWithAuthoredState";
        registry.Register(7301, name, MakeHsmDefinition(name, slots));
        Assign(world, sys, entity, name);

        WithTierMemory(world, entity, mem =>
        {
            byte* m = (byte*)mem;
            Assert.Equal(n, BlueprintBlackboardPartitions.GetSlotCount(m));
            foreach (int key in keys)
                Assert.True(BlueprintBlackboardPartitions.TryGetSlotOffset(m, key, out _),
                    $"slot 0x{key:X8} was not provisioned for an HSM-tier behaviour");
        });

        world.Dispose();
    }

    /// <summary>
    /// 🔴 <b>Zeroed at activation — and NOT vacuously.</b> The tier is pre-dirtied with <c>0xAA</c>
    /// through a scratch slot that is then detached, so the manifest slot lands on a free-list block
    /// still holding the previous occupant's bytes. ⛔ Without the allocator's <c>InitBlock</c> a stale
    /// non-zero value would be read as live working state on the very first tick.
    /// </summary>
    [Fact]
    public void ProvisionedSlots_StartZeroed_EvenOnAReusedPayloadBlock()
    {
        var world    = CreateWorld();
        var registry = new BehaviorRegistry();
        var sys      = new BehaviorIngressSystem(registry);
        var entity   = MakeBrainEntity(world);

        const int ScratchKey  = 0x5107BEEF;
        const int ScratchSize = 64;

        world.AddComponent(entity, new BlueprintBlackboard1024());
        {
            ref var tier = ref world.GetComponentRW<BlueprintBlackboard1024>(entity);
            fixed (byte* mem = tier.Memory)
            {
                BlueprintBlackboardPartitions.Initialize(
                    mem, BlueprintBlackboard1024.TotalSize, BlueprintBlackboard1024.MaxSlots);
                Assert.True(BlueprintBlackboardPartitions.TryAttach(mem, ScratchKey, ScratchSize, 0u, out int off));
                for (int i = 0; i < ScratchSize; i++) mem[off + i] = 0xAA;
                Assert.True(BlueprintBlackboardPartitions.TryDetach(mem, ScratchKey));
            }
        }

        const int SlotKey = 0x5107C0DE;
        const string name = "HsmZeroedState";
        registry.Register(7302, name,
            MakeHsmDefinition(name, new[] { new StatefulSlotInfo(SlotKey, ScratchSize, 0u) }));
        Assign(world, sys, entity, name);

        WithTierMemory(world, entity, mem =>
        {
            byte* m = (byte*)mem;
            Assert.True(BlueprintBlackboardPartitions.TryGetSlotOffset(m, SlotKey, out int off));
            for (int i = 0; i < ScratchSize; i++)
                Assert.Equal(0, m[off + i]);
        });

        world.Dispose();
    }

    /// <summary>
    /// ⭐⭐⭐ <b>The tier-agnosticism itself, asserted.</b> The identical manifest under
    /// <c>BrainTierHsm</c> and <c>BrainTierBTree</c> provisions the identical slot set. ⛔ If anyone
    /// ever gates provisioning on <c>BrainTier</c>, this is the test that says so — and it says it
    /// about the DESIGN CLAIM, not about a symptom downstream of it.
    /// </summary>
    [Fact]
    public void TheSameManifestProvisionsIdentically_UnderHsmAndBTreeTiers()
    {
        static int[] ProvisionUnder(byte brainTier, string name, int id)
        {
            var world    = CreateWorld();
            var registry = new BehaviorRegistry();
            var sys      = new BehaviorIngressSystem(registry);
            var entity   = MakeBrainEntity(world);

            var slots = new StatefulSlotInfo[]
            {
                new StatefulSlotInfo(0x5107A001, 4,  0u),
                new StatefulSlotInfo(0x5107A002, 16, 0u),
            };
            registry.Register(id, name, new BehaviorDefinition
            {
                Name                 = name,
                BrainTier            = brainTier,
                StatefulWorkingSlots = slots,
            });

            var world2 = world;
            Assign(world2, sys, entity, name);

            var offsets = new int[slots.Length];
            WithTierMemory(world, entity, mem =>
            {
                byte* m = (byte*)mem;
                for (int i = 0; i < slots.Length; i++)
                {
                    Assert.True(BlueprintBlackboardPartitions.TryGetSlotOffset(m, slots[i].SlotKey, out int off));
                    offsets[i] = off;
                }
            });

            world.Dispose();
            return offsets;
        }

        var hsm   = ProvisionUnder(BehaviorConstants.BrainTierHsm,   "TierAgnosticHsm",   7303);
        var btree = ProvisionUnder(BehaviorConstants.BrainTierBTree, "TierAgnosticBTree", 7304);

        Assert.Equal(btree, hsm);
    }
}
