using System;
using System.Numerics;
using CarKinem.Spatial;
using Fdp.Kernel;
using Fdp.Kernel.Collections;
using Xunit;

namespace CarKinem.Tests.Spatial
{
    public class SpatialHashGridTests
    {
        [Fact]
        public void Create_InitializesGrid()
        {
            var grid = SpatialHashGrid.Create(10, 10, 5f, 100, Allocator.Persistent);
            
            Assert.Equal(10, grid.Width);
            Assert.Equal(10, grid.Height);
            Assert.Equal(5f, grid.CellSize);
            Assert.Equal(0, grid.EntityCount);
            
            grid.Dispose();
        }
        
        [Fact]
        public void Add_InsertsEntityInCorrectCell()
        {
            var grid = SpatialHashGrid.Create(10, 10, 5f, 100, Allocator.Persistent);
            grid.Clear();
            
            // Add entity at (7.5, 7.5) -> should be in cell (1, 1)
            // CellX = 7.5 / 5 = 1
            // CellY = 7.5 / 5 = 1
            // CellIdx = 1 * 10 + 1 = 11
            var e42 = new Entity(42, 1);
            grid.Add(entity: e42, position: new Vector2(7.5f, 7.5f));
            
            Assert.Equal(1, grid.EntityCount);
            
            // Cell (1,1) should have entity
            int cellIdx = 11;
            Assert.NotEqual(-1, grid.GridHead[cellIdx]);
            
            // Verify content â€” GridValues stores Entity, not raw int
            int entityIdx = grid.GridHead[cellIdx];
            Assert.Equal(e42, grid.GridValues[entityIdx]);
            
            grid.Dispose();
        }
        
        [Fact]
        public void QueryNeighbors_FindsEntitiesWithinRadius()
        {
            var grid = SpatialHashGrid.Create(20, 20, 5f, 100, Allocator.Persistent);
            grid.Clear();
            
            // Add entities
            var e1 = new Entity(1, 1);
            var e2 = new Entity(2, 1);
            var e3 = new Entity(3, 1);
            grid.Add(e1, new Vector2(10, 10));
            grid.Add(e2, new Vector2(12, 10)); // 2m away
            grid.Add(e3, new Vector2(20, 10)); // 10m away
            
            // Query within 3m radius
            Span<(Entity entity, Vector2 pos)> results = stackalloc (Entity, Vector2)[10];
            int count = grid.QueryNeighbors(new Vector2(10, 10), radius: 3f, results);
            
            Assert.Equal(2, count); // Should find entities 1 and 2
            
            // Verify results contain expected entities
            bool found1 = false;
            bool found2 = false;
            for(int i=0; i<count; i++)
            {
                if (results[i].entity == e1) found1 = true;
                if (results[i].entity == e2) found2 = true;
            }
            Assert.True(found1, "Should find entity 1");
            Assert.True(found2, "Should find entity 2");
            
            grid.Dispose();
        }
        
        [Fact]
        public void QueryNeighbors_ExcludesEntitiesOutsideRadius()
        {
            var grid = SpatialHashGrid.Create(20, 20, 5f, 100, Allocator.Persistent);
            grid.Clear();
            
            var e1 = new Entity(1, 1);
            var e2 = new Entity(2, 1);
            grid.Add(e1, new Vector2(10, 10));
            grid.Add(e2, new Vector2(25, 25)); // Far away
            
            Span<(Entity, Vector2)> results = stackalloc (Entity, Vector2)[10];
            int count = grid.QueryNeighbors(new Vector2(10, 10), radius: 5f, results);
            
            Assert.Equal(1, count); // Only entity 1
            
            grid.Dispose();
        }
        
        [Fact]
        public void Clear_ResetsGrid()
        {
            var grid = SpatialHashGrid.Create(10, 10, 5f, 100, Allocator.Persistent);
            grid.Clear();
            
            var e1 = new Entity(1, 1);
            grid.Add(e1, new Vector2(5, 5));
            Assert.Equal(1, grid.EntityCount);
            
            grid.Clear();
            Assert.Equal(0, grid.EntityCount);
            
            // Check grid head is reset
            int cellIdx = (int)(5/5)*10 + (int)(5/5);
            Assert.Equal(-1, grid.GridHead[cellIdx]);
            
            grid.Dispose();
        }

        /// <summary>
        /// DEBT-009: Verifies that <see cref="SpatialHashGrid.QueryNeighbors"/> returns full
        /// <see cref="Entity"/> handles â€” including the <c>Generation</c> field â€” not just
        /// raw integer indices. A caller cannot detect stale references from raw ints alone.
        /// </summary>
        [Fact]
        public void SpatialHashGrid_QueryNeighbors_ReturnsFullEntity_NotRawIndex()
        {
            var grid = SpatialHashGrid.Create(20, 20, 5f, 100, Allocator.Persistent);
            grid.Clear();

            // Create an entity with a non-default generation (generation=3) to distinguish
            // it from a default/null entity and prove generation is preserved round-trip.
            var original = new Entity(5, 3); // index=5, generation=3
            grid.Add(original, new Vector2(0f, 0f));

            Span<(Entity entity, Vector2 pos)> results = stackalloc (Entity, Vector2)[10];
            int count = grid.QueryNeighbors(new Vector2(0f, 0f), radius: 1f, results);

            Assert.Equal(1, count);
            // Generational equality — both Index AND Generation must match.
            Assert.Equal(5,            results[0].entity.Index);
            Assert.Equal((ushort)3,    results[0].entity.Generation);
            Assert.Equal(original,     results[0].entity); // struct equality (both fields)

            grid.Dispose();
        }

        // ── BATCH-09: Remove + free-list tests ───────────────────────────────

        /// <summary>
        /// BATCH-09 Task 1: <see cref="SpatialHashGrid.Remove"/> splices the entity out
        /// of the linked-list chain for its cell so subsequent <see cref="SpatialHashGrid.QueryNeighbors"/>
        /// calls no longer return it.
        /// </summary>
        [Fact]
        public void SpatialHashGrid_Remove_SplicesEntryFromLinkedList()
        {
            var grid = SpatialHashGrid.Create(20, 20, 5f, 100, Allocator.Persistent);
            grid.Clear();

            var e1 = new Entity(1, 1);
            var e2 = new Entity(2, 1);
            grid.Add(e1, new Vector2(10f, 10f));
            grid.Add(e2, new Vector2(10f, 10f)); // same cell

            // Both entities visible before removal.
            Span<(Entity entity, Vector2 pos)> results = stackalloc (Entity, Vector2)[10];
            Assert.Equal(2, grid.QueryNeighbors(new Vector2(10f, 10f), 1f, results));

            // Remove e1.
            bool removed = grid.Remove(e1, new Vector2(10f, 10f));
            Assert.True(removed);

            // Only e2 should remain in the query.
            int count = grid.QueryNeighbors(new Vector2(10f, 10f), 1f, results);
            Assert.Equal(1, count);
            Assert.Equal(e2, results[0].entity);

            grid.Dispose();
        }

        /// <summary>
        /// BATCH-09 Task 1: A slot freed by <see cref="SpatialHashGrid.Remove"/> must be
        /// reused by a subsequent <see cref="SpatialHashGrid.Add"/> call so that
        /// <see cref="SpatialHashGrid.EntityCount"/> stays bounded and the new entity is
        /// queryable from its new cell.
        /// </summary>
        [Fact]
        public void SpatialHashGrid_FreeList_ReusesSlotsAfterRemove()
        {
            var grid = SpatialHashGrid.Create(20, 20, 5f, 100, Allocator.Persistent);
            grid.Clear();

            var e1 = new Entity(1, 1);
            grid.Add(e1, new Vector2(10f, 10f));
            int countBefore = grid.EntityCount; // should be 1

            // Remove, then re-add at a different position.
            bool removed = grid.Remove(e1, new Vector2(10f, 10f));
            Assert.True(removed);

            // FreeListCount should be 1 (slot 0 is now recycled).
            Assert.Equal(1, grid.FreeListCount);

            // Re-add the entity to a new cell — should reuse the freed slot.
            grid.Add(e1, new Vector2(50f, 50f));
            // EntityCount must NOT have grown beyond countBefore (slot was recycled).
            Assert.Equal(countBefore, grid.EntityCount);
            Assert.Equal(0, grid.FreeListCount);

            // Entity must be queryable at its new position.
            Span<(Entity entity, Vector2 pos)> results = stackalloc (Entity, Vector2)[10];
            int countNew = grid.QueryNeighbors(new Vector2(50f, 50f), 1f, results);
            Assert.Equal(1, countNew);
            Assert.Equal(e1, results[0].entity);

            // Old position must return nothing.
            int countOld = grid.QueryNeighbors(new Vector2(10f, 10f), 1f, results);
            Assert.Equal(0, countOld);

            grid.Dispose();
        }

        /// <summary>
        /// BATCH-09 Task 1: <see cref="SpatialHashGrid.Remove"/> returns <c>false</c> for
        /// an entity that was never added.  No state corruption should occur.
        /// </summary>
        [Fact]
        public void SpatialHashGrid_Remove_ReturnsFalse_WhenEntityNotPresent()
        {
            var grid = SpatialHashGrid.Create(10, 10, 5f, 100, Allocator.Persistent);
            grid.Clear();

            var e = new Entity(99, 2);
            bool result = grid.Remove(e, new Vector2(5f, 5f));
            Assert.False(result);
            Assert.Equal(0, grid.FreeListCount);

            grid.Dispose();
        }
    }
}
