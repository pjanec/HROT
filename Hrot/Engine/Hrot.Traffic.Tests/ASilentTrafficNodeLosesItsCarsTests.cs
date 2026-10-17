using CycloneDDS.Runtime;
using Sim.Core;
using Sim.Replication;
using Sim.Replication.Dds;

namespace Hrot.Traffic.Tests;

/// <summary>
/// ⭐ CE-4021 (AQ93 E10, R-4027) — a traffic node that stops sending is dropped by every mirror after
/// <see cref="TrafficMirrorSystem.SilentSourceSeconds"/> of CLUSTER time; a paused cluster keeps it.
///
/// <para>📌 Measured 2026-10-17 on a five-process cluster: after the traffic node was killed, CGF, SimHost and IG kept 19–21 frozen car
/// replicas for good — the mirror's subscriber had no source timeout.</para>
/// </summary>
public sealed class ASilentTrafficNodeLosesItsCarsTests
{
    private const int Domain = 199;

    [Fact]
    public void ASourceSilentFor5ClusterSeconds_IsDropped_AndAPausedClusterKeepsIt()
    {
        double cluster = 100.0;
        using var writerSide = new DdsParticipant(Domain);
        using var readerSide = new DdsParticipant(Domain);
        var subscriber = TrafficMirrorSystem.NewSubscriber(readerSide, () => cluster);
        using (var sink = new DdsReplicationSink(writerSide, source: 9))
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            uint step = 1;
            while (!subscriber.Sources.ContainsKey(9) && DateTime.UtcNow < deadline)
            {
                sink.PublishFrame(step, cluster, new[] { Car(1) });
                step++;
                Thread.Sleep(100);
                subscriber.Pump();
            }
            Assert.True(subscriber.Sources.ContainsKey(9), "the reader never discovered the writer — the rail proves nothing");

            // Paused: no frames and no cluster time pass, however long the wall clock runs.
            Thread.Sleep(1500);
            subscriber.Pump();
            Assert.True(subscriber.Sources.ContainsKey(9), "a paused cluster dropped a traffic node that was only quiet");

            // Running, silent: the writer is still matched (not disposed), it simply sends nothing.
            cluster += TrafficMirrorSystem.SilentSourceSeconds + 1.0;
            subscriber.Pump();
            Assert.False(subscriber.Sources.ContainsKey(9),
                $"a traffic node silent for {TrafficMirrorSystem.SilentSourceSeconds + 1} cluster seconds was kept — its cars stay frozen");
        }
        subscriber.Dispose();
    }

    private static VehicleRecord Car(uint index)
        => new(new VehicleHandle(index, 1), DrModel.LaneArc, laneHandle: 0, pos: index, posLat: 0, speed: 10, accel: 0, latSpeed: 0,
               upcoming: default);
}
