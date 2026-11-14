using System.Collections.Generic;
using Fdp.Core;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.ScenarioRules;
using Hrot.Map.Common.Dds;
using Hrot.Map.Common.Replication.Egress;
using Hrot.Map.Common.Replication.Ingress;
using Hrot.NED.Descriptors;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐ rules V-3 (📄 docs/DESIGN_Scenario_Rules.md §00.10) — <c>dtRuleLeaves</c>: a trigger's when-leaves with their live values cross to every node. Round trip, change-only
    /// publishing (the sampler is the throttle), the owner-only rule, and a second world's ingress.
    /// </summary>
    public sealed class RuleLeavesWireTests
    {
        private sealed class CapturingWriter : IDdsWriter<EntityRuleLeaves>
        {
            public List<EntityRuleLeaves> Written { get; } = new();
            public void Write(EntityRuleLeaves sample) => Written.Add(sample);
            public void DisposeInstance(EntityRuleLeaves key) { }
        }

        private static RuleLeaves Two()
        {
            var c = new RuleLeaves { Count = 2 };
            c.Leaves[0] = new RuleLeaf { Label = new FixedString32("@Bridge Health.Current"), Value = 1, Measured = 37.5f };
            c.Leaves[1] = new RuleLeaf { Label = new FixedString32("ScenarioTime"), Value = 0, Measured = 12.25f };
            return c;
        }

        private static (EntityRepository World, NetworkEntityMap Map, Entity E) Node(int primaryOwner, int localNode)
        {
            var w = new EntityRepository();
            w.RegisterComponent<NetworkIdentity>();
            w.RegisterComponent<NetworkAuthority>();
            w.RegisterComponent<RuleLeaves>();
            var map = new NetworkEntityMap();
            var e = w.CreateEntity();
            w.AddComponent(e, new NetworkIdentity { Value = 80 });
            w.AddComponent(e, new NetworkAuthority(primaryOwner, localNode));
            w.AddComponent(e, Two());
            map.Register(80, e);
            return (w, map, e);
        }

        /// <summary>Every field of every leaf survives the wire form and back; an empty one sends no list. ✅ Red-proof: <c>Measured</c> dropped from <c>ToSample</c> ⇒ red.</summary>
        [Fact]
        public void EveryLeaf_RoundTrips()
        {
            var sample = EntityRuleLeavesEgressTranslator.ToSample(80, Two());
            Assert.Equal(80, sample.EntityId);
            var back = EntityRuleLeavesIngressTranslator.FromSample(in sample);
            Assert.Equal(2, back.Count);
            Assert.Equal(("@Bridge Health.Current", (byte)1, 37.5f), (back.Leaves[0].Label.ToString(), back.Leaves[0].Value, back.Leaves[0].Measured));
            Assert.Equal(("ScenarioTime", (byte)0, 12.25f), (back.Leaves[1].Label.ToString(), back.Leaves[1].Value, back.Leaves[1].Measured));
            Assert.Null(EntityRuleLeavesEgressTranslator.ToSample(81, default).Leaves);
        }

        /// <summary>The owner publishes once and not again while nothing changed, then again when a leaf's number moves; a node that does not own the descriptor publishes nothing.
        /// ✅ Red-proof: the <c>Same</c> skip removed ⇒ red.</summary>
        [Fact]
        public void TheOwnersEgress_PublishesOnChange_AndNotWhenUnchanged_AndNotWithoutAuthority()
        {
            var (w, _, e) = Node(primaryOwner: 1, localNode: 1);
            using (w)
            {
                var wire = new CapturingWriter();
                var egress = new EntityRuleLeavesEgressTranslator(wire);
                egress.ScanAndPublish(w);
                egress.ScanAndPublish(w);
                Assert.Single(wire.Written);
                w.GetComponentRW<RuleLeaves>(e).Leaves[0].Measured = 41f;
                egress.ScanAndPublish(w);
                Assert.Equal(2, wire.Written.Count);
                Assert.Equal(41f, wire.Written[1].Leaves![0].Measured);
            }
            var (other, _, _) = Node(primaryOwner: 2, localNode: 1);
            using (other)
            {
                var wire = new CapturingWriter();
                new EntityRuleLeavesEgressTranslator(wire).ScanAndPublish(other);
                Assert.Empty(wire.Written);
            }
        }

        /// <summary>A second world's ingress applies the owner's sample as the same component; a sample for an entity not known yet is HELD, then applied; the recorded owner drops its own.
        /// ✅ Red-proof: the owner check removed from <c>TryApply</c> ⇒ the last rail goes red.</summary>
        [Fact]
        public void Ingress_HoldsForAnUnknownEntity_AppliesOnAReplica_AndTheOwnerDropsItsOwn()
        {
            var sample = EntityRuleLeavesEgressTranslator.ToSample(80, Two());
            var b = new EntityRepository();
            using (b)
            {
                b.RegisterComponent<NetworkIdentity>(); b.RegisterComponent<NetworkAuthority>(); b.RegisterComponent<RuleLeaves>();
                var map = new NetworkEntityMap();
                var ingress = new EntityRuleLeavesIngressTranslator(participant: null, map);
                ingress.Receive(in sample);
                using (var cmd = new EntityCommandBuffer()) { ingress.Flush(cmd, b); cmd.Playback(b); }
                Assert.Equal(1, ingress.PendingCount);                        // unknown: held
                var e = b.CreateEntity();
                b.AddComponent(e, new NetworkIdentity { Value = 80 });
                b.AddComponent(e, new NetworkAuthority(1, 2));                // node 1 owns it; this is node 2
                map.Register(80, e);
                using (var cmd = new EntityCommandBuffer()) { ingress.Flush(cmd, b); cmd.Playback(b); }
                Assert.Equal(0, ingress.PendingCount);
                var c = b.GetComponentRO<RuleLeaves>(e);
                Assert.Equal((2, "@Bridge Health.Current", 37.5f), (c.Count, c.Leaves[0].Label.ToString(), c.Leaves[0].Measured));
            }

            var (owner, ownerMap, own) = Node(primaryOwner: 1, localNode: 1);
            using (owner)
            {
                owner.GetComponentRW<RuleLeaves>(own).Count = 1;
                var ingress = new EntityRuleLeavesIngressTranslator(null, ownerMap);
                ingress.Receive(in sample);
                using (var cmd = new EntityCommandBuffer()) { ingress.Flush(cmd, owner); cmd.Playback(owner); }
                Assert.Equal(1, owner.GetComponentRO<RuleLeaves>(own).Count);   // its own sample is dropped
            }
        }
    }
}
