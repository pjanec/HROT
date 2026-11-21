using System.Numerics;
using CarKinem.Convoy;
using CarKinem.Formation;
using Fbt;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Navigation;
using Hrot.AI.Behaviors.Brains;
using Hrot.Map.Common;
using Hrot.Map.Definitions.Behavior;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐⭐ <c>CE-9004</c> (C-9, C-10; 📄 docs/designs/vehicle-tactics/DESIGN_Road_Convoy.md) — the convoy ORDER on each vehicle: the lead joins as rank 0 and asks its
    /// move with the convoy's road use (our route: <c>StronglyPrefer</c>; SUMO with the beacon: <c>InTrafficPriority</c>); a follower joins behind its lead, stops its own
    /// move, and is done when the lead is; a vehicle no longer running <c>PlatformConvoy</c> (contact, a new order) leaves the column at once.
    /// 🔴 Red-proofs: the membership sweep skipping the hash test ⇒ the "still running" vehicle loses its membership ⇒ red; <c>LeadRoadUse</c> always
    /// <c>Unspecified</c> ⇒ the road-use rows red; <c>TrafficRuns</c> always true ⇒ the no-traffic rows red (CE-9010).
    /// </summary>
    public sealed class PlatformConvoyTests
    {
        private static EntityRepository World()
        {
            var w = new EntityRepository();
            HrotSharedComponentRegistry.RegisterAll(w);
            Hrot.CGF.CgfComponentRegistry.RegisterAll(w);
            return w;
        }

        private static Entity Vehicle(EntityRepository w, Vector3 at)
        {
            var e = w.CreateEntity();
            w.AddComponent(e, new SimTransform { Position = at, Rotation = Quaternion.Identity });
            w.AddComponent(e, new LocomotionChannel());
            w.AddComponent(e, new BehaviorState { ActiveBehaviorHash = BehaviorHash.FromName(BehaviorNames.PlatformConvoy), Origin = BehaviorOrigin.Operator });
            return e;
        }

        private static unsafe RoadUse AskedRoadUse(EntityRepository w, Entity e)
        {
            var ch = w.GetComponentRO<LocomotionChannel>(e);
            Assert.Equal(NavigationConstants.ActionIdMoveTo, ch.ActiveAction);
            MoveToParams p = *(MoveToParams*)ch.Params;
            return NavigationConstants.RoadUseOf(p.Flags);
        }

        private static PlatformConvoyBehavior.ConvoyParams Params(Entity lead, byte rank, ConvoyMode mode, bool priority = false) => new()
        {
            LeadPacked = lead == default ? 0 : (long)lead.PackedValue, Destination = new Vector3(400, 0, 0), Gap = 15f, Speed = 10f,
            Rank = rank, Mode = (byte)mode, Priority = priority ? (byte)1 : (byte)0,
        };

        [Fact]
        public void TheLead_JoinsAsRankZero_AndAsksItsMove_WithTheConvoysRoadUse()
        {
            // ⭐ CE-9010 — Traffic / Auto hand the lead to SUMO only where SUMO traffic runs (a SUMO net + the traffic settings); else our own route
            foreach (var (mode, priority, traffic, want) in new[] {
                         (ConvoyMode.Route, false, true, RoadUse.StronglyPrefer),
                         (ConvoyMode.Auto, false, true, RoadUse.InTraffic), (ConvoyMode.Auto, false, false, RoadUse.StronglyPrefer),
                         (ConvoyMode.Traffic, true, true, RoadUse.InTrafficPriority), (ConvoyMode.Traffic, false, true, RoadUse.InTraffic),
                         (ConvoyMode.Traffic, true, false, RoadUse.StronglyPrefer) })
            {
                var w = World();
                if (traffic)
                {
                    w.SetSingletonManaged(new Fdp.Toolkit.Terrain.TerrainDefinition { Name = "t", SumoNet = new Fdp.Toolkit.Terrain.TerrainSumoNet("t.net.xml", 0, 0) });
                    w.AddComponent(w.CreateEntity(), Fdp.Toolkit.Traffic.TrafficDemand.Default);
                }
                var lead = Vehicle(w, Vector3.Zero);
                var p = Params(default, 0, mode, priority); var s = default(PlatformConvoyBehavior.ConvoyState);
                Assert.Equal(NodeStatus.Running, PlatformConvoyBehavior.Tick(ref p, ref s, lead, w));
                var c = w.GetComponentRO<FormationController>(lead);
                Assert.Equal((FormationType.Column, 15f), (c.Type, c.Params.Spacing));   // the lead leads a column
                Assert.False(w.HasComponent<FormationFollower>(lead));
                Assert.Equal(want, AskedRoadUse(w, lead));
                w.Dispose();
            }
        }

        /// <summary>⭐ <c>CE-9012</c> — a convoy given an authored ROUTE: the lead follows it (the one <c>CgfNodes.WriteFollowRoute</c> writer, as <c>FollowRoute</c>
        /// does) instead of routing to the destination itself. 🔴 Red-proof: the lead ignoring <c>RouteNet</c> ⇒ it asks a MoveTo ⇒ red.</summary>
        [Fact]
        public unsafe void ALeadGivenARoute_FollowsIt_AsFollowRouteDoes()
        {
            var w = World();
            var lead = Vehicle(w, Vector3.Zero);
            var p = Params(default, 0, ConvoyMode.Route); p.RouteNet = 9811;
            var s = default(PlatformConvoyBehavior.ConvoyState);
            Assert.Equal(NodeStatus.Running, PlatformConvoyBehavior.Tick(ref p, ref s, lead, w));
            var ch = w.GetComponentRO<LocomotionChannel>(lead);
            Assert.Equal(NavigationConstants.ActionIdFollowRoute, ch.ActiveAction);
            Assert.Equal(9811, (*(Fdp.Toolkit.Navigation.FollowRouteParams*)ch.Params).RouteEntityId);
            w.Dispose();
        }

        /// <summary>⭐ <c>CE-9014</c> / <c>CE-9017</c> — a lead whose own move FAILS: one that CANNOT move (its capabilities lost <c>CanMove</c> — a mobility kill)
        /// fails the order and ends its column, so its followers finish; one that CAN move (blocked: a queue, a breakdown) WAITS and asks again after
        /// <see cref="PlatformConvoyBehavior.RetrySeconds"/>. 📌 cv-blocked-road: the executor reported FailedBlocked 6 s behind a broken truck and the
        /// column never moved again. 🔴 Red-proofs: the CanMove test dropped ⇒ the blocked lead fails ⇒ red; no retry ⇒ the move stays failed ⇒ red.</summary>
        [Fact]
        public void ALeadWhoseMoveFails_FailsTheOrderIfItCannotMove_ElseWaitsAndAsksAgain()
        {
            var w = World();
            var lead = Vehicle(w, Vector3.Zero);
            var f = Vehicle(w, new Vector3(-15, 0, 0));
            var lp = Params(default, 0, ConvoyMode.Route); var ls = default(PlatformConvoyBehavior.ConvoyState);
            var fp = Params(lead, 1, ConvoyMode.Route); var fs = default(PlatformConvoyBehavior.ConvoyState);
            void At(double t) => w.SetSingletonUnmanaged(new GlobalTime { TotalTime = t, DeltaTime = 0.05f, TimeScale = 1f });
            At(10);
            PlatformConvoyBehavior.Tick(ref lp, ref ls, lead, w);
            PlatformConvoyBehavior.Tick(ref fp, ref fs, f, w);

            // blocked: the executor gave up, but the vehicle can move — the convoy waits, then asks again
            w.GetComponentRW<LocomotionChannel>(lead).Status = NodeStatus.Failure;
            uint first = w.GetComponentRO<LocomotionChannel>(lead).ActionInstanceId;
            Assert.Equal(NodeStatus.Running, PlatformConvoyBehavior.Tick(ref lp, ref ls, lead, w));
            At(11);
            Assert.Equal(NodeStatus.Running, PlatformConvoyBehavior.Tick(ref lp, ref ls, lead, w));
            Assert.Equal(first, w.GetComponentRO<LocomotionChannel>(lead).ActionInstanceId);   // not yet
            At(12.5);
            Assert.Equal(NodeStatus.Running, PlatformConvoyBehavior.Tick(ref lp, ref ls, lead, w));
            var ch = w.GetComponentRO<LocomotionChannel>(lead);
            Assert.True(ch.ActionInstanceId != first && ch.Status == NodeStatus.Running, "the blocked lead asked again");
            Assert.True(PlatformConvoyBehavior.Leads(in w.GetComponentRO<FormationController>(lead)));   // the column stands, whole

            // it CANNOT move any more: the order fails and the column ends
            w.AddComponent(lead, new ActorCapabilityState());   // no CanMove
            w.GetComponentRW<LocomotionChannel>(lead).Status = NodeStatus.Failure;
            Assert.Equal(NodeStatus.Failure, PlatformConvoyBehavior.Tick(ref lp, ref ls, lead, w));
            Assert.False(PlatformConvoyBehavior.Leads(in w.GetComponentRO<FormationController>(lead)));
            Assert.Equal(NodeStatus.Success, PlatformConvoyBehavior.Tick(ref fp, ref fs, f, w));
            Assert.False(w.HasComponent<FormationFollower>(f));
            w.Dispose();
        }

        /// <summary>⭐ <c>CE-9015</c> — a REACTION that drives the vehicle while the convoy order is paused (it writes the locomotion channel) does not end the
        /// column: on resume the lead issues its move again and a follower stops its own move again — and a reaction's leftover Failure is not the
        /// lead's move failing. 📌 cv-ambush: the hit lead's VehicleFightFromCover ended at once, the order resumed and the lead stood for good.
        /// 🔴 Red-proof: no re-issue on resume ⇒ the channel keeps the reaction's action ⇒ red.</summary>
        [Fact]
        public void AfterAReactionDroveIt_TheResumedLeadMovesAgain_AndTheFollowerStopsAgain()
        {
            var w = World();
            var lead = Vehicle(w, Vector3.Zero);
            var f = Vehicle(w, new Vector3(-15, 0, 0));
            var lp = Params(default, 0, ConvoyMode.Route); var ls = default(PlatformConvoyBehavior.ConvoyState);
            var fp = Params(lead, 1, ConvoyMode.Route); var fs = default(PlatformConvoyBehavior.ConvoyState);
            PlatformConvoyBehavior.Tick(ref lp, ref ls, lead, w);
            PlatformConvoyBehavior.Tick(ref fp, ref fs, f, w);
            foreach (var e in new[] { lead, f })
            {
                ref var ch = ref w.GetComponentRW<LocomotionChannel>(e);   // a reaction (VehicleFightFromCover) takes the channel, then ends failing
                unchecked { ch.ActionInstanceId++; }
                ch.ActiveAction = NavigationConstants.ActionIdMoveTo; ch.Status = NodeStatus.Failure;   // its own move, failed
            }
            Assert.Equal(NodeStatus.Running, PlatformConvoyBehavior.Tick(ref lp, ref ls, lead, w));   // ⛔ not Failure: that was the reaction's
            var lc = w.GetComponentRO<LocomotionChannel>(lead);
            Assert.Equal((NavigationConstants.ActionIdMoveTo, NodeStatus.Running), (lc.ActiveAction, lc.Status));   // its move, issued again
            Assert.Equal(NodeStatus.Running, PlatformConvoyBehavior.Tick(ref fp, ref fs, f, w));
            Assert.Equal(0, w.GetComponentRO<LocomotionChannel>(f).ActiveAction);   // ⛔ the reaction's move left in place would drive it beside the column
            w.Dispose();
        }

        [Fact]
        public void AFollower_JoinsBehindItsLead_TheArrivedColumnClosesUp_ThenTheFollowerIsDone()
        {
            var w = World();
            var lead = Vehicle(w, Vector3.Zero);
            var f = Vehicle(w, new Vector3(-15, 0, 0));
            var lp = Params(default, 0, ConvoyMode.Route); var ls = default(PlatformConvoyBehavior.ConvoyState);
            var fp = Params(lead, 2, ConvoyMode.Route); var fs = default(PlatformConvoyBehavior.ConvoyState);
            PlatformConvoyBehavior.Tick(ref lp, ref ls, lead, w);
            Assert.Equal(NodeStatus.Running, PlatformConvoyBehavior.Tick(ref fp, ref fs, f, w));
            var m = w.GetComponentRO<FormationFollower>(f);
            Assert.Equal((lead, (ushort)2, (byte)1), (m.Leader, m.SlotIndex, m.IsInFormation));

            void At(double t) => w.SetSingletonUnmanaged(new GlobalTime { TotalTime = t, DeltaTime = 0.05f, TimeScale = 1f });
            At(50);
            w.GetComponentRW<SimTransform>(lead).Position = new Vector3(380, 0, 0);   // ⭐ CE-9018 — 20 m short, but its executor says it ARRIVED (a SUMO shadow ends ≤ 25 m short)
            w.GetComponentRW<LocomotionChannel>(lead).Status = NodeStatus.Success;
            // ⭐ CE-9022 — arrived, it HALTS but leads its column CloseUpSeconds longer, so the followers close up (🔴 red-proof: ending at once ⇒ red)
            Assert.Equal(NodeStatus.Running, PlatformConvoyBehavior.Tick(ref lp, ref ls, lead, w));
            Assert.True(PlatformConvoyBehavior.Leads(in w.GetComponentRO<FormationController>(lead)));
            Assert.Equal(NodeStatus.Running, PlatformConvoyBehavior.Tick(ref fp, ref fs, f, w));   // still in its slot, closing up
            At(50 + PlatformConvoyBehavior.CloseUpSeconds - 0.5);
            Assert.Equal(NodeStatus.Running, PlatformConvoyBehavior.Tick(ref lp, ref ls, lead, w));
            At(50 + PlatformConvoyBehavior.CloseUpSeconds + 0.1);
            Assert.Equal(NodeStatus.Success, PlatformConvoyBehavior.Tick(ref lp, ref ls, lead, w));
            Assert.False(PlatformConvoyBehavior.Leads(in w.GetComponentRO<FormationController>(lead)));   // the column ended
            Assert.Equal(NodeStatus.Success, PlatformConvoyBehavior.Tick(ref fp, ref fs, f, w));
            Assert.False(w.HasComponent<FormationFollower>(f));
            w.Dispose();
        }

        [Fact]
        public void AVehicleNoLongerRunningTheConvoy_LeavesTheColumn_TheRestStay()
        {
            var w = World();
            var lead = Vehicle(w, Vector3.Zero);
            var hit = Vehicle(w, new Vector3(-15, 0, 0));
            var calm = Vehicle(w, new Vector3(-30, 0, 0));
            var column = new FormationController { Type = FormationType.Column, Params = new FormationParams { Spacing = 15f } };
            if (w.HasComponent<FormationController>(lead)) w.SetComponent(lead, column); else w.AddComponent(lead, column);
            w.AddComponent(hit, new FormationFollower { Leader = lead, SlotIndex = 1, IsInFormation = 1 });
            w.AddComponent(calm, new FormationFollower { Leader = lead, SlotIndex = 2, IsInFormation = 1 });
            var squadMate = Vehicle(w, new Vector3(0, 50, 0));   // a commander-led slot (no explicit leader): never the convoy's to remove
            w.AddComponent(squadMate, new FormationFollower { SlotIndex = 1, IsInFormation = 1 });
            w.GetComponentRW<BehaviorState>(squadMate) = new BehaviorState { ActiveBehaviorHash = BehaviorHash.FromName(BehaviorNames.PushThrough), Origin = BehaviorOrigin.Reaction };
            w.GetComponentRW<BehaviorState>(hit) = new BehaviorState { ActiveBehaviorHash = BehaviorHash.FromName(BehaviorNames.PushThrough), Origin = BehaviorOrigin.Reaction };

            new ConvoyMembershipSystem().Execute(w, 0.05f);

            Assert.False(w.HasComponent<FormationFollower>(hit));   // contact: its SOP drives it now
            Assert.True(PlatformConvoyBehavior.Leads(in w.GetComponentRO<FormationController>(lead)));
            Assert.True(w.HasComponent<FormationFollower>(calm));
            Assert.True(w.HasComponent<FormationFollower>(squadMate));

            // the LEAD taken by contact ends the whole column
            w.GetComponentRW<BehaviorState>(lead) = new BehaviorState { ActiveBehaviorHash = BehaviorHash.FromName(BehaviorNames.PushThrough), Origin = BehaviorOrigin.Reaction };
            new ConvoyMembershipSystem().Execute(w, 0.05f);
            Assert.False(PlatformConvoyBehavior.Leads(in w.GetComponentRO<FormationController>(lead)));
            w.Dispose();
        }
    }
}
