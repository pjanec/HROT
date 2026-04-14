using System.Collections.Generic;
using System.Numerics;
using CarKinem.Spatial;
using Fdp.Kernel;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Perception.Systems
{
    /// <summary>
    /// Async module system that rebuilds the <see cref="PerceptionModule"/>'s private
    /// <see cref="SpatialHashGrid"/> from the current simulation snapshot each tick.
    /// <para>
    /// This system runs <b>first</b> inside <see cref="PerceptionModule.Tick"/> so that
    /// subsequent systems (e.g. <see cref="VisionBroadphaseSystem"/>) can query the freshly
    /// rebuilt grid without touching the main-thread's <see cref="SpatialHashGrid"/> singleton.
    /// </para>
    /// <para>
    /// <b>SoD contract:</b> uses only <c>view.GetComponentRO&lt;SimTransform&gt;</c>.
    /// Zero direct world writes — all output is expressed through the shared native-memory
    /// backing of the grid struct passed in at construction time.
    /// </para>
    /// <para>
    /// <b>Struct-copy / shared-memory note:</b>
    /// <see cref="SpatialHashGrid"/> is a value type whose fields are
    /// <see cref="Fdp.Kernel.Collections.NativeArray{T}"/> wrappers around native pointers.
    /// Passing a struct copy here shares those native pointers with the caller's copy, so
    /// all <c>Clear()</c>, <c>Add()</c>, and <c>Remove()</c> mutations are visible to every
    /// other holder of a struct copy (e.g. <see cref="VisionBroadphaseSystem"/>).
    /// <see cref="EntityCount"/> and <see cref="SpatialHashGrid.FreeListCount"/> are updated
    /// locally in this system's copy; they do not need to match the caller's copy because
    /// <c>QueryNeighbors</c> iterates linked-list chains, not a count-bounded range.
    /// </para>
    /// <para>
    /// <b>Incremental update strategy (BATCH-09 / BATCH-10):</b>
    /// Per-entity delta tracking avoids the O(n) <c>Clear()</c>+re-insert when only a subset
    /// of entities have moved.  Keying <c>_prevPositions</c> by the full <see cref="Entity"/>
    /// handle (Index + Generation) rather than <c>Index</c> alone prevents stale-position
    /// mismatches when entity count stays constant but an index is recycled (destroy + create
    /// same count).
    /// <list type="bullet">
    ///   <item><b>Static frame (nothing moved):</b> O(n) dirty-scan, no grid writes — early exit.</item>
    ///   <item><b>Partial movement (k &lt; n entities moved):</b> O(k) <c>Remove</c>+<c>Add</c> pairs.
    ///     Each <c>Remove</c> is O(cell_chain_length) ≈ O(1) for a well-tuned grid.</item>
    ///   <item><b>Count changed (spawn/destroy):</b> full <c>Clear()</c>+re-insert — entity-level
    ///     bookkeeping is invalidated when the entity set changes.</item>
    ///   <item><b>Index recycled at stable count:</b> the new entity has a different
    ///     <c>Generation</c>, so <c>_prevPositions.TryGetValue</c> misses and the new entity
    ///     is treated as a fresh insert.  <c>_liveByIndex</c> (a
    ///     <c>Dictionary&lt;int,&nbsp;Entity&gt;</c> keyed by <c>entity.Index</c>) detects that
    ///     the slot owner has changed and calls <c>_grid.Remove</c> for the dead entity's
    ///     last-known position before the new entity is inserted — ensuring
    ///     <c>QueryNeighbors</c> never returns a stale dead-entity handle (BATCH-11
    ///     stale-slot fix).</item>
    ///   <item><b>Worst case (all entities moved):</b> identical cost to the old full-rebuild
    ///     path, but now with the added benefit of free-list slot reuse reducing allocation
    ///     pressure in the underlying <see cref="SpatialHashGrid.EntityCount"/> counter.</item>
    /// </list>
    /// Memory cost: one <see cref="Vector2"/> per live entity in <c>_prevPositions</c>.
    /// </para>
    /// </summary>
    public class LocalGridBuilderSystem : IEcsModuleSystem
    {
        // Value-copy of the PerceptionModule's grid struct.
        // Shares native-memory pointers with the caller's copy and VisionBroadphaseSystem's copy.
        private SpatialHashGrid _grid;

        // Per-entity position tracking: full Entity handle (Index + Generation) → last known XY position.
        // Keyed by the complete Entity struct so that index reuse after destroy+create at a stable
        // entity count does not incorrectly match the previous entity's position.
        private readonly Dictionary<Entity, Vector2> _prevPositions;
        private int _lastEntityCount = -1;

        // Maps entity Index → the currently live entity occupying that index.
        // Used during the incremental path to detect and evict stale grid slots left
        // when an old entity is destroyed and a new one reuses the same index at a
        // stable entity count (the count-unchanged path skips FullRebuild).
        private readonly Dictionary<int, Entity> _liveByIndex;

        /// <summary>
        /// Initialises the builder with a copy of the module's private grid.
        /// Ownership of the underlying native memory remains with <see cref="PerceptionModule"/>.
        /// </summary>
        public LocalGridBuilderSystem(SpatialHashGrid grid)
        {
            _grid = grid;
            _prevPositions = new Dictionary<Entity, Vector2>();
            _liveByIndex   = new Dictionary<int, Entity>();
        }

        /// <inheritdoc/>
        public void Execute(ISimulationView view, float deltaTime)
        {
            var query = view.Query().With<SimTransform>().Build();

            // ── Count pass — O(n) reads, no grid mutations ────────────────────
            // Count entities and detect whether the entity set has changed.
            int newCount = 0;
            foreach (var entity in query)
                newCount++;

            if (newCount != _lastEntityCount)
            {
                // Entity count changed (spawn or destroy) — full rebuild is cheaper and
                // simpler than reconciling individual additions/removals against the
                // prev-position map, which may reference deleted entity indices.
                FullRebuild(view, query);
                return;
            }

            // ── Incremental update — O(k) where k = number of moved entities ──
            // For each entity whose position changed: remove from old cell and re-insert
            // at the new cell.  Unchanged entities stay in the grid as-is.
            foreach (var entity in query)
            {
                ref readonly var tf = ref view.GetComponentRO<SimTransform>(entity);
                var newPos = new Vector2(tf.Position.X, tf.Position.Y);

                if (_prevPositions.TryGetValue(entity, out var oldPos))
                {
                    if (oldPos == newPos)
                        continue; // Entity did not move — leave in grid.

                    // Entity moved: splice out of old cell, insert at new cell.
                    _grid.Remove(entity, oldPos);
                }
                else
                {
                    // New entity at a stable count: this index was recycled by a destroy+create
                    // pair within the same tick.  Evict the stale slot for the dead entity so
                    // QueryNeighbors never returns a dead handle.
                    if (_liveByIndex.TryGetValue(entity.Index, out var staleEntity) && staleEntity != entity)
                    {
                        if (_prevPositions.TryGetValue(staleEntity, out var stalePos))
                        {
                            _grid.Remove(staleEntity, stalePos);
                            _prevPositions.Remove(staleEntity);
                        }
                    }
                    // Map this index to the new (current) entity.
                    _liveByIndex[entity.Index] = entity;
                }

                _grid.Add(entity, newPos);
                _prevPositions[entity] = newPos;
            }
        }

        // ── Private helpers ───────────────────────────────────────────────────

        /// <summary>
        /// Full clear-and-rebuild.  Called when the entity count changes (spawn/destroy).
        /// Resets all grid state and repopulates <c>_prevPositions</c> from the snapshot.
        /// </summary>
        private void FullRebuild(ISimulationView view, EntityQuery query)
        {
            _grid.Clear();
            _prevPositions.Clear();
            _liveByIndex.Clear();
            _lastEntityCount = 0;

            foreach (var entity in query)
            {
                ref readonly var tf = ref view.GetComponentRO<SimTransform>(entity);
                var pos = new Vector2(tf.Position.X, tf.Position.Y);
                _prevPositions[entity] = pos;
                _liveByIndex[entity.Index] = entity;
                _grid.Add(entity, pos);
                _lastEntityCount++;
            }
        }
    }
}
