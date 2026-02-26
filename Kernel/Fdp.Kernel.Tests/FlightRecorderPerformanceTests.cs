using System;
using System.IO;
using System.Diagnostics;
using System.Collections.Generic;
using Xunit;
using Xunit.Abstractions;
using Fdp.Kernel;
using Fdp.Kernel.FlightRecorder;
using MessagePack;

namespace Fdp.Tests
{
    /// <summary>
    /// Performance benchmarks for the Flight Recorder system.
    /// Measures FPS for recording, playback, seeking, and rewinding with large entity counts.
    /// </summary>
    public class FlightRecorderPerformanceTests : IDisposable
    {
        private readonly string _testFilePath;
        private readonly ITestOutputHelper _output;
        
        public FlightRecorderPerformanceTests(ITestOutputHelper output)
        {
            _output = output;
            _testFilePath = Path.Combine(Path.GetTempPath(), $"perf_test_{Guid.NewGuid()}.fdp");
        }
        
        public void Dispose()
        {
            try { File.Delete(_testFilePath); } catch {}
        }

        [ComponentId(230)]
        public struct Transform
        {
            public float X, Y, Z;
            public float RotX, RotY, RotZ;
            public float ScaleX, ScaleY, ScaleZ;
        }

        [ComponentId(231)]
        public struct Velocity
        {
            public float VX, VY, VZ;
        }

        [MessagePackObject]
        [ComponentId(232)]
        public record UnitStats
        {
            [Key(0)]
            public string Name { get; set; } = "";
            
            [Key(1)]
            public int Health { get; set; }
            
            [Key(2)]
            public int[] Inventory { get; set; } = Array.Empty<int>();
        }

        [Fact]
        public void RecordingPerformance_1000Entities_MeasuresFPS()
        {
            // Benchmark: Recording 1000 entities with MIXED components over 300 frames
            const int entityCount = 1000;
            const int frameCount = 300;
            const int keyframeInterval = 30;
            
            using var repo = new EntityRepository();
            repo.RegisterComponent<Transform>();
            repo.RegisterComponent<Velocity>();
            repo.RegisterComponent<UnitStats>(); // Mixed: managed component too!
            
            _output.WriteLine($"=== Recording Performance Test (Mixed Components) ===");
            _output.WriteLine($"Entities: {entityCount}, Frames: {frameCount}");
            
            // Setup entities with BOTH unmanaged and managed components
            var entities = new Entity[entityCount];
            for (int i = 0; i < entityCount; i++)
            {
                entities[i] = repo.CreateEntity();
                
                // Unmanaged: Transform + Velocity
                repo.AddComponent(entities[i], new Transform
                {
                    X = i, Y = i, Z = i,
                    RotX = 0, RotY = 0, RotZ = 0,
                    ScaleX = 1, ScaleY = 1, ScaleZ = 1
                });
                repo.AddComponent(entities[i], new Velocity
                {
                    VX = 1, VY = 0, VZ = 0
                });
                
                // Managed: UnitStats
                repo.AddManagedComponent(entities[i], new UnitStats
                {
                    Name = $"Unit{i % 100}", // Reuse names to test string dedup
                    Health = 100,
                    Inventory = new[] { 1, 2, 3 }
                });
            }
            
            var sw = Stopwatch.StartNew();
            long totalBytes = 0;
            
            using (var recorder = new AsyncRecorder(_testFilePath))
            {
                for (int frame = 0; frame < frameCount; frame++)
                {
                    repo.Tick();
                    
                    // Simulate: Half the entities move and take damage
                    for (int i = 0; i < entityCount; i += 2)
                    {
                        // Update unmanaged
                        ref var transform = ref repo.GetComponentRW<Transform>(entities[i]);
                        var velocity = repo.GetComponentRO<Velocity>(entities[i]);
                        transform.X += velocity.VX;
                        transform.Y += velocity.VY;
                        transform.Z += velocity.VZ;
                        
                        // Update managed occasionally (more expensive)
                        if (frame % 10 == 0)
                        {
                            var stats = repo.GetComponentRW<UnitStats>(entities[i]);
                            stats.Health = Math.Max(0, stats.Health - 1);
                        }
                    }
                    
                    if (frame % keyframeInterval == 0)
                        recorder.CaptureKeyframe(repo, blocking: true);
                    else
                        recorder.CaptureFrame(repo, repo.GlobalVersion - 1, blocking: true);
                }
            }
            
            sw.Stop();
            totalBytes = new FileInfo(_testFilePath).Length;
            
            double fps = frameCount / sw.Elapsed.TotalSeconds;
            double mbPerSec = (totalBytes / (1024.0 * 1024.0)) / sw.Elapsed.TotalSeconds;
            
            _output.WriteLine($"Recording Time: {sw.ElapsedMilliseconds}ms");
            _output.WriteLine($"Recording FPS: {fps:F1}");
            _output.WriteLine($"File Size: {totalBytes / (1024.0 * 1024.0):F2} MB");
            _output.WriteLine($"Write Speed: {mbPerSec:F2} MB/s");
            _output.WriteLine($"Components: {entityCount * 3:N0} (Transform + Velocity + UnitStats)");
            
            // Performance assertions (very generous - should easily pass)
            Assert.True(fps > 50, $"Recording FPS ({fps:F1}) should be > 50 with mixed components");
            Assert.True(sw.ElapsedMilliseconds < 15000, $"Recording should complete in < 15s (took {sw.ElapsedMilliseconds}ms)");
        }

        [Fact]
        public void PlaybackPerformance_SequentialPlayback_MeasuresFPS()
        {
            // First record a test scenario with mixed components
            const int entityCount = 1000;
            const int frameCount = 300;
            CreatePerformanceRecording(entityCount, frameCount);
            
            _output.WriteLine($"=== Playback Performance Test (Mixed Components) ===");
            _output.WriteLine($"Entities: {entityCount}, Frames: {frameCount}");
            
            using var targetRepo = new EntityRepository();
            targetRepo.RegisterComponent<Transform>();
            targetRepo.RegisterComponent<Velocity>();
            targetRepo.RegisterComponent<UnitStats>();
            
            using var controller = new PlaybackController(_testFilePath);
            
            var sw = Stopwatch.StartNew();
            int framesPlayed = 0;
            
            while (!controller.IsAtEnd)
            {
                controller.StepForward(targetRepo);
                framesPlayed++;
            }
            
            sw.Stop();
            
            double fps = framesPlayed / sw.Elapsed.TotalSeconds;
            
            _output.WriteLine($"Playback Time: {sw.ElapsedMilliseconds}ms");
            _output.WriteLine($"Playback FPS: {fps:F1}");
            _output.WriteLine($"Frames Played: {framesPlayed}");
            
            Assert.Equal(frameCount, framesPlayed);
            Assert.True(fps > 200, $"Playback FPS ({fps:F1}) should be > 200 (playback is faster than recording)");
        }

        [Fact]
        public void SeekPerformance_RandomSeeking_MeasuresLatency()
        {
            // Record with good keyframe density for seeking
            const int entityCount = 1000;
            const int frameCount = 200;
            const int keyframeInterval = 10; // Every 10 frames
            CreatePerformanceRecording(entityCount, frameCount, keyframeInterval);
            
            _output.WriteLine($"=== Seek Performance Test ===");
            _output.WriteLine($"Entities: {entityCount}, Frames: {frameCount}, Keyframe Interval: {keyframeInterval}");
            
            using var targetRepo = new EntityRepository();
            targetRepo.RegisterComponent<Transform>();
            targetRepo.RegisterComponent<Velocity>();
            targetRepo.RegisterComponent<UnitStats>();
            
            using var controller = new PlaybackController(_testFilePath);
            
            var random = new Random(42);
            const int seekOperations = 100;
            var seekTimes = new List<long>();
            
            for (int i = 0; i < seekOperations; i++)
            {
                int targetFrame = random.Next(0, frameCount);
                
                var sw = Stopwatch.StartNew();
                controller.SeekToFrame(targetRepo, targetFrame);
                sw.Stop();
                
                seekTimes.Add(sw.ElapsedMilliseconds);
            }
            
            // Calculate statistics
            seekTimes.Sort();
            double avgSeek = seekTimes.Average();
            long p50 = seekTimes[seekTimes.Count / 2];
            long p95 = seekTimes[(int)(seekTimes.Count * 0.95)];
            long p99 = seekTimes[(int)(seekTimes.Count * 0.99)];
            long max = seekTimes[seekTimes.Count - 1];
            
            _output.WriteLine($"Seek Operations: {seekOperations}");
            _output.WriteLine($"Average Seek: {avgSeek:F2}ms");
            _output.WriteLine($"P50 Seek: {p50}ms");
            _output.WriteLine($"P95 Seek: {p95}ms");
            _output.WriteLine($"P99 Seek: {p99}ms");
            _output.WriteLine($"Max Seek: {max}ms");
            
            // Within keyframe interval (worst case ~10 deltas) should be very fast
            Assert.True(avgSeek < 20, $"Average seek time ({avgSeek:F2}ms) should be < 20ms");
            Assert.True(p95 < 50, $"P95 seek time ({p95}ms) should be < 50ms");
        }

        [Fact]
        public void RewindPerformance_LargeJumpsBackward_MeasuresLatency()
        {
            const int entityCount = 500;
            const int frameCount = 100;
            const int keyframeInterval = 10;
            CreatePerformanceRecording(entityCount, frameCount, keyframeInterval);
            
            _output.WriteLine($"=== Rewind Performance Test ===");
            _output.WriteLine($"Entities: {entityCount}, Frames: {frameCount}");
            
            using var targetRepo = new EntityRepository();
            targetRepo.RegisterComponent<Transform>();
            targetRepo.RegisterComponent<Velocity>();
    targetRepo.RegisterComponent<UnitStats>();
            
            using var controller = new PlaybackController(_testFilePath);
            
            // Fast forward to end
            controller.PlayToEnd(targetRepo);
            Assert.True(controller.IsAtEnd);
            
            // Measure rewinding to various points
            var rewindTests = new[]
            {
                (frameCount - 1, 50, "Large rewind (50% back)"),
                (50, 25, "Medium rewind"),
                (25, 0, "Full rewind to start")
            };
            
            foreach (var (from, to, description) in rewindTests)
            {
                controller.SeekToFrame(targetRepo, from);
                
                var sw = Stopwatch.StartNew();
                controller.SeekToFrame(targetRepo, to);
                sw.Stop();
                
                _output.WriteLine($"{description}: {from} → {to} = {sw.ElapsedMilliseconds}ms");
                
                Assert.True(sw.ElapsedMilliseconds < 100, $"Rewind should complete in < 100ms");
            }
        }

        [Fact]
        public void ThroughputPerformance_CreateDestroy_MeasuresOpsPerSec()
        {
            // Measure how many entity create/destroy operations can be recorded per second
            const int operationsPerFrame = 100;
            const int frameCount = 100;
            
            using var repo = new EntityRepository();
            repo.RegisterComponent<IntComponent>();
            
            _output.WriteLine($"=== Entity Lifecycle Throughput Test ===");
            _output.WriteLine($"Operations per frame: {operationsPerFrame}, Frames: {frameCount}");
            
            var sw = Stopwatch.StartNew();
            long totalOperations = 0;
            
            using (var recorder = new AsyncRecorder(_testFilePath))
            {
                for (int frame = 0; frame < frameCount; frame++)
                {
                    repo.Tick();
                    
                    // Create entities
                    var tempEntities = new Entity[operationsPerFrame];
                    for (int i = 0; i < operationsPerFrame; i++)
                    {
                        tempEntities[i] = repo.CreateEntity();
                        repo.AddComponent(tempEntities[i], new IntComponent { Value = frame * 1000 + i });
                        totalOperations++;
                    }
                    
                    // Destroy half immediately (stress test)
                    for (int i = 0; i < operationsPerFrame / 2; i++)
                    {
                        repo.DestroyEntity(tempEntities[i]);
                        totalOperations++;
                    }
                    
                    if (frame % 10 == 0)
                        recorder.CaptureKeyframe(repo, blocking: true);
                    else
                        recorder.CaptureFrame(repo, repo.GlobalVersion - 1, blocking: true);
                }
            }
            
            sw.Stop();
            
            double opsPerSec = totalOperations / sw.Elapsed.TotalSeconds;
            
            _output.WriteLine($"Total Operations: {totalOperations:N0}");
            _output.WriteLine($"Time: {sw.ElapsedMilliseconds}ms");
            _output.WriteLine($"Throughput: {opsPerSec:N0} ops/sec");
            
            Assert.True(opsPerSec > 10000, $"Throughput ({opsPerSec:N0} ops/sec) should be > 10,000");
        }

        [Fact]
        public void ManagedComponentPerformance_SerializationOverhead_Measured()
        {
            // Compare unmanaged vs managed component recording performance
            const int entityCount = 500;
            const int frameCount = 100;
            
            // Test 1: Unmanaged only
            long unmanagedTime, managedTime;
            
            using (var repo = new EntityRepository())
            {
                repo.RegisterComponent<Transform>();
                
                var entities = new Entity[entityCount];
                for (int i = 0; i < entityCount; i++)
                {
                    entities[i] = repo.CreateEntity();
                    repo.AddComponent(entities[i], new Transform { X = i, Y = i, Z = i });
                }
                
                var sw = Stopwatch.StartNew();
                using (var recorder = new AsyncRecorder(_testFilePath + "_unmanaged"))
                {
                    for (int frame = 0; frame < frameCount; frame++)
                    {
                        repo.Tick();
                        for (int i = 0; i < entityCount; i += 2)
                        {
                            ref var t = ref repo.GetComponentRW<Transform>(entities[i]);
                            t.X += 1;
                        }
                        
                        if (frame % 10 == 0)
                            recorder.CaptureKeyframe(repo, blocking: true);
                        else
                            recorder.CaptureFrame(repo, repo.GlobalVersion - 1, blocking: true);
                    }
                }
                unmanagedTime = sw.ElapsedMilliseconds;
            }
            
            // Test 2: Managed components
            using (var repo = new EntityRepository())
            {
                repo.RegisterComponent<UnitStats>();
                
                var entities = new Entity[entityCount];
                for (int i = 0; i < entityCount; i++)
                {
                    entities[i] = repo.CreateEntity();
                    repo.AddManagedComponent(entities[i], new UnitStats
                    {
                        Name = $"Unit{i}",
                        Health = 100,
                        Inventory = new[] { 1, 2, 3, 4, 5 }
                    });
                }
                
                var sw = Stopwatch.StartNew();
                using (var recorder = new AsyncRecorder(_testFilePath + "_managed"))
                {
                    for (int frame = 0; frame < frameCount; frame++)
                    {
                        repo.Tick();
                        for (int i = 0; i < entityCount; i += 2)
                        {
                            var stats = repo.GetComponentRW<UnitStats>(entities[i]);
                            stats.Health = 100 - frame;
                        }
                        
                        if (frame % 10 == 0)
                            recorder.CaptureKeyframe(repo, blocking: true);
                        else
                            recorder.CaptureFrame(repo, repo.GlobalVersion - 1, blocking: true);
                    }
                }
                managedTime = sw.ElapsedMilliseconds;
            }
            
            _output.WriteLine($"=== Component Type Performance Comparison ===");
            _output.WriteLine($"Unmanaged (Transform): {unmanagedTime}ms");
            _output.WriteLine($"Managed (UnitStats): {managedTime}ms");
            _output.WriteLine($"Overhead: {((double)managedTime / unmanagedTime - 1) * 100:F1}%");
            
            // Managed should be reasonably close (within 5x)
            Assert.True(managedTime < unmanagedTime * 5, 
                $"Managed component overhead should be < 5x (was {(double)managedTime / unmanagedTime:F1}x)");
            
            // Cleanup
            try
            {
                File.Delete(_testFilePath + "_unmanaged");
                File.Delete(_testFilePath + "_managed");
            }
            catch { }
        }

        // Helper method to create standardized performance recordings with MIXED components
        private void CreatePerformanceRecording(int entityCount, int frameCount, int keyframeInterval = 30)
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<Transform>();
            repo.RegisterComponent<Velocity>();
            repo.RegisterComponent<UnitStats>();
            
            var entities = new Entity[entityCount];
            for (int i = 0; i < entityCount; i++)
            {
                entities[i] = repo.CreateEntity();
                
                // Unmanaged components
                repo.AddComponent(entities[i], new Transform
                {
                    X = i, Y = 0, Z = 0,
                    RotX = 0, RotY = 0, RotZ = 0,
                    ScaleX = 1, ScaleY = 1, ScaleZ = 1
                });
                repo.AddComponent(entities[i], new Velocity { VX = 1, VY = 0, VZ = 0 });
                
                // Managed component
                repo.AddManagedComponent(entities[i], new UnitStats
                {
                    Name = $"Unit{i % 50}",
                    Health = 100,
                    Inventory = new[] { 1, 2, 3 }
                });
            }
            
            using var recorder = new AsyncRecorder(_testFilePath);
            
            for (int frame = 0; frame < frameCount; frame++)
            {
                repo.Tick();
                
                // Move half the entities (unmanaged update)
                for (int i = 0; i < entityCount; i += 2)
                {
                    ref var transform = ref repo.GetComponentRW<Transform>(entities[i]);
                    var velocity = repo.GetComponentRO<Velocity>(entities[i]);
                    transform.X += velocity.VX;
                }
                
                // Update managed components occasionally
                if (frame % 5 == 0)
                {
                    for (int i = 0; i < entityCount; i += 4)
                    {
                        var stats = repo.GetComponentRW<UnitStats>(entities[i]);
                        stats.Health = Math.Max(0, 100 - frame / 5);
                    }
                }
                
                if (frame % keyframeInterval == 0)
                    recorder.CaptureKeyframe(repo, blocking: true);
                else
                    recorder.CaptureFrame(repo, repo.GlobalVersion - 1, blocking: true);
            }
        }
    }
}
