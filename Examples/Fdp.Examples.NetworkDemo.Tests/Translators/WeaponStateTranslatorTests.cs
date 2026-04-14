using System;
using System.Collections.Generic;
using Xunit;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Examples.NetworkDemo.Translators;
using Fdp.Examples.NetworkDemo.Descriptors;
using Fdp.Examples.NetworkDemo.Components;
using Fdp.Toolkit.Replication.Components; // Fix: For ChildMap
using Fdp.Toolkit.Replication.Services;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Interfaces;

namespace Fdp.Examples.NetworkDemo.Tests.Translators
{
    // Expose protected Decode for testing
    public class TestableWeaponStateTranslator : WeaponStateTranslator
    {
        public TestableWeaponStateTranslator(DdsParticipant p, NetworkEntityMap map) 
            : base(p, map)
        {
        }

        public void CallDecode(in WeaponStateTopic data, IEntityCommandBuffer cmd, ISimulationView view)
        {
            Decode(in data, cmd, view);
        }
    }

    public class WeaponStateTranslatorTests : IDisposable
    {
        private DdsParticipant _participant;
        private NetworkEntityMap _entityMap;
        private EntityRepository _repo;

        public WeaponStateTranslatorTests()
        {
            _participant = new DdsParticipant(0); // Domain 0
            _entityMap = new NetworkEntityMap();
            _repo = new EntityRepository();
            
            _repo.RegisterComponent<TurretState>();
            _repo.RegisterComponent<WeaponState>();
            _repo.RegisterManagedComponent<ChildMap>();
        }

        public void Dispose()
        {
            _participant?.Dispose();
        }

        [Fact]
        public void Decode_InstanceZero_UpdatesRootEntity()
        {
            var translator = new TestableWeaponStateTranslator(_participant, _entityMap);
            var root = _repo.CreateEntity();
            long netId = 1001;
            _entityMap.Register(netId, root);

            var topic = new WeaponStateTopic
            {
                EntityId = netId,
                InstanceId = 0,
                Azimuth = 45f,
                Elevation = 10f,
                Ammo = 99,
                Status = 1
            };

            // Act
            // In unit test using EntityRepository as both cmd and view works (it implements both interfaces + direct execution)
            // But WeaponStateTranslator uses cmd.SetComponent.
            // If we use repo as cmd, it executes immediately.
            
            // Wait, does WeaponStateTranslator use IEntityCommandBuffer cmd?
            // Yes. EntityRepository implements IEntityCommandBuffer explicitly? No, implicitly via ISimulationView.GetCommandBuffer?
            // EntityRepository implements ISimulationView.
            // But usually we need an EntityCommandBuffer to defer?
            // In unit tests, we can use repo directly if we cast or if we have a MockCommandBuffer.
            // Or just use repo which usually has methods.
            // But Decode signature takes IEntityCommandBuffer.
            // EntityRepository DOES implement IEntityCommandBuffer in Fdp.Core?
            // Let's assume we can use a mock wrapper or cast.
            // If repo doesn't implement it, we need a wrapper.
            
            // Looking at previous tests, they used MockCommandBuffer.
            // I'll implement a simple wrapper or use repo if possible.
            // Let's use `repo` as `ISimulationView` and `repo` as target?
            // Wait, implementation: `cmd.SetComponent`
            
            // I'll create a simple command buffer wrapper that forwards to repo
            var cmd = new DirectCommandBuffer(_repo);
            
            translator.CallDecode(topic, cmd, _repo);

            // Assert
            Assert.True(_repo.HasComponent<TurretState>(root));
            var turret = _repo.GetComponentRO<TurretState>(root);
            Assert.Equal(45f, turret.Yaw);
            Assert.Equal(10f, turret.Pitch);

            Assert.True(_repo.HasComponent<WeaponState>(root));
            var weapon = _repo.GetComponentRO<WeaponState>(root);
            Assert.Equal(99, weapon.Ammo);
        }

        [Fact]
        public void Decode_InstanceChild_UpdatesChildEntity()
        {
            var translator = new TestableWeaponStateTranslator(_participant, _entityMap);
            var root = _repo.CreateEntity();
            var child = _repo.CreateEntity();
            long netId = 1002;
            _entityMap.Register(netId, root);

            var childMap = new ChildMap();
            childMap.InstanceToEntity[1] = child;
            _repo.SetManagedComponent(root, childMap);

            var topic = new WeaponStateTopic
            {
                EntityId = netId,
                InstanceId = 1, // Target Child 1
                Azimuth = 90f
            };

            var cmd = new DirectCommandBuffer(_repo);
            translator.CallDecode(topic, cmd, _repo);

            // Assert Child updated
            Assert.True(_repo.HasComponent<TurretState>(child));
            Assert.Equal(90f, _repo.GetComponentRO<TurretState>(child).Yaw);
            
            // Assert Root NOT updated
            Assert.False(_repo.HasComponent<TurretState>(root));
        }

        private class DirectCommandBuffer : IEntityCommandBuffer
        {
            private EntityRepository _repo;
            public DirectCommandBuffer(EntityRepository repo) => _repo = repo;

            public void SetComponent<T>(Entity entity, in T component) where T : unmanaged
            {
                _repo.SetComponent(entity, component);
            }
            // Implement other members empty or throw
             public void AddComponent<T>(Entity entity, in T component) where T : unmanaged => _repo.AddComponent(entity, component);
             public void AddManagedComponent<T>(Entity entity, T? component) where T : class => _repo.SetManagedComponent(entity, component!);
             public Entity CreateEntity() => _repo.CreateEntity();

             public unsafe void SetComponentRaw(Entity entity, int typeId, void* ptr, int sizeBytes)
             {
                 throw new NotImplementedException();
             }
             public void SetManagedComponentRaw(Entity entity, int typeId, object component)
             {
                  throw new NotImplementedException();
             }
             public void DestroyEntity(Entity entity) => _repo.DestroyEntity(entity);
             public void PublishEvent<T>(in T evt) where T : unmanaged {}
             public void RemoveComponent<T>(Entity entity) where T : unmanaged => _repo.RemoveComponent<T>(entity);
             public void RemoveManagedComponent<T>(Entity entity) where T : class {}
             public void SetLifecycleState(Entity entity, EntityLifecycle state) {}
             public void SetManagedComponent<T>(Entity entity, T? component) where T : class => _repo.SetManagedComponent(entity, component!);
        }
    }
}
