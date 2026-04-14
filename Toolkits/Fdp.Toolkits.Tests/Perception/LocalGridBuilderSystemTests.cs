using System;
using System.Numerics;
using CarKinem.Spatial;
using FDP.Toolkit.Perception.Systems;
using Fdp.Kernel;
using Fdp.Kernel.Collections;
using Fdp.ModuleHost_Core.Abstractions;
using Xunit;

namespace FDP.Toolkit.Perception.Tests
{
    /// <summary>
    /// Unit tests for <see cref="LocalGridBuilderSystem"/> incremental-update behaviour
    /// (BATCH-09 Task 1 / DEBT-09).
    ///
    /// <para>Test pattern:</para>
    /// <list type="number">
    ///   <item>Create a world via <see cref="PerceptionTestWorldFactory"/>.</item>
    ///   <item>Create a <see cref="SpatialHashGrid"/> and pass it to the system.</item>
    ///   <item>Call <c>sys.Execute(view, 0f)</c> to drive both first-tick (full rebuild)
    ///     and subsequent-tick (incremental) paths.</item>
    ///   <item>Assert grid state via <c>QueryNeighbors</c>.</item>
    /// </list>
    /// </summary>
    public class LocalGridBuilderSystemTests
    {
        // ── Helpers ───────────────────────────────────────────────────────────

        private static SpatialHashGrid CreateTestGrid()
            => SpatialHashGrid.Create(100, 100, 5f, 1000, Allocator.Persistent);

        // ── Test 1 ────────────────────────────────────────────────────────────

        /// <summary>
        /// BATCH-09 Task 1: After the first <c>Execute</c> call (full-rebuild path), the
        /// entity is queryable at its initial position via <c>QueryNeighbors</c>.
        /// </summary>
        [Fact]
        public void LocalGridBuilder_FirstTick_FullRebuild_EntityIsQueryable()
        {
            var world = PerceptionTestWorldFactory.Create();
            var grid  = CreateTestGrid();
            var sys   = new LocalGridBuilderSystem(grid);

            var e = world.CreateEntity();
            world.AddComponent(e, new SimTransform { Position = new Vector3(50f, 50f, 0f) });

            ISimulationView view = world;
            sys.Execute(view, 0f);

            Span<(Entity entity, Vector2 pos)> results = stackalloc (Entity, Vector2)[10];
            int count = grid.QueryNeighbors(new Vector2(50f, 50f), 1f, results);
            Assert.Equal(1, count);
            Assert.Equal(e, results[0].entity);

            grid.Dispose();
        }

        // ── Test 2 ────────────────────────────────────────────────────────────

        /// <summary>
        /// BATCH-09 Task 1: When an entity moves between ticks the incremental path
        /// removes it from the old cell and inserts it at the new cell — so it is
        /// queryable only at the new position.
        /// </summary>
        [Fact]
        public void LocalGridBuilder_Incremental_MovedEntity_IsFoundAtNewCell()
        {
            var world = PerceptionTestWorldFactory.Create();
            var grid  = CreateTestGrid();
            var sys   = new LocalGridBuilderSystem(grid);

            var e = world.CreateEntity();
            world.AddComponent(e, new SimTransform { Position = new Vector3(10f, 10f, 0f) });

            ISimulationView view = world;
            sys.Execute(view, 0f); // tick 1: full rebuild — entity at (10, 10).

            // Move the entity to (80, 80).
            world.SetComponent(e, new SimTransform { Position = new Vector3(80f, 80f, 0f) });
            sys.Execute(view, 0f); // tick 2: incremental — Remove from (10,10), Add to (80,80).

            Span<(Entity entity, Vector2 pos)> results = stackalloc (Entity, Vector2)[10];

            // Must NOT be found at old position.
            int oldCount = grid.QueryNeighbors(new Vector2(10f, 10f), 1f, results);
            Assert.Equal(0, oldCount);

            // MUST be found at new position.
            int newCount = grid.QueryNeighbors(new Vector2(80f, 80f), 1f, results);
            Assert.Equal(1, newCount);
            Assert.Equal(e, results[0].entity);

            grid.Dispose();
        }

        // ── Test 3 ────────────────────────────────────────────────────────────

        /// <summary>
        /// BATCH-09 Task 1: When some entities move and others stay still, unchanged
        /// entities remain queryable at their original positions after the incremental pass.
        /// </summary>
        [Fact]
        public void LocalGridBuilder_Incremental_StaticEntities_RemainQueryable()
        {
            var world = PerceptionTestWorldFactory.Create();
            var grid  = CreateTestGrid();
            var sys   = new LocalGridBuilderSystem(grid);

            var moving = world.CreateEntity();
            world.AddComponent(moving, new SimTransform { Position = new Vector3(10f, 10f, 0f) });

            var stationary = world.CreateEntity();
            world.AddComponent(stationary, new SimTransform { Position = new Vector3(60f, 60f, 0f) });

            ISimulationView view = world;
            sys.Execute(view, 0f); // tick 1: full rebuild.

            // Move only the first entity.
            world.SetComponent(moving, new SimTransform { Position = new Vector3(30f, 30f, 0f) });
            sys.Execute(view, 0f); // tick 2: incremental.

            Span<(Entity entity, Vector2 pos)> results = stackalloc (Entity, Vector2)[10];

            // Stationary entity unchanged.
            int countStationary = grid.QueryNeighbors(new Vector2(60f, 60f), 1f, results);
            Assert.Equal(1, countStationary);
            Assert.Equal(stationary, results[0].entity);

            // Moving entity at new position.
            int countMoved = grid.QueryNeighbors(new Vector2(30f, 30f), 1f, results);
            Assert.Equal(1, countMoved);
            Assert.Equal(moving, results[0].entity);

            grid.Dispose();
        }

        // ── Test 4 ────────────────────────────────────────────────────────────

        /// <summary>
        /// BATCH-09 Task 1: When the entity count changes (simulated by creating a new
        /// entity on the second tick), the system performs a full rebuild — all entities
        /// are queryable after the rebuild including the newly added one.
        /// </summary>
        [Fact]
        public void LocalGridBuilder_FullRebuild_OnEntityCountChange()
        {
            var world = PerceptionTestWorldFactory.Create();
            var grid  = CreateTestGrid();
            var sys   = new LocalGridBuilderSystem(grid);

            var e1 = world.CreateEntity();
            world.AddComponent(e1, new SimTransform { Position = new Vector3(10f, 10f, 0f) });

            ISimulationView view = world;
            sys.Execute(view, 0f); // tick 1: first entity.

            // Add a second entity — count changes from 1 to 2.
            var e2 = world.CreateEntity();
            world.AddComponent(e2, new SimTransform { Position = new Vector3(50f, 50f, 0f) });
            sys.Execute(view, 0f); // tick 2: full rebuild triggered by count change.

            Span<(Entity entity, Vector2 pos)> results = stackalloc (Entity, Vector2)[10];

            int count1 = grid.QueryNeighbors(new Vector2(10f, 10f), 1f, results);
            Assert.Equal(1, count1);
            Assert.Equal(e1, results[0].entity);

            int count2 = grid.QueryNeighbors(new Vector2(50f, 50f), 1f, results);
            Assert.Equal(1, count2);
            Assert.Equal(e2, results[0].entity);

            grid.Dispose();
        }

        // ── Test 5 ────────────────────────────────────────────────────────────

        /// <summary>
        /// BATCH-10 Task 3 regression: when an entity is destroyed and a new entity is
        /// created with a recycled index (same <c>Index</c>, incremented <c>Generation</c>)
        /// at the same position, the new entity is correctly inserted into the grid.
        ///
        /// <para>With the old <c>Dictionary&lt;int, Vector2&gt;</c> key (index only),
        /// the incremental path would see <c>oldPos == newPos</c> and silently skip the
        /// <c>Add</c> call, causing the new entity to be invisible to neighbor queries
        /// until the next full rebuild.  The entity-keyed <c>Dictionary&lt;Entity, Vector2&gt;</c>
        /// misses on the new generation, correctly falling through to the <c>Add</c> path.</para>
        /// </summary>
        [Fact]
        public void LocalGridBuilder_IndexReuse_NewEntityAtSamePosition_IsInserted()
        {
            var world = PerceptionTestWorldFactory.Create();
            var grid  = CreateTestGrid();
            var sys   = new LocalGridBuilderSystem(grid);

            const float TestX = 25f, TestY = 25f;

            // Tick 1: spawn e1 at (25,25), full rebuild.
            var e1 = world.CreateEntity();
            world.AddComponent(e1, new SimTransform { Position = new Vector3(TestX, TestY, 0f) });

            ISimulationView view = world;
            sys.Execute(view, 0f);

            // Destroy e1, create e2 at the SAME position — entity count stays 1.
            // EntityIndex recycles e1.Index (free-list) so e2 has the same Index
            // but a higher Generation, making Entity(e1) != Entity(e2).
            world.DestroyEntity(e1);
            var e2 = world.CreateEntity();
            world.AddComponent(e2, new SimTransform { Position = new Vector3(TestX, TestY, 0f) });

            // Precondition: index reuse actually happened.
            Assert.Equal(e1.Index, e2.Index);
            Assert.NotEqual(e1.Generation, e2.Generation);

            // Tick 2: incremental path (count still 1).
            sys.Execute(view, 0f);

            // e2 must be present in the neighbor query results.
            // The entity-keyed fix ensures the new entity (different generation) is
            // not skipped even though oldPos == newPos would have triggered a skip
            // in the old index-keyed implementation.
            Span<(Entity entity, Vector2 pos)> results = stackalloc (Entity, Vector2)[10];
            int count = grid.QueryNeighbors(new Vector2(TestX, TestY), 1f, results);

            bool newEntityFound = false;
            for (int i = 0; i < count; i++)
            {
                if (results[i].entity == e2)
                    newEntityFound = true;
            }

            Assert.True(newEntityFound,
                $"Entity e2 (recycled index, new generation) must be present in the perception " +
                $"grid after the incremental tick. count={count}. " +
                $"Regression: old index-keyed code silently skipped the Add when oldPos == newPos.");

            grid.Dispose();
        }

        // ── Test 6 ────────────────────────────────────────────────────────────

        /// <summary>
        /// BATCH-11 Task 1: After index reuse at stable entity count, the dead entity's
        /// slot must be evicted from the grid so that <c>QueryNeighbors</c> never returns
        /// a dead handle (stale-slot correctness).
        ///
        /// <para>This test targets the second half of the stale-slot bug: BATCH-10 Task 3
        /// fixed the <em>insert</em> path (new entity was silently skipped when
        /// <c>oldPos == newPos</c>), but the dead entity's slot remained in the grid until
        /// the next count-change full rebuild.  A query could therefore return both e2
        /// <em>and</em> the dead e1.</para>
        /// </summary>
        [Fact]
        public void LocalGridBuilder_IndexReuse_DeadEntity_NotReturnedByQueryNeighbors()
        {
            var world = PerceptionTestWorldFactory.Create();
            var grid  = CreateTestGrid();
            var sys   = new LocalGridBuilderSystem(grid);

            const float TestX = 25f, TestY = 25f;

            // Tick 1: spawn e1, full rebuild.
            var e1 = world.CreateEntity();
            world.AddComponent(e1, new SimTransform { Position = new Vector3(TestX, TestY, 0f) });

            ISimulationView view = world;
            sys.Execute(view, 0f);

            // Destroy e1, create e2 with recycled index at same position — count stays 1.
            world.DestroyEntity(e1);
            var e2 = world.CreateEntity();
            world.AddComponent(e2, new SimTransform { Position = new Vector3(TestX, TestY, 0f) });

            // Precondition: index reuse confirmed.
            Assert.Equal(e1.Index, e2.Index);
            Assert.NotEqual(e1.Generation, e2.Generation);

            // Tick 2: incremental path — dead e1 slot must be evicted.
            sys.Execute(view, 0f);

            Span<(Entity entity, Vector2 pos)> results = stackalloc (Entity, Vector2)[10];
            int count = grid.QueryNeighbors(new Vector2(TestX, TestY), 1f, results);

            // Dead entity e1 must NOT appear in the results.
            for (int i = 0; i < count; i++)
            {
                Assert.NotEqual(e1, results[i].entity);
            }

            // Live entity e2 must still be present.
            bool e2Found = false;
            for (int i = 0; i < count; i++)
            {
                if (results[i].entity == e2) e2Found = true;
            }
            Assert.True(e2Found,
                $"Entity e2 (recycled index) must remain present in the grid after stale-slot eviction. count={count}.");

            grid.Dispose();
        }
    }
}
