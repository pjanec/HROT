using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Ambient;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Hrot.Map.Common.Dds;
using Hrot.Map.Common.Replication.Egress;
using Hrot.Map.Common.Replication.Ingress;
using Hrot.NED.Descriptors;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐⭐ <c>CE-11034</c> (R-11014; 📄 docs/designs/audio/DESIGN_Ambience.md §2, §5 slice 1) — an ambience layer crosses the wire: the owner's <see cref="EntityAmbienceEgressTranslator"/> builds the
    /// <c>dtAmbience</c> sample (every field), sends on change only, and DISPOSES the instance when the layer ends; every other node's <see cref="EntityAmbienceIngressTranslator"/> applies it —
    /// holding a sample whose entity is not known yet — and the recorded owner never takes its own sample back. The <c>dtAreaEffectCloud</c> / <c>dtWeather</c> shape.
    /// 🔴 Mutations: the egress not copying <c>Until</c> ⇒ the expiry never reaches a renderer ⇒ the round trip red; <c>Dispose</c> not forwarding ⇒ the ended layer is never deleted on the wire ⇒ red.
    /// </summary>
    public sealed class EntityAmbienceTranslatorTests
    {
        private sealed class Capture : IDdsWriter<EntityAmbience>
        {
            public List<EntityAmbience> Written { get; } = new();
            public List<int> Disposed { get; } = new();
            public void Write(EntityAmbience sample) => Written.Add(sample);
            public void DisposeInstance(EntityAmbience key) => Disposed.Add(key.EntityId);
        }

        private static (EntityRepository World, NetworkEntityMap Map, Entity Layer) Node(int primaryOwner, int localNode, int netId = 88)
        {
            var w = new EntityRepository();
            w.RegisterComponent<NetworkIdentity>();
            w.RegisterComponent<NetworkAuthority>();
            w.RegisterComponent<Ambience>();
            w.RegisterComponent<SimTransform>();
            var map = new NetworkEntityMap();
            var layer = w.CreateEntity();
            w.AddComponent(layer, new NetworkIdentity { Value = netId });
            w.AddComponent(layer, new NetworkAuthority(primaryOwner, localNode));
            map.Register(netId, layer);
            return (w, map, layer);
        }

        private static Ambience Siren(float intensity = 0.8f) => new()
        {
            Kind = new FixedString32("alarm.airraid.preliminary"), Intensity = intensity, Radius = 800f, Until = (SimInstant)150.25,
        };

        /// <summary>Every field reaches the other node (kind as its dotted name, intensity, radius, the sim time it ends), and each field's change is a NEW sample. A 31-character kind (the limit) survives.</summary>
        [Fact]
        public void TheDescriptor_RoundTripsEveryField_EgressAndIngress()
        {
            var layer = Siren();
            var sample = EntityAmbienceEgressTranslator.ToSample(88, layer);
            Assert.Equal((88, "alarm.airraid.preliminary", 0.8f, 800f, 150.25), (sample.EntityId, sample.Kind, sample.Intensity, sample.Radius, sample.Until));

            var (node, map, unit) = Node(primaryOwner: 2, localNode: 1);
            using (node)
            {
                var ingress = new EntityAmbienceIngressTranslator(participant: null, map);
                ingress.Receive(in sample);
                using (var cmd = new EntityCommandBuffer()) { ingress.Flush(cmd, node); cmd.Playback(node); }
                var got = node.GetComponentRO<Ambience>(unit);
                Assert.Equal(("alarm.airraid.preliminary", 0.8f, 800f, 150.25), (got.Kind.ToString(), got.Intensity, got.Radius, got.Until.Seconds));
            }

            var longest = new Ambience { Kind = new FixedString32("event.evacuation.city.012345678"), Until = SimInstant.Zero };   // 31 chars
            Assert.Equal("event.evacuation.city.012345678", EntityAmbienceIngressTranslator.FromSample(EntityAmbienceEgressTranslator.ToSample(1, longest)).Kind.ToString());

            // every field is part of the sample's identity
            Assert.NotEqual(sample, EntityAmbienceEgressTranslator.ToSample(88, layer with { Intensity = 0.5f }));
            Assert.NotEqual(sample, EntityAmbienceEgressTranslator.ToSample(88, layer with { Radius = 900f }));
            Assert.NotEqual(sample, EntityAmbienceEgressTranslator.ToSample(88, layer with { Until = (SimInstant)200.0 }));
            Assert.NotEqual(sample, EntityAmbienceEgressTranslator.ToSample(88, layer with { Kind = new FixedString32("alarm.airraid") }));
        }

        /// <summary>The owner sends the layer on change only; a non-owner sends nothing; and when the layer ENDS the instance is disposed on the wire (a late joiner must not hear a dead siren).</summary>
        [Fact]
        public void TheOwner_PublishesOnChangeOnly_ANonOwnerNothing_AnEndedLayerIsDisposedOnTheWire()
        {
            var (w, _, layer) = Node(primaryOwner: 1, localNode: 1);
            using (w)
            {
                var wire = new Capture();
                var egress = new EntityAmbienceEgressTranslator(wire);
                w.AddComponent(layer, Siren());
                egress.ScanAndPublish(w);
                Assert.Single(wire.Written);
                egress.ScanAndPublish(w);
                Assert.Single(wire.Written);                                  // unchanged: nothing sent

                w.GetComponentRW<Ambience>(layer).Intensity = 0.3f;
                egress.ScanAndPublish(w);
                Assert.Equal(2, wire.Written.Count);
                Assert.Equal(0.3f, wire.Written[1].Intensity);

                Assert.Empty(wire.Disposed);
                egress.Dispose(88);                                           // the layer's teardown (the expiry system's network destroy)
                Assert.Equal(new[] { 88 }, wire.Disposed);
            }

            var (other, _, theirs) = Node(primaryOwner: 2, localNode: 1);
            using (other)
            {
                var wire = new Capture();
                other.AddComponent(theirs, Siren());
                new EntityAmbienceEgressTranslator(wire).ScanAndPublish(other);
                Assert.Empty(wire.Written);
            }
        }

        /// <summary>A POSITIONLESS layer (no SimTransform) and a positioned one travel the same descriptor — the position is the entity's own transform, never a field of it.</summary>
        [Fact]
        public void APositionlessAndAPositionedLayer_BothPublish()
        {
            var (w, _, everywhere) = Node(primaryOwner: 1, localNode: 1, netId: 90);
            using (w)
            {
                var town = w.CreateEntity();
                w.AddComponent(town, new NetworkIdentity { Value = 91 });
                w.AddComponent(town, new NetworkAuthority(1, 1));
                w.AddComponent(town, new SimTransform { Position = new Vector3(300, 400, 0), Rotation = Quaternion.Identity });
                w.AddComponent(town, Siren());
                w.AddComponent(everywhere, new Ambience { Kind = new FixedString32("battle.distant"), Intensity = 1f });
                var wire = new Capture();
                new EntityAmbienceEgressTranslator(wire).ScanAndPublish(w);
                Assert.Equal(2, wire.Written.Count);
                Assert.Contains(wire.Written, s => s.EntityId == 90 && s.Kind == "battle.distant" && s.Until == 0.0);
                Assert.Contains(wire.Written, s => s.EntityId == 91 && s.Radius == 800f);
            }
        }

        /// <summary>A sample for an entity this node does not know yet is HELD and lands when the entity appears (the topic and the creation are not ordered — a late joiner's TransientLocal sample can come first); the recorded OWNER ignores a sample of its own layer.</summary>
        [Fact]
        public void ASampleBeforeItsEntity_IsHeld_AndTheOwnerNeverTakesItsOwnSampleBack()
        {
            using var w = new EntityRepository();
            w.RegisterComponent<NetworkIdentity>();
            w.RegisterComponent<NetworkAuthority>();
            w.RegisterComponent<Ambience>();
            var map = new NetworkEntityMap();
            var ingress = new EntityAmbienceIngressTranslator(participant: null, map);
            var sample = EntityAmbienceEgressTranslator.ToSample(77, Siren());

            ingress.Receive(in sample);
            using (var cmd = new EntityCommandBuffer()) { ingress.Flush(cmd, w); cmd.Playback(w); }
            Assert.Equal(1, ingress.PendingCount);                         // nobody to apply it to yet

            var ghost = w.CreateEntity();
            w.AddComponent(ghost, new NetworkIdentity { Value = 77 });
            w.AddComponent(ghost, new NetworkAuthority(primaryOwnerId: 2, localNodeId: 1));
            map.Register(77, ghost);
            using (var cmd = new EntityCommandBuffer()) { ingress.Flush(cmd, w); cmd.Playback(w); }
            Assert.Equal(0, ingress.PendingCount);
            Assert.Equal(0.8f, w.GetComponentRO<Ambience>(ghost).Intensity);

            // the recorded owner: its own sample is dropped, its value untouched
            var (own, ownMap, mine) = Node(primaryOwner: 1, localNode: 1, netId: 78);
            using (own)
            {
                own.AddComponent(mine, new Ambience { Kind = new FixedString32("alarm.bell"), Intensity = 0.1f });
                var echo = EntityAmbienceEgressTranslator.ToSample(78, Siren(0.9f));
                var mineIngress = new EntityAmbienceIngressTranslator(participant: null, ownMap);
                mineIngress.Receive(in echo);
                using (var cmd = new EntityCommandBuffer()) { mineIngress.Flush(cmd, own); cmd.Playback(own); }
                Assert.Equal(0.1f, own.GetComponentRO<Ambience>(mine).Intensity);
                Assert.Equal(0, mineIngress.PendingCount);
            }
        }

        /// <summary>The toolkit's component id and the descriptor's ordinal are the wire contract: the catalogue id 266 is the descriptor's mapped component, the ordinal 246 permanent.</summary>
        [Fact]
        public void TheOrdinalAndTheComponentId_AreThePermanentWireValues()
        {
            Assert.Equal(246L, (long)EDescriptorType.dtAmbience);
            Assert.Equal(266, GlobalComponentIds.Ambience);
            var egress = new EntityAmbienceEgressTranslator((IDdsWriter<EntityAmbience>?)null);
            Assert.Equal((long)EDescriptorType.dtAmbience, egress.DescriptorOrdinal);
            Assert.Equal(new[] { GlobalComponentIds.Ambience }, egress.TargetComponentIds);
        }
    }
}
