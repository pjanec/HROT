using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Network.Cyclone.Systems;
using Fdp.Toolkit.Replication.Services;
using Xunit;

namespace Fdp.Network.Cyclone.Tests;

/// <summary>
/// ⭐⭐ <c>CE-3329</c> PW — §12c W2: between a node's world swap (a replay seek, a checkpoint restore) and the master's commit, the
/// node's egress publishes NOTHING; the release republishes. 📄 docs/DESIGN_Replay_Time_Axis_And_Review.md §12a, §12c.
/// </summary>
public sealed class TheEgressHoldTests
{
    private sealed class CountingTranslator : INetworkTranslator
    {
        public int Scans { get; private set; }
        public string TopicName => "counting";
        public TranslatorDirection Direction => TranslatorDirection.Egress;
        public long ReceivedSampleCount => 0;
        public long SentSampleCount => Scans;
        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }
        public void ScanAndPublish(ISimulationView view) => Scans++;
    }

    [Fact]
    public void A_held_world_publishes_nothing_until_it_is_released()
    {
        using var world = new EntityRepository();
        var translator = new CountingTranslator();
        var egress = new CycloneEgressSystem(new INetworkTranslator[] { translator });

        egress.Execute(world, 0f);
        Assert.Equal(1, translator.Scans);

        EgressHold.Hold(world, "replay seek");
        Assert.True(EgressHold.IsHeld(world));
        egress.Execute(world, 0f);
        egress.Execute(world, 0f);
        Assert.Equal(1, translator.Scans);                 // held: no scan, no sample

        EgressHold.Release(world);
        egress.Execute(world, 0f);
        Assert.Equal(2, translator.Scans);                 // released: publishes again
    }

    [Fact]
    public void A_world_never_held_is_not_held()
    {
        using var world = new EntityRepository();
        Assert.False(EgressHold.IsHeld(world));
        EgressHold.Release(world);                          // releasing what was never held is a no-op
        Assert.False(EgressHold.IsHeld(world));
    }
}
