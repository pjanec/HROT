using System;
using System.IO;
using Xunit;
using Fdp.Core;
using Fdp.Core.Serialization;

namespace Fdp.Tests.Serialization
{
    // Define unique test components here to ensure isolation
    [ComponentId(253)]
    [MessagePack.MessagePackObject]
    public struct SerialObj1 { 
        [MessagePack.Key(0)] public int X; 
    }
    
    [ComponentId(254)]
    [MessagePack.MessagePackObject]
    public struct SerialObj2 { 
        [MessagePack.Key(0)] public float Y; 
    }

    public class SerializationTests : IDisposable
    {
        public SerializationTests()
        {
            // Reset registry to ensure clean state for tests
            // NOTE: Tests run sequentially so this is safe
            ComponentTypeRegistry.Clear();
        }

        public void Dispose()
        {
            ComponentTypeRegistry.Clear();
        }

        [Fact]
        public void SaveAndLoad_Stream_PreservesEntitiesAndMapping()
        {
            // 1. Setup initial state
            using var repo = new EntityRepository();
            repo.RegisterComponent<SerialObj1>(); // ID 0
            repo.RegisterComponent<SerialObj2>(); // ID 1
            
            var e1 = repo.CreateEntity();
            repo.AddComponent(e1, new SerialObj1 { X = 10 });
            
            var e2 = repo.CreateEntity();
            repo.AddComponent(e2, new SerialObj2 { Y = 3.14f });
            
            // 2. Save to MemoryStream
            using var ms = new MemoryStream();
            RepositorySerializer.SaveToStream(repo, ms);
            
            // 3. Load into NEW repo (simulating fresh load)
            ComponentTypeRegistry.Clear();
            
            using var repo2 = new EntityRepository();
            ms.Position = 0;
            RepositorySerializer.LoadFromStream(repo2, ms);
            
            // 4. Verify
            Assert.Equal(repo.EntityCount, repo2.EntityCount);
            
            // Check E1
            var restoredE1 = new Entity(e1.Index, e1.Generation);
            Assert.True(repo2.IsAlive(restoredE1));
            Assert.True(repo2.HasComponent<SerialObj1>(restoredE1));
            Assert.Equal(10, repo2.GetComponentRW<SerialObj1>(restoredE1).X);
            
            // Check E2
            var restoredE2 = new Entity(e2.Index, e2.Generation);
            Assert.True(repo2.IsAlive(restoredE2));
            Assert.True(repo2.HasComponent<SerialObj2>(restoredE2));
            Assert.Equal(3.14f, repo2.GetComponentRW<SerialObj2>(restoredE2).Y);
        }

        [Fact]
        public void Load_MismatchRegistration_Succeeds()
        {
            // 1. Save state with (SerialObj1=0, SerialObj2=1)
            using var ms = new MemoryStream();
            {
                using var repo = new EntityRepository();
                repo.RegisterComponent<SerialObj1>();
                repo.RegisterComponent<SerialObj2>();
                var e = repo.CreateEntity();
                repo.AddComponent(e, new SerialObj1 { X = 42 });
                RepositorySerializer.SaveToStream(repo, ms);
            }
            
            // 2. Reset and register in REVERSE order (SerialObj2=0, SerialObj1=1)
            ComponentTypeRegistry.Clear();
            // Force mismatched registration - but new serializer maps by NAME so it should work!
            ComponentTypeRegistry.GetOrRegister<SerialObj2>(); 
            ComponentTypeRegistry.GetOrRegister<SerialObj1>();
            
            // 3. Attempt Load -> Should SUCCEED now (Tolerant Reader)
            using var repo2 = new EntityRepository();
            ms.Position = 0;
            
            RepositorySerializer.LoadFromStream(repo2, ms);
            
            // Verify data loaded correctly despite ID mismatch
            // The first entity (index 0) passed from previous repo had SerialObj1.
            // In repo2, SerialObj1 has ID 1. The data should be loaded into ID 1 table.
            
            var idx = repo2.GetEntityIndex();
            // We need to find the entity. It should be index 0 if we started fresh.
            // But we saved after 1 create.
            // Entity 0 was created.
            
            bool found = false;
            for(int i=0; i<=repo2.MaxEntityIndex; i++)
            {
                var h = idx.GetHeader(i);
                if(h.IsActive)
                {
                    // Check if it has SerialObj1
                    if (repo2.HasComponent<SerialObj1>(new Entity(i, h.Generation)))
                    {
                        found = true;
                        Assert.Equal(42, repo2.GetComponentRW<SerialObj1>(new Entity(i, h.Generation)).X);
                    }
                }
            }
            Assert.True(found, "Entity with SerialObj1 should be found even with mismatched registry order.");
        }
    }
}
