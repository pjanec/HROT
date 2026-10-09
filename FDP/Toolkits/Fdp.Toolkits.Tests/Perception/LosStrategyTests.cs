using System.Linq;
using System;
using System.Numerics;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Perception.Events;
using Fdp.Interfaces;
using Fdp.Toolkit.Perception.LineOfSight;
using Fdp.Toolkit.Perception.Systems;
using Fdp.Toolkit.Perception.Translators;
using Fdp.Toolkit.Terrain;
using Fdp.Toolkit.Tkb.Domain;
using Fdp.Toolkit.Physics.Components;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Xunit;

namespace Fdp.Toolkit.Perception.Tests
{
    /// <summary>
    /// Unit tests for the line-of-sight strategies (<see cref="ILosStrategy"/>) — the test the visual sensor's
    /// <c>StrategySightTest</c> runs for every candidate.
    /// <para>⭐ <c>CE-3052</c> — re-homed from the retired <c>LosRequestBatchingSystem</c>: the occlusion cases now ask
    /// the strategy directly (<see cref="PlanarCircleLosStrategy"/> is the sweep that system did inline). Its mock mode
    /// and its dead-entity skip went with it — the sight test checks liveness itself.</para>
    /// </summary>
    public class LosStrategyTests
    {
        private static EntityRepository CreateWorldWithPhysics()
        {
            var world = PerceptionTestWorldFactory.Create();
            world.RegisterComponent<PhysicsCollider>();
            return world;
        }

        private static Func<ISimulationView, Entity, float> PhysicsRadiusReader() =>
            (view, e) => view.HasComponent<PhysicsCollider>(e)
                ? view.GetComponentRO<PhysicsCollider>(e).Radius : 0f;

        private static bool Sees(EntityRepository world, ILosStrategy strategy, Entity obs, Entity tgt)
        {
            ISimulationView view = world;
            strategy.BeginBatch(view);
            return strategy.IsVisible(view, obs, tgt);
        }

        private static (EntityRepository World, Entity Obs, Entity Tgt) ObserverAndTarget100mEast()
        {
            var world = CreateWorldWithPhysics();
            var obs = world.CreateEntity();
            world.AddComponent(obs, new SimTransform { Position = new Vector3(0f, 0f, 0f) });
            world.AddComponent(obs, new EntityInfo { ForceId = ForceId.Friend });
            var tgt = world.CreateEntity();
            world.AddComponent(tgt, new SimTransform { Position = new Vector3(100f, 0f, 0f) });
            world.AddComponent(tgt, new PhysicsCollider { Radius = 2f });
            world.AddComponent(tgt, new EntityInfo { ForceId = ForceId.Hostile });
            return (world, obs, tgt);
        }

        [Fact]
        public void PlanarSweep_SeesTheTarget_WhenTheLineIsClear()
        {
            var (world, obs, tgt) = ObserverAndTarget100mEast();
            Assert.True(Sees(world, new PlanarCircleLosStrategy(PhysicsRadiusReader()), obs, tgt));
        }

        [Fact]
        public void PlanarSweep_DoesNotSee_ThroughAColliderOnTheLine()
        {
            var (world, obs, tgt) = ObserverAndTarget100mEast();
            var wall = world.CreateEntity();   // directly on the observer→target segment, mid-range
            world.AddComponent(wall, new SimTransform { Position = new Vector3(50f, 0f, 0f) });
            world.AddComponent(wall, new PhysicsCollider { Radius = 10f });
            Assert.False(Sees(world, new PlanarCircleLosStrategy(PhysicsRadiusReader()), obs, tgt));
        }

        // ── 3-D sight through the terrain world (docs/DESIGN_Terrain_World.md §4.3, R-181/R-182) ─────────────

        private const string TwelveMetreBuildingAt50 = """
            {"type":"FeatureCollection","features":[{"type":"Feature","properties":{"kind":"building","height":12},
             "geometry":{"type":"Polygon","coordinates":[[[45,-5],[55,-5],[55,5],[45,5],[45,-5]]]}}]}
            """;

        private const string HalfMetreWallAt10 = """
            {"type":"FeatureCollection","features":[{"type":"Feature","properties":{"kind":"wall","height":0.5,"thickness":0.3},
             "geometry":{"type":"LineString","coordinates":[[10,-5],[10,5]]}}]}
            """;

        private static (EntityRepository World, Entity Obs, Entity Tgt) TwoSoldiers(float targetX)
        {
            var world = CreateWorldWithPhysics();
            var obs = world.CreateEntity();
            world.AddComponent(obs, new SimTransform { Position = new Vector3(0f, 0f, 0f) });
            var tgt = world.CreateEntity();
            world.AddComponent(tgt, new SimTransform { Position = new Vector3(targetX, 0f, 0f) });
            return (world, obs, tgt);
        }

        private static int VisibleCount(EntityRepository world, ILosStrategy strategy, Entity obs, Entity tgt)
            => Sees(world, strategy, obs, tgt) ? 1 : 0;

        private static TerrainWorldLosStrategy Strategy(TerrainWorld? terrain, StanceId observerStance = StanceId.Standing,
            StanceId targetStance = StanceId.Standing, Entity observer = default)
            => new(() => terrain, PhysicsRadiusReader(),
                (v, e) => v.HasComponent<PhysicsCollider>(e) ? v.GetComponentRO<PhysicsCollider>(e).Height : 0f,
                (v, e) => e.Index == observer.Index ? observerStance : targetStance);

        [Fact]
        public void TerrainLos_ABuildingBetween_BlocksSight_AndOpenGroundDoesNot()
        {
            var (world, obs, tgt) = TwoSoldiers(100f);
            Assert.Equal(0, VisibleCount(world, Strategy(TerrainWorldParser.Parse(TwelveMetreBuildingAt50)), obs, tgt));

            var (open, o2, t2) = TwoSoldiers(100f);
            Assert.Equal(1, VisibleCount(open, Strategy(TerrainWorldParser.Parse("""{"type":"FeatureCollection","features":[]}""")), o2, t2));
        }

        /// <summary>🔒 R-182 — <i>"infantry can lay on ground or squat, sensor height must follow posture"</i>:
        /// over a 0.5 m wall a STANDING observer sees a prone target, a PRONE observer does not.</summary>
        [Fact]
        public void TerrainLos_EyeHeightFollowsPosture_StandingSeesOverALowWall_ProneDoesNot()
        {
            var wall = TerrainWorldParser.Parse(HalfMetreWallAt10);

            var (w1, o1, t1) = TwoSoldiers(20f);
            Assert.Equal(1, VisibleCount(w1, Strategy(wall, StanceId.Standing, StanceId.Prone, o1), o1, t1));

            var (w2, o2, t2) = TwoSoldiers(20f);
            Assert.Equal(0, VisibleCount(w2, Strategy(wall, StanceId.Prone, StanceId.Prone, o2), o2, t2));
        }

        [Fact]
        public void TerrainLos_ASensorMount_OverridesTheDefaultEyeHeight()
        {
            var wall = TerrainWorldParser.Parse(HalfMetreWallAt10);
            var (world, obs, tgt) = TwoSoldiers(20f);
            // A prone sensor mounted HIGH (a periscope) sees over the wall the default prone eye cannot.
            world.AddComponent(obs, new SensorMount { Standing = 1.7f, Crouched = 1.1f, Prone = 1.5f });
            Assert.Equal(1, VisibleCount(world, Strategy(wall, StanceId.Prone, StanceId.Prone, obs), obs, tgt));
        }

        /// <summary>A collider blocks only within its HEIGHT; height 0 (unknown) blocks at every height, as the
        /// 2-D sweep always did.</summary>
        [Fact]
        public void TerrainLos_ACollider_BlocksWithinItsHeight_AndUnknownHeightBlocksAlways()
        {
            foreach (var (height, visible) in new[] { (1.0f, 1), (2.5f, 0), (0f, 0) })
            {
                var (world, obs, tgt) = TwoSoldiers(20f);
                var car = world.CreateEntity();
                world.AddComponent(car, new SimTransform { Position = new Vector3(10f, 0f, 0f) });
                world.AddComponent(car, new PhysicsCollider { Radius = 2f, Height = height });
                Assert.Equal(visible, VisibleCount(world, Strategy(null), obs, tgt));
            }
        }

        /// <summary>⭐ Tuning T-4 (<c>GET /perception/los</c>) — <c>Explain</c> makes the SAME decision as <c>IsVisible</c>, and says why.</summary>
        [Fact]
        public void T4_Explain_AgreesWithIsVisible_AndNamesTheReason()
        {
            var wall = TerrainWorldParser.Parse(HalfMetreWallAt10);
            foreach (var (obsStance, tgtStance, expect, reason) in new[]
            {
                (StanceId.Standing, StanceId.Prone, true, "clear"),
                (StanceId.Prone, StanceId.Prone, false, "blocked by terrain"),
            })
            {
                var (world, obs, tgt) = TwoSoldiers(20f);
                var strategy = Strategy(wall, obsStance, tgtStance, obs);
                strategy.BeginBatch(world);
                var x = strategy.Explain(world, obs, tgt);
                Assert.Equal(expect, x.Visible);
                Assert.Equal(strategy.IsVisible(world, obs, tgt), x.Visible);
                Assert.Contains(expect ? "seen:" : "not seen", x.Verdict);
                Assert.Contains(x.Points, p => p.Verdict.Contains(reason));
                Assert.Equal(obsStance, x.ObserverStance);
                Assert.Equal(strategy.EyeHeight(world, obs), x.Eye.Z, 3);
            }

            var (w, o, t) = TwoSoldiers(20f);
            var car = w.CreateEntity();
            w.AddComponent(car, new SimTransform { Position = new Vector3(10f, 0f, 0f) });
            w.AddComponent(car, new PhysicsCollider { Radius = 2f, Height = 2.5f });
            var s = Strategy(null);
            s.BeginBatch(w);
            var blocked = s.Explain(w, o, t);
            Assert.False(blocked.Visible);
            Assert.Equal(car, blocked.BlockingEntity);
        }

        /// <summary>⭐ Buildings Stage 4 (§3f, §3i) — the target is SEEN when ANY of its body points is: right behind a 0.5 m wall a
        /// standing target is seen and a prone one is not; a standing man's head shows over a 1.2 m wall (one mid-point would not).</summary>
        [Fact]
        public void Stage4_BodyPoints_StandingBehindALowWallIsSeen_ProneIsNot_AHeadShowsOverAChestHighWall()
        {
            var low = TerrainWorldParser.Parse(HalfMetreWallAt10);
            var (w1, o1, t1) = TwoSoldiers(11f);
            Assert.Equal(1, VisibleCount(w1, Strategy(low, StanceId.Standing, StanceId.Standing, o1), o1, t1));
            var (w2, o2, t2) = TwoSoldiers(11f);
            Assert.Equal(0, VisibleCount(w2, Strategy(low, StanceId.Standing, StanceId.Prone, o2), o2, t2));

            var chest = TerrainWorldParser.Parse(HalfMetreWallAt10.Replace("\"height\":0.5", "\"height\":1.2"));
            var (w3, o3, t3) = TwoSoldiers(11f);
            var s3 = Strategy(chest, StanceId.Standing, StanceId.Standing, o3);
            Assert.Equal(1, VisibleCount(w3, s3, o3, t3));
            var x = s3.Explain(w3, o3, t3);
            Assert.Equal(new[] { false, false, true }, x.Points.Select(p => p.Clear));   // only the head (the eye, ≈ 1.7 m — R-246) clears it
        }

        /// <summary>
        /// ⭐ <c>R-246</c> (peek-and-fire D16) — "if your eye sees out, your head shows": the top body point IS the eye, so sight is
        /// reciprocal. Two crouched men, one 1 m behind a 1.05 m wall, the other 11 m off: each sees the other (eye to eye at 1.1 m
        /// clears it). 🔴 Red-proof: the old crouched top (0.91 of the eye ≈ 1.0 m) — the man behind the wall sees out and is never seen.
        /// </summary>
        [Fact]
        public void R246_TheTopBodyPointIsTheEye_SoSightIsReciprocal()
        {
            foreach (var stance in new[] { StanceId.Standing, StanceId.Crouched, StanceId.Prone })
                Assert.Equal(1.0f, BodyProfile.Fractions(stance, hull: false)[^1]);

            var wall = TerrainWorldParser.Parse(HalfMetreWallAt10.Replace("\"height\":0.5", "\"height\":1.05"));
            var (w1, o1, t1) = TwoSoldiers(11f);
            Assert.Equal(1, VisibleCount(w1, Strategy(wall, StanceId.Crouched, StanceId.Crouched, o1), o1, t1));   // the far man sees the near one
            var (w2, o2, t2) = TwoSoldiers(11f);
            Assert.Equal(1, VisibleCount(w2, Strategy(wall, StanceId.Crouched, StanceId.Crouched, t2), t2, o2));   // …and the near one the far one
        }

        /// <summary>⭐ Stage 4 — the production composition reads the LOGICAL stance with no reader passed: the brain's StanceIntent.</summary>
        [Fact]
        public void Stage4_ForLiveWorld_ReadsTheLogicalStance_TheBrainsStanceIntent()
        {
            var low = TerrainWorldParser.Parse(HalfMetreWallAt10);
            var (world, obs, tgt) = TwoSoldiers(11f);
            world.RegisterManagedComponent<TerrainWorld>();
            world.SetSingletonManaged(low);
            world.RegisterComponent<Hrot.MuscleCharacter.Animation.Components.StanceIntent>();
            var live = TerrainWorldLosStrategy.ForLiveWorld(world);
            Assert.True(Sees(world, live, obs, tgt));                     // no intent ⇒ Standing
            world.AddComponent(tgt, new Hrot.MuscleCharacter.Animation.Components.StanceIntent { TargetStance = StanceId.Prone });
            Assert.False(Sees(world, live, obs, tgt));                    // ordered prone ⇒ hidden behind the wall, in that tick
        }

        /// <summary>
        /// ⭐ <c>CE-3116</c> — a SOLDIER's collider is not a hull. On the live world every soldier carries a 1.8 m collider (its capsule,
        /// <c>StrideRenderModelDefDto.ShapeHeight</c>), and the body profile used to scale by any collider height ⇒ a prone soldier was
        /// sampled at 0.45/0.9/1.5 m and SEEN over a 0.5 m wall. The live composition reads <c>PhysicsColliderReaders.HullHeight</c>:
        /// 0 for a pedestrian, so its posture decides; the collider still BLOCKS other lines up to its full height.
        /// </summary>
        [Fact]
        public void CE3116_ASoldiersCollider_IsNotAHull_ProneBehindALowWallStaysHidden()
        {
            var low = TerrainWorldParser.Parse(HalfMetreWallAt10);
            var (world, obs, tgt) = TwoSoldiers(11f);
            world.RegisterManagedComponent<TerrainWorld>();
            world.SetSingletonManaged(low);
            world.RegisterComponent<Hrot.MuscleCharacter.Animation.Components.StanceIntent>();
            world.RegisterComponent<global::CarKinem.Core.VehicleParams>();
            world.AddComponent(tgt, new PhysicsCollider { Radius = 0.3f, Height = 1.8f });
            world.AddComponent(tgt, new global::CarKinem.Core.VehicleParams { Class = global::CarKinem.Core.VehicleClass.Pedestrian });
            world.AddComponent(tgt, new Hrot.MuscleCharacter.Animation.Components.StanceIntent { TargetStance = StanceId.Prone });

            Assert.True(Sees(world, Strategy(low, StanceId.Standing, StanceId.Prone), obs, tgt));   // the defect: the collider height as a hull
            Assert.False(Sees(world, TerrainWorldLosStrategy.ForLiveWorld(world), obs, tgt));       // the live composition: by posture
            Assert.Equal(0f, PhysicsColliderReaders.HullHeight(world, tgt));
            Assert.Equal(1.8f, PhysicsColliderReaders.Height(world, tgt));                         // still a 1.8 m blocker
        }

        /// <summary>
        /// ⭐ <c>CE-1032</c> — the collider test shared with fragments (<see cref="ColliderOcclusion"/>) skips by INDEX: the observer's
        /// and the target's own 1.8 m colliders never block their own line, and the "no entity" sentinel
        /// (<see cref="ColliderOcclusion.Nobody"/>) skips nothing — not even entity index 0 (a terrain burst has no struck entity).
        /// </summary>
        [Fact]
        public void CE1032_OwnCollidersNeverBlockTheirOwnLine_AndNobodySkipsNothing()
        {
            var (world, obs, tgt) = TwoSoldiers(20f);
            world.AddComponent(obs, new PhysicsCollider { Radius = 0.3f, Height = 1.8f });
            world.AddComponent(tgt, new PhysicsCollider { Radius = 0.3f, Height = 1.8f });
            var s = Strategy(null);
            s.BeginBatch(world);
            Assert.True(s.IsVisible(world, obs, tgt));

            var occ = new ColliderOcclusion();
            occ.Build(world, PhysicsRadiusReader(), (v, e) => v.GetComponentRO<PhysicsCollider>(e).Height);
            var eye = new Vector3(0, 0, 1.7f);
            var aim = new Vector3(20, 0, 0.9f);
            Assert.Null(occ.Blocking(eye, aim, obs, tgt, out _));
            Assert.Equal(obs, occ.Blocking(eye, aim, ColliderOcclusion.Nobody, tgt, out _));   // index 0 is NOT skipped by Nobody
        }

        [Fact]
        public void PerceptionTkbTranslator_ProjectsPostureEyeHeights_IntoASensorMount()
        {
            var world = CreateWorldWithPhysics();
            var e = world.CreateEntity();
            var template = new TkbTemplate("Scout", 1);
            template.AddDescriptor(new SensorCapabilitiesDto { VisionRange = 300f, EyeHeightStanding = 1.8f, EyeHeightProne = 0.3f });
            new PerceptionTkbTranslator().Inject(world, e, template);

            var mount = world.GetComponentRO<SensorMount>(e);
            Assert.Equal(1.8f, mount.Standing);
            Assert.Equal(1.8f, mount.Crouched);   // unset ⇒ the standing height
            Assert.Equal(0.3f, mount.Prone);

            var bare = world.CreateEntity();
            var plain = new TkbTemplate("Plain", 2);
            plain.AddDescriptor(new SensorCapabilitiesDto { VisionRange = 300f });
            new PerceptionTkbTranslator().Inject(world, bare, plain);
            Assert.False(world.HasComponent<SensorMount>(bare));   // ⇒ the strategy's default mount
        }
    }
}
