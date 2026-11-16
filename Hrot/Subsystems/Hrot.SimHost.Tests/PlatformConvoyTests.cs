using System.Numerics;
using CarKinem.Convoy;
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
    /// <c>Unspecified</c> ⇒ the road-use rows red.
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
            LeadPacked = lead == default ? 0 : (long)lead.PackedValue, Destination = new Vector3(400, 0, 0), Gap = 15f, MinGap = 7f, Speed = 10f,
            Rank = rank, Mode = (byte)mode, Priority = priority ? (byte)1 : (byte)0,
        };

        [Fact]
        public void TheLead_JoinsAsRankZero_AndAsksItsMove_WithTheConvoysRoadUse()
        {
            foreach (var (mode, priority, want) in new[] { (ConvoyMode.Route, false, RoadUse.StronglyPrefer), (ConvoyMode.Auto, false, RoadUse.StronglyPrefer),
                                                           (ConvoyMode.Traffic, true, RoadUse.InTrafficPriority), (ConvoyMode.Traffic, false, RoadUse.InTraffic) })
            {
                var w = World();
                var lead = Vehicle(w, Vector3.Zero);
                var p = Params(default, 0, mode, priority); var s = default(PlatformConvoyBehavior.ConvoyState);
                Assert.Equal(NodeStatus.Running, PlatformConvoyBehavior.Tick(ref p, ref s, lead, w));
                var m = w.GetComponentRO<ConvoyMember>(lead);
                Assert.Equal((lead, (byte)0), (m.Lead, m.Rank));
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
            var m = w.GetComponentRO<ConvoyMember>(f);
            Assert.Equal((lead, (byte)2, 15f), (m.Lead, m.Rank, m.Gap));

            w.GetComponentRW<SimTransform>(lead).Position = new Vector3(395, 0, 0);   // the lead arrives
            Assert.Equal(NodeStatus.Success, PlatformConvoyBehavior.Tick(ref lp, ref ls, lead, w));
            Assert.False(w.HasComponent<ConvoyMember>(lead));
            Assert.Equal(NodeStatus.Success, PlatformConvoyBehavior.Tick(ref fp, ref fs, f, w));
            Assert.False(w.HasComponent<ConvoyMember>(f));
            w.Dispose();
        }

        [Fact]
        public void AVehicleNoLongerRunningTheConvoy_LeavesTheColumn_TheRestStay()
        {
            var w = World();
            var lead = Vehicle(w, Vector3.Zero);
            var hit = Vehicle(w, new Vector3(-15, 0, 0));
            var calm = Vehicle(w, new Vector3(-30, 0, 0));
            w.AddComponent(lead, new ConvoyMember { Lead = lead, Rank = 0 });
            w.AddComponent(hit, new ConvoyMember { Lead = lead, Rank = 1 });
            w.AddComponent(calm, new ConvoyMember { Lead = lead, Rank = 2 });
            w.GetComponentRW<BehaviorState>(hit) = new BehaviorState { ActiveBehaviorHash = BehaviorHash.FromName(BehaviorNames.PushThrough), Origin = BehaviorOrigin.Reaction };

            new ConvoyMembershipSystem().Execute(w, 0.05f);

            Assert.False(w.HasComponent<ConvoyMember>(hit));   // contact: its SOP drives it now
            Assert.True(w.HasComponent<ConvoyMember>(lead));
            Assert.True(w.HasComponent<ConvoyMember>(calm));
            w.Dispose();
        }
    }
}
