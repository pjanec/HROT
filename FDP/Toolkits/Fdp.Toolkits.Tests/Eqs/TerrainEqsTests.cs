using System;
using System.IO;
using System.Linq;
using System.Numerics;
using CarKinem.Core;
using Fdp.Core;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Perception;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Terrain;
using Xunit;

namespace Fdp.Toolkit.Spatial.Eqs.Tests
{
    /// <summary>
    /// ⭐ The terrain EQS slice (docs/designs/eqs-2/EQS_Design_v1.3_final.md §19): sight over the terrain world, terrain cover,
    /// the child-sensor self, the new generators and tests, and the starter templates run against the real <c>test-town</c>.
    /// </summary>
    public sealed class TerrainEqsTests : IDisposable
    {
        private readonly EntityRepository _repo = new();

        public TerrainEqsTests()
        {
            _repo.RegisterComponent<SimTransform>();
            _repo.RegisterComponent<PartMetadata>();
            _repo.RegisterComponent<TargetMemory>();
            _repo.RegisterComponent<SensorContactList>();
            _repo.RegisterComponent<SensorMount>();
            _repo.RegisterComponent<EntityInfo>();
            _repo.RegisterComponent<NavAgentProfile>();
            _repo.RegisterComponent<VehicleState>();
        }

        public void Dispose() => _repo.Dispose();

        // ── fixtures ─────────────────────────────────────────────────────────────────────────

        /// <summary>One 3 m high, 0.5 m thick wall along x ∈ [0,20] at y = 10.</summary>
        private static TerrainWorld WallWorld(float height = 3f) => new()
        {
            BoundsMin = new Vector2(-50, -50), BoundsMax = new Vector2(50, 50),
            Prisms = new[] { Prism(new Vector2(0, 10), new Vector2(20, 10.5f), height) },
        };

        private static TerrainPrism Prism(Vector2 min, Vector2 max, float height) => new()
        {
            Kind = TerrainPrismKind.Wall,
            Footprint = new[] { min, new Vector2(max.X, min.Y), max, new Vector2(min.X, max.Y) },   // counter-clockwise
            BaseZ = 0, TopZ = height, Min = min, Max = max,
        };

        private Entity At(float x, float y, float z = 0)
        {
            var e = _repo.CreateEntity();
            _repo.AddComponent(e, new SimTransform { Position = new Vector3(x, y, z), Rotation = Quaternion.Identity });
            return e;
        }

        private static EqsResult Point(float x, float y) => new() { EntityId = 0L, PositionX = x, PositionY = y };

        // ── sight ────────────────────────────────────────────────────────────────────────────

        /// <summary>Behind the wall from the threat is kept (hidden); in the open is rejected; HasLOSToContext1 is judged.</summary>
        [Fact]
        public void Los_RequireHidden_KeepsThePointBehindTheWall()
        {
            _repo.SetSingletonManaged(WallWorld());
            var threat = At(10, 30);                 // north of the wall
            var self = At(10, 0);
            var sensor = new EqsSensor { ContextSlot0 = self, ContextSlot1 = threat };
            var c = new[] { Point(10, 8), Point(30, 8) };   // behind the wall · beside it, in the open

            new CheapLineOfSightTest().ExecuteBatch(Entity.Null, ref sensor, _repo, c);

            Assert.Equal(0L, c[0].EntityId);
            Assert.Equal(-1L, c[1].EntityId);
            Assert.Equal(2, c[0].FlagsMeaningful & 2);
            Assert.Equal(0, c[0].Flags & 2);
        }

        /// <summary>A firing position must SEE the target from a standing eye; a low wall does not stop a standing soldier.</summary>
        [Fact]
        public void Los_RequireVisible_FromTheCandidate_UsesTheStandingEye()
        {
            _repo.SetSingletonManaged(WallWorld(height: 1.2f));   // waist-high: hides a crouch, not a standing eye
            var target = At(10, 30);
            var sensor = new EqsSensor { ContextSlot0 = At(0, 0), ContextSlot1 = target };
            var c = new[] { Point(10, 9) };

            new CheapLineOfSightTest { Viewer = EqsLosViewer.Candidate, Require = EqsLosRequire.Visible }
                .ExecuteBatch(Entity.Null, ref sensor, _repo, c);

            Assert.Equal(0L, c[0].EntityId);
            Assert.Equal(2, c[0].Flags & 2);
        }

        /// <summary>No terrain resident ⇒ sight is unknown ⇒ nothing judged, nothing rejected (never the old "always blocked").</summary>
        [Fact]
        public void Los_WithNoTerrain_JudgesNothing()
        {
            var sensor = new EqsSensor { ContextSlot0 = At(0, 0), ContextSlot1 = At(10, 30) };
            var c = new[] { Point(30, 8) };

            new CheapLineOfSightTest().ExecuteBatch(Entity.Null, ref sensor, _repo, c);

            Assert.Equal(0L, c[0].EntityId);
            Assert.Equal(0, c[0].FlagsMeaningful);
        }

        // ── the child-sensor self (§19.1 H3/H4) ──────────────────────────────────────────────

        /// <summary>A child sensor's carrier has only PartMetadata: the generators and tests use its PARENT's position, and the
        /// LOS test is not gated by the carrier's (absent) TargetMemory.</summary>
        [Fact]
        public void ChildSensorCarrier_ResolvesItsParentAsSelf()
        {
            _repo.SetSingletonManaged(WallWorld());
            var parent = At(10, 0);
            var carrier = _repo.CreateEntity();
            _repo.AddComponent(carrier, new PartMetadata { ParentEntity = parent, InstanceId = 1 });
            _repo.SetSingletonManaged<ICoverProvider>(TerrainCoverProvider.Build(WallWorld()));
            var sensor = new EqsSensor { SearchRadius = 15f, ContextSlot1 = At(10, 30) };

            var c = new EqsResult[64];
            int n = new CoverPointsGenerator().Generate(carrier, ref sensor, _repo, c);
            Assert.True(n > 0, "a child sensor generates around its parent");

            var span = c.AsSpan(0, n);
            new CheapLineOfSightTest().ExecuteBatch(carrier, ref sensor, _repo, span);
            Assert.Contains(span.ToArray(), r => r.EntityId == 0L && r.PositionY < 10f);   // south face: hidden from the threat
            Assert.DoesNotContain(span.ToArray(), r => r.EntityId == 0L && r.PositionY > 10.5f);   // north face: in its view
        }

        // ── terrain cover ────────────────────────────────────────────────────────────────────

        [Fact]
        public void TerrainCover_LinesBothFacesOfAWall_OutsideIt_FacingIt()
        {
            var cover = TerrainCoverProvider.Build(WallWorld());
            var pts = cover.Points;

            Assert.Contains(pts, p => p.PositionY < 10f && p.DirectionY > 0.9f);     // south face, facing north (the wall)
            Assert.Contains(pts, p => p.PositionY > 10.5f && p.DirectionY < -0.9f);  // north face, facing south
            Assert.All(pts, p => Assert.False(p.PositionX > 0f && p.PositionX < 20f && p.PositionY > 10f && p.PositionY < 10.5f,
                "a cover point inside the wall"));
            Assert.Contains(pts, p => p.PositionX < 0f && p.DirectionX > 0.9f);      // the west end cap, facing east
            Assert.All(pts, p => Assert.Equal(2, p.StanceHeight));                  // 3 m: standing cover
        }

        [Theory]
        [InlineData(1.0f, 1)]
        [InlineData(0.5f, 0)]
        public void TerrainCover_StanceFollowsTheWallHeight(float height, byte stance)
            => Assert.All(TerrainCoverProvider.Build(WallWorld(height)).Points, p => Assert.Equal(stance, p.StanceHeight));

        [Fact]
        public void TerrainCover_AnAnkleHighWallIsNoCover()
            => Assert.Empty(TerrainCoverProvider.Build(WallWorld(0.3f)).Points);

        [Fact]
        public void TerrainCover_RadiusQuery_ReturnsTheNearestWhenMoreMatchThanFit()
        {
            var cover = TerrainCoverProvider.Build(WallWorld());
            var two = new CoverPoint[2];
            int n = cover.GetCoverPointsInRadius(new Vector2(0, 9), 100f, two);

            Assert.Equal(2, n);
            float farthestKept = Math.Max(Vector2.Distance(new(0, 9), new(two[0].PositionX, two[0].PositionY)),
                                          Vector2.Distance(new(0, 9), new(two[1].PositionX, two[1].PositionY)));
            Assert.All(cover.Points.Where(p => !(p.PositionX == two[0].PositionX && p.PositionY == two[0].PositionY)
                                            && !(p.PositionX == two[1].PositionX && p.PositionY == two[1].PositionY)),
                p => Assert.True(Vector2.Distance(new(0, 9), new(p.PositionX, p.PositionY)) >= farthestKept - 1e-4f));
        }

        // ── threat exposure ──────────────────────────────────────────────────────────────────

        [Fact]
        public unsafe void ThreatExposure_ScoresByTheShareOfKnownThreatsThatSeeThePoint()
        {
            _repo.SetSingletonManaged(WallWorld());
            var self = At(10, 0);
            var north = At(10, 30);       // the wall hides the south side from it
            var east = At(40, 5);         // sees along the south side
            var contacts = new SensorContactList();
            SensorContactList.UpdateSighting(ref contacts, (long)north.PackedValue, 1);
            SensorContactList.UpdateSighting(ref contacts, (long)east.PackedValue, 1);
            contacts.State[0] = (byte)SensorContactState.Acquired;
            contacts.State[1] = (byte)SensorContactState.Acquired;
            _repo.AddComponent(self, contacts);
            var sensor = new EqsSensor { ContextSlot0 = self };
            var c = new[] { Point(10, 8), Point(10, 40) };   // south of the wall · far north, in the open

            new ThreatExposureTest().ExecuteBatch(Entity.Null, ref sensor, _repo, c);

            Assert.Equal(0.5f, c[0].Score, 3);                  // the north threat cannot see it, the east one can
            Assert.Equal(0, c[0].Flags & (1 << 4));
            Assert.NotEqual(0, c[0].Flags & (1 << 5));
            Assert.Equal(0f, c[1].Score, 3);                    // both see it
        }

        [Fact]
        public void ThreatExposure_WithNoKnownThreats_ScoresNothing()
        {
            _repo.SetSingletonManaged(WallWorld());
            var self = At(10, 0);
            _repo.AddComponent(self, new SensorContactList());
            var sensor = new EqsSensor { ContextSlot0 = self };
            var c = new[] { Point(10, 8) };

            new ThreatExposureTest().ExecuteBatch(Entity.Null, ref sensor, _repo, c);

            Assert.Equal(0f, c[0].Score);
            Assert.Equal(0, c[0].FlagsMeaningful);
        }

        // ── scoring tests ────────────────────────────────────────────────────────────────────

        [Fact]
        public void DotProduct_SideOnToTheTargetSelfLineScoresMost()
        {
            var sensor = new EqsSensor { ContextSlot0 = At(0, -20), ContextSlot1 = At(0, 0) };   // self due south of the target
            var c = new[] { Point(20, 0), Point(0, -20), Point(0, 20) };                       // east (flank) · self side · behind

            new DotProductTest { PivotSlot = 1, ReferenceSlot = 0, PreferredDot = 0f }.ExecuteBatch(Entity.Null, ref sensor, _repo, c);

            Assert.True(c[0].Score > c[1].Score && c[0].Score > c[2].Score);
            Assert.NotEqual(0, c[0].Flags & (1 << 6));
            Assert.Equal(0, c[1].Flags & (1 << 6));
        }

        [Fact]
        public void Height_ScoresHigherGroundAboveTheTarget()
        {
            var sensor = new EqsSensor { ContextSlot1 = At(0, 0, 0) };
            var c = new[] { new EqsResult { PositionZ = 6f }, new EqsResult { PositionZ = 0f } };

            new HeightScoreTest().ExecuteBatch(Entity.Null, ref sensor, _repo, c);

            Assert.Equal(1f, c[0].Score, 3);
            Assert.Equal(0f, c[1].Score, 3);
        }

        [Fact]
        public void Distance_PreferFarFromTheTarget()
        {
            var sensor = new EqsSensor { SearchRadius = 100f, ContextSlot0 = At(0, 0), ContextSlot1 = At(50, 0) };
            var c = new[] { Point(-40, 0), Point(40, 0) };

            new DistanceScoreTest { FromSlot = 1, PreferFar = true }.ExecuteBatch(Entity.Null, ref sensor, _repo, c);

            Assert.True(c[0].Score > c[1].Score);
        }

        // ── generators ───────────────────────────────────────────────────────────────────────

        [Fact]
        public void PointPatterns_DropPointsInsideSolids_AndCoverTheirShape()
        {
            _repo.SetSingletonManaged(new TerrainWorld
            {
                BoundsMin = new Vector2(-100, -100), BoundsMax = new Vector2(100, 100),
                Prisms = new[] { Prism(new Vector2(5, -30), new Vector2(30, 30), 10f) },   // a block east of the self
            });
            var sensor = new EqsSensor { SearchRadius = 20f, ContextSlot0 = At(0, 0), ContextSlot1 = At(0, 50) };
            var buf = new EqsResult[128];

            foreach (PointPatternGenerator g in new PointPatternGenerator[]
                     {
                         new DonutGenerator(), new GridGenerator(), new ConeGenerator(),
                         new OffsetFromContextGenerator { Offsets = new[] { new Vector2(10, 0), new Vector2(0, -10) } },
                     })
            {
                int n = g.Generate(Entity.Null, ref sensor, _repo, buf);
                Assert.True(n > 0, g.GetType().Name);
                for (int i = 0; i < n; i++)
                    Assert.False(buf[i].PositionX > 5f && buf[i].PositionX < 30f && Math.Abs(buf[i].PositionY) < 30f,
                        $"{g.GetType().Name} put a point inside the block at ({buf[i].PositionX},{buf[i].PositionY})");
            }

            // The cone points towards the target (north).
            int cn = new ConeGenerator().Generate(Entity.Null, ref sensor, _repo, buf);
            for (int i = 0; i < cn; i++) Assert.True(buf[i].PositionY > 0f);
        }

        [Fact]
        public void PointPattern_WithNoAnchorYet_WaitsInsteadOfAnsweringEmpty()
        {
            var sensor = new EqsSensor { SearchRadius = 20f };   // no self, no slot 1
            Assert.True(new DonutGenerator { AnchorSlot = 1 }.Generate(Entity.Null, ref sensor, _repo, new EqsResult[16]) < 0);
        }

        // ── the starter templates on the real test-town ──────────────────────────────────────

        private static TerrainWorld TestTown()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Hrot", "Subsystems"))) dir = dir.Parent;
            Assert.NotNull(dir);
            var path = Path.Combine(dir!.FullName, "Hrot", "Subsystems", "Hrot.AI.Behaviors", "Recipes", "Terrain", "test-town",
                "test-town.world.geojson");
            return TerrainWorldParser.Parse(File.ReadAllText(path), "test-town");
        }

        /// <summary>The solver's phases (EqsSolverSystem.EvaluateSensor) without the ECS plumbing.</summary>
        private EqsResult[] Run(EqsQueryTemplate t, EqsSensor sensor)
        {
            var buf = new EqsResult[t.MaxCandidates];
            int n = t.Generator.Generate(Entity.Null, ref sensor, _repo, buf);
            if (n <= 0) return Array.Empty<EqsResult>();
            var list = buf.Take(n).ToArray();
            void Phase(IEqsTest[]? tests) { if (tests != null) foreach (var x in tests) x.ExecuteBatch(Entity.Null, ref sensor, _repo, list); }
            Phase(t.FilterCheap); Phase(t.FilterExpensive);
            list = list.Where(r => r.EntityId != -1L).ToArray();
            Phase(t.ScoreCheap);
            list = list.OrderByDescending(r => r.Score).Take(16).ToArray();
            Phase(t.ScoreExpensive);
            return list.Where(r => r.EntityId != -1L).OrderByDescending(r => r.Score).ToArray();
        }

        private static bool SeenBy(TerrainWorld w, Vector3 threat, EqsResult r, float aim)
            => !w.SegmentBlocked(threat + new Vector3(0, 0, 1.7f), new Vector3(r.PositionX, r.PositionY, r.PositionZ + aim));

        /// <summary>tt-nav-los: the observer at (300,80) looks for cover from Hostile B at (360,175). Every answer must be hidden
        /// from B (crouched), within the radius, and there must be one.</summary>
        [Fact]
        public void FindCoverFromTarget_OnTestTown_EveryAnswerIsHiddenFromTheTarget()
        {
            var town = TestTown();
            _repo.SetSingletonManaged(town);
            _repo.SetSingletonManaged<ICoverProvider>(TerrainCoverProvider.Build(town));
            var self = At(300, 80);
            var b = At(360, 175);
            var sensor = new EqsSensor { SearchRadius = 60f, ContextSlot0 = self, ContextSlot1 = b };

            var top = Run(FindCoverFromTarget.Build((IEqsTemplateBuilder)new EqsTemplateBuilder()), sensor);

            Assert.NotEmpty(top);
            Assert.All(top, r => Assert.False(SeenBy(town, new Vector3(360, 175, 0), r, 1.1f), $"({r.PositionX},{r.PositionY}) is in B's view"));
            Assert.All(top, r => Assert.True(Vector2.Distance(new(300, 80), new(r.PositionX, r.PositionY)) <= 60f));
        }

        /// <summary>A firing position on B sees B (standing eye) and is not inside a building.</summary>
        [Fact]
        public void FindOpenFiringPosition_OnTestTown_EveryAnswerSeesTheTarget()
        {
            var town = TestTown();
            _repo.SetSingletonManaged(town);
            var sensor = new EqsSensor { SearchRadius = 40f, ContextSlot0 = At(300, 80), ContextSlot1 = At(360, 175) };

            var top = Run(FindOpenFiringPosition.Build(new EqsTemplateBuilder()), sensor);

            Assert.NotEmpty(top);
            Assert.All(top, r =>
            {
                Assert.False(town.SegmentBlocked(new Vector3(r.PositionX, r.PositionY, r.PositionZ + 1.7f), new Vector3(360, 175, 0.85f)));
                Assert.False(EqsTerrainSight.InsideSolid(town, new Vector2(r.PositionX, r.PositionY)));
            });
        }

        /// <summary>A retreat point is hidden from B and, on the whole, farther from it than the self.</summary>
        [Fact]
        public void FindSafeRetreatPoint_OnTestTown_IsHiddenAndAway()
        {
            var town = TestTown();
            _repo.SetSingletonManaged(town);
            var sensor = new EqsSensor { SearchRadius = 40f, ContextSlot0 = At(300, 80), ContextSlot1 = At(360, 175) };

            var top = Run(FindSafeRetreatPoint.Build(new EqsTemplateBuilder()), sensor);

            Assert.NotEmpty(top);
            Assert.All(top, r => Assert.False(SeenBy(town, new Vector3(360, 175, 0), r, 1.1f)));
            float selfDist = Vector2.Distance(new(300, 80), new(360, 175));
            Assert.True(Vector2.Distance(new(top[0].PositionX, top[0].PositionY), new(360, 175)) > selfDist);
        }

        /// <summary>A flank on B sees B and lies off the B→self line.</summary>
        [Fact]
        public void FindFlankingPosition_OnTestTown_SeesTheTargetFromTheSide()
        {
            var town = TestTown();
            _repo.SetSingletonManaged(town);
            var sensor = new EqsSensor { SearchRadius = 30f, ContextSlot0 = At(300, 80), ContextSlot1 = At(360, 175) };

            var top = Run(FindFlankingPosition.Build(new EqsTemplateBuilder()), sensor);

            Assert.NotEmpty(top);
            var axis = Vector2.Normalize(new Vector2(300 - 360, 80 - 175));
            var best = Vector2.Normalize(new Vector2(top[0].PositionX - 360, top[0].PositionY - 175));
            Assert.True(MathF.Abs(Vector2.Dot(axis, best)) < 0.5f, "the best flank is roughly side-on to the target→self line");
        }

        /// <summary>Every starter template's BlueprintId is the canonical hash of its AssetId (the blueprint compiler's key).</summary>
        [Theory]
        [InlineData(typeof(FindCoverFromTarget))]
        [InlineData(typeof(FindOpenFiringPosition))]
        [InlineData(typeof(FindFlankingPosition))]
        [InlineData(typeof(FindSafeRetreatPoint))]
        [InlineData(typeof(FindThreatsInView))]
        public void StarterTemplate_BlueprintIdIsTheHashOfItsAssetId(Type template)
        {
            string assetId = (string)template.GetField("AssetId")!.GetValue(null)!;
            uint id = (uint)template.GetField("BlueprintId")!.GetValue(null)!;
            Assert.Equal(EqsTemplateRegistry.BlueprintIdOf(new Guid(assetId)), id);
        }
    }
}
