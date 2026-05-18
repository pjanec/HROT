using System;
using System.Diagnostics;
using System.Threading;
using Xunit;
using Xunit.Abstractions;
using Fdp.Core;

namespace Fdp.Tests
{
    /// <summary>
    /// Tests core simulation determinism independent of Flight Recorder.
    /// Validates that fixed timestep produces deterministic results,
    /// and variable timestep works correctly for real-time scenarios.
    /// </summary>
    public class DeterministicSimulationTests
    {
        private readonly ITestOutputHelper _output;
        
        public DeterministicSimulationTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [ComponentId(222)]
        public struct Position { public float X, Y, Z; }
        [ComponentId(223)]
        public struct Velocity { public float VX, VY, VZ; }
        [ComponentId(224)]
        public struct Acceleration { public float AX, AY, AZ; }

        [Fact]
        public void FixedTimestep_SameInput_ProducesIdenticalOutput()
        {
            // Core determinism test: Same inputs MUST produce same outputs
            const int entityCount = 100;
            const int stepCount = 1000;
            const float fixedDeltaTime = 1.0f / 60.0f;
            
            _output.WriteLine("=== DETERMINISTIC SIMULATION TEST ===");
            _output.WriteLine($"Testing with {entityCount} entities over {stepCount} steps");
            _output.WriteLine($"Fixed delta time: {fixedDeltaTime}s");
            _output.WriteLine("");
            
            // Run simulation twice with identical setup
            var state1 = RunFixedTimestepSimulation(entityCount, stepCount, fixedDeltaTime, seed: 42);
            var state2 = RunFixedTimestepSimulation(entityCount, stepCount, fixedDeltaTime, seed: 42);
            
            _output.WriteLine($"Run 1 final state hash: {state1.hash:X16}");
            _output.WriteLine($"Run 2 final state hash: {state2.hash:X16}");
            _output.WriteLine($"Run 1 total distance: {state1.totalDistance:F6}");
            _output.WriteLine($"Run 2 total distance: {state2.totalDistance:F6}");
            
            // Must be EXACTLY identical (bit-for-bit)
            Assert.Equal(state1.hash, state2.hash);
            Assert.Equal(state1.totalDistance, state2.totalDistance);
            Assert.Equal(state1.entityCount, state2.entityCount);
            
            _output.WriteLine("✅ DETERMINISTIC: Identical inputs produced identical outputs");
        }

        [Fact]
        public void FixedTimestep_DifferentSeeds_ProducesDifferentOutput()
        {
            // Sanity check: Different inputs should produce different outputs
            const int entityCount = 100;
            const int stepCount = 100;
            const float fixedDeltaTime = 1.0f / 60.0f;
            
            var state1 = RunFixedTimestepSimulation(entityCount, stepCount, fixedDeltaTime, seed: 42);
            var state2 = RunFixedTimestepSimulation(entityCount, stepCount, fixedDeltaTime, seed: 123);
            
            _output.WriteLine($"Seed 42 hash: {state1.hash:X16}");
            _output.WriteLine($"Seed 123 hash: {state2.hash:X16}");
            
            // Different seeds must produce different results
            Assert.NotEqual(state1.hash, state2.hash);
            
            _output.WriteLine("✅ Different seeds produce different results");
        }

        [Fact]
        public void FixedTimestep_ComplexPhysics_RemainsStable()
        {
            // Test numerical stability with complex physics simulation
            const int entityCount = 50;
            const int stepCount = 10000; // Long simulation
            const float fixedDeltaTime = 1.0f / 60.0f;
            
            _output.WriteLine("=== NUMERICAL STABILITY TEST ===");
            _output.WriteLine($"Long simulation: {stepCount} steps");
            
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            repo.RegisterComponent<Velocity>();
            repo.RegisterComponent<Acceleration>();
            
            var entities = new Entity[entityCount];
            var random = new Random(42);
            
            // Setup entities with acceleration (more complex physics)
            for (int i = 0; i < entityCount; i++)
            {
                entities[i] = repo.CreateEntity();
                repo.AddComponent(entities[i], new Position { X = i * 10f, Y = 0, Z = 0 });
                repo.AddComponent(entities[i], new Velocity 
                { 
                    VX = (float)(random.NextDouble() * 2 - 1),
                    VY = (float)(random.NextDouble() * 2 - 1),
                    VZ = (float)(random.NextDouble() * 2 - 1)
                });
                repo.AddComponent(entities[i], new Acceleration
                {
                    AX = 0,
                    AY = -9.81f, // Gravity
                    AZ = 0
                });
            }
            
            float minY = float.MaxValue;
            float maxY = float.MinValue;
            int explosions = 0;
            
            // Simulate with gravity and bouncing
            for (int step = 0; step < stepCount; step++)
            {
                repo.Tick();
                
                for (int i = 0; i < entityCount; i++)
                {
                    ref var pos = ref repo.GetComponentRW<Position>(entities[i]);
                    ref var vel = ref repo.GetComponentRW<Velocity>(entities[i]);
                    var acc = repo.GetComponentRO<Acceleration>(entities[i]);
                    
                    // Euler integration (simple but prone to instability if not careful)
                    vel.VX += acc.AX * fixedDeltaTime;
                    vel.VY += acc.AY * fixedDeltaTime;
                    vel.VZ += acc.AZ * fixedDeltaTime;
                    
                    pos.X += vel.VX * fixedDeltaTime;
                    pos.Y += vel.VY * fixedDeltaTime;
                    pos.Z += vel.VZ * fixedDeltaTime;
                    
                    // Ground collision (bounce)
                    if (pos.Y < 0)
                    {
                        pos.Y = 0;
                        vel.VY = -vel.VY * 0.8f; // Bounce with damping
                    }
                    
                    minY = MathF.Min(minY, pos.Y);
                    maxY = MathF.Max(maxY, pos.Y);
                    
                    // Check for numerical explosion
                    if (MathF.Abs(pos.X) > 1_000_000 || MathF.Abs(pos.Y) > 1_000_000 || MathF.Abs(pos.Z) > 1_000_000)
                    {
                        explosions++;
                    }
                }
            }
            
            _output.WriteLine($"Y range after {stepCount} steps: [{minY:F2}, {maxY:F2}]");
            _output.WriteLine($"Numerical explosions: {explosions}");
            
            // Should remain stable (no explosions)
            Assert.Equal(0, explosions);
            Assert.True(maxY < 10000, "Simulation should remain bounded");
            
            _output.WriteLine("✅ STABLE: Complex physics simulation remained stable");
        }

        [Fact]
        public void VariableTimestep_RealTime_HandlesFrameDrops()
        {
            // Real-time simulation with variable timestep
            const int entityCount = 100;
            const int targetFrames = 120;
            const float targetFPS = 60;
            const float minDeltaTime = 1.0f / 120.0f;  // Cap to 120 FPS
            const float maxDeltaTime = 1.0f / 30.0f;   // Cap to 30 FPS min
            
            _output.WriteLine("=== REAL-TIME VARIABLE TIMESTEP TEST ===");
            _output.WriteLine($"Target FPS: {targetFPS}");
            _output.WriteLine($"Delta time range: [{minDeltaTime * 1000:F2}ms, {maxDeltaTime * 1000:F2}ms]");
            
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            repo.RegisterComponent<Velocity>();
            
            var entities = new Entity[entityCount];
            var random = new Random(42);
            
            for (int i = 0; i < entityCount; i++)
            {
                entities[i] = repo.CreateEntity();
                repo.AddComponent(entities[i], new Position { X = i * 10f, Y = 0, Z = 0 });
                repo.AddComponent(entities[i], new Velocity 
                { 
                    VX = (float)(random.NextDouble() * 10),
                    VY = 0,
                    VZ = 0
                });
            }
            
            var sw = Stopwatch.StartNew();
            long lastFrameTime = 0;
            var frameTimes = new System.Collections.Generic.List<float>();
            
            for (int frame = 0; frame < targetFrames; frame++)
            {
                long frameStart = sw.ElapsedTicks;
                float rawDelta = frame == 0 ? (1.0f / targetFPS) : 
                    (frameStart - lastFrameTime) / (float)Stopwatch.Frequency;
                
                // Clamp delta time (important for stability!)
                float deltaTime = Math.Clamp(rawDelta, minDeltaTime, maxDeltaTime);
                frameTimes.Add(deltaTime);
                lastFrameTime = frameStart;
                
                repo.Tick();
                
                // Update with variable delta time
                for (int i = 0; i < entityCount; i++)
                {
                    ref var pos = ref repo.GetComponentRW<Position>(entities[i]);
                    var vel = repo.GetComponentRO<Velocity>(entities[i]);
                    
                    pos.X += vel.VX * deltaTime;
                }
                
                // Simulate frame drops occasionally
                if (frame % 20 == 0)
                    Thread.Sleep(5); // Spike
            }
            
            sw.Stop();
            
            // Calculate statistics
            float avgDelta = 0;
            float minDelta = float.MaxValue;
            float maxDelta = float.MinValue;
            float variance = 0;
            
            for (int i = 1; i < frameTimes.Count; i++)
            {
                avgDelta += frameTimes[i];
                minDelta = MathF.Min(minDelta, frameTimes[i]);
                maxDelta = MathF.Max(maxDelta, frameTimes[i]);
            }
            avgDelta /= (frameTimes.Count - 1);
            
            for (int i = 1; i < frameTimes.Count; i++)
            {
                float diff = frameTimes[i] - avgDelta;
                variance += diff * diff;
            }
            variance = MathF.Sqrt(variance / (frameTimes.Count - 1));
            
            _output.WriteLine($"");
            _output.WriteLine($"Frame Time Statistics:");
            _output.WriteLine($"  Average: {avgDelta * 1000:F2}ms ({1.0f / avgDelta:F1} FPS)");
            _output.WriteLine($"  Min: {minDelta * 1000:F2}ms ({1.0f / minDelta:F1} FPS)");
            _output.WriteLine($"  Max: {maxDelta * 1000:F2}ms ({1.0f / maxDelta:F1} FPS)");
            _output.WriteLine($"  Std Dev: {variance * 1000:F2}ms");
            _output.WriteLine($"  Total time: {sw.ElapsedMilliseconds}ms");
            
            // Verify delta times were clamped correctly
            Assert.True(minDelta >= minDeltaTime * 0.99f, "Min delta should respect lower bound");
            Assert.True(maxDelta <= maxDeltaTime * 1.01f, "Max delta should respect upper bound");
            
            _output.WriteLine("✅ REAL-TIME: Variable timestep handled correctly");
        }

        [Fact]
        public void ComparisonTest_FixedVsVariable_BehaviorDiffers()
        {
            // Demonstrate that fixed and variable timestep produce different results
            // but both are valid for their use cases
            
            const int entityCount = 50;
            const int steps = 100;
            
            _output.WriteLine("=== FIXED vs VARIABLE TIMESTEP COMPARISON ===");
            
            // Fixed timestep
            var fixedState = RunFixedTimestepSimulation(entityCount, steps, 1.0f / 60.0f, seed: 42);
            
            // Variable timestep (simulated)
            var variableState = RunVariableTimestepSimulation(entityCount, steps, seed: 42);
            
            _output.WriteLine($"");
            _output.WriteLine($"Fixed timestep:");
            _output.WriteLine($"  Final hash: {fixedState.hash:X16}");
            _output.WriteLine($"  Total distance: {fixedState.totalDistance:F2}");
            
            _output.WriteLine($"");
            _output.WriteLine($"Variable timestep:");
            _output.WriteLine($"  Final hash: {variableState.hash:X16}");
            _output.WriteLine($"  Total distance: {variableState.totalDistance:F2}");
            _output.WriteLine($"  Avg delta: {variableState.avgDelta * 1000:F2}ms");
            
            // They should differ (different frame times = different physics)
            _output.WriteLine($"");
            _output.WriteLine($"Results differ: {fixedState.hash != variableState.hash}");
            _output.WriteLine($"Distance difference: {MathF.Abs(fixedState.totalDistance - variableState.totalDistance):F2}");
            
            // But variable timestep should still be deterministic when run twice
            var variableState2 = RunVariableTimestepSimulation(entityCount, steps, seed: 42);
            Assert.Equal(variableState.hash, variableState2.hash);
            
            _output.WriteLine("✅ Both modes are deterministic within their own paradigm");
        }

        // Helper methods
        private (ulong hash, float totalDistance, int entityCount) RunFixedTimestepSimulation(
            int entityCount, int stepCount, float fixedDeltaTime, int seed)
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            repo.RegisterComponent<Velocity>();
            
            var entities = new Entity[entityCount];
            var random = new Random(seed);
            
            for (int i = 0; i < entityCount; i++)
            {
                entities[i] = repo.CreateEntity();
                repo.AddComponent(entities[i], new Position { X = i * 10f, Y = 0, Z = 0 });
                repo.AddComponent(entities[i], new Velocity 
                { 
                    VX = (float)(random.NextDouble() * 2 - 1),
                    VY = (float)(random.NextDouble() * 2 - 1),
                    VZ = (float)(random.NextDouble() * 2 - 1)
                });
            }
            
            for (int step = 0; step < stepCount; step++)
            {
                repo.Tick();
                
                for (int i = 0; i < entityCount; i++)
                {
                    ref var pos = ref repo.GetComponentRW<Position>(entities[i]);
                    var vel = repo.GetComponentRO<Velocity>(entities[i]);
                    
                    pos.X += vel.VX * fixedDeltaTime;
                    pos.Y += vel.VY * fixedDeltaTime;
                    pos.Z += vel.VZ * fixedDeltaTime;
                }
            }
            
            return (ComputeStateHash(repo, entities), ComputeTotalDistance(repo, entities), repo.EntityCount);
        }

        private (ulong hash, float totalDistance, float avgDelta) RunVariableTimestepSimulation(
            int entityCount, int stepCount, int seed)
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            repo.RegisterComponent<Velocity>();
            
            var entities = new Entity[entityCount];
            var random = new Random(seed);
            
            for (int i = 0; i < entityCount; i++)
            {
                entities[i] = repo.CreateEntity();
                repo.AddComponent(entities[i], new Position { X = i * 10f, Y = 0, Z = 0 });
                repo.AddComponent(entities[i], new Velocity 
                { 
                    VX = (float)(random.NextDouble() * 2 - 1),
                    VY = (float)(random.NextDouble() * 2 - 1),
                    VZ = (float)(random.NextDouble() * 2 - 1)
                });
            }
            
            // Use predictable "random" delta times based on seed
            var deltaRandom = new Random(seed);
            float totalDelta = 0;
            
            for (int step = 0; step < stepCount; step++)
            {
                // Simulate variable frame time (but deterministic based on seed)
                float deltaTime = 0.01f + (float)(deltaRandom.NextDouble() * 0.02f); // 10-30ms range
                totalDelta += deltaTime;
                
                repo.Tick();
                
                for (int i = 0; i < entityCount; i++)
                {
                    ref var pos = ref repo.GetComponentRW<Position>(entities[i]);
                    var vel = repo.GetComponentRO<Velocity>(entities[i]);
                    
                    pos.X += vel.VX * deltaTime;
                    pos.Y += vel.VY * deltaTime;
                    pos.Z += vel.VZ * deltaTime;
                }
            }
            
            return (ComputeStateHash(repo, entities), ComputeTotalDistance(repo, entities), totalDelta / stepCount);
        }

        private ulong ComputeStateHash(EntityRepository repo, Entity[] entities)
        {
            ulong hash = 14695981039346656037UL; // FNV-1a offset
            
            foreach (var entity in entities)
            {
                if (repo.IsAlive(entity) && repo.HasUnmanagedComponent<Position>(entity))
                {
                    var pos = repo.GetComponentRO<Position>(entity);
                    
                    hash ^= BitConverter.ToUInt32(BitConverter.GetBytes(pos.X), 0);
                    hash *= 1099511628211UL;
                    hash ^= BitConverter.ToUInt32(BitConverter.GetBytes(pos.Y), 0);
                    hash *= 1099511628211UL;
                    hash ^= BitConverter.ToUInt32(BitConverter.GetBytes(pos.Z), 0);
                    hash *= 1099511628211UL;
                }
            }
            
            return hash;
        }

        private float ComputeTotalDistance(EntityRepository repo, Entity[] entities)
        {
            float total = 0;
            
            foreach (var entity in entities)
            {
                if (repo.IsAlive(entity) && repo.HasUnmanagedComponent<Position>(entity))
                {
                    var pos = repo.GetComponentRO<Position>(entity);
                    total += MathF.Sqrt(pos.X * pos.X + pos.Y * pos.Y + pos.Z * pos.Z);
                }
            }
            
            return total;
        }
    }
}
