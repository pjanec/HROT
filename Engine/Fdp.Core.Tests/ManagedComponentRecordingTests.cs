using System;
using System.IO;
using Xunit;
using Fdp.Kernel;
using Fdp.Kernel.FlightRecorder;
using MessagePack;

namespace Fdp.Tests
{
    public class ManagedComponentRecordingTests : IDisposable
    {
        private readonly string _testFilePath;
        
        public ManagedComponentRecordingTests()
        {
            _testFilePath = Path.Combine(Path.GetTempPath(), $"managed_rec_{Guid.NewGuid()}.fdp");
        }
        
        public void Dispose()
        {
            try { File.Delete(_testFilePath); } catch {}
        }

        // Test class for managed component
        [MessagePackObject]
        [ComponentId(229)]
        public record SquadName
        {
            [Key(0)]
            public string? Name { get; set; }
        }

        [Fact]
        public void RecordAndReplay_ManagedComponent_ShouldRestore()
        {
            // Arrange
            using var repo = new EntityRepository();
            repo.RegisterComponent<SquadName>();
            
            var e = repo.CreateEntity();
            
            // Act
            using (var recorder = new AsyncRecorder(_testFilePath))
            {
                // Tick first
                repo.Tick();
                
                // Then add component
                repo.AddManagedComponent(e, new SquadName { Name = "Alpha" });
                
                recorder.CaptureKeyframe(repo, DateTime.UtcNow.Ticks, blocking: true);
            }
            
            // Replay
            using var replayRepo = new EntityRepository();
            replayRepo.RegisterComponent<SquadName>();
            
            using (var reader = new RecordingReader(_testFilePath))
            {
                reader.ReadNextFrame(replayRepo);
            }
            
            // Assert
            Assert.True(replayRepo.HasManagedComponent<SquadName>(e), "Managed component should be restored");
            var comp = replayRepo.GetComponentRO<SquadName>(e);
            Assert.NotNull(comp);
            Assert.Equal("Alpha", comp.Name);
        }
    }
}
