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
    /// 🔴 Red-proof: the launch lookup without the sort (a commander's run not contiguous) ⇒ launches are lost ⇒ the heard = spotted assertion fails.
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

        private static (List<string> Reports, int Sent, HashSet<int> Heard, HashSet<int> Spotted) Run(bool parallel, int threads)
        {
            bool was = ParallelEntities.Enabled;
            ParallelEntities.Enabled = parallel;
            FdpWorkerPool.Resize(threads);
            try
            {
                var (w, cs, men) = World(60);
                var composer = new StaffComposerSystem();
                var reports = new List<string>();
                var heard = new HashSet<int>(); var spotted = new HashSet<int>();   // commanders whose men heard a launch / that SPOTted one
                var rng = new Random(4518);
                for (int f = 0; f < 120; f++)
                {
                    w.SetSingleton(new GlobalTime { TotalTime = 1.0 + f * 0.1, DeltaTime = 0.1f, TimeScale = 1f });
                    for (int k = rng.Next(0, 6); k > 0; k--)   // launches heard by random men, interleaved across commanders
                    {
                        int c = rng.Next(cs.Length);
                        if (f < 80) heard.Add(cs[c].Index);   // its SPOT goes after ReportDelaySeconds (3 s = 30 frames): only those that can arrive
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
                    {
                        if (n.Report.Kind == ReportKind.Spot && (n.Report.Equipment & ElementEquipment.IndirectFire) != 0) spotted.Add(n.FromUnit.Index);
                        reports.Add($"{f}:{n.FromUnit.Index}>{n.ToUnit.Index}:{n.Report.Kind}/{n.Report.Subtype}@{n.Report.At.X:0},{n.Report.At.Y:0}");
                    }
                }
                w.Dispose();
                return (reports, composer.SentCount, heard, spotted);
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
            // ⭐ absolute, not only serial = parallel: a commander's FIRST launch always goes up (no earlier one to be the same as)
            Assert.True(serial.Heard.Count > 20, $"{serial.Heard.Count} commanders heard a launch");
            Assert.Equal(serial.Heard, serial.Spotted);
            foreach (int t in new[] { 4, 8 })
            {
                var par = Run(true, t);
                Assert.Equal(serial.Reports, par.Reports);
                Assert.Equal(serial.Sent, par.Sent);
            }
        }

        /// <summary>⭐ CE-4518 — a steady composer frame (nothing heard, nothing due) allocates nothing, serial and as the phase — 0 B on the hot
        /// path (CLAUDE.md, R-2084). 🔴 Red-proof by construction: a per-frame list or closure in the job would show here.</summary>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void CE4518_ASteadyFrame_AllocatesNothing(bool parallel)
        {
            bool was = ParallelEntities.Enabled;
            ParallelEntities.Enabled = parallel;
            FdpWorkerPool.Resize(4);
            try
            {
                var (w, cs, _) = World(60);
                for (int c = 0; c < cs.Length; c++) w.SetComponent(cs[c], new ReportingRequirements());   // no sitrep timer: nothing becomes due
                var composer = new StaffComposerSystem();
                for (int f = 0; f < 40; f++)   // warm: outboxes added, buffers grown, the pool waited on once
                {
                    w.SetSingleton(new GlobalTime { TotalTime = 1.0 + f * 0.1, DeltaTime = 0.1f, TimeScale = 1f });
                    w.Bus.SwapBuffers(); composer.Execute(w, 0.1f); w.Bus.SwapBuffers();
                }
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int f = 40; f < 60; f++)
                {
                    w.SetSingleton(new GlobalTime { TotalTime = 1.0 + f * 0.1, DeltaTime = 0.1f, TimeScale = 1f });
                    composer.Execute(w, 0.1f);
                }
                Assert.Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);
                w.Dispose();
            }
            finally { ParallelEntities.Enabled = was; FdpWorkerPool.Resize(FdpWorkerPool.Budget()); }
        }
    }
}
