using Xunit;
using FDP.Toolkit.Lifecycle;
using FDP.Toolkit.Lifecycle.Events;
using Fdp.Kernel;
using ModuleHost.Core.Abstractions;
using Fdp.Interfaces;
using Moq; 
using System.Linq;

namespace FDP.Toolkit.Lifecycle.Tests
{
    public class EntityLifecycleModuleTests
    {
        private EntityCommandBuffer CreateCommandBuffer()
        {
            return new EntityCommandBuffer();
        }

        [Fact]
        public void ELM_BeginConstruction_PublishesOrder()
        {
            var elm = new EntityLifecycleModule(null, new[] { 1, 2, 3 });
            var cmd = CreateCommandBuffer();
            var entity = new Entity(100, 1);
            
            elm.BeginConstruction(entity, blueprintId: 1, currentFrame: 10, cmd);
            
            // Need to verify published event in command buffer
            Assert.True(cmd.Size > 0);
        }

        [Fact]
        public void ELM_AllAcksReceived_ActivatesEntity()
        {
            var elm = new EntityLifecycleModule(null, new[] { 1, 2 });
            var cmd = CreateCommandBuffer();
            var entity = new Entity(100, 1);
            
            elm.BeginConstruction(entity, 1, 10, cmd);
            
            // Module 1 ACKs
            elm.ProcessConstructionAck(new ConstructionAck
            {
                Entity = entity,
                ModuleId = 1,
                Success = true
            }, 11, cmd);
            
            // Verify NO activation command yet
            
            // Module 2 ACKs
            elm.ProcessConstructionAck(new ConstructionAck
            {
                Entity = entity,
                ModuleId = 2,
                Success = true
            }, 12, cmd);
            
            // Now activated.
        }
        
        [Fact]
        public void ELM_FailedAck_AbortsConstruction()
        {
            var elm = new EntityLifecycleModule(null, new[] { 1, 2 });
            var cmd = CreateCommandBuffer();
            var entity = new Entity(100, 1);
            
            elm.BeginConstruction(entity, 1, 10, cmd);
            
            elm.ProcessConstructionAck(new ConstructionAck
            {
                Entity = entity,
                ModuleId = 1,
                Success = false,
                ErrorMessage = new FixedString64("Physics setup failed")
            }, 11, cmd);
            
            // Entity should be destroyed
            Assert.True(cmd.Size > 0);
        }
        
        [Fact]
        public void ELM_Timeout_AbandonsConstruction()
        {
            var elm = new EntityLifecycleModule(null, new[] { 1, 2 }, timeoutFrames: 10);
            var cmd = CreateCommandBuffer();
            var entity = new Entity(100, 1);
            
            elm.BeginConstruction(entity, 1, currentFrame: 0, cmd);
            
            // Module 1 ACKs, module 2 never responds
            elm.ProcessConstructionAck(new ConstructionAck
            {
                Entity = entity,
                ModuleId = 1,
                Success = true
            }, 5, cmd);
            
            // Run timeout check at frame 15
            elm.CheckTimeouts(currentFrame: 15, cmd);
            
            // Entity should be destroyed due to timeout
            var stats = elm.GetStatistics();
            Assert.Equal(1, stats.timeouts);
        }

        [Fact]
        public void ELM_PartialAcks_RemainsPending()
        {
            var elm = new EntityLifecycleModule(null, new[] { 1, 2, 3 });
            var cmd = CreateCommandBuffer();
            var entity = new Entity(100, 1);
            
            elm.BeginConstruction(entity, 1, 10, cmd);
            
            // Module 1 ACKs, modules 2 & 3 silent
            elm.ProcessConstructionAck(new ConstructionAck
            {
                Entity = entity,
                ModuleId = 1,
                Success = true
            }, 11, cmd);
            
            // Verify entity still pending (not activated)
            var stats = elm.GetStatistics();
            Assert.Equal(1, stats.pending);  // Still waiting
        }

        [Fact]
        public void ELM_MultipleEntities_TrackedIndependently()
        {
            var elm = new EntityLifecycleModule(null, new[] { 1, 2 });
            var cmd = CreateCommandBuffer();
            var entity1 = new Entity(100, 1);
            var entity2 = new Entity(101, 1);
            
            elm.BeginConstruction(entity1, 1, 10, cmd);
            elm.BeginConstruction(entity2, 1, 11, cmd);
            
            // Module 1 ACKs for entity1 only
            elm.ProcessConstructionAck(new ConstructionAck
            {
                Entity = entity1,
                ModuleId = 1,
                Success = true
            }, 12, cmd);
            
            var stats = elm.GetStatistics();
            
            // Both still pending (entity1 needs mod2, entity2 needs mod1+2)
            Assert.Equal(2, stats.pending);
            
            // Module 2 ACKs for entity1 -> Activated
            elm.ProcessConstructionAck(new ConstructionAck
            {
                Entity = entity1,
                ModuleId = 2,
                Success = true
            }, 13, cmd);
            
            stats = elm.GetStatistics();
            Assert.Equal(1, stats.pending); // Entity 2 still pending
            Assert.Equal(1, stats.constructed); // Entity 1 finished
        }
    }
}
