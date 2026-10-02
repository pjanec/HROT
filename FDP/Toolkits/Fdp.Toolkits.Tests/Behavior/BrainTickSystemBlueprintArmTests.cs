using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fbt;
using Fbt.Compiler;
using Fbt.Runtime;
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
                BlueprintTick = (ref byte block, ref byte exec, EntityRepository w, Fdp.Interfaces.IEntityCommandBuffer ecb, Entity self, float time, float dt, uint instanceId, int occurrenceKey) =>
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

        // ══ CE-452 — hot reload of a RUNNING behaviour restarts it through the ONE start pipeline (Q77 §5.12) ══════════

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
            // ⭐ "{}" = no JSON ⇒ the authored default (10).
            ParseParams = parseThrows
                ? static (string j, byte* mem, int capacity, EntityRepository w, Entity self) => throw new InvalidOperationException("resolver failed")
                : static (string j, byte* mem, int capacity, EntityRepository w, Entity self) =>
                    ((Block*)mem)->Target = j == "{}" ? 10 : int.Parse(j),
            BlueprintTick = (ref byte block, ref byte exec, EntityRepository w, Fdp.Interfaces.IEntityCommandBuffer ecb, Entity self, float time, float dt, uint instanceId, int occurrenceKey) =>
            {
                ref var b = ref Unsafe.As<byte, Block>(ref block);
                b.Count++;
                onTick?.Invoke(b.Count);
                return b.Count >= b.Target ? NodeStatus.Success : NodeStatus.Running;
            },
        };

        private sealed class Host
        {
            public required EntityRepository World;
            public required BehaviorRegistry Registry;
            public required BehaviorIngressSystem Ingress;
            public required BrainTickSystem Brain;
            public required Entity E;

            /// <summary>One production frame: ingress (Input) → brain (Simulation) → swap.</summary>
            public void Frames(int n)
            {
                for (int i = 0; i < n; i++)
                {
                    Ingress.Execute(World, 0.016f);
                    Brain.Execute(World, 0.016f);
                    World.Bus.SwapBuffers();
                }
            }

            public uint InstanceId => World.GetComponent<BehaviorState>(E).InstanceId;
        }

        private static Host Running(BehaviorDefinition def, string json, Fdp.Toolkit.Blueprints.Systems.IReloadLogSink? log = null)
        {
            var world = TestWorldFactory.Create();
            BlueprintTierTable.RegisterAll(world);
            var registry = new BehaviorRegistry();
            registry.Register(Id, Name, def);

            var e = world.CreateEntity();
            world.AddComponent(e, new BehaviorState());
            world.Bus.PublishManaged(new AssignBehaviorEvent { Entity = e, BehaviorName = Name, JsonParams = json });
            world.Bus.SwapBuffers();
            return new Host
            {
                World = world, Registry = registry, Ingress = new BehaviorIngressSystem(registry),
                Brain = new BrainTickSystem(registry, reloadLog: log), E = e,
            };
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
        public void CE452_AReloadThatKeepsTheLayout_KeepsTheRunningState()
        {
            var h = Running(CountingDef<Block>(layout: 0xA1), "5");
            h.Frames(2);
            Assert.Equal(2, ReadBlock(h.World, h.E).Count);
            uint instance = h.InstanceId;

            Reload(h.Registry, CountingDef<Block>(layout: 0xA1));
            h.Frames(1);

            Assert.Equal(instance, h.InstanceId);            // not restarted
            Assert.Equal(3, ReadBlock(h.World, h.E).Count);  // continued
            Assert.Equal(5, ReadBlock(h.World, h.E).Target);
            h.World.Dispose();
        }

        /// <summary>
        /// ⭐⭐⭐ A reload that changes the layout RESTARTS the running instance through the start pipeline — ⭐ WITH THE
        /// PARAMETERS IT WAS ASSIGNED (the <see cref="BehaviorStartRecord"/>), not authored defaults. Logged once.
        /// <para>⚠ Inverse-edit red-proofs run: drop the <c>layoutChanged</c> term ⇒ the count continues from 2; drop the
        /// record read ⇒ <c>Target</c> is the default 10.</para>
        /// </summary>
        [Fact]
        public void CE452_AReloadThatChangesTheLayout_RestartsTheInstance_WithTheAssignedParameters()
        {
            var log = new RecordingReloadLog();
            var h = Running(CountingDef<Block>(layout: 0xA1), "5", log);
            h.Frames(2);
            uint instance = h.InstanceId;

            Reload(h.Registry, CountingDef<Block>(layout: 0xB2));
            h.Frames(1);                                     // detects → skips → requests the restart
            Assert.Equal(instance, h.InstanceId);
            h.Frames(1);                                     // ingress restarts it → it ticks once

            Assert.NotEqual(instance, h.InstanceId);         // a new instance
            Assert.Equal(1, ReadBlock(h.World, h.E).Count);  // from an empty block
            Assert.Equal(5, ReadBlock(h.World, h.E).Target); // ⭐ the ASSIGNED parameters survived the reload
            Assert.Equal((0xA1UL, 0xB2UL), Assert.Single(log.HardResets));
            h.World.Dispose();
        }

        /// <summary>
        /// ⭐⭐⭐ A reload that GROWS the block — landed between the assign and the FIRST tick, so no started layout is on record
        /// (the window the WIDTH check exists for) — restarts it at the new width before the new code ever ticks over the old
        /// slot (⛔ which would write into the next occurrence — <c>SLICE2-DESIGN.md</c> Flaw 2).
        /// <para>⚠ Inverse-edit red-proof run: drop the <c>widthChanged</c> term ⇒ the slot stays 8 bytes.</para>
        /// </summary>
        [Fact]
        public void CE452_AReloadThatGrowsTheBlock_BeforeItsFirstTick_RestartsItAtTheNewWidth()
        {
            var h = Running(CountingDef<Block>(layout: 0xA1), "5");
            h.Ingress.Execute(h.World, 0.016f);              // assigned, never ticked
            Assert.True(RootParamsAccess.TryGetRootBytes(h.World, h.E, out _, out int before));
            Assert.Equal(sizeof(Block), before);

            int ticksAtOldWidth = 0;
            Reload(h.Registry, CountingDef<WideBlock>(layout: 0xC3, onTick: _ => ticksAtOldWidth +=
                RootParamsAccess.TryGetRootBytes(h.World, h.E, out byte* wp, out int w) && w != sizeof(WideBlock) ? 1 : 0));
            h.Brain.Execute(h.World, 0.016f);
            h.World.Bus.SwapBuffers();
            h.Frames(1);

            Assert.True(RootParamsAccess.TryGetRootBytes(h.World, h.E, out _, out int after));
            Assert.Equal(sizeof(WideBlock), after);
            Assert.Equal(0, ticksAtOldWidth);
            Assert.Equal(5, ReadBlock(h.World, h.E).Target);
            h.World.Dispose();
        }

        /// <summary>
        /// ⛔ A restart whose start FAILS (the new parser / resolver throws) leaves the instance on the old block — so the next
        /// tick, finding the same instance still pending, CLEARS it (the <c>CE-449</c> clear) instead of ticking a block that
        /// does not fit the new code.
        /// </summary>
        [Fact]
        public void CE452_ARestartWhoseStartFails_ClearsTheBehaviour_InsteadOfTickingIt()
        {
            var h = Running(CountingDef<Block>(layout: 0xA1), "5");
            h.Frames(1);

            int ticksAfter = 0;
            Reload(h.Registry, CountingDef<Block>(layout: 0xB2, onTick: _ => ticksAfter++, parseThrows: true));
            h.Frames(4);

            Assert.Equal(0, ticksAfter);
            Assert.Equal(0, h.World.GetComponent<BehaviorState>(h.E).BrainTier);
            Assert.Equal(0, LiveBehaviourBlocks(h.World, h.E));
            h.World.Dispose();
        }

        /// <summary>
        /// ⭐ The record is written by EVERY successful start (with that start's <c>InstanceId</c>) and dropped by the clear —
        /// ⛔ a stale record would restart a behaviour the entity no longer runs.
        /// </summary>
        [Fact]
        public void CE452_TheStartRecord_IsWrittenAtStart_AndDroppedAtClear()
        {
            var h = Running(CountingDef<Block>(layout: 0xA1), "7");
            h.Ingress.Execute(h.World, 0.016f);

            Assert.True(h.World.HasManagedComponent<BehaviorStartRecord>(h.E));
            var record = ((Fdp.ModuleHost.Abstractions.ISimulationView)h.World).GetManagedComponentRO<BehaviorStartRecord>(h.E);
            Assert.Equal((Name, "7", h.InstanceId), (record.BehaviorName, record.JsonParams, record.InstanceId));

            h.World.Bus.Publish(new ClearBehaviorEvent { Entity = h.E });
            h.World.Bus.SwapBuffers();
            h.Ingress.Execute(h.World, 0.016f);
            Assert.False(h.World.HasManagedComponent<BehaviorStartRecord>(h.E));
            h.World.Dispose();
        }

        // ══ CE-451 — an assign BY HASH runs the same start pipeline ══════════════════════════════════════════════════

        /// <summary>
        /// ⭐⭐ An assign by hash of a behaviour WITH parameters provisions its block and parses it (no JSON ⇒ authored
        /// defaults), then ticks. ⛔ Before, it attached no params block and the tick THREW in <c>RootParamsAccess.RootRef</c>.
        /// <para>⚠ Inverse-edit red-proof: the old hand-written handler ⇒ the first assertion fails and the tick throws.</para>
        /// </summary>
        [Fact]
        public void CE451_AnAssignByHash_ProvisionsTheParamsBlock_AndTicks()
        {
            var world = TestWorldFactory.Create();
            BlueprintTierTable.RegisterAll(world);
            var registry = new BehaviorRegistry();
            registry.Register(Id, Name, CountingDef<Block>(layout: 0xA1));
            var e = world.CreateEntity();
            world.AddComponent(e, new BehaviorState());
            world.Bus.Publish(new AssignBehaviorHashEvent { Entity = e, BehaviorHash = Id });
            world.Bus.SwapBuffers();
            new BehaviorIngressSystem(registry).Execute(world, 0.016f);

            Assert.True(RootParamsAccess.TryGetRootBytes(world, e, out _));
            Assert.Null(Record.Exception(() => new BrainTickSystem(registry).Execute(world, 0.016f)));
            Assert.Equal(10, ReadBlock(world, e).Target);   // authored default — the hash event carries no JSON
            Assert.Equal(1, ReadBlock(world, e).Count);
            world.Dispose();
        }

        /// <summary>
        /// ⭐ On CGF a phase advance produces an assign BY NAME (with the task's parameters) and one BY HASH for the same
        /// behaviour. ⇒ when both land in one ingress pass, the hash one is dropped: ONE start, and the named parameters win.
        /// </summary>
        [Fact]
        public void CE451_AnAssignByHash_ThatDuplicatesANamedAssignInTheSamePass_IsDropped()
        {
            var h = Running(CountingDef<Block>(layout: 0xA1), "3");
            h.Ingress.Execute(h.World, 0.016f);                        // first start
            uint before = h.InstanceId;

            // ⭐ both land in ONE ingress pass, as a phase advance on CGF can produce them
            h.World.Bus.PublishManaged(new AssignBehaviorEvent { Entity = h.E, BehaviorName = Name, JsonParams = "5" });
            h.World.Bus.Publish(new AssignBehaviorHashEvent { Entity = h.E, BehaviorHash = Id });
            h.World.Bus.SwapBuffers();
            h.Ingress.Execute(h.World, 0.016f);

            Assert.Equal(before + 1, h.InstanceId);                   // started ONCE
            Assert.Equal(5, ReadBlock(h.World, h.E).Target);          // with the NAMED parameters
            h.World.Dispose();
        }

        // ══ CE-455 — a BTree root restarts on a SAME-WIDTH re-layout too ══════════════════════════════════════════════

        private static NodeStatus CountRunning(ref byte bb, ref BehaviorTreeState state, ref BTreeContext ctx, int paramIndex)
        {
            ref var b = ref Unsafe.As<byte, Block>(ref bb);
            b.Count++;
            return NodeStatus.Running;
        }

        /// <summary>A BTree root over the same <see cref="Block"/> — one leaf that counts and keeps running.</summary>
        private static BehaviorDefinition BTreeCountingDef(ulong layout)
        {
            var builder = new BTreeBuilder<byte, BTreeContext>().Action(CountRunning);
            return new BehaviorDefinition
            {
                Name                   = Name,
                BrainTier              = BehaviorConstants.BrainTierBTree,
                BTreeInterpreter       = new Interpreter<byte, BTreeContext>(builder.Compile(Name), builder.GetRegistry()),
                BlackboardLayoutType   = typeof(Block),
                BlueprintStructureHash = layout,
                ParseParams = static (string j, byte* mem, int capacity, EntityRepository w, Entity self) =>
                    ((Block*)mem)->Target = j == "{}" ? 10 : int.Parse(j),
            };
        }

        /// <summary>
        /// ⭐⭐ <c>CE-455</c>: a BTree root whose parameters are re-laid-out at the SAME width (the generated registrar now
        /// carries <c>BTreeBlackboardPackHelper.LayoutHash</c>) restarts with its assigned parameters — ⛔ before, only the
        /// width was compared for a BTree, so it kept ticking over bytes laid out for the old code.
        /// </summary>
        [Fact]
        public void CE455_ABTreeRoot_ReLaidOutAtTheSameWidth_Restarts_WithTheAssignedParameters()
        {
            var h = Running(BTreeCountingDef(layout: 0xA1), "5");
            h.Frames(2);
            uint instance = h.InstanceId;
            Assert.Equal(2, ReadBlock(h.World, h.E).Count);

            Reload(h.Registry, BTreeCountingDef(layout: 0xB2));          // same Block ⇒ same width
            h.Frames(2);

            Assert.NotEqual(instance, h.InstanceId);
            Assert.Equal(1, ReadBlock(h.World, h.E).Count);
            Assert.Equal(5, ReadBlock(h.World, h.E).Target);
            h.World.Dispose();
        }

        /// <summary>⭐ <c>CE-455</c>: the same layout hash is a soft reload — the BTree keeps counting.</summary>
        [Fact]
        public void CE455_ABTreeRoot_ReloadedWithTheSameLayout_KeepsRunning()
        {
            var h = Running(BTreeCountingDef(layout: 0xA1), "5");
            h.Frames(2);
            uint instance = h.InstanceId;

            Reload(h.Registry, BTreeCountingDef(layout: 0xA1));
            h.Frames(1);

            Assert.Equal(instance, h.InstanceId);
            Assert.Equal(3, ReadBlock(h.World, h.E).Count);
            h.World.Dispose();
        }

        // ══ CE-456 — an assign BY HASH from a mission phase runs on the PHASE's parameters ═══════════════════════════

        private static (EntityRepository World, Entity E) MissionPhaseWorld(string taskBehaviour, string taskParams)
        {
            var world = TestWorldFactory.Create();
            BlueprintTierTable.RegisterAll(world);
            if (!world.IsComponentTypeRegistered<MissionPlanQueue>()) world.RegisterComponent<MissionPlanQueue>();
            if (!world.TryGetTable(typeof(ActiveMissionPlan), out _)) world.RegisterManagedComponent<ActiveMissionPlan>();
            var e = world.CreateEntity();
            world.AddComponent(e, new BehaviorState());
            world.AddComponent(e, new MissionPlanQueue { PhaseCount = 2, CurrentPhase = 1 });     // the director just advanced
            world.SetManagedComponent(e, new ActiveMissionPlan
            {
                Plan = new DomainMissionPlan
                {
                    Tasks =
                    {
                        new DomainMissionTask { BehaviorName = "Other", BehaviorParams = "99" },
                        new DomainMissionTask { BehaviorName = taskBehaviour, BehaviorParams = taskParams },
                    },
                },
            });
            return (world, e);
        }

        /// <summary>
        /// ⭐⭐ <c>CE-456</c>: a mission phase started by HASH (the director's phase advance — no JSON on the event) starts with
        /// the phase task's parameters from <see cref="ActiveMissionPlan"/>. ⛔ Before, on every host without CGF's
        /// <c>MissionAdapterSystem</c>, it ran on its authored defaults (<c>Target == 10</c>).
        /// </summary>
        [Fact]
        public void CE456_AnAssignByHash_FromAMissionPhase_RunsOnThePhaseTasksParameters()
        {
            var (world, e) = MissionPhaseWorld(Name, "7");
            var registry = new BehaviorRegistry();
            registry.Register(Id, Name, CountingDef<Block>(layout: 0xA1));
            world.Bus.Publish(new AssignBehaviorHashEvent { Entity = e, BehaviorHash = Id });
            world.Bus.SwapBuffers();
            new BehaviorIngressSystem(registry).Execute(world, 0.016f);
            new BrainTickSystem(registry).Execute(world, 0.016f);

            Assert.Equal(7, ReadBlock(world, e).Target);
            world.Dispose();
        }

        /// <summary>⭐ <c>CE-456</c>: a task that names a DIFFERENT behaviour lends its parameters to nobody — authored defaults.</summary>
        [Fact]
        public void CE456_ATaskNamingAnotherBehaviour_IsNotUsed()
        {
            var (world, e) = MissionPhaseWorld("SomethingElse", "7");
            var registry = new BehaviorRegistry();
            registry.Register(Id, Name, CountingDef<Block>(layout: 0xA1));
            world.Bus.Publish(new AssignBehaviorHashEvent { Entity = e, BehaviorHash = Id });
            world.Bus.SwapBuffers();
            new BehaviorIngressSystem(registry).Execute(world, 0.016f);
            new BrainTickSystem(registry).Execute(world, 0.016f);

            Assert.Equal(10, ReadBlock(world, e).Target);
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
