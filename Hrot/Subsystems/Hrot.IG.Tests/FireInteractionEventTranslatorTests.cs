using System.Threading;
using Hrot.NED.Messages;
using Hrot.Map.Common.Replication;
using CycloneDDS.Runtime;
using Fdp.Kernel;
using Fdp.Toolkit.Replication.Services;
using Fdp.ModuleHost.Abstractions;

using DdsFireInteractionEvent = Hrot.NED.Messages.FireInteractionEvent;
using EcsFireInteractionEvent = Hrot.Map.Common.Events.FireInteractionEvent;
using Fdp.Interfaces;

namespace Hrot.IG.Tests;

public class FireInteractionEventTranslatorTests
{
    [Fact]
    public void IG_PollIngress_PublishesEventOnBus()
    {
        const uint domainId = 150u;
        using var participant = new DdsParticipant(domainId);
        using var writer = new DdsWriter<DdsFireInteractionEvent>(participant, "FireInteractionEvent");
        var entityMap = new NetworkEntityMap();
        var translator = new FireInteractionEventTranslator(participant, entityMap);

        var repo = new EntityRepository();
        repo.RegisterEvent<EcsFireInteractionEvent>();

        ISimulationView      view = repo;
        IEntityCommandBuffer cmd  = view.GetCommandBuffer();

        var expected = new DdsFireInteractionEvent
        {
            ShooterX = 10f,
            ShooterY = 20f,
            TargetX  = 30f,
            TargetY  = 40f
        };

        Thread.Sleep(200);
        writer.Write(expected);
        Thread.Sleep(200);

        translator.PollIngress(cmd, view);
        ((EntityCommandBuffer)cmd).Playback(repo);

        repo.Bus.SwapBuffers();
        var events = repo.Bus.Consume<EcsFireInteractionEvent>();

        Assert.Equal(1, events.Length);
        Assert.Equal(expected.ShooterX, events[0].ShooterX);
        Assert.Equal(expected.ShooterY, events[0].ShooterY);
        Assert.Equal(expected.TargetX,  events[0].TargetX);
        Assert.Equal(expected.TargetY,  events[0].TargetY);
    }
}
