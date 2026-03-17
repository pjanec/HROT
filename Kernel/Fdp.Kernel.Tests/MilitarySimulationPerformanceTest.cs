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
    /// Realistic military simulation performance test.
    /// Combines humans (managed health), vehicles (multi-part),
    /// events (explosions/fire), and particles (environmental effects).
    /// </summary>
    public class MilitarySimulationPerformanceTest : IDisposable
    {
        private readonly ITestOutputHelper _output;
        private readonly string _testFilePath;
        
        public MilitarySimulationPerformanceTest(ITestOutputHelper output)
        {
            _output = output;
            _testFilePath = Path.Combine(Path.GetTempPath(), $"military_sim_{Guid.NewGuid()}.fdp");
        }
        
        public void Dispose()
        {
            try { File.Delete(_testFilePath); } catch {}
        }

        // Lightweight components (particles - birds, smoke, debris)
        [ComponentId(233)]
        public struct ParticleState
        {
            public float X, Y, Z;
            public float VX, VY, VZ;
            public float LifeTime;
        }

        // Medium components (human soldiers)
        [ComponentId(234)]
        public struct Transform
        {
            public float X, Y, Z;
            public float RotationY;
        }

        [MessagePackObject]
        [ComponentId(235)]
        public record SoldierData
        {
            [Key(0)]
            public string Name { get; set; } = "";
            
            [Key(1)]
            public string Rank { get; set; } = "";
            
            [Key(2)]
            public int Health { get; set; }
            
            [Key(3)]
            public int Ammo { get; set; }
            
            [Key(4)]
            public string[] Equipment { get; set; } = Array.Empty<string>();
        }

        // Heavy components (vehicles with multi-part systems)
        [MessagePackObject]
        [ComponentId(236)]
        public record VehicleData
        {
            [Key(0)]
            public string Type { get; set; } = "";
            
            [Key(1)]
            public int Armor { get; set; }
            
            [Key(2)]
            public int Fuel { get; set; }
            
            [Key(3)]
            public WheelState[] Wheels { get; set; } = Array.Empty<WheelState>();
            
            [Key(4)]
            public WeaponSystem[] Weapons { get; set; } = Array.Empty<WeaponSystem>();
        }

        [MessagePackObject]
        public record WheelState
        {
            [Key(0)]
            public float Health { get; set; }
            
            [Key(1)]
            public float Pressure { get; set; }
            
            [Key(2)]
            public bool Damaged { get; set; }
        }

        [MessagePackObject]
        public record WeaponSystem
        {
            [Key(0)]
            public string Type { get; set; } = "";
            
            [Key(1)]
            public int Ammo { get; set; }
            
            [Key(2)]
            public float Cooldown { get; set; }
        }

        // Events
        [EventId(1)]
        public struct ExplosionEvent
        {
            public float X, Y, Z;
            public float Radius;
            public int Damage;
        }

        [EventId(2)]
        public struct FireEvent
        {
            public Entity TargetEntity;
            public Entity SourceEntity;
            public int Damage;
        }

        [Fact]
        public void RealisticMilitrarySimulation_CompleteScenario_MeasuresPerformance()
        {
            // Realistic scenario:
            // - 500 soldiers (managed health/equipment)
            // - 50 vehicles (heavy multi-part with wheels/weapons)
            // - 5000 environmental particles (birds, smoke, debris)
            // - Continuous events (explosions, fire)
            
            const int soldierCount = 500;
            const int vehicleCount = 50;
            const int particleCount = 5000;
            const int frameCount = 300;
            const int keyframeInterval = 30;
            
            _output.WriteLine($"=== REALISTIC MILITARY SIMULATION ===");
            _output.WriteLine($"Soldiers: {soldierCount}");
            _output.WriteLine($"Vehicles: {vehicleCount} (with 4 wheels + 2 weapons each)");
            _output.WriteLine($"Particles: {particleCount} (birds/smoke/debris)");
            _output.WriteLine($"Frames: {frameCount}");
            _output.WriteLine($"");
            
            using var repo = new EntityRepository();
            
            // Register components
            repo.RegisterComponent<Transform>();
            repo.RegisterComponent<ParticleState>();
            repo.RegisterComponent<SoldierData>();
            repo.RegisterComponent<VehicleData>();
            
            
            // Create event bus (separate from EntityRepository for now)
            // Note: Event bus recording/playback integration with Flight Recorder is future work
            using var eventBus = new FdpEventBus();
            
            var soldiers = new Entity[soldierCount];
            var vehicles = new Entity[vehicleCount];
            var particles = new Entity[particleCount];
            
            // Create soldiers (medium complexity)
            for (int i = 0; i < soldierCount; i++)
            {
                soldiers[i] = repo.CreateEntity();
                repo.AddComponent(soldiers[i], new Transform
                {
                    X = (i % 50) * 10f,
                    Y = 0,
                    Z = (i / 50) * 10f,
                    RotationY = 0
                });
                repo.AddManagedComponent(soldiers[i], new SoldierData
                {
                    Name = $"Soldier{i:D3}",
                    Rank = i % 10 == 0 ? "Sergeant" : "Private",
                    Health = 100,
                    Ammo = 120,
                    Equipment = new[] { "Rifle", "Helmet", "Radio" }
                });
            }
            
            // Create vehicles (heavy complexity - multi-part)
            for (int i = 0; i < vehicleCount; i++)
            {
                vehicles[i] = repo.CreateEntity();
                repo.AddComponent(vehicles[i], new Transform
                {
                    X = (i % 10) * 50f,
                    Y = 0,
                    Z = (i / 10) * 50f,
                    RotationY = 0
                });
                repo.AddManagedComponent(vehicles[i], new VehicleData
                {
                    Type = i % 3 == 0 ? "Tank" : (i % 3 == 1 ? "APC" : "Truck"),
                    Armor = i % 3 == 0 ? 100 : 50,
                    Fuel = 100,
                    Wheels = new[]
                    {
                        new WheelState { Health = 100, Pressure = 35, Damaged = false },
                        new WheelState { Health = 100, Pressure = 35, Damaged = false },
                        new WheelState { Health = 100, Pressure = 35, Damaged = false },
                        new WheelState { Health = 100, Pressure = 35, Damaged = false }
                    },
                    Weapons = new[]
                    {
                        new WeaponSystem { Type = "Cannon", Ammo = 40, Cooldown = 0 },
                        new WeaponSystem { Type = "MG", Ammo = 500, Cooldown = 0 }
                    }
                });
            }
            
            // Create particles (lightweight - environmental)
            for (int i = 0; i < particleCount; i++)
            {
                particles[i] = repo.CreateEntity();
                repo.AddComponent(particles[i], new ParticleState
                {
                    X = (float)(new Random(i).NextDouble() * 1000),
                    Y = (float)(new Random(i + 1000).NextDouble() * 50),
                    Z = (float)(new Random(i + 2000).NextDouble() * 1000),
                    VX = (float)(new Random(i + 3000).NextDouble() * 2 - 1),
                    VY = (float)(new Random(i + 4000).NextDouble() * 2 - 1),
                    VZ = (float)(new Random(i + 5000).NextDouble() * 2 - 1),
                    LifeTime = 10f
                });
            }
            
            _output.WriteLine($"Setup complete. Total entities: {soldierCount + vehicleCount + particleCount}");
            _output.WriteLine($"Starting simulation...");
            _output.WriteLine($"");
            
            var sw = Stopwatch.StartNew();
            int totalExplosions = 0;
            int totalFireEvents = 0;
            int soldierCasualties = 0;
            int vehiclesDestroyed = 0;
            
            using (var recorder = new AsyncRecorder(_testFilePath))
            {
                var random = new Random(42);
                
                for (int frame = 0; frame < frameCount; frame++)
                {
                    repo.Tick();
                    
                    // Update particles (lightweight, high frequency)
                    for (int i = 0; i < particleCount; i++)
                    {
                        if (!repo.IsAlive(particles[i])) continue;
                        
                        ref var particle = ref repo.GetComponentRW<ParticleState>(particles[i]);
                        particle.X += particle.VX;
                        particle.Y += particle.VY;
                        particle.Z += particle.VZ;
                        particle.LifeTime -= 0.1f;
                        
                        // Particle death and respawn
                        if (particle.LifeTime <= 0 && frame % 10 == 0)
                        {
                            repo.DestroyEntity(particles[i]);
                            particles[i] = repo.CreateEntity();
                            repo.AddComponent(particles[i], new ParticleState
                            {
                                X = random.Next(1000),
                                Y = random.Next(50),
                                Z = random.Next(1000),
                                VX = (float)(random.NextDouble() * 2 - 1),
                                VY = (float)(random.NextDouble() * 2 - 1),
                                VZ = (float)(random.NextDouble() * 2 - 1),
                                LifeTime = 10f
                            });
                        }
                    }
                    
                    // Update soldiers (medium frequency)
                    for (int i = 0; i < soldierCount; i++)
                    {
                        if (!repo.IsAlive(soldiers[i])) continue;
                        
                        ref var transform = ref repo.GetComponentRW<Transform>(soldiers[i]);
                        transform.X += (float)Math.Sin(frame * 0.1f) * 0.5f;
                        transform.Z += (float)Math.Cos(frame * 0.1f) * 0.5f;
                        
                        // Update soldier data occasionally
                        if (frame % 10 == 0)
                        {
                            var soldier = repo.GetComponentRW<SoldierData>(soldiers[i]);
                            soldier.Ammo = Math.Max(0, soldier.Ammo - random.Next(5));
                        }
                    }
                    
                    // Update vehicles (low frequency, heavy data)
                    if (frame % 5 == 0)
                    {
                        for (int i = 0; i < vehicleCount; i++)
                        {
                            if (!repo.IsAlive(vehicles[i])) continue;
                            
                            ref var transform = ref repo.GetComponentRW<Transform>(vehicles[i]);
                            transform.X += 0.2f;
                            transform.RotationY += 0.01f;
                            
                            var vehicle = repo.GetComponentRW<VehicleData>(vehicles[i]);
                            vehicle.Fuel = Math.Max(0, vehicle.Fuel - 1);
                            
                            // Wheel degradation
                            if (random.Next(100) < 5)
                            {
                                int wheelIdx = random.Next(vehicle.Wheels.Length);
                                vehicle.Wheels[wheelIdx].Pressure -= 1;
                                if (vehicle.Wheels[wheelIdx].Pressure < 20)
                                {
                                    vehicle.Wheels[wheelIdx].Damaged = true;
                                }
                            }
                        }
                    }
                    
                    // Simulate combat events
                    if (frame % 20 == 0)
                    {
                        // Explosion event
                        var explosionPos = new { X = random.Next(1000), Y = 0f, Z = random.Next(1000) };
                        eventBus.Publish(new ExplosionEvent
                        {
                            X = explosionPos.X,
                            Y = explosionPos.Y,
                            Z = explosionPos.Z,
                            Radius = 50,
                            Damage = 50
                        });
                        totalExplosions++;
                        
                        // Damage nearby soldiers
                        for (int i = 0; i < soldierCount; i++)
                        {
                            if (!repo.IsAlive(soldiers[i])) continue;
                            
                            var transform = repo.GetComponentRO<Transform>(soldiers[i]);
                            float dist = MathF.Sqrt(
                                (transform.X - explosionPos.X) * (transform.X - explosionPos.X) +
                                (transform.Z - explosionPos.Z) * (transform.Z - explosionPos.Z));
                            
                            if (dist < 50)
                            {
                                var soldier = repo.GetComponentRW<SoldierData>(soldiers[i]);
                                soldier.Health -= 25;
                                
                                if (soldier.Health <= 0)
                                {
                                    repo.DestroyEntity(soldiers[i]);
                                    soldierCasualties++;
                                }
                            }
                        }
                    }
                    
                    // Fire events
                    if (frame % 30 == 0)
                    {
                        for (int i = 0; i < 10; i++)
                        {
                            int sourceSoldier = random.Next(soldierCount);
                            int targetSoldier = random.Next(soldierCount);
                            
                            if (repo.IsAlive(soldiers[sourceSoldier]) && repo.IsAlive(soldiers[targetSoldier]))
                            {
                                eventBus.Publish(new FireEvent
                                {
                                    SourceEntity = soldiers[sourceSoldier],
                                    TargetEntity = soldiers[targetSoldier],
                                    Damage = 10
                                });
                                totalFireEvents++;
                            }
                        }
                    }
                    
                    // Record frame
                    if (frame % keyframeInterval == 0)
                        recorder.CaptureKeyframe(repo, DateTime.UtcNow.Ticks, blocking: true);
                    else
                        recorder.CaptureFrame(repo, repo.GlobalVersion - 1, DateTime.UtcNow.Ticks, blocking: true);
                }
            }
            
            sw.Stop();
            var fileSize = new FileInfo(_testFilePath).Length;
            double fps = frameCount / sw.Elapsed.TotalSeconds;
            int totalEntities = soldierCount + vehicleCount + particleCount;
            double entityFramesPerSec = (totalEntities * frameCount) / sw.Elapsed.TotalSeconds;
            
            _output.WriteLine($"");
            _output.WriteLine($"=== RESULTS ===");
            _output.WriteLine($"Simulation Time: {sw.ElapsedMilliseconds}ms ({sw.Elapsed.TotalSeconds:F1}s)");
            _output.WriteLine($"Recording FPS: {fps:F1}");
            _output.WriteLine($"File Size: {fileSize / (1024.0 * 1024.0):F2} MB");
            _output.WriteLine($"Throughput: {entityFramesPerSec:N0} entity-frames/sec");
            _output.WriteLine($"");
            _output.WriteLine($"=== SIMULATION STATISTICS ===");
            _output.WriteLine($"Total Explosions: {totalExplosions}");
            _output.WriteLine($"Total Fire Events: {totalFireEvents}");
            _output.WriteLine($"Soldier Casualties: {soldierCasualties}");
            _output.WriteLine($"Vehicles Destroyed: {vehiclesDestroyed}");
            _output.WriteLine($"");
            
            // Performance assertions
            Assert.True(fps > 10, $"Realistic simulation should achieve > 10 FPS (got {fps:F1})");
            Assert.True(sw.ElapsedMilliseconds < 60000, "Simulation should complete in < 60 seconds");
            
            // Now test playback performance
            _output.WriteLine($"=== PLAYBACK PERFORMANCE ===");
            
            using var playbackRepo = new EntityRepository();
            playbackRepo.RegisterComponent<Transform>();
            playbackRepo.RegisterComponent<ParticleState>();
            playbackRepo.RegisterComponent<SoldierData>();
            playbackRepo.RegisterComponent<VehicleData>();
            using var playbackEventBus = new FdpEventBus();
            
            using var controller = new PlaybackController(_testFilePath);
            
            // Test seeking
            var seekSw = Stopwatch.StartNew();
            controller.SeekToFrame(playbackRepo, frameCount / 2);
            seekSw.Stop();
            _output.WriteLine($"Seek to middle: {seekSw.ElapsedMilliseconds}ms");
            
            seekSw.Restart();
            controller.SeekToFrame(playbackRepo, 0);
            seekSw.Stop();
            _output.WriteLine($"Rewind to start: {seekSw.ElapsedMilliseconds}ms");
            
            // Test full playback
            var playbackSw = Stopwatch.StartNew();
            while (!controller.IsAtEnd)
            {
                controller.StepForward(playbackRepo);
            }
            playbackSw.Stop();
            
            double playbackFps = frameCount / playbackSw.Elapsed.TotalSeconds;
            _output.WriteLine($"Full playback: {playbackSw.ElapsedMilliseconds}ms ({playbackFps:F1} FPS)");
            
            Assert.True(playbackFps > fps, "Playback should be faster than recording");
        }
    }
}
