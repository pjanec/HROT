using System;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Terrain;

namespace Fdp.Toolkit.Spatial.Eqs
{
    /// <summary>
    /// ⭐ <c>CE-7016</c> (City Battle §11 S-3; handoff default: "no building / obstacle within 25 m, slope &lt; 10°") — a FILTER for ground a
    /// helicopter can set down on: a candidate is rejected when any solid of the resident terrain (a building, a wall, an obstacle — its
    /// prism's box) comes within <see cref="ClearMetres"/>, or when the ground falls more than <see cref="MaxSlopeDegrees"/> across
    /// ± <see cref="SlopeSampleMetres"/> (the larger of the two axes). No terrain ⇒ nothing is filtered. No allocation.
    /// </summary>
    public sealed class OpenGroundTest : IEqsTest
    {
        public float ClearMetres { get; set; } = 25f;
        public float MaxSlopeDegrees { get; set; } = 10f;
        public float SlopeSampleMetres { get; set; } = 5f;

        /// <inheritdoc/>
        public EqsTestPhase Phase => EqsTestPhase.FilterExpensive;

        /// <inheritdoc/>
        public void ExecuteBatch(Entity observer, ref EqsSensor sensor, ISimulationView view, Span<EqsResult> candidates)
        {
            var terrain = view is EntityRepository repo && repo.HasSingletonManaged<TerrainWorld>() ? repo.GetSingletonManaged<TerrainWorld>() : null;
            var ground = Fdp.Toolkit.World.WorldQuery.Of(view);
            float maxRise = MathF.Tan(MaxSlopeDegrees * MathF.PI / 180f) * 2f * SlopeSampleMetres;
            for (int i = 0; i < candidates.Length; i++)
            {
                ref var c = ref candidates[i];
                if (c.EntityId == -1L) continue;
                var p = new Vector2(c.PositionX, c.PositionY);
                if (terrain != null && Cluttered(terrain, p, ClearMetres)) { c.EntityId = -1; continue; }
                if (ground != null && Steep(ground, p, SlopeSampleMetres, maxRise)) c.EntityId = -1;
            }
        }

        /// <summary>True when a solid's box comes within <paramref name="clear"/> of <paramref name="p"/>.</summary>
        public static bool Cluttered(TerrainWorld world, Vector2 p, float clear)
        {
            float c2 = clear * clear;
            foreach (var prism in world.Prisms)
            {
                float dx = MathF.Max(0f, MathF.Max(prism.Min.X - p.X, p.X - prism.Max.X));
                float dy = MathF.Max(0f, MathF.Max(prism.Min.Y - p.Y, p.Y - prism.Max.Y));
                if (dx * dx + dy * dy <= c2) return true;
            }
            return false;
        }

        private static bool Steep(Fdp.Toolkit.World.IWorldQuery ground, Vector2 p, float d, float maxRise)
            => MathF.Abs(ground.GroundHeightAt(p.X + d, p.Y) - ground.GroundHeightAt(p.X - d, p.Y)) > maxRise
               || MathF.Abs(ground.GroundHeightAt(p.X, p.Y + d) - ground.GroundHeightAt(p.X, p.Y - d)) > maxRise;
    }

    /// <summary>
    /// ⭐⭐ <c>CE-7016</c> (City Battle §11 S-3; handoff default) — FIND A LANDING ZONE for an air assault: rings round the OBJECTIVE (context point,
    /// slot 1) from 300 to 800 m (the sensor's search radius 800, inner fraction 0.375), kept only on OPEN ground (<see cref="OpenGroundTest"/>),
    /// scored by staying beyond the reach of the threats the aircraft knows (<see cref="BeyondThreatRangeTest"/>) and by being near it.
    /// </summary>
    [EqsTemplate(AssetId)]
    public static class FindLandingZone
    {
        public const string AssetId = "bc8d95cf-751d-4754-9a01-8c5240e8fac4";
        public const uint BlueprintId = 0x33123142u;
        /// <summary>The ring band (m) round the objective — the handoff's 300–800 m.</summary>
        public const float InnerMetres = 300f, OuterMetres = 800f;

        public static EqsQueryTemplate Build(IEqsTemplateBuilder b) => new EqsQueryTemplate
        {
            BlueprintId     = BlueprintId,
            Generator       = new DonutGenerator { AnchorSlot = 1, Rings = 4, PointsPerRing = 16, InnerFraction = InnerMetres / OuterMetres },
            FilterExpensive = new IEqsTest[] { new OpenGroundTest() },
            ScoreCheap      = new IEqsTest[] { new DistanceScoreTest() },
            ScoreExpensive  = new IEqsTest[] { new BeyondThreatRangeTest { Weight = 2f } },
            MaxCandidates   = 64,
        };
    }
}
