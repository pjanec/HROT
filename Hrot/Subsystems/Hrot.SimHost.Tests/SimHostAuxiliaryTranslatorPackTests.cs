using System;
using System.Linq;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Toolkit.Replication.Services;
using Hrot.Network.NED.IG;
using Hrot.Network.NED.SimHost;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐ CE-3065 (S7 design §3a) — the sensor inputs reach a Perception node by role, never twice.
    /// Heat and sound are derived where the sensor solves; a Perception node WITHOUT MuscleGround reads shots and
    /// detonations back from the wire, a node WITH it already has them as local events.
    /// </summary>
    [Collection("SimHostDds")]
    public class SimHostAuxiliaryTranslatorPackTests
    {
        private static Type[] Compose(DdsParticipant participant, NodeRole role)
            => SimHostAuxiliaryTranslatorPack
                .Create(participant, new NetworkEntityMap(), new FdpEventBus(), localNodeId: 1, role)
                .Select(t => t.GetType()).ToArray();

        private static int Count<T>(Type[] set) => set.Count(t => t == typeof(T));

        /// <summary>
        /// ⭐ CE-1148 S2 (docs/DESIGN_Node_Roles_And_Policies.md §3.5, R-3015) — the shot / burst / mark pairs are no longer wired here by
        /// role: they live in SharedTranslatorPack on every node (NedReplicationModuleTests.CE1148_…) and the ingress drops the node's own
        /// samples. ⚠ This replaces the CE-3065 / IF-3 rails that pinned the old "Perception && !MuscleGround" / "Brain only" exclusions.
        /// What a unit HEARD still leaves from the node that solved it (CE-3065), keyed on Perception / MuscleGround.
        /// </summary>
        [Theory]
        [InlineData(NodeRole.Perception, 1)]
        [InlineData(NodeRole.Perception | NodeRole.MuscleGround, 1)]
        [InlineData(NodeRole.MuscleGround, 1)]
        [InlineData(NodeRole.Brain, 0)]
        public void CE1148_NoEventPairIsRoleWiredHere_AndHeardContactsLeaveTheSolver(NodeRole role, int heardEgress)
        {
            using var participant = new DdsParticipant(231u);
            var set = Compose(participant, role);

            Assert.Equal(0, Count<WeaponFireIngressTranslator>(set));
            Assert.Equal(0, Count<WeaponFireNotificationEgressTranslator>(set));
            Assert.Equal(0, Count<MunitionDetonationIngressTranslator>(set));
            Assert.Equal(0, Count<MunitionDetonationEgressTranslator>(set));
            Assert.Equal(0, Count<RoundImpactIngressTranslator>(set));
            Assert.Equal(0, Count<RoundImpactEgressTranslator>(set));
            Assert.Equal(heardEgress, Count<AudioTargetDetectedEgressTranslator>(set));
        }

        /// <summary>⭐ CE-3167 (DESIGN_Entity_Control §2.3) — an operator's order may be issued on ANY node, so every role carries
        /// the tactical-intent EGRESS exactly once; the INGRESS (resolving it) stays on the brain.</summary>
        [Theory]
        [InlineData(NodeRole.Brain, 1)]
        [InlineData(NodeRole.MuscleGround, 0)]
        [InlineData(NodeRole.Perception | NodeRole.MuscleGround, 0)]
        [InlineData(NodeRole.Brain | NodeRole.MuscleGround, 1)]
        [InlineData(NodeRole.Map2D, 0)]   // ⭐ CE-1148 S3 — the IG station takes the egress from this pack like every node
        public void CE3167_EveryNodeForwardsOrders_OnlyTheBrainResolvesThem(NodeRole role, int ingress)
        {
            using var participant = new DdsParticipant(231u);
            var set = Compose(participant, role);

            Assert.Equal(1, Count<Hrot.Network.NED.SimHost.TacticalIntentEgressTranslator>(set));
            Assert.Equal(ingress, Count<Hrot.Network.NED.SimHost.TacticalIntentIngressTranslator>(set));
        }

        /// <summary>⭐ CE-3169 / CE-1148 S3 (docs/DESIGN_Node_Roles_And_Policies.md §3.5) — the IG's own list no longer carries the order
        /// egress: the IG builds the auxiliary pack like every node and takes it from there (CE3167_… with Map2D). A copy here would
        /// send every order twice.</summary>
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void CE1148_TheIgsOwnList_CarriesNoOrderEgress(bool headless)
        {
            using var participant = new DdsParticipant(231u);
            var set = new Hrot.Network.NED.IG.NedIgTranslators()
                .GetTranslators(participant, new NetworkEntityMap(), new FdpEventBus(), ghostCreationSystem: null, localNodeId: 1, headless)
                .Select(t => t.GetType()).ToArray();
            Assert.Equal(0, Count<Hrot.Network.NED.SimHost.TacticalIntentEgressTranslator>(set));
            Assert.Equal(0, Count<Hrot.Network.NED.SimHost.TacticalIntentIngressTranslator>(set));
        }
    }
}
