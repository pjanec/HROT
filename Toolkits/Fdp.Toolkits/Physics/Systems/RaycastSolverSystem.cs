using System;
using System.Numerics;
using System.Threading.Tasks;
using CarKinem.Spatial;
using Fdp.Core;
using Fdp.Toolkit.Physics.Components;
using Fdp.Toolkit.Physics.Math;

namespace Fdp.Toolkit.Physics.Systems
{
    /// <summary>
    /// Main-thread system that resolves all pending <see cref="RaycastRequest"/>s in
    /// <see cref="RaycastBatchData"/> using a broad-phase spatial-hash query followed by
    /// per-entity <see cref="Intersection2D.RaycastCircle"/> narrow-phase checks.
    /// <para>
    /// <b>Execution phase:</b> <see cref="InputSystemGroup"/> — runs before the simulation
    /// group so that hit results are available to downstream systems within the same frame.
    /// </para>
    /// <para>
    /// <b>Parallelism:</b> The narrow-phase loop uses <see cref="System.Threading.Tasks.Parallel.For"/>
    /// so that each ray is resolved independently. Each iteration <c>i</c> writes
    /// <em>only</em> to <c>batch.Hits[i]</c> — there is no shared write target across
    /// iterations, making the writes inherently thread-safe.
    /// </para>
    /// <para>
    /// <b>Thread-safety of <c>World.GetComponent&lt;T&gt;</c> inside Parallel.For:</b>
    /// <see cref="EntityRepository"/> states that component access is not formally thread-safe.
    /// In practice the reads here are safe because:
    /// (a) No other system in <c>InputSystemGroup</c> writes to <c>SimTransform</c> or
    ///     <c>PhysicsCollider</c> concurrently.
    /// (b) <c>Dictionary&lt;Type, IComponentTable&gt;</c> (.NET) allows concurrent reads when no
    ///     write is in progress — the table lookup is read-only once all components are registered.
    /// (c) The underlying <c>ComponentTable&lt;T&gt;</c> storage is a contiguous native array;
    ///     different threads read different indices, so no cache-line sharing issues arise.
    /// See BATCH-08 report Q2 for the full analysis.
    /// </para>
    /// </summary>
    [UpdateInGroup(typeof(InputSystemGroup))]
    public class RaycastSolverSystem : ComponentSystem
    {
        protected override void OnUpdate()
        {
            if (!World.HasSingleton<RaycastBatchData>()) return;
            ref var batch = ref World.GetSingleton<RaycastBatchData>();
            if (batch.Count == 0) return;

            if (!World.HasSingleton<SpatialGridData>()) return;
            // Value-copy of the SpatialGridData struct — carries native pointers to grid arrays.
            // Reading grid data from multiple threads is safe: the grid is written only by
            // SpatialHashSystem which runs in a different group (SimulationSystemGroup).
            var gridData = World.GetSingleton<SpatialGridData>();
            var grid     = gridData.Grid;

            // Cap to array size — prevents IndexOutOfRangeException if upstream overflows the batch.
            // The capacity is capped rather than thrown so excess rays are silently dropped rather than
            // crashing. A Debug.Assert at the fill site would alert during development.
            int count = System.Math.Min(batch.Count, PhysicsConstants.RaycastBatchCapacity);

            // Extract NativeArray structs (value copies that share native pointers) so they
            // can be captured by the Parallel.For lambda without capturing the ref local 'batch'.
            // CS8175: ref locals cannot be used inside lambdas.
            var requests = batch.Requests;
            var hits     = batch.Hits;

            // ── Parallel broad + narrow phase ────────────────────────────────────────────────
            // Each iteration i:
            //   1. Queries the spatial grid for candidate entities near the ray's midpoint.
            //   2. Filters by layer mask and entity validity.
            //   3. Runs Intersection2D.RaycastCircle for each candidate.
            //   4. Writes the closest hit (if any) to hits[i].
            Parallel.For(0, count, i =>
            {
                var req = requests[i];   // value-copy; NativeArray[i] returns ref T, copy for lambda safety

                var start2D = new Vector2(req.Start.X, req.Start.Y);
                var end2D   = new Vector2(req.End.X,   req.End.Y);

                // Broad-phase: AABB query centred on the ray's midpoint.
                Vector2 midpoint     = (start2D + end2D) * 0.5f;
                float   queryRadius  = Vector2.Distance(start2D, end2D) * 0.5f
                                       + PhysicsConstants.QueryExpansionRadius;

                // stackalloc: stack-allocates the candidate buffer (no GC pressure per ray).
                // Requires unsafe context (AllowUnsafeBlocks = true in the project).
                unsafe
                {
                Span<(Entity entity, Vector2 pos)> candidates = stackalloc (Entity, Vector2)[PhysicsConstants.MaxBroadphaseCandidates];
                int nc = grid.QueryNeighbors(midpoint, queryRadius, candidates);

                float  bestT   = float.MaxValue;
                Entity bestEnt = default;
                bool   anyHit  = false;

                // ── Narrow phase ──────────────────────────────────────────────────────────────
                for (int j = 0; j < nc; j++)
                {
                    Entity candidate = candidates[j].entity;

                    // Generational validity check (guards against stale grid entries from
                    // entities destroyed earlier this frame before SpatialHashSystem rebuilt).
                    if (!World.IsAlive(candidate)) continue;

                    // Self-exclusion: skip the ignore entity (e.g. the shooter).
                    // Full generational check: both Index AND Generation must match, so a
                    // re-used entity slot is never accidentally excluded.  Entity.Null.IsNull
                    // is true (Generation == 0), making the struct-zero-default safe.
                    if (!req.IgnoreEntity.IsNull && candidate == req.IgnoreEntity) continue;

                    if (!World.HasComponent<PhysicsCollider>(candidate)) continue;

                    var collider = World.GetComponent<PhysicsCollider>(candidate);

                    // Layer-mask check (bitmask AND must be non-zero to hit).
                    if ((req.LayerMask & collider.CollisionLayer) == 0) continue;

                    var tf  = World.GetComponent<SimTransform>(candidate);
                    var c2D = new Vector2(tf.Position.X, tf.Position.Y);

                    if (Intersection2D.RaycastCircle(start2D, end2D, c2D, collider.Radius, out float t))
                    {
                        if (t < bestT)
                        {
                            bestT   = t;
                            bestEnt = candidate;
                            anyHit  = true;
                        }
                    }
                }

                // Write result — exclusive write to index i (thread-safe by construction).
                hits[i] = new RaycastHit
                {
                    T          = bestT,
                    HitEntity  = bestEnt,
                    RayId      = req.RayId,
                    Observer   = req.Observer,
                    Target     = req.Target,
                    HasHit     = (byte)(anyHit ? 1 : 0),
                    SourceNodeId = req.SourceNodeId,
                };
                } // end unsafe
            });
        }
    }
}
