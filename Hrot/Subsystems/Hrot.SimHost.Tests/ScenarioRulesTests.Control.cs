using System.Linq;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Diagnostics;
using Fdp.Toolkit.Replication.Attributes;
using Fdp.Toolkit.Replication.Patching;
using Fdp.Toolkit.ScenarioRules;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>⭐ rules V-4 (📄 docs/DESIGN_Scenario_Rules.md §00.10) — <c>RuleControl</c> through the attribute write path: the owner honours fire / disarm / rearm / reset / activate once per new Seq, and journals each.</summary>
    public sealed partial class ScenarioRulesTests
    {
        /// <summary>What the instructor's UI and the debug API do: ONE change list through the router (applied here when this node owns the entity).</summary>
        private static EntityWriteRoute Ctl(ObjWorld w, Entity e, RuleRequest what, int seq)
            => EntityWriteRouter.For(w.Repo).Write(e, new[]
            {
                EntityAttributeChange.Double(AttributeIds.RuleRequest, (int)what),
                EntityAttributeChange.Double(AttributeIds.RuleOrigin, (int)RuleOrigin.Instructor),
                EntityAttributeChange.Double(AttributeIds.RuleSeq, seq),
            });

        private static BrainJournalEntry[] RuleEntries(ObjWorld w, Entity e)
        {
            var j = w.Repo.GetComponentRO<BrainJournal>(e);
            var all = new System.Collections.Generic.List<BrainJournalEntry>();
            for (int i = 0; i < BrainJournal.Capacity; i++) if (j.Entries[i].Kind == BrainJournalKind.RuleControl) all.Add(j.Entries[i]);
            return all.OrderBy(x => x.Seq).ToArray();
        }

        /// <summary>V-4 — <c>Fire</c> runs the <c>do</c> list NOW (the condition never rose), skipping holdFor / delay; written through the router, honoured on the owner. ✅ Red-proof: the <c>Fire</c>
        /// arm of <c>TriggerBehavior.Control</c> removed ⇒ nothing runs ⇒ red.</summary>
        [Fact]
        public void Fire_RunsTheDoListNow_SkippingTheHoldAndTheDelay()
        {
            TrgActions.Reset();
            var w = new ObjWorld(); var src = Source(w);
            var t = w.Trigger(TrigJson(w.Net(src), ",\"holdFor\":30,\"delay\":30"));
            w.Run(1.5);
            Assert.Empty(TrgActions.Calls);
            Assert.Equal(EntityWriteRoute.Direct, Ctl(w, t, RuleRequest.Fire, 1));
            w.Run(0.5);
            Assert.Equal(new[] { 7 }, TrgActions.Calls);
            Assert.Equal((TriggerState.Done, 1), (Phase(w, t), w.S(t).FireCount));   // fired once, a non-repeating trigger is Done
        }

        /// <summary>V-4 — <c>Disarm</c> switches a trigger off (Inactive, Disarmed); the condition rising meanwhile fires nothing; <c>Rearm</c> with the condition ALREADY true arms it with no edge (no firing while it stays
        /// true), and a later drop and rise fires. ✅ Red-proof: <c>Disarm</c> mapped to Activate in <c>HonourControl</c> ⇒ it stays armed and fires ⇒ red.</summary>
        [Fact]
        public void Disarm_ThenRearm_DoesNotFireWhileTheConditionIsAlreadyTrue()
        {
            TrgActions.Reset();
            var w = new ObjWorld(); var src = Source(w);
            var t = w.Trigger(TrigJson(w.Net(src), ",\"repeat\":true"));
            w.Run(1.5);
            Ctl(w, t, RuleRequest.Disarm, 1); w.Run(0.5);
            Assert.Equal((ObjectiveState.Inactive, TriggerState.Disarmed), (w.S(t).State, Phase(w, t)));
            Flip(w, src, true); w.Run(1.5);
            Assert.Empty(TrgActions.Calls);                                   // disarmed: the rise is ignored
            Ctl(w, t, RuleRequest.Rearm, 2); w.Run(2);
            Assert.Equal((ObjectiveState.Active, TriggerState.Armed), (w.S(t).State, Phase(w, t)));
            Assert.Empty(TrgActions.Calls);                                   // true when re-armed is NOT an edge
            Flip(w, src, false); w.Run(0.5); Flip(w, src, true); w.Run(0.5);
            Assert.Equal(new[] { 7 }, TrgActions.Calls);
        }

        /// <summary>V-4 — <c>Reset</c> restarts an ACHIEVED objective through the SR-114 path (back to Active, progress cleared). ✅ Red-proof: the <c>Reset</c> arm calling <c>SetActive(…, reset: false)</c> ⇒ a final
        /// one refuses ⇒ red.</summary>
        [Fact]
        public void Reset_RestartsAnAchievedObjective()
        {
            var w = new ObjWorld();
            var man = w.Man(Outside, ForceId.Friend);
            var squad = w.Squad(ForceId.Friend, man);
            var o = w.Objective("ReachArea", $"{{{Common(w, squad)},\"area\":{Circle0}}}");
            w.Run(1.5);
            w.Move(man, Inside); w.Run(1);
            Assert.Equal(ObjectiveState.Achieved, w.S(o).State);
            w.Move(man, Outside);
            Ctl(w, o, RuleRequest.Reset, 1); w.Run(1);
            Assert.Equal((ObjectiveState.Active, 0), (w.S(o).State, w.S(o).ProgressCur));
        }

        /// <summary>V-4 — the SAME Seq twice acts ONCE; a new Seq acts again; and every honoured request is one journal entry "&lt;request&gt; by &lt;origin&gt;" on the rule entity. ✅ Red-proof: <c>rc.Done = seq</c> removed ⇒ the
        /// request re-fires every tick ⇒ red.</summary>
        [Fact]
        public void TheSameSeqActsOnce_ANewSeqActsAgain_AndEachRequestIsJournaled()
        {
            TrgActions.Reset();
            var w = new ObjWorld(); var src = Source(w);
            var t = w.Trigger(TrigJson(w.Net(src), ",\"repeat\":true"));
            w.Run(1.5);
            Ctl(w, t, RuleRequest.Fire, 5); w.Run(2);
            Ctl(w, t, RuleRequest.Fire, 5); w.Run(2);                         // the same Seq: nothing
            Assert.Equal(new[] { 7 }, TrgActions.Calls);
            Ctl(w, t, RuleRequest.Fire, 6); w.Run(2);                         // a new one: again
            Assert.Equal(new[] { 7, 7 }, TrgActions.Calls);
            var entries = RuleEntries(w, t);
            Assert.Equal(2, entries.Length);
            Assert.Equal(((int)RuleRequest.Fire, 5, (byte)RuleOrigin.Instructor), (entries[0].A, entries[0].B, entries[0].Origin));
            Assert.Equal(6, entries[1].B);
        }

        /// <summary>V-4 — on a node that does NOT own the rule entity the router turns the write into ONE change-request (all three attributes in one command — the owner never sees a Seq without its Request),
        /// and the owner applies those very changes through its own interpreter and acts on them. ✅ Red-proof: <c>Rule.Seq</c> left out of the change list ⇒ the owner never sees a new Seq ⇒ red.</summary>
        [Fact]
        public void OnANodeThatDoesNotOwnTheRule_TheWriteIsOneChangeRequest_AndTheOwnerActsOnIt()
        {
            TrgActions.Reset();
            var owner = new ObjWorld(); var src = Source(owner);
            var t = owner.Trigger(TrigJson(owner.Net(src)));
            owner.Run(1.5);
            int netId = owner.Net(t);

            using var other = new EntityRepository();                       // a node holding only the replica of the rule entity: no RuleControl, no authority over it
            Hrot.Map.Common.HrotSharedComponentRegistry.RegisterAll(other);
            var replica = other.CreateEntity();
            other.AddComponent(replica, new Fdp.Toolkit.Replication.Components.NetworkIdentity { Value = netId });
            Assert.Equal(EntityWriteRoute.Requested, Ctl2(other, replica, RuleRequest.Fire, 1));
            other.Bus.SwapBuffers();
            var commands = ((ISimulationView)other).ReadManagedEvents<Fdp.Toolkit.Replication.Events.UpdateEntityAttributeCommand>().ToArray();
            var cmd = Assert.Single(commands);                              // ONE command for the whole change list
            Assert.Equal((netId, 3), (cmd.NetworkId, cmd.AttributeChanges.Count));

            // the owner side (UpdateEntityAttributeRequestSystem's apply): the same interpreter, the owner's authority gate
            var patch = EcsPatchContext.Create(owner.Repo, t);
            var interp = AttributeCompilerFactory.BuildBinaryInterpreter();
            var bctx = interp.CreateContext(patch); bctx.Repo = owner.Repo; bctx.Entity = t;
            interp.Apply(bctx, cmd.AttributeChanges.ToArray());
            Assert.True(patch.HasAppliedAny);
            owner.Run(0.5);
            Assert.Equal(new[] { 7 }, TrgActions.Calls);                    // fired by the request that came from elsewhere
        }

        private static EntityWriteRoute Ctl2(EntityRepository repo, Entity e, RuleRequest what, int seq)
            => EntityWriteRouter.For(repo).Write(e, new[]
            {
                EntityAttributeChange.Double(AttributeIds.RuleRequest, (int)what),
                EntityAttributeChange.Double(AttributeIds.RuleOrigin, (int)RuleOrigin.Instructor),
                EntityAttributeChange.Double(AttributeIds.RuleSeq, seq),
            });
    }
}
