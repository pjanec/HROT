using System.Linq;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Diagnostics;
using Fdp.Toolkit.Replication.Attributes;
using Fdp.Toolkit.Replication.Edits;
using Fdp.Toolkit.Replication.Patching;
using Fdp.Toolkit.ScenarioRules;
using Fdp.Toolkit.ScenarioRules.Actions;
using Hrot.AI.Behaviors.Brains.Objectives;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>⭐ rules ② (📄 docs/DESIGN_Scenario_Rules.md §00.11a) — a LIVE EDIT of a rule's params that keeps its state: <c>RuleEdit</c> through the attribute write path, applied once per Seq by
    /// <c>RuleEditSystem</c> → <c>BehaviorIngressSystem.Reparameterise</c> (the same parse as an assign + the kind's <c>[BehaviorStateKeeper]</c>, no restart).</summary>
    public sealed partial class ScenarioRulesTests
    {
        /// <summary>What the rule editor / the debug API do: ONE change list through the router.</summary>
        private static EntityWriteRoute Edit(EntityRepository repo, Entity e, string json, int seq)
            => EntityWriteRouter.For(repo).Write(e, new[]
            {
                new EntityAttributeChange { AttributeId = AttributeIds.RuleParams, Value = AttributeValue.FromString(json) },
                EntityAttributeChange.Double(AttributeIds.RuleEditOrigin, (int)BehaviorOrigin.Operator),
                EntityAttributeChange.Double(AttributeIds.RuleEditSeq, seq),
            });

        private static unsafe T Bb<T>(ObjWorld w, Entity e) where T : unmanaged
        {
            Assert.True(RootParamsAccess.TryGetRootBytes(w.Repo, e, out byte* p));
            return *(T*)p;
        }

        private static BrainJournalEntry[] EditEntries(ObjWorld w, Entity e)
        {
            var j = w.Repo.GetComponentRO<BrainJournal>(e);
            var all = new System.Collections.Generic.List<BrainJournalEntry>();
            for (int i = 0; i < BrainJournal.Capacity; i++) if (j.Entries[i].Kind == BrainJournalKind.Reparameterised) all.Add(j.Entries[i]);
            return all.OrderBy(x => x.Seq).ToArray();
        }

        private static string[] DoItems(ObjWorld w, Entity e) => ((ISimulationView)w.Repo).GetManagedComponentRO<ActionArgs>(e).Items.ToArray();

        /// <summary>L1 / R1 — the router SETS <c>RuleEdit</c> as a NEW instance (immutable record, replaced whole — never mutated in place): the old reference is not the new one, the old
        /// instance is unchanged, and Json + Seq + Origin arrive together. ✅ Red-proof: <c>SetManagedComponent</c> in <c>EcsPatchContext</c> removed ⇒ nothing lands ⇒ red.</summary>
        [Fact]
        public void TheRouter_SetsRuleEdit_AsANewInstance_NeverInPlace()
        {
            var w = new ObjWorld(); var src = Source(w);
            var t = w.Trigger(TrigJson(w.Net(src)));
            w.Run(1.5);
            var before = ((ISimulationView)w.Repo).GetManagedComponentRO<RuleEdit>(t);
            Assert.Equal((0, 0), (before.Seq, before.Done));
            Assert.Equal(EntityWriteRoute.Direct, Edit(w.Repo, t, TrigJson(w.Net(src), ",\"cooldown\":9"), 1));
            var after = ((ISimulationView)w.Repo).GetManagedComponentRO<RuleEdit>(t);
            Assert.NotSame(before, after);
            Assert.Equal((0, "", 0), (before.Seq, before.Json, before.Origin));                       // the old instance is untouched
            Assert.Equal((1, (int)BehaviorOrigin.Operator), (after.Seq, after.Origin));
            Assert.Contains("\"cooldown\":9", after.Json);
        }

        /// <summary>L2 — a repeating trigger with FireCount 2 and a cooldown RUNNING gets a new <c>cooldown</c>: FireCount stays 2, the phase stays Cooldown, the cooldown CLOCK is not restarted (the
        /// same <c>CooldownUntil</c>), the instance id is the same (no restart), and the new value is what the NEXT cooldown uses; one journal entry "edited by Operator". ✅ Red-proof (a): the keeper not
        /// called in <c>Reparameterise</c> ⇒ FireCount / Phase / CooldownUntil are lost ⇒ red.</summary>
        [Fact]
        public void ATriggerInACooldown_KeepsItsFireCountAndItsClock_AndUsesTheNewCooldownNextTime()
        {
            TrgActions.Reset();
            var w = new ObjWorld(); var src = Source(w);
            var t = w.Trigger(TrigJson(w.Net(src), ",\"repeat\":true,\"cooldown\":4,\"maxFires\":9"));
            w.Run(1);
            Flip(w, src, true); w.Run(0.5);                                    // fire 1
            w.Run(4.5); Flip(w, src, false); w.Run(0.5); Flip(w, src, true); w.Run(0.5);   // fire 2
            w.Run(0.5);
            Assert.Equal((2, TriggerState.Cooldown), (w.S(t).FireCount, Phase(w, t)));
            var was = Bb<TriggerBehavior.TriggerBlackboard>(w, t).Block;
            uint run = w.Repo.GetComponentRO<BehaviorState>(t).InstanceId;

            Assert.Equal(EntityWriteRoute.Direct, Edit(w.Repo, t, TrigJson(w.Net(src), ",\"repeat\":true,\"cooldown\":20,\"maxFires\":9"), 1));
            w.Run(0.5);
            var now = Bb<TriggerBehavior.TriggerBlackboard>(w, t).Block;
            Assert.Equal((2, TriggerState.Cooldown), (w.S(t).FireCount, Phase(w, t)));
            Assert.Equal((20f, was.CooldownUntil), (now.Cooldown, now.CooldownUntil));        // the new value in, the running clock untouched
            Assert.Equal(run, w.Repo.GetComponentRO<BehaviorState>(t).InstanceId);            // not a restart
            Assert.Equal(1, EditEntries(w, t).Length);
            Assert.Equal(((byte)BehaviorOrigin.Operator, (byte)0), (EditEntries(w, t)[0].Origin, EditEntries(w, t)[0].Cause));

            w.Run(4); Flip(w, src, false); w.Run(0.5); Flip(w, src, true); w.Run(0.5);        // the old cooldown is over, the edge comes again: fire 3
            Assert.Equal(3, w.S(t).FireCount);
            w.Run(0.5);                                                                       // Fired → Cooldown
            Assert.True(Bb<TriggerBehavior.TriggerBlackboard>(w, t).Block.CooldownUntil >= w.Time + 19);   // the NEW cooldown (20 s) was started
        }

        /// <summary>L2 — a HoldArea mid-hold gets a new radius and a shorter hold: its progress (<c>HeldFor</c>) is kept, so it ends the moment the NEW hold is reached (not a fresh 6 s).
        /// ✅ Red-proof (a): the keeper not called ⇒ the hold restarts from 0 ⇒ red.</summary>
        [Fact]
        public void AHoldAreaMidHold_KeepsItsProgress_WhenItsRadiusAndHoldAreEdited()
        {
            var w = new ObjWorld();
            var men = new[] { w.Man(Inside, ForceId.Friend), w.Man(Inside, ForceId.Friend), w.Man(Inside, ForceId.Friend), w.Man(Inside, ForceId.Friend) };
            var squad = w.Squad(ForceId.Friend, men);
            var hold = w.Objective("HoldArea", $"{{{Common(w, squad)},\"area\":{Circle0},\"holdSeconds\":10}}");
            w.Run(5.5);
            Assert.InRange(w.S(hold).ProgressCur, 4, 5);
            Assert.Equal(EntityWriteRoute.Direct, Edit(w.Repo, hold, $"{{{Common(w, squad)},\"area\":{{\"circle\":{{\"center\":[0,0,0],\"radius\":50}}}},\"holdSeconds\":7}}", 1));
            w.Run(0.5);
            Assert.InRange(w.S(hold).ProgressCur, 4, 6);                                      // kept, not 0
            Assert.Equal(7f, Bb<HoldAreaBehavior.HoldAreaBlackboard>(w, hold).Block.HoldSeconds);   // the new value is in
            w.Run(2);
            Assert.Equal(ObjectiveState.Achieved, w.S(hold).State);                           // 7 s reached ~2 s after the edit; a restarted hold would need 7 more
        }

        /// <summary>L2 — a measure (a Counter) keeps what it has counted: after two men went down its Value is 7 and its census is kept, an edit of its <c>floor</c> changes neither (a lost keeper would
        /// write the start value again). ✅ Red-proof (a): the keeper not called ⇒ the Value jumps back to the start ⇒ red.</summary>
        [Fact]
        public void ACounter_KeepsItsValueAndItsCensus_WhenEdited()
        {
            var w = new ObjWorld();
            var f = new Entity[3];
            for (int i = 0; i < 3; i++) f[i] = w.Man(Inside, ForceId.Friend, health: 10f);
            var c = w.Objective("Counter", CounterJson(10, "[{\"side\":\"Friend\",\"per\":-1}]"));
            w.Run(2);
            w.Repo.GetComponentRW<Health>(f[0]).Current = 0f; w.Repo.GetComponentRW<Health>(f[1]).Current = 0f;
            w.Run(1);
            Assert.Equal(8f, w.S(c).Value);
            Assert.Equal(EntityWriteRoute.Direct, Edit(w.Repo, c, CounterJson(10, "[{\"side\":\"Friend\",\"per\":-1}]", ",\"floor\":3"), 1));
            w.Run(1);
            Assert.Equal(8f, w.S(c).Value);
            var b = Bb<CounterBehavior.CounterBlackboard>(w, c).Block;
            Assert.Equal((3f, 2), (b.Floor, b.LastDowns0));
            w.Repo.GetComponentRW<Health>(f[2]).Current = 0f; w.Run(1);
            Assert.Equal(7f, w.S(c).Value);                                                   // still counting from where it was
        }

        /// <summary>L2 / R3 — a trigger that is <c>Acting</c> finishes its OLD do list; the edit WAITS (its Seq is not consumed) and applies whole when the run ends, so the NEXT run does the new list.
        /// ✅ Red-proof (c): the Acting wait removed from <c>RuleEditSystem</c> ⇒ the new do list replaces the running one at once ⇒ red.</summary>
        [Fact]
        public void AnActingTrigger_FinishesItsOldList_TheEditWaits_AndTheNextRunDoesTheNewOne()
        {
            TrgActions.Reset();
            var w = new ObjWorld(); var src = Source(w);
            const string old = "[{\"act\":\"TrgRecord\",\"args\":{\"value\":1}},{\"act\":\"TrgWait\",\"args\":{\"ticks\":4}},{\"act\":\"TrgRecord\",\"args\":{\"value\":2}}]";
            var t = w.Trigger(TrigJson(w.Net(src), ",\"repeat\":true", old));
            w.Run(1);
            Flip(w, src, true); w.Tick(0.5);
            Assert.Equal(TriggerState.Acting, Phase(w, t));
            var running = DoItems(w, t);
            Assert.Equal(EntityWriteRoute.Direct, Edit(w.Repo, t, TrigJson(w.Net(src), ",\"repeat\":true", "[{\"act\":\"TrgRecord\",\"args\":{\"value\":9}}]"), 1));
            w.Tick(0.5);
            Assert.Equal(TriggerState.Acting, Phase(w, t));
            Assert.Equal(running, DoItems(w, t));                                             // the running list is the OLD one
            Assert.Equal((0, 1), (((ISimulationView)w.Repo).GetManagedComponentRO<RuleEdit>(t).Done, ((ISimulationView)w.Repo).GetManagedComponentRO<RuleEdit>(t).Seq));   // not consumed
            Assert.Empty(EditEntries(w, t));
            w.Run(3);                                                                         // the run ends
            Assert.Equal(new[] { 1, 2 }, TrgActions.Calls);
            Assert.Equal(1, ((ISimulationView)w.Repo).GetManagedComponentRO<RuleEdit>(t).Done);   // applied after it
            Assert.Contains("9", DoItems(w, t)[1]);
            Flip(w, src, false); w.Run(0.5); Flip(w, src, true); w.Run(1);
            Assert.Equal(new[] { 1, 2, 9 }, TrgActions.Calls);                                // the next run: the new list
        }

        /// <summary>L4 — bad params are REFUSED: the status Reason says "edit refused: …", the journal has one refused entry, and the running rule is untouched (its blackboard and do list intact).
        /// ✅ Red-proof (d): the validation removed from <c>Reparameterise</c> ⇒ the unknown action reaches the resolver / is accepted ⇒ red.</summary>
        [Fact]
        public void BadParams_AreRefused_WithAReason_AndTheRuleIsUntouched()
        {
            TrgActions.Reset();
            var w = new ObjWorld(); var src = Source(w);
            var t = w.Trigger(TrigJson(w.Net(src), ",\"cooldown\":3"));
            w.Run(1.5);
            var items = DoItems(w, t);
            var was = Bb<TriggerBehavior.TriggerBlackboard>(w, t).Block;
            Edit(w.Repo, t, TrigJson(w.Net(src), ",\"cooldown\":9", "[{\"act\":\"NoSuchAction\",\"args\":{}}]"), 1);
            w.Run(0.5);
            Assert.StartsWith("edit refused", w.S(t).Reason.ToString());
            Assert.DoesNotContain("did not parse", w.S(t).Reason.ToString());                 // the validator named the problem, the resolver was never reached
            var entries = EditEntries(w, t);
            Assert.Equal(1, entries.Length);
            Assert.Equal((byte)1, entries[0].Cause);
            Assert.Equal(items, DoItems(w, t));
            Assert.Equal(was.Cooldown, Bb<TriggerBehavior.TriggerBlackboard>(w, t).Block.Cooldown);
            Assert.Equal(TriggerState.Armed, Phase(w, t));
            Flip(w, src, true); w.Run(0.5);
            Assert.Equal(new[] { 7 }, TrgActions.Calls);                                      // it still runs its own list
        }

        /// <summary>L3 — a kind WITHOUT a keeper refuses a live edit ("restart the rule") and keeps running unchanged. ✅ Red-proof: a default keeper that keeps nothing ⇒ the edit is accepted ⇒ red.</summary>
        [Fact]
        public void AKindWithoutAKeeper_RefusesTheLiveEdit()
        {
            var w = new ObjWorld();
            var man = w.Man(Inside, ForceId.Friend);
            var squad = w.Squad(ForceId.Friend, man);
            var nh = w.Objective("NoHarm", $"{{{Common(w, squad)},\"protected\":\"Neutral\",\"bySide\":\"Friend\"}}");
            w.Run(1.5);
            Edit(w.Repo, nh, $"{{{Common(w, squad)},\"protected\":\"Neutral\",\"bySide\":\"Hostile\"}}", 1);
            w.Run(0.5);
            Assert.Contains("restart the rule", w.S(nh).Reason.ToString());
            Assert.StartsWith("edit refused", w.S(nh).Reason.ToString());
            Assert.Equal((byte)1, EditEntries(w, nh).Single().Cause);
        }

        /// <summary>The same Seq is applied ONCE (a second write of the same Seq with other params is ignored), a new Seq applies again. ✅ Red-proof (b): <c>Done = Seq</c> removed from <c>RuleEditSystem</c> ⇒ it
        /// re-applies every tick ⇒ more than one journal entry ⇒ red.</summary>
        [Fact]
        public void TheSameSeq_IsAppliedOnce_ANewSeqAppliesAgain()
        {
            TrgActions.Reset();
            var w = new ObjWorld(); var src = Source(w);
            var t = w.Trigger(TrigJson(w.Net(src), ",\"cooldown\":3"));
            w.Run(1.5);
            Edit(w.Repo, t, TrigJson(w.Net(src), ",\"cooldown\":5"), 1); w.Run(2);
            Assert.Equal((5f, 1), (Bb<TriggerBehavior.TriggerBlackboard>(w, t).Block.Cooldown, EditEntries(w, t).Length));
            Edit(w.Repo, t, TrigJson(w.Net(src), ",\"cooldown\":6"), 1); w.Run(2);            // the same Seq: nothing
            Assert.Equal((5f, 1), (Bb<TriggerBehavior.TriggerBlackboard>(w, t).Block.Cooldown, EditEntries(w, t).Length));
            Edit(w.Repo, t, TrigJson(w.Net(src), ",\"cooldown\":6"), 2); w.Run(2);            // a new one: again
            Assert.Equal((6f, 2), (Bb<TriggerBehavior.TriggerBlackboard>(w, t).Block.Cooldown, EditEntries(w, t).Length));
        }

        /// <summary>On a node that does NOT own the rule the router turns the write into ONE change-request (three attributes in one command), and the owner applies those very changes through its own
        /// interpreter and edits the rule. ✅ Red-proof: <c>Rule.Params</c> left out of the change list ⇒ the owner gets a Seq with no Json ⇒ the edit is refused ⇒ red.</summary>
        [Fact]
        public void OnANodeThatDoesNotOwnTheRule_TheEditIsOneChangeRequest_AndTheOwnerAppliesIt()
        {
            TrgActions.Reset();
            var owner = new ObjWorld(); var src = Source(owner);
            var t = owner.Trigger(TrigJson(owner.Net(src), ",\"cooldown\":3"));
            owner.Run(1.5);
            int netId = owner.Net(t);

            using var other = new EntityRepository();
            Hrot.Map.Common.HrotSharedComponentRegistry.RegisterAll(other);
            var replica = other.CreateEntity();
            other.AddComponent(replica, new Fdp.Toolkit.Replication.Components.NetworkIdentity { Value = netId });
            Assert.Equal(EntityWriteRoute.Requested, Edit(other, replica, TrigJson(owner.Net(src), ",\"cooldown\":8"), 1));
            other.Bus.SwapBuffers();
            var cmd = Assert.Single(((ISimulationView)other).ReadManagedEvents<Fdp.Toolkit.Replication.Events.UpdateEntityAttributeCommand>().ToArray());
            Assert.Equal((netId, 3), (cmd.NetworkId, cmd.AttributeChanges.Count));

            var patch = EcsPatchContext.Create(owner.Repo, t);
            var interp = AttributeCompilerFactory.BuildBinaryInterpreter();
            var bctx = interp.CreateContext(patch); bctx.Repo = owner.Repo; bctx.Entity = t;
            interp.Apply(bctx, cmd.AttributeChanges.ToArray());
            Assert.True(patch.HasAppliedAny);
            owner.Run(0.5);
            Assert.Equal(8f, Bb<TriggerBehavior.TriggerBlackboard>(owner, t).Block.Cooldown);
            Assert.Equal(1, EditEntries(owner, t).Length);
        }
        // ── CE-2380 part 2 — the rule's verdict comes back as an OUTCOME under the request id ─────────────

        private static EditOutcome Outcome(ObjWorld w, System.Guid id)
        {
            Assert.True(EditOutcomeLog.Of(w.Repo).TryGet(id, out var o), "no outcome for the request");
            return o;
        }

        private static System.Guid LiveEdit(ObjWorld w, Entity e, string json, ref int seq)
        {
            RuleCommands.Edit(w.Repo, e, json, (int)BehaviorOrigin.Operator, ref seq, out _, out var id);
            return id;
        }

        private static System.Guid Ask(ObjWorld w, Entity e, RuleRequest what, ref int seq)
        {
            RuleCommands.Control(w.Repo, e, what, RuleOrigin.DebugApi, ref seq, out _, out var id);
            return id;
        }

        /// <summary>CE-2380 part 2 (lean C) — a valid live edit is Pending the moment it is written, and Applied once <c>RuleEditSystem</c> has applied it.
        /// ✅ Red-proof: the verdict report removed from <c>RuleEditSystem</c> ⇒ it stays Pending ⇒ red.</summary>
        [Fact]
        public void CE2380_AValidEdit_IsPending_ThenApplied()
        {
            var w = new ObjWorld(); var src = Source(w);
            var t = w.Trigger(TrigJson(w.Net(src), ",\"cooldown\":3"));
            w.Run(1.5);
            int seq = 0;
            var id = LiveEdit(w, t, TrigJson(w.Net(src), ",\"cooldown\":5"), ref seq);
            Assert.Equal(EditStatus.Pending, Outcome(w, id).Status);
            w.Run(0.5);
            Assert.Equal(EditStatus.Applied, Outcome(w, id).Status);
            Assert.Equal(5f, Bb<TriggerBehavior.TriggerBlackboard>(w, t).Block.Cooldown);
        }

        /// <summary>CE-2380 part 2 — a refusal with TWO problems: the outcome carries the WHOLE text (both), the status Reason stays cut to 55 characters (unchanged).
        /// ✅ Red-proof: the outcome built from the cut text ⇒ the second problem is missing ⇒ red.</summary>
        [Fact]
        public void CE2380_ARefusedEdit_CarriesTheWholeReason_WhileTheStatusReasonStaysCut()
        {
            var w = new ObjWorld(); var src = Source(w);
            var t = w.Trigger(TrigJson(w.Net(src), ",\"cooldown\":3"));
            w.Run(1.5);
            int seq = 0;
            var id = LiveEdit(w, t, "{\"common\":{\"title\":\"T\"},\"do\":[{\"act\":\"NoSuchAction\",\"args\":{}}]}", ref seq);   // no 'when' AND an unknown action
            w.Run(0.5);
            var o = Outcome(w, id);
            Assert.Equal(EditStatus.Refused, o.Status);
            Assert.StartsWith("edit refused: ", o.Reason);
            Assert.Contains("'when' is required", o.Reason);
            Assert.Contains("NoSuchAction", o.Reason);                                        // both problems
            string status = w.S(t).Reason.ToString();
            Assert.StartsWith("edit refused", status);
            Assert.True(status.Length <= 55 && status.Length < o.Reason.Length, $"status '{status}' vs outcome '{o.Reason}'");
        }

        /// <summary>CE-2380 part 2 — an edit sent while the trigger is Acting stays Pending (the edit waits), and gets its final verdict once the list ends.
        /// ✅ Red-proof: the Acting wait removed from <c>RuleEditSystem</c> ⇒ Applied at once ⇒ the Pending assert reddens.</summary>
        [Fact]
        public void CE2380_AnEditSentWhileActing_StaysPending_ThenGetsItsVerdict()
        {
            TrgActions.Reset();
            var w = new ObjWorld(); var src = Source(w);
            const string old = "[{\"act\":\"TrgRecord\",\"args\":{\"value\":1}},{\"act\":\"TrgWait\",\"args\":{\"ticks\":4}},{\"act\":\"TrgRecord\",\"args\":{\"value\":2}}]";
            var t = w.Trigger(TrigJson(w.Net(src), ",\"repeat\":true", old));
            w.Run(1);
            Flip(w, src, true); w.Tick(0.5);
            Assert.Equal(TriggerState.Acting, Phase(w, t));
            int seq = 0;
            var id = LiveEdit(w, t, TrigJson(w.Net(src), ",\"repeat\":true", "[{\"act\":\"TrgRecord\",\"args\":{\"value\":9}}]"), ref seq);
            w.Tick(0.5);
            Assert.Equal(EditStatus.Pending, Outcome(w, id).Status);                          // still waiting behind the running list
            w.Run(3);
            Assert.Equal(EditStatus.Applied, Outcome(w, id).Status);
        }

        /// <summary>CE-2380 part 2 — rule control: an objective asked to Fire is Refused with the reason (the status says the same, cut), a trigger asked to Fire is Pending then Applied, and a switched-off
        /// trigger asked to Fire is Refused by the TRIGGER's own check. ✅ Red-proof: <c>ReportControl</c> removed from <c>ObjectiveHost.HonourControl</c> ⇒ the first stays Pending ⇒ red.</summary>
        [Fact]
        public void CE2380_RuleControl_ReportsItsVerdict()
        {
            TrgActions.Reset();
            var w = new ObjWorld(); var src = Source(w);
            var c = w.Objective("Counter", CounterJson(10, "[{\"side\":\"Friend\",\"per\":-1}]"));
            var t = w.Trigger(TrigJson(w.Net(src)));
            w.Run(1.5);
            int seq = 0;

            var refused = Ask(w, c, RuleRequest.Fire, ref seq);
            Assert.Equal(EditStatus.Pending, Outcome(w, refused).Status);
            w.Run(0.5);
            var r = Outcome(w, refused);
            Assert.Equal(EditStatus.Refused, r.Status);
            Assert.Contains("trigger's request", r.Reason);

            var fire = Ask(w, t, RuleRequest.Fire, ref seq);
            Assert.Equal(EditStatus.Pending, Outcome(w, fire).Status);
            w.Run(0.5);
            Assert.Equal(EditStatus.Applied, Outcome(w, fire).Status);

            var t2 = w.Trigger(TrigJson(w.Net(src)));                                          // a second trigger (the first has fired and is final)
            w.Run(1.5);
            var off = Ask(w, t2, RuleRequest.Deactivate, ref seq); w.Run(0.5);
            Assert.Equal(EditStatus.Applied, Outcome(w, off).Status);
            var late = Ask(w, t2, RuleRequest.Fire, ref seq); w.Run(0.5);
            var l = Outcome(w, late);
            Assert.Equal(EditStatus.Refused, l.Status);
            Assert.Contains("Fire refused", l.Reason);
        }
    }
}
