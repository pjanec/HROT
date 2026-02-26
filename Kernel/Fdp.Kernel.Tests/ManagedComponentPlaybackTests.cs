using System;
using System.IO;
using Xunit;
using Fdp.Kernel;
using Fdp.Kernel.FlightRecorder;
using MessagePack;

namespace Fdp.Tests
{
    /// <summary>
    /// Comprehensive tests for managed component recording and playback.
    /// Validates that managed components are properly captured and restored through
    /// keyframes, deltas, seeking, and other playback operations.
    /// </summary>
    public class ManagedComponentPlaybackTests : IDisposable
    {
        private readonly string _testFilePath;
        
        public ManagedComponentPlaybackTests()
        {
            _testFilePath = Path.Combine(Path.GetTempPath(), $"managed_playback_{Guid.NewGuid()}.fdp");
        }
        
        public void Dispose()
        {
            try { File.Delete(_testFilePath); } catch {}
        }

        // Test managed components
        [MessagePackObject]
        [ComponentId(227)]
        public record PlayerInfo
        {
            [Key(0)]
            public string Name { get; set; } = string.Empty;
            
            [Key(1)]
            public int Score { get; set; }
            
            [Key(2)]
            public bool IsActive { get; set; }
        }

        [MessagePackObject]
        [ComponentId(228)]
        public record InventoryData
        {
            [Key(0)]
            public string[] Items { get; set; } = Array.Empty<string>();
            
            [Key(1)]
            public int Gold { get; set; }
        }

        [Fact]
        public void ManagedComponent_Keyframe_RestoresCorrectly()
        {
            // Arrange
            using var repo = new EntityRepository();
            repo.RegisterComponent<PlayerInfo>();
            
            var e = repo.CreateEntity();
            repo.AddManagedComponent(e, new PlayerInfo 
            { 
                Name = "TestPlayer", 
                Score = 1000, 
                IsActive = true 
            });
            
            // Act - Record keyframe
            using (var recorder = new AsyncRecorder(_testFilePath))
            {
                repo.Tick();
                recorder.CaptureKeyframe(repo, blocking: true);
            }
            
            // Playback
            using var targetRepo = new EntityRepository();
            targetRepo.RegisterComponent<PlayerInfo>();
            
            using var reader = new RecordingReader(_testFilePath);
            Assert.True(reader.ReadNextFrame(targetRepo));
            
            // Assert
            Assert.True(targetRepo.HasManagedComponent<PlayerInfo>(e));
            var player = targetRepo.GetComponentRO<PlayerInfo>(e);
            Assert.Equal("TestPlayer", player.Name);
            Assert.Equal(1000, player.Score);
            Assert.True(player.IsActive);
        }

        [Fact]
        public void ManagedComponent_DeltaFrame_CapturesChanges()
        {
            // Test that managed component changes are captured in delta frames
            using var repo = new EntityRepository();
            repo.RegisterComponent<PlayerInfo>();
            
            var e = repo.CreateEntity();
            repo.AddManagedComponent(e, new PlayerInfo 
            { 
                Name = "Player1", 
                Score = 100, 
                IsActive = true 
            });
            
            using (var recorder = new AsyncRecorder(_testFilePath))
            {
                // Keyframe
                repo.Tick();
                recorder.CaptureKeyframe(repo, blocking: true);
                uint prevTick = repo.GlobalVersion;
                
                // Modify managed component
                repo.Tick();
                var player = repo.GetComponentRW<PlayerInfo>(e);
                player.Name = "Player1_Modified";
                player.Score = 500;
                player.IsActive = false;
                
                // Record delta
                recorder.CaptureFrame(repo, prevTick, blocking: true);
            }
            
            // Playback
            using var targetRepo = new EntityRepository();
            targetRepo.RegisterComponent<PlayerInfo>();
            
            using var reader = new RecordingReader(_testFilePath);
            
            // Frame 0: Initial state
            Assert.True(reader.ReadNextFrame(targetRepo));
            var player1 = targetRepo.GetComponentRO<PlayerInfo>(e);
            Assert.Equal("Player1", player1.Name);
            Assert.Equal(100, player1.Score);
            
            // Frame 1: Modified state (delta)
            Assert.True(reader.ReadNextFrame(targetRepo));
            var player2 = targetRepo.GetComponentRO<PlayerInfo>(e);
            Assert.Equal("Player1_Modified", player2.Name);
            Assert.Equal(500, player2.Score);
            Assert.False(player2.IsActive);
        }

        [Fact]
        public void ManagedComponent_ComplexData_PreservesArrays()
        {
            // Test managed components with complex data (arrays)
            using var repo = new EntityRepository();
            repo.RegisterComponent<InventoryData>();
            
            var e = repo.CreateEntity();
            repo.AddManagedComponent(e, new InventoryData
            {
                Items = new[] { "Sword", "Shield", "Potion" },
                Gold = 250
            });
            
            using (var recorder = new AsyncRecorder(_testFilePath))
            {
                repo.Tick();
                recorder.CaptureKeyframe(repo, blocking: true);
            }
            
            // Playback
            using var targetRepo = new EntityRepository();
            targetRepo.RegisterComponent<InventoryData>();
            
            using var reader = new RecordingReader(_testFilePath);
            reader.ReadNextFrame(targetRepo);
            
            // Assert
            var inventory = targetRepo.GetComponentRO<InventoryData>(e);
            Assert.NotNull(inventory.Items);
            Assert.Equal(3, inventory.Items.Length);
            Assert.Equal("Sword", inventory.Items[0]);
            Assert.Equal("Shield", inventory.Items[1]);
            Assert.Equal("Potion", inventory.Items[2]);
            Assert.Equal(250, inventory.Gold);
        }

        [Fact]
        public void MixedComponents_UnmanagedAndManaged_BothRestore()
        {
            // Test that both unmanaged and managed components restore correctly
            using var repo = new EntityRepository();
            repo.RegisterComponent<IntComponent>();
            repo.RegisterComponent<PlayerInfo>();
            
            var e = repo.CreateEntity();
            repo.AddComponent(e, new IntComponent { Value = 42 });
            repo.AddManagedComponent(e, new PlayerInfo 
            { 
                Name = "MixedTest", 
                Score = 999 
            });
            
            using (var recorder = new AsyncRecorder(_testFilePath))
            {
                repo.Tick();
                recorder.CaptureKeyframe(repo, blocking: true);
            }
            
            // Playback
            using var targetRepo = new EntityRepository();
            targetRepo.RegisterComponent<IntComponent>();
            targetRepo.RegisterComponent<PlayerInfo>();
            
            using var reader = new RecordingReader(_testFilePath);
            reader.ReadNextFrame(targetRepo);
            
            // Assert both components
            Assert.True(targetRepo.HasUnmanagedComponent<IntComponent>(e));
            Assert.Equal(42, targetRepo.GetComponentRO<IntComponent>(e).Value);
            
            Assert.True(targetRepo.HasManagedComponent<PlayerInfo>(e));
            var player = targetRepo.GetComponentRO<PlayerInfo>(e);
            Assert.Equal("MixedTest", player.Name);
            Assert.Equal(999, player.Score);
        }

        [Fact]
        public void ManagedComponent_SeekBetweenFrames_RestoresCorrectState()
        {
            // Test seeking with managed components
            using var repo = new EntityRepository();
            repo.RegisterComponent<PlayerInfo>();
            
            var e = repo.CreateEntity();
            repo.AddManagedComponent(e, new PlayerInfo { Name = "Start", Score = 0 });
            
            using (var recorder = new AsyncRecorder(_testFilePath))
            {
                for (int frame = 0; frame < 10; frame++)
                {
                    repo.Tick();
                    var player = repo.GetComponentRW<PlayerInfo>(e);
                    player.Name = $"Frame{frame}";
                    player.Score = frame * 10;
                    
                    if (frame % 5 == 0)
                        recorder.CaptureKeyframe(repo, blocking: true);
                    else
                        recorder.CaptureFrame(repo, repo.GlobalVersion - 1, blocking: true);
                }
            }
            
            // Playback with seeking
            using var controller = new PlaybackController(_testFilePath);
            using var targetRepo = new EntityRepository();
            targetRepo.RegisterComponent<PlayerInfo>();
            
            // Seek to frame 3
            controller.SeekToFrame(targetRepo, 3);
            var player3 = targetRepo.GetComponentRO<PlayerInfo>(e);
            Assert.Equal("Frame3", player3.Name);
            Assert.Equal(30, player3.Score);
            
            // Seek to frame 7
            controller.SeekToFrame(targetRepo, 7);
            var player7 = targetRepo.GetComponentRO<PlayerInfo>(e);
            Assert.Equal("Frame7", player7.Name);
            Assert.Equal(70, player7.Score);
            
            // Seek back to frame 1
            controller.SeekToFrame(targetRepo, 1);
            var player1 = targetRepo.GetComponentRO<PlayerInfo>(e);
            Assert.Equal("Frame1", player1.Name);
            Assert.Equal(10, player1.Score);
        }

        [Fact]
        public void ManagedComponent_MultipleEntities_AllRestore()
        {
            // Test multiple entities with managed components
            using var repo = new EntityRepository();
            repo.RegisterComponent<PlayerInfo>();
            
            var e1 = repo.CreateEntity();
            var e2 = repo.CreateEntity();
            var e3 = repo.CreateEntity();
            
            repo.AddManagedComponent(e1, new PlayerInfo { Name = "Player1", Score = 100 });
            repo.AddManagedComponent(e2, new PlayerInfo { Name = "Player2", Score = 200 });
            repo.AddManagedComponent(e3, new PlayerInfo { Name = "Player3", Score = 300 });
            
            using (var recorder = new AsyncRecorder(_testFilePath))
            {
                repo.Tick();
                recorder.CaptureKeyframe(repo, blocking: true);
            }
            
            // Playback
            using var targetRepo = new EntityRepository();
            targetRepo.RegisterComponent<PlayerInfo>();
            
            using var reader = new RecordingReader(_testFilePath);
            reader.ReadNextFrame(targetRepo);
            
            // Assert all three entities
            var p1 = targetRepo.GetComponentRO<PlayerInfo>(e1);
            var p2 = targetRepo.GetComponentRO<PlayerInfo>(e2);
            var p3 = targetRepo.GetComponentRO<PlayerInfo>(e3);
            
            Assert.Equal("Player1", p1.Name);
            Assert.Equal(100, p1.Score);
            Assert.Equal("Player2", p2.Name);
            Assert.Equal(200, p2.Score);
            Assert.Equal("Player3", p3.Name);
            Assert.Equal(300, p3.Score);
        }

        [Fact]
        public void ManagedComponent_AddedAndRemoved_TrackedCorrectly()
        {
            // Test adding and removing managed components across frames
            using var repo = new EntityRepository();
            repo.RegisterComponent<PlayerInfo>();
            
            var e = repo.CreateEntity();
            
            using (var recorder = new AsyncRecorder(_testFilePath))
            {
                // Frame 0: No component
                repo.Tick();
                recorder.CaptureKeyframe(repo, blocking: true);
                uint prevTick = repo.GlobalVersion;
                
                // Frame 1: Add component
                repo.Tick();
                repo.AddManagedComponent(e, new PlayerInfo { Name = "Added", Score = 50 });
                recorder.CaptureFrame(repo, prevTick, blocking: true);
                prevTick = repo.GlobalVersion;
                
                // Frame 2: Modify component
                repo.Tick();
                var player = repo.GetComponentRW<PlayerInfo>(e);
                player.Score = 100;
                recorder.CaptureFrame(repo, prevTick, blocking: true);
                prevTick = repo.GlobalVersion;
                
                // Frame 3: Remove component
                repo.Tick();
                repo.RemoveManagedComponent<PlayerInfo>(e);
                recorder.CaptureFrame(repo, prevTick, blocking: true);
            }
            
            // Playback
            using var targetRepo = new EntityRepository();
            targetRepo.RegisterComponent<PlayerInfo>();
            
            using var reader = new RecordingReader(_testFilePath);
            
            // Frame 0: No component
            reader.ReadNextFrame(targetRepo);
            Assert.False(targetRepo.HasManagedComponent<PlayerInfo>(e));
            
            // Frame 1: Component added
            reader.ReadNextFrame(targetRepo);
            Assert.True(targetRepo.HasManagedComponent<PlayerInfo>(e));
            Assert.Equal("Added", targetRepo.GetComponentRO<PlayerInfo>(e).Name);
            Assert.Equal(50, targetRepo.GetComponentRO<PlayerInfo>(e).Score);
            
            // Frame 2: Component modified
            reader.ReadNextFrame(targetRepo);
            Assert.True(targetRepo.HasManagedComponent<PlayerInfo>(e));
            Assert.Equal(100, targetRepo.GetComponentRO<PlayerInfo>(e).Score);
            
            // Frame 3: Component removed
            reader.ReadNextFrame(targetRepo);
            Assert.False(targetRepo.HasManagedComponent<PlayerInfo>(e));
        }

        [Fact]
        public void ManagedComponent_NullHandling_WorksCorrectly()
        {
            // Test that null/empty values are handled correctly
            using var repo = new EntityRepository();
            repo.RegisterComponent<PlayerInfo>();
            
            var e = repo.CreateEntity();
            repo.AddManagedComponent(e, new PlayerInfo 
            { 
                Name = "", // Empty string
                Score = 0,
                IsActive = false
            });
            
            using (var recorder = new AsyncRecorder(_testFilePath))
            {
                repo.Tick();
                recorder.CaptureKeyframe(repo, blocking: true);
            }
            
            // Playback
            using var targetRepo = new EntityRepository();
            targetRepo.RegisterComponent<PlayerInfo>();
            
            using var reader = new RecordingReader(_testFilePath);
            reader.ReadNextFrame(targetRepo);
            
            // Assert
            var player = targetRepo.GetComponentRO<PlayerInfo>(e);
            Assert.NotNull(player.Name);
            Assert.Equal("", player.Name);
            Assert.Equal(0, player.Score);
            Assert.False(player.IsActive);
        }
    }
}
