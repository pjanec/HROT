using System.Threading;
using CycloneDDS.Runtime;
using Fdp.Examples.DDS;
using Xunit;

namespace Fdp.Examples.Scenarios.Tests
{
    /// <summary>
    /// DEM1-I001: Serialization round-trip tests for Fdp.Examples.DDS message structs.
    /// Uses the CycloneDDS write/read path as the "FDP native CDR serializer equivalent".
    /// </summary>
    public class DdsMsgSerializationTests
    {
        private const int TestDomainId = 42; // Isolated domain to avoid cross-test pollution.
        private const int WaitMs = 300;

        [Fact]
        public void DemoTransformMsg_Serialization_RoundTrip()
        {
            using var participant = new DdsParticipant(TestDomainId);
            using var writer = new DdsWriter<DemoTransformMsg>(participant, "Test_DemoTransformMsg");
            using var reader = new DdsReader<DemoTransformMsg>(participant, "Test_DemoTransformMsg");

            var sent = new DemoTransformMsg
            {
                NetworkId = 99L,
                PosX = 1.5f,
                PosY = 2.5f,
                PosZ = 3.5f,
                RotX = 0.1f,
                RotY = 0.2f,
                RotZ = 0.3f,
                RotW = 0.9f
            };
            writer.Write(sent);
            Thread.Sleep(WaitMs);

            using var scope = reader.Take();
            Assert.True(scope.Count > 0, "No samples received");

            bool found = false;
            for (int i = 0; i < scope.Count; i++)
            {
                if (scope.Infos[i].ValidData == 0) continue;
                var recv = scope[i];
                Assert.Equal(sent.NetworkId, recv.NetworkId);
                Assert.Equal(sent.PosX,      recv.PosX);
                Assert.Equal(sent.PosY,      recv.PosY);
                Assert.Equal(sent.PosZ,      recv.PosZ);
                Assert.Equal(sent.RotX,      recv.RotX);
                Assert.Equal(sent.RotY,      recv.RotY);
                Assert.Equal(sent.RotZ,      recv.RotZ);
                Assert.Equal(sent.RotW,      recv.RotW);
                found = true;
                break;
            }
            Assert.True(found, "No valid sample found after round-trip");
        }

        [Fact]
        public void DemoSpawnMsg_Serialization_RoundTrip()
        {
            using var participant = new DdsParticipant(TestDomainId);
            using var writer = new DdsWriter<DemoSpawnMsg>(participant, "Test_DemoSpawnMsg");
            using var reader = new DdsReader<DemoSpawnMsg>(participant, "Test_DemoSpawnMsg");

            var sent = new DemoSpawnMsg
            {
                NetworkId   = 42L,
                TkbType     = 100L,
                OwnerNodeId = 1,
                IsDestroyed = false
            };
            writer.Write(sent);
            Thread.Sleep(WaitMs);

            using var scope = reader.Take();
            Assert.True(scope.Count > 0, "No samples received");

            bool found = false;
            for (int i = 0; i < scope.Count; i++)
            {
                if (scope.Infos[i].ValidData == 0) continue;
                var recv = scope[i];
                Assert.Equal(sent.NetworkId,   recv.NetworkId);
                Assert.Equal(sent.TkbType,     recv.TkbType);
                Assert.Equal(sent.OwnerNodeId, recv.OwnerNodeId);
                Assert.Equal(sent.IsDestroyed, recv.IsDestroyed);
                found = true;
                break;
            }
            Assert.True(found, "No valid sample found after round-trip");
        }

        [Fact]
        public void DemoCombatInteractionMsg_Serialization_RoundTrip()
        {
            using var participant = new DdsParticipant(TestDomainId);
            using var writer = new DdsWriter<DemoCombatInteractionMsg>(participant, "Test_DemoCombatMsg");
            using var reader = new DdsReader<DemoCombatInteractionMsg>(participant, "Test_DemoCombatMsg");

            var sent = new DemoCombatInteractionMsg
            {
                ShooterNetId = 1L,
                TargetNetId  = 2L,
                IsHit        = true,
                Damage       = 50f
            };
            writer.Write(sent);
            Thread.Sleep(WaitMs);

            using var scope = reader.Take();
            Assert.True(scope.Count > 0, "No samples received");

            bool found = false;
            for (int i = 0; i < scope.Count; i++)
            {
                if (scope.Infos[i].ValidData == 0) continue;
                var recv = scope[i];
                Assert.Equal(sent.ShooterNetId, recv.ShooterNetId);
                Assert.Equal(sent.TargetNetId,  recv.TargetNetId);
                Assert.Equal(sent.IsHit,        recv.IsHit);
                Assert.Equal(sent.Damage,        recv.Damage);
                found = true;
                break;
            }
            Assert.True(found, "No valid sample found after round-trip");
        }
    }
}
