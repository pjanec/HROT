using System;
using System.Collections.Generic;
using Xunit;
using Fdp.Kernel;
using FDP.Toolkit.Replication.Components;
using FDP.Toolkit.Replication.Systems;
using Fdp.Interfaces;
using ModuleHost.Core.Abstractions;
using FDP.Toolkit.Lifecycle.Events;

namespace FDP.Toolkit.Replication.Tests
{
    // Mocks
    class MockTkbDatabase : ITkbDatabase
    {
        public TkbTemplate TemplateToReturn;
        
        public IEnumerable<TkbTemplate> GetAll() => throw new NotImplementedException();
        public TkbTemplate GetByName(string name) => throw new NotImplementedException();
        public TkbTemplate GetByType(long tkbType) => TemplateToReturn;
        public TkbTemplate GetTemplateByEntityType(Fdp.Kernel.DISEntityType entityType) => null;
        public TkbTemplate GetTemplateByName(string templateName) => null;
        public void Register(TkbTemplate template) {}
        public bool TryGetByName(string name, out TkbTemplate template) => throw new NotImplementedException();
        public bool TryGetByType(long tkbType, out TkbTemplate template)
        {
            template = TemplateToReturn;
            return template != null;
        }
    }

    class MockSerializationRegistry : ISerializationRegistry
    {
        public ISerializationProvider Provider;
        public ISerializationProvider Get(long descriptorOrdinal) => Provider;
        public void Register(long descriptorOrdinal, ISerializationProvider provider) {}
        public bool TryGet(long descriptorOrdinal, out ISerializationProvider provider)
        {
            provider = Provider;
            return true;
        }
    }

    class MockProvider : ISerializationProvider
    {
        public bool ApplyCalled = false;
        public void Apply(Entity entity, ReadOnlySpan<byte> buffer, IEntityCommandBuffer cmd)
        {
            ApplyCalled = true;
        }

        public void Encode(object descriptor, Span<byte> buffer) {}
        public int GetSize(object descriptor) => 0;
    }
    
    class SlowMockProvider : ISerializationProvider
    {
        public int CallCount = 0;
        public void Apply(Entity entity, ReadOnlySpan<byte> buffer, IEntityCommandBuffer cmd)
        {
            CallCount++;
            System.Threading.Thread.Sleep(1); // Sleep 1ms to simulate load
        }
        public void Encode(object descriptor, Span<byte> buffer) {}
        public int GetSize(object descriptor) => 0;
    }

    public class GhostProtocolTests
    {
        [Fact]
        public void PromotionSystem_Promotes_WhenRequirementsMet()
        {
            using var repo = new EntityRepository();
            var sys = new GhostPromotionSystem();
            sys.Create(repo);
            
            repo.RegisterManagedComponent<BinaryGhostStore>();
            repo.RegisterComponent<NetworkSpawnRequest>();
            repo.RegisterEvent<ConstructionOrder>(); // Register event required by PromotionSystem
            
            repo.SetSingletonUnmanaged(new GlobalTime { FrameNumber = 100 });
            
            var template = new TkbTemplate("Test", 123); 
            var mockTkb = new MockTkbDatabase { TemplateToReturn = template };
            repo.SetSingletonManaged<ITkbDatabase>(mockTkb);
            
            var mockProvider = new MockProvider();
            var mockReg = new MockSerializationRegistry { Provider = mockProvider };
            repo.SetSingletonManaged<ISerializationRegistry>(mockReg);
            
            var entity = repo.CreateEntity();
            var store = new BinaryGhostStore();
            store.StashedData.Add(PackedKey.Create(1, 0), new byte[] { 1, 2, 3 });
            // Should be identified
            store.IdentifiedAtFrame = 100;
            repo.AddComponent(entity, store);
            repo.AddComponent(entity, new NetworkSpawnRequest { TkbType = 123 });
            
            sys.Run();
            
            // Check implicit removal of BinaryGhostStore
            Assert.False(repo.HasManagedComponent<BinaryGhostStore>(entity));
            Assert.True(mockProvider.ApplyCalled);
        }

        [Fact]
        public void Execute_RespectsTimeBudget()
        {
            using var repo = new EntityRepository();
            var sys = new GhostPromotionSystem();
            sys.Create(repo);
            
            repo.RegisterManagedComponent<BinaryGhostStore>();
            repo.RegisterComponent<NetworkSpawnRequest>();
            repo.RegisterEvent<ConstructionOrder>();
            repo.SetSingletonUnmanaged(new GlobalTime { FrameNumber = 100 });
            
            var template = new TkbTemplate("Test", 123); 
            var mockTkb = new MockTkbDatabase { TemplateToReturn = template };
            repo.SetSingletonManaged<ITkbDatabase>(mockTkb);
            
            // Use slow provider
            var heavyProvider = new SlowMockProvider();
            var mockReg = new MockSerializationRegistry { Provider = heavyProvider };
            repo.SetSingletonManaged<ISerializationRegistry>(mockReg);
            
            // Create 10 ghosts
            for (int i = 0; i < 10; i++)
            {
                var searchEntity = repo.CreateEntity();
                var store = new BinaryGhostStore();
                store.StashedData.Add(PackedKey.Create(1, 0), new byte[] { 1 });
                store.IdentifiedAtFrame = 100;
                repo.AddComponent(searchEntity, store);
                repo.AddComponent(searchEntity, new NetworkSpawnRequest { TkbType = 123 });
            }
            
            // Expected Budget: 2ms.
            // Each ghost takes >1ms (due to Sleep(1)).
            // So we expect only 1-2 ghosts to be processed per frame.
            
            sys.Run();
            
            Assert.True(heavyProvider.CallCount < 10, $"Processed too many ghosts: {heavyProvider.CallCount}. Should be limited by time budget.");
            Assert.True(heavyProvider.CallCount > 0, "Processed zero ghosts. Should process at least one.");
        }
        
        [Fact]
        public void Execute_SoftTimeout_Waits()
        {
            using var repo = new EntityRepository();
            var sys = new GhostPromotionSystem();
            sys.Create(repo);
            
            repo.RegisterManagedComponent<BinaryGhostStore>();
            repo.RegisterComponent<NetworkSpawnRequest>();
            repo.RegisterEvent<ConstructionOrder>();
            // Frame 100.
            repo.SetSingletonUnmanaged(new GlobalTime { FrameNumber = 100 });
            
            var template = new TkbTemplate("SoftReq", 123);
            template.MandatoryDescriptors.Add(new MandatoryDescriptor 
            {
                PackedKey = PackedKey.Create(2, 0), // Missing key
                IsHard = false,
                SoftTimeoutFrames = 10
            });
            
            var mockTkb = new MockTkbDatabase { TemplateToReturn = template };
            repo.SetSingletonManaged<ITkbDatabase>(mockTkb);
            
            var mockProvider = new MockProvider();
            var mockReg = new MockSerializationRegistry { Provider = mockProvider };
            repo.SetSingletonManaged<ISerializationRegistry>(mockReg);
            
            var entity = repo.CreateEntity();
            var store = new BinaryGhostStore();
            // Identified at frame 90. 100 - 90 = 10 frames elapsed.
            // Timeout is 10. condition: elapsed <= timeout returns false (wait).
            // So at frame 100 (elapsed 10), it should WAIT.
            store.IdentifiedAtFrame = 90;
            repo.AddComponent(entity, store);
            repo.AddComponent(entity, new NetworkSpawnRequest { TkbType = 123 });
            
            sys.Run();
            
            // Should NOT be promoted
            Assert.True(repo.HasManagedComponent<BinaryGhostStore>(entity));
            
            // Advance time to 102. Elapsed = 12 > 10.
            repo.SetSingletonUnmanaged(new GlobalTime { FrameNumber = 102 });
            sys.Run();
            
            // Should be promoted
            Assert.False(repo.HasManagedComponent<BinaryGhostStore>(entity));
        }
    }
}
