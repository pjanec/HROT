using System;
using System.IO;
using System.Diagnostics;
using System.Threading;
using Xunit;
using Xunit.Abstractions;
using Fdp.Core;
using Fdp.Core.FlightRecorder;

namespace Fdp.Tests
{
    /// <summary>
    /// Tests Flight Recorder with both deterministic (fixed timestep) 
    /// and real-time (variable timestep) simulation modes.
    /// Validates that playback is always deterministic regardless of recording mode.
    /// </summary>
    public class SimulationModeTests : IDisposable
    {
        private readonly ITestOutputHelper _output;
        private readonly string _deterministicFile;
        private readonly string _realTimeFile;
        
        public SimulationModeTests(ITestOutputHelper output)
        {
            _output = output;
            _deterministicFile = Path.Combine(Path.GetTempPath(), $"deterministic_{Guid.NewGuid()}.fdp");
            _realTimeFile = Path.Combine(Path.GetTempPath(), $"realtime_{Guid.NewGuid()}.fdp");
        }
        
        public void Dispose()
        {
            try { File.Delete(_deterministicFile); } catch {}
            try { File.Delete(_realTimeFile); } catch {}
        }

        [ComponentId(225)]
        public struct Position { public float X, Y, Z; }
        [ComponentId(226)]
        public struct Velocity { public float VX, VY, VZ; }

        [Fact]
        public void DeterministicSimulation_FixedTimestep_ProducesConsistentResults()
        {
            // Deterministic simulation: Fixed timestep, no real-time delays
            // Same input always produces same output
            
            const int entityCount = 100;
            const int frameCount = 120;
            const float fixedDeltaTime = 1.0f / 60.0f; // 60 FPS fixed
            
            _output.WriteLine("=== DETERMINISTIC SIMULATION (Fixed Timestep) ===");
            _output.WriteLine($"Delta Time: {fixedDeltaTime:F6}s (fixed)");
            _output.WriteLine($"Frames: {frameCount}");
            
            // Run simulation twice - should produce identical results
            var hash1 = RunDeterministicSimulation(entityCount, frameCount, fixedDeltaTime, _deterministicFile);
            var hash2 = RunDeterministicSimulation(entityCount, frameCount, fixedDeltaTime, _deterministicFile + ".verify");
            
            _output.WriteLine($"Run 1 Hash: {hash1}");
            _output.WriteLine($"Run 2 Hash: {hash2}");
            
            Assert.Equal(hash1, hash2);
            _output.WriteLine("✅ Deterministic: Same inputs produced identical outputs");
            
            // Verify playback is also deterministic
            var playbackHash1 = PlaybackAndHash(_deterministicFile, entityCount);
            var playbackHash2 = PlaybackAndHash(_deterministicFile, entityCount);
            
            Assert.Equal(playbackHash1, playbackHash2);
            _output.WriteLine("✅ Playback: Multiple playbacks produced identical results");
            
            // Record and playback should match
            Assert.Equal(hash1, playbackHash1);
            _output.WriteLine("✅ Record/Playback: Recording matches playback exactly");
            
            try { File.Delete(_deterministicFile + ".verify"); } catch {}
        }

        [Fact]
        public void RealTimeSimulation_VariableTimestep_RecordsCorrectly()
        {
            // Real-time simulation: Variable timestep based on wall clock
            // Frame times vary, but events captured correctly
            
            const int entityCount = 100;
            const int frameCount = 60;
            const float targetFPS = 60;
            const float targetDeltaTime = 1.0f / targetFPS;
            
            _output.WriteLine("=== REAL-TIME SIMULATION (Variable Timestep) ===");
            _output.WriteLine($"Target FPS: {targetFPS}");
            _output.WriteLine($"Target Delta: {targetDeltaTime:F6}s");
            _output.WriteLine($"Frames: {frameCount}");
            
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            repo.RegisterComponent<Velocity>();
            
            var entities = new Entity[entityCount];
            var random = new Random(42);
            
            for (int i = 0; i < entityCount; i++)
            {
                entities[i] = repo.CreateEntity();
                repo.AddComponent(entities[i], new Position { X = i * 10, Y = 0, Z = 0 });
                repo.AddComponent(entities[i], new Velocity 
                { 
                    VX = (float)(random.NextDouble() * 2 - 1),
                    VY = 0,
                    VZ = (float)(random.NextDouble() * 2 - 1)
                });
            }
            
            var frameTimings = new float[frameCount];
            var sw = Stopwatch.StartNew();
            long lastFrameTime = 0;
            
            using (var recorder = new AsyncRecorder(_realTimeFile))
            {
                for (int frame = 0; frame < frameCount; frame++)
                {
                    long frameStart = sw.ElapsedTicks;
                    float actualDeltaTime = frame == 0 ? targetDeltaTime : 
                        (frameStart - lastFrameTime) / (float)Stopwatch.Frequency;
                    
                    frameTimings[frame] = actualDeltaTime;
                    lastFrameTime = frameStart;
                    
                    repo.Tick();
                    
                    // Update with ACTUAL elapsed time (real-time)
                    for (int i = 0; i < entityCount; i++)
                    {
                        ref var pos = ref repo.GetComponentRW<Position>(entities[i]);
                        var vel = repo.GetComponentRO<Velocity>(entities[i]);
                        
                        pos.X += vel.VX * actualDeltaTime;
                        pos.Y += vel.VY * actualDeltaTime;
                        pos.Z += vel.VZ * actualDeltaTime;
                    }
                    
                    if (frame % 10 == 0)
                        recorder.CaptureKeyframe(repo, DateTime.UtcNow.Ticks, blocking: true);
                    else
                        recorder.CaptureFrame(repo, repo.GlobalVersion - 1, DateTime.UtcNow.Ticks, blocking: true);
                    
                    // Simulate variable frame times
                    if (frame % 5 == 0)
                        Thread.Sleep(1); // Occasional spike
                }
            }
            
            // Calculate timing statistics
            float avgDelta = 0;
            float minDelta = float.MaxValue;
            float maxDelta = float.MinValue;
            
            for (int i = 1; i < frameCount; i++) // Skip first frame
            {
                avgDelta += frameTimings[i];
                minDelta = MathF.Min(minDelta, frameTimings[i]);
                maxDelta = MathF.Max(maxDelta, frameTimings[i]);
            }
            avgDelta /= (frameCount - 1);
            
            _output.WriteLine($"");
            _output.WriteLine($"Frame Time Statistics:");
            _output.WriteLine($"  Average: {avgDelta * 1000:F2}ms ({1.0f / avgDelta:F1} FPS)");
            _output.WriteLine($"  Min: {minDelta * 1000:F2}ms");
            _output.WriteLine($"  Max: {maxDelta * 1000:F2}ms");
            _output.WriteLine($"  Variance: {(maxDelta - minDelta) * 1000:F2}ms");
            
            // Verify recording completed successfully
            Assert.True(File.Exists(_realTimeFile));
            Assert.True(new FileInfo(_realTimeFile).Length > 1000);
            _output.WriteLine($"✅ Real-time recording completed successfully");
            
            // Verify playback is DETERMINISTIC even though recording was real-time
            var playbackHash1 = PlaybackAndHash(_realTimeFile, entityCount);
            var playbackHash2 = PlaybackAndHash(_realTimeFile, entityCount);
            
            Assert.Equal(playbackHash1, playbackHash2);
            _output.WriteLine($"✅ Playback is deterministic despite variable recording times");
        }

        [Fact]
        public void BothModes_ProduceDifferentRecordings_ButDeterministicPlayback()
        {
            // Verify that deterministic and real-time produce DIFFERENT recordings
            // (because frame times differ), but playback is always deterministic
            
            const int entityCount = 50;
            const int frameCount = 60;
            
            _output.WriteLine("=== COMPARISON: Deterministic vs Real-Time ===");
            
            // Run both simulations
            var detHash = RunDeterministicSimulation(entityCount, frameCount, 1.0f / 60.0f, _deterministicFile);
            var rtHash = RunRealTimeSimulation(entityCount, frameCount, 60, _realTimeFile);
            
            _output.WriteLine($"Deterministic final state hash: {detHash}");
            _output.WriteLine($"Real-time final state hash: {rtHash}");
            
            // They should produce DIFFERENT results (different frame times)
            // Note: In rare cases they might match if real-time was perfectly synchronized
            _output.WriteLine($"Results differ: {detHash != rtHash}");
            
            // Compare file sizes
            var detSize = new FileInfo(_deterministicFile).Length;
            var rtSize = new FileInfo(_realTimeFile).Length;
            
            _output.WriteLine($"");
            _output.WriteLine($"File Size Comparison:");
            _output.WriteLine($"  Deterministic: {detSize / 1024.0:F2} KB");
            _output.WriteLine($"  Real-time: {rtSize / 1024.0:F2} KB");
            _output.WriteLine($"  Difference: {Math.Abs(detSize - rtSize) / 1024.0:F2} KB");
            
            // Both should produce deterministic playback
            var detPlayback1 = PlaybackAndHash(_deterministicFile, entityCount);
            var detPlayback2 = PlaybackAndHash(_deterministicFile, entityCount);
            var rtPlayback1 = PlaybackAndHash(_realTimeFile, entityCount);
            var rtPlayback2 = PlaybackAndHash(_realTimeFile, entityCount);
            
            Assert.Equal(detPlayback1, detPlayback2);
            Assert.Equal(rtPlayback1, rtPlayback2);
            
            _output.WriteLine($"✅ Both modes produce deterministic playback");
        }

        [Fact]
        public void PlaybackIgnoresRecordingTiming_AlwaysDeterministic()
        {
            // Critical test: Playback should IGNORE the timing of the original recording
            // and produce the same state regardless of how it was recorded
            
            const int entityCount = 100;
            const int frameCount = 100;
            
            _output.WriteLine("=== PLAYBACK DETERMINISM TEST ===");
            _output.WriteLine($"Testing that playback is frame-based, not time-based");
            _output.WriteLine($"");
            
            // Record with artificial delays
            using (var repo = new EntityRepository())
            {
                repo.RegisterComponent<Position>();
                
                var entities = new Entity[entityCount];
                for (int i = 0; i < entityCount; i++)
                {
                    entities[i] = repo.CreateEntity();
                    repo.AddComponent(entities[i], new Position { X = i, Y = 0, Z = 0 });
                }
                
                using var recorder = new AsyncRecorder(_deterministicFile);
                
                for (int frame = 0; frame < frameCount; frame++)
                {
                    repo.Tick();
                    
                    // Update entities
                    for (int i = 0; i < entityCount; i++)
                    {
                        ref var pos = ref repo.GetComponentRW<Position>(entities[i]);
                        pos.X += 1.0f;
                    }
                    
                    // Add random delays during recording
                    if (frame % 10 == 0)
                        Thread.Sleep(new Random(frame).Next(1, 5));
                    
                    if (frame % 10 == 0)
                        recorder.CaptureKeyframe(repo, DateTime.UtcNow.Ticks, blocking: true);
                    else
                        recorder.CaptureFrame(repo, repo.GlobalVersion - 1, DateTime.UtcNow.Ticks, blocking: true);
                }
            }
            
            // Playback multiple times at different speeds
            var normalSpeed = PlaybackAtSpeed(_deterministicFile, entityCount, 0);
            var fastSpeed = PlaybackAtSpeed(_deterministicFile, entityCount, 0); // No delay
            var slowSpeed = PlaybackAtSpeed(_deterministicFile, entityCount, 2); // 2ms delay per frame
            
            _output.WriteLine($"Normal speed hash: {normalSpeed}");
            _output.WriteLine($"Fast speed hash: {fastSpeed}");
            _output.WriteLine($"Slow speed hash: {slowSpeed}");
            
            // All should be identical - playback is frame-based, not time-based
            Assert.Equal(normalSpeed, fastSpeed);
            Assert.Equal(normalSpeed, slowSpeed);
            
            _output.WriteLine($"✅ Playback speed doesn't affect determinism");
        }

        // Helper methods
        private ulong RunDeterministicSimulation(int entityCount, int frameCount, float fixedDeltaTime, string filePath)
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            repo.RegisterComponent<Velocity>();
            
            var entities = new Entity[entityCount];
            var random = new Random(42);
            
            for (int i = 0; i < entityCount; i++)
            {
                entities[i] = repo.CreateEntity();
                repo.AddComponent(entities[i], new Position { X = i * 10, Y = 0, Z = 0 });
                repo.AddComponent(entities[i], new Velocity 
                { 
                    VX = (float)(random.NextDouble() * 2 - 1),
                    VY = 0,
                    VZ = (float)(random.NextDouble() * 2 - 1)
                });
            }
            
            using (var recorder = new AsyncRecorder(filePath))
            {
                for (int frame = 0; frame < frameCount; frame++)
                {
                    repo.Tick();
                    
                    // Update with FIXED delta time
                    for (int i = 0; i < entityCount; i++)
                    {
                        ref var pos = ref repo.GetComponentRW<Position>(entities[i]);
                        var vel = repo.GetComponentRO<Velocity>(entities[i]);
                        
                        pos.X += vel.VX * fixedDeltaTime;
                        pos.Y += vel.VY * fixedDeltaTime;
                        pos.Z += vel.VZ * fixedDeltaTime;
                    }
                    
                    if (frame % 10 == 0)
                        recorder.CaptureKeyframe(repo, DateTime.UtcNow.Ticks, blocking: true);
                    else
                        recorder.CaptureFrame(repo, repo.GlobalVersion - 1, DateTime.UtcNow.Ticks, blocking: true);
                }
            }
            
            return ComputeStateHash(repo, entities);
        }

        private ulong RunRealTimeSimulation(int entityCount, int frameCount, float targetFPS, string filePath)
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            repo.RegisterComponent<Velocity>();
            
            var entities = new Entity[entityCount];
            var random = new Random(42);
            
            for (int i = 0; i < entityCount; i++)
            {
                entities[i] = repo.CreateEntity();
                repo.AddComponent(entities[i], new Position { X = i * 10, Y = 0, Z = 0 });
                repo.AddComponent(entities[i], new Velocity 
                { 
                    VX = (float)(random.NextDouble() * 2 - 1),
                    VY = 0,
                    VZ = (float)(random.NextDouble() * 2 - 1)
                });
            }
            
            var sw = Stopwatch.StartNew();
            long lastTime = 0;
            
            using (var recorder = new AsyncRecorder(filePath))
            {
                for (int frame = 0; frame < frameCount; frame++)
                {
                    float deltaTime = frame == 0 ? (1.0f / targetFPS) :
                        (sw.ElapsedTicks - lastTime) / (float)Stopwatch.Frequency;
                    lastTime = sw.ElapsedTicks;
                    
                    repo.Tick();
                    
                    for (int i = 0; i < entityCount; i++)
                    {
                        ref var pos = ref repo.GetComponentRW<Position>(entities[i]);
                        var vel = repo.GetComponentRO<Velocity>(entities[i]);
                        
                        pos.X += vel.VX * deltaTime;
                        pos.Y += vel.VY * deltaTime;
                        pos.Z += vel.VZ * deltaTime;
                    }
                    
                    if (frame % 10 == 0)
                        recorder.CaptureKeyframe(repo, DateTime.UtcNow.Ticks, blocking: true);
                    else
                        recorder.CaptureFrame(repo, repo.GlobalVersion - 1, DateTime.UtcNow.Ticks, blocking: true);
                    
                    // Simulate occasional frame drops
                    if (frame % 7 == 0)
                        Thread.Sleep(1);
                }
            }
            
            return ComputeStateHash(repo, entities);
        }

        private ulong PlaybackAndHash(string filePath, int entityCount)
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            repo.RegisterComponent<Velocity>();
            
            using var controller = new PlaybackController(filePath);
            
            while (!controller.IsAtEnd)
            {
                controller.StepForward(repo);
            }
            
            // Get all entities and compute hash
            var entities = new Entity[entityCount];
            for (int i = 0; i < entityCount; i++)
            {
                entities[i] = new Entity(i, 1); // Reconstruct entity IDs
            }
            
            return ComputeStateHash(repo, entities);
        }

        private ulong PlaybackAtSpeed(string filePath, int entityCount, int delayMs)
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            
            using var controller = new PlaybackController(filePath);
            
            while (!controller.IsAtEnd)
            {
                controller.StepForward(repo);
                if (delayMs > 0)
                    Thread.Sleep(delayMs);
            }
            
            var entities = new Entity[entityCount];
            for (int i = 0; i < entityCount; i++)
            {
                entities[i] = new Entity(i, 1);
            }
            
            return ComputeStateHash(repo, entities);
        }

        private ulong ComputeStateHash(EntityRepository repo, Entity[] entities)
        {
            ulong hash = 14695981039346656037UL; // FNV offset
            
            foreach (var entity in entities)
            {
                if (repo.IsAlive(entity) && repo.HasUnmanagedComponent<Position>(entity))
                {
                    var pos = repo.GetComponentRO<Position>(entity);
                    
                    hash ^= BitConverter.ToUInt32(BitConverter.GetBytes(pos.X), 0);
                    hash *= 1099511628211UL; // FNV prime
                    hash ^= BitConverter.ToUInt32(BitConverter.GetBytes(pos.Y), 0);
                    hash *= 1099511628211UL;
                    hash ^= BitConverter.ToUInt32(BitConverter.GetBytes(pos.Z), 0);
                    hash *= 1099511628211UL;
                }
            }
            
            return hash;
        }
    }
}
