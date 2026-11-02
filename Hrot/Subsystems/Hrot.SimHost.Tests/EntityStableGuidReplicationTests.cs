using System;
using System.Linq;
using Fdp.Core;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Scenario;
using Hrot.Map.Common.Replication.Egress;
using Hrot.Map.Common.Replication.Ingress;
using Hrot.NED.Descriptors;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐ <c>CE-4153</c> S3 (docs/DESIGN_Scenario_Entity_Keys.md) — an entity's <see cref="EntityGuid"/> crosses the wire from its creator
    /// (<c>dtEntityGuid</c>, no ownership group): every node then knows the scenario key, so whichever node owns the entity at save time
    /// writes the same one. Also rides a forwarded creation request (<c>CreateEntityRequestDescriptorBuilder</c>).
    /// </summary>
    public sealed class EntityStableGuidReplicationTests
    {
        private static EntityRepository World()
        {
            var w = new EntityRepository();
            w.RegisterComponent<NetworkIdentity>();
            w.RegisterComponent<EntityGuid>();
            return w;
        }

        private static Entity Spawn(EntityRepository w, int netId, NetworkEntityMap? map = null)
        {
            var e = w.CreateEntity();
            w.AddComponent(e, new NetworkIdentity { Value = netId });
            map?.Register(netId, e);
            return e;
        }

        [Fact]
        public void TheGuid_RoundTripsTheWire_BitForBit()
        {
            var g = Guid.Parse("a1b2c3d4-0102-4030-8050-60708090a0b0");
            var sample = EntityStableGuidEgressTranslator.ToSample(77, new EntityGuid(g));
            Assert.Equal(77, sample.EntityId);
            Assert.Equal(g, EntityStableGuidIngressTranslator.FromSample(in sample));
        }

        [Fact]
        public void TheCreator_PublishesItOnce_AndAReplicaStampsIt()
        {
            var g = Guid.NewGuid();
            using var owner = World();
            var e = Spawn(owner, 51);
            owner.AddComponent(e, new EntityGuid(g));
            var egress = new EntityStableGuidEgressTranslator(participant: null);
            egress.ScanAndPublish(owner);
            egress.ScanAndPublish(owner);
            Assert.Equal(1, egress.SentSampleCount);   // immutable: published once, not per scan

            using var replica = World();
            var map = new NetworkEntityMap();
            var re = Spawn(replica, 51, map);
            var ingress = new EntityStableGuidIngressTranslator(participant: null, map);
            using (var cmd = new EntityCommandBuffer())
            {
                Assert.True(ingress.TryApply(51, g, cmd, replica));
                cmd.Playback(replica);
            }
            Assert.Equal(g, replica.GetComponentRO<EntityGuid>(re).Value);
        }

        [Fact]
        public void ASampleThatArrivesBeforeItsEntity_IsHeld_NotDropped()
        {
            using var replica = World();
            var map = new NetworkEntityMap();
            var ingress = new EntityStableGuidIngressTranslator(participant: null, map);
            using var cmd = new EntityCommandBuffer();

            Assert.False(ingress.TryApply(88, Guid.NewGuid(), cmd, replica));   // the caller (PollIngress) keeps it waiting
        }

        [Fact]
        public void AForwardedCreationRequest_CarriesTheKey_ToTheCreator()
        {
            var g = Guid.NewGuid();
            var wire = Hrot.Map.Common.Replication.Egress.CreateEntityRequestDescriptorBuilder.Build(
                Guid.NewGuid(), tkbType: 2004, initialAttributesJson: null, anchor: null,
                initialComponents: new object[] { new EntityGuid(g) });

            Assert.Contains(wire.InitialDescriptors, d => d._d == EDescriptorType.dtEntityGuid);
            var comps = Hrot.Map.Common.Replication.Utils.DescriptorMapper.MapToComponents(wire.InitialDescriptors);
            Assert.Equal(g, comps.OfType<EntityGuid>().Single().Value);
        }
    }
}
