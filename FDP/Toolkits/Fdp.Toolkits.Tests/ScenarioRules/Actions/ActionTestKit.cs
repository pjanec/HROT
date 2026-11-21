using System;
using Fbt;
using Fbt.Kernel;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Tests;
using Fdp.Toolkit.Blueprints.Components;
using Fdp.Toolkit.Blueprints.Partitioning;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.ScenarioRules.Actions;

namespace Fdp.Toolkit.ScenarioRules.Tests.Actions
{
    // ⭐ The test vocabulary of the action machinery (re-homed from the trigger PoC, SR-108). Test-only component id 509
    //   (FdpConfig.MAX_COMPONENT_TYPES = 512; production ids 427–429 are ActionArgs / ActionFailure).

    /// <summary>A test-visible log: <c>TestRecord</c> appends its param here.</summary>
    [ComponentId(509)]
    public struct ActionTestLog
    {
        public int Count;
        public int V0, V1, V2, V3, V4, V5, V6, V7;

        public void Append(int v)
        {
            switch (Count) { case 0: V0 = v; break; case 1: V1 = v; break; case 2: V2 = v; break; case 3: V3 = v; break;
                             case 4: V4 = v; break; case 5: V5 = v; break; case 6: V6 = v; break; case 7: V7 = v; break; }
            Count++;
        }
        public int At(int i) => i switch { 0 => V0, 1 => V1, 2 => V2, 3 => V3, 4 => V4, 5 => V5, 6 => V6, _ => V7 };
    }

    public enum TestMode : byte { Slow = 0, Fast = 1 }

    public struct WaitParams { public int Ticks; }
    public struct WaitState { public int Elapsed; }
    public struct NoState { public int Pad; }
    public struct FailParams { public int Code; }
    public struct RecordParams { public int Value; }
    /// <summary>⭐ rules S2 ⑨ — params with an <c>ArgsRef</c>: the index of the step's nested <c>"params"</c> in the caller's <see cref="ActionArgs.Items"/>.</summary>
    public struct RefParams { public int ArgsRef; public int N; }

    /// <summary>The "rich" params: FixedString / enum / float through the JSON seeding, and an authored <see cref="EntityLink"/>.</summary>
    public struct RichParams
    {
        public FixedString32 Name;
        public TestMode Mode;
        public EntityLink Target;
        public float Gain;
        public int N;
    }

    /// <summary>⭐ CE-4162 — a params struct with an <see cref="Entity"/> field (directly, and one nested): the catalogue must refuse it.</summary>
    public struct EntityParams { public Entity Target; public int N; }
    public struct NestedEntity { public Entity Inner; }
    public struct NestedEntityParams { public NestedEntity Holder; }

    /// <summary>A FAT params struct (> 260 B): bigger than the ingress-sized first block's payload, so its step slot must APPEND a block.</summary>
    public struct FatParams { public FixedString128 A; public FixedString128 B; public int N; }

    /// <summary>Measurement hook: did the outer working state's address change while the loop attached / detached a step slot?</summary>
    public static unsafe class Trap
    {
        [ThreadStatic] public static int Checks, Moved;
        public static void Reset() { Checks = Moved = 0; }
    }

    /// <summary>The test <c>[SharedAiAction]</c> vocabulary. Same stateful signature as <c>SharedNodeStatefulAction</c>.</summary>
    public static class TestActions
    {
        [SharedAiAction]
        public static NodeStatus TestWaitTicks(ref WaitParams p, ref WaitState s, Entity self, EntityRepository world)
        {
            s.Elapsed++;
            return s.Elapsed >= p.Ticks ? NodeStatus.Success : NodeStatus.Running;
        }

        /// <summary>Fails, giving its reason through the ONE channel (CE-4165).</summary>
        [SharedAiAction]
        public static NodeStatus TestFail(ref FailParams p, ref NoState s, Entity self, EntityRepository world)
        {
            ActionFailure.Set(world, self, "code " + p.Code);
            return NodeStatus.Failure;
        }

        /// <summary>Fails and says nothing — RunActions must still stamp the step and a generic reason.</summary>
        [SharedAiAction]
        public static NodeStatus TestFailSilently(ref FailParams p, ref NoState s, Entity self, EntityRepository world) => NodeStatus.Failure;

        [SharedAiAction]
        public static NodeStatus TestRecord(ref RecordParams p, ref NoState s, Entity self, EntityRepository world)
        {
            if (s.Pad == 0) { world.GetComponentRW<ActionTestLog>(self).Append(p.Value); s.Pad = 1; }   // once per step run
            return NodeStatus.Success;
        }

        /// <summary>Logs the LENGTH of the item its <c>ArgsRef</c> points at, so a rail sees which item the seeding chose.</summary>
        [SharedAiAction]
        public static NodeStatus TestRefLength(ref RefParams p, ref NoState s, Entity self, EntityRepository world)
        {
            var items = ((Fdp.ModuleHost.Abstractions.ISimulationView)world).GetManagedComponentRO<ActionArgs>(self).Items;
            world.GetComponentRW<ActionTestLog>(self).Append(items[p.ArgsRef].Length);
            return NodeStatus.Success;
        }

        [SharedAiAction]
        public static NodeStatus TestRich(ref RichParams p, ref NoState s, Entity self, EntityRepository world)
        {
            world.GetComponentRW<ActionTestLog>(self).Append(p.N);
            return NodeStatus.Success;
        }

        [SharedAiAction]
        public static NodeStatus TestFat(ref FatParams p, ref NoState s, Entity self, EntityRepository world)
        {
            world.GetComponentRW<ActionTestLog>(self).Append(p.N);
            return NodeStatus.Success;
        }

        /// <summary>Takes a link and logs the entity it resolved to (its network id), so a rail can see the resolution happened.</summary>
        [SharedAiAction]
        public static NodeStatus TestLogLink(ref RichParams p, ref NoState s, Entity self, EntityRepository world)
        {
            world.GetComponentRW<ActionTestLog>(self).Append(p.Target.Ref.NetworkId);
            return NodeStatus.Success;
        }

        [SharedAiAction]
        public static NodeStatus TestTakesEntity(ref EntityParams p, ref NoState s, Entity self, EntityRepository world) => NodeStatus.Success;

        [SharedAiAction]
        public static NodeStatus TestTakesNestedEntity(ref NestedEntityParams p, ref NoState s, Entity self, EntityRepository world) => NodeStatus.Success;
    }

    /// <summary>
    /// The CALLER of <c>RunActions</c> for hosts that have no generated binding: it keeps the loop's own params + state in an occurrence slot (the WORST
    /// case for the "outer ref stays valid" trap — outer and inner step slots then live in the SAME store). A BTree leaf and the direct rails use it.
    /// </summary>
    public static unsafe class ActionHost
    {
        public const int OuterSlotKey = 0x7C0000FF;
        private const ulong OuterHash = 0x0C0FFEE;

        /// <summary>Ticks the loop. On a terminal status the outer slot is detached too (the next run starts fresh) and the final state is handed back.</summary>
        public static NodeStatus Tick(EntityRepository world, Entity self, int listRef, out RunActionsState final, int outerKey = OuterSlotKey, int depth = 0)
        {
            ref RunActionsState rs = ref OccurrenceWorkingState.ResolveOrAttach<RunActionsParams, RunActionsState>(
                world, self, outerKey, OuterHash, OccurrenceKind.Hsm, out bool fresh, out RunActionsParams* pp);
            if (fresh) { pp->ListRef = listRef; pp->Depth = depth; }
            nint before = SlotAddress(world, self, outerKey);
            var st = ScenarioActions.RunActions(ref *pp, ref rs, self, world);   // attaches / detaches step slots (maybe APPENDS a block) while `rs` is held
            Trap.Checks++; if (SlotAddress(world, self, outerKey) != before) Trap.Moved++;   // the STORE's own lookup: did the outer slot move?
            final = rs;
            if (st != NodeStatus.Running) OccurrenceStoreAccess.TryDetachSlot(world, self, outerKey);
            return st;
        }

        private static nint SlotAddress(EntityRepository world, Entity self, int key)
        {
            OccurrenceStoreAccess.TryFindSlot(world, self, key, out byte* blk, out int off, out _);
            return (nint)(blk + off);
        }

        /// <summary>How many slots (of any kind) the unit's store holds across ALL its blocks.</summary>
        public static int SlotCount(EntityRepository world, Entity self)
        {
            OccurrenceStoreAccess.GetBlocksReadOnly(world, self, out var blocks);
            int n = 0;
            for (int b = 0; b < blocks.Count; b++) n += BlueprintBlackboardPartitions.GetSlotCount(blocks.Memory(b));
            return n;
        }

        public static int BlockCount(EntityRepository world, Entity self)
        {
            OccurrenceStoreAccess.GetBlocksReadOnly(world, self, out var blocks);
            return blocks.Count;
        }

        public static bool Holds(EntityRepository world, Entity self, int key)
            => OccurrenceStoreAccess.TryFindSlotReadOnly(world, self, key, out _, out _, out _);
    }

    /// <summary>World / unit / step-list builders shared by the action rails.</summary>
    public static unsafe class ActionKit
    {
        public static EntityRepository NewWorld()
        {
            var w = TestWorldFactory.Create();
            w.RegisterComponent<ActionTestLog>();
            w.RegisterComponent<ActionFailure>();
            w.RegisterManagedComponent<ActionArgs>();
            return w;
        }

        /// <summary>A unit with a hand-built store (BlueprintBlackboard1024 raw, as scenario/test setup does) and the given step lists.</summary>
        public static Entity Unit(EntityRepository w, params string[] lists)
        {
            var e = w.CreateEntity();
            w.AddComponent(e, new ActionTestLog());
            w.AddComponent(e, new ActionFailure { Step = -1 });
            w.SetManagedComponent(e, new ActionArgs(lists));
            w.AddComponent(e, new BlueprintBlackboard1024());
            ref var tier = ref w.GetComponentRW<BlueprintBlackboard1024>(e);
            fixed (byte* mem = tier.Memory)
                BlueprintBlackboardPartitions.Initialize(mem, BlueprintBlackboard1024.TotalSize, (byte)BlueprintBlackboard1024.MaxSlots);
            return e;
        }

        public static string Steps(params string[] steps) => "[" + string.Join(",", steps) + "]";
        public static string Step(string act, string args = "{}") => "{\"act\":\"" + act + "\",\"args\":" + args + "}";

        public static (WaitParams P, WaitState S) PeekWait(EntityRepository w, Entity e, int depth = 0)
        {
            var (p, s) = ActionCatalogue.RunnerFor("TestWaitTicks").Peek(w, e, StepRunner.SlotKey(depth));
            return ((WaitParams)p, (WaitState)s);
        }
    }
}
