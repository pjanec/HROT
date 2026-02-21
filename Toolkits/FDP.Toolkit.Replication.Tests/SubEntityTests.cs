using Fdp.Interfaces;
using Fdp.Kernel;
using FDP.Toolkit.Lifecycle.Events;
using FDP.Toolkit.Replication.Components;
using FDP.Toolkit.Replication.Systems;
using ModuleHost.Core.Abstractions;
using System;
using System.Collections.Generic;
using Xunit;

namespace FDP.Toolkit.Replication.Tests
{
    public class SubEntityTests
    {
        class TestTkbDatabase : ITkbDatabase
        {
            public Dictionary<long, TkbTemplate> Templates = new Dictionary<long, TkbTemplate>();
            
            public IEnumerable<TkbTemplate> GetAll() => Templates.Values;
            public TkbTemplate GetByName(string name) => throw new NotImplementedException();
            public TkbTemplate GetByType(long tkbType) => Templates[tkbType];
            public void Register(TkbTemplate template) { Templates[template.TkbType] = template; }
            public bool TryGetByName(string name, out TkbTemplate template) => throw new NotImplementedException();
            public bool TryGetByType(long tkbType, out TkbTemplate template)
            {
                return Templates.TryGetValue(tkbType, out template);
            }
        }

        class TestSerializationRegistry : ISerializationRegistry
        {
            public ISerializationProvider Provider;
            public ISerializationProvider Get(long descriptorOrdinal) => Provider;
            void ISerializationRegistry.Register(long descriptorOrdinal, ISerializationProvider provider) {}
            public bool TryGet(long descriptorOrdinal, out ISerializationProvider provider)
            {
                provider = Provider;
                return true;
            }
        }

        class CapturingProvider : ISerializationProvider
        {
            public List<(Entity entity, byte[] data)> Calls = new List<(Entity, byte[])>();
            public void Apply(Entity entity, ReadOnlySpan<byte> buffer, IEntityCommandBuffer cmd)
            {
                Calls.Add((entity, buffer.ToArray()));
            }
            public void Encode(object descriptor, Span<byte> buffer) { }
            public int GetSize(object descriptor) => 10;
        }

        [Fact]
        public void AtomicSpawn_CreatesHierarchy()
        {
            // Setup
            using var repo = new EntityRepository();
            var sys = new GhostPromotionSystem();
            sys.Create(repo);

            repo.RegisterManagedComponent<BinaryGhostStore>();
            repo.RegisterManagedComponent<ChildMap>();
            repo.RegisterComponent<NetworkSpawnRequest>();
            repo.RegisterComponent<PartMetadata>();
            repo.RegisterEvent<ConstructionOrder>(); // Register event required by PromotionSystem
            repo.SetSingletonUnmanaged(new GlobalTime { FrameNumber = 100 });

            // Define Templates
            var tkb = new TestTkbDatabase();
            var parentTemplate = new TkbTemplate("Parent", 100);
            var childTemplate = new TkbTemplate("Child", 200);
            
            parentTemplate.ChildBlueprints.Add(new ChildBlueprintDefinition(1, 200));
            parentTemplate.ChildBlueprints.Add(new ChildBlueprintDefinition(2, 200));

            tkb.Register(parentTemplate);
            tkb.Register(childTemplate);
            repo.SetSingletonManaged<ITkbDatabase>(tkb);

            var provider = new CapturingProvider();
            repo.SetSingletonManaged<ISerializationRegistry>(new TestSerializationRegistry { Provider = provider });

            // Create Ghost
            var entity = repo.CreateEntity();
            var store = new BinaryGhostStore();
            store.IdentifiedAtFrame = 100;
            
            // Stash data for child instance 1
            var childData = new byte[] { 0xCA, 0xFE };
            store.StashedData.Add(PackedKey.Create(1, 1), childData);

            repo.AddComponent(entity, store);
            repo.AddComponent(entity, new NetworkSpawnRequest { TkbType = 100 });

            // Run System
            sys.Run();

            // Assertions
            
            // 1. Parent exists and is constructing
            Assert.Equal(EntityLifecycle.Constructing, repo.GetLifecycleState(entity));
            
            // 2. Parent has ChildMap
            Assert.True(repo.HasComponent<ChildMap>(entity));
            var childMap = repo.GetComponent<ChildMap>(entity);
            Assert.Equal(2, childMap.InstanceToEntity.Count);
            
            // 3. Children exist
            var child1 = childMap.InstanceToEntity[1];
            var child2 = childMap.InstanceToEntity[2];
            
            Assert.Equal(EntityLifecycle.Constructing, repo.GetLifecycleState(child1));
            Assert.Equal(EntityLifecycle.Constructing, repo.GetLifecycleState(child2));
            
            // 4. Children have PartMetadata
            var meta1 = repo.GetComponent<PartMetadata>(child1);
            Assert.Equal(entity, meta1.ParentEntity);
            Assert.Equal(1, meta1.InstanceId);
            
            var meta2 = repo.GetComponent<PartMetadata>(child2);
            Assert.Equal(entity, meta2.ParentEntity);
            Assert.Equal(2, meta2.InstanceId);
            
            // 5. Data Application
            // provider should be called for child1
            Assert.NotEmpty(provider.Calls);
            bool foundChildCall = false;
            foreach (var call in provider.Calls)
            {
                if (call.entity == child1 && call.data[0] == 0xCA && call.data[1] == 0xFE)
                {
                    foundChildCall = true;
                    break;
                }
            }
            Assert.True(foundChildCall, "Data should be applied to child instance 1");
        }
    }
}
