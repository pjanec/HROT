using System.Threading;
using CycloneDDS.Runtime;
using Fdp.Kernel;
using Fdp.Toolkit.Replication.Components;
using Hrot.Map.Common.Replication;
using Fdp.ModuleHost.Abstractions;

using OwnershipUpdateMsg  = Fdp.Toolkit.Replication.Messages.OwnershipUpdate;
using OwnershipUpdateWire = Fdp.Network.Cyclone.Topics.OwnershipUpdate;

namespace Hrot.SimHost.Tests
{
    [Collection("SimHostDds")]
    public class OwnershipUpdateTranslatorTests
    {
        [Fact]
        [Trait("Category", "Integration")]
        public void ScanAndPublish_ForwardsOnlyLocalOwnerClaims()
        {
            const int localNodeId = 7;
            const uint domainId = 212u;
            using var participant = new DdsParticipant(domainId);
            using var reader = new DdsReader<OwnershipUpdateWire>(participant, "SST_OwnershipUpdate");
            var translator = new OwnershipUpdateTranslator(participant, localNodeId);

            var repo = new EntityRepository();
            repo.RegisterEvent<OwnershipUpdateMsg>();

            long packedKey = Fdp.ModuleHost.Network.OwnershipExtensions.PackKey(descriptorTypeId: 2, instanceId: 0);

            repo.Bus.Publish(new OwnershipUpdateMsg
            {
                NetworkId = new NetworkIdentity { Value = 1001 },
                PackedKey = packedKey,
                NewOwnerNodeId = localNodeId
            });

            repo.Bus.Publish(new OwnershipUpdateMsg
            {
                NetworkId = new NetworkIdentity { Value = 1002 },
                PackedKey = packedKey,
                NewOwnerNodeId = localNodeId + 1
            });

            repo.Bus.SwapBuffers();

            Thread.Sleep(200);
            translator.ScanAndPublish(repo);
            Thread.Sleep(250);

            using var loan = reader.Take();
            int written = 0;
            foreach (var sample in loan)
            {
                if (!sample.IsValid)
                    continue;

                written++;
                Assert.Equal(1001, sample.Data.EntityId);
                Assert.Equal(localNodeId, sample.Data.NewOwner);
            }

            Assert.Equal(1, written);
        }

        [Fact]
        [Trait("Category", "Integration")]
        public void PollIngress_DropsLoopbackSamplesForLocalOwner()
        {
            const int localNodeId = 9;
            const uint domainId = 213u;
            using var participant = new DdsParticipant(domainId);
            using var writer = new DdsWriter<OwnershipUpdateWire>(participant, "SST_OwnershipUpdate");
            var translator = new OwnershipUpdateTranslator(participant, localNodeId);

            var repo = new EntityRepository();
            repo.RegisterEvent<OwnershipUpdateMsg>();

            writer.Write(new OwnershipUpdateWire
            {
                EntityId = 2222,
                DescrTypeId = 2,
                InstanceId = 0,
                NewOwner = localNodeId
            });

            Thread.Sleep(200);
            ISimulationView view = repo;
            var cmd = view.GetCommandBuffer();
            translator.PollIngress(cmd, view);

            repo.Bus.SwapBuffers();
            var events = repo.Bus.Consume<OwnershipUpdateMsg>();
            Assert.Equal(0, events.Length);
        }
    }
}
