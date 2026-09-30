using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fbt;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Behavior.Systems;
using Fdp.Toolkit.Blueprints.Partitioning;
using Xunit;

namespace Fdp.Toolkit.Behavior.Tests
{
    /// <summary>
    /// ⭐⭐ <b><c>CE-446</c> — ARM 3 of <see cref="BrainTickSystem"/>: a behaviour implemented by a blueprint.</b>
    /// 📄 <c>Architect_Question_77</c> §3 C/D.
    ///
    /// <para>
    /// ⭐ Driven through the REAL ingress (so the store and the root block are provisioned exactly as in production)
    /// with a hand-written <see cref="BlueprintBehaviorTickDelegate"/> standing in for the generated one — the runtime
    /// contract is what these rails pin; the compiler half has its own.
    /// </para>
    /// </summary>
    public unsafe class BrainTickSystemBlueprintArmTests
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct Block
        {
            public int Target;   // In — from the intent JSON
            public int Count;    // St — the behaviour's own state
        }

        private const string Name = "CountToTarget";
        private const int    Id   = 9301;

        private static (EntityRepository world, BehaviorIngressSystem ingress, BrainTickSystem brain, Entity e, Func<int> ticks, Func<Block> last)
            Fixture(string json)
        {
            var world = TestWorldFactory.Create();
            BlueprintTierTable.RegisterAll(world);

            int ticks = 0;
            Block seen = default;
            var registry = new BehaviorRegistry();
            registry.Register(Id, Name, new BehaviorDefinition
            {
                Name                 = Name,
                BrainTier            = BehaviorConstants.BrainTierBlueprint,
                BlackboardLayoutType = typeof(Block),
                ParseParams = static (string j, byte* mem, int capacity, EntityRepository w, Entity self) =>
                    ((Block*)mem)->Target = int.Parse(j),
                BlueprintTick = (ref byte block, EntityRepository w, Fdp.Interfaces.IEntityCommandBuffer ecb, Entity self, float time, float dt, uint instanceId) =>
                {
                    ticks++;
                    ref var b = ref Unsafe.As<byte, Block>(ref block);
                    b.Count++;
                    seen = b;
                    return b.Count >= b.Target ? NodeStatus.Success : NodeStatus.Running;
                },
            });

            var e = world.CreateEntity();
            world.AddComponent(e, new BehaviorState());
            world.Bus.PublishManaged(new AssignBehaviorEvent { Entity = e, BehaviorName = Name, JsonParams = json });
            world.Bus.SwapBuffers();

            var ingress = new BehaviorIngressSystem(registry);
            ingress.Execute(world, 0.016f);
            return (world, ingress, new BrainTickSystem(registry), e, () => ticks, () => seen);
        }

        private static int LiveBehaviourBlocks(EntityRepository world, Entity e)
        {
            byte* memory = OccurrenceStoreAccess.TryGetStore(world, e, out _);
            if (memory == null) return 0;
            ref var header = ref Unsafe.AsRef<BlueprintBlackboardHeader>(memory);
            byte* table = memory + sizeof(BlueprintBlackboardHeader);
            int live = 0;
            for (int i = 0; i < header.SlotCount; i++)
                if (Unsafe.AsRef<BlueprintSlotEntry>(table + i * BlueprintBlackboardPartitions.SlotEntrySize).BlueprintId != 0
                    && BlueprintBlackboardPartitions.GetSlotKind(memory, i) == OccurrenceKind.BlueprintBehavior)
                    live++;
            return live;
        }

        private static int TickAndCountFinished(EntityRepository world, BrainTickSystem brain, Entity e, int frames)
        {
            int finished = 0;
            for (int i = 0; i < frames; i++)
            {
                brain.Execute(world, 0.016f);
                world.Bus.SwapBuffers();
                foreach (var evt in world.Bus.Read<BehaviorFinishedEvent>())
                    if (evt.Entity.Index == e.Index && evt.Result == NodeStatus.Success) finished++;
            }
            return finished;
        }

        /// <summary>
        /// ⭐⭐⭐ Assign → the tier is committed → it runs over the root block (the JSON reached <c>In</c>, the state
        /// persists across frames) → it finishes on <c>Success</c> → <see cref="BehaviorFinishedEvent"/> EXACTLY once →
        /// and it is NOT ticked again afterwards.
        /// <para>⚠ Inverse-edit red-proof: drop the "already finished" guard in <c>TickBlueprint</c> and the tick count
        /// runs past 3 (and the count past the target).</para>
        /// </summary>
        [Fact]
        public void CE446_ABlueprintBehaviour_RunsOverItsRootBlock_AndFinishesExactlyOnce()
        {
            var (world, _, brain, e, ticks, last) = Fixture("3");

            Assert.Equal(BehaviorConstants.BrainTierBlueprint, world.GetComponent<BehaviorState>(e).BrainTier);

            int finished = TickAndCountFinished(world, brain, e, frames: 6);

            Assert.Equal(1, finished);
            Assert.Equal(3, ticks());
            Assert.Equal(3, last().Target);   // the JSON reached In
            Assert.Equal(3, last().Count);    // the state persisted across frames

            world.Dispose();
        }

        /// <summary>
        /// ⭐⭐ A new assign bumps <c>InstanceId</c>, starts the block from empty (<c>R-153</c>) and ticks again — ⇒
        /// "finished" is per instance, never per entity. ⭐ This is also cancellation (<c>Q77</c> D): the new assign
        /// replaces the running one.
        /// </summary>
        [Fact]
        public void CE446_ReassigningAFinishedBlueprintBehaviour_RunsItAgainFromAnEmptyBlock()
        {
            var (world, ingress, brain, e, ticks, last) = Fixture("2");
            Assert.Equal(1, TickAndCountFinished(world, brain, e, frames: 4));

            world.Bus.PublishManaged(new AssignBehaviorEvent { Entity = e, BehaviorName = Name, JsonParams = "2" });
            world.Bus.SwapBuffers();
            ingress.Execute(world, 0.016f);

            Assert.Equal(1, TickAndCountFinished(world, brain, e, frames: 4));
            Assert.Equal(4, ticks());
            Assert.Equal(2, last().Count);    // from an EMPTY block, not 2 + 2

            world.Dispose();
        }

        /// <summary>
        /// ⭐⭐ <b>The block is freed AT FINISH</b> (user, <c>2026-09-30</c>: <i>"isn't it well defined when a behavior finished so
        /// when to free its resources?"</i>) — for this tier it is: the instance is never ticked again. ⭐ The decider's
        /// clear that follows (<c>BD1-DESIGN.md</c> §1) then finds nothing to free and is a no-op.
        /// <para>⚠ Inverse-edit red-proof: drop the <c>DetachRoot</c> in <c>TickBlueprint</c> and the first assertion fails.</para>
        /// </summary>
        [Fact]
        public void CE446_AFinishedBlueprintBehaviour_FreesItsBlockAtFinish_AndTheClearAfterIsANoOp()
        {
            var (world, ingress, brain, e, _, _) = Fixture("1");
            Assert.Equal(1, LiveBehaviourBlocks(world, e));

            Assert.Equal(1, TickAndCountFinished(world, brain, e, frames: 2));
            Assert.Equal(0, LiveBehaviourBlocks(world, e));
            Assert.False(RootParamsAccess.TryGetRootBytes(world, e, out _));

            world.Bus.Publish(new ClearBehaviorEvent { Entity = e });
            world.Bus.SwapBuffers();
            Assert.Null(Record.Exception(() => ingress.Execute(world, 0.016f)));
            Assert.Equal(0, world.GetComponent<BehaviorState>(e).BrainTier);

            world.Dispose();
        }

        // ══ CE-446 step 3 — hot reload of a RUNNING blueprint behaviour (Q77 §5.12) ══════════════════════════════

        [StructLayout(LayoutKind.Sequential)]
        private struct WideBlock
        {
            public int Target;
            public int Count;
            public long Extra;   // the reload GREW the block
        }

        /// <summary>A hot reload, as production commits it: a staging registry merged over the live one.</summary>
        private static void Reload(BehaviorRegistry live, BehaviorDefinition next)
        {
            var staging = new BehaviorRegistry();
            staging.Register(Id, Name, next);
            live.MergeFrom(staging);
        }

        private static BehaviorDefinition CountingDef<TBlock>(ulong layout, Action<int>? onTick = null, bool parseThrows = false)
            where TBlock : unmanaged => new()
        {
            Name                   = Name,
            BrainTier              = BehaviorConstants.BrainTierBlueprint,
            BlackboardLayoutType   = typeof(TBlock),
            BlueprintStructureHash = layout,
            // ⭐ "{}" is the reset's call: no JSON ⇒ the authored default (10).
            ParseParams = parseThrows
                ? static (string j, byte* mem, int capacity, EntityRepository w, Entity self) => throw new InvalidOperationException("resolver failed")
                : static (string j, byte* mem, int capacity, EntityRepository w, Entity self) =>
                    ((Block*)mem)->Target = j == "{}" ? 10 : int.Parse(j),
            BlueprintTick = (ref byte block, EntityRepository w, Fdp.Interfaces.IEntityCommandBuffer ecb, Entity self, float time, float dt, uint instanceId) =>
            {
                ref var b = ref Unsafe.As<byte, Block>(ref block);
                b.Count++;
                onTick?.Invoke(b.Count);
                return b.Count >= b.Target ? NodeStatus.Success : NodeStatus.Running;
            },
        };

        private static (EntityRepository world, BehaviorRegistry registry, BrainTickSystem brain, Entity e) Running(BehaviorDefinition def, string json)
        {
            var world = TestWorldFactory.Create();
            BlueprintTierTable.RegisterAll(world);
            var registry = new BehaviorRegistry();
            registry.Register(Id, Name, def);

            var e = world.CreateEntity();
            world.AddComponent(e, new BehaviorState());
            world.Bus.PublishManaged(new AssignBehaviorEvent { Entity = e, BehaviorName = Name, JsonParams = json });
            world.Bus.SwapBuffers();
            new BehaviorIngressSystem(registry).Execute(world, 0.016f);
            return (world, registry, new BrainTickSystem(registry), e);
        }

        private static Block ReadBlock(EntityRepository world, Entity e)
        {
            Assert.True(RootParamsAccess.TryGetRootBytes(world, e, out byte* p));
            return *(Block*)p;
        }

        /// <summary>
        /// ⭐ A reload that keeps the layout is SOFT: the running instance keeps its state and simply ticks the new code
        /// (<c>AI_Editor_Shared_Infrastructure.md</c> §17 — <i>"instances retain runtime state"</i>).
        /// </summary>
        [Fact]
        public void CE446_AReloadThatKeepsTheLayout_KeepsTheRunningState()
        {
            var (world, registry, brain, e) = Running(CountingDef<Block>(layout: 0xA1), "5");
            TickAndCountFinished(world, brain, e, frames: 2);
            Assert.Equal(2, ReadBlock(world, e).Count);

            Reload(registry, CountingDef<Block>(layout: 0xA1));
            TickAndCountFinished(world, brain, e, frames: 1);

            Assert.Equal(3, ReadBlock(world, e).Count);   // continued, not restarted
            Assert.Equal(5, ReadBlock(world, e).Target);  // the assigned params survived
            world.Dispose();
        }

        /// <summary>
        /// ⭐⭐ A reload that changes the layout at the SAME width is HARD: the block is rebuilt by the definition's own
        /// pipeline with no JSON — authored defaults — exactly <c>R-24</c>'s Instance reset. The reset is LOGGED.
        /// <para>⚠ Inverse-edit red-proof: drop the <c>layoutChanged</c> term and the count continues from 2.</para>
        /// </summary>
        [Fact]
        public void CE446_AReloadThatChangesTheLayout_HardResetsTheRunningInstance_AndLogsIt()
        {
            var (world, registry, _, e) = Running(CountingDef<Block>(layout: 0xA1), "5");
            var log = new RecordingReloadLog();
            var brain = new BrainTickSystem(registry, reloadLog: log);
            TickAndCountFinished(world, brain, e, frames: 2);

            Reload(registry, CountingDef<Block>(layout: 0xB2));
            TickAndCountFinished(world, brain, e, frames: 1);

            Assert.Equal(1, ReadBlock(world, e).Count);    // restarted from an empty block, then ticked once
            Assert.Equal(10, ReadBlock(world, e).Target);  // authored default — the JSON is not retained
            Assert.Equal((0xA1UL, 0xB2UL), Assert.Single(log.HardResets));
            world.Dispose();
        }

        /// <summary>
        /// ⭐⭐⭐ A reload that GROWS the block re-attaches it at the new width before the new code ticks — ⛔ ticking the
        /// wider layout over the old slot would write into whatever the allocator put after it (<c>SLICE2-DESIGN.md</c> Flaw 2).
        /// ⭐ Landed between the assign and the FIRST tick, so no started layout is on record yet — the window the WIDTH
        /// check exists for (the layout hash cannot see it).
        /// <para>⚠ Inverse-edit red-proof run: drop the <c>widthChanged</c> term and the slot stays 8 bytes.</para>
        /// </summary>
        [Fact]
        public void CE446_AReloadThatGrowsTheBlock_BeforeItsFirstTick_ReattachesItAtTheNewWidth()
        {
            var (world, registry, brain, e) = Running(CountingDef<Block>(layout: 0xA1), "5");
            Assert.True(RootParamsAccess.TryGetRootBytes(world, e, out _, out int before));
            Assert.Equal(sizeof(Block), before);

            int seenAtNewWidth = -1;
            Reload(registry, CountingDef<WideBlock>(layout: 0xC3, onTick: c => seenAtNewWidth = c));
            TickAndCountFinished(world, brain, e, frames: 1);

            Assert.True(RootParamsAccess.TryGetRootBytes(world, e, out _, out int after));
            Assert.Equal(sizeof(WideBlock), after);
            Assert.Equal(1, seenAtNewWidth);
            world.Dispose();
        }

        /// <summary>
        /// ⛔ A reset whose rebuild FAILS (the resolver throws) must not tick a half-built block: the behaviour is CLEARED
        /// — the same clear as finishing (<c>CE-449</c>) — and never ticked again.
        /// </summary>
        [Fact]
        public void CE446_AHardResetWhoseRebuildFails_ClearsTheBehaviour_InsteadOfTickingIt()
        {
            var (world, registry, brain, e) = Running(CountingDef<Block>(layout: 0xA1), "5");
            TickAndCountFinished(world, brain, e, frames: 1);

            int ticksAfter = 0;
            var broken = CountingDef<Block>(layout: 0xB2, onTick: _ => ticksAfter++, parseThrows: true);
            Reload(registry, broken);
            TickAndCountFinished(world, brain, e, frames: 3);

            Assert.Equal(0, ticksAfter);
            Assert.Equal(0, world.GetComponent<BehaviorState>(e).BrainTier);
            Assert.Equal(0, LiveBehaviourBlocks(world, e));
            world.Dispose();
        }

        private sealed class RecordingReloadLog : Fdp.Toolkit.Blueprints.Systems.IReloadLogSink
        {
            public readonly System.Collections.Generic.List<(ulong, ulong)> HardResets = new();
            public void OnSoftReload(int blueprintId, Entity entity, ulong hash) { }
            public void OnHardReset(int blueprintId, Entity entity, ulong oldHash, ulong newHash) => HardResets.Add((oldHash, newHash));
        }

        /// <summary>
        /// ⛔ The root block is declared <see cref="OccurrenceKind.BlueprintBehavior"/> — NOT <see cref="OccurrenceKind.Blueprint"/>,
        /// which <c>BlueprintTickSystem</c> walks as an attached Instance and ingress sweeps as a hosted occurrence.
        /// </summary>
        [Fact]
        public void CE446_TheRootBlock_IsDeclaredAsABlueprintBehaviourSlot_NotAnInstance()
        {
            var (world, _, _, e, _, _) = Fixture("1");

            byte* memory = OccurrenceStoreAccess.TryGetStore(world, e, out _);
            Assert.True(memory != null, "the store must exist");
            int key = RootParamsAccess.KeyFor(world, e);

            ref var header = ref Unsafe.AsRef<BlueprintBlackboardHeader>(memory);
            byte* table = memory + sizeof(BlueprintBlackboardHeader);
            int found = -1;
            for (int i = 0; i < header.SlotCount; i++)
                if (Unsafe.AsRef<BlueprintSlotEntry>(table + i * BlueprintBlackboardPartitions.SlotEntrySize).BlueprintId == key)
                    found = i;

            Assert.True(found >= 0, "the root block must be attached");
            Assert.Equal(OccurrenceKind.BlueprintBehavior, BlueprintBlackboardPartitions.GetSlotKind(memory, found));

            world.Dispose();
        }
    }
}
