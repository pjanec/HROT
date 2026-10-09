using System;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Spatial.Eqs
{
    /// <summary>
    /// Generates positional (EntityId=0) EQS candidates from the ICoverProvider singleton.
    /// Uses stackalloc for the intermediate CoverPoint buffer -- zero heap allocation.
    /// </summary>
    public sealed class CoverPointsGenerator : IEqsGenerator
    {
        /// <summary>⭐ Stage 7a — which points to generate: cover (the default, so <see cref="FindCoverFromTarget"/> keeps its
        /// meaning) or window firing positions (<see cref="FindWindowFiringPosition"/>). 📄 docs/DESIGN_Building_Interiors.md §3l C5.</summary>
        public CoverKind Kind { get; set; } = CoverKind.Cover;

        /// <inheritdoc/>
        public int Generate(Entity observer, ref EqsSensor sensor, ISimulationView view, Span<EqsResult> candidates)
        {
            if (view is not EntityRepository repo) return 0;
            if (!repo.HasSingletonManaged<ICoverProvider>()) return 0;

            ICoverProvider provider = repo.GetSingletonManaged<ICoverProvider>()!;

            // ⭐ §19 H3 — the self, not the observer: a child sensor's carrier has no position.
            if (!EqsContext.SelfPosition(repo, observer, sensor, out var selfPos)) return 0;
            var center = new Vector2(selfPos.X, selfPos.Y);

            // Intermediate stackalloc buffer for raw cover points.
            Span<CoverPoint> rawPoints = stackalloc CoverPoint[candidates.Length];
            int rawCount = provider.GetCoverPointsInRadius(center, sensor.SearchRadius, rawPoints, Kind);

            for (int i = 0; i < rawCount; i++)
            {
                // EntityId = 0 marks a positional candidate (no entity attached).
                candidates[i] = new EqsResult
                {
                    EntityId  = 0L,
                    PositionX = rawPoints[i].PositionX,
                    PositionY = rawPoints[i].PositionY,
                    PositionZ = rawPoints[i].PositionZ, // P3D-203: stream cover altitude.
                    Score     = rawPoints[i].Quality, // Seed score with cover quality.
                    // ⛔ SUPERSEDED (§19): Flags = StanceHeight — it wrote stance into the §4.2 flag bits 0–1 (HasLOSToContext).
                };
            }

            return rawCount;
        }
    }
}
