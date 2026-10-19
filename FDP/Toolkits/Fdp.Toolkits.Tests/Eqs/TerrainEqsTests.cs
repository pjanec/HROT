using System;
using System.Collections.Generic;
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

        /// <summary>
        /// ⭐ <c>CE-3135</c> P-1 (peek-and-fire D5) — a candidate that CARRIES a stance looks from that stance's eye: crouched behind a
        /// waist-high wall it does NOT see the target the unstanced (standing-eye) candidate above sees. And a result with no stance
        /// — every non-cover generator, every recording made before the field — reads "none".
        /// </summary>
        [Fact]
        public void P1_ACandidateWithAStance_LooksFromThatStancesEye_AndNoStanceReadsNone()
        {
            _repo.SetSingletonManaged(WallWorld(height: 1.2f));
            var sensor = new EqsSensor { ContextSlot0 = At(0, 0), ContextSlot1 = At(10, 30) };
            var c = new[] { Point(10, 9) };
            c[0].Stance = EqsResult.EncodeStance(Fdp.Toolkit.Tkb.Domain.StanceId.Crouched);

            new CheapLineOfSightTest { Viewer = EqsLosViewer.Candidate, Require = EqsLosRequire.Visible }
                .ExecuteBatch(Entity.Null, ref sensor, _repo, c);

            Assert.Equal(-1L, c[0].EntityId);   // rejected: a crouched eye (1.1 m) is under the 1.2 m wall
            Assert.Equal(0, c[0].Flags & 2);
            Assert.False(default(EqsResult).TryGetStance(out _));
        }

        /// <summary>
        /// ⭐ <c>CE-3144</c> (peek-and-fire D15) — "can I shoot him from there" looks at the target's BODY POINTS and is clear when
        /// ANY is: a standing man 1 m behind a 1.2 m wall shows his head (1.6 m) though his middle (0.85 m — the old single aim) is
        /// hidden; crouched (top ≈ 1.0 m) he is hidden; a remembered POINT is a standing man's points.
        /// </summary>
        [Fact]
        public void CE3144_D15_TheCandidateSeesAnyBodyPoint_ByTheTargetsStance_AndAPointIsAStandingMan()
        {
            _repo.RegisterComponent<Hrot.MuscleCharacter.Animation.Components.StanceIntent>();
            _repo.SetSingletonManaged(WallWorld(height: 1.2f));
            var target = At(10, 11.5f);   // 1 m behind the wall (y 10 … 10.5)
            var self = At(0, 0);
            var test = new CheapLineOfSightTest { Viewer = EqsLosViewer.Candidate, Require = EqsLosRequire.Visible };

            var sensor = new EqsSensor { ContextSlot0 = self, ContextSlot1 = target };
            var c = new[] { Point(10, 0) };
            test.ExecuteBatch(Entity.Null, ref sensor, _repo, c);
            Assert.Equal(0L, c[0].EntityId);   // kept: the head clears the wall

            _repo.AddComponent(target, new Hrot.MuscleCharacter.Animation.Components.StanceIntent { TargetStance = Fdp.Toolkit.Tkb.Domain.StanceId.Crouched });
            c = new[] { Point(10, 0) };
            test.ExecuteBatch(Entity.Null, ref sensor, _repo, c);
            Assert.Equal(-1L, c[0].EntityId);  // crouched: every body point is under the wall

            var atPoint = new EqsSensor
            {
                ContextSlot0 = self, ContextPoint1 = new Vector3(10, 11.5f, 0), ContextPointMask = EqsSensor.Point1Bit,
            };
            c = new[] { Point(10, 0) };
            test.ExecuteBatch(Entity.Null, ref atPoint, _repo, c);
            Assert.Equal(0L, c[0].EntityId);   // a remembered spot: a standing man there would be seen
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

        // ── Stage 7a: cover inside buildings (docs/DESIGN_Building_Interiors.md §3l, CE-3134) ──────────────────

        /// <summary>A recipe terrain with its building templates (bt-range's House A is a two-storey template).</summary>
        private static TerrainWorld Recipe(string name)
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Hrot", "Subsystems"))) dir = dir.Parent;
            Assert.NotNull(dir);
            var folder = Path.Combine(dir!.FullName, "Hrot", "Subsystems", "Hrot.AI.Behaviors", "Recipes", "Terrain", name);
            var file = Directory.GetFiles(folder, "*.world.geojson").Single();
            return TerrainWorldParser.Parse(File.ReadAllText(file), name, TerrainAssets.ForFolder(folder));
        }

        /// <summary>House A on bt-range: footprint (100,100)–(110,108), storeys at Z 0 and 3, roof at 6; an inner wall at x = 105
        /// on the ground storey; a stair ramp in (107.5..108.5, 101..106).</summary>
        private static (TerrainWorld World, List<CoverPoint> Points) HouseA()
        {
            var w = Recipe("bt-range");
            var pts = TerrainCoverProvider.Build(w).Points
                .Where(p => p.PositionX > 97 && p.PositionX < 113 && p.PositionY > 97 && p.PositionY < 111).ToList();
            Assert.NotEmpty(pts);
            return (w, pts);
        }

        private static bool InHouseA(CoverPoint p) => p.PositionX > 100 && p.PositionX < 110 && p.PositionY > 100 && p.PositionY < 108;

        /// <summary>📌 Measured before Stage 7a: 26 coincident pairs on House A — a storey's wall was read once per expanded piece AND
        /// once per storey prism stacked on it. One point per spot and kind (a window has a Cover and a WindowFiring point).</summary>
        [Fact]
        public void CE3134_HouseA_NoTwoPointsOfOneKindStandOnOneSpot()
        {
            var (_, pts) = HouseA();
            for (int i = 0; i < pts.Count; i++)
                for (int j = i + 1; j < pts.Count; j++)
                    Assert.False(pts[i].Kind == pts[j].Kind
                                 && MathF.Abs(pts[i].PositionX - pts[j].PositionX) < 0.1f
                                 && MathF.Abs(pts[i].PositionY - pts[j].PositionY) < 0.1f
                                 && MathF.Abs(pts[i].PositionZ - pts[j].PositionZ) < 0.1f,
                        $"two {pts[i].Kind} points at ({pts[i].PositionX:F2},{pts[i].PositionY:F2},{pts[i].PositionZ:F2})");
        }

        /// <summary>📌 Measured before Stage 7a: crouch cover IN the front doorway — the lintel above it read as a 0.9 m wall. A door
        /// span gives no point on its storey.</summary>
        [Fact]
        public void CE3134_NoPointStandsInADoorway()
        {
            var (w, pts) = HouseA();
            int doors = 0;
            foreach (var panel in w.Panels.Where(q => q.Building == 0))
            {
                var dir = (panel.B - panel.A) / panel.Length;
                foreach (var o in panel.Openings.Where(o => o.Kind != TerrainOpeningKind.Window))
                {
                    doors++;
                    foreach (var p in pts.Where(p => MathF.Abs(p.PositionZ - panel.BaseZ) <= 0.3f))
                    {
                        var d = new Vector2(p.PositionX, p.PositionY) - panel.A;
                        float t = Vector2.Dot(d, dir), off = MathF.Abs(dir.X * d.Y - dir.Y * d.X);
                        Assert.False(t > o.At && t < o.At + o.Width && off < panel.Thickness * 0.5f + TerrainCoverProvider.StandOff + 0.1f,
                            $"a point in the {o.Kind} of panel ({panel.A})→({panel.B}) at ({p.PositionX:F2},{p.PositionY:F2},{p.PositionZ:F2})");
                    }
                }
            }
            Assert.Equal(3, doors);   // front, back, the inner wall's
        }

        /// <summary>📌 Measured before Stage 7a: points OUTSIDE the house at Z 3 — the upper storey's outer face, standing in the
        /// air. A point stands on its storey's floor (or the ground), never above nothing; none on the stair ramp's opening.</summary>
        [Fact]
        public void CE3134_EveryPointStandsOnItsStoreyFloor_AndAnUpperOneIsInside()
        {
            var (_, pts) = HouseA();
            Assert.All(pts, p => Assert.True(MathF.Abs(p.PositionZ) < 0.05f || MathF.Abs(p.PositionZ - 3f) < 0.05f,
                $"({p.PositionX:F2},{p.PositionY:F2}) at Z {p.PositionZ:F2} is on neither the ground nor the upper floor"));
            var upper = pts.Where(p => p.PositionZ > 0.5f).ToList();
            Assert.True(upper.Count >= 10, $"the upper storey has only {upper.Count} points");
            Assert.All(upper, p => Assert.True(InHouseA(p), $"({p.PositionX:F2},{p.PositionY:F2}) on the upper storey is outside the house"));
            Assert.DoesNotContain(upper, p => p.PositionX > 107.5f && p.PositionX < 108.5f && p.PositionY > 101f && p.PositionY < 106f);
        }

        /// <summary>The inner wall at x = 105 is cover from both rooms: a point on each face, facing it, on the ground floor.</summary>
        [Fact]
        public void CE3134_TheInnerWall_IsCoverOnBothFaces()
        {
            var (_, pts) = HouseA();
            var west = pts.Where(p => MathF.Abs(p.PositionZ) < 0.05f && p.PositionX > 103.5f && p.PositionX < 105f && p.DirectionX > 0.9f).ToList();
            var east = pts.Where(p => MathF.Abs(p.PositionZ) < 0.05f && p.PositionX > 105f && p.PositionX < 106.5f && p.DirectionX < -0.9f).ToList();
            Assert.Equal(3, west.Count);
            Assert.Equal(3, east.Count);
            Assert.All(west.Concat(east), p => Assert.Equal(2, p.StanceHeight));
        }

        /// <summary>A window (sill 0.9 m) is crouch cover on both faces, and on the INSIDE face a crouch firing position facing out.</summary>
        [Fact]
        public void CE3134_AWindow_IsSillCoverOnBothFaces_AndAFiringPositionInside()
        {
            var (_, pts) = HouseA();
            var at = pts.Where(p => MathF.Abs(p.PositionY - 103.6f) < 0.05f && MathF.Abs(p.PositionZ) < 0.05f).ToList();   // the east window
            Assert.Contains(at, p => p.Kind == CoverKind.Cover && p.PositionX < 110 && p.StanceHeight == 1);
            Assert.Contains(at, p => p.Kind == CoverKind.Cover && p.PositionX > 110 && p.StanceHeight == 1);
            var fire = Assert.Single(at, p => p.Kind == CoverKind.WindowFiring);
            Assert.True(InHouseA(fire));
            Assert.Equal(1, fire.StanceHeight);
            Assert.True(fire.DirectionX > 0.9f, "a firing position faces out through the window");
            Assert.All(pts.Where(p => p.Kind == CoverKind.WindowFiring), p => Assert.True(InHouseA(p)));
        }

        /// <summary>A radius query returns one kind: the 3-argument form is cover only; the kind form is that kind only.</summary>
        [Fact]
        public void CE3134_ARadiusQuery_ReturnsOneKind()
        {
            var cover = TerrainCoverProvider.Build(Recipe("bt-range"));
            var buf = new CoverPoint[256];
            int n = cover.GetCoverPointsInRadius(new Vector2(105, 104), 8f, buf);
            Assert.True(n > 0);
            Assert.All(buf.Take(n), p => Assert.Equal(CoverKind.Cover, p.Kind));
            n = cover.GetCoverPointsInRadius(new Vector2(105, 104), 8f, buf, CoverKind.WindowFiring);
            Assert.True(n >= 5, $"House A has 5 windows with firing positions, found {n}");
            Assert.All(buf.Take(n), p => Assert.Equal(CoverKind.WindowFiring, p.Kind));
        }

        /// <summary>A provider that knows only cover answers a window query with nothing (the interface default).</summary>
        [Fact]
        public void CE3134_AProviderThatKnowsOnlyCover_HasNoWindowPositions()
        {
            ICoverProvider manual = new OnlyCover();
            Assert.Equal(1, manual.GetCoverPointsInRadius(Vector2.Zero, 5f, new CoverPoint[4], CoverKind.Cover));
            Assert.Equal(0, manual.GetCoverPointsInRadius(Vector2.Zero, 5f, new CoverPoint[4], CoverKind.WindowFiring));
        }

        private sealed class OnlyCover : ICoverProvider
        {
            public int GetCoverPointsInRadius(Vector2 center, float radius, Span<CoverPoint> results) { results[0] = default; return 1; }
        }

        /// <summary>The lowest stance whose eye clears the sill by 0.1 m and stays under the head (eyes 0.35 / 1.1 / 1.7).</summary>
        [Theory]
        [InlineData(0.9f, 2.1f, 1)]     // a 0.9 m sill: crouch
        [InlineData(0.2f, 2.1f, 0)]     // a floor-level slit: prone
        [InlineData(1.3f, 2.1f, 2)]     // a high sill: standing
        [InlineData(1.65f, 2.1f, 255)]  // above a standing eye: no firing position
        [InlineData(0.9f, 1.1f, 255)]   // a crouch eye clears the sill but not the head
        public void CE3134_FiringStance_IsTheLowestEyeThatFits(float sill, float head, int stance)
            => Assert.Equal((byte)stance, TerrainCoverProvider.FiringStance(sill, head));

        /// <summary>⭐ The plan's acceptance (§3l 7a-2): a rifleman upstairs in House A, the target south of the house —
        /// <c>FindWindowFiringPosition</c> answers a SOUTH window on HIS storey (Z 3), inside the house, and the database point
        /// there is a crouch position. ⭐ CE-3135 (P-1): the ANSWER carries that crouch, and every answer sees the target from
        /// the eye of ITS stance (was: a standing eye — over a sill nobody fires over).</summary>
        [Fact]
        public void CE3134_FindWindowFiringPosition_UpstairsInHouseA_AnswersASouthWindowOnHisStorey()
        {
            var w = Recipe("bt-range");
            var cover = TerrainCoverProvider.Build(w);
            _repo.SetSingletonManaged(w);
            _repo.SetSingletonManaged<ICoverProvider>(cover);
            var target = new Vector3(104, 80, 0);
            var sensor = new EqsSensor { SearchRadius = 15f, ContextSlot0 = At(104, 104, 3), ContextSlot1 = At(target.X, target.Y) };

            var top = Run(FindWindowFiringPosition.Build(new EqsTemplateBuilder()), sensor);

            Assert.NotEmpty(top);
            var best = top[0];
            Assert.Equal(100.9f, best.PositionY, 2);       // the south wall's inside face
            Assert.Equal(3f, best.PositionZ, 2);           // his storey
            Assert.True(best.PositionX > 100 && best.PositionX < 110);
            var point = Assert.Single(cover.Points, p => p.Kind == CoverKind.WindowFiring
                && MathF.Abs(p.PositionX - best.PositionX) < 0.01f && MathF.Abs(p.PositionY - best.PositionY) < 0.01f
                && MathF.Abs(p.PositionZ - best.PositionZ) < 0.01f);
            Assert.Equal(1, point.StanceHeight);           // a 0.9 m sill: crouch
            Assert.True(best.TryGetStance(out var stance));   // ⭐ P-1: the answer carries it
            Assert.Equal(Fdp.Toolkit.Tkb.Domain.StanceId.Crouched, stance);
            Assert.All(top, r => Assert.True(r.TryGetKind(out var k) && k == CoverKind.WindowFiring));   // ⭐ CE-3158 G1: and its kind
            var mount = Fdp.Toolkit.Perception.LineOfSight.TerrainWorldLosStrategy.DefaultMount;
            Assert.All(top, r => Assert.False(
                w.SegmentBlocked(new Vector3(r.PositionX, r.PositionY, r.PositionZ + (r.TryGetStance(out var s) ? mount.For(s) : mount.Standing)),
                    target + new Vector3(0, 0, 0.85f)),
                $"({r.PositionX:F2},{r.PositionY:F2},{r.PositionZ:F2}) does not see the target"));
            Assert.DoesNotContain(top, r => r.PositionY > 104);   // the north window looks away from the target
        }

        /// <summary>The generator asks for ITS kind: the default is cover (FindCoverFromTarget unchanged), and a window generator
        /// never yields a cover point.</summary>
        [Fact]
        public void CE3134_CoverPointsGenerator_GeneratesItsKindOnly()
        {
            _repo.SetSingletonManaged<ICoverProvider>(new ManualCoverProvider(new[]
            {
                new CoverPoint { PositionX = 1, PositionY = 0, Quality = 1f, Kind = CoverKind.Cover },
                new CoverPoint { PositionX = 2, PositionY = 0, Quality = 1f, Kind = CoverKind.WindowFiring },
            }));
            var sensor = new EqsSensor { SearchRadius = 10f, ContextSlot0 = At(0, 0) };
            var c = new EqsResult[8];

            Assert.Equal(1, new CoverPointsGenerator().Generate(Entity.Null, ref sensor, _repo, c));
            Assert.Equal(1f, c[0].PositionX);
            Assert.True(c[0].TryGetStance(out var st));   // ⭐ P-1: StanceHeight 0 (prone) travels as StanceId.Prone
            Assert.Equal(Fdp.Toolkit.Tkb.Domain.StanceId.Prone, st);
            Assert.Equal(1, new CoverPointsGenerator { Kind = CoverKind.WindowFiring }.Generate(Entity.Null, ref sensor, _repo, c));
            Assert.Equal(2f, c[0].PositionX);
        }

        /// <summary>
        /// ⭐⭐ <c>CE-2275</c> D-2b (DA-6, DA-41) — the SET-BACK post of House A's east window lies 2 m back into the room on the line through
        /// the window (same storey, same facing); a depth that would cross the far wall at both tries falls back to the window point itself.
        /// ✅ Red-proofs: pushed the wrong way ⇒ outside the house; the wall check dropped ⇒ the 40 m depth lands outside.
        /// </summary>
        [Fact]
        public void CE2275_SetBack_TheEastWindowsPost_IsTwoMetresBackOnItsLine_AndAnImpossibleDepthStaysAtTheSill()
        {
            var (w, pts) = HouseA();
            var fire = Assert.Single(pts, p => p.Kind == CoverKind.WindowFiring
                && MathF.Abs(p.PositionY - 103.6f) < 0.05f && MathF.Abs(p.PositionZ) < 0.05f);

            var back = TerrainCoverProvider.SetBack(w, in fire, 2f, 1.5f);
            Assert.Equal(fire.PositionX - 2f, back.X, 2);   // the window faces +X: back is −X, deeper in the room
            Assert.Equal(fire.PositionY, back.Y, 2);
            Assert.Equal(fire.PositionZ, back.Z, 2);
            Assert.True(InHouseA(new CoverPoint { PositionX = back.X, PositionY = back.Y }));

            var none = TerrainCoverProvider.SetBack(w, in fire, 40f, 30f);   // through the west wall, both tries
            Assert.Equal(new Vector3(fire.PositionX, fire.PositionY, fire.PositionZ), none);
        }

        /// <summary>
        /// ⭐ <c>CE-2275</c> D-2j (DA-16, DA-50) — a set-back pass MARKS what it did: House A's east window, moved 2 m back, carries §4.2 bit 8
        /// <c>IsSetBack</c>; a window 1 m in front of the west wall (no room behind it at either depth) stays at the sill with the bit
        /// meaningful and UNSET; a plain window pass marks nothing. What lets a Veteran squad keep only real set-back posts.
        /// ✅ Red-proof: the flag written for every point ⇒ the shallow window reads as set back.
        /// </summary>
        [Fact]
        public void CE2275_D2j_ASetBackPass_MarksTheWindowsItMovedBack_AndLeavesTheShallowOneUnmarked()
        {
            var (w, pts) = HouseA();
            var east = Assert.Single(pts, p => p.Kind == CoverKind.WindowFiring
                && MathF.Abs(p.PositionY - 103.6f) < 0.05f && MathF.Abs(p.PositionZ) < 0.05f);
            var shallow = east with { PositionX = 101f, DirectionX = 1f, DirectionY = 0f };   // backs onto the west wall at x 100
            _repo.SetSingletonManaged(w);
            _repo.SetSingletonManaged<ICoverProvider>(new ManualCoverProvider(new[] { east, shallow }));
            var sensor = new EqsSensor { SearchRadius = 20f, ContextSlot0 = At(105f, 104f) };
            var c = new EqsResult[8];

            int n = new CoverPointsGenerator { Kind = CoverKind.WindowFiring, SetBackMetres = 2f }.Generate(Entity.Null, ref sensor, _repo, c);
            Assert.Equal(2, n);
            var moved = c[..n].Single(r => MathF.Abs(r.PositionX - (east.PositionX - 2f)) < 0.05f);   // 📐 109.1 → 107.1, 2 m back
            var stayed = c[..n].Single(r => MathF.Abs(r.PositionX - 101f) < 0.05f);
            Assert.NotEqual(0, moved.Flags & CoverPointsGenerator.FlagSetBack);
            Assert.NotEqual(0, stayed.FlagsMeaningful & CoverPointsGenerator.FlagSetBack);
            Assert.Equal(0, stayed.Flags & CoverPointsGenerator.FlagSetBack);

            n = new CoverPointsGenerator { Kind = CoverKind.WindowFiring }.Generate(Entity.Null, ref sensor, _repo, c);
            for (int i = 0; i < n; i++) Assert.Equal(0, c[i].FlagsMeaningful & CoverPointsGenerator.FlagSetBack);
        }

        /// <summary>
        /// ⭐ <c>CE-2275</c> D-2b — <c>FindWindowSetBackPosition</c> upstairs in House A (the acceptance of
        /// <see cref="CE3134_FindWindowFiringPosition_UpstairsInHouseA_AnswersASouthWindowOnHisStorey"/>, set back): every answer is a
        /// window position ≥ 1.5 m back from every sill point, on his storey, and still sees the target from its stance's eye.
        /// ✅ Red-proof: the generator option ignored ⇒ the answers are the sill points.
        /// </summary>
        [Fact]
        public void CE2275_FindWindowSetBackPosition_UpstairsInHouseA_AnswersSetBackPointsThatStillSeeTheTarget()
        {
            var w = Recipe("bt-range");
            var cover = TerrainCoverProvider.Build(w);
            _repo.SetSingletonManaged(w);
            _repo.SetSingletonManaged<ICoverProvider>(cover);
            // ⚠ measured: at the sill rail's 24 m target the upstairs set-back posts do NOT see it — 2 m back, the line down to a man that
            //   close crosses the sill (DA-6's "narrower arc") and the ground floor answers instead. A far target is what a defence watches.
            var target = new Vector3(104, 20, 0);
            var sensor = new EqsSensor { SearchRadius = 15f, ContextSlot0 = At(104, 104, 3), ContextSlot1 = At(target.X, target.Y) };

            var top = Run(FindWindowSetBackPosition.Build(new EqsTemplateBuilder()), sensor);

            Assert.NotEmpty(top);
            Assert.Equal(3f, top[0].PositionZ, 2);
            Assert.True(top[0].PositionY > 100.9f + 1.4f, $"set back from the south wall's inside face: {top[0].PositionY:F2}");
            var sills = cover.Points.Where(p => p.Kind == CoverKind.WindowFiring).ToList();
            var mount = Fdp.Toolkit.Perception.LineOfSight.TerrainWorldLosStrategy.DefaultMount;
            Assert.All(top, r =>
            {
                Assert.True(r.TryGetKind(out var k) && k == CoverKind.WindowFiring);
                Assert.All(sills, s => Assert.True(Vector3.Distance(new Vector3(s.PositionX, s.PositionY, s.PositionZ),
                    new Vector3(r.PositionX, r.PositionY, r.PositionZ)) >= 1.45f));
                Assert.False(w.SegmentBlocked(
                    new Vector3(r.PositionX, r.PositionY, r.PositionZ + (r.TryGetStance(out var s) ? mount.For(s) : mount.Standing)),
                    target + new Vector3(0, 0, 0.85f)), $"({r.PositionX:F2},{r.PositionY:F2},{r.PositionZ:F2}) does not see the target");
            });
        }

        /// <summary>
        /// ⭐ <c>CE-2275</c> sq-defend-house (Platoon §12.2 B) — on <c>cqb-village</c>, a squad in OBJ (house-3f, front north) whose sector
        /// looks south (its point 45 m out) gets WINDOW posts from both window asks: the sill one and the set-back one each answer OBJ's
        /// south windows. 📌 Measured live first: the post ask gave no window posts and the squad fell back to cover points.
        /// </summary>
        [Fact]
        public void CE2275_CqbVillage_OBJsSouthWindows_AnswerBothWindowAsks_TowardASouthSector()
        {
            var w = Recipe("cqb-village");
            var cover = TerrainCoverProvider.Build(w);
            _repo.SetSingletonManaged(w);
            _repo.SetSingletonManaged<ICoverProvider>(cover);
            var ea = new Vector3(120.9f, 112.2f, 0f);
            var self = At(120.9f, 157f, 0f);
            var mount = Fdp.Toolkit.Perception.LineOfSight.TerrainWorldLosStrategy.DefaultMount;
            var windows = cover.Points.Where(p => p.Kind == CoverKind.WindowFiring
                && Vector2.Distance(new Vector2(p.PositionX, p.PositionY), new Vector2(120.9f, 157f)) <= 14f).ToList();
            string Seen(Vector3 at, byte stance) => w.SegmentBlocked(at + new Vector3(0, 0, mount.For(CoverPointsGenerator.StanceOf(stance))),
                ea + new Vector3(0, 0, 0.85f)) ? "blocked" : "SEES";
            var report = string.Join("; ", windows.Select(p =>
            {
                var sill = new Vector3(p.PositionX, p.PositionY, p.PositionZ);
                var back = TerrainCoverProvider.SetBack(w, in p, 2f, 1.5f);
                return $"({p.PositionX:F1},{p.PositionY:F1},{p.PositionZ:F0}) faces ({p.DirectionX:F1},{p.DirectionY:F1}) st {p.StanceHeight}: sill {Seen(sill, p.StanceHeight)}, back ({back.X:F1},{back.Y:F1}) {Seen(back, p.StanceHeight)}";
            }));
            Assert.True(windows.Count > 0, "OBJ has window points");

            var sensor = new EqsSensor { SearchRadius = 14f, ContextSlot0 = self, ContextSlot1 = At(ea.X, ea.Y) };
            var atSill = Run(FindDefensiveWindowPosts.Build(new EqsTemplateBuilder()), sensor);
            var setBack = Run(FindDefensiveWindowSetBackPosts.Build(new EqsTemplateBuilder()), sensor);
            Assert.True(atSill.Length > 0, $"the sill ask answers ({windows.Count} window points: {report})");
            Assert.True(setBack.Length > 0, $"the set-back ask answers ({windows.Count} window points: {report})");
        }

        /// <summary>
        /// ⭐ <c>CE-2275</c> D-2g (Defend DA-5 / DA-22 / DA-47) — <c>FindRoofPosts</c> on <c>cqb-village</c>: OBJ (house-3f) has a roof
        /// storey at 6 m with a 1 m parapet, so it carries crouched cover points; toward a south sector the roof ask answers ONLY roof
        /// points (the top surface under them, within half a metre) and every one SEES the sector point. 📌 Measured first: OBJ's roof has
        /// 22 cover points and <c>FindCoverFromTarget</c> keeps none of them (it wants points HIDDEN from the target) — hence its own ask.
        /// The generator alone, asked about the same house, yields no ground or upper-floor point.
        /// </summary>
        [Fact]
        public void CE2275_D2g_FindRoofPosts_OnOBJ_AnswersRoofPointsOnly_ThatSeeTheSector()
        {
            var w = Recipe("cqb-village");
            var cover = TerrainCoverProvider.Build(w);
            _repo.SetSingletonManaged(w);
            _repo.SetSingletonManaged<ICoverProvider>(cover);
            var ea = new Vector3(120.9f, 112.2f, 0f);
            var mount = Fdp.Toolkit.Perception.LineOfSight.TerrainWorldLosStrategy.DefaultMount;
            Assert.True(cover.Points.Count(p => p.PositionZ > 5f
                && Vector2.Distance(new Vector2(p.PositionX, p.PositionY), new Vector2(120.9f, 157f)) <= 14f) > 0, "OBJ's roof has cover points");
            var sensor = new EqsSensor { SearchRadius = 14f, ContextSlot0 = At(120.9f, 157f, 0f), ContextSlot1 = At(ea.X, ea.Y) };

            var answer = Run(FindRoofPosts.Build(new EqsTemplateBuilder()), sensor);

            Assert.NotEmpty(answer);
            Assert.All(answer, r =>
            {
                var at = new Vector3(r.PositionX, r.PositionY, r.PositionZ);
                Assert.Equal(6f, r.PositionZ, 1);
                Assert.True(CoverPointsGenerator.OnRoof(w, at), $"({at.X:F1},{at.Y:F1},{at.Z:F1}) is on the roof");
                Assert.False(w.SegmentBlocked(at + new Vector3(0, 0, r.TryGetStance(out var s) ? mount.For(s) : mount.Standing),
                    ea + new Vector3(0, 0, 0.85f)), $"({at.X:F1},{at.Y:F1}) sees the sector");
            });

            var generated = new EqsResult[64];
            int n = new CoverPointsGenerator { RoofOnly = true }.Generate(Entity.Null, ref sensor, _repo, generated);
            Assert.True(n > 0);
            for (int i = 0; i < n; i++) Assert.Equal(6f, generated[i].PositionZ, 1);
        }

        /// <summary>Solid prisms and free walls are read exactly as before Stage 7a: test-town and basic-desert (no building
        /// templates) produce the same database, point for point (count + checksum measured on the pre-7a code).
        /// ⭐ RE-PINNED `2026-10-09` by <c>CE-3143</c> (deliberate): a side now gets a point per STARTED 2.5 m (rounded up) — basic-desert
        /// 303 → 313 (ten sides gained a second point); test-town unchanged (measured, count and checksum).</summary>
        [Theory]
        [InlineData("test-town", 612, -1979792939338976871L)]
        [InlineData("basic-desert", 313, -1036683430095822395L)]
        public void CE3134_TerrainsWithoutBuildings_KeepTheirCoverDatabase(string terrain, int count, long checksum)
        {
            var pts = TerrainCoverProvider.Build(Recipe(terrain)).Points;
            long h = 17;
            foreach (var p in pts)
                h = h * 31 + (long)MathF.Round(p.PositionX * 100) * 7 + (long)MathF.Round(p.PositionY * 100) * 13
                    + (long)MathF.Round(p.PositionZ * 100) + p.StanceHeight;
            Assert.Equal(count, pts.Count);
            Assert.Equal(checksum, h);
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

        /// <summary>
        /// ⭐ <c>CE-3136</c> P-4 (peek-and-fire D6) — the known threats come from the unit's PERCEPTION SENSOR CHILDREN (where
        /// <c>CE-3038</c> moved the lists): the unit carries no list of its own, one child sensor holds the north threat, another the
        /// east one — and both hold the east one, which counts once. Same geometry as the unit-level test ⇒ the same 0.5.
        /// 🔴 Red-proof: before P-4 the test read only the unit's own list ⇒ no threats ⇒ 0 (inert in production).
        /// </summary>
        [Fact]
        public unsafe void P4_ThreatExposure_ReadsTheUnitsSensorChildren()
        {
            if (!_repo.IsComponentTypeRegistered<Fdp.Toolkit.Replication.Components.PartMetadata>())
                _repo.RegisterComponent<Fdp.Toolkit.Replication.Components.PartMetadata>();
            _repo.SetSingletonManaged(WallWorld());
            var self = At(10, 0);
            var north = At(10, 30);
            var east = At(40, 5);
            Entity Child(int part, params Entity[] held)
            {
                var c = _repo.CreateEntity();
                _repo.AddComponent(c, new Fdp.Toolkit.Replication.Components.PartMetadata { ParentEntity = self, InstanceId = part });
                var list = new SensorContactList();
                foreach (var h in held) SensorContactList.UpdateSighting(ref list, (long)h.PackedValue, 1);
                for (int i = 0; i < list.Count; i++) list.State[i] = (byte)SensorContactState.Acquired;
                _repo.AddComponent(c, list);
                return c;
            }
            Child(1000, north, east);
            Child(1001, east);
            var sensor = new EqsSensor { ContextSlot0 = self };
            var c = new[] { Point(10, 8), Point(10, 40) };

            new ThreatExposureTest().ExecuteBatch(Entity.Null, ref sensor, _repo, c);

            Assert.Equal(0.5f, c[0].Score, 3);
            Assert.Equal(0f, c[1].Score, 3);
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

        // ── CE-2165 V3: beyond the threats' reach (DESIGN_Vehicle_Exposure_Cycle §3) ─────────────────────

        private const long TankType = 103, SoldierType = 2002, RpgManType = 9101, RiflemanType = 9102;

        /// <summary>A TKB: a T-72 (500 / 250 / 150 mm), a soldier (no armour), an RPG man (300 m, 300 mm), a rifleman (500 m, 5 mm).</summary>
        private static Fdp.Toolkit.Tkb.TkbDatabase VehicleTkb()
        {
            static Fdp.Interfaces.TkbTemplate Armed(string name, long type, float range, float pen)
            {
                var t = new Fdp.Interfaces.TkbTemplate(name, type);
                t.AddDescriptor(new Fdp.Toolkit.Tkb.Domain.WeaponSuiteDto { Mounts = { new Fdp.Toolkit.Tkb.Domain.WeaponMountDto { Range = range, Penetration = pen, DamagePerHit = 100 } } });
                return t;
            }
            var tank = new Fdp.Interfaces.TkbTemplate("T-72", TankType);
            tank.AddDescriptor(new Fdp.Toolkit.Tkb.Domain.CombatPlatformDefDto { MaxHealth = 2500, ArmorFront = 500, ArmorSide = 250, ArmorRear = 150 });
            var soldier = new Fdp.Interfaces.TkbTemplate("Soldier", SoldierType);
            soldier.AddDescriptor(new Fdp.Toolkit.Tkb.Domain.CombatPlatformDefDto { MaxHealth = 100 });
            var db = new Fdp.Toolkit.Tkb.TkbDatabase();
            db.Register(tank); db.Register(soldier);
            db.Register(Armed("RPG man", RpgManType, 300, 300)); db.Register(Armed("Rifleman", RiflemanType, 500, 5));
            return db;
        }

        private Entity Typed(float x, float y, long type, VehicleClass? cls = null)
        {
            if (!_repo.IsComponentTypeRegistered<TkbIdentity>()) _repo.RegisterComponent<TkbIdentity>();
            if (!_repo.IsComponentTypeRegistered<VehicleParams>()) _repo.RegisterComponent<VehicleParams>();
            var e = At(x, y);
            _repo.AddComponent(e, new TkbIdentity { TkbType = type });
            if (cls != null) _repo.AddComponent(e, new VehicleParams { Class = cls.Value });
            return e;
        }

        private unsafe EqsSensor Knowing(Entity self, params Entity[] threats)
        {
            var contacts = new SensorContactList();
            foreach (var t in threats) SensorContactList.UpdateSighting(ref contacts, (long)t.PackedValue, 1);
            for (int i = 0; i < contacts.Count; i++) contacts.State[i] = (byte)SensorContactState.Acquired;
            _repo.AddComponent(self, contacts);
            return new EqsSensor { ContextSlot0 = self };
        }

        /// <summary>
        /// ⭐ A TANK scores a point by its distance from the RPG man over his 300 m reach (his 300 mm beats its 150 mm rear) — 1 out of
        /// reach, ½ half-way, 0 on top of him — and IGNORES the rifleman, whose 5 mm hurts no face of it. A SOLDIER counts both: the
        /// mean of the RPG's 1 and the rifleman's 223.6 / 500. The TKB comes through the injected SOURCE, not the world.
        /// </summary>
        [Fact]
        public void V3_BeyondThreatRange_ATankFleesTheAtReach_AndIgnoresTheRifle_ASoldierCountsBoth()
        {
            var db = VehicleTkb();
            var rpg = Typed(0, 100, RpgManType);
            var rifle = Typed(100, 0, RiflemanType);
            var test = new BeyondThreatRangeTest { Tkb = () => db };

            var tank = Typed(0, 0, TankType, VehicleClass.Tank);
            var sensor = Knowing(tank, rpg, rifle);
            var c = new[] { Point(0, -200), Point(0, 250), Point(0, 100) };
            test.ExecuteBatch(Entity.Null, ref sensor, _repo, c);
            Assert.Equal(1f, c[0].Score, 3);
            Assert.Equal(0.5f, c[1].Score, 3);
            Assert.Equal(0f, c[2].Score, 3);

            var soldier = Typed(0, 0, SoldierType, VehicleClass.Pedestrian);
            var asSoldier = Knowing(soldier, rpg, rifle);
            var s = new[] { Point(0, -200) };
            test.ExecuteBatch(Entity.Null, ref asSoldier, _repo, s);
            Assert.Equal((1f + MathF.Sqrt(100 * 100 + 200 * 200) / 500f) / 2f, s[0].Score, 3);
        }

        /// <summary>⭐ No TKB — no source and none on the view (a solver snapshot before the host hands one over) — scores nothing.</summary>
        [Fact]
        public void V3_BeyondThreatRange_WithNoTkb_ScoresNothing()
        {
            var tank = Typed(0, 0, TankType, VehicleClass.Tank);
            var sensor = Knowing(tank, Typed(0, 100, RpgManType));
            var c = new[] { Point(0, -200) };
            new BeyondThreatRangeTest().ExecuteBatch(Entity.Null, ref sensor, _repo, c);
            Assert.Equal(0f, c[0].Score);
        }

        /// <summary>⭐ The host hands its TKB source to every TKB-reading test of every registered template, in every phase it sits in.</summary>
        [Fact]
        public void V3_InjectTkb_ReachesEveryTkbAwareTestOfEveryTemplate()
        {
            var registry = new EqsTemplateRegistry();
            var a = new BeyondThreatRangeTest();
            var b = new BeyondThreatRangeTest();
            registry.Register(Guid.NewGuid(), new EqsQueryTemplate { Generator = new GridGenerator(), ScoreExpensive = new IEqsTest[] { new DistanceScoreTest(), a } }, "A");
            registry.Register(Guid.NewGuid(), new EqsQueryTemplate { Generator = new GridGenerator(), ScoreCheap = new IEqsTest[] { b } }, "B");
            Func<Fdp.Interfaces.ITkbDatabase?> source = () => null;

            BeyondThreatRangeTest.InjectTkb(registry, source);

            Assert.Same(source, a.Tkb);
            Assert.Same(source, b.Tkb);
        }

        // ── CE-2165 V3 / V8: overhead cover (DESIGN_Vehicle_Exposure_Cycle §3) ─────────────────────────────

        /// <summary>A HIGH deck (4–4.5 m) over x, y ∈ [5, 15] and a LOW one (1.5–2 m) over x, y ∈ [-15, -5]; open sky elsewhere.</summary>
        private static TerrainWorld Decks() => new()
        {
            BoundsMin = new Vector2(-50, -50), BoundsMax = new Vector2(50, 50),
            Prisms = new[] { Deck(new Vector2(5, 5), new Vector2(15, 15), 4f, 4.5f), Deck(new Vector2(-15, -15), new Vector2(-5, -5), 1.5f, 2f) },
        };

        private static TerrainPrism Deck(Vector2 min, Vector2 max, float baseZ, float topZ) => new()
        {
            Kind = TerrainPrismKind.Building,
            Footprint = new[] { min, new Vector2(max.X, min.Y), max, new Vector2(min.X, max.Y) },
            BaseZ = baseZ, TopZ = topZ, Min = min, Max = max,
        };

        /// <summary>
        /// ⭐ A soldier is covered under either deck and not under the open sky; a 2.4 m TANK fits only under the HIGH deck — the low
        /// one is below its hull, so the line above its roof sees the sky (it could not drive under it anyway).
        /// </summary>
        [Fact]
        public void V3_OverheadCover_KeepsThePointsUnderADeck_AboveTheSelfsHeight()
        {
            if (!_repo.IsComponentTypeRegistered<Fdp.Toolkit.Physics.Components.PhysicsCollider>())
                _repo.RegisterComponent<Fdp.Toolkit.Physics.Components.PhysicsCollider>();
            if (!_repo.IsComponentTypeRegistered<VehicleParams>()) _repo.RegisterComponent<VehicleParams>();
            _repo.SetSingletonManaged(Decks());

            var soldier = At(40, 40);
            var asSoldier = new EqsSensor { ContextSlot0 = soldier };
            var s = new[] { Point(10, 10), Point(-10, -10), Point(30, 30) };   // high deck · low deck · open
            new OverheadCoverTest().ExecuteBatch(Entity.Null, ref asSoldier, _repo, s);
            Assert.Equal(new[] { 0L, 0L, -1L }, s.Select(p => p.EntityId).ToArray());

            var tank = At(40, -40);
            _repo.AddComponent(tank, new VehicleParams { Class = VehicleClass.Tank });
            _repo.AddComponent(tank, new Fdp.Toolkit.Physics.Components.PhysicsCollider { Radius = 3f, Height = 2.4f });
            var asTank = new EqsSensor { ContextSlot0 = tank };
            var t = new[] { Point(10, 10), Point(-10, -10), Point(30, 30) };
            new OverheadCoverTest().ExecuteBatch(Entity.Null, ref asTank, _repo, t);
            Assert.Equal(new[] { 0L, -1L, -1L }, t.Select(p => p.EntityId).ToArray());
        }

        /// <summary>⭐ A WALKABLE SLAB — how the terrain models a floor or a bridge deck (the ground under it stays standable) — is
        /// overhead cover too: the vertical line crosses its triangles.</summary>
        [Fact]
        public void V3_OverheadCover_AWalkableSlabIsCover()
        {
            _repo.SetSingletonManaged(new TerrainWorld
            {
                BoundsMin = new Vector2(-50, -50), BoundsMax = new Vector2(50, 50),
                Walkables = new[]
                {
                    new TerrainWalkable
                    {
                        Kind = TerrainWalkableKind.Slab,
                        Vertices = new[] { new Vector3(5, 5, 4.5f), new Vector3(15, 5, 4.5f), new Vector3(15, 15, 4.5f), new Vector3(5, 15, 4.5f) },
                        Triangles = new[] { 0, 1, 2, 0, 2, 3 },
                        Min = new Vector2(5, 5), Max = new Vector2(15, 15), MinZ = 4.5f, MaxZ = 4.5f,
                    },
                },
            });
            var sensor = new EqsSensor { ContextSlot0 = At(40, 40) };
            var c = new[] { Point(10, 10), Point(30, 30) };
            new OverheadCoverTest().ExecuteBatch(Entity.Null, ref sensor, _repo, c);
            Assert.Equal(new[] { 0L, -1L }, c.Select(p => p.EntityId).ToArray());
        }

        /// <summary>⭐ In <see cref="OverheadRequire.Score"/> mode nothing is dropped: a covered point gains the weight; with no terrain
        /// nothing is judged at all.</summary>
        [Fact]
        public void V3_OverheadCover_ScoreMode_AddsTheWeight_AndNoTerrainJudgesNothing()
        {
            var self = At(40, 40);
            var sensor = new EqsSensor { ContextSlot0 = self };
            var none = new[] { Point(10, 10) };
            new OverheadCoverTest().ExecuteBatch(Entity.Null, ref sensor, _repo, none);
            Assert.Equal(0L, none[0].EntityId);

            _repo.SetSingletonManaged(Decks());
            var c = new[] { Point(10, 10), Point(30, 30) };
            new OverheadCoverTest { Require = OverheadRequire.Score, Weight = 2f }.ExecuteBatch(Entity.Null, ref sensor, _repo, c);
            Assert.Equal(new[] { 0L, 0L }, c.Select(p => p.EntityId).ToArray());
            Assert.Equal(2f, c[0].Score);
            Assert.Equal(0f, c[1].Score);
        }

        // ── CE-2165 V3: hull-sized hide points (DESIGN_Vehicle_Exposure_Cycle §3) ────────────────────────

        /// <summary>
        /// ⭐ The wall of <see cref="WallWorld"/>, a PILLAR (x −3…−2, y 6…8) beside its west end, and a DECK (4–4.5 m) over x 8…12, y 6…9.
        /// Two man-sized points 0.75 m south of the wall (facing it): one mid-wall, one at its west end.
        /// </summary>
        private static TerrainWorld HideWorld() => new()
        {
            BoundsMin = new Vector2(-50, -50), BoundsMax = new Vector2(50, 50),
            Prisms = new[]
            {
                Prism(new Vector2(0, 10), new Vector2(20, 10.5f), 3f),
                Prism(new Vector2(-3, 6), new Vector2(-2, 8), 3f),
                Deck(new Vector2(8, 6), new Vector2(12, 9), 4f, 4.5f),
            },
        };

        /// <summary>
        /// ⭐ For a TANK (7 × 3.6 m, 2.4 m high) the mid-wall point moves out to half its width + 0.5 m (y 9.25 → 7.7), kept although a
        /// deck hangs above it (above the hull); the west-end point is dropped — the hull's box there runs into the pillar; and no
        /// point round the tank's OWN standing box is offered. A SOLDIER gets both points where they were.
        /// </summary>
        [Fact]
        public void V3_VehicleFootprint_StandsTheHullOffTheWall_DropsABlockedBox_AndNeverItsOwnBox()
        {
            if (!_repo.IsComponentTypeRegistered<Fdp.Toolkit.Physics.Components.PhysicsCollider>())
                _repo.RegisterComponent<Fdp.Toolkit.Physics.Components.PhysicsCollider>();
            if (!_repo.IsComponentTypeRegistered<VehicleParams>()) _repo.RegisterComponent<VehicleParams>();
            _repo.SetSingletonManaged(HideWorld());
            _repo.SetSingletonManaged<ICoverProvider>(new ManualCoverProvider(new[]
            {
                new CoverPoint { PositionX = 10, PositionY = 9.25f, DirectionY = 1f, Quality = 1f, StanceHeight = 2, Kind = CoverKind.Cover },
                new CoverPoint { PositionX = 1,  PositionY = 9.25f, DirectionY = 1f, Quality = 1f, StanceHeight = 2, Kind = CoverKind.Cover },
            }));
            var gen = new CoverPointsGenerator { VehicleFootprint = true };

            var soldier = new EqsSensor { SearchRadius = 30f, ContextSlot0 = At(10, 0) };
            var s = new EqsResult[16];
            Assert.Equal(2, gen.Generate(Entity.Null, ref soldier, _repo, s));
            Assert.Equal(9.25f, s[0].PositionY, 3);

            var tank = At(10, 0);
            _repo.AddComponent(tank, new VehicleParams { Class = VehicleClass.Tank, Length = 7f, Width = 3.6f });
            _repo.AddComponent(tank, new Fdp.Toolkit.Physics.Components.PhysicsCollider { Radius = 3.5f, Height = 2.4f });
            var asTank = new EqsSensor { SearchRadius = 30f, ContextSlot0 = tank };
            var t = new EqsResult[16];
            int n = gen.Generate(Entity.Null, ref asTank, _repo, t);
            Assert.Equal(1, n);
            Assert.Equal(10f, t[0].PositionX, 3);
            Assert.Equal(7.7f, t[0].PositionY, 3);
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
            EqsFilters.Run(in t, Entity.Null, ref sensor, _repo, list);   // ⭐ the solver's own filter pipeline (CE-3143), incl. CE-2164's soft filters
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

        /// <summary>⭐ <c>CE-2158</c> F-c — a bounding squad asks for cover hidden from a POINT (where the enemy is, or is expected)
        /// as well as from an entity, and the two agree: north of the squad at (140,200) with the threat at (195,300), the answers
        /// are on Block B's south face (y ≈ 229). ⚠ Measured: the Low Wall (0.5 m, prone points) on the way is NOT an answer —
        /// a point candidate is judged at the CROUCHED eye (<c>CheapLineOfSightTest</c>, Viewer = Slot), and the movers are not
        /// ordered prone there either.</summary>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void CE2158_FindCoverFromTarget_HiddenFromAPointOrAnEntity_FindsBlockBsSouthFace(bool asEntity)
        {
            var town = TestTown();
            _repo.SetSingletonManaged(town);
            _repo.SetSingletonManaged<ICoverProvider>(TerrainCoverProvider.Build(town));
            var self = At(140, 200);
            var threat = new Vector3(195, 300, 0);
            var sensor = asEntity
                ? new EqsSensor { SearchRadius = 35f, ContextSlot0 = self, ContextSlot1 = At(threat.X, threat.Y) }
                : new EqsSensor { SearchRadius = 35f, ContextSlot0 = self, ContextPoint1 = threat, ContextPointMask = EqsSensor.Point1Bit };

            var top = Run(FindCoverFromTarget.Build((IEqsTemplateBuilder)new EqsTemplateBuilder()), sensor);

            Assert.NotEmpty(top);
            Assert.All(top, r => Assert.InRange(r.PositionY, 228.5f, 230f));   // the face, outside the building
            Assert.All(top, r => Assert.False(SeenBy(town, threat, r, 1.1f)));

            var lowWall = new EqsSensor { SearchRadius = 35f, ContextSlot0 = At(147, 72), ContextPoint1 = threat, ContextPointMask = EqsSensor.Point1Bit };
            Assert.Empty(Run(FindCoverFromTarget.Build((IEqsTemplateBuilder)new EqsTemplateBuilder()), lowWall));
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

        /// <summary>
        /// ⭐⭐ <c>CE-2164</c> (R-3004) — on test-town where the live squad broke contact (the squad at (146,65), the hostile at (202,156),
        /// 60 m): NO point is hidden from him, and the retreat still answers — the top point is farther from him than the self.
        /// 🔴 Red-proof: with <c>KeepAllWhenFilteredOut</c> off it answered nothing and the squad held in the open being shot.
        /// </summary>
        [Fact]
        public void CE2164_FindSafeRetreatPoint_OnOpenGround_StillAnswers_AwayFromTheThreat()
        {
            var town = TestTown();
            _repo.SetSingletonManaged(town);
            var sensor = new EqsSensor { SearchRadius = 60f, ContextSlot0 = At(146, 65), ContextSlot1 = At(202, 156) };
            var template = FindSafeRetreatPoint.Build(new EqsTemplateBuilder());

            var buf = new EqsResult[template.MaxCandidates];
            int n = template.Generator.Generate(Entity.Null, ref sensor, _repo, buf);
            Assert.True(n > 0);
            Assert.All(buf.Take(n), r => Assert.True(SeenBy(town, new Vector3(202, 156, 0), r, 1.1f), "the premise: open ground"));

            var top = Run(template, sensor);

            Assert.NotEmpty(top);
            float selfDist = Vector2.Distance(new(146, 65), new(202, 156));
            Assert.True(Vector2.Distance(new(top[0].PositionX, top[0].PositionY), new(202, 156)) > selfDist + 20f,
                $"the retreat goes away from the threat: ({top[0].PositionX},{top[0].PositionY})");
        }

        /// <summary>⭐ <c>CE-2164</c> (R-3004) — the grid covers the radius it is asked for: a 60 m search in a 128-point span reaches
        /// beyond 50 m (it stopped at ~28 m: 5 m rings, nearest first, rings 1–5 filled the span).</summary>
        [Fact]
        public void CE2164_GridGenerator_FitsTheWholeRadiusIntoItsSpan()
        {
            _repo.SetSingletonManaged(new TerrainWorld { BoundsMin = new Vector2(-200, -200), BoundsMax = new Vector2(200, 200) });
            var sensor = new EqsSensor { SearchRadius = 60f, ContextSlot0 = At(0, 0) };
            var buf = new EqsResult[128];

            int n = new GridGenerator { Spacing = 5f }.Generate(Entity.Null, ref sensor, _repo, buf);

            Assert.True(n > 90, $"{n} points");
            float far = buf.Take(n).Max(r => new Vector2(r.PositionX, r.PositionY).Length());
            Assert.InRange(far, 50f, 60f);
            // and a span that fits the 5 m grid keeps the 5 m grid
            sensor.SearchRadius = 10f;
            n = new GridGenerator { Spacing = 5f }.Generate(Entity.Null, ref sensor, _repo, buf);
            Assert.Contains(buf.Take(n), r => MathF.Abs(r.PositionX - 5f) < 0.01f && MathF.Abs(r.PositionY) < 0.01f);
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
        [InlineData(typeof(FindWindowFiringPosition))]
        [InlineData(typeof(FindRoofPosts))]
        public void StarterTemplate_BlueprintIdIsTheHashOfItsAssetId(Type template)
        {
            string assetId = (string)template.GetField("AssetId")!.GetValue(null)!;
            uint id = (uint)template.GetField("BlueprintId")!.GetValue(null)!;
            Assert.Equal(EqsTemplateRegistry.BlueprintIdOf(new Guid(assetId)), id);
        }
    }
}
