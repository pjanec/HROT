using System;
using System.IO;
using Xunit;
using Fdp.Core;
using Fdp.Core.FlightRecorder;

namespace Fdp.Tests
{
    /// <summary>
    /// Debug tests to investigate PlaybackController behavior and frame recording/playback logic.
    /// These tests provide detailed insight into the tick-to-frame mapping and delta application process.
    /// </summary>
    public class PlaybackDebugTests : IDisposable
    {
        private readonly string _testFilePath;
        
        public PlaybackDebugTests()
        {
            _testFilePath = Path.Combine(Path.GetTempPath(), $"debug_playback_{Guid.NewGuid()}.fdp");
        }
        
        public void Dispose()
        {
            if (File.Exists(_testFilePath))
            {
                File.Delete(_testFilePath);
            }
        }
        
        /// <summary>
        /// Comprehensive test that shows the complete recording and playback pipeline.
        /// This test documents the expected behavior and helps identify issues.
        /// </summary>
        [Fact]
        public void DebugPlayback_ShowsCompleteRecordingAndPlaybackPipeline()
        {
            // Arrange: Create test recording with detailed logging
            var recordingLog = CreateTestRecordingWithLogging(frameCount: 20, keyframeInterval: 5);
            
            // Act: Analyze the recorded data
            using var controller = new PlaybackController(_testFilePath);
            var analysisResults = AnalyzeRecordedData(controller);
            
            // Assert: Verify basic structure
            Assert.Equal(20, controller.TotalFrames);
            Assert.True(analysisResults.KeyframeCount > 0);
            Assert.True(analysisResults.DeltaCount > 0);
            
            // Act: Test seeking behavior
            var seekResults = TestSeekingBehavior(controller);
            
            // Document findings (this will show up in test output)
            var report = GenerateDebugReport(recordingLog, analysisResults, seekResults);
            
            // This assertion will show the full report in test output when it fails
            // Comment out this line to see the report, or use xUnit output helper in real scenarios
            // Assert.True(false, report);
        }
        
        /// <summary>
        /// Specific test to investigate the delta frame application issue.
        /// Expected: SeekToTick(10) should result in position X=8
        /// Actual: SeekToTick(10) results in position X=5 (only keyframe applied)
        /// </summary>
        [Fact]
        public void DebugDeltaApplication_InvestigatesDeltaFrameIssue()
        {
            // Arrange
            CreateTestRecordingWithLogging(frameCount: 20, keyframeInterval: 5);
            
            using var controller = new PlaybackController(_testFilePath);
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            
            // Act: Test the problematic scenario
            controller.SeekToTick(repo, 10);
            
            // Collect results
            var currentFrame = controller.CurrentFrame;
            var position = GetEntityPosition(repo);
            
            // Expected behavior based on our debug analysis:
            // - Tick 10 should map to Frame 8
            // - Frame 8 should contain position X=8, Y=16, Z=24
            // - SeekToTick should apply keyframe 5 (X=5) + deltas 6,7,8 to reach X=8
            
            Assert.Equal(8, currentFrame); // This should pass
            
            // This currently fails - getting X=5 instead of X=8
            // The issue is that deltas 6, 7, 8 are not being applied
            Assert.Equal(8f, position.X); // Currently fails
            Assert.Equal(16f, position.Y);
            Assert.Equal(24f, position.Z);
        }
        
        private RecordingLog CreateTestRecordingWithLogging(int frameCount, int keyframeInterval)
        {
            var log = new RecordingLog();
            
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            
            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new Position { X = 0, Y = 0, Z = 0 });
            
            using var recorder = new AsyncRecorder(_testFilePath);
            uint prevTick = 0; // Initialize for delta recording
            
            for (int frame = 0; frame < frameCount; frame++)
            {
                // CRITICAL: Advance tick FIRST so modifications happen at the new version
                repo.Tick();
                uint currentTick = repo.GlobalVersion;
                
                // NOW modify position - will be tagged with currentTick
                ref var pos = ref repo.GetComponentRW<Position>(entity);
                pos.X = frame;
                pos.Y = frame * 2;
                pos.Z = frame * 3;
                
                var frameInfo = new FrameInfo
                {
                    FrameIndex = frame,
                    GlobalVersion = currentTick,
                    Position = new Position { X = pos.X, Y = pos.Y, Z = pos.Z },
                    PrevTick = prevTick
                };
                
                if (frame % keyframeInterval == 0)
                {
                    recorder.CaptureKeyframe(repo, DateTime.UtcNow.Ticks, blocking: true);
                    frameInfo.IsKeyframe = true;
                }
                else
                {
                    // Record delta against previous tick
                    // Version check: currentTick > prevTick will succeed
                    recorder.CaptureFrame(repo, prevTick, DateTime.UtcNow.Ticks, blocking: true);
                    frameInfo.IsKeyframe = false;
                }
                
                log.Frames.Add(frameInfo);
                prevTick = currentTick;
            }
            
            return log;
        }
        
        private AnalysisResults AnalyzeRecordedData(PlaybackController controller)
        {
            var results = new AnalysisResults();
            
            for (int i = 0; i < controller.TotalFrames; i++)
            {
                var metadata = controller.GetFrameMetadata(i);
                
                var frameAnalysis = new RecordedFrameAnalysis
                {
                    Index = i,
                    Tick = metadata.Tick,
                    FrameType = metadata.FrameType,
                    Size = metadata.CompressedSize
                };
                
                if (metadata.FrameType == FrameType.Keyframe)
                    results.KeyframeCount++;
                else
                    results.DeltaCount++;
                
                results.Frames.Add(frameAnalysis);
            }
            
            return results;
        }
        
        private SeekResults TestSeekingBehavior(PlaybackController controller)
        {
            var results = new SeekResults();
            
            // Test seeking to tick 10 (should be frame 8)
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            
            controller.SeekToTick(repo, 10);
            
            results.TargetTick = 10;
            results.ResultingFrame = controller.CurrentFrame;
            results.ResultingPosition = GetEntityPosition(repo);
            
            return results;
        }
        
        private Position GetEntityPosition(EntityRepository repo)
        {
            var result = new Position { X = -999, Y = -999, Z = -999 };
            
            var query = repo.Query().With<Position>().Build();
            foreach (var e in query)
            {
                ref readonly var pos = ref repo.GetComponentRO<Position>(e);
                result = pos;
            }
            
            return result;
        }
        
        private string GenerateDebugReport(RecordingLog recordingLog, AnalysisResults analysisResults, SeekResults seekResults)
        {
            var report = new System.Text.StringBuilder();
            report.AppendLine("=== PLAYBACK DEBUG REPORT ===");
            report.AppendLine();
            
            report.AppendLine("RECORDING PHASE:");
            foreach (var frame in recordingLog.Frames)
            {
                report.AppendLine($"  Frame {frame.FrameIndex}: GlobalVersion={frame.GlobalVersion}, " +
                    $"Position=({frame.Position.X}, {frame.Position.Y}, {frame.Position.Z}), " +
                    $"Type={frame.IsKeyframe}, PrevTick={frame.PrevTick}");
            }
            report.AppendLine();
            
            report.AppendLine("RECORDED DATA ANALYSIS:");
            report.AppendLine($"  Total frames: {analysisResults.Frames.Count}");
            report.AppendLine($"  Keyframes: {analysisResults.KeyframeCount}");
            report.AppendLine($"  Deltas: {analysisResults.DeltaCount}");
            report.AppendLine("  Frame details:");
            foreach (var frame in analysisResults.Frames)
            {
                report.AppendLine($"    Frame {frame.Index}: Tick {frame.Tick}, " +
                    $"Type {frame.FrameType}, Size {frame.Size} bytes");
            }
            report.AppendLine();
            
            report.AppendLine("SEEK TEST RESULTS:");
            report.AppendLine($"  Target: Seek to tick {seekResults.TargetTick}");
            report.AppendLine($"  Result: Frame {seekResults.ResultingFrame}");
            report.AppendLine($"  Position: ({seekResults.ResultingPosition.X}, " +
                $"{seekResults.ResultingPosition.Y}, {seekResults.ResultingPosition.Z})");
            report.AppendLine();
            
            report.AppendLine("ANALYSIS:");
            report.AppendLine($"  Expected frame for tick {seekResults.TargetTick}: 8");
            report.AppendLine($"  Actual frame: {seekResults.ResultingFrame}");
            report.AppendLine($"  Expected position X: 8 (from frame 8)");
            report.AppendLine($"  Actual position X: {seekResults.ResultingPosition.X}");
            
            if (seekResults.ResultingPosition.X == 5f)
            {
                report.AppendLine("  >> ISSUE DETECTED: Only keyframe (frame 5) was applied!");
                report.AppendLine("  >> Deltas (frames 6, 7, 8) were not applied during seek.");
            }
            
            return report.ToString();
        }
    }
    
    // Helper classes for debug data
    public class RecordingLog
    {
        public List<FrameInfo> Frames { get; } = new List<FrameInfo>();
    }
    
    public class FrameInfo
    {
        public int FrameIndex { get; set; }
        public uint GlobalVersion { get; set; }
        public Position Position { get; set; }
        public uint PrevTick { get; set; }
        public bool IsKeyframe { get; set; }
    }
    
    public class AnalysisResults
    {
        public List<RecordedFrameAnalysis> Frames { get; } = new List<RecordedFrameAnalysis>();
        public int KeyframeCount { get; set; }
        public int DeltaCount { get; set; }
    }
    
    public class RecordedFrameAnalysis
    {
        public int Index { get; set; }
        public ulong Tick { get; set; }
        public FrameType FrameType { get; set; }
        public int Size { get; set; }
    }
    
    public class SeekResults
    {
        public ulong TargetTick { get; set; }
        public int ResultingFrame { get; set; }
        public Position ResultingPosition { get; set; }
    }
}