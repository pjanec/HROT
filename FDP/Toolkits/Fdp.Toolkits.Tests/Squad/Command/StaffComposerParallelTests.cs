using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Core.Jobs;
using Fdp.Toolkit.Perception.Events;
using Fdp.Toolkit.Squad.Command;
using Xunit;

namespace Fdp.Toolkits.Tests.Squad.Command
{
    /// <summary>
    /// ⭐ CE-4518 (X-3; docs/DESIGN_Parallel_Simulation.md §18) — the staff composer as a per-commander phase: 60 commanders under one HQ, sitreps on
    /// a timer, launches heard by their men (several per commander per frame, interleaved on the bus) — the reports that go up are the SERIAL ones,
    /// in the same order, at 4 and 8 threads, and <see cref="StaffComposerSystem.SentCount"/> agrees.
    /// 🔴 Red-proof: the launch lookup without the sort (a commander's run not contiguous) ⇒ launches are lost and the sequences differ.
    /// </summary>
    [Collection("FdpWorkerPool")]
    public sealed class StaffComposerParallelTests
    {
        private static (EntityRepository World, Entity[] Commanders, Entity[][] Men) World(int commanders)
        {
            var w = new EntityRepository();
            w.RegisterComponent<UnitRoster>(); w.RegisterComponent<UnitSubordinate>(); w.RegisterComponent<ReportOutbox>();
            w.RegisterComponent<ReportingRequirements>(); w.RegisterComponent<SimTransform>();
            w.RegisterComponent<Fdp.Toolkit.Behavior.Components.BehaviorState>();   // the composer's query (WithOwnedWhen ⇒ With when ungated)
            w.Bus.RegisterManaged<SquadReportNotification>();
            w.Bus.Register<SoundContactEvent>();
            var hq = w.CreateEntity();
            w.AddComponent(hq, new UnitRoster());
            var cs = new Entity[commanders];
            var men = new Entity[commanders][];
            for (int c = 0; c < commanders; c++)
            {
                var cmd = w.CreateEntity();
                var roster = new UnitRoster();
                men[c] = new Entity[3];
                for (int m = 0; m < 3; m++)
                {
                    var man = w.CreateEntity();
                    w.AddComponent(man, new SimTransform { Position = new Vector3(c * 100f + m, 0f, 0f) });
                    w.AddComponent(man, new UnitSubordinate { Commander = cmd });
                    UnitRoster.Add(ref roster, man);
                    men[c][m] = man;
                }
                w.AddComponent(cmd, roster);
                w.AddComponent(cmd, new Fdp.Toolkit.Behavior.Components.BehaviorState());
                w.AddComponent(cmd, new UnitSubordinate { Commander = hq });
                w.AddComponent(cmd, new ReportingRequirements { SitrepEverySeconds = 0.5f + (c % 4) * 0.25f });
                cs[c] = cmd;
            }
            return (w, cs, men);
        }

        private static (List<string> Reports, int Sent) Run(bool parallel, int threads)
        {
            bool was = ParallelEntities.Enabled;
            ParallelEntities.Enabled = parallel;
            FdpWorkerPool.Resize(threads);
            try
            {
                var (w, cs, men) = World(60);
                var composer = new StaffComposerSystem();
                var reports = new List<string>();
                var rng = new Random(4518);
                for (int f = 0; f < 120; f++)
                {
                    w.SetSingleton(new GlobalTime { TotalTime = 1.0 + f * 0.1, DeltaTime = 0.1f, TimeScale = 1f });
                    for (int k = rng.Next(0, 6); k > 0; k--)   // launches heard by random men, interleaved across commanders
                    {
                        int c = rng.Next(cs.Length);
                        w.Bus.Publish(new SoundContactEvent
                        {
                            Observer = men[c][rng.Next(3)], X = rng.Next(0, 4000), Y = rng.Next(0, 4000), Radius = 50f,
                            Kind = (byte)Fdp.Toolkit.Perception.Signatures.SoundKind.Shot,
                            Family = (byte)(rng.Next(2) == 0 ? Fdp.Toolkit.Tkb.Domain.SoundFamily.Mortar : Fdp.Toolkit.Tkb.Domain.SoundFamily.Gun),
                        });
                    }
                    w.Bus.SwapBuffers();
                    composer.Execute(w, 0.1f);
                    w.Bus.SwapBuffers();
                    foreach (var n in w.ReadManagedEvents<SquadReportNotification>())
                        reports.Add($"{f}:{n.FromUnit.Index}>{n.ToUnit.Index}:{n.Report.Kind}/{n.Report.Subtype}@{n.Report.At.X:0},{n.Report.At.Y:0}");
                }
                w.Dispose();
                return (reports, composer.SentCount);
            }
            finally { ParallelEntities.Enabled = was; FdpWorkerPool.Resize(FdpWorkerPool.Budget()); }
        }

        [Fact]
        public void CE4518_TheParallelComposer_SendsTheSerialReports_InTheSameOrder()
        {
            var serial = Run(false, 1);
            Assert.True(serial.Reports.Count > 200, $"{serial.Reports.Count} reports: the rail would prove little");
            Assert.Contains(serial.Reports, r => r.Contains("Spot"));     // launches went up
            Assert.Contains(serial.Reports, r => r.Contains("Sitrep"));
            Assert.Equal(serial.Reports.Count, serial.Sent);
            foreach (int t in new[] { 4, 8 })
            {
                var par = Run(true, t);
                Assert.Equal(serial.Reports, par.Reports);
                Assert.Equal(serial.Sent, par.Sent);
            }
        }
    }
}
