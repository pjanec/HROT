using System;
using System.Numerics;
using CarKinem.Spatial;
using Fdp.Core;
using Fdp.Core.Collections;
using Xunit;

namespace CarKinem.Tests.Spatial
{
    public class SpatialHashGridTests
    {
        /// <summary>
        /// ⭐⭐⭐ <c>CE-3152</c> — <b>the empty-cell invariant, and why this test was GREEN while the grid was broken.</b>
        /// <para>📌 Measured live `2026-10-09`: a fresh grid had <c>GridHead[0] == 0</c> and <c>GridNext[0] == 0</c>, so
        /// walking cell 0 followed <c>0 → 0 → 0</c> forever and hung the editor (<c>CE-3146</c>). In this structure
        /// <b>-1 means empty</b>, but <see cref="Fdp.Core.Collections.NativeArray{T}"/> deliberately ZERO-fills, and
        /// <c>Create</c> never wrote the sentinel — only <c>Clear</c>/<c>Rebase</c> did.</para>
        /// <para>⛔⛔ <b>This test asserted width/height/cell size/count and never the heads</b>, so it passed on a grid
        /// whose every cell claimed slot 0. ⚠ And the blindness was systemic, not local: <b>every other test in this
        /// file calls <c>grid.Clear()</c> immediately after <c>Create()</c></b> — the suite worked around the missing
        /// initialisation, production did not. ⇒ the assertion below is the one that had to exist.</para>
        /// </summary>
        [Fact]
        public void Create_InitializesGrid()
        {
            var grid = SpatialHashGrid.Create(10, 10, 5f, 100, Allocator.Persistent);

            Assert.Equal(10, grid.Width);
            Assert.Equal(10, grid.Height);
            Assert.Equal(5f, grid.CellSize);
            Assert.Equal(0, grid.EntityCount);

            // ⭐ CE-3152 — EVERY cell must read "empty" straight out of Create, with no Clear() in between.
            for (int c = 0; c < grid.Width * grid.Height; c++)
            {
                Assert.Equal(-1, grid.GridHead[c]);
            }
            Assert.Equal(0, grid.FreeListCount);

            grid.Dispose();
        }

        /// <summary>
        /// ⭐⭐ <c>CE-3152</c> — a fresh grid must be WALKABLE, i.e. the chain from every cell terminates. This is the
        /// shape the consumer (<c>VisionBroadphase.Rebuild</c>) actually relies on, asserted at the grid level so the
        /// invariant is owned here rather than by its caller's guard.
        /// </summary>
        [Fact]
        public void CE3152_AFreshGrid_HasNoWalkableChainFromAnyCell()
        {
            var grid = SpatialHashGrid.Create(10, 10, 5f, 100, Allocator.Persistent);
            try
            {
                int capacity = grid.GridValues.Length;
                for (int c = 0; c < grid.Width * grid.Height; c++)
                {
                    int steps = 0;
                    for (int head = grid.GridHead[c]; head >= 0; head = grid.GridNext[head])
                    {
                        Assert.True(++steps <= capacity,
                            $"cell {c} does not terminate — walked {steps} slots of {capacity}. A fresh grid must be empty.");
                    }
                    Assert.Equal(0, steps);   // fresh ⇒ nothing in any cell
                }
            }
            finally
            {
                grid.Dispose();
            }
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

        /// <summary>
        /// ⭐ CE-3018 — a REBASE is seen by every value copy (the geometry lives in shared native memory, like the cells)
        /// and needs no reallocation: a copy made BEFORE the rebase indexes and finds an entity far outside the old extent.
        /// </summary>
        [Fact]
        public void Rebase_IsSeenByEveryCopy_CE3018()
        {
            var grid = SpatialHashGrid.Create(100, 100, 5f, 100, Allocator.Persistent);   // covers [0,500)²
            var copy = grid;                                                              // e.g. the broadphase's copy
            var e = new Entity(7, 1);

            copy.Add(e, new Vector2(4000f, 4000f));                                       // outside: dropped
            Span<(Entity entity, Vector2 pos)> hits = stackalloc (Entity, Vector2)[4];
            Assert.Equal(0, grid.QueryNeighbors(new Vector2(4000f, 4000f), 1f, hits));

            grid.Rebase(0f, 0f, 50f);                                                     // now covers [0,5000)²
            Assert.Equal(50f, copy.CellSize);
            copy = grid;                                                                  // the rebase cleared the cells
            copy.Add(e, new Vector2(4000f, 4000f));

            Assert.Equal(1, grid.QueryNeighbors(new Vector2(4000f, 4000f), 1f, hits));
            Assert.Equal(e, hits[0].entity);
            Assert.Throws<ArgumentOutOfRangeException>(() => grid.Rebase(0f, 0f, 0f));
            grid.Dispose();
        }
    }
}
