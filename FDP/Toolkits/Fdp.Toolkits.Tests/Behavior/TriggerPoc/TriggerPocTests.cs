using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Fbt;
using Fbt.Compiler;
using Fbt.Runtime;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Behavior.Systems;
using Fdp.Toolkit.Blueprints.Components;
using Fdp.Toolkit.Blueprints.Partitioning;
using System.Runtime.CompilerServices;
using Xunit;

namespace Fdp.Toolkit.Behavior.Tests.TriggerPoc
{
    /// <summary>
    /// ⭐ TRIGGER ACTION-SEQUENCE PoC (SR-93..98, docs/DESIGN_Scenario_Rules.md). Tests ASSUMPTIONS of the design; a red/false finding is a RESULT.
    /// P1 runtime binding · P2 per-step slot · P3 the loop as a shared action · P4 called from a real BTree · P5 the when (trigger.cs) · P6 restore.
    /// </summary>
    public sealed unsafe partial class TriggerPocTests
    {
        // ══ shared helpers ═══════════════════════════════════════════════════════════════════════════════════════════

        private static EntityRepository NewWorld()
        {
            var w = TestWorldFactory.Create();
            w.RegisterComponent<PocLog>();
            w.RegisterComponent<PocSignal>();
            w.RegisterComponent<PocSignalB>();
            w.RegisterComponent<PocTriggerState>();
            w.RegisterComponent<PocLastReason>();
            w.RegisterManagedComponent<PocActionArgs>();
            return w;
        }

        /// <summary>A unit with a hand-built store (BlueprintBlackboard1024 raw, as scenario/test setup does) and the given step lists.</summary>
        private static Entity Unit(EntityRepository w, params string[] lists)
        {
            var e = w.CreateEntity();
            w.AddComponent(e, new PocLog());
            w.AddComponent(e, new PocLastReason());
            w.SetManagedComponent(e, new PocActionArgs { Items = lists });
            w.AddComponent(e, new BlueprintBlackboard1024());
            ref var tier = ref w.GetComponentRW<BlueprintBlackboard1024>(e);
            fixed (byte* mem = tier.Memory)
                BlueprintBlackboardPartitions.Initialize(mem, BlueprintBlackboard1024.TotalSize, (byte)BlueprintBlackboard1024.MaxSlots);
            return e;
        }

        private static string Steps(params string[] steps) => "[" + string.Join(",", steps) + "]";
        private static string Step(string act, string args = "{}") => "{\"act\":\"" + act + "\",\"args\":" + args + "}";

        private static (WaitParams P, WaitState S) PeekWait(EntityRepository w, Entity e)
        {
            var (p, s) = PocCatalogue.RunnerFor("PocWaitTicks").Peek(w, e, PocActions.StepSlotKey);
            return ((WaitParams)p, (WaitState)s);
        }

        // ══ P1 — RUNTIME BINDING ═════════════════════════════════════════════════════════════════════════════════════

        [Fact]
        public void P1_TheRuntimeCatalogue_FindsTheSharedActions_ByShortNameAndFqn_WithTheirTypes()
        {
            Assert.True(PocCatalogue.TryGet("PocWaitTicks", out var e));
            Assert.Equal(typeof(WaitParams), e.ParamsType);
            Assert.Equal(typeof(WaitState), e.StateType);
            Assert.True(PocCatalogue.TryGet(e.Fqn, out var byFqn));
            Assert.Same(e, byFqn);
            Assert.True(PocCatalogue.TryGet("PocRunActions", out var run));     // the loop is itself in the catalogue
            Assert.Equal(typeof(RunParams), run.ParamsType);
            Assert.False(PocCatalogue.TryGet("NoSuchAction", out _));
        }

        [Fact]
        public void P1_ADelegateIsBuiltOncePerActionType_AndASecondEntityHitsTheCache()
        {
            PocCatalogue.ResetRunnerCache();
            using var w = NewWorld();
            var a = Unit(w); var b = Unit(w);

            var ra = PocCatalogue.RunnerFor("PocWaitTicks");
            ra.Tick(w, a, PocActions.StepSlotKey, "{\"ticks\":5}");
            var rb = PocCatalogue.RunnerFor("PocWaitTicks");
            rb.Tick(w, b, PocActions.StepSlotKey, "{\"ticks\":2}");

            Assert.Same(ra, rb);
            Assert.Equal(1, PocCatalogue.RunnersBuilt);                       // ← the cache-hit rail (mutation: build a new runner per call → 2)
            PocCatalogue.RunnerFor("PocRecord");
            Assert.Equal(2, PocCatalogue.RunnersBuilt);                       // a different action TYPE builds its own
        }

        [Fact]
        public void P1_CallingTheDelegate_MovesTheRightBytes_ForEachEntity()
        {
            using var w = NewWorld();
            var a = Unit(w); var b = Unit(w);
            var run = PocCatalogue.RunnerFor("PocWaitTicks");

            Assert.Equal(NodeStatus.Running, run.Tick(w, a, PocActions.StepSlotKey, "{\"ticks\":3}"));
            Assert.Equal(NodeStatus.Running, run.Tick(w, a, PocActions.StepSlotKey, "{\"ticks\":3}"));
            Assert.Equal(NodeStatus.Running, run.Tick(w, b, PocActions.StepSlotKey, "{\"ticks\":9}"));

            var (pa, sa) = PeekWait(w, a); var (pb, sb) = PeekWait(w, b);
            Assert.Equal((3, 2), (pa.Ticks, sa.Elapsed));
            Assert.Equal((9, 1), (pb.Ticks, sb.Elapsed));                      // b untouched by a's two ticks
            Assert.Equal(NodeStatus.Success, run.Tick(w, a, PocActions.StepSlotKey, "{\"ticks\":3}"));
        }

        [Fact]
        public void P1_AnUnknownActionName_IsALoudRefusal_NotASilentSkip()
        {
            var ex = Assert.Throws<InvalidOperationException>(() => PocCatalogue.RunnerFor("Nope"));
            Assert.Contains("unknown action", ex.Message);
        }

        // ══ P2 — PER-STEP SLOT ═══════════════════════════════════════════════════════════════════════════════════════

        [Fact]
        public void P2_ParamsArriveIntact_FromJson_IncludingFixedStringEnumFloat()
        {
            using var w = NewWorld();
            var e = Unit(w);
            var run = PocCatalogue.RunnerFor("PocRich");
            run.Tick(w, e, PocActions.StepSlotKey, "{\"name\":\"alpha\",\"mode\":\"Fast\",\"gain\":1.5,\"n\":7}");
            var (p, _) = run.Peek(w, e, PocActions.StepSlotKey);
            var rp = (RichParams)p;
            Assert.Equal("alpha", rp.Name.ToString());
            Assert.Equal(PocMode.Fast, rp.Mode);
            Assert.Equal(1.5f, rp.Gain);
            Assert.Equal(7, rp.N);
            Assert.Equal(7, w.GetComponentRO<PocLog>(e).V0);
        }

        [Fact]
        public void P2_AbsentJsonKeys_KeepTheStructsFieldInitialisers_NotZero()
        {
            using var w = NewWorld();
            var e = Unit(w);
            var run = PocCatalogue.RunnerFor("PocWaitTicks");
            run.Tick(w, e, PocActions.StepSlotKey, "{}");                      // no "ticks" ⇒ default(0) here; Elapsed 1 ≥ 0 ⇒ Success
            Assert.Equal(0, PeekWait(w, e).P.Ticks);
        }

        [Fact]
        public void P2_FINDING_AnEntityField_IsSILENTLY_NotSeededFromJson_ItStaysNull()
        {
            // ⛔ Entity is `readonly struct` (readonly fields, two ctors) ⇒ System.Text.Json writes {Index,Generation} but cannot READ them back:
            //   no error, the field is just Entity.Null. ⇒ an entity-valued arg must travel as a RefKey/GUID and be RESOLVED at step start.
            using var w = NewWorld();
            var e = Unit(w);
            var target = w.CreateEntity();
            string json = System.Text.Json.JsonSerializer.Serialize(new { target }, Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed);
            Assert.Contains("Index", json);                                     // the writer DID emit it
            var run = PocCatalogue.RunnerFor("PocRich");
            run.Tick(w, e, PocActions.StepSlotKey, json);
            var rp = (RichParams)run.Peek(w, e, PocActions.StepSlotKey).Params;
            Assert.Equal(Entity.Null, rp.Target);                               // … and the reader dropped it, silently
            Assert.NotEqual(target, rp.Target);
        }

        [Fact]
        public void P2_BadArgsJson_LeavesNoHalfSeededSlot_AndThrows()
        {
            using var w = NewWorld();
            var e = Unit(w);
            var run = PocCatalogue.RunnerFor("PocRich");
            Assert.ThrowsAny<Exception>(() => run.Tick(w, e, PocActions.StepSlotKey, "{\"mode\":\"Warp9\"}"));   // strict enum
            Assert.False(PocOuter.Holds(w, e, PocActions.StepSlotKey));
            Assert.ThrowsAny<Exception>(() => run.Tick(w, e, PocActions.StepSlotKey, "{not json"));
            Assert.False(PocOuter.Holds(w, e, PocActions.StepSlotKey));
        }

        [Fact]
        public void P2_OnlyOneStepSlotExists_AtATime_AndNoneAfterTheEnd()
        {
            using var w = NewWorld();
            var e = Unit(w, Steps(Step("PocRecord", "{\"value\":1}"), Step("PocWaitTicks", "{\"ticks\":3}"), Step("PocRecord", "{\"value\":2}")));
            var seen = new List<int>();
            NodeStatus st;
            do
            {
                st = PocOuter.Tick(w, e, 0, out _);
                seen.Add(PocOuter.SlotCount(w, e));
            } while (st == NodeStatus.Running);

            // Running ticks: outer + ONE step slot = 2 every time; terminal tick: both detached = 0
            Assert.Equal(new[] { 2, 2, 0 }, seen);
            Assert.False(PocOuter.Holds(w, e, PocActions.StepSlotKey));
            Assert.False(PocOuter.Holds(w, e, PocOuter.OuterSlotKey));
        }

        [Fact]
        public void P2_WorkingState_PersistsAcrossTicks_ParamsStayPut()
        {
            using var w = NewWorld();
            var e = Unit(w, Steps(Step("PocWaitTicks", "{\"ticks\":4}")));
            for (int i = 1; i <= 3; i++)
            {
                Assert.Equal(NodeStatus.Running, PocOuter.Tick(w, e, 0, out _));
                var (p, s) = PeekWait(w, e);
                Assert.Equal((4, i), (p.Ticks, s.Elapsed));
            }
        }

        [Fact]
        public void P2_DetachThenReattachOfTheNextStep_DoesNotCorruptOrLeakStaleBytes()
        {
            using var w = NewWorld();
            // step 0 is WIDE (RichParams), step 1 is narrow: it reuses the freed bytes; its working state must start ZERO and its params be its own
            var e = Unit(w, Steps(Step("PocRich", "{\"n\":41,\"gain\":9.5,\"name\":\"zzzz\"}"), Step("PocWaitTicks", "{\"ticks\":2}"), Step("PocRecord", "{\"value\":3}")));
            Assert.Equal(NodeStatus.Running, PocOuter.Tick(w, e, 0, out _));   // rich done+detached, wait attached into the freed bytes, Elapsed=1
            var (p, s) = PeekWait(w, e);
            Assert.Equal((2, 1), (p.Ticks, s.Elapsed));
            Assert.Equal(NodeStatus.Success, PocOuter.Tick(w, e, 0, out var fin));
            Assert.Equal(3, fin.Index);
            var log = w.GetComponentRO<PocLog>(e);
            Assert.Equal((2, 41, 3), (log.Count, log.V0, log.V1));
        }

        [Fact]
        public void P2_TRAP_TheOuterWorkingStateRef_StaysValid_WhileTheInnerSlotIsAttachedAndDetached_SameBlock()
        {
            PocTrap.Reset();
            using var w = NewWorld();
            var e = Unit(w, Steps(Step("PocRecord", "{\"value\":1}"), Step("PocRich", "{\"n\":2}"), Step("PocWaitTicks", "{\"ticks\":2}")));
            Assert.Equal(NodeStatus.Running, PocOuter.Tick(w, e, 0, out _));   // one call: 2 attaches + 2 detaches, the outer ref held throughout
            Assert.True(PocTrap.Checks >= 3);
            Assert.Equal(0, PocTrap.Moved);                                    // ← red-proof for P2: make PocRunActions re-resolve... see report (mutation: Moved++ forced)
            Assert.Equal(1, PocOuter.BlockCount(w, e));                        // it really was the same-block case
        }

        [Fact]
        public void P2_TRAP_TheOuterRef_StaysValid_WhenTheStepDoesNotFitTheFirstBlock_AppendPath()
        {
            PocTrap.Reset();
            using var w = NewWorld();
            var e = w.CreateEntity();
            w.AddComponent(e, new PocLog()); w.AddComponent(e, new PocLastReason());
            w.SetManagedComponent(e, new PocActionArgs { Items = new[] { Steps(Step("PocRecord", "{\"value\":1}"), Step("PocRich", "{\"n\":2}"), Step("PocWaitTicks", "{\"ticks\":2}")) } });
            w.AddComponent(e, new BlueprintBlackboard256());                    // ONLY the smallest tier …
            ref var tier = ref w.GetComponentRW<BlueprintBlackboard256>(e);
            fixed (byte* mem = tier.Memory)
                BlueprintBlackboardPartitions.Initialize(mem, BlueprintBlackboard256.TotalSize, (byte)BlueprintBlackboard256.MaxSlots);

            // the outer first (it lands in block 0) …
            ref RunState probe = ref OccurrenceWorkingState.ResolveOrAttach<RunParams, RunState>(w, e, PocOuter.OuterSlotKey, 0x0C0FFEE, OccurrenceKind.Hsm, out _, out RunParams* pp);
            pp->ListRef = 0;
            OccurrenceStoreAccess.TryFindSlot(w, e, PocOuter.OuterSlotKey, out byte* blk0, out int outerOff, out _);
            nint outerAddr = (nint)(blk0 + outerOff);
            // … then FILL block 0 (raw TryAttach: never appends) so every step slot MUST append a bigger block
            for (int k = 0; BlueprintBlackboardPartitions.TryAttach(blk0, 0x6000 + k, 16, 0x1, OccurrenceKind.UnitMemory, out _); k++) { }
            Assert.Equal(1, PocOuter.BlockCount(w, e));

            Assert.Equal(NodeStatus.Running, PocOuter.Tick(w, e, 0, out _));    // 2 steps done + the wait started, all inside one outer call
            Assert.Equal(2, PocOuter.BlockCount(w, e));                         // the append DID happen (mid-tick, inside the outer's call)
            OccurrenceStoreAccess.TryFindSlot(w, e, PocOuter.OuterSlotKey, out byte* blkNow, out int offNow, out _);
            Assert.Equal(outerAddr, (nint)(blkNow + offNow));                   // the outer slot did not move
            NodeStatus st = PocOuter.Tick(w, e, 0, out _);
            Assert.Equal(NodeStatus.Success, st);
            Assert.True(PocTrap.Checks >= 3);
            Assert.Equal(0, PocTrap.Moved);                                     // the outer ref never moved
            var log = w.GetComponentRO<PocLog>(e);
            Assert.Equal((2, 1, 2), (log.Count, log.V0, log.V1));               // every step ran, once
        }

        [Fact]
        public void P2_TRAP_AppendingABlock_DoesNotMoveTheOuterBlock_AndAWriteThroughTheOldRefLands()
        {
            using var w = NewWorld();
            var e = w.CreateEntity();
            w.AddComponent(e, new PocLog()); w.AddComponent(e, new PocLastReason());
            w.AddComponent(e, new BlueprintBlackboard256());
            ref var tier = ref w.GetComponentRW<BlueprintBlackboard256>(e);
            fixed (byte* mem = tier.Memory)
                BlueprintBlackboardPartitions.Initialize(mem, BlueprintBlackboard256.TotalSize, (byte)BlueprintBlackboard256.MaxSlots);

            ref RunState outer = ref OccurrenceWorkingState.ResolveOrAttach<RunParams, RunState>(
                w, e, PocOuter.OuterSlotKey, 1, OccurrenceKind.Hsm, out _, out _);
            nint before = (nint)Unsafe.AsPointer(ref outer);
            outer.Index = 5;

            // exhaust block 0, forcing an APPEND (a new 1024 block via AddComponent — a structural change on the entity)
            int appended = 0;
            for (int k = 0; k < 40 && appended == 0; k++)
            {
                OccurrenceStoreAccess.TryAttachSlot(w, e, 0x6000 + k, 48, 0x2, OccurrenceKind.UnitMemory, out _, out _);
                if (PocOuter.BlockCount(w, e) > 1) appended = k + 1;
            }
            Assert.True(appended > 0, "the append path was not reached");

            OccurrenceStoreAccess.TryDetachSlot(w, e, 0x6000);                  // and a detach in the first block
            Assert.Equal(before, (nint)Unsafe.AsPointer(ref outer));            // by definition (same ref) …
            OccurrenceStoreAccess.TryFindSlot(w, e, PocOuter.OuterSlotKey, out byte* blk, out int off, out _);
            Assert.Equal(before, (nint)(blk + off));                            // … and the store's OWN lookup still says the same address ⇒ nothing moved
            outer.Index = 77;                                                   // write through the OLD ref
            Assert.Equal(77, Unsafe.AsRef<RunState>(blk + off).Index);          // lands in the live slot
        }
    }
}
