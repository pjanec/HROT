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
    /// Performance benchmarks for different entity complexity levels.
    /// Tests lightweight (simple unmanaged), medium (mixed), and heavy (complex managed) entities.
    /// </summary>
    public class EntityComplexityPerformanceTests : IDisposable
    {
        private readonly ITestOutputHelper _output;
        private readonly List<string> _tempFiles = new List<string>();
        
        public EntityComplexityPerformanceTests(ITestOutputHelper output)
        {
            _output = output;
        }
        
        public void Dispose()
        {
            foreach (var file in _tempFiles)
            {
                try { File.Delete(file); } catch {}
            }
        }

        // Lightweight components
        [ComponentId(209)]
        public struct Position { public float X, Y, Z; }
        [ComponentId(210)]
        public struct Health { public int Value; }

        // Medium components
        [ComponentId(211)]
        public struct Transform
        {
            public float X, Y, Z;
            public float RotX, RotY, RotZ;
            public float ScaleX, ScaleY, ScaleZ;
        }

        // Heavy managed components
        [MessagePackObject]
        [ComponentId(212)]
        public record GameState
        {
            [Key(0)]
            public string PlayerName { get; set; } = "";
            
            [Key(1)]
            public int[] Inventory { get; set; } = Array.Empty<int>();
            
            [Key(2)]
            public Dictionary<string, float> Stats { get; set; } = new Dictionary<string, float>();
            
            [Key(3)]
            public List<string> Achievements { get; set; } = new List<string>();
        }

        [MessagePackObject]
        [ComponentId(213)]
        public record AIBehavior
        {
            [Key(0)]
            public string State { get; set; } = "Idle";
            
            [Key(1)]
            public float[] Weights { get; set; } = new float[10];
            
            [Key(2)]
            public int TargetEntity { get; set; }
        }

        [Fact]
        [Trait("Category", "Performance")]
        public void Lightweight_PlainUnmanaged_BestPerformance()
        {
            // Scenario: 2000 entities with just 2 small unmanaged components
            const int entityCount = 2000;
            const int frameCount = 500;
            const int keyframeInterval = 25;
            
            var filePath = Path.GetTempFileName() + ".fdp";
            _tempFiles.Add(filePath);
            
            _output.WriteLine($"=== LIGHTWEIGHT: Plain Unmanaged Components ===");
            _output.WriteLine($"Entities: {entityCount}, Frames: {frameCount}");
            _output.WriteLine($"Components per entity: 2 (Position + Health)");
            
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            repo.RegisterComponent<Health>();
            
            var entities = new Entity[entityCount];
            for (int i = 0; i < entityCount; i++)
            {
                entities[i] = repo.CreateEntity();
                repo.AddComponent(entities[i], new Position { X = i, Y = 0, Z = 0 });
                repo.AddComponent(entities[i], new Health { Value = 100 });
            }
            
            var sw = Stopwatch.StartNew();
            
            using (var recorder = new AsyncRecorder(filePath))
            {
                for (int frame = 0; frame < frameCount; frame++)
                {
                    repo.Tick();
                    
                    // Update all entities
                    for (int i = 0; i < entityCount; i++)
                    {
                        ref var pos = ref repo.GetComponentRW<Position>(entities[i]);
                        pos.X += 1;
                    }
                    
                    if (frame % keyframeInterval == 0)
                        recorder.CaptureKeyframe(repo, blocking: true);
                    else
                        recorder.CaptureFrame(repo, repo.GlobalVersion - 1, blocking: true);
                }
            }
            
            sw.Stop();
            var fileSize = new FileInfo(filePath).Length;
            double fps = frameCount / sw.Elapsed.TotalSeconds;
            
            _output.WriteLine($"Recording Time: {sw.ElapsedMilliseconds}ms");
            _output.WriteLine($"Recording FPS: {fps:F1}");
            _output.WriteLine($"File Size: {fileSize / (1024.0 * 1024.0):F2} MB");
            _output.WriteLine($"Throughput: {(entityCount * frameCount) / sw.Elapsed.TotalSeconds:N0} entity-frames/sec");
            
            Assert.True(fps > 200, $"Lightweight entities should achieve > 200 FPS (got {fps:F1})");
        }

        [Fact]
        public void Medium_MixedComponents_GoodPerformance()
        {
            // Scenario: 1000 entities with mix of unmanaged and one managed component
            const int entityCount = 1000;
            const int frameCount = 300;
            const int keyframeInterval = 30;
            
            var filePath = Path.GetTempFileName() + ".fdp";
            _tempFiles.Add(filePath);
            
            _output.WriteLine($"=== MEDIUM: Mixed Unmanaged + Managed ===");
            _output.WriteLine($"Entities: {entityCount}, Frames: {frameCount}");
            _output.WriteLine($"Components per entity: 3 (Transform + Position + AIBehavior)");
            
            using var repo = new EntityRepository();
            repo.RegisterComponent<Transform>();
            repo.RegisterComponent<Position>();
            repo.RegisterComponent<AIBehavior>();
            
            var entities = new Entity[entityCount];
            for (int i = 0; i < entityCount; i++)
            {
                entities[i] = repo.CreateEntity();
                repo.AddComponent(entities[i], new Transform
                {
                    X = i, Y = 0, Z = 0,
                    RotX = 0, RotY = 0, RotZ = 0,
                    ScaleX = 1, ScaleY = 1, ScaleZ = 1
                });
                repo.AddComponent(entities[i], new Position { X = i, Y = 0, Z = 0 });
                repo.AddManagedComponent(entities[i], new AIBehavior
                {
                    State = i % 2 == 0 ? "Patrol" : "Idle",
                    Weights = new float[10],
                    TargetEntity = -1
                });
            }
            
            var sw = Stopwatch.StartNew();
            
            using (var recorder = new AsyncRecorder(filePath))
            {
                for (int frame = 0; frame < frameCount; frame++)
                {
                    repo.Tick();
                    
                    // Update unmanaged frequently
                    for (int i = 0; i < entityCount; i++)
                    {
                        ref var pos = ref repo.GetComponentRW<Position>(entities[i]);
                        pos.X += 1;
                    }
                    
                    // Update managed occasionally
                    if (frame % 10 == 0)
                    {
                        for (int i = 0; i < entityCount; i += 2)
                        {
                            var ai = repo.GetComponentRW<AIBehavior>(entities[i]);
                            ai.State = frame % 20 == 0 ? "Attack" : "Patrol";
                        }
                    }
                    
                    if (frame % keyframeInterval == 0)
                        recorder.CaptureKeyframe(repo, blocking: true);
                    else
                        recorder.CaptureFrame(repo, repo.GlobalVersion - 1, blocking: true);
                }
            }
            
            sw.Stop();
            var fileSize = new FileInfo(filePath).Length;
            double fps = frameCount / sw.Elapsed.TotalSeconds;
            
            _output.WriteLine($"Recording Time: {sw.ElapsedMilliseconds}ms");
            _output.WriteLine($"Recording FPS: {fps:F1}");
            _output.WriteLine($"File Size: {fileSize / (1024.0 * 1024.0):F2} MB");
            _output.WriteLine($"Throughput: {(entityCount * frameCount) / sw.Elapsed.TotalSeconds:N0} entity-frames/sec");
            
            Assert.True(fps > 50, $"Medium entities should achieve > 50 FPS (got {fps:F1})");
        }

        [Fact]
        public void Heavy_ComplexManaged_AcceptablePerformance()
        {
            // Scenario: 500 entities with multiple complex managed components
            const int entityCount = 500;
            const int frameCount = 200;
            const int keyframeInterval = 20;
            
            var filePath = Path.GetTempFileName() + ".fdp";
            _tempFiles.Add(filePath);
            
            _output.WriteLine($"=== HEAVY: Complex Managed Components ===");
            _output.WriteLine($"Entities: {entityCount}, Frames: {frameCount}");
            _output.WriteLine($"Components per entity: 4 (Position + GameState + AIBehavior + Health)");
            
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            repo.RegisterComponent<Health>();
            repo.RegisterComponent<GameState>();
            repo.RegisterComponent<AIBehavior>();
            
            var entities = new Entity[entityCount];
            for (int i = 0; i < entityCount; i++)
            {
                entities[i] = repo.CreateEntity();
                repo.AddComponent(entities[i], new Position { X = i, Y = 0, Z = 0 });
                repo.AddComponent(entities[i], new Health { Value = 100 });
                
                repo.AddManagedComponent(entities[i], new GameState
                {
                    PlayerName = $"Player{i % 100}",
                    Inventory = new int[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 },
                    Stats = new Dictionary<string, float>
                    {
                        ["Health"] = 100,
                        ["Mana"] = 50,
                        ["Stamina"] = 80
                    },
                    Achievements = new List<string> { "First Steps", "Explorer" }
                });
                
                repo.AddManagedComponent(entities[i], new AIBehavior
                {
                    State = "Idle",
                    Weights = new float[10],
                    TargetEntity = -1
                });
            }
            
            var sw = Stopwatch.StartNew();
            
            using (var recorder = new AsyncRecorder(filePath))
            {
                for (int frame = 0; frame < frameCount; frame++)
                {
                    repo.Tick();
                    
                    // Update unmanaged
                    for (int i = 0; i < entityCount; i++)
                    {
                        ref var pos = ref repo.GetComponentRW<Position>(entities[i]);
                        pos.X += 1;
                    }
                    
                    // Update heavy managed components
                    if (frame % 5 == 0)
                    {
                        for (int i = 0; i < entityCount; i += 2)
                        {
                            var gameState = repo.GetComponentRW<GameState>(entities[i]);
                            gameState.Stats["Health"] = Math.Max(0, 100 - frame);
                            
                            if (frame % 10 == 0)
                            {
                                gameState.Achievements.Add($"Frame{frame}");
                            }
                        }
                    }
                    
                    if (frame % keyframeInterval == 0)
                        recorder.CaptureKeyframe(repo, blocking: true);
                    else
                        recorder.CaptureFrame(repo, repo.GlobalVersion - 1, blocking: true);
                }
            }
            
            sw.Stop();
            var fileSize = new FileInfo(filePath).Length;
            double fps = frameCount / sw.Elapsed.TotalSeconds;
            
            _output.WriteLine($"Recording Time: {sw.ElapsedMilliseconds}ms");
            _output.WriteLine($"Recording FPS: {fps:F1}");
            _output.WriteLine($"File Size: {fileSize / (1024.0 * 1024.0):F2} MB");
            _output.WriteLine($"Throughput: {(entityCount * frameCount) / sw.Elapsed.TotalSeconds:N0} entity-frames/sec");
            
            Assert.True(fps > 20, $"Heavy entities should achieve > 20 FPS (got {fps:F1})");
        }

        [Fact]
        public void Comparison_AllComplexityLevels_ShowsScaling()
        {
            // Run all three scenarios and compare
            _output.WriteLine($"=== COMPLEXITY COMPARISON ===");
            _output.WriteLine("");
            
            const int entityCount = 500;
            const int frameCount = 100;
            
            // Lightweight
            var lightResult = RunComplexityTest("Lightweight", entityCount, frameCount, 
                repo => {
                    repo.RegisterComponent<Position>();
                    repo.RegisterComponent<Health>();
                },
                (repo, e) => {
                    repo.AddComponent(e, new Position { X = e.Index, Y = 0, Z = 0 });
                    repo.AddComponent(e, new Health { Value = 100 });
                },
                (repo, entities, frame) => {
                    for (int i = 0; i < entities.Length; i++)
                    {
                        ref var pos = ref repo.GetComponentRW<Position>(entities[i]);
                        pos.X += 1;
                    }
                });
            
            // Medium
            var mediumResult = RunComplexityTest("Medium", entityCount, frameCount,
                repo => {
                    repo.RegisterComponent<Position>();
                    repo.RegisterComponent<Transform>();
                    repo.RegisterComponent<AIBehavior>();
                },
                (repo, e) => {
                    repo.AddComponent(e, new Position { X = e.Index, Y = 0, Z = 0 });
                    repo.AddComponent(e, new Transform { X = e.Index, Y = 0, Z = 0 });
                    repo.AddManagedComponent(e, new AIBehavior { State = "Idle", Weights = new float[10] });
                },
                (repo, entities, frame) => {
                    for (int i = 0; i < entities.Length; i++)
                    {
                        ref var pos = ref repo.GetComponentRW<Position>(entities[i]);
                        pos.X += 1;
                    }
                    if (frame % 5 == 0)
                    {
                        for (int i = 0; i < entities.Length; i += 2)
                        {
                            var ai = repo.GetComponentRW<AIBehavior>(entities[i]);
                            ai.State = "Changed";
                        }
                    }
                });
            
            // Heavy
            var heavyResult = RunComplexityTest("Heavy", entityCount, frameCount,
                repo => {
                    repo.RegisterComponent<Position>();
                    repo.RegisterComponent<GameState>();
                    repo.RegisterComponent<AIBehavior>();
                },
                (repo, e) => {
                    repo.AddComponent(e, new Position { X = e.Index, Y = 0, Z = 0 });
                    repo.AddManagedComponent(e, new GameState
                    {
                        PlayerName = $"P{e.Index}",
                        Inventory = new int[10],
                        Stats = new Dictionary<string, float> { ["HP"] = 100 },
                        Achievements = new List<string> { "Test" }
                    });
                    repo.AddManagedComponent(e, new AIBehavior { State = "Idle", Weights = new float[10] });
                },
                (repo, entities, frame) => {
                    for (int i = 0; i < entities.Length; i++)
                    {
                        ref var pos = ref repo.GetComponentRW<Position>(entities[i]);
                        pos.X += 1;
                    }
                    if (frame % 3 == 0)
                    {
                        for (int i = 0; i < entities.Length; i += 2)
                        {
                            var gs = repo.GetComponentRW<GameState>(entities[i]);
                            gs.Stats["HP"] -= 1;
                        }
                    }
                });
            
            _output.WriteLine("");
            _output.WriteLine($"┌─────────────┬──────────┬─────────────┬──────────────┐");
            _output.WriteLine($"│ Complexity  │ FPS      │ File Size   │ Throughput   │");
            _output.WriteLine($"├─────────────┼──────────┼─────────────┼──────────────┤");
            _output.WriteLine($"│ Lightweight │ {lightResult.fps,8:F1} │ {lightResult.fileSizeMB,9:F2} MB │ {lightResult.throughput,10:N0} ef/s │");
            _output.WriteLine($"│ Medium      │ {mediumResult.fps,8:F1} │ {mediumResult.fileSizeMB,9:F2} MB │ {mediumResult.throughput,10:N0} ef/s │");
            _output.WriteLine($"│ Heavy       │ {heavyResult.fps,8:F1} │ {heavyResult.fileSizeMB,9:F2} MB │ {heavyResult.throughput,10:N0} ef/s │");
            _output.WriteLine($"└─────────────┴──────────┴─────────────┴──────────────┘");
            _output.WriteLine($"");
            _output.WriteLine($"Performance ratio (Light:Medium:Heavy) = 1.0 : {lightResult.fps / mediumResult.fps:F2} : {lightResult.fps / heavyResult.fps:F2}");
        }

        private (double fps, double fileSizeMB, double throughput) RunComplexityTest(
            string name,
            int entityCount,
            int frameCount,
            Action<EntityRepository> registerComponents,
            Action<EntityRepository, Entity> addComponents,
            Action<EntityRepository, Entity[], int> updateFrame)
        {
            var filePath = Path.GetTempFileName() + ".fdp";
            _tempFiles.Add(filePath);
            
            using var repo = new EntityRepository();
            registerComponents(repo);
            
            var entities = new Entity[entityCount];
            for (int i = 0; i < entityCount; i++)
            {
                entities[i] = repo.CreateEntity();
                addComponents(repo, entities[i]);
            }
            
            var sw = Stopwatch.StartNew();
            
            using (var recorder = new AsyncRecorder(filePath))
            {
                for (int frame = 0; frame < frameCount; frame++)
                {
                    repo.Tick();
                    updateFrame(repo, entities, frame);
                    
                    if (frame % 10 == 0)
                        recorder.CaptureKeyframe(repo, blocking: true);
                    else
                        recorder.CaptureFrame(repo, repo.GlobalVersion - 1, blocking: true);
                }
            }
            
            sw.Stop();
            var fileSize = new FileInfo(filePath).Length;
            double fps = frameCount / sw.Elapsed.TotalSeconds;
            double throughput = (entityCount * frameCount) / sw.Elapsed.TotalSeconds;
            
            return (fps, fileSize / (1024.0 * 1024.0), throughput);
        }
    }
}
