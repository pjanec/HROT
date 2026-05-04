using System;
using System.Collections.Generic;
using System.Numerics;
using CarKinem.Spatial;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Spatial.Eqs;
using Hrot.IG.Components;

namespace Hrot.SimHost.Systems
{
    /// <summary>
    /// Resolves pending <see cref="AreaQueryRequestEvent"/>s consumed from the event bus
    /// against the spatial hash grid and polygon areas.
    ///
    /// <para><b>Execution context:</b> runs inside <see cref="Modules.EqsModule"/> at
    /// 10 Hz on a background thread (SoD snapshot).  The <see cref="EqsTargetPool"/>
    /// <c>Targets</c> NativeArray shares its native memory pointer with the live world,
    /// so target handle writes from the background thread are immediately visible.
    /// Struct fields (<c>NextFreeIndex</c>) and ring-buffer results are published as
    /// <see cref="AreaQueryResultEvent"/> via <see cref="IEntityCommandBuffer"/> and
    /// materialized on the main thread by <c>AreaQueryResultMaterializationSystem</c>.
    /// </para>
    ///
    /// <para><b>Result availability:</b> results become available within one
    /// EqsModule tick cycle (nominally 100 ms at 10 Hz) plus one materialization frame.
    /// Brain BTree nodes must poll <c>AreaQueryBatchData.Results[slot].IsReady</c> each
    /// frame until <c>true</c>.</para>
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
            // Read all accumulated request events since the last solver tick.
            var requests = view.ReadEvents<AreaQueryRequestEvent>();
            if (requests.IsEmpty) return;

            // Access live singletons. NativeArray fields share native pointers with the
            // live world, so writes to Targets[] are immediately visible to the Brain tick.
            if (view is not EntityRepository repo)
                throw new InvalidOperationException(
                    $"{nameof(AreaQuerySolverSystem)} requires direct EntityRepository access " +
                    $"and cannot run on a non-EntityRepository view ({view.GetType().Name}).");

            if (!repo.HasSingleton<SpatialGridData>()) return;
            var gridData = repo.GetSingleton<SpatialGridData>();
            var grid = gridData.Grid;

            if (!repo.HasSingleton<EqsTargetPool>()) return;
            ref var pool = ref repo.GetSingleton<EqsTargetPool>();

            var cmd = view.GetCommandBuffer();

            // Track pool cursor locally across iterations within this solver tick.
            int localPoolNext = pool.NextFreeIndex;

            for (int r = 0; r < requests.Length; r++)
            {
                ref readonly var req = ref requests[r];

                // Resolve the polygon area entity.
                if (!repo.IsAlive(req.TargetAreaEntity))
                {
                    PublishEmptyResult(cmd, in req, localPoolNext);
                    continue;
                }

                var polyline = view.GetManagedComponentRO<EditablePolyline>(req.TargetAreaEntity);
                if (polyline == null || polyline.Points == null || polyline.Points.Count < 3)
                {
                    PublishEmptyResult(cmd, in req, localPoolNext);
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

                    // Allocate pool chunk for this request using ring-buffer semantics.
                    int maxTargets = AreaQueryBatchData.DefaultCapacity;
                    if (localPoolNext + maxTargets > pool.Targets.Length)
                        localPoolNext = 0;
                    int groupHandle = localPoolNext;

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
                        int poolIdx = localPoolNext + targetCount;
                        if (poolIdx >= pool.Targets.Length)
                        {
                            // Pool full — stop adding targets; result will be partial.
                            break;
                        }

                        // Write directly to shared native memory (safe: NativeArray shares pointer).
                        pool.Targets[poolIdx] = (long)candidate.PackedValue;
                        targetCount++;
                    }

                    localPoolNext += targetCount;

                    // Publish result event — the materialization system writes it into the ring buffer.
                    cmd.PublishEvent(new AreaQueryResultEvent
                    {
                        RequestId           = req.RequestId,
                        TargetCount         = targetCount,
                        TargetGroupHandle   = targetCount > 0 ? groupHandle : -1,
                        SourceNodeId        = req.SourceNodeId,
                        NewPoolNextFreeIndex = localPoolNext,
                    });
                }
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        private static void PublishEmptyResult(
            IEntityCommandBuffer cmd, in AreaQueryRequestEvent req, int poolNext)
        {
            cmd.PublishEvent(new AreaQueryResultEvent
            {
                RequestId            = req.RequestId,
                TargetCount          = 0,
                TargetGroupHandle    = -1,
                SourceNodeId         = req.SourceNodeId,
                NewPoolNextFreeIndex = poolNext,
            });
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
