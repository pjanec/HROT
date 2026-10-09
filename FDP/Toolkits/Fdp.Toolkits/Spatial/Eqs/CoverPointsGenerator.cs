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

        [ThreadStatic] private static System.Collections.Generic.List<CoverPoint>? _vehicleScratch;

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
                    // ⭐ CE-3135 (peek-and-fire D5) — the point's stance travels with the answer, so a window point says
                    //   "crouch here" to the node and to the LOS test (CoverPoint.StanceHeight: 0 prone, 1 crouch, 2 stand).
                    Stance    = EqsResult.EncodeStance(StanceOf(rawPoints[i].StanceHeight)),
                    // ⛔ SUPERSEDED (§19): Flags = StanceHeight — it wrote stance into the §4.2 flag bits 0–1 (HasLOSToContext).
                };
            }

            // ⭐ CE-3142 (P-7a O5) — a STANDING vehicle's sides are cover too, read live from this view (never baked: a car that drives
            //   away is gone from the next query). Same rule as a terrain piece (TerrainCoverProvider.PointsAround); the tests after this
            //   generator see the vehicle as opaque (EqsTerrainSight.Sight), so only the side away from the threat survives.
            if (Kind == CoverKind.Cover && rawCount < candidates.Length)
            {
                var around = _vehicleScratch ??= new System.Collections.Generic.List<CoverPoint>();   // R-220 — per thread, reused
                around.Clear();
                if (VehicleCover.StandingPoints(view, EqsTerrainSight.World(view), around))
                    foreach (var p in around)
                    {
                        if (rawCount >= candidates.Length) break;
                        if (Vector2.Distance(new Vector2(p.PositionX, p.PositionY), center) > sensor.SearchRadius) continue;
                        candidates[rawCount++] = new EqsResult
                        {
                            EntityId  = 0L,
                            PositionX = p.PositionX,
                            PositionY = p.PositionY,
                            PositionZ = p.PositionZ,
                            Score     = p.Quality,
                            Stance    = EqsResult.EncodeStance(StanceOf(p.StanceHeight)),
                        };
                    }
            }

            return rawCount;
        }

        /// <summary><see cref="CoverPoint.StanceHeight"/> (0 prone, 1 crouch, 2 stand) as the engine's stance.</summary>
        internal static Fdp.Toolkit.Tkb.Domain.StanceId StanceOf(byte stanceHeight) => stanceHeight switch
        {
            0 => Fdp.Toolkit.Tkb.Domain.StanceId.Prone,
            1 => Fdp.Toolkit.Tkb.Domain.StanceId.Crouched,
            _ => Fdp.Toolkit.Tkb.Domain.StanceId.Standing,
        };
    }
}
