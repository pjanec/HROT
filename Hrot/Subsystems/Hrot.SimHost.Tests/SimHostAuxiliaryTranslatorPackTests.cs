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

        [Fact]
        public void APerceptionOnlyNode_ReadsShotsAndDetonations_AndSendsWhatItHeard_CE3065()
        {
            using var participant = new DdsParticipant(231u);
            var set = Compose(participant, NodeRole.Perception);

            Assert.Equal(1, Count<WeaponFireIngressTranslator>(set));
            Assert.Equal(1, Count<MunitionDetonationIngressTranslator>(set));
            Assert.Equal(1, Count<AudioTargetDetectedEgressTranslator>(set));
        }

        [Fact]
        public void APerceptionAndMuscleNode_HasNoShotIngress_AndEachTranslatorOnce_CE3065()
        {
            using var participant = new DdsParticipant(231u);
            var set = Compose(participant, NodeRole.Perception | NodeRole.MuscleGround);

            // ⛔ Its shots are local events; its own WeaponFire samples would loop back and heat each shooter twice.
            Assert.Equal(0, Count<WeaponFireIngressTranslator>(set));
            Assert.Equal(1, Count<MunitionDetonationIngressTranslator>(set));
            Assert.Equal(1, Count<AudioTargetDetectedEgressTranslator>(set));
        }

        [Fact]
        public void ABrainOnlyNode_GetsNoSensorInputs_CE3065()
        {
            using var participant = new DdsParticipant(231u);
            var set = Compose(participant, NodeRole.Brain);

            Assert.Equal(0, Count<WeaponFireIngressTranslator>(set));
            Assert.Equal(0, Count<MunitionDetonationIngressTranslator>(set));
            Assert.Equal(0, Count<AudioTargetDetectedEgressTranslator>(set));
        }
    }
}
