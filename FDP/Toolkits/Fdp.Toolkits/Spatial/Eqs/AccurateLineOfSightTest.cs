using System;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Physics;
using Fdp.Toolkit.Physics.Components;

namespace Fdp.Toolkit.Spatial.Eqs
{
    /// <summary>
    /// Phase 5 scoring test: accurate LOS via deferred raycast ring buffer.
    ///
    /// <para>For each candidate, checks whether a raycast result is already in
    /// <see cref="RaycastBatchData.Hits"/>. If found, resolves the candidate
    /// immediately (flag bit 0 = occluded = good cover; EntityId = -1L = exposed).
    /// If not found, submits a <see cref="RaycastRequestEvent"/> (subject to the
    /// per-tick budget in <see cref="EqsSolverGlobalState"/>) and marks the candidate
    /// with <see cref="FlagPendingRay"/> so the solver knows to yield.</para>
    /// </summary>
    public sealed class AccurateLineOfSightTest : IEqsTest
    {
        /// <summary>Flag bit 15: raycast submitted but result not yet in ring buffer.</summary>
        public const short FlagPendingRay = unchecked((short)(1 << 15));

        /// <inheritdoc/>
        public EqsTestPhase Phase => EqsTestPhase.ScoreExpensive;

        /// <inheritdoc/>
        public unsafe void ExecuteBatch(
            Entity observer,
            ref EqsSensor sensor,
            ISimulationView view,
            Span<EqsResult> candidates)
        {
            if (view is not EntityRepository repo) return;

            // Bypass: no TargetMemory or no threats tracked.
            if (!repo.HasComponent<TargetMemory>(observer)) return;
            ref readonly var mem = ref repo.GetComponentRO<TargetMemory>(observer);
            if (mem.Count == 0) return;

            // Bypass: threat score below threshold.
            if (mem.ThreatScores[0] < sensor.ThreatThreshold) return;

            // Guard: ring buffer not initialized — mark all non-rejected candidates as pending.
            if (!repo.HasSingleton<RaycastBatchData>())
            {
                for (int i = 0; i < candidates.Length; i++)
                {
                    if (candidates[i].EntityId != -1L)
                        candidates[i].Flags = unchecked((short)(candidates[i].Flags | (1 << 15)));
                }
                return;
            }

            // Guard: global budget state not initialized.
            if (!repo.HasSingleton<EqsSolverGlobalState>()) return;

            ref readonly var rayBatch   = ref repo.GetSingleton<RaycastBatchData>();
            ref var          globalState = ref repo.GetSingletonUnmanaged<EqsSolverGlobalState>();

            var targetPos3D = new Vector3(mem.PositionsX[0], mem.PositionsY[0], 1.5f);
            var cmd         = view.GetCommandBuffer();

            for (int i = 0; i < candidates.Length; i++)
            {
                if (candidates[i].EntityId == -1L) continue;

                long rayId = ((long)observer.Index << 32) | (uint)i;
                int  slot  = (int)((uint)rayId % (uint)PhysicsConstants.RaycastBatchCapacity);

                var hit = rayBatch.Hits[slot];

                if (hit.RayId == rayId)
                {
                    // Result already in ring buffer: resolve now.
                    // Clear FlagPendingRay (bit 15).
                    candidates[i].Flags = unchecked((short)(candidates[i].Flags & ~(1 << 15)));

                    if (hit.HasHit != 0)
                    {
                        // Geometry blocks LOS → candidate is occluded → good cover.
                        candidates[i].Flags |= 1;
                    }
                    else
                    {
                        // Clear LOS → candidate exposed to threat → reject.
                        candidates[i].EntityId = -1L;
                    }
                }
                else
                {
                    // Not yet resolved.
                    if (globalState.AccurateRaysSubmittedThisTick < globalState.MaxAccurateRaycastsPerSolverTick)
                    {
                        cmd.PublishEvent(new RaycastRequestEvent
                        {
                            Start        = new Vector3(candidates[i].PositionX, candidates[i].PositionY, 1.5f),
                            End          = targetPos3D,
                            RayId        = rayId,
                            Observer     = observer,
                            Target       = Entity.Null,
                            LayerMask    = -1,
                            IgnoreEntity = observer,
                            SourceNodeId = 0,
                        });
                        globalState.AccurateRaysSubmittedThisTick++;
                    }

                    // Mark pending regardless (even if budget exhausted).
                    candidates[i].Flags = unchecked((short)(candidates[i].Flags | (1 << 15)));
                }
            }
        }
    }
}
