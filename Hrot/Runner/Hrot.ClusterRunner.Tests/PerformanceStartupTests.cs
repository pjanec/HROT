using System;
using System.IO;
using Fdp.Core.Jobs;
using Hrot.ClusterRunner.Configuration;
using Hrot.ClusterRunner.Presentation;
using Xunit;

namespace Hrot.ClusterRunner.Tests
{
    /// <summary>
    /// ⭐⭐ U-2 / U-3 (CE-4531 / CE-4532, R-4224) — the product's performance defaults, per run mode, with NO environment variable: the editor and
    /// the player run the threaded node and the parallel phases; the server and a separate CGF / SimHost / orchestrator / traffic node the
    /// parallel phases (headless: no threaded node); <c>-m all</c> runs serial; the user file is obeyed; the menu writes it.
    /// 🔴 Red-proofs: the UI-host test dropped ⇒ the server resolves the threaded node; the <c>all</c> rule dropped ⇒ <c>-m all</c> parallel.
    /// </summary>
    [Collection(nameof(PerformanceStartupTests))]
    public class PerformanceStartupTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "hrot-perf-" + Guid.NewGuid().ToString("N"));

        public PerformanceStartupTests()
        {
            PerformanceStartup.PathOverride = Path.Combine(_dir, PerformanceSettings.FileName);
            PerformanceMenu.Forget();
        }

        public void Dispose()
        {
            PerformanceStartup.PathOverride = null;
            PerformanceMenu.Forget();
            try { Directory.Delete(_dir, true); } catch (IOException) { }
        }

        private static readonly Func<string, string?> NoEnv = _ => null;

        private static HrotRunnerConfiguration Mode(string mode)
        {
            var c = new HrotRunnerConfiguration { ModeString = mode, NoWait = true };
            c.Validate();
            return c;
        }

        [Theory]
        [InlineData("editor", 3, true)]
        [InlineData("player", 3, true)]   // CE-3446 — the shared staged presentation
        [InlineData("server", 0, true)]
        [InlineData("cgf", 0, true)]
        [InlineData("simhost", 0, true)]
        [InlineData("orchestrator", 0, true)]
        [InlineData("traffic", 0, true)]
        [InlineData("all", 0, false)]
        public void EveryMode_NoEnvironmentVariable_ResolvesItsDefault(string mode, int threadedKernel, bool parallel)
        {
            var (r, _, _) = PerformanceStartup.ResolveFor(Mode(mode), NoEnv);
            Assert.Equal((threadedKernel, parallel), (r.ThreadedKernelMode, r.ParallelSimulation));
        }

        [Fact]
        public void TheUserFile_IsObeyed_ByTheEditor_AndAHeadlessNode()
        {
            new PerformanceSettings { ThreadedEditor = false, ParallelSimulation = false, WorkerThreads = 2 }.Save(PerformanceStartup.PathOverride!);
            var (editor, _, _) = PerformanceStartup.ResolveFor(Mode("editor"), NoEnv);
            var (cgf, _, _) = PerformanceStartup.ResolveFor(Mode("cgf"), NoEnv);
            Assert.Equal((0, false, 2), (editor.ThreadedKernelMode, editor.ParallelSimulation, editor.WorkerThreads));
            Assert.Equal((0, false, 2), (cgf.ThreadedKernelMode, cgf.ParallelSimulation, cgf.WorkerThreads));
        }

        [Fact]
        public void AnEnvironmentVariable_OverridesTheFile_AndMAll()
        {
            new PerformanceSettings { ParallelSimulation = false }.Save(PerformanceStartup.PathOverride!);
            var (r, _, _) = PerformanceStartup.ResolveFor(Mode("all"), k => k == "HROT_PARALLEL_SIM" ? "1" : null);
            Assert.True(r.ParallelSimulation);
        }

        [Fact]
        public void TheMenu_WritesTheFile_ThatTheNextStartReads()
        {
            PerformanceMenu.Save(PerformanceMenu.Saved with { ParallelSimulation = false, WorkerThreads = 4 });
            Assert.True(File.Exists(PerformanceStartup.PathOverride));
            PerformanceMenu.Forget();
            Assert.Equal((false, 4), (PerformanceMenu.Saved.ParallelSimulation, PerformanceMenu.Saved.WorkerThreads));
            var (cgf, _, _) = PerformanceStartup.ResolveFor(Mode("cgf"), NoEnv);
            Assert.False(cgf.ParallelSimulation);
            Assert.Contains("restart to apply", PerformanceMenu.Status());   // what runs here differs from the saved choice
        }

        [Fact]
        public void MAll_Applied_LeavesEverySwitchOff()
        {
            bool before = ParallelEntities.Enabled;
            try
            {
                var line = PerformanceStartup.Apply(Mode("all"), NoEnv);
                Assert.False(ParallelEntities.Enabled);
                Assert.False(Fdp.ModuleHost.ThreadedKernelGate.ComposedWorlds);
                Assert.Contains("-m all (serial)", line);
            }
            finally { ParallelEntities.Enabled = before; }
        }
    
        /// <summary>⭐ CE-4585 (B-5) — <c>GET /performance</c>'s source: a process started with <c>HROT_PARALLEL_SIM=0</c> reports it from the
        /// <c>environment</c>; without it and with no saved file, from the <c>default</c>; a saved file is <c>file</c>; a headless node's threaded node
        /// is <c>not a UI host</c>. 🔴 Red-proof: <c>SourcesOf</c> ignoring the environment ⇒ the first assertion red.</summary>
        [Fact]
        public void ThePerformanceReport_SaysWhereEachSwitchCameFrom()
        {
            bool before = ParallelEntities.Enabled;
            try
            {
                PerformanceStartup.Apply(Mode("editor"), k => k == "HROT_PARALLEL_SIM" ? "0" : null);
                Assert.Equal("environment", PerformanceStatus.Sources.ParallelSimulation);
                Assert.False(PerformanceStatus.Running!.Value.ParallelSimulation);
                var report = PerformanceStatus.Report();
                Assert.Equal("environment", report["running"]!["sources"]!["parallelSimulation"]!.GetValue<string>());
                Assert.Contains("HROT_PARALLEL_SIM", report["howToChange"]!.GetValue<string>());

                PerformanceStartup.Apply(Mode("editor"), NoEnv);
                Assert.Equal(("default", "default"), (PerformanceStatus.Sources.ParallelSimulation, PerformanceStatus.Sources.ThreadedNode));
                Assert.True(PerformanceStatus.Running!.Value.ParallelSimulation);

                PerformanceMenu.Save(PerformanceMenu.Saved with { WorkerThreads = 2 });
                PerformanceStartup.Apply(Mode("cgf"), NoEnv);
                Assert.Equal(("file", "not a UI host"), (PerformanceStatus.Sources.WorkerThreads, PerformanceStatus.Sources.ThreadedNode));
            }
            finally { ParallelEntities.Enabled = before; Fdp.Core.FdpConfig.MaxDegreeOfParallelism = -1; }
        }

        /// <summary>⭐ CE-4585 (B-5) — the menu's performance toggles are refused over HTTP (they would rewrite the user's own saved file); the file
        /// is untouched. 🔴 Red-proof: the refusal predicate returning false ⇒ red.</summary>
        [Fact]
        public void ThePerformanceToggles_AreRefusedOverHttp_AndTheFileIsUntouched()
        {
            Assert.True(Hrot.Editor.DebugApi.DebugApiService.RefusedOverHttp("settings.performance.parallel"));
            Assert.True(Hrot.Editor.DebugApi.DebugApiService.RefusedOverHttp("settings.performance.threads.4"));
            Assert.False(Hrot.Editor.DebugApi.DebugApiService.RefusedOverHttp("edit.undo"));
            var svc = new Hrot.Editor.DebugApi.DebugApiService(new Hrot.Presentation.DebugApi.PerspectiveScopedDispatcher(
                Array.Empty<Hrot.Presentation.DebugApi.ISubsystemDebugProvider>(), currentPerspective: () => "Scenario", acksPending: null));
            var (result, error, _) = svc.InvokeEditorCommand("settings.performance.parallel", null, "shell");
            Assert.Null(result);
            Assert.StartsWith(Hrot.Editor.DebugApi.DebugApiService.HttpRefusedPrefix, error);
            Assert.Contains("HROT_PARALLEL_SIM", error);
            Assert.False(File.Exists(PerformanceStartup.PathOverride));   // nothing was saved
        }
}
}
