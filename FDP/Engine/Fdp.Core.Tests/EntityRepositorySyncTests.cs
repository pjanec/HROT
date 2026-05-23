using Xunit;
using Fdp.Core;
using System;

namespace Fdp.Tests
{
    public class EntityRepositorySyncTests
    {
        // Define components
        [ComponentId(187)]
        struct Pos { public float X; }
        [ComponentId(188)]
        struct Vel { public float X; }
        
        [ComponentId(189)]
        record Tag(string Label);

        [Fact]
        public void FullSync_CopiesAllDirtyChunks()
        {
             using var source = new EntityRepository();
             using var dest = new EntityRepository();
             
             source.RegisterComponent<Pos>();
             source.RegisterComponent<Vel>();
             dest.RegisterComponent<Pos>();
             dest.RegisterComponent<Vel>();
             
             var e = source.CreateEntity();
             source.AddComponent(e, new Pos { X=1 });
             source.Tick(); // GlobalVersion++
             
             dest.SyncFrom(source);
             
             var destE = new Entity(e.Index, e.Generation);
             Assert.True(dest.IsAlive(destE));
             Assert.Equal(1f, dest.GetComponentRO<Pos>(destE).X);
             Assert.Equal(source.GlobalVersion, dest.GlobalVersion);
        }

        [Fact]
         public void FilteredSync_CopiesOnlyMaskedComponents()
         {
             using var source = new EntityRepository();
             using var dest = new EntityRepository();
             
             source.RegisterComponent<Pos>();
             source.RegisterComponent<Vel>();
             dest.RegisterComponent<Pos>();
             dest.RegisterComponent<Vel>();
             
             var e = source.CreateEntity();
             source.AddComponent(e, new Pos { X=10 });
             source.AddComponent(e, new Vel { X=20 });
             
             // Create mask for Pos only
             var mask = new BitMask512();
             mask.SetBit(ComponentType<Pos>.ID);
             
             dest.SyncFrom(source, mask);
             
             var destE = new Entity(e.Index, e.Generation);
             Assert.True(dest.HasComponent<Pos>(destE));
             Assert.False(dest.HasComponent<Vel>(destE)); 
         }
        
        [Fact]
        public void Tier2_ShallowCopy_Works()
        {
             using var source = new EntityRepository();
             using var dest = new EntityRepository();
             source.RegisterManagedComponent<Tag>();
             dest.RegisterManagedComponent<Tag>();
             
             var e = source.CreateEntity();
             var tag = new Tag("Test");
             source.AddComponent(e, tag);
             
             dest.SyncFrom(source);
             
             var destE = new Entity(e.Index, e.Generation);
             var destTag = dest.GetComponentRO<Tag>(destE);
             
             Assert.Same(tag, destTag);
        }
        
        [Fact]
        public void EmptySource_ClearsDestination()
        {
            using var source = new EntityRepository();
            using var dest = new EntityRepository();
            source.RegisterComponent<Pos>();
            dest.RegisterComponent<Pos>();
            
            // Dest has data
            var e = dest.CreateEntity();
            dest.AddComponent(e, new Pos { X=1 });
            
            // Source has no entities (we just key it clean)
            // But structural sync needs Empty source repository?
            // "Source" is empty by default (no entities).
            // But wait, SyncFrom syncs chunks.
            // If Source keys are empty, it syncs empty keys.
            // But if Dest keys are not empty, they should be cleared?
            // EntityIndex.SyncEmpty clears Dest.
            // SyncDirtyChunks on tables clears chunks.
            
            // Fresh source
            using var freshSource = new EntityRepository();
            freshSource.RegisterComponent<Pos>();
            
            dest.SyncFrom(freshSource);
            
            // Check
            Assert.Equal(0, dest.EntityCount);
            Assert.False(dest.IsAlive(e));
        }

        [Fact]
        public void Performance_MeetsTarget()
        {
             using var source = new EntityRepository();
             using var dest = new EntityRepository();
             // Register some components
             source.RegisterComponent<Pos>();
             dest.RegisterComponent<Pos>();
             
             // 100K entities
             int count = 100_000;
             for(int i=0; i<count; i++)
             {
                 var e = source.CreateEntity();
                 if (i % 2 == 0) source.AddComponent(e, new Pos{X=i});
             }
             
             var sw = System.Diagnostics.Stopwatch.StartNew();
             dest.SyncFrom(source);
             sw.Stop();
             
             // Allowing 50ms for cold start + full sync. 
             // Target is 2ms BUT for "30% dirty". This is 100% dirty.
             Assert.True(sw.ElapsedMilliseconds < 50, $"Time: {sw.ElapsedMilliseconds}");
        }
        [DataPolicy(DataPolicy.Transient)]
        [ComponentId(144)]
        struct TransientData { public int Val; }

        [ComponentId(145)]
        struct PersistentData { public int Val; }

        [Fact]
        public void SyncFrom_Default_ExcludesTransient()
        {
             using var source = new EntityRepository();
             using var dest = new EntityRepository();
             
             source.RegisterComponent<TransientData>(); // Marked as Transient via Attribute
             source.RegisterComponent<PersistentData>();
             
             var e = source.CreateEntity();
             source.AddComponent(e, new TransientData { Val=1 });
             source.AddComponent(e, new PersistentData { Val=2 });
             
             dest.SyncFrom(source);
             
             var destE = new Entity(e.Index, e.Generation);
             Assert.False(dest.HasComponent<TransientData>(destE));
             Assert.True(dest.HasComponent<PersistentData>(destE));
        }

        [Fact]
        public void SyncFrom_ExplicitMask_ExcludesTransient_UnlessOverridden()
        {
             using var source = new EntityRepository();
             using var dest = new EntityRepository();
             
             source.RegisterComponent<TransientData>();
             
             var e = source.CreateEntity();
             source.AddComponent(e, new TransientData { Val=1 });
             
             // Create mask matching TransientData
             var mask = new BitMask512();
             mask.SetBit(ComponentType<TransientData>.ID);
             
             // 1. Sync with mask (Should Exclude by default safety rule)
             dest.SyncFrom(source, mask: mask);
             var destE = new Entity(e.Index, e.Generation);
             Assert.False(dest.HasComponent<TransientData>(destE));
             
             // 2. Sync with mask AND includeTransient=true (Should Include)
             dest.SyncFrom(source, mask: mask, includeTransient: true);
             Assert.True(dest.HasComponent<TransientData>(destE));
        }

        [Fact]
        public void SyncFrom_IncludeTransient_IncludesTransient()
        {
             using var source = new EntityRepository();
             using var dest = new EntityRepository();
             
             source.RegisterComponent<TransientData>();
             
             var e = source.CreateEntity();
             source.AddComponent(e, new TransientData { Val=1 });
             
             // Sync with includeTransient=true (no mask)
             dest.SyncFrom(source, includeTransient: true);
             
             var destE = new Entity(e.Index, e.Generation);
             Assert.True(dest.HasComponent<TransientData>(destE));
        }

        [Fact]
        public void SyncFrom_ExcludeTypes_FiltersSpecificTypes()
        {
             using var source = new EntityRepository();
             using var dest = new EntityRepository();
             
             source.RegisterComponent<PersistentData>();
             
             var e = source.CreateEntity();
             source.AddComponent(e, new PersistentData { Val=2 });
             
             // Sync with excludeTypes
             dest.SyncFrom(source, excludeTypes: new Type[] { typeof(PersistentData) });
             
             var destE = new Entity(e.Index, e.Generation);
             Assert.False(dest.HasComponent<PersistentData>(destE));
        }
    }
}
