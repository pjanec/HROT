using System.Collections.Generic;
using System.Linq;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.ScenarioRules;
using Hrot.Map.Common.Dds;
using Hrot.Map.Common.Replication.Egress;
using Hrot.Map.Common.Replication.Ingress;
using Hrot.Map.Common.Translators;
using Hrot.NED.Descriptors;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐ rules S1 ① (📄 docs/DESIGN_Scenario_Rules.md §00.8, SR-120) — <c>dtObjectiveStatus</c>: an objective's status and links cross to
    /// every node. Round trip of every field, change-only publishing, the 0.5 s progress throttle (a state change goes at once), and a
    /// sample for an entity not known yet held until it is.
    /// </summary>
    [Collection("SimHostDds")]   // the pack test opens a real participant
    public sealed class ObjectiveStatusWireTests
    {
        private sealed class CapturingWriter : IDdsWriter<EntityObjectiveStatus>
        {
            public List<EntityObjectiveStatus> Written { get; } = new();
            public void Write(EntityObjectiveStatus sample) => Written.Add(sample);
            public void DisposeInstance(EntityObjectiveStatus key) { }
        }

        private static ObjectiveStatus Full() => new()
        {
            Role = ObjectiveRole.Measure, State = ObjectiveState.Failed, Closed = 1, Owner = ForceId.Hostile,
            ProgressCur = 3, ProgressNeed = 7, Value = 1.5f, Target = 9.25f, Score = 0.75f, Points = 12f, ChangedAt = 321.5,
            Reason = new FixedString64("held 60 s, 5 Blue / 0 Red"), Title = new FixedString64("Hold the bridge"), Kind = new FixedString32("HoldArea"),
            Required = 1, Scored = 1, Weight = 2.5f, VisibleSides = 0b110, FireCount = 4, LastFired = 99.25, TriggerState = TriggerState.Cooldown,
            DependsOnCount = 2, DependsOn = Ids(101, 102), ActsOnCount = 3, ActsOn = Ids(201, 202, 203),
            HoldFor = 1.5f, Delay = 2.5f, Cooldown = 3.5f, Deadline = 60f, ExpectedAt = 77.25,
            Group = new FixedString32("fire"), Pass = 0.8f,
        };

        private static NetIds8 Ids(params int[] v) { var ids = default(NetIds8); for (int i = 0; i < v.Length; i++) ids[i] = v[i]; return ids; }

        private static ObjectiveLinks Links() => new() { Subject = new EntityRef(41), Parent = new EntityRef(42) };

        private static (EntityRepository World, NetworkEntityMap Map, Entity E) Node(int primaryOwner, int localNode)
        {
            var w = new EntityRepository();
            w.RegisterComponent<NetworkIdentity>();
            w.RegisterComponent<NetworkAuthority>();
            w.RegisterComponent<ObjectiveStatus>();
            w.RegisterComponent<ObjectiveLinks>();
            var map = new NetworkEntityMap();
            var e = w.CreateEntity();
            w.AddComponent(e, new NetworkIdentity { Value = 80 });
            w.AddComponent(e, new NetworkAuthority(primaryOwner, localNode));
            w.AddComponent(e, Full());
            w.AddComponent(e, Links());
            map.Register(80, e);
            return (w, map, e);
        }

        /// <summary>Every field survives the wire form and back. ✅ Red-proof: any field dropped from <c>ToSample</c> or <c>FromSample</c> ⇒ red.</summary>
        [Fact]
        public void EveryField_RoundTrips()
        {
            var sample = EntityObjectiveStatusEgressTranslator.ToSample(80, Full(), Links());
            Assert.Equal((80, 41, 42), (sample.EntityId, sample.Subject, sample.Parent));
            var back = EntityObjectiveStatusIngressTranslator.FromSample(in sample);
            var want = Full();
            Assert.Equal(want.Role, back.Role); Assert.Equal(want.State, back.State); Assert.Equal(want.Closed, back.Closed); Assert.Equal(want.Owner, back.Owner);
            Assert.Equal(want.ProgressCur, back.ProgressCur); Assert.Equal(want.ProgressNeed, back.ProgressNeed);
            Assert.Equal(want.Value, back.Value); Assert.Equal(want.Target, back.Target); Assert.Equal(want.Score, back.Score); Assert.Equal(want.Points, back.Points);
            Assert.Equal(want.ChangedAt, back.ChangedAt); Assert.Equal("held 60 s, 5 Blue / 0 Red", back.Reason.ToString());
            Assert.Equal("Hold the bridge", back.Title.ToString()); Assert.Equal("HoldArea", back.Kind.ToString());
            Assert.Equal(want.Required, back.Required); Assert.Equal(want.Scored, back.Scored); Assert.Equal(want.Weight, back.Weight);
            Assert.Equal(want.VisibleSides, back.VisibleSides); Assert.Equal(want.FireCount, back.FireCount); Assert.Equal(want.LastFired, back.LastFired);
            Assert.Equal(TriggerState.Cooldown, back.TriggerState);   // ⭐ rules S2-c
            Assert.Equal("fire", sample.Group); Assert.Equal("fire", back.Group.ToString());   // ⭐ rules S4 E-3: the evaluation group crosses ✅ Red-proof: Group dropped from ToSample / FromSample ⇒ red
            Assert.Equal(0.8f, sample.Pass); Assert.Equal(0.8f, back.Pass);   // ⭐ rules S4 ②: the pass mark crosses ✅ Red-proof: Pass dropped from ToSample / FromSample ⇒ red
            // ⭐ rules V-1 / V-2: the rule's links and timing cross too (so ANY node answers GET /objectives with them)
            Assert.Equal((2, 101, 102), (back.DependsOnCount, back.DependsOn[0], back.DependsOn[1]));
            Assert.Equal((3, 201, 202, 203), (back.ActsOnCount, back.ActsOn[0], back.ActsOn[1], back.ActsOn[2]));
            Assert.Equal((1.5f, 2.5f, 3.5f, 60f, 77.25), (back.HoldFor, back.Delay, back.Cooldown, back.Deadline, back.ExpectedAt));
            var links = EntityObjectiveStatusIngressTranslator.LinksFromSample(in sample);
            Assert.Equal((41, 42), (links.Subject.NetworkId, links.Parent.NetworkId));
        }

        /// <summary>The owner publishes once and not again while nothing changed; a node that does not own the descriptor publishes nothing.
        /// ✅ Red-proof: the <c>SameWire</c> skip removed ⇒ red.</summary>
        [Fact]
        public void TheOwnersEgress_PublishesOnChange_AndNotWhenUnchanged_AndNotWithoutAuthority()
        {
            var (w, _, _) = Node(primaryOwner: 1, localNode: 1);
            using (w)
            {
                var wire = new CapturingWriter();
                var egress = new EntityObjectiveStatusEgressTranslator(wire);
                egress.ScanAndPublish(w);
                Assert.Single(wire.Written);
                w.SetSimulationTime(30f);                                    // well past the throttle window: only "unchanged" can suppress it
                egress.ScanAndPublish(w);
                Assert.Single(wire.Written);
                Assert.Equal(1, egress.SentSampleCount);
            }
            var (other, _, _) = Node(primaryOwner: 2, localNode: 1);
            using (other)
            {
                var wire = new CapturingWriter();
                new EntityObjectiveStatusEgressTranslator(wire).ScanAndPublish(other);
                Assert.Empty(wire.Written);
            }
        }

        /// <summary>SR-120: a progress-only change inside the 0.5 s window is NOT published; a state change IS, at once; the held progress
        /// goes out once the window has passed. ✅ Red-proof: the throttle removed ⇒ red (the progress sample goes at once); every change
        /// throttled ⇒ red (the state change waits).</summary>
        [Fact]
        public void AProgressOnlyChange_IsThrottled_AStateChange_IsNot()
        {
            var (w, _, e) = Node(primaryOwner: 1, localNode: 1);
            using (w)
            {
                var wire = new CapturingWriter();
                var egress = new EntityObjectiveStatusEgressTranslator(wire);
                w.SetSimulationTime(10f);
                egress.ScanAndPublish(w);
                Assert.Single(wire.Written);

                w.SetSimulationTime(10.2f);
                w.GetComponentRW<ObjectiveStatus>(e).ProgressCur = 5;
                egress.ScanAndPublish(w);
                Assert.Single(wire.Written);                                  // progress only, 0.2 s later: held

                w.SetSimulationTime(10.3f);
                w.GetComponentRW<ObjectiveStatus>(e).State = ObjectiveState.Achieved;
                egress.ScanAndPublish(w);
                Assert.Equal(2, wire.Written.Count);                          // a state change: at once, with the held progress
                Assert.Equal(((byte)ObjectiveState.Achieved, 5), (wire.Written[1].State, wire.Written[1].ProgressCur));

                w.SetSimulationTime(10.4f);
                w.GetComponentRW<ObjectiveStatus>(e).ProgressCur = 6;
                egress.ScanAndPublish(w);
                Assert.Equal(2, wire.Written.Count);                          // held again

                w.SetSimulationTime(10.9f);                                   // the window has passed
                egress.ScanAndPublish(w);
                Assert.Equal(3, wire.Written.Count);
                Assert.Equal(6, wire.Written[2].ProgressCur);
            }
        }

        /// <summary>⭐ rules S4 E-3 — the evaluation <c>Group</c> is a DISPLAY field: a change goes at once, inside the progress window. ✅ Red-proof: <c>Group</c> left out of <c>SameDisplayAndState</c> ⇒ red.</summary>
        [Fact]
        public void AGroupChange_GoesAtOnce_InsideTheProgressWindow()
        {
            var (w, _, e) = Node(primaryOwner: 1, localNode: 1);
            using (w)
            {
                var wire = new CapturingWriter();
                var egress = new EntityObjectiveStatusEgressTranslator(wire);
                w.SetSimulationTime(10f);
                egress.ScanAndPublish(w);
                w.SetSimulationTime(10.1f);
                w.GetComponentRW<ObjectiveStatus>(e).Group = new FixedString32("reports");
                egress.ScanAndPublish(w);
                Assert.Equal(2, wire.Written.Count);
                Assert.Equal("reports", wire.Written[1].Group);
            }
        }

        /// <summary>⭐ rules S4 ② — the <c>Pass</c> mark is a DISPLAY field: a change goes at once, inside the progress window. ✅ Red-proof: <c>Pass</c> left out of <c>SameDisplayAndState</c> ⇒ red.</summary>
        [Fact]
        public void APassChange_GoesAtOnce_InsideTheProgressWindow()
        {
            var (w, _, e) = Node(primaryOwner: 1, localNode: 1);
            using (w)
            {
                var wire = new CapturingWriter();
                var egress = new EntityObjectiveStatusEgressTranslator(wire);
                w.SetSimulationTime(10f);
                egress.ScanAndPublish(w);
                w.SetSimulationTime(10.1f);
                w.GetComponentRW<ObjectiveStatus>(e).Pass = 0.7f;
                egress.ScanAndPublish(w);
                Assert.Equal(2, wire.Written.Count);
                Assert.Equal(0.7f, wire.Written[1].Pass);
            }
        }

        /// <summary>⭐ rules S2-c — a trigger's <c>TriggerState</c> is a STATE field: a change goes at once, inside the progress window. ✅ Red-proof: <c>TriggerState</c> left out of
        /// <c>SameDisplayAndState</c> ⇒ red (the change counts as progress and waits).</summary>
        [Fact]
        public void ATriggerStateChange_GoesAtOnce_InsideTheProgressWindow()
        {
            var (w, _, e) = Node(primaryOwner: 1, localNode: 1);
            using (w)
            {
                var wire = new CapturingWriter();
                var egress = new EntityObjectiveStatusEgressTranslator(wire);
                w.SetSimulationTime(10f);
                egress.ScanAndPublish(w);
                w.SetSimulationTime(10.1f);
                w.GetComponentRW<ObjectiveStatus>(e).TriggerState = TriggerState.Armed;
                egress.ScanAndPublish(w);
                Assert.Equal(2, wire.Written.Count);
                Assert.Equal((byte)TriggerState.Armed, wire.Written[1].TriggerState);
            }
        }

        /// <summary>⭐ rules V-1 / V-2 (the "any node" claim) — the owner's egress publishes the status, a SECOND world's ingress applies it: the rule's links and timing
        /// arrive on the replica's component. ✅ Red-proof: <c>ActsOn</c> dropped from <c>ToSample</c> ⇒ red.</summary>
        [Fact]
        public void TheLinksAndTheTiming_CrossToASecondWorld_AsTheSameComponent()
        {
            var (a, _, _) = Node(primaryOwner: 1, localNode: 1);
            var wire = new CapturingWriter();
            using (a) new EntityObjectiveStatusEgressTranslator(wire).ScanAndPublish(a);
            var sample = Assert.Single(wire.Written);

            var b = new EntityRepository();
            using (b)
            {
                b.RegisterComponent<NetworkIdentity>(); b.RegisterComponent<NetworkAuthority>(); b.RegisterComponent<ObjectiveStatus>(); b.RegisterComponent<ObjectiveLinks>();
                var map = new NetworkEntityMap();
                var e = b.CreateEntity();
                b.AddComponent(e, new NetworkIdentity { Value = 80 });
                b.AddComponent(e, new NetworkAuthority(1, 2));                // node 1 owns it; this is node 2
                map.Register(80, e);
                var ingress = new EntityObjectiveStatusIngressTranslator(null, map);
                ingress.Receive(in sample);
                using (var cmd = new EntityCommandBuffer()) { ingress.Flush(cmd, b); cmd.Playback(b); }
                var s = b.GetComponentRO<ObjectiveStatus>(e);
                Assert.Equal((2, 101, 102), (s.DependsOnCount, s.DependsOn[0], s.DependsOn[1]));
                Assert.Equal((3, 201, 202, 203), (s.ActsOnCount, s.ActsOn[0], s.ActsOn[1], s.ActsOn[2]));
                Assert.Equal((1.5f, 2.5f, 3.5f, 60f, 77.25), (s.HoldFor, s.Delay, s.Cooldown, s.Deadline, s.ExpectedAt));
            }
        }

        /// <summary>⭐ rules V-2 — <c>ExpectedAt</c> is a state change: it goes at once, inside the progress window. ✅ Red-proof: <c>ExpectedAt</c> left out of <c>SameDisplayAndState</c> ⇒ red.</summary>
        [Fact]
        public void AnExpectedAtChange_GoesAtOnce_InsideTheProgressWindow()
        {
            var (w, _, e) = Node(primaryOwner: 1, localNode: 1);
            using (w)
            {
                var wire = new CapturingWriter();
                var egress = new EntityObjectiveStatusEgressTranslator(wire);
                w.SetSimulationTime(10f);
                egress.ScanAndPublish(w);
                w.SetSimulationTime(10.1f);
                w.GetComponentRW<ObjectiveStatus>(e).ExpectedAt = 99;
                egress.ScanAndPublish(w);
                Assert.Equal(2, wire.Written.Count);
                Assert.Equal(99d, wire.Written[1].ExpectedAt);
            }
        }

        /// <summary>Ingress for an entity this node does not know yet is HELD, then applied (status and links) once it is; the recorded owner
        /// never takes its own sample back. ✅ Red-proof: the pending hold dropped (return true on an unknown entity) ⇒ red.</summary>
        [Fact]
        public void Ingress_HoldsASampleForAnUnknownEntity_ThenAppliesIt()
        {
            var sample = EntityObjectiveStatusEgressTranslator.ToSample(80, Full(), Links());
            var w = new EntityRepository();
            using (w)
            {
                w.RegisterComponent<NetworkIdentity>();
                w.RegisterComponent<NetworkAuthority>();
                w.RegisterComponent<ObjectiveStatus>();
                w.RegisterComponent<ObjectiveLinks>();
                var map = new NetworkEntityMap();
                var ingress = new EntityObjectiveStatusIngressTranslator(participant: null, map);
                ingress.Receive(in sample);
                using (var cmd = new EntityCommandBuffer()) { ingress.Flush(cmd, w); cmd.Playback(w); }
                Assert.Equal(1, ingress.PendingCount);                        // unknown: held

                var e = w.CreateEntity();
                w.AddComponent(e, new NetworkIdentity { Value = 80 });
                w.AddComponent(e, new NetworkAuthority(2, 1));                // someone else owns it
                map.Register(80, e);
                using (var cmd = new EntityCommandBuffer()) { ingress.Flush(cmd, w); cmd.Playback(w); }
                Assert.Equal(0, ingress.PendingCount);
                Assert.Equal(ObjectiveState.Failed, w.GetComponentRO<ObjectiveStatus>(e).State);
                Assert.Equal("Hold the bridge", w.GetComponentRO<ObjectiveStatus>(e).Title.ToString());
                Assert.Equal(42, w.GetComponentRO<ObjectiveLinks>(e).Parent.NetworkId);
            }

            var (owner, ownerMap, own) = Node(primaryOwner: 1, localNode: 1);
            using (owner)
            {
                owner.GetComponentRW<ObjectiveStatus>(own).State = ObjectiveState.Pending;
                var ingress = new EntityObjectiveStatusIngressTranslator(null, ownerMap);
                ingress.Receive(in sample);
                using (var cmd = new EntityCommandBuffer()) { ingress.Flush(cmd, owner); cmd.Playback(owner); }
                Assert.Equal(ObjectiveState.Pending, owner.GetComponentRO<ObjectiveStatus>(own).State);   // its own sample is dropped
            }
        }

        /// <summary>The shared pack carries the pair exactly once; the egress declares the components it publishes. ✅ Red-proof: either
        /// registration removed from <c>SharedTranslatorPack</c> ⇒ red.</summary>
        [Fact]
        public void TheSharedPack_CarriesThePair_OnEveryNode()
        {
            using var participant = new DdsParticipant(232u);
            var entityMap = new NetworkEntityMap();
            var pack = SharedTranslatorPack.Create(participant, entityMap, localNodeId: 1, new FdpEventBus(),
                new Fdp.Toolkit.Replication.Systems.GhostCreationSystem(entityMap)).ToList();
            var egress = Assert.Single(pack.OfType<EntityObjectiveStatusEgressTranslator>());
            Assert.Single(pack.OfType<EntityObjectiveStatusIngressTranslator>());
            Assert.Equal(new[] { GlobalComponentIds.ObjectiveStatus, GlobalComponentIds.ObjectiveLinks }, egress.TargetComponentIds);
            Assert.Equal((long)EDescriptorType.dtObjectiveStatus, egress.DescriptorOrdinal);
        }
    }
}
