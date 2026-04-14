using System;
using System.Collections.Generic;
using System.Numerics;
using Xunit;
using Fdp.Examples.NetworkDemo.Modules;
using Fdp.Examples.NetworkDemo.Systems;
using Fdp.Examples.NetworkDemo.Events;
using Fdp.Examples.NetworkDemo.Components;
using Fdp.ModuleHost.Abstractions;
using Fdp.Core;
using Fdp.Toolkit.Replication.Components;


namespace Fdp.Examples.NetworkDemo.Tests.Modules
{
    public class MockEventBus : IEventBus
    {
        public List<object> PublishedEvents = new List<object>();

        // Must match interface constraints exactly
        public void Publish<T>(T eventData) where T : unmanaged
        {
            PublishedEvents.Add(eventData);
        }
        
        public void PublishManaged<T>(T eventData)
        {
            PublishedEvents.Add(eventData);
        }

        public void Subscribe<T>(Action<T> handler) where T : struct {} // This signature might not exist in IEventBus?
        public void Unsubscribe<T>(Action<T> handler) where T : struct {}
    }

    public class AdvancedModulesTests : IDisposable
    {
        private EntityRepository _world;

        public AdvancedModulesTests()
        {
            _world = new EntityRepository();
            // Register components
            _world.RegisterComponent<SimTransform>();
            _world.RegisterComponent<NetworkIdentity>();
            _world.RegisterComponent<Health>();
        }

        public void Dispose()
        {
            _world.Dispose();
        }

        [Fact]
        public void RadarSystem_DetectsNearbyEntites_PublishesEvent()
        {
            // Arrange
            var mockBus = new MockEventBus();
            var system = new RadarSystem(mockBus);

            // Create target entity
            var e = _world.CreateEntity();
            _world.AddComponent(e, new SimTransform { Position = new Vector3(10, 0, 0) });
            _world.AddComponent(e, new NetworkIdentity { Value = 123 });

            // Act
            // Execute system directly (timing is handled by ModuleHost Kernel via Policy)
            system.Execute(_world, 0.1f);

            // Assert
            Assert.Single(mockBus.PublishedEvents);
            var evt = (RadarContactEvent)mockBus.PublishedEvents[0];
            Assert.Equal(123, evt.EntityId);
        }

        [Fact]
        public void DamageControlModule_OnDetonation_ReducesHealth()
        {
            // Arrange
            var module = new DamageControlModule();

            // Create victim entity
            var e = _world.CreateEntity();
            _world.AddComponent(e, new SimTransform { Position = new Vector3(10, 0, 0) });
            _world.AddComponent(e, new Health { Value = 100 });

            // Publish Detonation Event
            // Radius 20 covers distance 10. Damage 50.
            // Damage = 50 * (1 - 10/20) = 50 * 0.5 = 25.
            // Result Health = 75.
            _world.Bus.Publish(new DetonationEvent { 
                Position = Vector3.Zero, 
                Radius = 20, 
                Damage = 50 
            });
            _world.Bus.SwapBuffers(); // Make events available for consumption

            // Act
            module.Execute(_world, 0.1f);

            // Apply command buffer
            var cmd = ((ISimulationView)_world).GetCommandBuffer();
            ((EntityCommandBuffer)cmd).Playback(_world);

            // Assert
            var health = _world.GetComponent<Health>(e);
            Assert.Equal(75, health.Value); // Floating point comparison might be finicky, but integers 75 should be exact here.
        }
    }
}
