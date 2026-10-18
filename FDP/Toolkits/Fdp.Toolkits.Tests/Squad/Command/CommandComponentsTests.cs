using System.Numerics;
using System.Text.Json;
using Fdp.Core;
using Fdp.Core.Serialization;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Squad.Command;
using Xunit;

namespace Fdp.Toolkits.Tests.Squad.Command
{
    /// <summary>⭐ <c>CE-2291</c> CC-A — a commander's three components and the requirements event (Command Chain §6, §8). New feature,
    /// no suite before.</summary>
    public sealed class CommandComponentsTests
    {
        private static EntityRepository World()
        {
            var world = new EntityRepository();
            world.RegisterComponent<CommandPicture>();
            world.RegisterComponent<ReportingRequirements>();
            world.RegisterComponent<ReportOutbox>();
            world.Bus.Register<SetReportingEvent>();
            return world;
        }

        private static void Send(EntityRepository world, ReportingRequirementsSystem sys, in SetReportingEvent evt)
        {
            world.Bus.Publish(evt);
            world.Bus.SwapBuffers();
            sys.Execute(world, 0.016f);
        }

        /// <summary>⭐ CC-2 — the ROE twin: an order sets the requirements, a lower origin cannot change them, an all-zero event withdraws them
        /// (and then anyone may set them again). ✅ Red-proof: the origin gate removed ⇒ the Sop event overwrites the Superior's set.</summary>
        [Fact]
        public void CE2291_SetReporting_ALowerOriginIsRefused_AndAllZeroWithdraws()
        {
            var world = World();
            var sys = new ReportingRequirementsSystem();
            var squad = world.CreateEntity();

            Send(world, sys, new SetReportingEvent { Entity = squad, Origin = BehaviorOrigin.Superior,
                Pir = PirFlags.AnyElement | PirFlags.PirMachineGun, Ffir = FfirFlags.EveryPhase, SitrepEverySeconds = 15f, PirArea = 77 });
            var req = world.GetComponentRO<ReportingRequirements>(squad);
            Assert.Equal((PirFlags.AnyElement | PirFlags.PirMachineGun, FfirFlags.EveryPhase, 15f, 77L, BehaviorOrigin.Superior),
                         (req.Pir, req.Ffir, req.SitrepEverySeconds, req.PirArea, req.SetBy));

            Send(world, sys, new SetReportingEvent { Entity = squad, Origin = BehaviorOrigin.Sop, Pir = PirFlags.InArea });
            Assert.Equal(PirFlags.AnyElement | PirFlags.PirMachineGun, world.GetComponentRO<ReportingRequirements>(squad).Pir);
            Assert.Equal(1, sys.RefusedCount);

            Send(world, sys, new SetReportingEvent { Entity = squad, Origin = BehaviorOrigin.Superior });   // withdraw
            req = world.GetComponentRO<ReportingRequirements>(squad);
            Assert.Equal((PirFlags.NoPir, 0f, BehaviorOrigin.Unmarked), (req.Pir, req.SitrepEverySeconds, req.SetBy));

            Send(world, sys, new SetReportingEvent { Entity = squad, Origin = BehaviorOrigin.Sop, SitrepEverySeconds = 30f });
            Assert.Equal(30f, world.GetComponentRO<ReportingRequirements>(squad).SitrepEverySeconds);   // withdrawn ⇒ anyone may set
            world.Dispose();
        }

        /// <summary>⭐ <c>CE-2299</c> — the <c>RoeSystem</c> rule (CE-3251): on a node holding a REPLICA of the unit's brain the requirements are
        /// left to the owner (the egress forwards them, wire kind 3); a world with no network identity applies them as before.</summary>
        [Fact]
        public void CE2299_AReplicasRequirements_AreLeftToTheBrainsOwner()
        {
            var world = World();
            world.RegisterComponent<BehaviorState>();
            world.RegisterComponent<Fdp.Toolkit.Replication.Components.NetworkIdentity>();
            var sys = new ReportingRequirementsSystem();
            var replica = world.CreateEntity();
            world.AddComponent(replica, new BehaviorState());   // no authority: the brain runs on another node
            world.AddComponent(replica, new Fdp.Toolkit.Replication.Components.NetworkIdentity { Value = 7 });
            var owned = world.CreateEntity();
            world.AddComponent(owned, new BehaviorState());
            world.AddComponent(owned, new Fdp.Toolkit.Replication.Components.NetworkIdentity { Value = 8 });
            world.SetAuthority<BehaviorState>(owned, true);
            var offline = world.CreateEntity();
            world.AddComponent(offline, new BehaviorState());

            foreach (var e in new[] { replica, owned, offline })
                Send(world, sys, new SetReportingEvent { Entity = e, Origin = BehaviorOrigin.Superior, SitrepEverySeconds = 10f });

            Assert.False(world.HasComponent<ReportingRequirements>(replica));
            Assert.Equal(10f, world.GetComponentRO<ReportingRequirements>(owned).SitrepEverySeconds);
            Assert.Equal(10f, world.GetComponentRO<ReportingRequirements>(offline).SitrepEverySeconds);
            Assert.Equal(1, sys.ForwardedCount);
            world.Dispose();
        }

        /// <summary>⭐ CC-16 (R-2050) — every buffer is a fixed-list wrapper, so the entity dump prints the USED elements as a plain array —
        /// no special reader. ✅ Red-proof: a third field in a wrapper ⇒ <c>FixedListShape</c> refuses it and the dump shows raw slots.</summary>
        [Fact]
        public void CE2291_EveryBuffer_IsAFixedList_AndTheDumpShowsAPlainArray()
        {
            Assert.True(FixedListShape.TryGet(typeof(ReportedElementList), out var e1, out int c1));
            Assert.True(FixedListShape.TryGet(typeof(SubordinateRowList), out var e2, out int c2));
            Assert.True(FixedListShape.TryGet(typeof(PendingRequestList), out var e3, out int c3));
            Assert.True(FixedListShape.TryGet(typeof(QueuedReportList), out var e4, out int c4));
            Assert.Equal((typeof(ReportedElement), 8, typeof(SubordinateRow), 6, typeof(PendingRequest), 4, typeof(QueuedReport), 8),
                         (e1, c1, e2, c2, e3, c3, e4, c4));

            var picture = new CommandPicture();
            picture.Elements.Items[0] = new ReportedElement { At = new Vector3(1, 2, 0), Count = 3, Size = ElementSize.Team, FromNetId = 5002, Seq = 4 };
            picture.Elements.Items[1] = new ReportedElement { At = new Vector3(9, 9, 0), Count = 1, Size = ElementSize.Individual, FromNetId = 5003 };
            picture.Elements.Count = 2;
            picture.Subordinates.Items[0] = new SubordinateRow { UnitNetId = 5002, Progress = ProgressKind.Set, Strength = StatusColour.Green };
            picture.Subordinates.Count = 1;

            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(picture, FdpJsonOptionsRegistry.DefaultRelaxed));
            var root = doc.RootElement;
            Assert.Equal(JsonValueKind.Array, root.GetProperty("Elements").ValueKind);
            Assert.Equal(2, root.GetProperty("Elements").GetArrayLength());
            Assert.Equal(1, root.GetProperty("Subordinates").GetArrayLength());
            Assert.Equal(0, root.GetProperty("Requests").GetArrayLength());
            Assert.Equal("Set", root.GetProperty("Subordinates")[0].GetProperty("Progress").GetString());   // enums as strings (R-2007)
        }
    }
}
