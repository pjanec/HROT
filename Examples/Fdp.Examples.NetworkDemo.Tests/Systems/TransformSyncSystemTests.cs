using System;
using System.Numerics;
using Fdp.Examples.NetworkDemo.Components;
using Fdp.Examples.NetworkDemo.Systems;
using Fdp.Kernel;
using Fdp.Toolkit.Replication.Components;

using Fdp.Toolkit.Replication.Extensions;
using Fdp.ModuleHost.Abstractions;
using Xunit;

namespace Fdp.Examples.NetworkDemo.Tests.Systems
{
    public class TransformSyncSystemTests
    {
        [Fact]
        public void TransformSync_Owned_CopiesToBuffer()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<SimTransform>();
            repo.RegisterComponent<NetworkTransform>();
            repo.RegisterComponent<NetworkAuthority>();

            var entity = repo.CreateEntity();
            
            repo.AddComponent(entity, new SimTransform { Position = new Vector3(10, 0, 0) });
            repo.AddComponent(entity, new NetworkTransform());
            
            // Set Authority - LocalNodeId == PrimaryOwnerId (e.g. 1 == 1)
            repo.AddComponent(entity, new NetworkAuthority(1, 1));

            var system = new TransformSyncSystem();
            system.Execute(repo, 0.016f); // EntityRepository acts as ISimulationView

            // Playback commands
            var view = (ISimulationView)repo;
            if (view.GetCommandBuffer() is EntityCommandBuffer ecb)
            {
                ecb.Playback(repo);
            }

            var netTf = repo.GetComponent<NetworkTransform>(entity);
            Assert.Equal(new Vector3(10, 0, 0), netTf.LastPosition);
        }

        [Fact]
        public void TransformSync_Remote_SmoothsPosition()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<SimTransform>();
            repo.RegisterComponent<NetworkTransform>();
            repo.RegisterComponent<NetworkAuthority>();

            var entity = repo.CreateEntity();
            
            // Initial State
            // DemoPosition is at origin
            repo.AddComponent(entity, new SimTransform { Position = new Vector3(0, 0, 0) });
            // Network (Target) is at 10
            repo.AddComponent(entity, new NetworkTransform { LastPosition = new Vector3(10, 0, 0) });
            
            // Remote Authority - LocalNodeId (1) != PrimaryOwnerId (2)
            repo.AddComponent(entity, new NetworkAuthority(2, 1)); 

            var system = new TransformSyncSystem();
            
            // Use deltaTime = 0.05f with Smoothing Rate 10.0f
            // t = 0.05 * 10 = 0.5
            // Lerp(0, 10, 0.5) = 5.0
            float dt = 0.05f;
            system.Execute(repo, dt); // EntityRepository acts as ISimulationView

            // Playback commands
            var view = (ISimulationView)repo;
            if (view.GetCommandBuffer() is EntityCommandBuffer ecb)
            {
                ecb.Playback(repo);
            }

            var appPos = repo.GetComponent<SimTransform>(entity);
            
            Assert.Equal(5.0f, appPos.Position.X, 0.01f);
        }
    }
}
