using System;
using System.Collections.Generic;
using Xunit;
using Fdp.Core;
using Fdp.Core.Systems;
using Fdp.Core.Internal;
using System.Runtime.InteropServices;

namespace Fdp.Core.Tests
{
    // Define Mock Modules as bits
    public static class Modules
    {
        public const ulong Physics = 1UL << 0;
        public const ulong Network = 1UL << 1;
        public const ulong AI = 1UL << 2;
    }

    // Mock Components
    [StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    [ComponentId(175)]
    public struct RigidBody { public float Mass; }

    [StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    [ComponentId(176)]
    public struct NetIdentity { public int NetworkId; }

    public unsafe class EntityLifecycleTests : IDisposable
    {
        private EntityRepository _repo;
        private NativeEventStream<EntityLifecycleEvent> _lifecycleStream;
        private EntityValidationSystem _validationSystem;
        private TimeSystem _timeSystem;

        public EntityLifecycleTests()
        {
            _repo = new EntityRepository();
            _repo.RegisterComponent<LifecycleDescriptor>();
            _repo.RegisterComponent<IsActiveTag>();
            _repo.RegisterComponent<RigidBody>();
            _repo.RegisterComponent<NetIdentity>();

            _lifecycleStream = new NativeEventStream<EntityLifecycleEvent>(1024);
            _repo.RegisterLifecycleStream(_lifecycleStream);

            // Setup System
            _validationSystem = new EntityValidationSystem();
            _validationSystem.InternalCreate(_repo);
            
            // Initialize TimeSystem
            _timeSystem = new TimeSystem(_repo);
        }

        private void RunValidation()
        {
             // Update time for the "Frame"
             _timeSystem.Step(1.0f);

             if (_repo.HasSingleton<GlobalTime>())
             {
                 var gt = _repo.GetSingletonUnmanaged<GlobalTime>();
                 if (gt.DeltaTime != 1.0f)
                     throw new Exception($"GlobalTime.DeltaTime is {gt.DeltaTime}, expected 1.0f");
             }
             else
             {
                 throw new Exception("GlobalTime Singleton missing!");
             }

             // Use the real system logic!
             _validationSystem.InternalUpdate();
        }

        public void Dispose()
        {
            _validationSystem.Dispose();
            _repo.Dispose();
            _lifecycleStream.Dispose();
        }

        [Fact]
        public void CreateEntity_EmitsEvent()
        {
            var e = _repo.CreateEntity();
            
            // Verify Event
            _lifecycleStream.Swap(); // Move to read buffer
            var events = _lifecycleStream.Read();
            
            Assert.Single(events.ToArray());
            Assert.Equal(LifecycleEventType.Created, events[0].Type);
            Assert.Equal(e, events[0].Entity);
        }

        [Fact]
        public void StagedConstruction_AssemblyLineFlow()
        {
            // 1. Create Staged Entity (Requirements: Physics + Network)
            ulong required = Modules.Physics | Modules.Network;
            var entity = _repo.CreateStagedEntity(required, default);

            // Phase 1: Construction
            Assert.True(_repo.HasComponent<LifecycleDescriptor>(entity));
            Assert.False(_repo.HasComponent<IsActiveTag>(entity)); // Not active yet
            
            ref var desc = ref _repo.GetComponentRW<LifecycleDescriptor>(entity);
            Assert.Equal(EntityState.Constructing, desc.State);

            // 2. Simulate Physics Module (Consuming Event)
            // It sees the event (we skip event read for brevity)
            _repo.AddComponent(entity, new RigidBody { Mass = 10f });
            desc = ref _repo.GetComponentRW<LifecycleDescriptor>(entity);
            desc.AckedModulesMask |= Modules.Physics;

            // Run Validation -> Should NOT promote yet (Network missing)
            RunValidation();
            Assert.False(_repo.HasComponent<IsActiveTag>(entity));

            // 3. Simulate Network Module
            _repo.AddComponent(entity, new NetIdentity { NetworkId = 999 });
            desc = ref _repo.GetComponentRW<LifecycleDescriptor>(entity);
            desc.AckedModulesMask |= Modules.Network;

            // Run Validation -> Should PROMOTE
            RunValidation();
            
            // Verify
            Assert.True(_repo.HasComponent<IsActiveTag>(entity));
             ref var finalDesc = ref _repo.GetComponentRW<LifecycleDescriptor>(entity);
            Assert.Equal(EntityState.Active, finalDesc.State);
        }

        [Fact]
        public void DistributedAuthority_MaskIsSet()
        {
            // Peer A creates entity, claims Authority over Physics (Mask 1) and AI (Mask 4)
            // Authority Mask: 101 (5)
            // Note: BitMask uses ComponentType<T>.ID as index.
            
            var authMask = new BitMask256();
            authMask.SetBit(ComponentType<RigidBody>.ID);
            
            var entity = _repo.CreateStagedEntity(Modules.Physics, authMask);

            // Verify Authority
            Assert.True(_repo.HasAuthority<RigidBody>(entity));
            Assert.False(_repo.HasAuthority<NetIdentity>(entity));
        }

        [Fact]
        public void ZombieCleanup_TimeoutDestroysEntity()
        {
             var entity = _repo.CreateStagedEntity(Modules.Physics, default);

             // Run validation until we exceed the system threshold.
             // MaxTime is 5.0f, and our mock DT is 1.0f per step.
             int iterations = (int)Math.Ceiling(EntityValidationSystem.MaxConstructionTime) + 1;
             
             for(int i=0; i <= iterations; i++)
             {
                 RunValidation();
             }

             Assert.False(_repo.IsAlive(entity));
        }
    }
    
}
