using System;
using System.IO;
using System.Numerics;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Runner;
using Xunit;

namespace Fdp.Toolkit.Runner.Tests
{
    /// <summary>
    /// CE-3340 (3) - the content seed (repository -> NAS) runs in ONE place: <see cref="SubsystemOrchestrator.Initialize"/>, which every host's start passes
    /// through, BEFORE any subsystem initialises (the asset catalogues read the NAS from their own Initialize). A host that must not touch the NAS opts out
    /// with <see cref="RunnerOptions.SeedContent"/>. The per-host-family source scan lives in <c>Hrot.ClusterRunner.Tests</c>
    /// (<c>EveryHostSeedsThroughTheOneHostStartRails</c>).
    /// </summary>
    public sealed class TheContentSeedRunsAtTheOneHostStartTests : IDisposable
    {
        private readonly string _nas = Path.Combine(Path.GetTempPath(), "host-seed-" + Guid.NewGuid().ToString("N"));

        public void Dispose() { try { Directory.Delete(_nas, recursive: true); } catch { /* best effort */ } }

        private sealed class ProbeSubsystem : ISubsystem
        {
            private readonly string _nas;
            public bool SawSeededNasAtInitialize;
            public ProbeSubsystem(string nas) { _nas = nas; }
            public string Name => "Probe";
            public Vector4 TitleBarColor => Vector4.Zero;
            public void Initialize(SubsystemConfig config)
                => SawSeededNasAtInitialize = Directory.Exists(ContentLayout.ScenarioRecipes(_nas))
                                              && Directory.Exists(ContentLayout.TerrainAssets(_nas))
                                              && Directory.Exists(ContentLayout.Scenarios(_nas));
            public void Update(float deltaTime) { }
            public void DrawWorld() { }
            public void DrawUI() { }
            public void Shutdown() { }
        }

        /// <summary>
        /// The NAS holds scenarios/, assets/ (terrain) and recipes/scenarios by the time the FIRST subsystem's Initialize runs - seeded by the orchestrator, not by the subsystem
        /// or the host. Red proof: the seed call removed from <c>SubsystemOrchestrator.Initialize</c> => <see cref="ProbeSubsystem.SawSeededNasAtInitialize"/> is false.
        /// </summary>
        [Fact]
        public void TheOrchestratorSeedsTheNas_BeforeAnySubsystemInitializes()
        {
            Assert.NotNull(ContentLayout.TryFindSourceTree());   // the tests run in a checkout; a production install would seed nothing
            var probe = new ProbeSubsystem(_nas);

            new SubsystemOrchestrator(new ISubsystem[] { probe }, new RunnerOptions { Headless = true, NasRoot = _nas }).Initialize();

            Assert.True(probe.SawSeededNasAtInitialize);
            Assert.True(File.Exists(Path.Combine(ContentLayout.ScenarioRecipes(_nas), "demo-city", ContentSeed.MarkerFileName)));
        }

        /// <summary>The opt-out is real: a host that sets <c>SeedContent = false</c> leaves the NAS untouched.</summary>
        [Fact]
        public void ASeedContentFalseHost_LeavesTheNasUntouched()
        {
            var probe = new ProbeSubsystem(_nas);

            new SubsystemOrchestrator(new ISubsystem[] { probe }, new RunnerOptions { Headless = true, NasRoot = _nas, SeedContent = false }).Initialize();

            Assert.False(probe.SawSeededNasAtInitialize);
            Assert.False(Directory.Exists(_nas));
        }
    }
}
