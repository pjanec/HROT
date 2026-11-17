using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Aggregate;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Hrot.AI.Behaviors.Orders;
using Hrot.CGF.Systems;
using Hrot.Core.Tkb;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐⭐⭐ <c>X-2</c> (agg-behaviors, R-8353 … R-8364; 📄 docs/designs/aggregate-units/DESIGN_X2_Order_Reresolution.md §8 S2) — a stochastic SQUAD that
    /// SPLITS keeps its order (the same request id, the physical play the routing table gives, started once its men are in its roster); one that
    /// MERGES keeps it too (the stochastic play, the params projected). In F-5's suite (the reconciler is F-5's system, renamed), T-1.
    /// </summary>
    public partial class SquadManeuverTests
    {
        private static readonly Vector3 X2Post = new(40f, 40f, 0f);
        private static readonly Guid X2Request = new("2b000000-0000-4000-8000-000000008353");

        /// <summary>A stochastic squad of <paramref name="men"/> (echelon 0, an infantry squad's DIS) holding an operator's Defend at <see cref="X2Post"/>.</summary>
        private static (Squad S, UnitResolutionFitSystem Fit, Hrot.Common.Systems.UnitHierarchySystem Hier, EntityRepository R, Entity U) X2Squad(int men = 10, bool order = true)
        {
            var (s, _, repo, u) = StochasticMover(slots: men, at: X2Post);
            AggregateTestUnits.Make(repo, u, slots: men, up: men, echelon: 0);
            repo.SetDisType(u, UnitDis.Land(UnitArm.Infantry, UnitEchelon.Squad, 225));
            if (!repo.IsComponentTypeRegistered<AggregateChild>()) repo.RegisterComponent<AggregateChild>();
            if (order)
            {
                repo.Bus.PublishManaged(new AssignBehaviorEvent
                {
                    Entity = u, BehaviorName = ResolutionRoutingTable.Plays.Defend, Origin = BehaviorOrigin.Operator, RequestId = X2Request,
                    JsonParams = "{" + Loc("Position", X2Post) + ",\"SearchRadius\":25}",
                });
                s.Tick(); s.Tick(); s.Tick();
                Assert.Equal(ResolutionRoutingTable.Plays.Defend, s.TaskName);
            }
            return (s, new UnitResolutionFitSystem(s.Registry), new Hrot.Common.Systems.UnitHierarchySystem(), repo, u);
        }

        /// <summary>What the manager's handover does (D-P6, X2-B): the released men exist (Held 0), each is assigned to the unit, the unit is Physical.</summary>
        private static Entity[] X2Split(Squad s, Entity u, int men = 10, bool assign = true)
        {
            var repo = s.Repo;
            var list = new Entity[men];
            for (int i = 0; i < men; i++)
            {
                var m = repo.CreateEntity();
                repo.AddComponent(m, new SimTransform { Position = X2Post + new Vector3(i * 2f, 0f, 0f), Rotation = Quaternion.Identity });
                repo.AddComponent(m, new AggregateChild { Unit = u, Slot = (byte)i, Held = 0 });
                list[i] = m;
            }
            if (assign) X2Assign(s, u, list);
            repo.GetComponentRW<AggregateUnit>(u).Resolution = AggregateResolution.Physical;
            return list;
        }

        /// <summary>The handover's assigns (X2-B): one <see cref="CmdAssignSubordinate"/> per man (the hierarchy applies them a frame later).</summary>
        private static void X2Assign(Squad s, Entity u, Entity[] men)
        {
            for (int i = 0; i < men.Length; i++)
                s.Repo.Bus.Publish(new CmdAssignSubordinate { Subordinate = men[i], Commander = u, Designation = i == 0 ? TacticalDesignation.SquadLeader : TacticalDesignation.Wingman });
        }

        /// <summary>The merge (D-P7): the men removed; the roster emptied. ⚠ The harness's squad sensing needs the commander's roster (production's
        /// <c>SquadCoordinationSystem</c> queries <c>With&lt;UnitRoster&gt;</c>, so the switch REMOVES it — UnitResolutionRails X2B pins that).</summary>
        private static void X2Merge(Squad s, Entity u, Entity[] men)
        {
            foreach (var m in men) s.Repo.DestroyEntity(m);
            s.Repo.SetComponent(u, new UnitRoster());
            s.Repo.GetComponentRW<AggregateUnit>(u).Resolution = AggregateResolution.Stochastic;
        }

        /// <summary>One frame in a node's order: the world (Input: the ingress), the reconciler, then the hierarchy at the Simulation tail.</summary>
        private static void X2Frame(Squad s, UnitResolutionFitSystem fit, Hrot.Common.Systems.UnitHierarchySystem? hier)
        {
            s.Tick();
            fit.Execute(s.Repo, 0.016f);
            hier?.Execute(s.Repo, 0.016f);
        }

        private static BehaviorStartRecord? X2Record(EntityRepository repo, Entity u)
            => repo.HasManagedComponent<BehaviorStartRecord>(u) ? ((ISimulationView)repo).GetManagedComponentRO<BehaviorStartRecord>(u) : null;

        /// <summary>⭐⭐⭐ X-2 ACCEPTANCE (S2 a / b / c) — a split squad under an operator's Defend runs <c>SquadDefend</c>, with the SAME request id and origin,
        /// started ONCE, and only after every released man is in its roster (the hierarchy's assigns land a frame after the handover). ✅ Red-proofs:
        /// the roster wait dropped ⇒ <c>SquadDefend</c> starts with an empty roster ⇒ red · <c>IsResume</c> / the id not carried ⇒ a new id ⇒ red ·
        /// the bus-pending wait dropped ⇒ two starts ⇒ red.</summary>
        [Fact]
        public void X2_ASplitSquad_KeepsItsDefend_AsSquadDefend_SameId_StartedOnce_AfterItsMenAreRostered()
        {
            var (s, fit, hier, repo, u) = X2Squad();
            var men = X2Split(s, u, assign: false);
            for (int i = 0; i < 6; i++) X2Frame(s, fit, hier);                 // the men exist, released, not yet rostered
            Assert.Equal(0, fit.Reissued);                                     // b — it waits for the men
            Assert.Equal(ResolutionRoutingTable.Plays.Defend, s.TaskName);
            X2Assign(s, u, men);

            var starts = new HashSet<uint>();
            uint before = repo.GetComponentRO<BehaviorState>(u).InstanceId;
            int rosterAtStart = -1;
            for (int i = 0; i < 30; i++)
            {
                X2Frame(s, fit, hier);
                var bs = repo.GetComponentRO<BehaviorState>(u);
                if (bs.InstanceId != before && starts.Add(bs.InstanceId) && rosterAtStart < 0)
                    rosterAtStart = repo.HasComponent<UnitRoster>(u) ? repo.GetComponentRO<UnitRoster>(u).Count : 0;
            }
            Assert.Equal("SquadDefend", s.TaskName);                           // a — the physical play the table gives (the fallback row, X2-C2)
            Assert.Single(starts);                                             // c — ONE start per switch
            Assert.Equal(1, fit.Reissued);
            Assert.Equal(10, rosterAtStart);                                   // b — with its men
            var rec = X2Record(repo, u);
            Assert.NotNull(rec);
            Assert.Equal(X2Request, rec!.RequestId);                           // a — the same order (R-2090: one id)
            Assert.Equal(BehaviorOrigin.Operator, repo.GetComponentRO<BehaviorState>(u).Origin);
            Assert.Equal(OrderKind.Defend, CurrentOrders.Of(repo, u).Kind);
        }

        /// <summary>⭐⭐ X-2 (S2 d) — a MERGED squad (men gone, roster removed) running <c>SquadDefend</c> with physical-only params (<c>Posts</c>) runs
        /// <c>StochasticUnitDefend</c>, the params PROJECTED onto its DTO, the same id. ✅ Red-proof: the projection dropped (the physical json fed raw)
        /// ⇒ the strict read refuses it ⇒ the task stays <c>SquadDefend</c> ⇒ red.</summary>
        [Fact]
        public void X2_AMergedSquad_KeepsItsDefend_AsStochasticUnitDefend_FromProjectedParams()
        {
            var (s, fit, hier, repo, u) = X2Squad(order: false);
            var men = X2Split(s, u);
            X2Frame(s, fit, hier);
            repo.Bus.PublishManaged(new AssignBehaviorEvent
            {
                Entity = u, BehaviorName = "SquadDefend", Origin = BehaviorOrigin.Operator, RequestId = X2Request,
                JsonParams = "{" + Loc("Position", X2Post) + ",\"SearchRadius\":25,\"Posts\":[{\"point\":" + Pt(X2Post) + ",\"window\":true}]}",
            });
            for (int i = 0; i < 4; i++) X2Frame(s, fit, hier);
            Assert.Equal("SquadDefend", s.TaskName);
            X2Merge(s, u, men);
            fit.Execute(repo, 0.016f);                                         // the node's order: the manager merges, the reconciler runs next, then the brains
            for (int i = 0; i < 6; i++) X2Frame(s, fit, hier);
            Assert.Equal(ResolutionRoutingTable.Plays.Defend, s.TaskName);
            Assert.Equal(X2Request, X2Record(repo, u)!.RequestId);
            Assert.DoesNotContain("Posts", X2Record(repo, u)!.JsonParams);
            Assert.Equal(1, fit.Reissued);
        }

        /// <summary>⭐ X-2 (S2 e, X2-E) — a PAUSED order (a reaction runs over it) is re-issued at the split: it outranks the reaction, which ends; the
        /// paused task is gone. ✅ Red-proof: the paused arm dropped ⇒ the stochastic reaction keeps running on the physical squad ⇒ red.</summary>
        [Fact]
        public void X2_APausedOrder_IsReissued_AndTheReactionEnds()
        {
            var (s, fit, hier, repo, u) = X2Squad();
            s.React(ResolutionRoutingTable.Plays.BreakContact);
            s.Tick(); s.Tick(); s.Tick();
            Assert.Equal(ResolutionRoutingTable.Plays.BreakContact, s.TaskName);
            Assert.True(CurrentOrders.Of(repo, u).Paused);
            X2Split(s, u);
            for (int i = 0; i < 10; i++) X2Frame(s, fit, hier);
            Assert.Equal("SquadDefend", s.TaskName);
            Assert.False(CurrentOrders.Of(repo, u).Paused);
            Assert.Equal(X2Request, CurrentOrders.Of(repo, u).RequestId);
        }

        /// <summary>⭐ X-2 (S2 f, X2-E, R-8364) — a stochastic REACTION with no order behind it, on a squad that is now physical, is ended. ✅ Red-proof:
        /// the clear arm dropped ⇒ it keeps running ⇒ red.</summary>
        [Fact]
        public void X2_AStochasticReactionAlone_IsClearedOnAPhysicalSquad()
        {
            var (s, fit, hier, repo, u) = X2Squad(order: false);
            s.React(ResolutionRoutingTable.Plays.BreakContact);
            s.Tick(); s.Tick(); s.Tick();
            Assert.Equal(ResolutionRoutingTable.Plays.BreakContact, s.TaskName);
            X2Split(s, u);
            for (int i = 0; i < 6; i++) X2Frame(s, fit, hier);
            Assert.NotEqual(ResolutionRoutingTable.Plays.BreakContact, s.TaskName);
            Assert.Equal(1, fit.Cleared);
        }

        /// <summary>⭐ X-2 (X2-D, SV-10 / SV-14) — an order with NO play at the new resolution (a pinned merge of a squad recovering its wounded: no
        /// stochastic Recover row) is cleared and its commander hears TaskFailed; <see cref="OrderReissue.BlockReason"/> says why an automatic switch
        /// waits for a squad, and never holds a platoon (X2-I). ✅ Red-proof: the clear dropped (the arm waiting on a missing play) ⇒ SquadRecover
        /// keeps running on a stochastic unit ⇒ red.</summary>
        [Fact]
        public void X2_AnOrderWithNoPlayThere_IsCleared_AndItsCommanderHearsTaskFailed()
        {
            var (s, fit, hier, repo, u) = X2Squad(order: false);
            var men = X2Split(s, u);
            var boss = repo.CreateEntity();
            repo.AddComponent(u, new UnitSubordinate { Commander = boss });
            X2Frame(s, fit, hier);
            // the squad (physical) runs its superior's Recover order (stamped as the ingress leaves it: the play would end at once with no casualty)
            void Recovering()
            {
                var bs = repo.GetComponentRO<BehaviorState>(u);
                repo.SetComponent(u, bs with { ActiveBehaviorHash = BehaviorHash.FromName("SquadRecover"), Origin = BehaviorOrigin.Superior, InstanceId = bs.InstanceId + 100 });
                if (!repo.TryGetTable(typeof(BehaviorStartRecord), out _)) repo.RegisterManagedComponent<BehaviorStartRecord>();
                repo.SetManagedComponent(u, new BehaviorStartRecord { BehaviorName = "SquadRecover", JsonParams = "{}", InstanceId = bs.InstanceId + 100, RequestId = X2Request });
            }
            Recovering();
            Assert.Equal(OrderKind.Recover, CurrentOrders.Of(repo, u).Kind);
            Assert.NotNull(OrderReissue.BlockReason(repo, u, AggregateResolution.Stochastic, fit.IsBehaviour));   // an automatic merge would wait
            repo.GetComponentRW<AggregateUnit>(u).Echelon = 1;
            Assert.Null(OrderReissue.BlockReason(repo, u, AggregateResolution.Stochastic, fit.IsBehaviour));      // never a platoon (X2-I)
            repo.GetComponentRW<AggregateUnit>(u).Echelon = 0;
            fit.Execute(repo, 0.016f);
            Assert.Equal(0, fit.Cleared);                                      // physical: Recover fits

            X2Merge(s, u, men);                                                // the PINNED merge goes anyway (SV-14)
            Recovering();
            fit.Execute(repo, 0.016f);
            Assert.Equal(1, fit.Cleared);
            Assert.Equal(1, Reports(repo, u, Fdp.Toolkit.Squad.Command.ProgressKind.TaskFailed));
            s.Tick();                                                          // the clear is on the bus now (applied next frame)
            Recovering();
            fit.Execute(repo, 0.016f);
            Assert.Equal(1, fit.Cleared);                                      // once: the clear on the bus is waited for
        }

        /// <summary>⭐ X-2 (S2 g, R-2084) — a steady frame of the reconciler (the order fits) allocates nothing over 200 frames. ✅ Red-proof: a
        /// <c>CurrentOrders.Of</c> + <c>PlayFor</c> on every unit every frame (no fit pre-check) ⇒ the routing's strings ⇒ red.</summary>
        [Fact]
        public void X2_ASteadyFrame_AllocatesNothing()
        {
            var (s, fit, hier, repo, u) = X2Squad();
            X2Split(s, u);
            for (int i = 0; i < 12; i++) X2Frame(s, fit, hier);
            Assert.Equal("SquadDefend", s.TaskName);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 200; i++) fit.Execute(repo, 0.016f);
            Assert.Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        /// <summary>⭐⭐ X-2 S3 (X2-H, R-8361; CE-2304) — a commander's routed checks are RESOLUTION-BLIND: it routed <c>Order.Defend</c> (neutral) to a
        /// stochastic member; the member SPLITS and the reconciler re-issued it as <c>SquadDefend</c> under the SAME request id ⇒ the commander still sees
        /// its order running and given (no re-send, no restart of the posted men); a member running ANOTHER order (a new id) is not matched. ✅ Red-proof:
        /// the helpers keyed by the member's CURRENT resolution again ⇒ the physical key differs from the recorded <c>Order.Defend</c> ⇒ red.</summary>
        [Fact]
        public void X2H_ACommandersRoutedCheck_SurvivesTheMembersSplit_ByRequestId()
        {
            var s = new Squad(members: 1);
            var repo = s.Repo;
            var m = s.Members[0];
            MakeStochastic(repo, m);
            repo.GetComponentRW<BehaviorState>(s.Commander).InstanceId = 7;     // the commander's ticking run
            const string neutral = "{\"SearchRadius\":25}", physical = "{\"SearchRadius\":25,\"MinRange\":5}";
            Hrot.AI.Behaviors.Brains.CommandOrders.Route(repo, s.Commander, m, OrderKind.Defend, neutral, "SquadDefend", physical, newOrder: true);
            s.Tick();
            var sent = Assert.Single(s.Orders);
            Assert.Equal("Order.Defend", sent.IntentId);

            // what the ingress + the reconciler leave on the member after the split: SquadDefend, its order's id kept (IsResume)
            if (!repo.HasComponent<BehaviorState>(m)) repo.AddComponent(m, new BehaviorState());
            repo.SetComponent(m, new BehaviorState { ActiveBehaviorHash = BehaviorHash.FromName("SquadDefend"), Origin = BehaviorOrigin.Superior, InstanceId = 41 });
            if (!repo.TryGetTable(typeof(BehaviorStartRecord), out _)) repo.RegisterManagedComponent<BehaviorStartRecord>();
            repo.SetManagedComponent(m, new BehaviorStartRecord { BehaviorName = "SquadDefend", JsonParams = neutral, InstanceId = 41, RequestId = sent.RequestId });
            repo.GetComponentRW<AggregateUnit>(m).Resolution = AggregateResolution.Physical;
            Assert.True(Hrot.AI.Behaviors.Brains.CommandOrders.StillRoutedRunning(repo, s.Commander, m, OrderKind.Defend, neutral, "SquadDefend", physical));
            Assert.True(Hrot.AI.Behaviors.Brains.CommandOrders.GaveRouted(repo, s.Commander, m, OrderKind.Defend, neutral, "SquadDefend", physical));

            // another order on the member (a new id) is not the commander's: it re-sends
            repo.SetManagedComponent(m, new BehaviorStartRecord { BehaviorName = "SquadDefend", JsonParams = neutral, InstanceId = 41, RequestId = Guid.NewGuid() });
            Assert.False(Hrot.AI.Behaviors.Brains.CommandOrders.StillRoutedRunning(repo, s.Commander, m, OrderKind.Defend, neutral, "SquadDefend", physical));
        }

        /// <summary>⭐⭐ X-2 S4 (X2-F, R-8359, R-8365; DP3-D ②) — a split squad whose plan posted every man re-issues its Defend as <c>SquadDefend</c> WITH those
        /// posts (D-2f <c>Posts</c>, all 10 — over the old cap of 8), the men standing on them: the play never asks the terrain for windows, and reports
        /// PROGRESS Set. ✅ Red-proof: the posts not carried ⇒ the window ask is issued ⇒ red.</summary>
        [Fact]
        public void X2F_ASplitSquadsSquadDefend_StartsFromItsPlannedPosts_NoAsk_AndReportsSet()
        {
            var (s, fit, hier, repo, u) = X2Squad();
            var men = X2Split(s, u, assign: false);
            var plan = new PlannedPosts { Count = 10, Ready = 1, Stage = PlannedPosts.StageReady };
            for (int k = 0; k < 10; k++)
            {
                var post = X2Post + new Vector3(-9f + 2f * k, 6f, 0f);
                plan.Set(k, post, Vector3.Zero, (byte)(PlannedPosts.PostedFlag | PlannedPosts.WindowFlag));
                repo.GetComponentRW<SimTransform>(men[k]).Position = post;     // DP3-D ①: Materialise placed each man on his post
            }
            if (!repo.IsComponentTypeRegistered<PlannedPosts>()) repo.RegisterComponent<PlannedPosts>();
            repo.AddComponent(u, plan);
            X2Assign(s, u, men);
            bool asked = false;
            for (int i = 0; i < 40; i++)
            {
                X2Frame(s, fit, hier);
                asked |= !Fdp.Toolkit.Spatial.Eqs.EqsChildSensor.Find(repo, u, Hrot.AI.Behaviors.Brains.SquadDefendPlay.WindowSite).IsNull;
            }
            Assert.Equal("SquadDefend", s.TaskName);
            var json = X2Record(repo, u)!.JsonParams;
            var dto = System.Text.Json.JsonSerializer.Deserialize<Hrot.Map.Definitions.Behavior.SquadDefendParamsJsonDto>(json, Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed);
            Assert.Equal(10, dto.Posts!.Length);                               // every man's post, past the old cap of 8
            Assert.False(asked, "a man standing on a planned post is never surveyed for windows");
            Assert.True(Reports(repo, u, Fdp.Toolkit.Squad.Command.ProgressKind.Set) >= 1, "PROGRESS Set: every man on his post");
        }
    }
}
