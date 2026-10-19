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

        /// <summary>⭐ <c>CE-3251</c> (BR-13, backend) — on a node holding a REPLICA of the subordinate's brain the requirements are left
        /// to the owner (the tactical-intent egress forwards them); a world with no network identity still applies them. ✅ Red-proof: drop
        /// the replica skip in <c>ReportingRequirementsSystem.Execute</c> ⇒ the replica gets the requirements.</summary>
        [Fact]
        public void CE3251_AReplicasReportingRequirements_AreLeftToTheBrainsOwner()
        {
            var world = World();
            world.RegisterComponent<BehaviorState>();
            world.RegisterComponent<Fdp.Toolkit.Replication.Components.NetworkIdentity>();
            var sys = new ReportingRequirementsSystem();
            var replica = world.CreateEntity();
            world.AddComponent(replica, new BehaviorState());   // no authority: the brain runs on another node
            world.AddComponent(replica, new Fdp.Toolkit.Replication.Components.NetworkIdentity { Value = 7 });
            var offline = world.CreateEntity();
            world.AddComponent(offline, new BehaviorState());

            Send(world, sys, new SetReportingEvent { Entity = replica, Origin = BehaviorOrigin.Superior, SitrepEverySeconds = 15f });
            Send(world, sys, new SetReportingEvent { Entity = offline, Origin = BehaviorOrigin.Superior, SitrepEverySeconds = 15f });

            Assert.False(world.HasComponent<ReportingRequirements>(replica));
            Assert.Equal(15f, world.GetComponentRO<ReportingRequirements>(offline).SitrepEverySeconds);
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
    
        // ── CE-2292 CC-B — the picture guards and the outbox ──────────────────────────────────────────────────────────────

        private static (EntityRepository world, Entity platoon, Entity alpha, Entity bravo) Platoon()
        {
            var world = World();
            world.RegisterComponent<Fdp.Core.CommandHierarchy.UnitRoster>();
            world.RegisterComponent<SimTransform>();
            var platoon = world.CreateEntity();
            var alpha = world.CreateEntity();
            var bravo = world.CreateEntity();
            var roster = new Fdp.Core.CommandHierarchy.UnitRoster();
            Fdp.Core.CommandHierarchy.UnitRoster.Add(ref roster, alpha);
            Fdp.Core.CommandHierarchy.UnitRoster.Add(ref roster, bravo);
            world.AddComponent(platoon, roster);
            world.AddComponent(platoon, new SimTransform { Position = Vector3.Zero });
            return (world, platoon, alpha, bravo);
        }

        private static void At(EntityRepository world, double t) => world.SetSingleton(new GlobalTime { TotalTime = t, DeltaTime = 0.016f, TimeScale = 1f });

        /// <summary>Writes a row as the staff's inbox will (CC-C).</summary>
        private static void Reported(EntityRepository world, Entity platoon, Entity sub, ProgressKind progress, StatusColour strength, float at)
        {
            int row = CommandPictureOps.EnsureRow(world, platoon, CommandPictureOps.NetIdOf(world, sub));
            ref var r = ref world.GetComponentRW<CommandPicture>(platoon).Subordinates.Items[row];
            r.Progress = progress; r.Strength = strength; r.LastReportAt = at;
        }

        /// <summary>⭐ CC-B — <c>ProgressReached</c> names the subordinate by roster SLOT and is true for its latest progress; a NEW order
        /// (<c>OrderGiven</c>) forgets it, so a guard waiting for this leg's Set is not passed by the last leg's. ✅ Red-proof: OrderGiven
        /// not resetting ⇒ the guard stays true after the new order.</summary>
        [Fact]
        public void CE2292_ProgressReached_BySlot_AndANewOrderForgetsTheLastLegsProgress()
        {
            var (world, platoon, alpha, bravo) = Platoon();
            var set = new ProgressReachedParams { Slot = 1, Progress = ProgressKind.Set };
            Assert.False(PictureConditions.ProgressReached(ref set, platoon, world));
            Reported(world, platoon, bravo, ProgressKind.Set, StatusColour.Green, 1f);
            Assert.True(PictureConditions.ProgressReached(ref set, platoon, world));
            var alphaSet = new ProgressReachedParams { Slot = 0, Progress = ProgressKind.Set };
            Assert.False(PictureConditions.ProgressReached(ref alphaSet, platoon, world));

            CommandPictureOps.OrderGiven(world, platoon, bravo);
            Assert.False(PictureConditions.ProgressReached(ref set, platoon, world));
            var none = new ProgressReachedParams { Slot = 1, Progress = ProgressKind.NoProgress };
            Assert.False(PictureConditions.ProgressReached(ref none, platoon, world));   // "no progress" is never a milestone
            world.Dispose();
        }

        /// <summary>⭐ CC-B — colour (this or worse), freshness (overdue), elements by size / distance / age, pending requests.</summary>
        [Fact]
        public void CE2292_TheOtherGuards_ReadThePicture()
        {
            var (world, platoon, alpha, _) = Platoon();
            At(world, 100);
            Reported(world, platoon, alpha, ProgressKind.NoProgress, StatusColour.Amber, 90f);
            var amber = new SubordinateColourParams { Slot = 0, AtLeast = StatusColour.Amber };
            var red = new SubordinateColourParams { Slot = 0, AtLeast = StatusColour.Red };
            Assert.True(PictureConditions.SubordinateColour(ref amber, platoon, world));
            Assert.False(PictureConditions.SubordinateColour(ref red, platoon, world));
            var fresh15 = new SubordinateFreshParams { Slot = 0, WithinSeconds = 15f };
            var fresh5 = new SubordinateFreshParams { Slot = 0, WithinSeconds = 5f };
            Assert.True(PictureConditions.SubordinateFresh(ref fresh15, platoon, world));
            Assert.False(PictureConditions.SubordinateFresh(ref fresh5, platoon, world));   // overdue

            ref var pic = ref world.GetComponentRW<CommandPicture>(platoon);
            pic.Elements.Items[pic.Elements.Count++] = new ReportedElement { At = new Vector3(0, 300, 0), Size = ElementSize.Team, ObservedAt = 95f };
            var near = new ElementReportedParams { MinSize = ElementSize.Team, WithinMetres = 400f, FreshSeconds = 30f };
            var squad = new ElementReportedParams { MinSize = ElementSize.SquadSize, WithinMetres = 400f };
            var close = new ElementReportedParams { MinSize = ElementSize.Individual, WithinMetres = 200f };
            var stale = new ElementReportedParams { FreshSeconds = 2f };
            Assert.True(PictureConditions.ElementReported(ref near, platoon, world));
            Assert.False(PictureConditions.ElementReported(ref squad, platoon, world));
            Assert.False(PictureConditions.ElementReported(ref close, platoon, world));
            Assert.False(PictureConditions.ElementReported(ref stale, platoon, world));

            var anyReq = new PendingRequestParams();
            var casevac = new PendingRequestParams { Kind = RequestKind.Casevac };
            Assert.False(PictureConditions.PendingRequest(ref anyReq, platoon, world));
            pic = ref world.GetComponentRW<CommandPicture>(platoon);
            pic.Requests.Items[pic.Requests.Count++] = new PendingRequest { Kind = RequestKind.FireSupport };
            Assert.True(PictureConditions.PendingRequest(ref anyReq, platoon, world));
            Assert.False(PictureConditions.PendingRequest(ref casevac, platoon, world));
            world.Dispose();
        }

        /// <summary>⭐ CC-B — the outbox numbers each report per sender (CB-32); ⭐ <c>CE-2304</c> — full, it drops its OLDEST for the new
        /// one (newest wins): the ninth is kept and the first is gone. ✅ Red-proof: refusing the ninth (the old rule) ⇒ the newest is lost.</summary>
        [Fact]
        public void CE2292_TheOutbox_NumbersEachReport_AndWhenFullDropsTheOldest()
        {
            var (world, platoon, _, _) = Platoon();
            for (int i = 0; i < ReportOutbox.MaxQueued; i++)
                Assert.True(ReportQueue.Enqueue(world, platoon, new CommandReport { Kind = ReportKind.Progress }, 3f));
            Assert.True(ReportQueue.Enqueue(world, platoon, new CommandReport { Kind = ReportKind.Progress, Subtype = (byte)ProgressKind.Set }, 3f));
            var outbox = world.GetComponentRO<ReportOutbox>(platoon);
            Assert.Equal((ReportOutbox.MaxQueued, (uint)ReportOutbox.MaxQueued + 1, 2u, (uint)ReportOutbox.MaxQueued + 1, (byte)ProgressKind.Set),
                         (outbox.Queued.Count, outbox.NextSeq, outbox.Queued.Items[0].Report.Seq,
                          outbox.Queued.Items[ReportOutbox.MaxQueued - 1].Report.Seq, outbox.Queued.Items[ReportOutbox.MaxQueued - 1].Report.Subtype));
            world.Dispose();
        }
}
}
