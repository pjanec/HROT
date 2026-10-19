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

        /// <summary>⭐ <c>CE-2165</c> V3 — for a VEHICLE querier, size every point by its hull (<see cref="Eqs.VehicleFootprint"/>): pushed out to
        /// half its width + a margin, kept only where its box is clear, none round its own box. A person querier is unchanged. Default off.</summary>
        public bool VehicleFootprint { get; set; }

        /// <summary>⭐ <c>CE-2275</c> D-2b (DA-6, DA-41) — with <see cref="CoverKind.WindowFiring"/>, each window point is set back into the room
        /// this far (else <see cref="MinSetBackMetres"/>, else left at the sill — <see cref="TerrainCoverProvider.SetBack"/>). 0 = at the sill.</summary>
        public float SetBackMetres { get; set; }

        /// <summary>The shorter set-back tried when <see cref="SetBackMetres"/> does not fit (DA-6's 1.5 m).</summary>
        public const float MinSetBackMetres = 1.5f;

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
            int found = provider.GetCoverPointsInRadius(center, sensor.SearchRadius, rawPoints, Kind);

            // ⭐ CE-2165 V3 — a vehicle's hide point is sized by its hull (DESIGN_Vehicle_Exposure_Cycle §3).
            var self = VehicleFootprint ? EqsContext.Self(repo, observer, sensor) : Entity.Null;
            float hullLength = 0f, hullWidth = 0f, hullHeight = 0f;
            bool hull = VehicleFootprint && Eqs.VehicleFootprint.Hull(repo, self, out hullLength, out hullWidth, out hullHeight);
            var world = hull ? EqsTerrainSight.World(repo) : null;
            var terrain = Kind == CoverKind.WindowFiring && SetBackMetres > 0f ? EqsTerrainSight.World(repo) : null;   // ⭐ D-2b

            int rawCount = 0;
            for (int i = 0; i < found; i++)
            {
                var at = new Vector3(rawPoints[i].PositionX, rawPoints[i].PositionY, rawPoints[i].PositionZ);
                if (hull && !Sized(world, ref at, new Vector2(rawPoints[i].DirectionX, rawPoints[i].DirectionY), hullLength, hullWidth, hullHeight)) continue;
                if (terrain != null) at = TerrainCoverProvider.SetBack(terrain, in rawPoints[i], SetBackMetres, MinSetBackMetres);   // ⭐ D-2b
                // EntityId = 0 marks a positional candidate (no entity attached).
                candidates[rawCount++] = new EqsResult
                {
                    EntityId  = 0L,
                    PositionX = at.X,
                    PositionY = at.Y,
                    PositionZ = at.Z, // P3D-203: stream cover altitude.
                    Score     = rawPoints[i].Quality, // Seed score with cover quality.
                    // ⭐ CE-3135 (peek-and-fire D5) — the point's stance travels with the answer, so a window point says
                    //   "crouch here" to the node and to the LOS test (CoverPoint.StanceHeight: 0 prone, 1 crouch, 2 stand).
                    Stance    = EqsResult.EncodeStance(StanceOf(rawPoints[i].StanceHeight)),
                    // ⭐ CE-3158 G1 — and what the point IS (a cover, a window to fire from): the peek is chosen from the point
                    Kind      = EqsResult.EncodeKind(rawPoints[i].Kind),
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
                        var at = new Vector3(p.PositionX, p.PositionY, p.PositionZ);
                        if (hull)
                        {
                            // its own box is not its cover: the points round it stand within its half diagonal + a stand-off
                            if (Vector2.Distance(new Vector2(at.X, at.Y), center) < OwnBoxReach(hullLength, hullWidth)) continue;
                            if (!Sized(world, ref at, new Vector2(p.DirectionX, p.DirectionY), hullLength, hullWidth, hullHeight)) continue;
                        }
                        candidates[rawCount++] = new EqsResult
                        {
                            EntityId  = 0L,
                            PositionX = at.X,
                            PositionY = at.Y,
                            PositionZ = at.Z,
                            Score     = p.Quality,
                            Stance    = EqsResult.EncodeStance(StanceOf(p.StanceHeight)),
                            Kind      = EqsResult.EncodeKind(CoverKind.Cover),   // ⭐ CE-3158 G1 — a vehicle's side is cover
                        };
                    }
            }

            return rawCount;
        }

        /// <summary>Pushes a man's cover point out to the hull's stand-off along its protection direction and checks the box is clear.</summary>
        private static bool Sized(Fdp.Toolkit.Terrain.TerrainWorld? world, ref Vector3 at, Vector2 towardCover, float hullLength, float hullWidth, float hullHeight)
        {
            if (towardCover.LengthSquared() > 1e-6f)
            {
                float push = Eqs.VehicleFootprint.StandOff(hullWidth) - TerrainCoverProvider.StandOff;
                if (push > 0f)
                {
                    var away = -Vector2.Normalize(towardCover) * push;
                    at = new Vector3(at.X + away.X, at.Y + away.Y, at.Z);
                }
            }
            return world == null || Eqs.VehicleFootprint.Clear(world, at, towardCover, hullLength, hullWidth, hullHeight);
        }

        private static float OwnBoxReach(float hullLength, float hullWidth)
            => 0.5f * MathF.Sqrt(hullLength * hullLength + hullWidth * hullWidth) + TerrainCoverProvider.StandOff + Eqs.VehicleFootprint.Margin;

        /// <summary><see cref="CoverPoint.StanceHeight"/> (0 prone, 1 crouch, 2 stand) as the engine's stance.</summary>
        internal static Fdp.Toolkit.Tkb.Domain.StanceId StanceOf(byte stanceHeight) => stanceHeight switch
        {
            0 => Fdp.Toolkit.Tkb.Domain.StanceId.Prone,
            1 => Fdp.Toolkit.Tkb.Domain.StanceId.Crouched,
            _ => Fdp.Toolkit.Tkb.Domain.StanceId.Standing,
        };
    }
}
