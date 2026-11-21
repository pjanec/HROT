using System;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Spatial.Eqs;
using Fdp.Toolkit.Terrain;
using Xunit;

namespace Fdp.Toolkit.Spatial.Tests.Eqs
{
    /// <summary>
    /// ⭐ <c>CE-7016</c> (City Battle §11 S-3) — the landing zone: <see cref="OpenGroundTest"/> keeps only ground nothing stands within 25 m of;
    /// <see cref="FindLandingZone"/> rings the objective 300–800 m and keeps the open points.
    /// </summary>
    public sealed class LandingZoneTests
    {
        private static TerrainWorld OneBlock()
        {
            var prism = TerrainObstacles.PrismOf(Vector3.Zero, 0f, new ObstacleShape { Length = 10f, Width = 10f, Height = 6f },
                                                 TerrainMaterialLibrary.Shared.Get("concrete", "test"), "house");
            return new TerrainWorld { BoundsMin = new Vector2(-500, -500), BoundsMax = new Vector2(500, 500), Prisms = new[] { prism } };
        }

        /// <summary>⭐ A candidate 10 m from a building's wall is rejected, 40 m away kept; a candidate already rejected stays so.
        /// ✅ Red-proof: the clearance measured to the box CENTRE (not its nearest side) ⇒ the 28 m one (23 m from the wall) is kept ⇒ red.</summary>
        [Fact]
        public void CE7016_OpenGround_RejectsAPointNearABuilding_KeepsOneInTheOpen()
        {
            using var repo = new EntityRepository();
            repo.SetSingletonManaged(OneBlock());
            var sensor = new EqsSensor();
            Span<EqsResult> c = stackalloc EqsResult[4];
            c[0] = new EqsResult { PositionX = 15f };                  // 10 m from the wall (x = 5)
            c[1] = new EqsResult { PositionX = 28f };                  // 23 m from the wall
            c[2] = new EqsResult { PositionX = 45f };                  // 40 m
            c[3] = new EqsResult { PositionX = 60f, EntityId = -1 };   // already rejected
            new OpenGroundTest().ExecuteBatch(Entity.Null, ref sensor, repo, c);
            Assert.Equal(-1L, c[0].EntityId);
            Assert.Equal(-1L, c[1].EntityId);
            Assert.Equal(0L, c[2].EntityId);
            Assert.Equal(-1L, c[3].EntityId);
        }

        [Fact]
        public void CE7016_FindLandingZone_TheTemplateIdIsTheCanonicalOne()
            => Assert.Equal(EqsTemplateRegistry.BlueprintIdOf(Guid.Parse(FindLandingZone.AssetId)), FindLandingZone.BlueprintId);

        /// <summary>⭐ demo-city: the ring round its centre (300–800 m) — every point the template keeps is 300–800 m off and ≥ 25 m clear of every
        /// solid; some are kept (the town has open ground round it) and some rejected (the houses).</summary>
        [Fact]
        public void CE7016_DemoCity_TheRingKeepsOnlyOpenPoints_InTheBand()
        {
            var folder = System.IO.Path.Combine(Fdp.Toolkit.Orchestration.ContentLayout.TerrainAssets(Fdp.Toolkit.Orchestration.ContentLayout.TryFindSourceTree()!), "demo-city");
            var w = TerrainWorldParser.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(folder, "demo-city.world.geojson")), "demo-city",
                TerrainAssets.ForFolder(folder));
            using var repo = new EntityRepository();
            repo.RegisterComponent<SimTransform>();
            repo.SetSingletonManaged(w);
            var objective = new Vector3(400f, 400f, 0f);
            var sensor = new EqsSensor { SearchRadius = FindLandingZone.OuterMetres, ContextPoint1 = objective, ContextPointMask = EqsSensor.Point1Bit };
            var template = FindLandingZone.Build(null!);
            Span<EqsResult> c = stackalloc EqsResult[template.MaxCandidates];
            int n = template.Generator.Generate(Entity.Null, ref sensor, repo, c);
            Assert.True(n > 20, $"{n} candidates");
            foreach (var t in template.FilterExpensive!) t.ExecuteBatch(Entity.Null, ref sensor, repo, c.Slice(0, n));
            int kept = 0, rejected = 0;
            for (int i = 0; i < n; i++)
            {
                var p = new Vector2(c[i].PositionX, c[i].PositionY);
                float d = Vector2.Distance(p, new Vector2(objective.X, objective.Y));
                Assert.InRange(d, FindLandingZone.InnerMetres - 1f, FindLandingZone.OuterMetres + 1f);
                if (c[i].EntityId == -1L) { rejected++; continue; }
                kept++;
                Assert.False(OpenGroundTest.Cluttered(w, p, 25f), $"{p} kept but cluttered");
            }
            Assert.True(kept > 0, "some open ground round the town");
            Assert.True(rejected > 0, "some points by the houses rejected");
        }
    }
}
