using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.Core.Jobs;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Perception.Events;
using Fdp.Toolkit.Perception.Systems;
using Xunit;
using Xunit.Abstractions;

namespace Fdp.Toolkit.Perception.Tests
{
    /// <summary>
    /// ⭐ P-1 (docs/DESIGN_Parallel_Simulation.md §4 the safety net; the parallel experiment E-3) — THE SERIAL-VS-PARALLEL RAIL for the threat
    /// evaluation: the same world (2 000 units, tracks onto each other, some hit), evaluated once by the serial loop and once in parallel batches at
    /// 1 / 2 / 4 / 8 threads, ends with every unit's TargetMemory byte for byte the same and the same SensorChangedEvents in the same order.
    /// 🔴 Red-proof: the parallel body writing its threat count into the SHARED dictionary instead of its own slot ⇒ a torn dictionary / a
    /// different next-frame edge set.
    /// </summary>
    [Collection("FdpWorkerPool")]
    public class ThreatEvaluationParallelRailTests
    {
        private readonly ITestOutputHelper _out;
        public ThreatEvaluationParallelRailTests(ITestOutputHelper o) => _out = o;

        private static EntityRepository World(int n, int seed)
        {
            var w = PerceptionTestWorldFactory.Create();
            if (!w.IsComponentTypeRegistered<Health>()) w.RegisterComponent<Health>();
            var rnd = new Random(seed);
            var all = new Entity[n];
            for (int i = 0; i < n; i++)
            {
                var e = w.CreateEntity();
                w.AddComponent(e, new SimTransform { Position = new Vector3(rnd.Next(800), rnd.Next(800), 0), Rotation = Quaternion.Identity });
                w.AddComponent(e, new Health { Current = 100, Max = 100 });
                all[i] = e;
            }
            for (int i = 0; i < n; i++)
            {
                var mem = new TargetMemory();
                for (int k = 0; k < rnd.Next(0, 5); k++)
                    TargetMemory.AddOrUpdateTarget(ref mem, entityId: (long)all[rnd.Next(n)].PackedValue, posX: rnd.Next(800), posY: rnd.Next(800), scoreBoost: rnd.Next(1, 100), tick: 0u);
                w.AddComponent(all[i], mem);
                if (rnd.Next(3) == 0)
                {
                    var tracks = new ActiveSensorTracks();
                    unsafe
                    {
                        int c = rnd.Next(1, 4);
                        for (int k = 0; k < c; k++) { tracks.EntityIds[k] = (long)all[rnd.Next(n)].PackedValue; tracks.PositionsX[k] = rnd.Next(800); tracks.PositionsY[k] = rnd.Next(800); }
                        tracks.Count = c;
                    }
                    w.AddComponent(all[i], tracks);
                }
            }
            return w;
        }

        private static (string Mem, string Events) Run(bool parallel, int threads, int frames = 3)
        {
            using var w = World(2000, 42);
            ParallelEntities.Enabled = parallel;
            FdpWorkerPool.Resize(threads);
            var sys = new ThreatEvaluationSystem();
            var ev = new System.Text.StringBuilder();
            var rnd = new Random(7);
            for (int f = 0; f < frames; f++)
            {
                // some units are hit between frames (the Hit edge)
                foreach (var e in w.Query().With<Health>().Build())
                    if (rnd.Next(10) == 0) w.GetComponentRW<Health>(e).Current -= 5;
                sys.Execute(w, 0.1f);
                ((EntityCommandBuffer)((ISimulationView)w).GetCommandBuffer()).Playback(w);
                w.Bus.SwapBuffers();
                foreach (ref readonly var x in w.ReadEvents<SensorChangedEvent>()) ev.Append(x.Unit.Index).Append(x.What).Append(';');
            }
            var mem = new System.Text.StringBuilder();
            foreach (var e in w.Query().With<TargetMemory>().Build())
            {
                var m = w.GetComponent<TargetMemory>(e);
                unsafe { var span = new ReadOnlySpan<byte>(&m, sizeof(TargetMemory)); mem.Append(e.Index).Append(':').Append(Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(span))).Append(','); }
            }
            return (mem.ToString(), ev.ToString());
        }

        [Fact]
        public void P1_ThreatEvaluation_Parallel_EqualsSerial_At1_2_4_8Threads()
        {
            try
            {
                var serial = Run(parallel: false, threads: 1);
                Assert.NotEmpty(serial.Events);
                foreach (int t in new[] { 1, 2, 4, 8 })
                {
                    var par = Run(parallel: true, threads: t);
                    Assert.Equal(serial.Mem, par.Mem);
                    Assert.Equal(serial.Events, par.Events);
                }
                _out.WriteLine($"serial == parallel at 1/2/4/8 threads; {serial.Events.Split(';').Length - 1} edges over 3 frames");
            }
            finally { ParallelEntities.Enabled = false; FdpWorkerPool.Resize(FdpWorkerPool.Budget()); }
        }
    }
}
