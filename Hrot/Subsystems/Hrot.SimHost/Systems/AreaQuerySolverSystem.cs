using System;
using System.Collections.Generic;
using System.Numerics;
using CarKinem.Spatial;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Spatial.Eqs;
using Hrot.IG.Components;

namespace Hrot.SimHost.Systems
{
    /// <summary>
    /// Resolves pending <see cref="AreaQueryRequest"/>s in <see cref="AreaQueryBatchData"/>
    /// against the spatial hash grid and polygon areas.
    ///
    /// <para><b>Execution context:</b> runs inside <see cref="Modules.EqsModule"/> at
    /// 10 Hz on a background thread (SoD snapshot).  The <see cref="AreaQueryBatchData"/>
    /// singleton's <see cref="AreaQueryBatchData.Results"/> NativeArray shares its native
    /// memory pointer with the live world, so writes from the background thread are
    /// visible to the Brain BTree tick immediately after <c>IsReady</c> is set to <c>true</c>.
    /// </para>
    ///
    /// <para><b>Result availability:</b> results become available within one
    /// EqsModule tick cycle (nominally 100 ms at 10 Hz).  Brain BTree nodes must poll
    /// <c>AreaQueryBatchData.Results[i].IsReady</c> each frame until <c>true</c>.</para>
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    public sealed class AreaQuerySolverSystem : IEcsModuleSystem
    {
        // Maximum polygon vertex count for stack-allocated geometry buffers.
        private const int MaxPolyVertices = 64;

        // Broad-phase query radius expansion beyond the polygon bounding circle (metres).
        private const float BroadphaseExpansion = 10f;

        /// <inheritdoc/>
        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo)
                throw new InvalidOperationException(
                    $"{nameof(AreaQuerySolverSystem)} requires direct EntityRepository access " +
                    $"and cannot run on a non-EntityRepository view ({view.GetType().Name}).");

            if (!repo.HasSingleton<AreaQueryBatchData>()) return;

            // NativeArray value-copies share the same native memory pointers as the live
            // world — writes through these copies are visible to the Brain tick immediately.
            ref var batch = ref repo.GetSingleton<AreaQueryBatchData>();
            int requestCount = batch.Count;
            if (requestCount == 0) return;
            batch.Count = 0;

            if (!repo.HasSingleton<SpatialGridData>()) return;
            var gridData = repo.GetSingleton<SpatialGridData>();
            var grid = gridData.Grid;

            if (!repo.HasSingleton<EqsTargetPool>()) return;
            ref var pool = ref repo.GetSingleton<EqsTargetPool>();

            for (int i = 0; i < requestCount; i++)
            {
                // Skip already resolved slots to avoid re-processing within the same solver cycle.
                if (batch.Results[i].IsReady) continue;

                var req = batch.Requests[i];

                // Resolve the polygon area entity.
                if (!repo.IsAlive(req.TargetAreaEntity))
                {
                    WriteEmptyResult(ref batch, i, req);
                    continue;
                }

                var polyline = view.GetManagedComponentRO<EditablePolyline>(req.TargetAreaEntity);
                if (polyline == null || polyline.Points == null || polyline.Points.Count < 3)
                {
                    WriteEmptyResult(ref batch, i, req);
                    continue;
                }

                IList<Vector2> points = polyline.Points;
                int nVerts = Math.Min(points.Count, MaxPolyVertices);

                // Compute polygon bounding circle centre and radius for the broad phase.
                Vector2 centroid = Vector2.Zero;
                for (int v = 0; v < nVerts; v++)
                    centroid += points[v];
                centroid /= nVerts;

                float maxDistSq = 0f;
                for (int v = 0; v < nVerts; v++)
                {
                    float d = Vector2.DistanceSquared(centroid, points[v]);
                    if (d > maxDistSq) maxDistSq = d;
                }
                float queryRadius = MathF.Sqrt(maxDistSq) + BroadphaseExpansion;

                // Broad-phase spatial query.
                unsafe
                {
                    const int MaxCandidates = 256;
                    Span<(Entity entity, Vector2 pos)> candidates =
                        stackalloc (Entity, Vector2)[MaxCandidates];
                    int nc = grid.QueryNeighbors(centroid, queryRadius, candidates);

                    // Allocate pool chunk for this request.
                    int maxTargets = AreaQueryBatchData.DefaultCapacity; // max per request
                    if (pool.NextFreeIndex + maxTargets > pool.Targets.Length)
                        pool.NextFreeIndex = 0;
                    int groupHandle = pool.NextFreeIndex;

                    int targetCount = 0;

                    for (int j = 0; j < nc && targetCount < maxTargets; j++)
                    {
                        Entity candidate = candidates[j].entity;
                        if (!repo.IsAlive(candidate)) continue;

                        // Force-affiliation filter.
                        if (!repo.HasComponent<EntityInfo>(candidate)) continue;
                        var info = repo.GetComponent<EntityInfo>(candidate);
                        if (info.ForceId != req.TargetForce) continue;

                        // Precise point-in-polygon test (ray casting algorithm).
                        Vector2 pos2d = candidates[j].pos;
                        if (!PointInPolygon(pos2d, points, nVerts)) continue;

                        // Guard: check pool capacity before writing.
                        int poolIdx = pool.NextFreeIndex + targetCount;
                        if (poolIdx >= pool.Targets.Length)
                        {
                            // Pool full — stop adding targets; result will be partial.
                            break;
                        }

                        pool.Targets[poolIdx] = (long)candidate.PackedValue;
                        targetCount++;
                    }

                    // Advance pool free index by the number of targets stored.
                    pool.NextFreeIndex += targetCount;

                    // Write result. Set IsReady last so the Brain reader sees a consistent result.
                    batch.Results[i] = new AreaQueryResult
                    {
                        RequestId         = req.RequestId,
                        TargetCount       = targetCount,
                        TargetGroupHandle = targetCount > 0 ? groupHandle : -1,
                        SourceNodeId      = req.SourceNodeId,
                        IsReady           = true,
                    };
                }
            }

            // Write back the updated pool free index (the struct itself is a value copy;
            // write it back via SetSingleton so the next iteration starts from the correct offset).
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        private static void WriteEmptyResult(ref AreaQueryBatchData batch, int slot, in AreaQueryRequest req)
        {
            batch.Results[slot] = new AreaQueryResult
            {
                RequestId         = req.RequestId,
                TargetCount       = 0,
                TargetGroupHandle = -1,
                SourceNodeId      = req.SourceNodeId,
                IsReady           = true,
            };
        }

        /// <summary>
        /// 2D point-in-polygon test using the ray casting algorithm.
        /// Zero heap allocations — reads directly from the list by index.
        /// </summary>
        private static bool PointInPolygon(Vector2 point, IList<Vector2> polygon, int nVerts)
        {
            bool inside = false;
            int j = nVerts - 1;
            for (int i = 0; i < nVerts; j = i++)
            {
                float xi = polygon[i].X, yi = polygon[i].Y;
                float xj = polygon[j].X, yj = polygon[j].Y;
                bool intersects =
                    ((yi > point.Y) != (yj > point.Y))
                    && (point.X < (xj - xi) * (point.Y - yi) / (yj - yi) + xi);
                if (intersects) inside = !inside;
            }
            return inside;
        }
    }
}
