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

        [Fact]
        public void AFollower_JoinsBehindItsLead_AndIsDoneWhenTheLeadArrives()
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

            w.GetComponentRW<SimTransform>(lead).Position = new Vector3(395, 0, 0);   // the lead arrives
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
