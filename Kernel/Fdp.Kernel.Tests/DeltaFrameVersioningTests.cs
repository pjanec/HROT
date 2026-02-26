using System;
using System.IO;
using Xunit;
using Fdp.Kernel;
using Fdp.Kernel.FlightRecorder;

namespace Fdp.Tests
{
    /// <summary>
    /// Tests to verify that delta frame recording and version tracking work correctly.
    /// This addresses the critical bug where modifications after Tick() were not captured in deltas.
    /// </summary>
    public class DeltaFrameVersioningTests : IDisposable
    {
        private readonly string _testFilePath;
        
        public DeltaFrameVersioningTests()
        {
            _testFilePath = Path.Combine(Path.GetTempPath(), $"delta_versioning_test_{Guid.NewGuid()}.fdp");
        }
        
        public void Dispose()
        {
            try { File.Delete(_testFilePath); } catch {}
        }

        [Fact]
        public void DeltaFrameRecording_CorrectTickOrder_CapturesChanges()
        {
            // This test validates the correct order: Tick() -> Modify -> Record
            using var repo = new EntityRepository();
            repo.RegisterComponent<IntComponent>();
            
            var e = repo.CreateEntity();
            repo.AddComponent(e, new IntComponent { Value = 0 });
            
            using (var recorder = new AsyncRecorder(_testFilePath))
            {
                // Frame 0: Keyframe
                repo.Tick(); // V=2
                repo.SetUnmanagedComponent(e, new IntComponent { Value = 100 });
                recorder.CaptureKeyframe(repo, blocking: true);
                uint prevTick = repo.GlobalVersion;
                
                // Frame 1: Delta (CORRECT ORDER)
                repo.Tick(); // V=3
                repo.SetUnmanagedComponent(e, new IntComponent { Value = 200 }); // Modified at V=3
                recorder.CaptureFrame(repo, prevTick, blocking: true); // Record against V=2
                prevTick = repo.GlobalVersion;
                
                // Frame 2: Delta
                repo.Tick(); // V=4
                repo.SetUnmanagedComponent(e, new IntComponent { Value = 300 }); // Modified at V=4
                recorder.CaptureFrame(repo, prevTick, blocking: true); // Record against V=3
            }
            
            // Verify: Playback all frames
            using var targetRepo = new EntityRepository();
            targetRepo.RegisterComponent<IntComponent>();
            
            using var reader = new RecordingReader(_testFilePath);
            
            Assert.True(reader.ReadNextFrame(targetRepo)); // Frame 0
            Assert.Equal(100, targetRepo.GetComponentRO<IntComponent>(e).Value);
            
            Assert.True(reader.ReadNextFrame(targetRepo)); // Frame 1
            Assert.Equal(200, targetRepo.GetComponentRO<IntComponent>(e).Value);
            
            Assert.True(reader.ReadNextFrame(targetRepo)); // Frame 2
            Assert.Equal(300, targetRepo.GetComponentRO<IntComponent>(e).Value);
        }

        [Fact]
        public void DeltaFrameRecording_IncorrectTickOrder_FailsToCaptureChanges()
        {
            // This test documents the WRONG order and shows it fails
            // ORDER: Modify -> Tick() -> Record (WRONG!)
            using var repo = new EntityRepository();
            repo.RegisterComponent<IntComponent>();
            
            var e = repo.CreateEntity();
            repo.AddComponent(e, new IntComponent { Value = 0 });
            
            using (var recorder = new AsyncRecorder(_testFilePath))
            {
                // Frame 0: Keyframe
                repo.Tick(); // V=2
                repo.SetUnmanagedComponent(e, new IntComponent { Value = 100 });
                recorder.CaptureKeyframe(repo, blocking: true);
                
                // Frame 1: Delta (WRONG ORDER - will fail)
                uint prevTick = repo.GlobalVersion; // V=2
                repo.SetUnmanagedComponent(e, new IntComponent { Value = 200 }); // Modified at V=2
                repo.Tick(); // V=3
                // Now currentVersion=3, modification wasAt V=2, prevTick=2
                // Check: 2 > 2? FALSE - change not captured!
                recorder.CaptureFrame(repo, prevTick, blocking: true);
            }
            
            // Verify: The delta frame will be empty (no data captured)
            using var targetRepo = new EntityRepository();
            targetRepo.RegisterComponent<IntComponent>();
            
            using var reader = new RecordingReader(_testFilePath);
            
            Assert.True(reader.ReadNextFrame(targetRepo)); // Frame 0
            Assert.Equal(100, targetRepo.GetComponentRO<IntComponent>(e).Value);
            
            Assert.True(reader.ReadNextFrame(targetRepo)); // Frame 1 (delta is empty!)
            // Value should still be 100 because delta didn't capture the change to 200
            Assert.Equal(100, targetRepo.GetComponentRO<IntComponent>(e).Value);
        }

        [Fact]
        public void DeltaFrameSize_ContainsComponentData_NotJustHeader()
        {
            // Verify that delta frames actually contain component data
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            
            var e = repo.CreateEntity();
            repo.AddComponent(e, new Position { X = 0, Y = 0, Z = 0 });
            
            using (var recorder = new AsyncRecorder(_testFilePath))
            {
                // Keyframe
                repo.Tick();
                recorder.CaptureKeyframe(repo, blocking: true);
                uint prevTick = repo.GlobalVersion;
                
                // Delta with change
                repo.Tick();
                repo.SetUnmanagedComponent(e, new Position { X = 99, Y = 88, Z = 77 });
                recorder.CaptureFrame(repo, prevTick, blocking: true);
            }
            
            // Analyze file to check delta frame size
            using var controller = new PlaybackController(_testFilePath);
            Assert.Equal(2, controller.TotalFrames);
            
            var keyframeMetadata = controller.GetFrameMetadata(0);
            var deltaMetadata = controller.GetFrameMetadata(1);
            
            // Delta should be MORE than just header (17 bytes)
            // Header: Tick(8) + Type(1) + DestroyCount(4) + ChunkCount(4) = 17 bytes
            // With data: Should include chunk data (at least 64KB of component data)
            
            Assert.True(deltaMetadata.CompressedSize > 100, 
                $"Delta frame size {deltaMetadata.CompressedSize} is too small - likely contains no component data");
                
            // Verify the delta actually applied the change
            using var targetRepo = new EntityRepository();
            targetRepo.RegisterComponent<Position>();
            
            using var reader = new RecordingReader(_testFilePath);
            reader.ReadNextFrame(targetRepo); // Keyframe
            reader.ReadNextFrame(targetRepo); // Delta
            
            ref readonly var pos = ref targetRepo.GetComponentRO<Position>(e);
            Assert.Equal(99f, pos.X);
            Assert.Equal(88f, pos.Y);
            Assert.Equal(77f, pos.Z);
        }

        [Fact]
        public void VersionTracking_ComponentModification_UpdatesChunkVersion()
        {
            // Verify that modifying a component updates its chunk version correctly
            using var repo = new EntityRepository();
            repo.RegisterComponent<IntComponent>();
            
            var e = repo.CreateEntity();
            repo.AddComponent(e, new IntComponent { Value = 42 });
            
            repo.Tick(); // V=2
            uint tickBeforeModification = repo.GlobalVersion;
            
            // Get the component table to check version
            var table = repo.GetComponentTable<IntComponent>();
            uint versionBefore = table.GetVersionForEntity(e.Index);
            
            // Modify the component
            repo.SetUnmanagedComponent(e, new IntComponent { Value = 100 });
            
            uint versionAfter = table.GetVersionForEntity(e.Index);
            
            // Version should have been updated
            Assert.True(versionAfter > versionBefore, 
                $"Component version should increase after modification. Before: {versionBefore}, After: {versionAfter}");
            Assert.Equal(tickBeforeModification, versionAfter);
        }

        [ComponentId(214)]
        public struct Position
        {
            public float X, Y, Z;
        }
    }
}
