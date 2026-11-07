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
    /// ⭐ rules S2 ⑧ (📄 docs/DESIGN_Scenario_Rules.md §00.9 S2-e, SR-122) — <c>dtExerciseResult</c>: the exercise's phase and outcome per side cross to every node; the ingress
    /// raises <c>ExerciseEndedEvent</c> ONCE when the phase becomes Ended. Mirrors <see cref="ObjectiveStatusWireTests"/>.
    /// </summary>
    [Collection("SimHostDds")]   // the pack test opens a real participant
    public sealed class ExerciseResultWireTests
    {
        private sealed class CapturingWriter : IDdsWriter<EntityExerciseResult>
        {
            public List<EntityExerciseResult> Written { get; } = new();
            public void Write(EntityExerciseResult sample) => Written.Add(sample);
            public void DisposeInstance(EntityExerciseResult key) { }
        }

        private static ExerciseResult Full() => new()
        {
            Phase = ExercisePhase.Ended, EndedAt = (SimInstant)321.5, Reason = new FixedString64("time is up"),
            Friend = new SideResult { Outcome = SideOutcome.Win, Points = 12f, Score = 0.75f },
            Hostile = new SideResult { Outcome = SideOutcome.Lose, Points = 3f, Score = 0.25f },
            Neutral = new SideResult { Outcome = SideOutcome.Draw, Points = 1f, Score = 0.5f },
        };

        private static EntityRepository World()
        {
            var w = new EntityRepository();
            w.RegisterComponent<NetworkIdentity>(); w.RegisterComponent<NetworkAuthority>(); w.RegisterComponent<ExerciseResult>();
            return w;
        }

        private static Entity Env(EntityRepository w, NetworkEntityMap map, int primaryOwner, int localNode, ExerciseResult r)
        {
            var e = w.CreateEntity();
            w.AddComponent(e, new NetworkIdentity { Value = 80 });
            w.AddComponent(e, new NetworkAuthority(primaryOwner, localNode));
            w.AddComponent(e, r);
            map.Register(80, e);
            return e;
        }

        /// <summary>Every field survives the wire form and back. ✅ Red-proof: any field dropped from <c>ToSample</c> or <c>FromSample</c> ⇒ red.</summary>
        [Fact]
        public void EveryField_RoundTrips()
        {
            var sample = EntityExerciseResultEgressTranslator.ToSample(80, Full());
            Assert.Equal(80, sample.EntityId);
            var back = EntityExerciseResultIngressTranslator.FromSample(in sample);
            var want = Full();
            Assert.Equal((want.Phase, want.EndedAt, "time is up"), (back.Phase, back.EndedAt, back.Reason.ToString()));
            foreach (var side in new[] { ForceId.Friend, ForceId.Hostile, ForceId.Neutral })
                Assert.Equal((want.Of(side).Outcome, want.Of(side).Points, want.Of(side).Score), (back.Of(side).Outcome, back.Of(side).Points, back.Of(side).Score));
        }

        /// <summary>The owner publishes once and not again while nothing changed, and again on a phase change; a node that does not own the descriptor publishes nothing.
        /// ✅ Red-proof: the <c>Same</c> skip removed ⇒ red.</summary>
        [Fact]
        public void TheOwnersEgress_PublishesOnChange_AndNotWhenUnchanged_AndNotWithoutAuthority()
        {
            using var w = World(); var map = new NetworkEntityMap();
            var e = Env(w, map, 1, 1, new ExerciseResult { Phase = ExercisePhase.Closing });
            var wire = new CapturingWriter();
            var egress = new EntityExerciseResultEgressTranslator(wire);
            egress.ScanAndPublish(w); egress.ScanAndPublish(w);
            Assert.Single(wire.Written);
            w.GetComponentRW<ExerciseResult>(e) = Full();
            egress.ScanAndPublish(w);
            Assert.Equal(new byte[] { 1, 2 }, wire.Written.Select(x => x.Phase).ToArray());

            using var other = World(); var otherMap = new NetworkEntityMap();
            Env(other, otherMap, 2, 1, Full());
            var w2 = new CapturingWriter();
            new EntityExerciseResultEgressTranslator(w2).ScanAndPublish(other);
            Assert.Empty(w2.Written);
        }

        /// <summary>The ingress applies a sample (held until its entity is known), raises <c>ExerciseEndedEvent</c> ONCE when the phase is Ended (a repeat of the Ended sample raises
        /// nothing), and the recorded owner neither applies nor raises. ✅ Red-proof: the <c>_endedRaised</c> guard removed ⇒ the second Ended sample raises again ⇒ red.</summary>
        [Fact]
        public void Ingress_AppliesTheResult_AndRaisesExerciseEndedOnce_NotOnTheOwner()
        {
            var ended = EntityExerciseResultEgressTranslator.ToSample(80, Full());
            using var w = World(); var map = new NetworkEntityMap();
            var ingress = new EntityExerciseResultIngressTranslator(participant: null, map);
            ingress.Receive(in ended);
            using (var cmd = new EntityCommandBuffer()) { ingress.Flush(cmd, w); cmd.Playback(w); }
            Assert.Equal(1, ingress.PendingCount);                               // unknown entity: held, nothing raised
            Assert.False(w.Bus.IsRegistered<ExerciseEndedEvent>() && w.ReadEvents<ExerciseEndedEvent>().Length > 0);

            var e = Env(w, map, 2, 1, default);                                  // someone else owns it
            using (var cmd = new EntityCommandBuffer()) { ingress.Flush(cmd, w); cmd.Playback(w); }
            Assert.Equal(0, ingress.PendingCount);
            Assert.Equal((ExercisePhase.Ended, SideOutcome.Win), (w.GetComponentRO<ExerciseResult>(e).Phase, w.GetComponentRO<ExerciseResult>(e).Friend.Outcome));
            w.Bus.SwapBuffers();
            Assert.Equal(1, w.ReadEvents<ExerciseEndedEvent>().Length);
            Assert.Equal(321.5, w.ReadEvents<ExerciseEndedEvent>()[0].EndedAt);

            ingress.Receive(in ended);                                           // the same Ended sample again
            using (var cmd = new EntityCommandBuffer()) { ingress.Flush(cmd, w); cmd.Playback(w); }
            w.Bus.SwapBuffers();
            Assert.Empty(w.ReadEvents<ExerciseEndedEvent>().ToArray());          // nothing new: ONCE

            using var owner = World(); var ownerMap = new NetworkEntityMap();
            var own = Env(owner, ownerMap, 1, 1, new ExerciseResult { Phase = ExercisePhase.Closing });
            var ownerIngress = new EntityExerciseResultIngressTranslator(null, ownerMap);
            ownerIngress.Receive(in ended);
            using (var cmd = new EntityCommandBuffer()) { ownerIngress.Flush(cmd, owner); cmd.Playback(owner); }
            Assert.Equal(ExercisePhase.Closing, owner.GetComponentRO<ExerciseResult>(own).Phase);   // its own sample is dropped
            owner.Bus.SwapBuffers();
            Assert.False(owner.Bus.IsRegistered<ExerciseEndedEvent>() && owner.ReadEvents<ExerciseEndedEvent>().Length > 0);   // and raises nothing
        }

        /// <summary>The shared pack carries the pair exactly once, and the component is in the Brain group with its descriptor. ✅ Red-proof: either registration removed ⇒ red.</summary>
        [Fact]
        public void TheSharedPack_CarriesThePair_OnEveryNode()
        {
            using var participant = new DdsParticipant(189u);
            var entityMap = new NetworkEntityMap();
            var pack = SharedTranslatorPack.Create(participant, entityMap, localNodeId: 1, new FdpEventBus(),
                new Fdp.Toolkit.Replication.Systems.GhostCreationSystem(entityMap)).ToList();
            var egress = Assert.Single(pack.OfType<EntityExerciseResultEgressTranslator>());
            Assert.Single(pack.OfType<EntityExerciseResultIngressTranslator>());
            Assert.Equal(new[] { GlobalComponentIds.ExerciseResult }, egress.TargetComponentIds);
            Assert.Equal((long)EDescriptorType.dtExerciseResult, egress.DescriptorOrdinal);
            Assert.Equal(427, GlobalComponentIds.ExerciseResult);
        }
    }
}
