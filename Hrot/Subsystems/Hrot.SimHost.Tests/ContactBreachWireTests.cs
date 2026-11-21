using System.Collections.Generic;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Terrain;
using Hrot.Map.Common.Dds;
using Hrot.NED.Messages;
using Hrot.Network.NED.SimHost;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐⭐ <c>CE-5001</c> (B-3b, V-17; docs/DESIGN_Vehicle_Breaching.md §7.16) — a vehicle's contact with a barrier reaches every node: the deciding node's egress sends each local
    /// break, and a push / "too strong" at most every 0.2 s per hull (stamped with its node id); a remote copy is never sent again; the receiving node drops its own sample and a hull it
    /// does not know, and republishes the rest as a REMOTE <see cref="ContactBreachEvent"/> (what the HUD cue, the dust and the crash read). 🔴 Red-proof: drop the repeat
    /// throttle ⇒ three pushes in one frame are three samples.
    /// </summary>
    public sealed class ContactBreachWireTests
    {
        private sealed class CapturingWriter<T> : IDdsWriter<T>
        {
            public List<T> Written { get; } = new();
            public void Write(T sample) => Written.Add(sample);
            public void DisposeInstance(T key) { }
        }

        private static EntityRepository World(double now)
        {
            var w = new EntityRepository();
            w.RegisterEvent<ContactBreachEvent>();
            w.SetSingletonUnmanaged(new GlobalTime { TotalTime = now, DeltaTime = 0.016f, TimeScale = 1f });
            return w;
        }

        private static void Frame(EntityRepository w, double now, params ContactBreachEvent[] events)
        {
            w.SetSingletonUnmanaged(new GlobalTime { TotalTime = now, DeltaTime = 0.016f, TimeScale = 1f });
            foreach (var e in events) w.Bus.Publish(e);
            w.Bus.SwapBuffers();
        }

        [Fact]
        public void ABreak_IsAlwaysSent_APushAtMostFiveTimesASecond_ARemoteCopyNever()
        {
            using var w = World(10.0);
            var map = new NetworkEntityMap();
            var hull = w.CreateEntity(); map.Register(42, hull);
            var stranger = w.CreateEntity();
            var writer = new CapturingWriter<ContactBreach>();
            var egress = new ContactBreachEgressTranslator(writer, map, localNodeId: 3);
            ContactBreachEvent E(Entity v, ContactBreachKind k, float progress = 0f, bool remote = false) => new() { Vehicle = v, Kind = (byte)k, X = 1f, Y = 2f, Z = 0.5f, Progress = progress, IsRemote = remote };

            Frame(w, 10.0, E(hull, ContactBreachKind.Pushing, 0.1f), E(hull, ContactBreachKind.Pushing, 0.2f), E(hull, ContactBreachKind.Pushing, 0.3f),
                  E(hull, ContactBreachKind.Broke, remote: true), E(stranger, ContactBreachKind.Broke));
            egress.ScanAndPublish(w);
            var push = Assert.Single(writer.Written);
            Assert.Equal((byte)ContactBreachKind.Pushing, push.Kind); Assert.Equal(42, push.VehicleEntityId); Assert.Equal(3, push.SourceNodeId); Assert.Equal(0.1f, push.Progress);

            Frame(w, 10.1, E(hull, ContactBreachKind.Pushing, 0.5f));
            egress.ScanAndPublish(w);
            Assert.Single(writer.Written);                                       // 0.1 s later: held back
            Frame(w, 10.25, E(hull, ContactBreachKind.Pushing, 0.8f), E(hull, ContactBreachKind.Broke));
            egress.ScanAndPublish(w);
            Assert.Equal(3, writer.Written.Count);                               // the push again, and the break — always
            Assert.Equal((byte)ContactBreachKind.Broke, writer.Written[2].Kind);
        }

        [Fact]
        public void TheReceiver_DropsItsOwn_AndAnUnknownHull_AndRepublishesTheRestAsRemote()
        {
            using var w = World(10.0);
            var map = new NetworkEntityMap();
            var hull = w.CreateEntity(); map.Register(42, hull);
            var ingress = new ContactBreachIngressTranslator(participant: null, map, localNodeId: 5);
            var cmd = (EntityCommandBuffer)((ISimulationView)w).GetCommandBuffer();
            ingress.ProcessSample(new ContactBreach { VehicleEntityId = 42, Kind = (byte)ContactBreachKind.Broke, SourceNodeId = 5 }, cmd);   // mine
            ingress.ProcessSample(new ContactBreach { VehicleEntityId = 99, Kind = (byte)ContactBreachKind.Broke, SourceNodeId = 3 }, cmd);   // a hull unknown here
            ingress.ProcessSample(new ContactBreach { VehicleEntityId = 42, Kind = (byte)ContactBreachKind.TooStrong, X = 7f, SourceNodeId = 3 }, cmd);
            cmd.Playback(w);
            w.Bus.SwapBuffers();
            var e = Assert.Single(w.Bus.Read<ContactBreachEvent>().ToArray());
            Assert.Equal(hull, e.Vehicle); Assert.Equal((byte)ContactBreachKind.TooStrong, e.Kind); Assert.Equal(7f, e.X); Assert.True(e.IsRemote);
        }
    }
}
