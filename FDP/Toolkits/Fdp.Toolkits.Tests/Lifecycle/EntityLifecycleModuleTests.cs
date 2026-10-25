using Xunit;
using Fdp.Toolkit.Lifecycle;
using Fdp.Toolkit.Lifecycle.Events;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Interfaces;
using Moq; 
using System.Linq;

namespace Fdp.Toolkit.Lifecycle.Tests
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

        /// <summary>⭐ CE-3304 — a participant with its own (longer) timeout keeps the construction alive past the default; once only
        /// default-timeout modules remain, the default applies again.</summary>
        [Fact]
        public void ELM_ModuleTimeout_OutlastsTheDefault_OnlyWhileThatModuleIsOutstanding()
        {
            var elm = new EntityLifecycleModule(null, new[] { 1, 2 }, timeoutFrames: 10);
            elm.SetModuleTimeout(2, 100);
            var cmd = CreateCommandBuffer();
            var entity = new Entity(100, 1);
            elm.BeginConstruction(entity, 1, currentFrame: 0, cmd);

            elm.CheckTimeouts(currentFrame: 50, cmd);   // module 2 (100 frames) still outstanding
            Assert.Equal(0, elm.GetStatistics().timeouts);

            elm.ProcessConstructionAck(new ConstructionAck { Entity = entity, ModuleId = 2, Success = true }, 60, cmd);
            elm.CheckTimeouts(currentFrame: 61, cmd);   // only module 1 left: the default 10 applies
            Assert.Equal(1, elm.GetStatistics().timeouts);
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

        // ── ⭐ AQ107 (CE-3323) — the generic lifecycle notifications (docs/DESIGN_Entity_Creation_Unification.md §3.4e) ──────────

        private static readonly System.Guid Request = new System.Guid("6d3f1a2b-0c4d-4e5f-8a9b-1c2d3e4f5a6b");

        // A real world holding one entity with the identity a spawned network entity carries.
        private static (EntityRepository world, Entity entity) NotifiedWorld(bool owned = true, bool withRequest = true)
        {
            var world = new EntityRepository();
            world.RegisterComponent<Fdp.Toolkit.Replication.Components.NetworkIdentity>();
            world.RegisterComponent<Fdp.Toolkit.Replication.Components.NetworkAuthority>();
            world.RegisterComponent<Fdp.Toolkit.Replication.Components.TkbIdentity>();
            world.RegisterComponent<Fdp.Toolkit.Lifecycle.Components.CreationRequestId>();
            world.RegisterEvent<ConstructionOrder>();
            world.RegisterEvent<DestructionOrder>();
            world.RegisterEvent<EntityConstructionCompleted>();
            world.RegisterEvent<EntityDestructionCompleted>();
            var e = world.CreateEntity();
            world.SetLifecycleState(e, EntityLifecycle.Constructing);
            world.AddComponent(e, new Fdp.Toolkit.Replication.Components.NetworkIdentity(4242));
            world.AddComponent(e, new Fdp.Toolkit.Replication.Components.TkbIdentity { TkbType = 77 });
            world.AddComponent(e, new Fdp.Toolkit.Replication.Components.NetworkAuthority(owned ? 3 : 9, 3));
            if (withRequest) world.AddComponent(e, new Fdp.Toolkit.Lifecycle.Components.CreationRequestId { Value = Request });
            return (world, e);
        }

        // What the lifecycle system does at the end of its tick, then what the next frame can read.
        private static void Publish(EntityLifecycleModule elm, EntityRepository world, EntityCommandBuffer cmd)
        {
            elm.PublishOutcomes(world, cmd);
            cmd.Playback(world);
            world.Bus.SwapBuffers();
        }

        private static EntityConstructionCompleted[] Constructions(EntityRepository w) => ((ISimulationView)w).ReadEvents<EntityConstructionCompleted>().ToArray();
        private static EntityDestructionCompleted[] Destructions(EntityRepository w) => ((ISimulationView)w).ReadEvents<EntityDestructionCompleted>().ToArray();

        [Fact]
        public void AQ107_ConstructionCompleted_WhenTheLastModuleAcks_NamesTheEntityAndItsRequest()
        {
            var (world, e) = NotifiedWorld();
            var elm = new EntityLifecycleModule(null, new[] { 1, 2 });
            var cmd = new EntityCommandBuffer();
            elm.BeginConstruction(e, 77, 10, cmd);
            elm.ProcessConstructionAck(new ConstructionAck { Entity = e, ModuleId = 1, Success = true }, 11, cmd);
            Publish(elm, world, cmd);
            Assert.Empty(Constructions(world));   // ⛔ not before the LAST ack

            elm.ProcessConstructionAck(new ConstructionAck { Entity = e, ModuleId = 2, Success = true }, 12, cmd);
            Publish(elm, world, cmd);
            var done = Assert.Single(Constructions(world));
            Assert.Equal(e, done.Entity);
            Assert.Equal(4242L, done.NetworkId);
            Assert.Equal(77L, done.TkbType);
            Assert.Equal(Request, done.RequestId);
            Assert.True(done.IsLocallyOwned);
            Assert.True(done.Succeeded);
            Assert.False(done.Resumed);
            Assert.Equal(12u, done.FrameNumber);
            Assert.Equal(EntityLifecycle.Active, world.GetLifecycleState(e));
        }

        [Fact]
        public void AQ107_ConstructionCompleted_OnAReplica_SaysItIsNotOurs_AndWithoutARequest_CarriesNone()
        {
            var (world, e) = NotifiedWorld(owned: false, withRequest: false);
            var elm = new EntityLifecycleModule(null, new[] { 1 });
            var cmd = new EntityCommandBuffer();
            elm.BeginConstruction(e, 77, 10, cmd);
            elm.ProcessConstructionAck(new ConstructionAck { Entity = e, ModuleId = 1, Success = true }, 11, cmd);
            Publish(elm, world, cmd);
            var done = Assert.Single(Constructions(world));
            Assert.False(done.IsLocallyOwned);
            Assert.Equal(System.Guid.Empty, done.RequestId);
        }

        [Fact]
        public void AQ107_ConstructionCompleted_WhenAModuleRefuses_IsAFailure_WithItsReason()
        {
            var (world, e) = NotifiedWorld();
            var elm = new EntityLifecycleModule(null, new[] { 1, 2 });
            var cmd = new EntityCommandBuffer();
            elm.BeginConstruction(e, 77, 10, cmd);
            elm.ProcessConstructionAck(new ConstructionAck { Entity = e, ModuleId = 1, Success = false, ErrorMessage = new FixedString64("no terrain") }, 11, cmd);
            Publish(elm, world, cmd);
            var done = Assert.Single(Constructions(world));
            Assert.False(done.Succeeded);
            Assert.Equal("no terrain", done.Failure.ToString());
            Assert.Equal(Request, done.RequestId);   // read before the buffer destroyed it
            Assert.False(world.IsAlive(e));
            Assert.Empty(Destructions(world));       // it was never constructed, so it is not "destroyed"
        }

        [Fact]
        public void AQ107_ConstructionCompleted_OnATimeout_IsAFailure()
        {
            var (world, e) = NotifiedWorld();
            var elm = new EntityLifecycleModule(null, new[] { 1 }, timeoutFrames: 5);
            var cmd = new EntityCommandBuffer();
            elm.BeginConstruction(e, 77, 10, cmd);
            elm.CheckTimeouts(20, cmd);
            Publish(elm, world, cmd);
            var done = Assert.Single(Constructions(world));
            Assert.False(done.Succeeded);
            Assert.Equal("construction timed out", done.Failure.ToString());
        }

        [Fact]
        public void AQ107_ConstructionCompleted_WithNoParticipants_ComesWithTheInstantPromotion()
        {
            var (world, e) = NotifiedWorld();
            var elm = new EntityLifecycleModule(null, System.Array.Empty<int>());
            var cmd = new EntityCommandBuffer();
            elm.BeginConstruction(e, 77, 10, cmd);
            elm.DrainInstantComplete(cmd, 10);
            Publish(elm, world, cmd);
            Assert.Empty(Constructions(world));      // the one-frame visibility delay holds
            elm.DrainInstantComplete(cmd, 11);
            Publish(elm, world, cmd);
            Assert.True(Assert.Single(Constructions(world)).Succeeded);
        }

        [Fact]
        public void AQ107_DestructionCompleted_WhenTheLastModuleAcks_CarriesWhatTheDeadHandleNoLongerCan()
        {
            var (world, e) = NotifiedWorld();
            world.SetLifecycleState(e, EntityLifecycle.Active);
            var elm = new EntityLifecycleModule(null, new[] { 1, 2 });
            var cmd = new EntityCommandBuffer();
            elm.BeginDestruction(e, 30, "killed", cmd);
            elm.ProcessDestructionAck(new DestructionAck { Entity = e, ModuleId = 1, Success = true }, 31, cmd);
            Publish(elm, world, cmd);
            Assert.Empty(Destructions(world));
            elm.ProcessDestructionAck(new DestructionAck { Entity = e, ModuleId = 2, Success = true }, 32, cmd);
            Publish(elm, world, cmd);
            var gone = Assert.Single(Destructions(world));
            Assert.False(world.IsAlive(e));
            Assert.Equal(e, gone.Entity);
            Assert.Equal(4242L, gone.NetworkId);
            Assert.Equal(77L, gone.TkbType);
            Assert.Equal(Request, gone.RequestId);
            Assert.Equal("killed", gone.Reason.ToString());
            Assert.False(gone.Forced);
        }

        [Fact]
        public void AQ107_DestructionCompleted_OnATimeout_IsForced()
        {
            var (world, e) = NotifiedWorld();
            var elm = new EntityLifecycleModule(null, new[] { 1 }, timeoutFrames: 5);
            var cmd = new EntityCommandBuffer();
            elm.BeginDestruction(e, 30, "cleared", cmd);
            elm.CheckTimeouts(40, cmd);
            Publish(elm, world, cmd);
            Assert.True(Assert.Single(Destructions(world)).Forced);
        }

        [Fact]
        public void AQ107_AConstructionReopenedAfterARestore_SaysResumed()
        {
            var (world, e) = NotifiedWorld();
            var elm = new EntityLifecycleModule(null, new[] { 1 });
            var cmd = new EntityCommandBuffer();
            elm.OnWorldReplaced(resumingToLive: true);
            elm.ResumeFromRestoredWorld(world, 50, cmd);
            elm.ProcessConstructionAck(new ConstructionAck { Entity = e, ModuleId = 1, Success = true }, 51, cmd);
            Publish(elm, world, cmd);
            Assert.True(Assert.Single(Constructions(world)).Resumed);
        }

        [Fact]
        public void AQ107_AWorldReplacement_DropsTheOutcomesDecidedAgainstTheOldWorld()
        {
            var (world, e) = NotifiedWorld();
            var elm = new EntityLifecycleModule(null, new[] { 1 });
            var cmd = new EntityCommandBuffer();
            elm.BeginConstruction(e, 77, 10, cmd);
            elm.ProcessConstructionAck(new ConstructionAck { Entity = e, ModuleId = 1, Success = true }, 11, cmd);
            elm.OnWorldReplaced(resumingToLive: false);
            Publish(elm, world, cmd);
            Assert.Empty(Constructions(world));
        }
    }
}
