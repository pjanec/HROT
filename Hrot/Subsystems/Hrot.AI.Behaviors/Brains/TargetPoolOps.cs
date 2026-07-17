using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Spatial.Eqs;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// Curated EQS target-pool kernels for the Hill-attack wave core (architect Q#8-E) — the round-robin
    /// target resolve in <c>DispatchWaveWithTargets</c> (<c>GetTargetFromPool</c> + alive +
    /// <c>NetworkIdentity</c> read) and the target-count probe. Self-contained kernels over
    /// <see cref="AreaQueryBatchHelper"/>; the graph reads only the resulting scalars. P7 trailing
    /// <c>ISimulationView view</c> is baked <c>TrailingContext:"View"</c> and downcast to the concrete
    /// <see cref="EntityRepository"/> (GAP-10, like <see cref="WorldOps.IsAlive"/>). No oracle change.
    /// </summary>
    public static class TargetPoolOps
    {
        /// <summary>
        /// Resolves the wave's target count — the oracle's cached-EQS-result count, falling back to a
        /// pool probe, floored at 1 (avoid divide-by-zero in the round-robin). Mirrors
        /// <c>DispatchWaveWithTargets</c> lines ~294-313.
        /// </summary>
        public static int ResolveTargetCount(long cachedEqsRequestId, int targetGroupHandle, ISimulationView view)
        {
            if (view is not EntityRepository world) return 1;

            int targetCount = 0;
            if (cachedEqsRequestId != -1)
            {
                var eqsResult = AreaQueryBatchHelper.GetAreaQueryResult(world, cachedEqsRequestId);
                if (eqsResult.IsReady) targetCount = eqsResult.TargetCount;
            }
            if (targetCount == 0 && targetGroupHandle >= 0)
            {
                while (true)
                {
                    long t = AreaQueryBatchHelper.GetTargetFromPool(world, targetGroupHandle, targetCount);
                    if (t == 0L) break;
                    targetCount++;
                    if (targetCount > 1024) break;   // safety cap (oracle)
                }
            }
            return targetCount == 0 ? 1 : targetCount;
        }

        /// <summary>
        /// Round-robin target NetworkId for the <paramref name="roundRobinIndex"/>-th dispatched tank —
        /// the oracle's <c>targetIdx = index % targetCount; GetTargetFromPool → alive → NetworkIdentity.Value</c>.
        /// The target count is resolved internally (<see cref="ResolveTargetCount"/>) so the visual graph
        /// passes only the cached EQS handles + the round-robin index. Returns <c>0</c> when the pool slot
        /// is empty/dead or the target has no <see cref="NetworkIdentity"/> (the oracle's
        /// <c>targetNetId = 0</c> default).
        /// </summary>
        public static long ResolveNetId(
            long cachedEqsRequestId, int targetGroupHandle, int roundRobinIndex, ISimulationView view)
        {
            if (view is not EntityRepository world) return 0L;

            int targetCount = ResolveTargetCount(cachedEqsRequestId, targetGroupHandle, view);
            int targetIdx = roundRobinIndex % targetCount;
            long targetPacked = AreaQueryBatchHelper.GetTargetFromPool(world, targetGroupHandle, targetIdx);
            if (targetPacked == 0L) return 0L;

            var targetEntity = new Entity((ulong)targetPacked);
            if (world.IsAlive(targetEntity) && world.HasComponent<NetworkIdentity>(targetEntity))
                return world.GetComponentRO<NetworkIdentity>(targetEntity).Value;

            return 0L;
        }
    }
}
