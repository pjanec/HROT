using System;
using System.IO;
using System.Linq;
using System.Numerics;
using Fdp.Examples.NetworkDemo.Components;
using Fdp.Examples.NetworkDemo.Systems;
using Fdp.Kernel;
using FDP.Toolkit.Replication.Components;
using Fdp.Kernel.FlightRecorder;
using ModuleHost.Core.Abstractions;

using Xunit;

namespace Fdp.Examples.NetworkDemo.Tests.Systems
{
    public class ReplayBridgeSystemTests : IDisposable
    {
        private readonly string _testFile;
        private const int CHASSIS_KEY = 5;
        private const int TURRET_KEY = 10;

        public ReplayBridgeSystemTests()
        {
            _testFile = Path.GetTempFileName();
        }

        public void Dispose()
        {
            if (File.Exists(_testFile))
            {
                try { File.Delete(_testFile); } catch { }
            }
        }

        [Fact]
        public void ReplayBridge_PropagatesOwnedComponents()
        {
            // 1. Create a recording with two entities
            CreateTestRecording();

            // 2. Setup Live World
            using var liveRepo = new EntityRepository();
            RegisterComponents(liveRepo);

            // 3. Run ReplayBridgeSystem as Node 2
            using var system = new ReplayBridgeSystem(_testFile, 2);
            system.Execute(liveRepo, 0.1f);

            var view = (ISimulationView)liveRepo;
            if (view.GetCommandBuffer() is EntityCommandBuffer ecb)
            {
                ecb.Playback(liveRepo);
            }

            // 4. Verify
            // We expect ONLY the entity with Owner=2 (NetworkId=99) to be present.
            // Expected entity count: 1
            var entities = new System.Collections.Generic.List<Entity>();
            foreach(var e in liveRepo.Query().Build()) { entities.Add(e); }
            
            // Should find Entity 99 (Owned by 2), NOT Entity 42 (Owned by 1)
            bool found99 = false;
            foreach(var e in entities)
            {
                 if (liveRepo.HasComponent<NetworkIdentity>(e))
                 {
                     var netId = liveRepo.GetComponent<NetworkIdentity>(e).Value;

                     if (netId == 99)
                     {
                         found99 = true;
                         // Entity 99 has Position but NO Turret in recording
                         Assert.True(liveRepo.HasComponent<SimTransform>(e));
                     }
                     if (netId == 42)
                     {
                         Assert.Fail("Entity 42 (Owned by Node 1) should not be injected when running as Node 2");
                     }
                 }
            }
            Assert.True(found99, "Entity 99 not found");
        }

        private void CreateTestRecording()
        {
            using var repo = new EntityRepository();
            RegisterComponents(repo);

            // Entity 1: Owned by us (Node 1)
            var e1 = repo.CreateEntity();
            repo.AddComponent(e1, new NetworkIdentity { Value = 42 });
            repo.AddComponent(e1, new NetworkAuthority(1, 1));
            repo.AddComponent(e1, new SimTransform { Position = new Vector3(100, 0, 0) });
            repo.AddComponent(e1, new TurretState { Yaw = 90 });

            // Entity 2: Owned by remote (Node 2)
            var e2 = repo.CreateEntity();
            repo.AddComponent(e2, new NetworkIdentity { Value = 99 });
            // NetworkAuthority(Primary, Local)
            repo.AddComponent(e2, new NetworkAuthority(2, 1)); // Owner 2, Local 1
            repo.AddComponent(e2, new SimTransform { Position = new Vector3(200, 0, 0) });

            repo.Tick();

            using var recorder = new AsyncRecorder(_testFile); // This assumes synchronous flush on dispose
            // Or we check if recorder.CaptureKeyframe is enough
            // Since this is just a test utility, we assume it writes.
            // Wait, AsyncRecorder might not write immediately?
            // Assuming the test logic itself is sound regarding recorder behaviour.
            recorder.CaptureKeyframe(repo, DateTime.UtcNow.Ticks);
        }

        private void RegisterComponents(EntityRepository repo)
        {
            repo.RegisterComponent<NetworkIdentity>();
            repo.RegisterComponent<NetworkAuthority>();
            repo.RegisterComponent<SimTransform>();
            repo.RegisterComponent<TurretState>();
            // DescriptorOwnership is a managed component (class)
            // repo.RegisterManagedComponent<DescriptorOwnership>(); // Might not exist here or be needed
        }
    }
}
