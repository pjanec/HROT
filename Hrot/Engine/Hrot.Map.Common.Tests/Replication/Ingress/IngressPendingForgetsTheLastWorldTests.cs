using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Replication.Services;
using Hrot.Map.Common.Replication.Ingress;
using Hrot.NED.Descriptors;
using Xunit;

namespace Hrot.Map.Common.Tests.Replication.Ingress;

/// <summary>
/// ⭐ R-4297 ④ (CE-1980; 📄 docs/DESIGN_System_State_Inventory.md §9) — an ingress translator's PENDING samples (received for a network id whose entity does not exist
/// yet) name the world they came from: a world replacement (a load, a restore — <see cref="WorldEpoch"/>) forgets them, as the overlay ingress always did, so the next
/// world's entity that REUSES the id is not handed the old world's value. The wire is unchanged. 🔴 Red-proof (compiles): the <c>WorldEpoch.Moved</c> clear removed
/// from one translator ⇒ its case is red.
/// </summary>
public sealed class IngressPendingForgetsTheLastWorldTests
{
    public static TheoryData<string> Translators => new() { "Weather", "Passengers", "ScenarioClock", "TrafficDemand", "WeaponSystemState" };

    [Theory]
    [MemberData(nameof(Translators))]
    public void APendingSample_IsForgottenAtAWorldReplacement_AndKeptOtherwise(string which)
    {
        using var repo = new EntityRepository();
        var map = new NetworkEntityMap();
        var cmd = new EntityCommandBuffer();
        const int UnknownNetId = 4242;   // no entity yet: the sample waits
        (System.Action receive, System.Action<IEntityCommandBuffer, ISimulationView> poll, System.Func<int> pending) t = which switch
        {
            "Weather" => Make(new EntityWeatherIngressTranslator(null, map), x => x.Receive(new EntityWeather { EntityId = UnknownNetId }), (x, c, v) => x.PollIngress(c, v), x => x.PendingCount),
            "Passengers" => Make(new EntityPassengersIngressTranslator(null, map), x => x.Receive(new EntityPassengers { EntityId = UnknownNetId }), (x, c, v) => x.PollIngress(c, v), x => x.PendingCount),
            "ScenarioClock" => Make(new EntityScenarioClockIngressTranslator(null, map), x => x.Receive(new EntityScenarioClock { EntityId = UnknownNetId }), (x, c, v) => x.PollIngress(c, v), x => x.PendingCount),
            "TrafficDemand" => Make(new EntityTrafficDemandIngressTranslator(null, map), x => x.Receive(new EntityTrafficDemand { EntityId = UnknownNetId }), (x, c, v) => x.PollIngress(c, v), x => x.PendingCount),
            _ => Make(new EntityWeaponSystemStateIngressTranslator(null, map), x => x.Receive(new EntityWeaponSystemState { EntityId = UnknownNetId }), (x, c, v) => x.PollIngress(c, v), x => x.PendingCount),
        };

        t.poll(cmd, repo);   // first sight of the world
        t.receive();
        t.poll(cmd, repo);
        Assert.Equal(1, t.pending());   // the same world: it keeps waiting for its entity

        WorldEpoch.Restored(repo);      // the world was put back (a restore; a load's Advance moves it the same way)
        t.poll(cmd, repo);
        Assert.Equal(0, t.pending());
    }

    private static (System.Action, System.Action<IEntityCommandBuffer, ISimulationView>, System.Func<int>) Make<T>(T x, System.Action<T> receive,
        System.Action<T, IEntityCommandBuffer, ISimulationView> poll, System.Func<T, int> pending)
        => (() => receive(x), (c, v) => poll(x, c, v), () => pending(x));
}
