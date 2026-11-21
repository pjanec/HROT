using Fdp.Interfaces;
using System;
using Fdp.Core;
using Fdp.Toolkit.Replication;
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
    /// ⭐ Macros S3a ③ (docs/DESIGN_Scenario_Macros.md §14 SR-170 / SR-175 / SR-178, R-134) — the three macro components cross the wire from their
    /// creator (<c>dtMacroInstance</c> 158 · <c>dtMacroExpansionStatus</c> 159 · <c>dtMacroOrigin</c> 170, no ownership group), the
    /// <see cref="EntityStableGuidReplicationTests"/> shape: a round trip per descriptor, then publish-on-change and a replica that applies it.
    /// 🔴 Mutation: drop a field copy in a <c>ToSample</c> / <c>FromSample</c> ⇒ the matching round trip is red.
    /// </summary>
    public sealed class MacroWireReplicationTests
    {
        private static EntityRepository World()
        {
            var w = new EntityRepository();
            w.RegisterComponent<NetworkIdentity>();
            w.RegisterManagedComponent<MacroInstance>();
            w.RegisterComponent<MacroExpansionStatus>();
            w.RegisterComponent<MacroOrigin>();
            w.RegisterComponent<RefKey>();
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
        public void TheOrdinals_AreTheMeasuredFreeOnes_AndTheTrafficBlockIsKeptClear()
        {
            Assert.Equal(158, (int)EDescriptorType.dtMacroInstance);
            Assert.Equal(159, (int)EDescriptorType.dtMacroExpansionStatus);
            Assert.Equal(170, (int)EDescriptorType.dtMacroOrigin);
        }

        [Fact]
        public void TheInstance_RoundTripsTheWire_FieldForField()
        {
            var mi = new MacroInstance { Macro = "test/fire-team", ArgsJson = "{\"side\":\"Friend\"}" };
            var sample = EntityMacroInstanceEgressTranslator.ToSample(41, mi);
            Assert.Equal(41, sample.EntityId);
            Assert.Equal(mi, EntityMacroInstanceIngressTranslator.FromSample(in sample));   // record value equality
        }

        [Fact]
        public void TheExpansionStatus_RoundTripsTheWire_FieldForField()
        {
            var st = new MacroExpansionStatus { State = MacroExpansionState.Failed, Reason = new FixedString128("no such param 'side'") };
            var sample = EntityMacroExpansionStatusEgressTranslator.ToSample(42, in st);
            Assert.Equal(42, sample.EntityId);
            var back = EntityMacroExpansionStatusIngressTranslator.FromSample(in sample);
            Assert.Equal(MacroExpansionState.Failed, back.State);
            Assert.Equal("no such param 'side'", back.Reason.ToString());
        }

        [Fact]
        public void TheOrigin_RoundTripsTheWire_BitForBit()
        {
            var o = new MacroOrigin { Instance = new EntityRef(7), Local = Guid.Parse("a1b2c3d4-0102-4030-8050-60708090a0b0") };
            var sample = EntityMacroOriginEgressTranslator.ToSample(43, in o);
            Assert.Equal(43, sample.EntityId);
            Assert.Equal(7, sample.Instance);
            var back = EntityMacroOriginIngressTranslator.FromSample(in sample);
            Assert.Equal(7, back.Instance.NetworkId);
            Assert.Equal(o.Local, back.Local);
        }

        [Fact]
        public void TheCreator_PublishesEachOnChangeOnly_AndAReplicaAppliesThem()
        {
            using var owner = World();
            var e = Spawn(owner, 61);
            owner.SetManagedComponent(e, new MacroInstance { Macro = "m/a" });
            owner.AddComponent(e, new MacroExpansionStatus { State = MacroExpansionState.Expanding });
            owner.AddComponent(e, new MacroOrigin { Instance = new EntityRef(60), Local = Guid.NewGuid() });

            var inst = new EntityMacroInstanceEgressTranslator(participant: null);
            var stat = new EntityMacroExpansionStatusEgressTranslator(participant: null);
            var orig = new EntityMacroOriginEgressTranslator(participant: null);
            foreach (var t in new IDescriptorTranslator[] { inst, stat, orig }) { t.ScanAndPublish(owner); t.ScanAndPublish(owner); }
            Assert.Equal(1, inst.SentSampleCount);   // unchanged ⇒ not re-sent
            Assert.Equal(1, stat.SentSampleCount);
            Assert.Equal(1, orig.SentSampleCount);

            owner.SetManagedComponent(e, new MacroInstance { Macro = "m/a", ArgsJson = "{\"k\":1}" });   // a NEW record ⇒ a change
            owner.SetComponent(e, new MacroExpansionStatus { State = MacroExpansionState.Done });
            inst.ScanAndPublish(owner); stat.ScanAndPublish(owner); orig.ScanAndPublish(owner);
            Assert.Equal(2, inst.SentSampleCount);
            Assert.Equal(2, stat.SentSampleCount);
            Assert.Equal(1, orig.SentSampleCount);   // the origin never changes

            using var replica = World();
            var map = new NetworkEntityMap();
            var re = Spawn(replica, 61, map);
            var iin = new EntityMacroInstanceIngressTranslator(participant: null, map);
            var sin = new EntityMacroExpansionStatusIngressTranslator(participant: null, map);
            var oin = new EntityMacroOriginIngressTranslator(participant: null, map);
            var g = Guid.NewGuid();
            using (var cmd = new EntityCommandBuffer())
            {
                Assert.True(iin.TryApply(61, new MacroInstance { Macro = "m/a", ArgsJson = "{\"k\":1}" }, cmd, replica));
                Assert.True(sin.TryApply(61, new MacroExpansionStatus { State = MacroExpansionState.Done }, cmd, replica));
                Assert.True(oin.TryApply(61, new MacroOrigin { Instance = new EntityRef(60), Local = g }, cmd, replica));
                cmd.Playback(replica);
            }
            Assert.Equal("{\"k\":1}", ((Fdp.ModuleHost.Abstractions.ISimulationView)replica).GetManagedComponentRO<MacroInstance>(re).ArgsJson);
            Assert.Equal(MacroExpansionState.Done, replica.GetComponentRO<MacroExpansionStatus>(re).State);
            Assert.Equal(g, replica.GetComponentRO<MacroOrigin>(re).Local);
        }

        [Fact]
        public void AForwardedCreationRequest_CarriesTheMacroInstance_ToTheExpandingNode()
        {
            var mi = new MacroInstance { Macro = "test/fire-team", ArgsJson = "{\"side\":\"Friend\"}" };
            var wire = Hrot.Map.Common.Replication.Egress.CreateEntityRequestDescriptorBuilder.Build(
                Guid.NewGuid(), tkbType: 8813, initialAttributesJson: null, anchor: null,
                initialComponents: new object[] { mi });

            Assert.Contains(wire.InitialDescriptors, d => d._d == EDescriptorType.dtMacroInstance);
            var comps = Hrot.Map.Common.Replication.Utils.DescriptorMapper.MapToComponents(wire.InitialDescriptors);
            Assert.Equal(mi, System.Linq.Enumerable.Single(System.Linq.Enumerable.OfType<MacroInstance>(comps)));
        }

        [Fact]
        public void ASampleThatArrivesBeforeItsEntity_IsHeld_NotDropped()
        {
            using var replica = World();
            var map = new NetworkEntityMap();
            using var cmd = new EntityCommandBuffer();
            Assert.False(new EntityMacroInstanceIngressTranslator(null, map).TryApply(88, new MacroInstance(), cmd, replica));
            Assert.False(new EntityMacroExpansionStatusIngressTranslator(null, map).TryApply(88, default, cmd, replica));
            Assert.False(new EntityMacroOriginIngressTranslator(null, map).TryApply(88, default, cmd, replica));
        }

        // ═══ ⭐ macros wire fix — dtRefKey 182: an entity's authored name ("@Key") reaches every node, so a RUNTIME-inserted entity resolves in the live "@Key" scope everywhere ═══

        /// <summary>The ordinal is 182 (180/181 are the placement pair, 175–179 the rules block's) and the sample round-trips the key text.</summary>
        [Fact]
        public void TheRefKey_HasItsOrdinal_AndRoundTripsTheWire()
        {
            Assert.Equal(182, (int)EDescriptorType.dtRefKey);
            var sample = EntityRefKeyEgressTranslator.ToSample(43, new FixedString32("RedQRF"));
            Assert.Equal((43, "RedQRF"), (sample.EntityId, sample.Value));
            Assert.Equal("RedQRF", EntityRefKeyIngressTranslator.FromSample(in sample).Value.ToString());
        }

        /// <summary>The creator publishes a keyed entity ONCE (and again only when re-keyed); an entity without a key, or with an empty one, publishes nothing; the replica's ghost gets the
        /// key added, or its value set when it already has one; a sample ahead of its entity is held. ✅ Red-proof: the empty-key guard removed from <c>EntityRefKeyEgressTranslator.ScanAndPublish</c>
        /// ⇒ the empty-keyed entity is sent ⇒ red.</summary>
        [Fact]
        public void TheRefKey_IsPublishedOnceForAKeyedEntity_NothingWithoutOne_AndTheGhostTakesIt()
        {
            using var owner = World();
            var keyed = Spawn(owner, 71);
            owner.AddComponent(keyed, new RefKey("RedQRF"));
            var empty = Spawn(owner, 72);
            owner.AddComponent(empty, default(RefKey));
            Spawn(owner, 73);                                          // no RefKey at all

            var egress = new EntityRefKeyEgressTranslator(participant: null);
            egress.ScanAndPublish(owner); egress.ScanAndPublish(owner);
            Assert.Equal(1, egress.SentSampleCount);                   // once, only the keyed one
            owner.SetComponent(keyed, new RefKey("RedQRF2"));
            egress.ScanAndPublish(owner);
            Assert.Equal(2, egress.SentSampleCount);                   // re-keyed ⇒ republished

            using var replica = World();
            var map = new NetworkEntityMap();
            var ghost = Spawn(replica, 71, map);                       // a runtime-inserted entity's ghost: no key yet
            var ingress = new EntityRefKeyIngressTranslator(participant: null, map);
            using (var cmd = new EntityCommandBuffer())
            {
                Assert.True(ingress.TryApply(71, new RefKey("RedQRF"), cmd, replica));
                cmd.Playback(replica);
            }
            Assert.Equal("RedQRF", replica.GetComponentRO<RefKey>(ghost).Value.ToString());
            using (var cmd = new EntityCommandBuffer())
            {
                Assert.True(ingress.TryApply(71, new RefKey("RedQRF2"), cmd, replica));   // present ⇒ set
                cmd.Playback(replica);
            }
            Assert.Equal("RedQRF2", replica.GetComponentRO<RefKey>(ghost).Value.ToString());

            using var cmd2 = new EntityCommandBuffer();
            Assert.False(ingress.TryApply(88, new RefKey("Ahead"), cmd2, replica));       // ahead of its entity ⇒ held, never dropped
        }
    }
}
