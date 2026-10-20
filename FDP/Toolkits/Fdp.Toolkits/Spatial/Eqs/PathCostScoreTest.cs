using System;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Navigation;

namespace Fdp.Toolkit.Spatial.Eqs
{
    /// <summary>
    /// Scores candidates by inverse-linear path cost (closer path = higher score).
    /// Rejects candidates where no navmesh path exists (EntityId = -1L).
    /// Runs in ScoreExpensive phase.
    /// </summary>
    public sealed class PathCostScoreTest : IEqsTest, IEqsCostWeight, IEqsCountedWork
    {
        /// <inheritdoc/>
        /// <inheritdoc/>
        public int CostPerCandidate => EqsCost.Path;   // CE-3037 — path weight (DESIGN_Sensors_And_Doctrine §5.3)

        public EqsTestPhase Phase => EqsTestPhase.ScoreExpensive;

        /// <summary>⭐ CE-3279 (b) — how much longer than the straight line a path may be and still count (the search's reach factor).</summary>
        public const float DetourFactor = 1.5f;

        /// <summary>⭐ CE-3279 (b) — and a fixed allowance on top (m), so a near candidate behind one wall is still reached.</summary>
        public const float DetourSlack = 25f;

        /// <inheritdoc/>
        // ⭐ CE-3279 (c) — this thread's count for the last batch (the template's tests are shared by every sensor and thread)
        [ThreadStatic] private static int t_work;

        /// <inheritdoc/>
        public int TakeCountedWork() { int w = t_work; t_work = 0; return w; }

        public void ExecuteBatch(Entity observer, ref EqsSensor sensor, ISimulationView view, Span<EqsResult> candidates)
        {
            t_work = 0;
            if (view is not EntityRepository repo) return;
            if (!repo.HasSingletonManaged<INavmeshProvider>()) return;
            if (!EqsContext.SelfPosition(repo, observer, sensor, out var obsPos)) return;   // ⭐ §19 H3
            uint layer = EqsContext.SelfLayer(repo, observer, sensor);                      // ⭐ §19 H5

            var navmesh = repo.GetSingletonManaged<INavmeshProvider>()!;
            var doors = Fdp.Toolkit.Terrain.DoorStates.Of(repo);   // ⭐ R-219 — this EQS view's doors (its snapshot)
            // INavmeshProvider is Z-up like Sim (R-182 / W7): no swizzle (P3D-205).

            float maxDist = sensor.SearchRadius;
            if (maxDist <= 0f) return;

            // ⭐ CE-3279 (b) — every live candidate in ONE navmesh search from the self (INavmeshProvider.PathCosts), reaching as
            //   far as the farthest candidate × DetourFactor + DetourSlack: a point reached only by a longer detour reads as no path.
            //   📐 an A* per candidate cost 3.3 ms on demo-city (0.85 s of a solve). 📄 docs/DESIGN_Sensors_And_Doctrine.md §5.3.
            int n = candidates.Length;
            var points = System.Buffers.ArrayPool<Vector3>.Shared.Rent(Math.Max(1, n));
            var costs  = System.Buffers.ArrayPool<float>.Shared.Rent(Math.Max(1, n));
            var which  = System.Buffers.ArrayPool<int>.Shared.Rent(Math.Max(1, n));
            try
            {
                int m = 0;
                float farthest = 0f;
                for (int i = 0; i < n; i++)
                {
                    ref readonly var candidate = ref candidates[i];
                    if (candidate.EntityId == -1L) continue;   // already rejected
                    var p = new Vector3(candidate.PositionX, candidate.PositionY, candidate.PositionZ);
                    farthest = Math.Max(farthest, Vector3.Distance(obsPos, p));
                    points[m] = p;
                    which[m++] = i;
                }
                if (m == 0) return;

                int searched = navmesh.PathCosts(obsPos, points.AsSpan(0, m), costs.AsSpan(0, m), farthest * DetourFactor + DetourSlack, layer, doors);
                // the polygons one search visited (+ a lookup per point), or a path per point from a provider that cannot batch
                t_work = searched >= 0 ? searched * EqsCost.PathPolygon + m * EqsCost.Cheap : m * EqsCost.Path;

                for (int j = 0; j < m; j++)
                {
                    ref var candidate = ref candidates[which[j]];
                    float pathDist = costs[j];
                    if (pathDist != float.MaxValue)
                        candidate.Score += 1.0f - Math.Clamp(pathDist / maxDist, 0f, 1f);   // inverse-linear: shorter path = higher score
                    else
                        candidate.EntityId = -1L;   // reject: no path
                }
            }
            finally
            {
                System.Buffers.ArrayPool<Vector3>.Shared.Return(points);
                System.Buffers.ArrayPool<float>.Shared.Return(costs);
                System.Buffers.ArrayPool<int>.Shared.Return(which);
            }
        }
    }
}
