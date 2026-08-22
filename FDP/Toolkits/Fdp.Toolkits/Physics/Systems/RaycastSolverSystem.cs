using System;
using System.Numerics;
using System.Threading.Tasks;
using CarKinem.Spatial;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Physics.Components;
using Fdp.Toolkit.Physics.Math;

namespace Fdp.Toolkit.Physics.Systems
{
    /// <summary>
    /// Background-capable system that resolves all pending <see cref="RaycastRequestEvent"/>s.
    ///
    /// <para><b>Default mode (spatial-hash):</b> broad-phase spatial-hash query followed by
    /// per-entity <see cref="Intersection2D.RaycastCircle"/> narrow-phase checks.
    /// Reads <see cref="RaycastRequestEvent"/>s, resolves each cast, publishes one
    /// <see cref="RaycastResultEvent"/> per request via
    /// <see cref="IEntityCommandBuffer.PublishEvent{T}"/>.
    /// <see cref="RaycastResultMaterializationSystem"/> (main thread) writes the results into
    /// the <see cref="RaycastBatchData"/> ring buffer so BTree consumers can poll them.</para>
    ///
    /// <para><b>Backend override (STR-P3-T3):</b> When <see cref="RaycastBackend"/> is set
    /// (e.g. to the Stride/Bullet adapter <c>StrideRaycastBackend</c> in
    /// <c>Hrot.Stride.Core</c>), every ray is resolved via that backend instead.
    /// The spatial-hash path is bypassed entirely.  This allows the Stride node to use real
    /// 3-D scene-geometry raycasts (walls, ramps, obstacles) without changing the event
    /// plumbing.  <c>Fdp.Toolkits</c> never references <c>Hrot.Stride.Core</c>; the
    /// dependency goes the other way via the <see cref="IRaycastBackend"/> seam.</para>
    ///
    /// <para><b>Parallelism:</b> The narrow-phase loop uses <see cref="Parallel.For"/> in
    /// spatial-hash mode.  Backend mode runs serially (backends are single-threaded by the
    /// host-thread invariant, design §8.3).</para>
    ///
    /// <para><b>Thread safety of <c>World.GetComponent&lt;T&gt;</c> inside Parallel.For:</b>
    /// <see cref="EntityRepository"/> states that component access is not formally thread-safe.
    /// In practice the reads here are safe because:
    /// (a) No other system writes to <c>SimTransform</c> or <c>PhysicsCollider</c> concurrently.
    /// (b) <c>Dictionary&lt;Type, IComponentTable&gt;</c> (.NET) allows concurrent reads when no
    ///     write is in progress.
    /// See BATCH-08 report Q2 for the full analysis.</para>
    /// </summary>
    [UpdateInPhase(SystemPhase.Input)]
    public class RaycastSolverSystem : IEcsModuleSystem
    {
        /// <summary>
        /// Optional 3-D raycast backend (STR-P3-T3).
        ///
        /// <para>
        /// When non-null, every <see cref="RaycastRequestEvent"/> is resolved via this
        /// backend instead of the default flat spatial-hash + circle-sweep path.
        /// Set by the Stride node after the <c>PhysicsProcessor</c> is running.
        /// Leave null on non-Stride nodes (SimHost, headless tests) — they continue to
        /// use the spatial-hash approximation.
        /// </para>
        /// </summary>
        public IRaycastBackend? RaycastBackend { get; set; }

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo)
                throw new InvalidOperationException(
                    $"{nameof(RaycastSolverSystem)} requires direct EntityRepository access " +
                    $"and cannot run on a read-only snapshot ({view.GetType().Name}).");

            var requests = view.ReadEvents<RaycastRequestEvent>();
            if (requests.IsEmpty) return;

            // ── Backend override path (STR-P3-T3) ─────────────────────────────────
            // When a 3-D raycast backend is registered, delegate every request to it
            // and bypass the spatial-hash path entirely.
            if (RaycastBackend != null)
            {
                var backendCmd = view.GetCommandBuffer();
                for (int i = 0; i < requests.Length; i++)
                {
                    ref readonly var req = ref requests[i];
                    var hit = RaycastBackend.Raycast(
                        start:          req.Start,
                        end:            req.End,
                        rayId:          req.RayId,
                        layerMask:      req.LayerMask,
                        ignoreEntity:   req.IgnoreEntity,
                        observerEntity: req.Observer,
                        targetEntity:   req.Target);
                    // Echo the request fields that HitResolutionSystem needs.
                    hit.Start        = req.Start;
                    hit.End          = req.End;
                    hit.IgnoreEntity = req.IgnoreEntity;
                    hit.Observer     = req.Observer;
                    hit.Target       = req.Target;
                    hit.SourceNodeId = req.SourceNodeId;
                    backendCmd.PublishEvent(new RaycastResultEvent { Hit = hit });
                }
                return;
            }

            if (!repo.HasSingleton<SpatialGridData>()) return;
            // Value-copy of the SpatialGridData struct — carries native pointers to grid arrays.
            var gridData = repo.GetSingleton<SpatialGridData>();
            var grid     = gridData.Grid;

            int count = requests.Length;

            // Snapshot events to a plain array so the lambda can safely capture it
            // (ReadEvents returns a ref-backed buffer that cannot be captured in closures).
            var requestsSnapshot = new RaycastRequestEvent[count];
            for (int k = 0; k < count; k++)
                requestsSnapshot[k] = requests[k];

            // Resolve all casts in parallel into a local hit-result array, then publish serially.
            var results = new RaycastHit[count];

            Parallel.For(0, count, i =>
            {
                var req = requestsSnapshot[i];

                var start2D = new Vector2(req.Start.X, req.Start.Y);
                var end2D   = new Vector2(req.End.X,   req.End.Y);

                // Broad-phase: AABB query centred on the ray's midpoint.
                Vector2 midpoint    = (start2D + end2D) * 0.5f;
                float   queryRadius = Vector2.Distance(start2D, end2D) * 0.5f
                                      + PhysicsConstants.QueryExpansionRadius;

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

                    if (!repo.IsAlive(candidate)) continue;

                    if (!req.IgnoreEntity.IsNull && candidate == req.IgnoreEntity) continue;

                    if (!repo.HasComponent<PhysicsCollider>(candidate)) continue;

                    var collider = repo.GetComponent<PhysicsCollider>(candidate);

                    if ((req.LayerMask & collider.CollisionLayer) == 0) continue;

                    var tf  = repo.GetComponent<SimTransform>(candidate);
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

                results[i] = new RaycastHit
                {
                    T            = bestT,
                    HitEntity    = bestEnt,
                    RayId        = req.RayId,
                    Observer     = req.Observer,
                    Target       = req.Target,
                    HasHit       = (byte)(anyHit ? 1 : 0),
                    SourceNodeId = req.SourceNodeId,
                    Start        = req.Start,
                    End          = req.End,
                    IgnoreEntity = req.IgnoreEntity,
                };
                } // end unsafe
            });

            // Publish results serially via the command buffer (keeps cmd-buffer access on one thread).
            var cmd = view.GetCommandBuffer();
            for (int i = 0; i < count; i++)
                cmd.PublishEvent(new RaycastResultEvent { Hit = results[i] });
        }
    }
}
