using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Fbt;
using Fbt.Kernel;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.ScenarioRules;
using Fdp.Toolkit.ScenarioRules.Actions;
using Hrot.AI.Behaviors.Brains.Objectives;
using Hrot.Map.Definitions.Behavior;
using Fdp.Toolkit.Replication.Components;
using Xunit;

namespace Hrot.SimHost.Tests
{
    public struct TrgRecordParams { public int Value; }
    public struct TrgFailParams { public int Code; }
    public struct TrgNoState { public int Pad; }
    public struct TrgWaitParams { public int Ticks; }
    public struct TrgWaitState { public int Elapsed; }

    /// <summary>⭐ rules S2-c — the test vocabulary of the trigger rails: an action that appends to a log, and one that fails with a reason.
    /// (A thread-static log: the brain tick runs on the test's own thread.)</summary>
    public static class TrgActions
    {
        [ThreadStatic] public static List<int>? Log;
        public static List<int> Calls => Log ??= new List<int>();
        public static void Reset() => Log = new List<int>();

        [SharedAiAction]
        public static NodeStatus TrgRecord(ref TrgRecordParams p, ref TrgNoState s, Entity self, EntityRepository world)
        {
            if (s.Pad == 0) { Calls.Add(p.Value); s.Pad = 1; }   // once per step run
            return NodeStatus.Success;
        }

        /// <summary>Runs for <c>Ticks</c> ticks, then succeeds (a step that is "running" for a while).</summary>
        [SharedAiAction]
        public static NodeStatus TrgWait(ref TrgWaitParams p, ref TrgWaitState s, Entity self, EntityRepository world)
        {
            s.Elapsed++;
            return s.Elapsed >= p.Ticks ? NodeStatus.Success : NodeStatus.Running;
        }

        [SharedAiAction]
        public static NodeStatus TrgFail(ref TrgFailParams p, ref TrgNoState s, Entity self, EntityRepository world)
        {
            ActionFailure.Set(world, self, "code " + p.Code);
            return NodeStatus.Failure;
        }
    }

    /// <summary>
    /// ⭐ rules S2-c (📄 docs/DESIGN_Scenario_Rules.md §10.11 T1–T10, §00.9) — the <c>Trigger</c> behaviour, through the real load path
    /// (<c>BehaviorIngressSystem</c> assignment → <c>BrainTickSystem</c>). The condition is an <c>OnEntity</c> reading another entity's
    /// <c>ObjectiveStatus.State == Achieved</c> (a plain entity the test flips); the action is <see cref="TrgActions.TrgRecord"/>, which appends to a log.
    /// Re-homes the trigger-semantics rails of the retired PoC (edge · already-true · delay · once · failure · repeat) onto the production behaviour.
    /// </summary>
    public sealed partial class ScenarioRulesTests
    {
        private const string RecordSeven = "[{\"act\":\"TrgRecord\",\"args\":{\"value\":7}}]";

        private static string WhenAchieved(int net)
            => "{\"$type\":\"OnEntity\",\"ref\":" + net + ",\"predicate\":{\"$type\":\"PropertyMatch\",\"componentType\":\"ObjectiveStatus\",\"propertyPath\":\"State\"," +
               "\"operator\":\"Equals\",\"predicate\":{\"$type\":\"Numeric\",\"minValue\":2,\"maxValue\":2}}}";

        private static string TrigJson(int net, string extra = "", string doList = RecordSeven, string common = "\"title\":\"T\"")
            => "{\"common\":{" + common + "},\"when\":" + WhenAchieved(net) + ",\"do\":" + doList + extra + "}";

        /// <summary>The condition source: a plain entity carrying an <c>ObjectiveStatus</c> nobody ticks — its State is flipped by the rail.</summary>
        private static int s_srcSeq;
        private static Entity Source(ObjWorld w)
        {
            var e = w.Repo.CreateEntity();
            w.Repo.AddComponent(e, new NetworkIdentity { Value = 9000 + System.Threading.Interlocked.Increment(ref s_srcSeq) });
            w.Repo.AddComponent(e, new ObjectiveStatus { State = ObjectiveState.Active });
            return e;
        }

        private static void Flip(ObjWorld w, Entity src, bool up) => w.Repo.GetComponentRW<ObjectiveStatus>(src).State = up ? ObjectiveState.Achieved : ObjectiveState.Active;
        private static TriggerState Phase(ObjWorld w, Entity t) => w.S(t).TriggerState;

        /// <summary>T1 — the trigger fires on the RISING EDGE only (never while the condition stays true), reports Role Trigger / unscored (T8), and a repeating
        /// one fires again on the next edge. ✅ Red-proof: <c>cond && Prev == 0</c> relaxed to <c>cond</c> in <c>TriggerBehavior.Step</c> ⇒ the already-true rail below fires
        /// while the condition stays true ⇒ red.</summary>
        [Fact]
        public void ATrigger_FiresOnTheRisingEdge_OnceNotWhileTrue_AndAgainOnTheNextEdge()
        {
            TrgActions.Reset();
            var w = new ObjWorld(); var src = Source(w);
            var t = w.Trigger(TrigJson(w.Net(src), ",\"repeat\":true"));
            w.Run(2);
            Assert.Equal((ObjectiveRole.Trigger, ObjectiveState.Active, TriggerState.Armed, (byte)0), (w.S(t).Role, w.S(t).State, Phase(w, t), w.S(t).Scored));   // T8: scored defaults false
            Assert.Equal("Trigger", w.S(t).Kind.ToString());
            Assert.Empty(TrgActions.Calls);

            Flip(w, src, true); w.Run(0.5);                                   // the edge tick
            Assert.Equal(new[] { 7 }, TrgActions.Calls);
            Assert.Equal((TriggerState.Fired, 1), (Phase(w, t), w.S(t).FireCount));
            Assert.Equal(w.Time, w.S(t).LastFired);
            w.Run(3);                                                         // true for six more ticks: NO re-fire
            Assert.Equal(new[] { 7 }, TrgActions.Calls);
            Assert.Equal(TriggerState.Cooldown, Phase(w, t));                 // cooled down (0 s) but the condition is still true

            Flip(w, src, false); w.Run(0.5); Assert.Equal(TriggerState.Armed, Phase(w, t));
            Flip(w, src, true); w.Run(0.5);
            Assert.Equal(new[] { 7, 7 }, TrgActions.Calls);                   // a second edge, a second fire
            Assert.Equal(2, w.S(t).FireCount);
        }

        /// <summary>T1 / CE-4166 — a condition already TRUE when the trigger arms is NOT an edge: it stays quiet while true and fires after a drop and a rise; with
        /// <c>fireIfTrueWhenArmed</c> it fires at once. ✅ Red-proof: <c>PrevKnown</c> ignored on the first armed tick ⇒ the default case fires at arming ⇒ red.</summary>
        [Fact]
        public void AConditionAlreadyTrueWhenArmed_IsNotAnEdge_UnlessTheTriggerOptsIn()
        {
            TrgActions.Reset();
            var w = new ObjWorld(); var src = Source(w); Flip(w, src, true);
            var t = w.Trigger(TrigJson(w.Net(src)));
            w.Run(3);
            Assert.Empty(TrgActions.Calls);                                    // ⚠ a design CHOICE (SR-108): pinned here
            Assert.Equal(TriggerState.Armed, Phase(w, t));
            Flip(w, src, false); w.Run(0.5); Flip(w, src, true); w.Run(0.5);
            Assert.Equal(new[] { 7 }, TrgActions.Calls);
            w.Run(0.5);
            Assert.Equal((TriggerState.Done, ObjectiveState.Achieved), (Phase(w, t), w.S(t).State));   // the next tick: Fired → Done

            TrgActions.Reset();
            var w2 = new ObjWorld(); var src2 = Source(w2); Flip(w2, src2, true);
            var opted = w2.Trigger(TrigJson(w2.Net(src2), ",\"fireIfTrueWhenArmed\":true"));
            w2.Run(1);
            Assert.Equal(new[] { 7 }, TrgActions.Calls);                       // the opt-in: true at arming IS an edge
            Assert.Equal(1, w2.S(opted).FireCount);
        }

        /// <summary>T3 — <c>holdFor</c> debounces: a condition true for LESS than holdFor falls back to Armed and fires nothing; one that holds fires when the time is up.
        /// ✅ Red-proof: the <c>!cond</c> branch of <c>Holding</c> removed ⇒ the dropped condition still fires ⇒ red.</summary>
        [Fact]
        public void HoldFor_DebouncesTheCondition_ADropBeforeTheHoldReturnsToArmed()
        {
            TrgActions.Reset();
            var w = new ObjWorld(); var src = Source(w);
            var t = w.Trigger(TrigJson(w.Net(src), ",\"holdFor\":3"));
            w.Run(1);
            Flip(w, src, true); w.Run(0.5);
            Assert.Equal(TriggerState.Holding, Phase(w, t));
            w.Run(1.5);                                                        // true for 2 s of the 3
            Flip(w, src, false); w.Run(0.5);
            Assert.Equal(TriggerState.Armed, Phase(w, t));
            w.Run(5);
            Assert.Empty(TrgActions.Calls);

            Flip(w, src, true); w.Run(0.5);                                    // a new edge: a new hold
            Assert.Equal(TriggerState.Holding, Phase(w, t));
            w.Run(2.5);
            Assert.Empty(TrgActions.Calls);                                    // 2.5 s: not yet
            w.Run(0.5);                                                        // 3.0 s
            Assert.Equal(new[] { 7 }, TrgActions.Calls);
        }

        /// <summary>T3 — <c>delay</c>: it fires that long after the edge even though the condition dropped meanwhile (a scheduled consequence), and not before.
        /// ✅ Red-proof: the delay compare off by one tick (<c>&gt;</c> for <c>&gt;=</c>) ⇒ it fires a tick late ⇒ red.</summary>
        [Fact]
        public void TheDelay_FiresAfterTheEdge_EvenWhenTheConditionDropsMeanwhile()
        {
            TrgActions.Reset();
            var w = new ObjWorld(); var src = Source(w);
            var t = w.Trigger(TrigJson(w.Net(src), ",\"delay\":2"));
            w.Run(1);
            Flip(w, src, true); w.Run(0.5);
            Assert.Equal(TriggerState.Waiting, Phase(w, t));
            Flip(w, src, false);
            w.Run(1.0);                                                        // 1.0 s after the edge
            Assert.Equal((TriggerState.Waiting, 0), (Phase(w, t), TrgActions.Calls.Count));
            w.Run(0.5);                                                        // 1.5 s
            Assert.Empty(TrgActions.Calls);
            w.Run(0.5);                                                        // 2.0 s
            Assert.Equal(new[] { 7 }, TrgActions.Calls);
            Assert.Equal(TriggerState.Fired, Phase(w, t));
        }

        /// <summary>T2 — <c>repeat</c> + <c>cooldown</c> + <c>maxFires</c> 2: fires exactly twice. A cooldown that has elapsed does NOT re-arm while the condition is
        /// still true (it needs a new edge); a drop INSIDE the cooldown does not re-arm early. ✅ Red-proof: the <c>!cond</c> test removed from the <c>Cooldown</c> arm ⇒ a
        /// still-true condition fires again after the cooldown ⇒ red.</summary>
        [Fact]
        public void RepeatWithCooldownAndMaxFires_FiresExactlyTwice_AndRearmsOnlyAfterTheConditionWentFalse()
        {
            TrgActions.Reset();
            var w = new ObjWorld(); var src = Source(w);
            var t = w.Trigger(TrigJson(w.Net(src), ",\"repeat\":true,\"cooldown\":3,\"maxFires\":2"));
            w.Run(1);
            Flip(w, src, true); w.Run(0.5);                                    // fire 1
            Assert.Equal(1, TrgActions.Calls.Count);
            w.Run(0.5); Assert.Equal(TriggerState.Cooldown, Phase(w, t));
            Flip(w, src, false); w.Run(0.5);                                   // false INSIDE the cooldown (1.0 s of 3)
            Assert.Equal(TriggerState.Cooldown, Phase(w, t));
            Flip(w, src, true);
            w.Run(4);                                                          // the cooldown is long over and the condition is TRUE
            Assert.Equal((TriggerState.Cooldown, 1), (Phase(w, t), TrgActions.Calls.Count));
            Flip(w, src, false); w.Run(0.5);
            Assert.Equal(TriggerState.Armed, Phase(w, t));
            Flip(w, src, true); w.Run(0.5);                                    // fire 2
            Assert.Equal(2, TrgActions.Calls.Count);
            w.Run(0.5);                                                        // maxFires reached
            Assert.Equal((TriggerState.Done, ObjectiveState.Achieved, 2), (Phase(w, t), w.S(t).State, w.S(t).FireCount));
            for (int i = 0; i < 3; i++) { Flip(w, src, false); w.Run(5); Flip(w, src, true); w.Run(5); }
            Assert.Equal(2, TrgActions.Calls.Count);                           // exactly twice
        }

        /// <summary>T7 — a failing action stops the list, ends the trigger Fired + Failed, and its reason (step index and the action's own text) is the status Reason.
        /// ✅ Red-proof: the failure not read from <c>ActionFailure</c> ⇒ the Reason is generic ⇒ red.</summary>
        [Fact]
        public void AFailingAction_EndsTheTriggerFiredAndFailed_WithTheReason()
        {
            TrgActions.Reset();
            var w = new ObjWorld(); var src = Source(w);
            var t = w.Trigger(TrigJson(w.Net(src), doList: "[{\"act\":\"TrgRecord\",\"args\":{\"value\":1}},{\"act\":\"TrgFail\",\"args\":{\"code\":3}},{\"act\":\"TrgRecord\",\"args\":{\"value\":2}}]"));
            w.Run(1);
            Flip(w, src, true); w.Run(0.5);
            Assert.Equal(new[] { 1 }, TrgActions.Calls);                       // the third step never ran
            Assert.Equal((TriggerState.Fired, ObjectiveState.Failed, 1), (Phase(w, t), w.S(t).State, w.S(t).FireCount));
            Assert.Contains("step 1", w.S(t).Reason.ToString());
            Assert.Contains("code 3", w.S(t).Reason.ToString());
            w.Run(3); Flip(w, src, false); w.Run(1); Flip(w, src, true); w.Run(1);
            Assert.Equal((TriggerState.Fired, 1, 1), (Phase(w, t), w.S(t).FireCount, TrgActions.Calls.Count));   // final: it does not run again
        }

        /// <summary>⭐ rule editor ① — while Acting the status shows the running step (1-based) of the list's length: 1/3 on the first step, 2/3 on the wait, 3/3 on the last,
        /// 0/0 once Done. ✅ Red-proof: <c>StepCur</c> not written in <c>TriggerBehavior.Act</c> ⇒ red.</summary>
        [Fact]
        public void AnActingTrigger_ShowsItsStep_AndNothingWhenDone()
        {
            TrgActions.Reset();
            var w = new ObjWorld(); var src = Source(w);
            var t = w.Trigger(TrigJson(w.Net(src), doList: "[{\"act\":\"TrgRecord\",\"args\":{\"value\":1}},{\"act\":\"TrgWait\",\"args\":{\"ticks\":4}},{\"act\":\"TrgRecord\",\"args\":{\"value\":2}}]"));
            w.Run(1);
            Assert.Equal((0, 0), (w.S(t).StepCur, w.S(t).StepCount));          // armed: nothing
            Flip(w, src, true); w.Tick(0.5);
            Assert.Equal((TriggerState.Acting, 2, 3), (Phase(w, t), (int)w.S(t).StepCur, (int)w.S(t).StepCount));   // step 1 ran in the firing tick, now on the wait
            w.Tick(0.5);
            Assert.Equal((TriggerState.Acting, 2, 3), (Phase(w, t), (int)w.S(t).StepCur, (int)w.S(t).StepCount));
            w.Run(3);
            Assert.Equal(new[] { 1, 2 }, TrgActions.Calls);
            Assert.Equal((TriggerState.Done, 0, 0), (Phase(w, t), (int)w.S(t).StepCur, (int)w.S(t).StepCount));
        }

        /// <summary>⭐ rule editor ① — a failed step keeps its index (the reason is in Reason). ✅ Red-proof: the failure path zeroing <c>StepCur</c> ⇒ red.</summary>
        [Fact]
        public void AFailedStep_KeepsItsIndex()
        {
            TrgActions.Reset();
            var w = new ObjWorld(); var src = Source(w);
            var t = w.Trigger(TrigJson(w.Net(src), doList: "[{\"act\":\"TrgRecord\",\"args\":{\"value\":1}},{\"act\":\"TrgFail\",\"args\":{\"code\":3}},{\"act\":\"TrgRecord\",\"args\":{\"value\":2}}]"));
            w.Run(1);
            Flip(w, src, true); w.Run(0.5);
            Assert.Equal((TriggerState.Fired, ObjectiveState.Failed, 2, 3), (Phase(w, t), w.S(t).State, (int)w.S(t).StepCur, (int)w.S(t).StepCount));
        }

        /// <summary>T4 — an Inactive objective state DISARMS the trigger (the real <c>SetObjectiveActive</c> action is S2's next row — a direct write stands in): a rise while
        /// disarmed fires nothing; re-activated, it re-arms, and a condition already true then is again no edge. <c>startActive:false</c> starts Disarmed.
        /// ✅ Red-proof: the Inactive branch emptied in <c>Tick</c> ⇒ the phase stays Armed (and the deadline case is not Disarmed) ⇒ red.</summary>
        [Fact]
        public void AnInactiveTrigger_IsDisarmed_AndRearmsWhenReactivated()
        {
            TrgActions.Reset();
            var w = new ObjWorld(); var src = Source(w);
            var t = w.Trigger(TrigJson(w.Net(src)));
            w.Run(1);
            Assert.Equal(TriggerState.Armed, Phase(w, t));
            w.Repo.GetComponentRW<ObjectiveStatus>(t).State = ObjectiveState.Inactive;
            w.Run(0.5); Assert.Equal(TriggerState.Disarmed, Phase(w, t));
            Flip(w, src, true); w.Run(2);
            Assert.Empty(TrgActions.Calls);                                    // a disarmed trigger never fires
            w.Repo.GetComponentRW<ObjectiveStatus>(t).State = ObjectiveState.Active;
            w.Run(2);
            Assert.Equal((TriggerState.Armed, 0), (Phase(w, t), TrgActions.Calls.Count));   // true when re-armed: not an edge
            Flip(w, src, false); w.Run(0.5); Flip(w, src, true); w.Run(0.5);
            Assert.Single(TrgActions.Calls);

            var w2 = new ObjWorld(); var s2 = Source(w2);
            var off = w2.Trigger(TrigJson(w2.Net(s2), common: "\"title\":\"T\",\"startActive\":false"));
            w2.Run(1);
            Assert.Equal((TriggerState.Disarmed, ObjectiveState.Inactive), (Phase(w2, off), w2.S(off).State));

            TrgActions.Reset();
            var w3 = new ObjWorld(); var s3 = Source(w3);
            var late = w3.Trigger(TrigJson(w3.Net(s3), common: "\"title\":\"T\",\"deadlineSeconds\":3"));   // T4: its deadline disarms it
            w3.Run(2); Assert.Equal(TriggerState.Armed, Phase(w3, late));
            w3.Run(2);
            Flip(w3, s3, true); w3.Run(1);
            Assert.Equal((TriggerState.Disarmed, ObjectiveState.Inactive, 0), (Phase(w3, late), w3.S(late).State, TrgActions.Calls.Count));
        }

        /// <summary>CE-4163 — a bad <c>do</c> list FAILS ASSIGNMENT: an unknown act, args that do not read, a <c>when</c> that is missing or unknown — the behaviour never
        /// starts (the ingress drops it and logs it), no status is written, and the resolver's message names the step and the act. A good one writes
        /// <c>ActionArgs</c> [when, do]. ✅ Red-proof: the <c>ActionList.Validate</c> call removed from <c>Resolve</c> ⇒ the bad list is assigned ⇒ red.</summary>
        [Fact]
        public void ABadDoListOrWhen_FailsAssignment_NotTheFirstFiring()
        {
            var w = new ObjWorld(); var src = Source(w);
            int net = w.Net(src);
            var bad = new[]
            {
                TrigJson(net, doList: "[{\"act\":\"NoSuchAction\",\"args\":{}}]"),
                TrigJson(net, doList: "[{\"act\":\"TrgRecord\",\"args\":{\"nope\":1}}]"),
                TrigJson(net, doList: "{\"act\":\"TrgRecord\"}"),                                   // not an array
                "{\"common\":{\"title\":\"T\"},\"do\":" + RecordSeven + "}",                       // no when
                "{\"common\":{\"title\":\"T\"},\"when\":{\"$type\":\"Nope\"},\"do\":" + RecordSeven + "}",   // an unknown predicate kind
            };
            var entities = bad.Select(j => w.Trigger(j)).ToList();
            var good = w.Trigger(TrigJson(net));
            w.Run(2);
            foreach (var e in entities)
            {
                Assert.False(w.Repo.HasComponent<ObjectiveStatus>(e), "a refused trigger must write no status");
                Assert.False(w.Repo.HasManagedComponent<ActionArgs>(e));
                Assert.False(w.Registry.TryGetName(w.Repo.GetComponentRO<BehaviorState>(e).ActiveBehaviorHash, out var n) && n == "Trigger", "the refused assignment started the behaviour");
            }
            Assert.True(w.Repo.HasComponent<ObjectiveStatus>(good));
            var args = ((Fdp.ModuleHost.Abstractions.ISimulationView)w.Repo).GetManagedComponentRO<ActionArgs>(good).Items;
            Assert.Equal(2, args.Count);
            Assert.Contains("OnEntity", args[TriggerBehavior.WhenItem]);
            Assert.Contains("TrgRecord", args[TriggerBehavior.DoItem]);

            // the message that reaches the log names the step and the act
            var dto = new TriggerParamsJsonDto { When = System.Text.Json.JsonSerializer.Deserialize<Fdp.Toolkit.ReplayBrowser.Search.SearchPredicateDto>(WhenAchieved(net), BehaviorParams.JsonOptions),
                                                  Do = System.Text.Json.JsonSerializer.Deserialize<List<Fdp.Toolkit.ScenarioRules.Actions.ActionStep>>("[{\"act\":\"TrgRecord\",\"args\":{\"value\":1}},{\"act\":\"NoSuchAction\"}]", BehaviorParams.JsonOptions) };
            var bb = new TriggerBehavior.TriggerBlackboard();
            var ex = Assert.Throws<InvalidOperationException>(() => TriggerBehavior.Resolve(in dto, ref bb, w.Repo, good));
            Assert.Contains("step 1", ex.Message);
            Assert.Contains("NoSuchAction", ex.Message);
        }

        /// <summary>The compiled <c>when</c> is cached PROCESS-WIDE by its JSON string: two equal strings (different instances) give ONE delegate.</summary>
        [Fact]
        public void TheCompiledWhen_IsCachedByItsJsonString()
        {
            string a = WhenAchieved(4242), b = new string(a.AsSpan());
            Assert.NotSame(a, b);
            Assert.Same(TriggerWhen.Get(a), TriggerWhen.Get(b));
            Assert.NotSame(TriggerWhen.Get(a), TriggerWhen.Get(WhenAchieved(4243)));
        }

        /// <summary>⭐ SR-94 / SR-160 — a <c>do</c> step is a typed <c>BindableNode</c> (<c>ActionStep</c>): the ONE binder, run over the trigger's params JSON, writes a step's
        /// <c>bind</c> (<c>"args.text": "msg"</c>) into its args — the step's text IS the param's value; a step with no <c>bind</c> keeps its literal; a bind naming a missing
        /// param is LOUD (the binder's rule). The bound list reads back through the contract and the runtime's ONE serialisation (<c>ActionStep.ToJson</c>).
        /// ✅ Red-proof: the dotted-path arm of <c>ParamBind.Node</c> skipped ⇒ the step keeps its literal ⇒ red.</summary>
        [Fact]
        public void AStepsBind_IsAppliedByTheOneBinder_OverTheTriggersParams()
        {
            string json = TrigJson(1, "", "[{\"act\":\"Message\",\"args\":{\"text\":\"literal\"},\"bind\":{\"args.text\":\"msg\"}},{\"act\":\"Message\",\"args\":{\"text\":\"kept\"}}]");
            var src = new Fdp.Toolkit.Scenario.JsonParamSource(new System.Collections.Generic.Dictionary<string, JsonNode?> { ["msg"] = JsonValue.Create("from the param") });
            string bound = Fdp.Toolkit.Scenario.ParamBinder.ApplyJson(typeof(TriggerParamsJsonDto), json, src)!;
            var dto = System.Text.Json.JsonSerializer.Deserialize<TriggerParamsJsonDto>(bound, BehaviorParams.JsonOptions);
            Assert.Equal("from the param", (string?)dto.Do![0].Args!["text"]);
            Assert.Equal("kept", (string?)dto.Do[1].Args!["text"]);
            Assert.Equal("msg", dto.Do[0].Bind!["args.text"]);                                              // non-destructive (SR-168): the binding stays
            Assert.Contains("from the param", Fdp.Toolkit.ScenarioRules.Actions.ActionStep.ToJson(dto.Do));

            var none = new Fdp.Toolkit.Scenario.JsonParamSource(new System.Collections.Generic.Dictionary<string, JsonNode?>());
            Assert.Throws<InvalidOperationException>(() => Fdp.Toolkit.Scenario.ParamBinder.ApplyJson(typeof(TriggerParamsJsonDto), json, none));
        }
    }
}
