using System.Threading;
using Bagira.BDC.SSTM;
using Bagira.SimHost.Translators;
using CycloneDDS.Runtime;
using Fdp.Kernel;
using FDP.Toolkit.Replication.Services;

using DdsFireInteractionEvent = Bagira.BDC.SSTM.FireInteractionEvent;
using EcsFireInteractionEvent = Bagira.Map.Common.Events.FireInteractionEvent;

namespace Bagira.SimHost.Tests
{
    public class FireInteractionEventTranslatorTests
    {
        [Fact]
        public void SimHost_ScanAndPublish_WritesDdsOnEvent()
        {
            const uint domainId = 151u;
            using var participant = new DdsParticipant(domainId);
            using var reader = new DdsReader<DdsFireInteractionEvent>(participant, "FireInteractionEvent");
            var entityMap = new NetworkEntityMap();
            var translator = new FireInteractionEventTranslator(participant, entityMap);

            var repo = new EntityRepository();
            repo.RegisterEvent<EcsFireInteractionEvent>();

            repo.Bus.Publish(new EcsFireInteractionEvent
            {
                ShooterX = 1f,
                ShooterY = 2f,
                TargetX  = 3f,
                TargetY  = 4f
            });
            repo.Bus.SwapBuffers();

            Thread.Sleep(200);
            translator.ScanAndPublish(repo);
            Thread.Sleep(200);

            using var loan = reader.Take();
            bool found = false;
            foreach (var sample in loan)
            {
                if (!sample.IsValid)
                    continue;

                if (sample.Data.ShooterX == 1f &&
                    sample.Data.ShooterY == 2f &&
                    sample.Data.TargetX == 3f &&
                    sample.Data.TargetY == 4f)
                {
                    found = true;
                    break;
                }
            }

            Assert.True(found, "Expected FireInteractionEvent DDS sample to be written.");
        }
    }
}
