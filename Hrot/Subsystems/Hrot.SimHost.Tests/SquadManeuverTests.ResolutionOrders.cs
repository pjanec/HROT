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
        /// waits for a squad (CE-8307: and for any unit whose direct children fit a roster — X2-I's echelon rule is gone). ✅ Red-proof: the clear dropped (the arm waiting on a missing play) ⇒ SquadRecover
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
            // ⭐ CE-8307 (R-8389): the scope is no longer the echelon (X2-I) but the split's direct children fitting a roster — pinned by X2I_*
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

        /// <summary>⭐ X-2 R-8372, as revised by CE-8307 (R-8389) — a unit whose split's DIRECT children do not fit a roster (no composite to group them ⇒
        /// each of its 20 slots a man) is out of scope: the manager's <c>SwitchBlocked</c> never holds it, and the reconciler leaves its play alone after a
        /// split. ✅ Red-proof: <c>StochasticUnitFill.RostersItsChildren</c> always true ⇒ BlockReason holds the split (a platoon's Defend at Physical is
        /// <c>PlatoonDefend</c>, but the 16 men are not squads) … ⇒ the reconciler re-issues ⇒ red.</summary>
        [Fact]
        public void X2I_AnAuthoredPlatoonTable_AtEchelonZero_IsOutOfScope()
        {
            var (s, fit, hier, repo, u) = X2Squad(men: 20);
            repo.SetDisType(u, UnitDis.Land(UnitArm.Infantry, UnitEchelon.Platoon, 225));
            Assert.Null(OrderReissue.BlockReason(repo, u, AggregateResolution.Physical, fit.IsBehaviour));
            X2Split(s, u, men: 16);
            for (int i = 0; i < 6; i++) X2Frame(s, fit, hier);
            Assert.Equal(ResolutionRoutingTable.Plays.Defend, s.TaskName);   // left alone (X2-I)
            Assert.Equal(0, fit.Cleared);
            Assert.Equal(0, fit.Reissued);
        }

        /// <summary>⭐⭐ CE-8307 (R-8386; DESIGN_Split_One_Level.md §6 Q-e) — a PLATOON whose split made its 3 stochastic SQUADS keeps its Defend: once the
        /// squads are in its roster the reconciler re-issues it as <c>PlatoonDefend</c> (the new <c>InfantryPlatoon</c> class and fallback row), the SAME
        /// request id, once; the platoon then commands its squads. ✅ Red-proof: the <c>(InfantryPlatoon, Physical, Defend)</c> row dropped ⇒ no physical play
        /// ⇒ the order is cleared ⇒ red.</summary>
        [Fact]
        public void CE8307_ASplitPlatoon_KeepsItsDefend_AsPlatoonDefend_OverItsSquads_SameId()
        {
            var (s, fit, hier, repo, u) = X2Squad(men: 3);                      // 3 direct children (no TKB here: the table's count stands in for them)
            repo.SetDisType(u, UnitDis.Land(UnitArm.Infantry, UnitEchelon.Platoon, 225));
            Assert.Null(OrderReissue.BlockReason(repo, u, AggregateResolution.Physical, fit.IsBehaviour));   // a platoon's Defend HAS a physical play now
            var squads = new Entity[3];
            for (int k = 0; k < 3; k++)
            {
                var c = repo.CreateEntity();
                repo.AddComponent(c, new SimTransform { Position = X2Post + new Vector3(k * 20f, 0f, 0f), Rotation = Quaternion.Identity });
                repo.AddComponent(c, new AggregateChild { Unit = u, Slot = (byte)(k + 1), Held = 0 });
                repo.AddComponent(c, new AggregateUnit { Resolution = AggregateResolution.Stochastic });
                repo.SetDisType(c, UnitDis.Land(UnitArm.Infantry, UnitEchelon.Squad, 225));
                squads[k] = c;
            }
            repo.GetComponentRW<AggregateUnit>(u).Resolution = AggregateResolution.Physical;
            for (int i = 0; i < 4; i++) X2Frame(s, fit, hier);
            Assert.Equal(0, fit.Reissued);                                       // not before the squads are rostered
            for (int k = 0; k < 3; k++) repo.Bus.Publish(new CmdAssignSubordinate { Subordinate = squads[k], Commander = u, Designation = TacticalDesignation.Wingman });
            for (int i = 0; i < 20; i++) X2Frame(s, fit, hier);
            Assert.Equal("PlatoonDefend", s.TaskName);
            Assert.Equal(1, fit.Reissued);
            Assert.Equal(0, fit.Cleared);
            Assert.Equal(X2Request, X2Record(repo, u)!.RequestId);
            Assert.Equal(3, repo.GetComponentRO<UnitRoster>(u).Count);
        }

        /// <summary>A split "platoon" of 3 stochastic squads (the CE8307 setup), its squads rostered; the platoon Physical.</summary>
        private static (Squad S, UnitResolutionFitSystem Fit, Hrot.Common.Systems.UnitHierarchySystem Hier, EntityRepository R, Entity U, Entity[] Squads) SplitPlatoon(bool order = true)
        {
            var (s, fit, hier, repo, u) = X2Squad(men: 3, order: order);
            repo.SetDisType(u, UnitDis.Land(UnitArm.Infantry, UnitEchelon.Platoon, 225));
            var squads = new Entity[3];
            for (int k = 0; k < 3; k++)
            {
                var c = repo.CreateEntity();
                repo.AddComponent(c, new SimTransform { Position = X2Post + new Vector3(k * 20f, 0f, 0f), Rotation = Quaternion.Identity });
                repo.AddComponent(c, new AggregateChild { Unit = u, Slot = (byte)(k + 1), Held = 0 });
                repo.AddComponent(c, new AggregateUnit { Resolution = AggregateResolution.Stochastic });
                repo.SetDisType(c, UnitDis.Land(UnitArm.Infantry, UnitEchelon.Squad, 225));
                squads[k] = c;
            }
            repo.GetComponentRW<AggregateUnit>(u).Resolution = AggregateResolution.Physical;
            for (int k = 0; k < 3; k++) repo.Bus.Publish(new CmdAssignSubordinate { Subordinate = squads[k], Commander = u, Designation = TacticalDesignation.Wingman });
            X2Frame(s, fit, hier);
            return (s, fit, hier, repo, u, squads);
        }

        /// <summary>⭐⭐ CE-8307 (R-8392, the review's F1 (b)) — THE ONE RELAY: a split platoon under a Move (no platoon Move play) runs <c>UnitRelayMove</c>,
        /// which passes the SAME order on — by KIND (<c>Order.Move</c>), the SAME json — to each squad in its roster, once. ✅ Red-proof: the relay
        /// sending <c>"{}"</c> instead of its own order's json ⇒ red.</summary>
        [Fact]
        public void CE8307_ASplitPlatoonsMove_IsRelayedToEverySquad_ByKind_SameParams()
        {
            var (s, fit, hier, repo, u, squads) = SplitPlatoon(order: false);
            const string json = "{\"Location\":[400,40,0],\"Speed\":2}";
            repo.Bus.PublishManaged(new AssignBehaviorEvent { Entity = u, BehaviorName = Hrot.AI.Behaviors.Brains.UnitRelayBehaviors.UnitRelayMove, Origin = BehaviorOrigin.Operator, RequestId = X2Request, JsonParams = json });
            SubordinateOrders? held = null;
            bool ran = false;
            for (int i = 0; i < 10 && held == null; i++)
            {
                X2Frame(s, fit, hier);
                if (s.TaskName == Hrot.AI.Behaviors.Brains.UnitRelayBehaviors.UnitRelayMove)
                {
                    ran = true;
                    Assert.Equal(OrderKind.Move, CurrentOrders.Of(repo, u).Kind);
                    if (SubordinateOrders.Of(repo, u) is { Orders.Count: 3 } h) held = h;   // ⚠ the bare squads here run nothing: none ever starts it
                }
            }
            Assert.True(ran, "the relay ran");
            Assert.NotNull(held);
            Assert.Equal(3, held!.Orders.Count);
            foreach (var e in held.Orders)
            {
                Assert.Contains(e.Member, squads);
                Assert.Equal("Order.Move", e.Behavior);
                Assert.Equal(json, e.Json);
            }
            // ⭐ CE-4685 — a squad GIVEN the order but not yet running it is still busy (StartGraceSeconds): the relay does not end under it, so ending
            //   cannot clear the squads' moves as they start (📌 llm-company-aggregated: every split platoon stopped where it split). ✅ Red-proof: the
            //   grace dropped ⇒ the relay ends the frame after ordering ⇒ red. And a squad that never starts still lets it end after the grace (no hang).
            for (int i = 0; i < 60; i++) X2Frame(s, fit, hier);   // ~1 s
            Assert.Equal(Hrot.AI.Behaviors.Brains.UnitRelayBehaviors.UnitRelayMove, s.TaskName);
            for (int i = 0; i < 250; i++) X2Frame(s, fit, hier);  // past the 3 s grace
            Assert.NotEqual(Hrot.AI.Behaviors.Brains.UnitRelayBehaviors.UnitRelayMove, s.TaskName);
        }

        /// <summary>⭐ CE-8307 (R-8392, F1 (a)) — a platoon's Attack has a physical play (<c>PlatoonAttack</c> fulfils Attack): its automatic split is not held.
        /// ⭐ CE-4686 ③ (AMENDS R-8392's hold-back half; backend, cross-lane): a HOLD-BACK attack is support by fire — at Physical it re-resolves to the
        /// kind SupportByFire's play (a platoon: <c>PlatoonSupportByFire</c>, which never closes), its Position (where it stands) carried and HoldBack
        /// dropped, so its split is not held either; a COMPANY's hold-back attack (no company support-by-fire play) still waits (X2-D). 📐 Measured on
        /// llm-company-aggregated (Haiku): the old rule CLEARED a split platoon's support by fire — TaskFailed 150 m short of its support position.
        /// ✅ Red-proof: the SupportByFire re-resolution dropped ⇒ the platoon's hold-back attack waits again ⇒ red.</summary>
        [Fact]
        public void CE8307_APlatoonsAttack_HasAPhysicalPlay_AHoldBackAttackBecomesItsSupportByFire()
        {
            foreach (bool holdBack in new[] { false, true })
                foreach (var echelon in new[] { UnitEchelon.Platoon, UnitEchelon.Company })
                {
                    var (s, fit, _, repo, u) = X2Squad(men: 3, order: false);
                    repo.SetDisType(u, UnitDis.Land(UnitArm.Infantry, echelon, 225));
                    string json = "{" + Loc("Position", X2Post + new Vector3(300f, 0f, 0f)) + (holdBack ? ",\"HoldBack\":true" : "") + "}";
                    repo.Bus.PublishManaged(new AssignBehaviorEvent
                    {
                        Entity = u, BehaviorName = ResolutionRoutingTable.Plays.Attack, Origin = BehaviorOrigin.Operator, RequestId = X2Request, JsonParams = json,
                    });
                    s.Tick(); s.Tick(); s.Tick();
                    Assert.Equal(ResolutionRoutingTable.Plays.Attack, s.TaskName);
                    string attackPlay = echelon == UnitEchelon.Platoon ? "PlatoonAttack" : "CompanyAttack";
                    Assert.Equal(attackPlay, OrderReissue.PlayFor(repo, u, AggregateResolution.Physical, OrderKind.Attack, fit.IsBehaviour));
                    var why = OrderReissue.BlockReason(repo, u, AggregateResolution.Physical, fit.IsBehaviour);
                    string? want = OrderReissue.PlayFor(repo, u, AggregateResolution.Physical, OrderKind.Attack, fit.IsBehaviour, json);
                    if (!holdBack) { Assert.Null(why); Assert.Equal(attackPlay, want); continue; }
                    if (echelon == UnitEchelon.Company) { Assert.NotNull(why); Assert.Null(want); continue; }   // no company support-by-fire play: waits
                    Assert.Null(why);
                    Assert.Equal("PlatoonSupportByFire", want);
                    string carried = OrderReissue.ParamsFor(want!, json, toStochastic: false);
                    Assert.Contains("\"Position\"", carried);
                    Assert.DoesNotContain("HoldBack", carried);
                }
        }
    }
}

