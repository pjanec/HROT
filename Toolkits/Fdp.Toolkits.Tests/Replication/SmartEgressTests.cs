using Xunit;
using Fdp.Kernel;
using Fdp.Toolkit.Replication.Systems;
using Fdp.Toolkit.Replication.Components;

namespace Fdp.Toolkit.Replication.Tests
{
    public class SmartEgressTests
    {
        private void InitializeSystem(SmartEgressSystem system, EntityRepository world)
        {
            world.RegisterManagedComponent<EgressPublicationState>();
            system.Execute(world, 0f); // sets internal _world field
        }

        [Fact]
        public void ShouldPublish_ReliableWithChanges_ReturnsTrue()
        {
            var system = new SmartEgressSystem();
            var world = new EntityRepository();
            InitializeSystem(system, world);
            
            var entity = world.CreateEntity();
            
            // Reliable, Changed
            bool result = system.ShouldPublishDescriptor(entity, 123, 100, false, 2, 1);
            Assert.True(result);
        }

        [Fact]
        public void ShouldPublish_Unreliable_Throttles()
        {
            var system = new SmartEgressSystem();
            var world = new EntityRepository();
            InitializeSystem(system, world);
            
            var entity = world.CreateEntity(); 
            long id = entity.Index;
            uint salt = (uint)(id % 600);
            
            // Find a tick that does NOT trigger
            uint nonTriggerTick = (600 - salt + 1) % 600;
            // Ensure nonTrigger phase is not 0
            if ((nonTriggerTick + salt) % 600 == 0) nonTriggerTick++;
            
            Assert.False(system.ShouldPublishDescriptor(entity, 123, nonTriggerTick, true, 2, 1));
            
            // Find a tick that DOES trigger
            // (tick + salt) % 600 == 0 => tick + salt = 600
            uint triggerTick = 600 - salt;
            
            Assert.True(system.ShouldPublishDescriptor(entity, 123, triggerTick, true, 2, 1));
        }

        [Fact]
        public void ShouldPublish_UpdatesPublicationState()
        {
            var system = new SmartEgressSystem();
            var world = new EntityRepository();
            InitializeSystem(system, world);
            
            var entity = world.CreateEntity();
            long key = 999;
            long id = entity.Index;
            uint salt = (uint)(id % 600);
            uint triggerTick = 600 - salt;

            Assert.True(system.ShouldPublishDescriptor(entity, key, triggerTick, true, 2, 1));

            // Verify state updated
            Assert.True(world.HasManagedComponent<EgressPublicationState>(entity));
            var state = world.GetComponent<EgressPublicationState>(entity);
            Assert.True(state.LastPublishedTickMap.ContainsKey(key));
            Assert.Equal(triggerTick, state.LastPublishedTickMap[key]);
        }
    }
}
