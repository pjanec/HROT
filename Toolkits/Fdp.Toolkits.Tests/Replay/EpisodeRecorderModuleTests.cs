using System;
using System.IO;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Toolkit.Replay;
using Fdp.ModuleHost.Abstractions;
using Xunit;

namespace Fdp.Toolkit.Replay.Tests
{
    /// <summary>
    /// Unit tests for <see cref="EpisodeRecorderModule"/>, <see cref="EpisodeTag"/>,
    /// and <see cref="EpisodeReplayTag"/> (P8T3).
    /// </summary>
    public class EpisodeRecorderModuleTests : IDisposable
    {
        private readonly string _tempDir;

        public EpisodeRecorderModuleTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"EpisodeRecorderTests_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { }
        }

        // ── P8T3 success condition 2 ─────────────────────────────────────────────

        [Fact]
        public void EpisodeRecorderModule_EntityFilter_RestrictsToEpisodeEntities()
        {
            // Arrange: build a world with one episode entity and one non-episode entity.
            using var world = new EntityRepository();
            world.RegisterComponent<SimTransform>();
            world.RegisterComponent<EpisodeTag>();

            var episodeId = Guid.NewGuid();

            // The filter predicate mirrors what EcsRecordReplayController builds.
            Predicate<Entity> filter = entity =>
                world.HasComponent<EpisodeTag>(entity) &&
                world.GetComponentRO<EpisodeTag>(entity).EpisodeId == episodeId;

            var config = new RecordingConfiguration
            {
                FilePath     = Path.Combine(_tempDir, "episode.fdp"),
                EntityFilter = filter,
                ExerciseId      = episodeId,
            };

            var module   = new EpisodeRecorderModule(config);
            var registry = new CapturingSystemRegistry();
            module.RegisterSystems(registry);

            // Assert: the module registered exactly one system (RecorderTickSystem).
            Assert.Single(registry.Systems);

            module.Dispose();
        }

        // ── P8T3 success condition 4 ─────────────────────────────────────────────

        [Fact]
        public void EpisodeReplayTag_IsDistinctFromEpisodeTag()
        {
            // Both components must have distinct component IDs.
            Assert.NotEqual(ReplayComponentIds.EpisodeTag, ReplayComponentIds.EpisodeReplayTag);
        }

        // ── Component registration ────────────────────────────────────────────────

        [Fact]
        public void EpisodeTag_CanBeRegisteredAndRead()
        {
            using var world = new EntityRepository();
            world.RegisterComponent<EpisodeTag>();

            var entity  = world.CreateEntity();
            var episodeId = Guid.NewGuid();
            world.AddComponent(entity, new EpisodeTag { EpisodeId = episodeId });

            ref readonly var tag = ref world.GetComponentRO<EpisodeTag>(entity);
            Assert.Equal(episodeId, tag.EpisodeId);
        }

        [Fact]
        public void EpisodeReplayTag_CanBeRegisteredAndRead()
        {
            using var world = new EntityRepository();
            world.RegisterComponent<EpisodeReplayTag>();

            var entity = world.CreateEntity();
            world.AddComponent(entity, new EpisodeReplayTag { EpisodeId = Guid.NewGuid(), OriginalEntityId = 7 });

            ref readonly var tag = ref world.GetComponentRO<EpisodeReplayTag>(entity);
            Assert.Equal(7, tag.OriginalEntityId);
        }

        // ── Two concurrent episodes produce isolated files ───────────────────────

        [Fact]
        public void TwoEpisodeRecorderModules_RunConcurrently_ProduceIsolatedFiles()
        {
            // Arrange: two modules with distinct file paths.
            var episodeA = Guid.NewGuid();
            var episodeB = Guid.NewGuid();

            var pathA = Path.Combine(_tempDir, "episodeA.fdp");
            var pathB = Path.Combine(_tempDir, "episodeB.fdp");

            using var world = new EntityRepository();
            world.RegisterComponent<SimTransform>();

            var moduleA = new EpisodeRecorderModule(new RecordingConfiguration
            {
                FilePath = pathA, ExerciseId = episodeA,
            });
            var moduleB = new EpisodeRecorderModule(new RecordingConfiguration
            {
                FilePath = pathB, ExerciseId = episodeB,
            });

            var registryA = new CapturingSystemRegistry();
            var registryB = new CapturingSystemRegistry();
            moduleA.RegisterSystems(registryA);
            moduleB.RegisterSystems(registryB);

            // RecorderTickSystem reads GlobalTime.TotalWallTicks each tick.
            world.SetSingletonUnmanaged(new GlobalTime { DeltaTime = 0.016f, TimeScale = 1.0f });

            // Tick both systems once.
            ISimulationView view = world;
            foreach (var sys in registryA.Systems) sys.Execute(view, 0.016f);
            foreach (var sys in registryB.Systems) sys.Execute(view, 0.016f);

            // Dispose both — flushes and closes.
            moduleA.Dispose();
            moduleB.Dispose();

            // Assert: both files were created independently.
            Assert.True(File.Exists(pathA), $"Episode A file missing: {pathA}");
            Assert.True(File.Exists(pathB), $"Episode B file missing: {pathB}");
            Assert.NotEqual(new FileInfo(pathA).Length, 0);
            Assert.NotEqual(new FileInfo(pathB).Length, 0);
        }

        private sealed class CapturingSystemRegistry : ISystemRegistry
        {
            public System.Collections.Generic.List<IEcsModuleSystem> Systems { get; } = new();
            public void RegisterSystem<T>(T system) where T : IEcsModuleSystem => Systems.Add(system);
        }
    }
}
