using System;
using System.IO;
using Xunit;
using Fdp.Kernel;
using Fdp.Kernel.FlightRecorder;

namespace Fdp.Tests
{
    public class PlaybackSystemTests
    {
        [Fact]
        public void ApplyFrame_RestoresHeaderAndActiveCount()
        {
            // Arrange - Record data using the actual RecorderSystem
            using var sourceRepo = new EntityRepository();
            
            // Advance tick to 10 to simulate some history and verify tick restoration
            for(int i=0; i<10; i++) sourceRepo.Tick();
            
            var e0 = sourceRepo.CreateEntity();
            
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            
            var recorder = new RecorderSystem();
            recorder.RecordKeyframe(sourceRepo, writer, 0L);

            // Act - Replay into a fresh repository
            using var destRepo = new EntityRepository();
            var playback = new PlaybackSystem();
            
            ms.Position = 0;
            using var reader = new BinaryReader(ms);
            playback.ApplyFrame(destRepo, reader);
            
            // Assert
            Assert.Equal(1, destRepo.EntityCount);
            // Note: e0.Generation comes from sourceRepo.
            Assert.True(destRepo.IsAlive(new Entity(0, e0.Generation)));
            
            // Verify GlobalVersion was restored
            Assert.Equal(sourceRepo.GlobalVersion, destRepo.GlobalVersion);
        }

        [Fact]
        public void ApplyFrame_RestoresComponentData()
        {
             // Arrange
            using var sourceRepo = new EntityRepository();
            sourceRepo.RegisterComponent<IntComponent>();
            
            // Advance tick to 20
            for(int i=0; i<20; i++) sourceRepo.Tick();

            var e0 = sourceRepo.CreateEntity();
            sourceRepo.AddComponent(e0, new IntComponent { Value = 999 });
            
            // Record
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            
            var recorder = new RecorderSystem();
            recorder.RecordKeyframe(sourceRepo, writer, 0L);
            
            // Act
            using var destRepo = new EntityRepository();
            destRepo.RegisterComponent<IntComponent>(); // Must register same components
            
            var playback = new PlaybackSystem();
            
            ms.Position = 0;
            using var reader = new BinaryReader(ms);
            playback.ApplyFrame(destRepo, reader);
            
            // Assert
            Assert.Equal(sourceRepo.GlobalVersion, destRepo.GlobalVersion);

            var entity = new Entity(0, e0.Generation);
            Assert.True(destRepo.IsAlive(entity));
            Assert.True(destRepo.HasComponent<IntComponent>(entity));
            
            ref IntComponent val = ref destRepo.GetComponentRW<IntComponent>(entity);
            Assert.Equal(999, val.Value);
        }
    }
}
