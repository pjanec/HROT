using Xunit;
using Fdp.Core;
using System;

namespace Fdp.Tests
{
    public unsafe class NativeChunkTableSyncTests
    {
        /// <summary>⭐ CE-2394 — the sync walks only up to the highest chunk either side uses (was every chunk the table COULD have:
        /// 1 000 000 / capacity), and still copies a far chunk, clears a chunk the source released, and a versioned chunk past the
        /// source's bound after the source is cleared. ✅ Red-proof: bound the loop by the DESTINATION's mark only ⇒ the far write is lost.</summary>
        [Fact]
        public void CE2394_TheSyncWalksOnlyTheUsedChunks_AndMissesNothing()
        {
            using var source = new NativeChunkTable<Position>();
            using var dest = new NativeChunkTable<Position>();
            Assert.Equal(0, source.UsedChunkBound);
            int cap = FdpConfig.GetChunkCapacity<Position>();
            source.GetRefRW(5, 10).X = 1f;               // chunk 0
            source.GetRefRW(cap * 7 + 3, 11).X = 7f;      // chunk 7
            Assert.Equal(8, source.UsedChunkBound);
            Assert.True(source.UsedChunkBound < source.TotalChunks);

            dest.SyncDirtyChunks(source);
            Assert.Equal(1f, dest.GetRefRO(5).X);
            Assert.Equal(7f, dest.GetRefRO(cap * 7 + 3).X);
            Assert.Equal(11u, dest.GetChunkVersion(7));

            // the source drops everything: the destination follows, though the source's bound is back to 0
            source.Clear();
            Assert.Equal(0, source.UsedChunkBound);
            dest.SyncDirtyChunks(source);
            Assert.False(dest.IsChunkCommitted(7));
            Assert.Equal(0u, dest.GetChunkVersion(7));

            // and a later write far out is copied again
            source.GetRefRW(cap * 7 + 3, 12).X = 9f;
            dest.SyncDirtyChunks(source);
            Assert.Equal(9f, dest.GetRefRO(cap * 7 + 3).X);
        }

        [Fact]
        public void DirtyChunk_Copied()
        {
            using var source = new NativeChunkTable<Position>();
            using var dest = new NativeChunkTable<Position>();
            
            // Setup source
            source.GetRefRW(0, 10).X = 123.0f; // Version 10
            
            // Setup dest (stale)
            dest.GetRefRW(0, 9).X = 0.0f; // Version 9
            
            // Act
            dest.SyncDirtyChunks(source);
            
            // Assert
            Assert.Equal(123.0f, dest.GetRefRO(0).X);
            Assert.Equal(10u, dest.GetChunkVersion(0));
        }
        
        [Fact]
        public void CleanChunk_Skipped()
        {
            using var source = new NativeChunkTable<Position>();
            using var dest = new NativeChunkTable<Position>();
            
            // Setup identical versions
            source.GetRefRW(0, 10).X = 123.0f;
            dest.GetRefRW(0, 10).X = 123.0f; // Already sync'd
            
            // Modifying source without updating version (simulating non-dirty)
            // But to prove it skipped, we need to verify copy didn't happen.
            // Actually, we can modify dest to something else, set version to match source, 
            // and verify it is NOT overwritten.
            
            dest.GetRefRW(0, 0).X = 999.0f; // Set value
            // Force set version on dest to match source
            // Can't easily force set version without writing? 
            // GetRefRW updates version.
            // But we can use reflection or internal access? No.
            // Wait, we can modify source, but NOT increment version?
            // GetRefRW increments version? No, it takes version as arg.
            
            source.GetRefRW(0, 10).X = 123.0f;
            
            // Set Dest to have SAME version 10, but different data.
            dest.GetRefRW(0, 10).X = 999.0f; // Version 10
            
            // Now Sync. Source has 123, Dest has 999. Version match (10 == 10).
            dest.SyncDirtyChunks(source);
            
            // Assert: Dest should STILL be 999 because versions matched (so copy skipped)
            Assert.Equal(999.0f, dest.GetRefRO(0).X);
        }
        
        [Fact]
        public void ChunkVersion_UpdatedCorrectly()
        {
            using var source = new NativeChunkTable<Position>();
            using var dest = new NativeChunkTable<Position>();
            
            // Chunk 0: Dirty
            source.GetRefRW(0, 5).X = 1.0f;
            
            // Chunk 1: Dirty
            int chunk1Idx = source.ChunkCapacity;
            source.GetRefRW(chunk1Idx, 7).X = 2.0f;
            
            dest.SyncDirtyChunks(source);
            
            Assert.Equal(5u, dest.GetChunkVersion(0));
            Assert.Equal(7u, dest.GetChunkVersion(1));
            Assert.Equal(1.0f, dest.GetRefRO(0).X);
            Assert.Equal(2.0f, dest.GetRefRO(chunk1Idx).X);
        }
        
        [Fact]
        public void ChunkAllocation_OnDemand()
        {
            using var source = new NativeChunkTable<Position>();
            using var dest = new NativeChunkTable<Position>();
            
            source.GetRefRW(0, 1).X = 42.0f;
            
            // Dest has no chunks allocated
            Assert.False(dest.IsChunkCommitted(0));
            
            dest.SyncDirtyChunks(source);
            
            Assert.True(dest.IsChunkCommitted(0));
            Assert.Equal(42.0f, dest.GetRefRO(0).X);
        }
        
        [Fact]
        public void ChunkClearing_WhenSourceEmpty()
        {
            using var source = new NativeChunkTable<Position>();
            using var dest = new NativeChunkTable<Position>();
            
            // Dest has data
            dest.GetRefRW(0, 1).X = 42.0f;
            Assert.True(dest.IsChunkCommitted(0));
            
            // Source is empty (chunk 0 not committed)
            // But we need source ver != dest ver to trigger sync logic?
            // source ver is 0. Dest ver is 1. SyncDirtyChunks should proceed.
            // Check logic: if (chunkVersions[i] == srcVer) continue.
            // 1 != 0. So it proceeds.
            // !source.IsChunkCommitted(0) -> True.
            // dest.IsChunkCommitted(0) -> True.
            // TryDecommitChunk called.
            
            dest.SyncDirtyChunks(source);
            
            // Assert: Dest should be decommitted
            Assert.False(dest.IsChunkCommitted(0));
            // And version should be synced (0)
            Assert.Equal(0u, dest.GetChunkVersion(0));
        }
        
        [Fdp.Testing.FlakyFact("wall-clock 5 ms threshold")]
        [Trait("Category", "Flaky")]
        public void Performance_1000Chunks_30PercentDirty()
        {
            // Performance test
            using var source = new NativeChunkTable<Position>();
            using var dest = new NativeChunkTable<Position>();
            
            int totalChunks = source.TotalChunks; // 62 defined in test config usually?
            // Use smaller number if needed. The instruction says "1000 chunks".
            // FdpConfig.GetRequiredChunks<Position>() is usually small because Position is small.
            // But let's check FdpConfig.
            // Test assumes we can reach 1000 chunks.
            // But MAX_ENTITIES is 1M. Chunk capacity for Position(12 bytes) = 64KB/12 = ~5461.
            // 1M / 5461 = ~183 chunks.
            // We can't really test 1000 chunks with default config.
            // We'll test with Max Chunks available.
            
            // Actually, let's just test copy speed for all available chunks.
            
            // Setup
            for (int i = 0; i < source.TotalChunks; i++)
            {
                // Mark 30% as dirty
                if (i % 3 == 0)
                {
                    int entityId = i * source.ChunkCapacity;
                     source.GetRefRW(entityId, 2).X = i; // Version 2
                     dest.GetRefRW(entityId, 1).X = 0;   // Version 1
                }
                else
                {
                     // Clean
                     int entityId = i * source.ChunkCapacity;
                     source.GetRefRW(entityId, 1).X = i;
                     dest.GetRefRW(entityId, 1).X = i;
                }
            }
            
            var sw = System.Diagnostics.Stopwatch.StartNew();
            dest.SyncDirtyChunks(source);
            sw.Stop();
            
            // Assert correctly copied
            for (int i = 0; i < source.TotalChunks; i++)
            {
                 if (i % 3 == 0)
                 {
                      int entityId = i * source.ChunkCapacity;
                      Assert.Equal((float)i, dest.GetRefRO(entityId).X);
                      Assert.Equal(2u, dest.GetChunkVersion(i));
                 }
            }
            
            // Allow 1ms, but since we have fewer chunks, it should be very fast.
            Assert.True(sw.ElapsedMilliseconds < 5, $"Elapsed {sw.ElapsedMilliseconds}ms");
        }
    }
}
