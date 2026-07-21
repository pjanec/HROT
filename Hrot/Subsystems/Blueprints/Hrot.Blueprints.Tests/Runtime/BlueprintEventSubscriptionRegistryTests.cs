using System.Linq;
using Fdp.Toolkit.Blueprints;
using Xunit;

namespace Hrot.Blueprints.Tests.Runtime;

/// <summary>Q#14 slice 3a: the event-type-id → subscribers index the dispatch pump queries.</summary>
public sealed class BlueprintEventSubscriptionRegistryTests
{
    private static void NoOp(
        System.Span<byte> s, Fdp.ModuleHost.Abstractions.ISimulationView v,
        Fdp.Interfaces.IEntityCommandBuffer e, Fdp.Core.Entity self,
        float t, float dt, System.ReadOnlySpan<byte> p) { }

    private static BlueprintDefinition Def(string name, params string[] eventKeys) => new()
    {
        Name          = name,
        Kind          = BlueprintDispatchKind.Instance,
        StructureHash = 1,
        StateSize     = 8,
        EventHandlers = eventKeys.ToDictionary(
            k => k, k => (EventHandlerDelegate)NoOp, System.StringComparer.Ordinal),
    };

    // Deterministic resolver mirroring the bus's custom-event FQN hash.
    private static int Hash(string s) => s.GetHashCode() & 0x7FFFFFFF;

    [Fact]
    public void Build_IndexesSubscribersByTypeId()
    {
        var a = Def("A", "SquadRegroup", "TargetSpotted");
        var b = Def("B", "SquadRegroup");
        var reg = BlueprintEventSubscriptionRegistry.Build(new[] { a, b }, Hash);

        var regroup = reg.ForTypeId(Hash("SquadRegroup"));
        Assert.Equal(2, regroup.Count);
        Assert.Contains(regroup, s => s.Def.Name == "A");
        Assert.Contains(regroup, s => s.Def.Name == "B");

        var spotted = reg.ForTypeId(Hash("TargetSpotted"));
        Assert.Single(spotted);
        Assert.Equal("A", spotted[0].Def.Name);

        Assert.True(reg.HasSubscribers(Hash("SquadRegroup")));
        Assert.False(reg.HasSubscribers(Hash("Nope")));
        Assert.Empty(reg.ForTypeId(Hash("Nope")));
    }

    [Fact]
    public void Build_SkipsDefsWithNoEventHandlers()
    {
        var reg = BlueprintEventSubscriptionRegistry.Build(new[] { Def("Plain") }, Hash);
        Assert.Empty(reg.SubscribedTypeIds);
    }
}
