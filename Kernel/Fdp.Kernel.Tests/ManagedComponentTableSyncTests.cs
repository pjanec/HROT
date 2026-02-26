using Xunit;
using Fdp.Kernel;
using System;

namespace Fdp.Tests
{
    public class ManagedComponentTableSyncTests
    {
        // Immutable record for testing
        [ComponentId(252)]
        public record TestRecord(string Name, int Value);

        [Fact]
        public void ShallowCopy_Works()
        {
             using var source = new ManagedComponentTable<TestRecord>();
             using var dest = new ManagedComponentTable<TestRecord>();
             
             var r1 = new TestRecord("Ref1", 1);
             source.Set(0, r1, 10);
             
             dest.SyncDirtyChunks(source);
             
             var r2 = dest.GetRO(0);
             Assert.Same(r1, r2); // Reference verification
             Assert.Equal(10u, dest.GetChunkVersion(0));
        }

        [Fact]
        public void VersionTracking_Works()
        {
             using var source = new ManagedComponentTable<TestRecord>();
             using var dest = new ManagedComponentTable<TestRecord>();
             
             // Chunk 0: Dirty
             var r1 = new TestRecord("Dirty", 1);
             source.Set(0, r1, 10);
             
             // Chunk 1: Clean (versions match)
             int chunk1Idx = source.ChunkCapacity;
             var r2 = new TestRecord("Clean", 2);
             source.Set(chunk1Idx, r2, 5);
             dest.Set(chunk1Idx, r2, 5); // Already synced
             
             // Modify dest chunk 1 to something else but keep version same to test skip
             var r3 = new TestRecord("ModifiedButCached", 3);
             dest.Set(chunk1Idx, r3, 5); 
             
             dest.SyncDirtyChunks(source);
             
             // Chunk 0 (Dirty) should be synced
             Assert.Same(r1, dest.GetRO(0));
             
             // Chunk 1 (Clean version match) should be SKIPPED (so dest keeps r3, effectively ignoring source)
             // This proves the optimization works (it didn't copy form source)
             Assert.Same(r3, dest.GetRO(chunk1Idx));
        }
        
        [Fact]
        public void SyncFrom_Interface_Works()
        {
             using var source = new ManagedComponentTable<TestRecord>();
             using var dest = new ManagedComponentTable<TestRecord>();
             
             ((ManagedComponentTable<TestRecord>)source).Set(0, new TestRecord("A", 1), 1);
             
             // Call via interface
             dest.SyncFrom((IComponentTable)source);
             
             Assert.NotNull(dest.GetRO(0));
             Assert.Equal("A", dest.GetRO(0)!.Name);
        }
        
        [Fact]
        public void Clear_WhenSourceEmpty()
        {
            using var source = new ManagedComponentTable<TestRecord>();
            using var dest = new ManagedComponentTable<TestRecord>();
            
            // Dest has data
            dest.Set(0, new TestRecord("Data", 1), 1);
            Assert.NotNull(dest.GetRO(0));
            
            // Source is empty (chunk 0 is null/empty)
            // Need versions to differ to trigger check
            // Source version 0 (default). Dest version 1.
            
            dest.SyncDirtyChunks(source);
            
            // Dest chunk should be cleared (null ref in array, so GetRO returns null)
            Assert.Null(dest.GetRO(0));
        }

        [Fact]
        public void Performance_Benchmark()
        {
             using var source = new ManagedComponentTable<TestRecord>();
             using var dest = new ManagedComponentTable<TestRecord>();
             
             int totalChunks = source.TotalChunks;
             for(int i=0; i<totalChunks; i++)
             {
                 int entityId = i * source.ChunkCapacity;
                 if (i % 3 == 0)
                     source.Set(entityId, new TestRecord("MakeDirty", i), 2);
                 else
                     source.Set(entityId, new TestRecord("Clean", i), 1);
                     
                 dest.Set(entityId, new TestRecord("Clean", i), 1);
             }
             
             var sw = System.Diagnostics.Stopwatch.StartNew();
             dest.SyncDirtyChunks(source);
             sw.Stop();
             
             Assert.True(sw.ElapsedMilliseconds < 5, $"Time: {sw.ElapsedMilliseconds}ms");
        }
    }
}
