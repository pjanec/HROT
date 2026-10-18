using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Physics.BodyParts;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Hrot.Map.Common.Replication.Egress;
using Hrot.Map.Common.Replication.Ingress;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐ <c>CE-3210</c> (docs/designs/hit-location/DESIGN_Body_Parts_And_Hit_Location.md HL-6, HL-8) — a body's per-part damage (written by
    /// the Health owner) and its hit marks (written by the muscle) reach every other node whole: the egress wire form applied by the
    /// ingress on a second world gives back the same component.
    /// </summary>
    public sealed class BodyPartReplicationTests
    {
        private static (EntityRepository World, Entity Unit, NetworkEntityMap Map) Node(long netId)
        {
            var w = new EntityRepository();
            w.RegisterComponent<NetworkIdentity>();
            w.RegisterComponent<PartDamage>();
            w.RegisterComponent<HitMarks>();
            w.RegisterComponent<Fdp.Toolkit.Combat.EffectState>();
            w.RegisterComponent<Fdp.Toolkit.Combat.Gesture>();
            w.RegisterComponent<Fdp.Toolkit.Civilians.HostileAct>();   // ⭐ CE-3238
            w.RegisterComponent<Fdp.Toolkit.Combat.AreaEffectCloud>();
            w.RegisterComponent<Fdp.Toolkit.Perception.Look.LookState>();   // ⭐ CE-3252
            var unit = w.CreateEntity();
            w.AddComponent(unit, new NetworkIdentity { Value = netId });
            var map = new NetworkEntityMap();
            map.Register(netId, unit);
            return (w, unit, map);
        }

        [Fact]
        public unsafe void CE3210_PartDamage_TheOwnersLevels_ReachTheOtherNode()
        {
            var damage = new PartDamage { Plan = BodyPlan.Person };
            damage.Add(PersonPart.Head, 200);
            damage.Add(PersonPart.ShinL, 64);
            damage.Add(15, 7);
            var sample = EntityPartDamageEgressTranslator.ToSample(41L, damage);

            var (node, unit, map) = Node(41L);
            using (node)
            {
                var ingress = new EntityPartDamageIngressTranslator(participant: null, map);
                using var cmd = new EntityCommandBuffer();
                ingress.Apply(in sample, cmd, node);
                cmd.Playback(node);
                var got = node.GetComponentRO<PartDamage>(unit);
                Assert.Equal(BodyPlan.Person, got.Plan);
                for (byte p = 0; p < PartDamage.MaxParts; p++) Assert.Equal(damage.LevelOf(p), got.LevelOf(p));
            }
        }

        [Fact]
        public void CE3210_HitMarks_TheRingCrosses_InSequence_EvenAfterItWrapped()
        {
            var marks = new HitMarks();
            for (int i = 0; i < HitMarks.Capacity + 5; i++)
                marks.Add((byte)(i % 10), new Vector3(i / 100f, -0.25f, 0.5f), (byte)(i % 4), i % 2 == 0 ? HitMarkKind.Bullet : HitMarkKind.Fragment);
            var sample = EntityHitMarksEgressTranslator.ToSample(42L, marks);
            Assert.Equal(HitMarks.Capacity, sample.Marks.Count);
            Assert.Equal(5, sample.Marks[0].Seq);   // the oldest still in the ring

            var (node, unit, map) = Node(42L);
            using (node)
            {
                var ingress = new EntityHitMarksIngressTranslator(participant: null, map);
                using var cmd = new EntityCommandBuffer();
                ingress.Apply(in sample, cmd, node);
                cmd.Playback(node);
                var got = node.GetComponentRO<HitMarks>(unit);
                Assert.Equal(marks.NextSeq, got.NextSeq);
                for (int seq = 0; seq < marks.NextSeq; seq++)
                {
                    bool had = marks.TryGet(seq, out var p0, out var l0, out var f0, out var k0);
                    Assert.Equal(had, got.TryGet(seq, out var p1, out var l1, out var f1, out var k1));
                    if (!had) continue;
                    Assert.Equal((p0, f0, k0), (p1, f1, k1));
                    Assert.Equal(l0, l1);
                }
            }
        }

        /// <summary>⭐ <c>CE-3219</c> + ui finding F-3 (civilians design §7.6) — a person's effects, the behaviour's cower mode included,
        /// reach the other node whole; the owner publishes again when only the mode changes.</summary>
        [Fact]
        public void CE3219_F3_EffectState_WithCowering_ReachesTheOtherNode()
        {
            var effects = new Fdp.Toolkit.Combat.EffectState { StunnedUntil = 12.5, IrritatedUntil = 3, KnockedDownUntil = 7.25, Cowering = 1 };
            var sample = EntityEffectStateEgressTranslator.ToSample(44L, effects);

            var (node, unit, map) = Node(44L);
            using (node)
            {
                var ingress = new EntityEffectStateIngressTranslator(participant: null, map);
                using var cmd = new EntityCommandBuffer();
                ingress.Apply(in sample, cmd, node);
                cmd.Playback(node);
                Assert.Equal(effects, node.GetComponentRO<Fdp.Toolkit.Combat.EffectState>(unit));
            }

            Assert.NotEqual(sample, EntityEffectStateEgressTranslator.ToSample(44L, effects with { Cowering = 0 }));
        }

        /// <summary>⭐ <c>CE-3224</c> (civilians B-6, §7.7) — a person's gesture reaches the other node, and going back to None is
        /// published too (a protester lowering his fist is a change).</summary>
        [Fact]
        public void CE3224_Gesture_ReachesTheOtherNode_AndNoneIsAChange()
        {
            var raised = new Fdp.Toolkit.Combat.Gesture { Kind = Fdp.Toolkit.Combat.GestureKind.FistRaised };
            var sample = EntityGestureEgressTranslator.ToSample(45L, raised);

            var (node, unit, map) = Node(45L);
            using (node)
            {
                var ingress = new EntityGestureIngressTranslator(participant: null, map);
                using var cmd = new EntityCommandBuffer();
                ingress.Apply(in sample, cmd, node);
                cmd.Playback(node);
                Assert.Equal(Fdp.Toolkit.Combat.GestureKind.FistRaised, Fdp.Toolkit.Combat.Gestures.Of(node, unit));
            }

            Assert.NotEqual(sample, EntityGestureEgressTranslator.ToSample(45L, default));
        }

        /// <summary>⭐ <c>CE-3238</c> (civilians B-16, §7.16 HA-1) — a man's hostile act reaches the other node with its kind and its time, and
        /// an UPGRADE (non-lethal → lethal) is a change the egress publishes. ✅ Red-proof: the ingress dropping the kind ⇒ None ⇒ red.</summary>
        [Fact]
        public void CE3238_HostileAct_ReachesTheOtherNode_AndAnUpgradeIsAChange()
        {
            var shot = new Fdp.Toolkit.Civilians.HostileAct { SinceSimTime = 12.5, Kind = Fdp.Toolkit.Civilians.HostileActKind.Lethal };
            var sample = EntityHostileActEgressTranslator.ToSample(46L, shot);

            var (node, unit, map) = Node(46L);
            using (node)
            {
                var ingress = new EntityHostileActIngressTranslator(participant: null, map);
                using var cmd = new EntityCommandBuffer();
                ingress.Apply(in sample, cmd, node);
                cmd.Playback(node);
                Assert.Equal(Fdp.Toolkit.Civilians.HostileActKind.Lethal, Fdp.Toolkit.Civilians.HostileActs.Of(node, unit));
                Assert.Equal(12.5, node.GetComponentRO<Fdp.Toolkit.Civilians.HostileAct>(unit).SinceSimTime);
                Assert.True(Fdp.Toolkit.Civilians.HostileActs.Committed(node, unit));
            }

            Assert.NotEqual(sample, EntityHostileActEgressTranslator.ToSample(46L, shot with { Kind = Fdp.Toolkit.Civilians.HostileActKind.NonLethal }));
        }

        /// <summary>⭐ <c>CE-3219</c> part 3 (§7.3b) — a cloud's every field reaches the other node (its kind, size, sight, irritation and
        /// the born/seconds a renderer fades on), and a thinner cloud is a new sample.</summary>
        [Fact]
        public void CE3219_ACloud_ReachesTheOtherNode_WithEveryField()
        {
            var cloud = new Fdp.Toolkit.Combat.AreaEffectCloud
            {
                Kind = Fdp.Toolkit.Combat.CloudKind.TearGas, Radius = 6f, Height = 3f, SightTransmittance = 0.4f, IrritantSeconds = 2f,
                BornAtSimSeconds = 12.5, Seconds = 60f, SourceNetworkId = 4711,   // ⭐ CE-3236 — the canister it sits on
            };
            var sample = EntityAreaEffectCloudEgressTranslator.ToSample(46L, cloud);

            var (node, unit, map) = Node(46L);
            using (node)
            {
                var ingress = new EntityAreaEffectCloudIngressTranslator(participant: null, map);
                using var cmd = new EntityCommandBuffer();
                ingress.Apply(in sample, cmd, node);
                cmd.Playback(node);
                Assert.Equal(cloud, node.GetComponentRO<Fdp.Toolkit.Combat.AreaEffectCloud>(unit));
            }

            Assert.NotEqual(sample, EntityAreaEffectCloudEgressTranslator.ToSample(46L, cloud with { SightTransmittance = 0.6f }));
        }

        [Fact]
        public void CE3210_AnUnknownBody_IsDropped()
        {
            var (node, _, map) = Node(43L);
            using (node)
            {
                var ingress = new EntityHitMarksIngressTranslator(participant: null, map);
                using var cmd = new EntityCommandBuffer();
                var sample = EntityHitMarksEgressTranslator.ToSample(999L, new HitMarks());
                ingress.Apply(in sample, cmd, node);
                Assert.Equal(1, ingress.DroppedSampleCount);
            }
        }
    
        /// <summary>⭐ <c>CE-3252</c> (LD-E) — where a person looks reaches the other node (target, source, head turn); the owner sends
        /// again only on news (≥ 1° of head, ≥ 0.25 m of target). ✅ Red-proof: drop HeadYaw from the wire ⇒ the far node's head is 0.</summary>
        [Fact]
        public void CE3252_LookState_TheMusclesLook_ReachesTheOtherNode_OnNewsOnly()
        {
            var look = new Fdp.Toolkit.Perception.Look.LookState
            {
                Target = new Vector3(4f, 5f, 1.5f), HasTarget = 1, Source = Fdp.Toolkit.Perception.Look.LookSource.LookGlance, HeadYaw = 0.6f,
                GlanceSince = 12.0, RestUntil = 3.0,   // the muscle's own timers — not on the wire
            };
            var sample = Hrot.Map.Common.Replication.Egress.EntityLookStateEgressTranslator.ToSample(46L, look);

            var (node, unit, map) = Node(46L);
            using (node)
            {
                var ingress = new Hrot.Map.Common.Replication.Ingress.EntityLookStateIngressTranslator(participant: null, map);
                using var cmd = new EntityCommandBuffer();
                ingress.Apply(in sample, cmd, node);
                cmd.Playback(node);
                var got = node.GetComponentRO<Fdp.Toolkit.Perception.Look.LookState>(unit);
                Assert.Equal((look.Target, look.HasTarget, look.Source, look.HeadYaw), (got.Target, got.HasTarget, got.Source, got.HeadYaw));
                Assert.Equal(0.0, got.GlanceSince);
            }

            var tiny = sample; tiny.HeadYaw += 0.01f;            // < 1°
            Assert.True(Hrot.Map.Common.Replication.Egress.EntityLookStateEgressTranslator.Same(sample, tiny));
            var turned = sample; turned.HeadYaw += 0.05f;        // ≈ 3°
            Assert.False(Hrot.Map.Common.Replication.Egress.EntityLookStateEgressTranslator.Same(sample, turned));
            var released = sample; released.HasTarget = 0;
            Assert.False(Hrot.Map.Common.Replication.Egress.EntityLookStateEgressTranslator.Same(sample, released));
        }
    }
}
