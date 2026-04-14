using System;
using Xunit;
using FDP.Toolkit.Lifecycle.Systems;
using Fdp.Kernel;
using Fdp.ModuleHost.Core.Abstractions;

namespace FDP.Toolkit.Lifecycle.Tests.Systems
{
    [DataPolicy(DataPolicy.Transient)]
    [ComponentId(245)]
    public struct TestTransientComponent
    {
        public int Value;
    }

    public class LifecycleCleanupSystemTests
    {
        [Fact]
        public void Execute_RemovesTransientComponents_WhenActive()
        {
            // Ensure component is registered so system can find it
            var typeId = ComponentType<TestTransientComponent>.ID;

            var repo = new EntityRepository();
            repo.RegisterComponent<TestTransientComponent>();
            var system = new LifecycleCleanupSystem();
            
            // Create Active entity
            var entity = repo.CreateEntity();
            repo.SetLifecycleState(entity, EntityLifecycle.Active);
            
            // Add transient component
            repo.AddComponent(entity, new TestTransientComponent { Value = 99 });
            
            // Run system
            system.Execute(repo, 0.1f);
            
            // Playback commands
            // System records removals to command buffer
            var cmd = (EntityCommandBuffer) ((ISimulationView)repo).GetCommandBuffer();
            cmd.Playback(repo);
            
            // Assert
            Assert.False(repo.HasComponent<TestTransientComponent>(entity), "Transient component should be removed");
        }
    }
}
