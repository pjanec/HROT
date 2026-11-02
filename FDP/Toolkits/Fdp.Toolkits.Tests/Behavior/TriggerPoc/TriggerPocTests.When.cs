using System;
using System.Collections.Generic;
using System.Text.Json;
using Fbt;
using Fdp.Toolkit.ScenarioRules.Actions;
using Fdp.Toolkit.ScenarioRules.Tests.Actions;
using Fdp.Core;
using Fdp.Toolkit.Blueprints.Components;
using Fdp.Toolkit.Blueprints.Partitioning;
using Fdp.Toolkit.ReplayBrowser.Search;
using StructEdit.Reflection;
using Xunit;

namespace Fdp.Toolkit.Behavior.Tests.TriggerPoc
{
    /// <summary>A DTO the production compiler does NOT know — stands for the proposed <c>OnEntity {Ref, Predicate}</c>.</summary>
    public sealed class PocOnEntityDto : SearchPredicateDto
    {
        public string Ref { get; set; } = "";
        public SearchPredicateDto Predicate { get; set; } = null!;
    }

    public sealed unsafe partial class TriggerPocTests
    {
        // ══ P5 — THE WHEN ════════════════════════════════════════════════════════════════════════════════════════════

        private const string SignalAbove5 =
            "{\"$type\":\"PropertyMatch\",\"componentType\":\"PocSignal\",\"propertyPath\":\"Value\",\"operator\":\"GreaterThan\"," +
            "\"predicate\":{\"$type\":\"Numeric\",\"minValue\":5}}";
        private const string SignalBAbove5 =
            "{\"$type\":\"PropertyMatch\",\"componentType\":\"PocSignalB\",\"propertyPath\":\"Value\",\"operator\":\"GreaterThan\"," +
            "\"predicate\":{\"$type\":\"Numeric\",\"minValue\":5}}";
        private static string And(params string[] parts) => "{\"$type\":\"Compound\",\"operator\":\"And\",\"conditions\":[" + string.Join(",", parts) + "]}";

        private static readonly IPredicateCompiler Compiler = new PredicateCompiler(new ComponentEditServiceBuilder().Build());

        /// <summary>JSON → SearchPredicateDto → ONE compiled delegate (what a scenario load would do once).</summary>
        private static Func<EntityRepository, Entity, bool> CompileWhen(string json)
        {
            var dto = JsonSerializer.Deserialize<SearchPredicateDto>(json, Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed)!;
            return Compiler.CompileComponentPredicate(dto);
        }

        private static Entity TriggerUnit(EntityRepository w, string list, int signal = 0, int signalB = 0)
        {
            var e = Unit(w, list);
            w.AddComponent(e, new PocTriggerState());
            w.AddComponent(e, new PocSignal { Value = signal });
            w.AddComponent(e, new PocSignalB { Value = signalB });
            return e;
        }

        private static void SetSignal(EntityRepository w, Entity e, int v) => w.GetComponentRW<PocSignal>(e).Value = v;

        private static readonly string RecordOne = Steps(Step("TestRecord", "{\"value\":1}"));

        [Fact]
        public void P5_AJsonPredicate_IsCompiledOnce_AndEvaluatesCheaply_PropertyMatchAndCompoundAnd()
        {
            using var w = NewWorld();
            var e = TriggerUnit(w, RecordOne);
            var one = CompileWhen(SignalAbove5);
            var both = CompileWhen(And(SignalAbove5, SignalBAbove5));

            Assert.False(one(w, e));
            SetSignal(w, e, 10);
            Assert.True(one(w, e));
            Assert.False(both(w, e));                                           // B still 0
            w.GetComponentRW<PocSignalB>(e).Value = 9;
            Assert.True(both(w, e));

            for (int i = 0; i < 100; i++) both(w, e);                           // warm
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++) both(w, e);
            long perEval = (GC.GetAllocatedBytesForCurrentThread() - before) / 1000;
            Assert.True(perEval <= 0, $"a compiled Compound-And evaluation allocated {perEval} B per call");   // measured number goes in the report
        }

        [Fact]
        public void P5_TheTrigger_FiresOnTheRisingEdgeOnly_NotWhileTheConditionStaysTrue()
        {
            using var w = NewWorld();
            var e = TriggerUnit(w, RecordOne);
            var trig = new PocTrigger(CompileWhen(SignalAbove5), delaySeconds: 0, once: false, listRef: 0);   // repeat, so "not while true" is observable
            double t = 0;
            void Tick() { trig.Tick(w, e, t); t += 0.1; }

            Tick(); Tick(); Tick();                                              // false ×3
            Assert.Equal(0, w.GetComponentRO<PocTriggerState>(e).Fired);
            SetSignal(w, e, 10);
            Tick();                                                              // the edge
            Assert.Equal(1, w.GetComponentRO<PocTriggerState>(e).Fired);
            Tick(); Tick(); Tick(); Tick(); Tick();                              // true ×5 more: NO re-fire
            Assert.Equal(1, w.GetComponentRO<PocTriggerState>(e).Fired);         // ← red-proof: fire on `cond` instead of `cond && Prev==0` ⇒ Fired climbs
            Assert.Equal(1, w.GetComponentRO<ActionTestLog>(e).Count);

            SetSignal(w, e, 0); Tick(); SetSignal(w, e, 10); Tick();             // drop then rise again ⇒ a second edge
            Assert.Equal(2, w.GetComponentRO<PocTriggerState>(e).Fired);
            Assert.Equal(2, w.GetComponentRO<ActionTestLog>(e).Count);
        }

        [Fact]
        public void P5_AConditionAlreadyTrueWhenTheTriggerArms_IsNotAnEdge_UntilItDropsAndRises()
        {
            using var w = NewWorld();
            var e = TriggerUnit(w, RecordOne, signal: 10);
            var trig = new PocTrigger(CompileWhen(SignalAbove5), 0, once: true, 0);
            trig.Tick(w, e, 0); trig.Tick(w, e, 0.1); trig.Tick(w, e, 0.2);
            Assert.Equal(0, w.GetComponentRO<PocTriggerState>(e).Fired);         // ⚠ a design CHOICE the T-rules do not state (arm-on-true): pinned here
            SetSignal(w, e, 0); trig.Tick(w, e, 0.3); SetSignal(w, e, 10); trig.Tick(w, e, 0.4);
            Assert.Equal(1, w.GetComponentRO<PocTriggerState>(e).Fired);
        }

        [Fact]
        public void P5_TheDelay_IsInSimSeconds_FromTheEdge_AndSurvivesTheConditionDropping()
        {
            using var w = NewWorld();
            var e = TriggerUnit(w, RecordOne);
            var trig = new PocTrigger(CompileWhen(SignalAbove5), delaySeconds: 0.5, once: true, 0);
            int Count() => w.GetComponentRO<ActionTestLog>(e).Count;

            trig.Tick(w, e, 0.9);                                                // arm (false)
            SetSignal(w, e, 10);
            trig.Tick(w, e, 1.0);                                                // the EDGE at t=1.0
            SetSignal(w, e, 0);                                                  // … it drops straight away: the delay still runs
            for (double t = 1.1; t < 1.45; t += 0.1) { trig.Tick(w, e, t); Assert.Equal(0, Count()); }
            trig.Tick(w, e, 1.5);                                                // 1.0 + 0.5
            Assert.Equal(1, Count());                                            // ← red-proof: delay compare off by one tick (> instead of >=) or from `now`
            Assert.Equal(1, w.GetComponentRO<PocTriggerState>(e).Fired);
        }

        [Fact]
        public void P5_Once_FiresOneTime_EvenAcrossLaterEdges_AndReportsTheListsResult()
        {
            using var w = NewWorld();
            var e = TriggerUnit(w, Steps(Step("TestWaitTicks", "{\"ticks\":2}"), Step("TestRecord", "{\"value\":4}")));
            var trig = new PocTrigger(CompileWhen(SignalAbove5), 0, once: true, 0);
            double t = 0;
            trig.Tick(w, e, t += 0.1);
            SetSignal(w, e, 10);
            var seen = new List<NodeStatus>();
            for (int i = 0; i < 3; i++) seen.Add(trig.Tick(w, e, t += 0.1));    // edge+wait1 · wait2+record · done
            Assert.Equal(NodeStatus.Success, seen[^1]);
            Assert.Equal(1, w.GetComponentRO<PocTriggerState>(e).Result);
            for (int k = 0; k < 3; k++) { SetSignal(w, e, 0); trig.Tick(w, e, t += 0.1); SetSignal(w, e, 10); trig.Tick(w, e, t += 0.1); }
            Assert.Equal(1, w.GetComponentRO<PocTriggerState>(e).Fired);
            Assert.Equal(1, w.GetComponentRO<ActionTestLog>(e).Count);
        }

        [Fact]
        public void P5_AFailingActionList_EndsTheTriggerWithFailureResult()
        {
            using var w = NewWorld();
            var e = TriggerUnit(w, Steps(Step("TestFail", "{\"code\":3}")));
            var trig = new PocTrigger(CompileWhen(SignalAbove5), 0, once: true, 0);
            trig.Tick(w, e, 0); SetSignal(w, e, 10);
            Assert.Equal(NodeStatus.Failure, trig.Tick(w, e, 0.1));
            Assert.Equal(2, w.GetComponentRO<PocTriggerState>(e).Result);
        }

        [Fact]
        public void P5_OnEntity_RetargetsTheCompiledChildAtAnotherEntity_AndAndsWithALocalCondition()
        {
            using var w = NewWorld();
            var self = TriggerUnit(w, RecordOne, signal: 0, signalB: 9);        // the trigger entity: its own B is already high
            var other = w.CreateEntity(); w.AddComponent(other, new PocSignal { Value = 0 });
            var refs = new PocRefTable(); refs.Set("A", other);

            var onA = PocOnEntity.Wrap(refs, "A", CompileWhen(SignalAbove5));   // "@A.Signal > 5"
            var local = CompileWhen(SignalBAbove5);                              // "self.SignalB > 5"
            var both = PocOnEntity.All(onA, local);

            Assert.False(onA(w, self));
            Assert.True(local(w, self));
            w.GetComponentRW<PocSignal>(other).Value = 10;                       // the OTHER entity changes
            Assert.True(onA(w, self));
            Assert.True(both(w, self));
            Assert.False(PocOnEntity.Wrap(refs, "Missing", CompileWhen(SignalAbove5))(w, self));   // unresolved ref ⇒ false, no throw
            w.DestroyEntity(other);
            Assert.False(onA(w, self));                                          // dead target ⇒ false, no throw

            // and as the trigger's `when`: fires when A rises
            var w2 = NewWorld();
            var s2 = TriggerUnit(w2, RecordOne, signalB: 9);
            var o2 = w2.CreateEntity(); w2.AddComponent(o2, new PocSignal { Value = 0 });
            var r2 = new PocRefTable(); r2.Set("A", o2);
            var trig = new PocTrigger(PocOnEntity.All(PocOnEntity.Wrap(r2, "A", CompileWhen(SignalAbove5)), CompileWhen(SignalBAbove5)), 0, once: true, 0);
            trig.Tick(w2, s2, 0); trig.Tick(w2, s2, 0.1);
            Assert.Equal(0, w2.GetComponentRO<PocTriggerState>(s2).Fired);
            w2.GetComponentRW<PocSignal>(o2).Value = 10;
            trig.Tick(w2, s2, 0.2);
            Assert.Equal(1, w2.GetComponentRO<PocTriggerState>(s2).Fired);
            w2.Dispose();
        }

        [Fact]
        public void P5_CE4159_AnUnknownDtoNestedInACompound_IsALoudError()
        {
            // ⭐ CE-4159 — this rail was P5_FINDING_…_CompilesToConstantTrue_SILENTLY: PredicateCompiler's `default:` returned
            // `static (_, _) => true`, so the unknown DTO made the whole Compound true with no error. Now the default arm THROWS.
            using var w = NewWorld();
            var e = TriggerUnit(w, RecordOne, signal: 0);
            var childFalse = new PropertyMatchDto
            {
                ComponentType = typeof(PocSignal), PropertyPath = "Value", Operator = SearchOperator.GreaterThan,
                Predicate = new NumericPredicateDto { MinValue = 5 },
            };
            var nested = new CompoundPredicateDto
            {
                Operator = LogicalOperator.Or,
                Conditions = new List<SearchPredicateDto> { new PocOnEntityDto { Ref = "A", Predicate = childFalse } },
            };
            Assert.False(Compiler.CompileComponentPredicate(childFalse)(w, e));          // the child alone is false …
            var ex = Assert.Throws<NotSupportedException>(() => Compiler.CompileComponentPredicate(nested));   // … wrapped in an unknown DTO: refused
            Assert.Contains(nameof(PocOnEntityDto), ex.Message);
            Assert.Throws<NotSupportedException>(() => Compiler.CompileComponentPredicate(new PocOnEntityDto()));   // … and on its own
        }

        [Fact]
        public void P5_FINDING_AnUnregisteredDerivedDto_CannotBeSerialized_ItNeedsAJsonDerivedType()
        {
            // SearchPredicateDto.cs:35-50 — the polymorphic list is a closed attribute list: a new DTO needs a `[JsonDerivedType]` there.
            var dto = new CompoundPredicateDto { Conditions = new List<SearchPredicateDto> { new PocOnEntityDto { Ref = "A" } } };
            Assert.ThrowsAny<NotSupportedException>(() => JsonSerializer.Serialize<SearchPredicateDto>(dto, Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed));
        }

        [Fact]
        public void P5_TheTriggersStateRef_HeldAcrossAStoreBlockAppend_StaysValid()
        {
            // the trigger driver holds `ref PocTriggerState` while ActionHost.Tick may AddComponent(a bigger block) on the SAME entity
            using var w = NewWorld();
            var e = w.CreateEntity();
            w.AddComponent(e, new ActionTestLog()); w.AddComponent(e, new ActionFailure { Step = -1 }); w.AddComponent(e, new PocTriggerState());
            w.AddComponent(e, new PocSignal { Value = 0 });
            w.SetManagedComponent(e, new ActionArgs(new[] { Steps(Step("TestFat", "{\"n\":8}"), Step("TestRecord", "{\"value\":1}")) }));
            w.AddComponent(e, new BlueprintBlackboard256());                     // 256 only ⇒ the fat step MUST append a block
            ref var tier = ref w.GetComponentRW<BlueprintBlackboard256>(e);
            fixed (byte* mem = tier.Memory)
                BlueprintBlackboardPartitions.Initialize(mem, BlueprintBlackboard256.TotalSize, (byte)BlueprintBlackboard256.MaxSlots);

            var trig = new PocTrigger(CompileWhen(SignalAbove5), 0, once: true, 0);
            trig.Tick(w, e, 0); SetSignal(w, e, 10);
            Assert.Equal(NodeStatus.Success, trig.Tick(w, e, 0.1));
            Assert.True(ActionHost.BlockCount(w, e) >= 2, "the append was not reached");
            var st = w.GetComponentRO<PocTriggerState>(e);
            Assert.Equal((4, 1, 1), (st.Phase, st.Result, st.Fired));            // written through the ref held ACROSS the append
        }
    }
}
