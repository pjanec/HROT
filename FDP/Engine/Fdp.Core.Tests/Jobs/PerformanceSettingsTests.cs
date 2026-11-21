using System;
using System.Collections.Generic;
using System.IO;
using Fdp.Core.Jobs;
using Xunit;

namespace Fdp.Core.Tests.Jobs
{
    /// <summary>
    /// ⭐⭐ U-2 / U-3 (CE-4531 / CE-4532, R-4224) — the performance settings' precedence: an explicit <c>HROT_*</c> variable &gt; <c>-m all</c>
    /// serial &gt; the user file &gt; ON; the threaded node only on a UI host. 🔴 Red-proofs: the default flipped off ⇒ <c>NoFile_EverythingOn</c>;
    /// the serial rule dropped ⇒ <c>MAll_IsSerial</c>; the env read dropped ⇒ <c>AnEnvironmentVariable_OverridesTheFile</c>.
    /// </summary>
    public class PerformanceSettingsTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "hrot-perf-" + Guid.NewGuid().ToString("N"));
        private string FilePath => Path.Combine(_dir, PerformanceSettings.FileName);
        public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

        private static Func<string, string?> Env(params (string Key, string Value)[] vars)
        {
            var d = new Dictionary<string, string>();
            foreach (var (k, v) in vars) d[k] = v;
            return k => d.TryGetValue(k, out var v) ? v : null;
        }

        [Fact]
        public void NoFile_EverythingOn()
        {
            var r = PerformanceSettings.Resolve(PerformanceSettings.Load(FilePath), Env(), serialMode: false, uiHost: true);
            Assert.Equal(3, r.ThreadedKernelMode);
            Assert.True(r.ParallelSimulation);
            Assert.Equal(0, r.WorkerThreads);                         // auto
        }

        [Fact]
        public void TheFile_IsObeyed_AndRoundTrips()
        {
            new PerformanceSettings { ThreadedEditor = false, ParallelSimulation = false, WorkerThreads = 4 }.Save(FilePath);
            var file = PerformanceSettings.Load(FilePath);
            Assert.Equal(new PerformanceSettings { ThreadedEditor = false, ParallelSimulation = false, WorkerThreads = 4 }, file);
            var r = PerformanceSettings.Resolve(file, Env(), serialMode: false, uiHost: true);
            Assert.Equal((0, false, 4), (r.ThreadedKernelMode, r.ParallelSimulation, r.WorkerThreads));
        }

        [Fact]
        public void AnEnvironmentVariable_OverridesTheFile()
        {
            new PerformanceSettings { ThreadedEditor = false, ParallelSimulation = false, WorkerThreads = 4 }.Save(FilePath);
            var r = PerformanceSettings.Resolve(PerformanceSettings.Load(FilePath),
                Env(("HROT_THREADED_KERNEL", "3"), ("HROT_PARALLEL_SIM", "1"), ("HROT_SIM_THREADS", "2")), serialMode: false, uiHost: true);
            Assert.Equal((3, true, 2, "environment"), (r.ThreadedKernelMode, r.ParallelSimulation, r.WorkerThreads, r.Source));
        }

        [Fact]
        public void MAll_IsSerial_WhateverTheFile_ButAVariableStillTurnsItOn()
        {
            var on = PerformanceSettings.Default;
            var r = PerformanceSettings.Resolve(on, Env(), serialMode: true, uiHost: true);
            Assert.Equal((0, false), (r.ThreadedKernelMode, r.ParallelSimulation));
            var forced = PerformanceSettings.Resolve(on, Env(("HROT_PARALLEL_SIM", "1")), serialMode: true, uiHost: true);
            Assert.True(forced.ParallelSimulation);
        }

        [Fact]
        public void AHeadlessNode_RunsTheParallelPhases_NeverTheThreadedNode()
        {
            var r = PerformanceSettings.Resolve(PerformanceSettings.Default, Env(), serialMode: false, uiHost: false);
            Assert.Equal((0, true), (r.ThreadedKernelMode, r.ParallelSimulation));
        }

        [Fact]
        public void ABrokenFile_IsTheDefaults_NeverAThrow()
        {
            Directory.CreateDirectory(_dir);
            File.WriteAllText(FilePath, "{ not json");
            Assert.Equal(PerformanceSettings.Default, PerformanceSettings.Load(FilePath));
        }

        [Fact]
        public void TheStartUpLine_IsLanguageNeutral()
        {
            var line = PerformanceSettings.Describe(new ResolvedPerformance(3, true, 8, "settings"));
            Assert.Equal("[Performance] threaded node ON · parallel simulation ON · worker threads 8 (settings)", line);
        }
    }
}
