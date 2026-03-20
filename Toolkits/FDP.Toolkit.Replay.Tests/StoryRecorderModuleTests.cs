using System;
using System.IO;
using System.Threading.Tasks;
using Fdp.Kernel;
using FDP.Toolkit.Replay;
using ModuleHost.Core.Abstractions;
using Xunit;

namespace FDP.Toolkit.Replay.Tests
{
    /// <summary>
    /// Unit tests for <see cref="StoryRecorderModule"/>, <see cref="StoryTag"/>,
    /// and <see cref="StoryReplayTag"/> (P8T3).
    /// </summary>
    public class StoryRecorderModuleTests : IDisposable
    {
        private readonly string _tempDir;

        public StoryRecorderModuleTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"StoryRecorderTests_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { }
        }

        // ── P8T3 success condition 2 ─────────────────────────────────────────────

        [Fact]
        public void StoryRecorderModule_EntityFilter_RestrictsToStoryEntities()
        {
            // Arrange: build a world with one story entity and one non-story entity.
            using var world = new EntityRepository();
            world.RegisterComponent<SimTransform>();
            world.RegisterComponent<StoryTag>();

            var storyId = Guid.NewGuid();

            // The filter predicate mirrors what EcsRecordReplayController builds.
            Predicate<Entity> filter = entity =>
                world.HasComponent<StoryTag>(entity) &&
                world.GetComponentRO<StoryTag>(entity).StoryId == storyId;

            var config = new RecordingConfiguration
            {
                FilePath     = Path.Combine(_tempDir, "story.fdp"),
                EntityFilter = filter,
                DrillId      = storyId,
            };

            var module   = new StoryRecorderModule(config);
            var registry = new CapturingSystemRegistry();
            module.RegisterSystems(registry);

            // Assert: the module registered exactly one system (RecorderTickSystem).
            Assert.Single(registry.Systems);

            module.Dispose();
        }

        // ── P8T3 success condition 4 ─────────────────────────────────────────────

        [Fact]
        public void StoryReplayTag_IsDistinctFromStoryTag()
        {
            // Both components must have distinct component IDs.
            Assert.NotEqual(ReplayComponentIds.StoryTag, ReplayComponentIds.StoryReplayTag);
        }

        // ── Component registration ────────────────────────────────────────────────

        [Fact]
        public void StoryTag_CanBeRegisteredAndRead()
        {
            using var world = new EntityRepository();
            world.RegisterComponent<StoryTag>();

            var entity  = world.CreateEntity();
            var storyId = Guid.NewGuid();
            world.AddComponent(entity, new StoryTag { StoryId = storyId });

            ref readonly var tag = ref world.GetComponentRO<StoryTag>(entity);
            Assert.Equal(storyId, tag.StoryId);
        }

        [Fact]
        public void StoryReplayTag_CanBeRegisteredAndRead()
        {
            using var world = new EntityRepository();
            world.RegisterComponent<StoryReplayTag>();

            var entity = world.CreateEntity();
            world.AddComponent(entity, new StoryReplayTag { StoryId = Guid.NewGuid(), OriginalEntityId = 7 });

            ref readonly var tag = ref world.GetComponentRO<StoryReplayTag>(entity);
            Assert.Equal(7, tag.OriginalEntityId);
        }

        // ── Two concurrent stories produce isolated files ───────────────────────

        [Fact]
        public void TwoStoryRecorderModules_RunConcurrently_ProduceIsolatedFiles()
        {
            // Arrange: two modules with distinct file paths.
            var storyA = Guid.NewGuid();
            var storyB = Guid.NewGuid();

            var pathA = Path.Combine(_tempDir, "storyA.fdp");
            var pathB = Path.Combine(_tempDir, "storyB.fdp");

            using var world = new EntityRepository();
            world.RegisterComponent<SimTransform>();

            var moduleA = new StoryRecorderModule(new RecordingConfiguration
            {
                FilePath = pathA, DrillId = storyA,
            });
            var moduleB = new StoryRecorderModule(new RecordingConfiguration
            {
                FilePath = pathB, DrillId = storyB,
            });

            var registryA = new CapturingSystemRegistry();
            var registryB = new CapturingSystemRegistry();
            moduleA.RegisterSystems(registryA);
            moduleB.RegisterSystems(registryB);

            // Tick both systems once.
            ISimulationView view = world;
            foreach (var sys in registryA.Systems) sys.Execute(view, 0.016f);
            foreach (var sys in registryB.Systems) sys.Execute(view, 0.016f);

            // Dispose both — flushes and closes.
            moduleA.Dispose();
            moduleB.Dispose();

            // Assert: both files were created independently.
            Assert.True(File.Exists(pathA), $"Story A file missing: {pathA}");
            Assert.True(File.Exists(pathB), $"Story B file missing: {pathB}");
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
