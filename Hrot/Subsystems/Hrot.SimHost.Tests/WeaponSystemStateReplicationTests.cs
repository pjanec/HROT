using Fdp.Core;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Hrot.Map.Common.Replication.Egress;
using Hrot.Map.Common.Replication.Ingress;
using Hrot.NED.Descriptors;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐⭐ Weapon systems WS-1 (W-C, R-136; docs/designs/weapons/DESIGN_Weapon_Systems.md §2) — a weapon system's deploy state crosses the
    /// wire (<c>dtWeaponSystemState</c>, new): the owner publishes it on change, another node applies it (holding a sample until the weapon is
    /// known there), and the recorded owner never takes its own sample back.
    /// </summary>
    public sealed class WeaponSystemStateReplicationTests
    {
        private const long M2Id = 950L;

        private static EntityRepository World()
        {
            var w = new EntityRepository();
            w.RegisterComponent<NetworkIdentity>();
            w.RegisterComponent<NetworkAuthority>();
            w.RegisterComponent<WeaponSystemState>();
            return w;
        }

        private static Entity Weapon(EntityRepository w, NetworkEntityMap map)
        {
            var e = w.CreateEntity();
            w.AddComponent(e, new NetworkIdentity { Value = M2Id });
            w.AddComponent(e, new WeaponSystemState { Deploy = WeaponDeploy.Deployed });
            map.Register(M2Id, e);
            return e;
        }

        /// <summary>⭐ The descriptor ordinal the toolkit keys ownership on is NED's.</summary>
        [Fact]
        public void WS1_TheDeployStateOrdinal_IsNeds()
            => Assert.Equal((long)EDescriptorType.dtWeaponSystemState, WeaponDeployment.WeaponSystemStateDescriptorOrdinal);

        [Fact]
        public void WS1_TheOwnersDeployState_ReachesTheOtherNode_OnChange()
        {
            using var owner = World();
            var ownerMap = new NetworkEntityMap();
            var m2 = Weapon(owner, ownerMap);
            var egress = new EntityWeaponSystemStateEgressTranslator(participant: null);
            egress.ScanAndPublish(owner);
            Assert.Equal(1, egress.SentSampleCount);
            egress.ScanAndPublish(owner);
            Assert.Equal(1, egress.SentSampleCount);   // unchanged ⇒ said once

            owner.GetComponentRW<WeaponSystemState>(m2) = new WeaponSystemState { Deploy = WeaponDeploy.Packing, Until = 12.5 };
            egress.ScanAndPublish(owner);
            Assert.Equal(2, egress.SentSampleCount);
            var sample = EntityWeaponSystemStateEgressTranslator.ToSample(M2Id, owner.GetComponentRO<WeaponSystemState>(m2));

            using var other = World();
            var otherMap = new NetworkEntityMap();
            var ingress = new EntityWeaponSystemStateIngressTranslator(participant: null, otherMap);
            using var cmd = new EntityCommandBuffer();
            ingress.Receive(in sample);
            ingress.Flush(cmd, other);
            Assert.Equal(1, ingress.PendingCount);   // not known here yet: held

            var otherM2 = Weapon(other, otherMap);
            ingress.Flush(cmd, other);
            cmd.Playback(other);
            Assert.Equal(0, ingress.PendingCount);
            var got = other.GetComponentRO<WeaponSystemState>(otherM2);
            Assert.Equal((WeaponDeploy.Packing, 12.5), (got.Deploy, got.Until));
        }
    }
}
