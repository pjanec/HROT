using CycloneDDS.Runtime;
using Sim.Core;
using Sim.Replication;
using Sim.Replication.Dds;

namespace Hrot.Traffic.Tests;

/// <summary>
/// ⭐ CE-4017 — a reader that pumps once after the traffic node published SEVERAL frames still learns every car in them.
///
/// <para>📌 Measured 2026-10-17 on a five-process cluster: each node posed the same SUMO car differently, one node at a time freezing a
/// car for 5–10 s and then jumping. 📐 The mechanism: <c>TrafficSession.StepTo</c> catches up to 40 SUMO steps per call, every step
/// writes the vehicle frame on ONE instance (chunk 0), the frames are SPARSE (<c>PublishPolicy</c>: a car only when it diverges or its
/// 3 s heartbeat fires), and the topic was best-effort <c>KeepLast(1)</c> — so a reader saw only the last frame of a burst, the others'
/// cars lost their update, and <c>DrClock</c>'s 3 s extrapolation cap froze them until the next one.</para>
/// </summary>
public sealed class ABurstOfTrafficFramesReachesEveryReaderTests
{
    private const int Domain = 198;
    private const int Burst = 5;

    [Fact]
    public void FiveFramesBackToBack_EachWithItsOwnCar_AllReachTheReader()
    {
        using var writerSide = new DdsParticipant(Domain);
        using var readerSide = new DdsParticipant(Domain);
        using var sink = new DdsReplicationSink(writerSide, source: 7);
        var subscriber = new DdsMultiSourceSubscriber(readerSide, stampWithSimTime: true);

        // Discovery: keep a heartbeat frame going until the reader has seen the source.
        var deadline = DateTime.UtcNow.AddSeconds(10);
        uint step = 1;
        while (!subscriber.Sources.ContainsKey(7) && DateTime.UtcNow < deadline)
        {
            sink.PublishFrame(step, step * 0.5, new[] { Car(100) });
            step++;
            Thread.Sleep(100);
            subscriber.Pump();
        }
        Assert.True(subscriber.Sources.ContainsKey(7), "the reader never discovered the writer — the rail proves nothing");

        // The burst: one SUMO catch-up, five sparse frames, each carrying a car no other frame carries.
        for (uint i = 0; i < Burst; i++, step++)
            sink.PublishFrame(step, step * 0.5, new[] { Car(200 + i) });
        Thread.Sleep(500);
        subscriber.Pump();

        var history = subscriber.Sources[7].History;
        var missing = Enumerable.Range(0, Burst).Select(i => (uint)(200 + i)).Where(ix => !history.ContainsKey(new VehicleHandle(ix, 1))).ToList();
        Assert.True(missing.Count == 0, $"a burst of {Burst} frames lost the cars of {missing.Count}: [{string.Join(", ", missing)}]");
    }

    private static VehicleRecord Car(uint index)
        => new(new VehicleHandle(index, 1), DrModel.LaneArc, laneHandle: 0, pos: index, posLat: 0, speed: 10, accel: 0, latSpeed: 0,
               upcoming: default);
}
