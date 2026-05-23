using System;
using Xunit;
using Fdp.Core;

namespace Fdp.Tests
{
    /// <summary>
    /// Tests specific to the hot/cold split in EntityIndex.
    /// Hot table  = NativeChunkTable(BitMask512)   - component masks, 64 bytes/entity.
    /// Cold table = NativeChunkTable(EntityMetadataCold) - lifecycle state, 128 bytes/entity.
    /// </summary>
    public class EntityIndexHotColdTests
    {
        // ---------------------------------------------------------------
        // 1. Destroy zeroes the hot component mask
        // ---------------------------------------------------------------
        [Fact]
        public void CreateDestroy_RoundTrip_HotMaskZeroedOnDestroy()
        {
            using var index = new EntityIndex();

            var e = index.CreateEntity();
            ref var mask = ref index.GetComponentMask(e.Index);
            mask.SetBit(1);
            mask.SetBit(42);

            Assert.True(index.GetComponentMask(e.Index).IsSet(1));

            index.DestroyEntity(e);

            // After destroy the hot mask for that slot must be cleared
            Assert.True(index.GetComponentMask(e.Index).IsEmpty(),
                "Hot component mask must be zeroed after DestroyEntity");
        }

        // ---------------------------------------------------------------
        // 2. Hot and cold tables have different chunk capacities
        // ---------------------------------------------------------------
        [Fact]
        public void HotAndCold_ChunkCapacities_AreDifferent()
        {
            using var index = new EntityIndex();

            int hotCap  = index.GetChunkCapacity();        // BitMask512 = 64 bytes -> 1024/chunk
            int coldCap = index.GetColdChunkCapacity();    // EntityMetadataCold = 128 bytes -> 512/chunk

            // Cold capacity must be strictly smaller (more bytes per entry)
            Assert.True(coldCap < hotCap,
                $"Cold capacity ({coldCap}) should be less than hot capacity ({hotCap})");

            // Sanity: both must be positive powers-of-two-friendly values
            Assert.True(hotCap > 0);
            Assert.True(coldCap > 0);
        }

        // ---------------------------------------------------------------
        // 3. Population counters stay consistent after create/destroy
        // ---------------------------------------------------------------
        [Fact]
        public void PopulationCounters_ConsistentAfterCreateDestroy()
        {
            using var index = new EntityIndex();

            // Create N entities
            int n = 5;
            var entities = new Entity[n];
            for (int i = 0; i < n; i++)
                entities[i] = index.CreateEntity();

            Assert.Equal(n, index.ActiveCount);

            // Destroy the first one
            index.DestroyEntity(entities[0]);

            Assert.Equal(n - 1, index.ActiveCount);

            // Hot chunk population must also reflect the change
            int hotPop = index.GetChunkPopulation(0);
            Assert.Equal(n - 1, hotPop);
        }

        // ---------------------------------------------------------------
        // 4. SyncFrom copies the hot component mask correctly
        // ---------------------------------------------------------------
        [Fact]
        public void SyncFrom_CopiesHotMask_Correctly()
        {
            using var src  = new EntityIndex();
            using var dest = new EntityIndex();

            var e = src.CreateEntity();
            src.GetComponentMask(e.Index).SetBit(7);
            src.GetComponentMask(e.Index).SetBit(15);

            dest.SyncFrom(src);

            Assert.True(dest.GetComponentMask(e.Index).IsSet(7),
                "Bit 7 must be synced to dest");
            Assert.True(dest.GetComponentMask(e.Index).IsSet(15),
                "Bit 15 must be synced to dest");
            Assert.True(dest.IsAlive(e),
                "Entity must be alive in dest after sync");
        }

        // ---------------------------------------------------------------
        // 5. GetChunkLiveness reflects cold IsActive state
        // ---------------------------------------------------------------
        [Fact]
        public void GetChunkLiveness_ReflectsColdIsActive()
        {
            using var index = new EntityIndex();

            var e0 = index.CreateEntity();
            var e1 = index.CreateEntity();
            var e2 = index.CreateEntity();

            // Destroy e1 -> cold IsActive[1] = false
            index.DestroyEntity(e1);

            int cap = index.GetChunkCapacity();
            Span<bool> liveness = stackalloc bool[cap];
            index.GetChunkLiveness(0, liveness);

            Assert.True(liveness[e0.Index],  "e0 should be alive");
            Assert.False(liveness[e1.Index], "e1 should be dead (cold IsActive = false)");
            Assert.True(liveness[e2.Index],  "e2 should be alive");
        }

        // ---------------------------------------------------------------
        // 6. ForceRestoreEntity sets both hot mask and cold metadata
        // ---------------------------------------------------------------
        [Fact]
        public void ForceRestoreEntity_SetsBothHotAndCold()
        {
            using var index = new EntityIndex();

            var mask = new BitMask512();
            mask.SetBit(3);
            mask.SetBit(99);

            ushort generation = 7;
            index.ForceRestoreEntity(5, isActive: true, generation, mask);

            // Cold metadata
            ref readonly var meta = ref index.GetMetadata(5);
            Assert.True(meta.IsActive,           "IsActive must be true in cold metadata");
            Assert.Equal(generation, meta.Generation);

            // Hot mask
            ref readonly var comp = ref index.GetComponentMask(5);
            Assert.True(comp.IsSet(3),  "Bit 3 must be set in hot mask");
            Assert.True(comp.IsSet(99), "Bit 99 must be set in hot mask");
        }

        // ---------------------------------------------------------------
        // 7. Dead entity hot mask is empty (IsEmpty returns true)
        // ---------------------------------------------------------------
        [Fact]
        public void DeadEntity_HotMask_IsEmpty()
        {
            using var index = new EntityIndex();

            var e = index.CreateEntity();
            index.GetComponentMask(e.Index).SetBit(5);

            // Confirm it's set before destroy
            Assert.False(index.GetComponentMask(e.Index).IsEmpty());

            index.DestroyEntity(e);

            Assert.True(index.GetComponentMask(e.Index).IsEmpty(),
                "Hot component mask must be empty (all zeros) after destroy");
        }
    }
}
