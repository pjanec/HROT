using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Fdp.Kernel;
using Fdp.Interfaces;
using Fdp.ModuleHost_Core;
using Fdp.ModuleHost_Core.Abstractions;
using Fdp.ModuleHost_Core.Time;
using FDP.Toolkit.Lifecycle;
using FDP.Toolkit.Lifecycle.Events;
using Xunit;

namespace FDP.Toolkit.Lifecycle.Tests
{
    public class EntityLifecycleIntegrationTests
    {
        private EntityRepository _repo;
        private EventAccumulator _accumulator;

        private ModuleHostKernel CreateKernel()
        {
            _repo = new EntityRepository();
            _accumulator = new EventAccumulator();
            var kernel = new ModuleHostKernel(_repo, _accumulator);
            kernel.SetTimeController(new MockTimeController());
            return kernel;
        }

        private class MockTimeController : ITimeController
        {
            public float TimeScale { get; private set; } = 1.0f;
            public void Dispose() { }
            public GlobalTime Update() => new GlobalTime { DeltaTime = 0.016f, TotalTime = 0 };
            public void SetTimeScale(float scale) => TimeScale = scale;
            public float GetTimeScale() => TimeScale;
            public TimeMode GetMode() => TimeMode.Continuous;
            public GlobalTime GetCurrentState() => new GlobalTime();
            public void SeedState(GlobalTime state) { }
        }

        private class MockModule : IEcsModule
        {
            public int Id { get; set; }
            public string Name => $"Mock{Id}";
            public ExecutionPolicy Policy => ExecutionPolicy.Synchronous();
            public IReadOnlyList<Type> WatchEvents => new[] { typeof(ConstructionOrder), typeof(DestructionOrder) };
            
            public HashSet<Entity> InitializedEntities = new HashSet<Entity>();

            public void RegisterSystems(ISystemRegistry registry)
            {
                registry.RegisterSystem(new MockSystem(this));
            }

            public void Tick(ISimulationView view, float deltaTime) { }
            
            [UpdateInPhase(SystemPhase.Input)]
            private class MockSystem : IEcsModuleSystem
            {
                private MockModule _module;
                public MockSystem(MockModule module) { _module = module; }

                public void Execute(ISimulationView view, float deltaTime)
                {
                    var cmd = view.GetCommandBuffer();
                    foreach(var order in view.ConsumeEvents<ConstructionOrder>())
                    {
                        if (order.BlueprintId == 1) 
                        {
                            _module.InitializedEntities.Add(order.Entity);
                            cmd.PublishEvent(new ConstructionAck { 
                                Entity = order.Entity, 
                                ModuleId = _module.Id, 
                                Success = true 
                            });
                        }
                    }
                    
                    foreach(var order in view.ConsumeEvents<DestructionOrder>())
                    {
                        cmd.PublishEvent(new DestructionAck {
                            Entity = order.Entity,
                            ModuleId = _module.Id,
                            Success = true
                        });
                    }
                }
            }
        }

        private class MockTkbDatabase : ITkbDatabase
        {
            public bool TryGetByType(long blueprintId, out TkbTemplate template)
            {
                template = new TkbTemplate("Mock", blueprintId);
                return true;
            }
            public bool TryGetByName(string name, out TkbTemplate template) { template = null; return false; }
            public TkbTemplate GetByType(long blueprintId) => new TkbTemplate("Mock", blueprintId);
            public void Register(TkbTemplate template) { }
            public IEnumerable<TkbTemplate> GetAll() => Array.Empty<TkbTemplate>();
            public TkbTemplate GetByName(string name) => null;
            public TkbTemplate GetTemplateByEntityType(Fdp.Kernel.DISEntityType entityType) => null;
            public TkbTemplate GetTemplateByName(string templateName) => null;
        }

        [Fact]
        public async Task ELM_3Module_CoordinatedSpawn()
        {
            // Setup: 3 modules (Physics, AI, Network) all participate
            var physics = new MockModule { Id = 1 };
            var ai = new MockModule { Id = 2 };
            var network = new MockModule { Id = 3 };
            
            var elm = new EntityLifecycleModule(new MockTkbDatabase(), new[] { 1, 2, 3 });
            
            var kernel = CreateKernel();
            kernel.RegisterModule(elm);
            kernel.RegisterModule(physics);
            kernel.RegisterModule(ai);
            kernel.RegisterModule(network);
            kernel.Initialize();
            
            // Register events (required for PublishRaw in CommandBuffer playback)
            _repo.RegisterEvent<ConstructionOrder>();
            _repo.RegisterEvent<ConstructionAck>();
            _repo.RegisterEvent<DestructionOrder>();
            _repo.RegisterEvent<DestructionAck>();
            
            // Spawn entity
            var entity = _repo.CreateEntity();
            _repo.SetLifecycleState(entity, EntityLifecycle.Constructing);
            
            // Invoke ELM logic (simulate SpawnerSystem)
            var cmd = new EntityCommandBuffer();
            elm.BeginConstruction(entity, 1, _repo.GlobalVersion, cmd);
            cmd.Playback(_repo);
             
            // Run frames until all modules ACK
            for (int frame = 0; frame < 10; frame++)
            {
                kernel.Update(0.016f);
                await Task.Delay(10);
                
                // Check if activated
                 var query = _repo.Query().WithLifecycle(EntityLifecycle.Active).Build();
                 if (query.Any() && query.FirstOrNull().Index == entity.Index) 
                     break;
            }
            
            // Verify activated
            var activeQuery = _repo.Query().WithLifecycle(EntityLifecycle.Active).Build();
            Assert.True(activeQuery.Any(), "Entity should be active");
            Assert.Equal(entity.Index, activeQuery.FirstOrNull().Index);
            
            // Verify all modules initialized
            Assert.True(physics.InitializedEntities.Contains(entity));
            Assert.True(ai.InitializedEntities.Contains(entity));
            Assert.True(network.InitializedEntities.Contains(entity));
        }
        [Fact]
        public async Task ELM_Teardown_CoordinatedDestruction()
        {
            var physics = new MockModule { Id = 1 };
            var elm = new EntityLifecycleModule(new MockTkbDatabase(), new[] { 1 });
            
            var kernel = CreateKernel();
            kernel.RegisterModule(elm);
            kernel.RegisterModule(physics);
            kernel.Initialize();
            
            _repo.RegisterEvent<ConstructionOrder>();
            _repo.RegisterEvent<ConstructionAck>();
            _repo.RegisterEvent<DestructionOrder>();
            _repo.RegisterEvent<DestructionAck>();
            
            // 1. Create Active Entity
            var entity = _repo.CreateEntity(); // Defaults to Active
            
            // 2. Begin Destruction
            var cmd = new EntityCommandBuffer();
            elm.BeginDestruction(entity, _repo.GlobalVersion, new FixedString64("Test"), cmd);
            cmd.Playback(_repo);
            
            // 3. Run frames
            for (int i = 0; i < 10; i++)
            {
                kernel.Update(0.016f);
                await Task.Delay(10);
                
                if (!_repo.IsAlive(entity)) break;
            }
            
            // 4. Verify Destroyed
            Assert.False(_repo.IsAlive(entity), "Entity should be destroyed after ACK");
        }
    }
}
