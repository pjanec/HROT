using System;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Combat.Contracts; // DEBT-031: HitEvent moved from Fdp.Core
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Combat.Systems;
using Fdp.Toolkit.Physics;
using Fdp.Toolkit.Physics.Components;
using Xunit;

namespace Fdp.Toolkit.Combat.Tests
{
    /// <summary>
    /// Unit tests for <see cref="BallisticsSystem"/> (BCS-P5-T4, second half).
    /// Tests isolate the system by seeding <see cref="BallisticProjectile"/> components
    /// directly, running the system, and asserting post-run state.
    /// BallisticsSystem now publishes <see cref="RaycastRequestEvent"/> via the cmd buffer
    /// instead of writing to <see cref="RaycastBatchData.Requests"/> directly.
    /// </summary>
    public class BallisticsSystemTests : IDisposable
    {
        private readonly EntityRepository _world;
        private readonly BallisticsSystem _sys;

        public BallisticsSystemTests()
        {
            _world = new EntityRepository();
            _world.RegisterComponent<SimTransform>();
            _world.RegisterComponent<SimVelocity>();
            _world.RegisterComponent<BallisticProjectile>();
            _world.RegisterComponent<PhysicsCollider>();
            _world.RegisterEvent<HitEvent>();
            _world.RegisterEvent<RaycastRequestEvent>();

            // Initialise GlobalTime singleton so CurrentTick reads are valid.
            _world.SetSingleton(new GlobalTime { FrameNumber = 0, TimeScale = 1f });

            _sys = new BallisticsSystem();
        }

        public void Dispose()
        {
            _world.Dispose();
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>Sets the world's simulated tick via the GlobalTime singleton.</summary>
        private void SetCurrentTick(uint tick)
        {
            _world.SetSingleton(new GlobalTime { FrameNumber = tick, TimeScale = 1f });
        }

        /// <summary>
        /// Spawns a bullet entity at the given position with the specified spawn tick and shooter.
        /// </summary>
        private Entity SpawnBullet(Vector3 position, uint spawnTick, Entity shooter = default,
                                   Vector3 previousPosition = default)
        {
            var bullet = _world.CreateEntity();
            _world.AddComponent(bullet, new SimTransform { Position = position, Rotation = Quaternion.Identity });
            _world.AddComponent(bullet, new SimVelocity  { Linear = new Vector3(100f, 0f, 0f), Angular = Vector3.Zero });
            _world.AddComponent(bullet, new BallisticProjectile
            {
                Shooter          = shooter,
                PreviousPosition = previousPosition,
                Damage           = CombatConstants.DefaultBulletDamage,
                SpawnTick        = spawnTick,
            });
            return bullet;
        }

        /// <summary>
        /// Runs the system, flushes the cmd buffer, and swaps buffers so that the
        /// published <see cref="RaycastRequestEvent"/>s become readable.
        /// Returns a snapshot array of the published events.
        /// </summary>
        private RaycastRequestEvent[] RunAndReadEvents()
        {
            ISimulationView view = _world;
            _sys.Execute(view, 0.016f);
            ((EntityCommandBuffer)view.GetCommandBuffer()).Playback(_world);
            _world.Bus.SwapBuffers();
            var span = _world.Bus.Read<RaycastRequestEvent>();
            var arr  = new RaycastRequestEvent[span.Length];
            span.CopyTo(arr);
            return arr;
        }

        /// <summary>
        /// ⭐ M4 (DESIGN_Missiles_And_Ammo §3) — a GUIDED round lives its flight (<see cref="GuidedRound.LifetimeTicks"/>, from
        /// <c>MissileDto.MaxFlightSeconds</c>), not a bullet's 2 s: at tick 121 a bullet is gone and the missile still flies; past its own
        /// lifetime it goes too. ⛔ A 2 km TOW vanished mid-flight.
        /// </summary>
        [Fact]
        public void M4_AGuidedRound_LivesItsFlight_NotTwoSeconds()
        {
            _world.RegisterComponent<GuidedRound>();
            SetCurrentTick(0);
            var bullet  = SpawnBullet(new Vector3(5f, 0f, 0f), spawnTick: 0);
            var missile = SpawnBullet(new Vector3(5f, 9f, 0f), spawnTick: 0);
            _world.AddComponent(missile, new GuidedRound { LifetimeTicks = 20 * 60 });

            SetCurrentTick(121);
            RunAndReadEvents();
            Assert.False(_world.IsAlive(bullet));
            Assert.True(_world.IsAlive(missile));

            SetCurrentTick(20 * 60 + 1);
            RunAndReadEvents();
            Assert.False(_world.IsAlive(missile));
        }

        // ── Test 1 ────────────────────────────────────────────────────────────

        /// <summary>
        /// Two live bullet entities must each publish one <see cref="RaycastRequestEvent"/>.
        /// </summary>
        [Fact]
        public void Ballistics_SubmitsRaycastRequest_ForEachLiveBullet()
        {
            SetCurrentTick(0);
            SpawnBullet(new Vector3(5f,  0f, 0f), spawnTick: 0);
            SpawnBullet(new Vector3(10f, 0f, 0f), spawnTick: 0);

            var events = RunAndReadEvents();
            Assert.Equal(2, events.Length);
        }

        // ── Test 2 ────────────────────────────────────────────────────────────

        /// <summary>
        /// After the system runs, <see cref="BallisticProjectile.PreviousPosition"/> must
        /// equal the bullet's <see cref="SimTransform.Position"/> at the time of the run.
        /// </summary>
        [Fact]
        public void Ballistics_UpdatesPreviousPosition_AfterRequest()
        {
            SetCurrentTick(0);
            var currentPos = new Vector3(5f, 0f, 0f);
            var bullet = SpawnBullet(currentPos, spawnTick: 0, previousPosition: Vector3.Zero);

            _sys.Execute(_world, 0.016f);

            var proj = _world.GetComponent<BallisticProjectile>(bullet);
            Assert.Equal(currentPos, proj.PreviousPosition);
        }

        // ── Test 3 ────────────────────────────────────────────────────────────

        /// <summary>
        /// A bullet whose <c>CurrentTick - SpawnTick >= BulletLifetimeTicks</c> must be
        /// destroyed (<see cref="EntityRepository.IsAlive"/> returns false).
        /// </summary>
        [Fact]
        public void Ballistics_DestroysEntity_WhenLifetimeExpired()
        {
            // SpawnTick=0, CurrentTick=121 -> age = 121 >= BulletLifetimeTicks(120) -> destroy.
            SetCurrentTick(121);
            var bullet = SpawnBullet(new Vector3(1f, 0f, 0f), spawnTick: 0);

            _sys.Execute(_world, 0.016f);

            Assert.False(_world.IsAlive(bullet), "Bullet entity should have been destroyed after lifetime expired.");
        }

        /// <summary>⭐ Indirect fire IF-1 (I5) — an ARC round lives its own flight + 10 s: a 90 s high-angle round is still flying at 70 s (the
        /// flat 60 s killed it in the air) and is gone after its lifetime. ✅ Red-proof: without the ArcLifetimeTicks read ⇒ gone at 60 s ⇒ red.</summary>
        [Fact]
        public void IF1_AnArcRound_LivesItsTimeOfFlight_NotAFlat60s()
        {
            uint life = FireProcessingSystem.ArcLifetimeTicks(Vector3.Zero, new Vector3(15000f, 0f, 0f),
                new Vector3(0f, 0f, 441.45f));   // straight up and down: ~90 s
            Assert.InRange(life / GuidedRound.TicksPerSecond, 99f, 101f);
            var round = SpawnBullet(new Vector3(1f, 0f, 500f), spawnTick: 0);
            ref var proj = ref _world.GetComponentRW<BallisticProjectile>(round);
            proj.Warhead = WarheadRound.Arc;
            proj.ArcLifetimeTicks = life;
            SetCurrentTick(70 * 60);
            _sys.Execute(_world, 0.016f);
            Assert.True(_world.IsAlive(round), "a 90 s shell is still in the air at 70 s");
            SetCurrentTick(life + 1);
            _sys.Execute(_world, 0.016f);
            Assert.False(_world.IsAlive(round));
            Assert.Equal(CombatConstants.ArcRoundLifetimeTicks, FireProcessingSystem.ArcLifetimeTicks(Vector3.Zero, new Vector3(20f, 0f, 0f), new Vector3(10f, 0f, 10f)));   // never shorter
        }

        // ── Test 4 ────────────────────────────────────────────────────────────

        /// <summary>
        /// An expired bullet must NOT publish a <see cref="RaycastRequestEvent"/> — it is
        /// destroyed before the event is published.
        /// </summary>
        [Fact]
        public void Ballistics_DoesNotSubmitRaycast_WhenLifetimeExpired()
        {
            SetCurrentTick(121);
            SpawnBullet(new Vector3(1f, 0f, 0f), spawnTick: 0);

            var events = RunAndReadEvents();
            Assert.Equal(0, events.Length);
        }

        // ── Test 5 ────────────────────────────────────────────────────────────

        /// <summary>
        /// The <see cref="RaycastRequestEvent.IgnoreEntity"/> field must be set to
        /// <see cref="BallisticProjectile.Shooter"/> to prevent self-hits.
        /// </summary>
        [Fact]
        public void Ballistics_IgnoresShooter_InRaycastRequest()
        {
            SetCurrentTick(0);

            var shooter = _world.CreateEntity();
            _world.AddComponent(shooter, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });

            SpawnBullet(new Vector3(5f, 0f, 0f), spawnTick: 0, shooter: shooter);

            var events = RunAndReadEvents();
            Assert.Equal(1, events.Length);
            Assert.Equal(shooter, events[0].IgnoreEntity);
        }

        // ── CE-3064 (R-206) — near misses ─────────────────────────────────────

        /// <summary>
        /// ⭐ <c>CE-3064</c> — a round passing within <see cref="CombatConstants.NearMissRadius"/> of a unit of ANOTHER side reports
        /// one <see cref="Fdp.Toolkit.Combat.Events.NearMissEvent"/> per pass; the shooter's own side, the shooter and units
        /// farther than the radius report none.
        /// </summary>
        [Fact]
        public void ARoundPassingClose_ReportsOneNearMiss_ForAnotherSideOnly_CE3064()
        {
            _world.RegisterComponent<EntityInfo>();
            _world.RegisterEvent<Fdp.Toolkit.Combat.Events.NearMissEvent>();
            var grid = global::CarKinem.Spatial.SpatialHashGrid.Create(100, 100, 5f, 100, Fdp.Core.Collections.Allocator.Persistent);
            try
            {
                grid.Clear();
                _world.SetSingleton(new global::CarKinem.Spatial.SpatialGridData { Grid = grid });
                Entity Unit(Vector2 at, ForceId force)
                {
                    var e = _world.CreateEntity();
                    _world.AddComponent(e, new SimTransform { Position = new Vector3(at, 0f), Rotation = Quaternion.Identity });
                    _world.AddComponent(e, new EntityInfo { ForceId = force });
                    grid.Add(e, at);
                    return e;
                }
                var shooter = Unit(new Vector2(0f, 0f), ForceId.Friend);
                var enemyNear = Unit(new Vector2(30f, 2f), ForceId.Hostile);    // 2 m off the line
                var enemyFar  = Unit(new Vector2(30f, 8f), ForceId.Hostile);    // 8 m off
                var friendNear = Unit(new Vector2(25f, 1f), ForceId.Friend);    // own side

                var bullet = SpawnBullet(new Vector3(40f, 0f, 0f), 0, shooter, previousPosition: new Vector3(10f, 0f, 0f));
                _world.GetComponentRW<BallisticProjectile>(bullet).Muzzle = new Vector3(0.5f, 0f, 1.4f);   // ⭐ CE-3262 — where it left the barrel
                _sys.Execute(_world, 0.016f);
                ((EntityCommandBuffer)((ISimulationView)_world).GetCommandBuffer()).Playback(_world);
                _world.Bus.SwapBuffers();
                var events = _world.Bus.Read<Fdp.Toolkit.Combat.Events.NearMissEvent>();
                Assert.Equal(1, events.Length);
                Assert.Equal(enemyNear, events[0].Unit);
                Assert.Equal(30f, events[0].X, 2);
                // ⭐ CE-3262 (DA-14) — the round's ORIGIN rides it (the muzzle), not the shooter. ✅ Red-proof: origin not set ⇒ red.
                Assert.True(events[0].HasOrigin);
                Assert.Equal((0.5f, 0f, 1.4f), (events[0].OriginX, events[0].OriginY, events[0].OriginZ));

                // The same round still near the same unit next tick ⇒ not reported again.
                ref var tf = ref _world.GetComponentRW<SimTransform>(bullet);
                tf.Position = new Vector3(42f, 0f, 0f);
                ref var p = ref _world.GetComponentRW<BallisticProjectile>(bullet);
                p.PreviousPosition = new Vector3(29f, 0f, 0f);
                _sys.Execute(_world, 0.016f);
                ((EntityCommandBuffer)((ISimulationView)_world).GetCommandBuffer()).Playback(_world);
                _world.Bus.SwapBuffers();
                Assert.Equal(0, _world.Bus.Read<Fdp.Toolkit.Combat.Events.NearMissEvent>().Length);
                _ = enemyFar; _ = friendNear;
            }
            finally { grid.Dispose(); }
        }

        // ── ⭐ Buildings §3d P2 (R-217) — rounds through the terrain ─────────────────────────────────────────────

        /// <summary>A concrete wall at y = 10 (0.2 m), two wooden fences at y = 20 and y = 30 (0.05 m — 3 mm RHA each), a chain-link
        /// fence at y = 40; everything 2 m high.</summary>
        private void SeedTerrain()
            => _world.SetSingletonManaged(Fdp.Toolkit.Terrain.TerrainWorldParser.Parse("""
            { "type": "FeatureCollection", "features": [
              { "type": "Feature", "properties": { "kind": "wall", "height": 2, "thickness": 0.2, "material": "concrete" }, "geometry": { "type": "LineString", "coordinates": [[0,10],[10,10]] } },
              { "type": "Feature", "properties": { "kind": "fence", "height": 2 }, "geometry": { "type": "LineString", "coordinates": [[20,20],[30,20]] } },
              { "type": "Feature", "properties": { "kind": "fence", "height": 2 }, "geometry": { "type": "LineString", "coordinates": [[20,30],[30,30]] } },
              { "type": "Feature", "properties": { "kind": "fence", "height": 2, "material": "fence-chainlink" }, "geometry": { "type": "LineString", "coordinates": [[40,40],[50,40]] } } ] }
            """));

        private Entity SpawnRound(Vector3 from, Vector3 to, float damage, float penetration)
        {
            var bullet = SpawnBullet(to, 0, previousPosition: from);
            ref var p = ref _world.GetComponentRW<BallisticProjectile>(bullet);
            p.Damage = damage;
            p.Penetration = penetration;
            return bullet;
        }

        private Fdp.Toolkit.Combat.Contracts.RoundImpactNotification[] Impacts()
            => _world.Bus.Read<Fdp.Toolkit.Combat.Contracts.RoundImpactNotification>().ToArray();

        /// <summary>
        /// ⭐⭐ CE-1128 (docs/DESIGN_Visual_Effects.md §10 IM-2, IM-4) — a round MARKS every surface it meets: a fence it passes on BOTH
        /// sides (the entry facing the shooter, the exit facing away — R-217 already lets it go on), then the fence that stops it, each with
        /// the face's normal. 🔒 User, 2026-10-16: <i>"Every surface pierced should get a decal and particle effect on the entry side (maybe
        /// also in the exit side…)"</i>.
        /// </summary>
        [Fact]
        public void CE1128_AFenceIsMarkedOnBothSides_TheFenceThatStopsTheRound_OnItsNearFace_EachWithItsNormal()
        {
            SeedTerrain();
            _world.RegisterEvent<Fdp.Toolkit.Combat.Contracts.RoundImpactNotification>();
            var round = SpawnRound(new Vector3(25, 15, 1.6f), new Vector3(25, 25, 1.6f), damage: 25f, penetration: 5f);

            RunAndReadEvents();
            var through = Impacts();
            Assert.Equal(2, through.Length);
            Assert.Equal((byte)ImpactKind.Entry, through[0].Kind);
            Assert.InRange(through[0].Y, 19.965f, 19.985f);
            Assert.Equal(new Vector3(0, -1, 0), new Vector3(through[0].NormalX, through[0].NormalY, through[0].NormalZ));   // faces the shooter
            Assert.Equal((byte)ImpactKind.Exit, through[1].Kind);
            Assert.InRange(through[1].Y, 20.015f, 20.035f);
            Assert.Equal(new Vector3(0, 1, 0), new Vector3(through[1].NormalX, through[1].NormalY, through[1].NormalZ));    // faces away
            Assert.True(through[1].TravelY > 0f);

            ref var tf = ref _world.GetComponentRW<SimTransform>(round);
            tf.Position = new Vector3(25, 35, 1.6f);
            SetCurrentTick(1);
            RunAndReadEvents();
            var stop = Assert.Single(Impacts());
            Assert.Equal((byte)ImpactKind.Stop, stop.Kind);
            Assert.InRange(stop.Y, 29.965f, 29.985f);
            Assert.Equal(-1f, stop.NormalY, 3);
        }

        /// <summary>
        /// ⭐ CE-1128 (IM-2) — a bullet aimed at the GROUND stops there and marks it, facing up. ⛔ The fire query does not count flat ground,
        /// so it flew on below it until it expired: nothing could be marked. A WARHEAD round's stop is NOT marked (its burst draws it — IM-5).
        /// </summary>
        [Fact]
        public void CE1128_ABulletAimedAtTheGround_StopsThereAndMarksIt_FacingUp_AWarheadRoundsStopIsLeftToItsBurst()
        {
            SeedTerrain();
            _world.RegisterEvent<Fdp.Toolkit.Combat.Contracts.RoundImpactNotification>();
            var round = SpawnRound(new Vector3(60, 60, 1.6f), new Vector3(60, 70, -1.6f), damage: 25f, penetration: 5f);

            var req = Assert.Single(RunAndReadEvents());
            Assert.Equal(0f, req.End.Z, 3);                                         // the raycast ends at the ground
            Assert.NotEqual(0u, _world.GetComponent<BallisticProjectile>(round).StoppedTick);
            var mark = Assert.Single(Impacts());
            Assert.Equal((byte)ImpactKind.Stop, mark.Kind);
            Assert.InRange(mark.Y, 64.99f, 65.01f);
            Assert.Equal(1f, mark.NormalZ, 3);

            var shell = SpawnRound(new Vector3(5, 0, 1.6f), new Vector3(5, 15, 1.6f), damage: 25f, penetration: 5f);
            _world.GetComponentRW<BallisticProjectile>(shell).Warhead = WarheadRound.Area;
            SetCurrentTick(1);
            RunAndReadEvents();
            Assert.DoesNotContain(Impacts(), i => i.Kind == (byte)ImpactKind.Stop && i.Y < 11f);   // stopped by the wall: its burst draws
        }

        [Fact]
        public void R217_AConcreteWall_EndsTheSegmentAtTheWall_FreezesTheRound_AndRemovesItAfterTheGrace()
        {
            SeedTerrain();
            var round = SpawnRound(new Vector3(5, 0, 1.6f), new Vector3(5, 15, 1.6f), damage: 25f, penetration: 5f);   // a rifle round

            var req = Assert.Single(RunAndReadEvents());
            Assert.Equal(9.9f, req.End.Y, 3);                      // ⭐ the raycast stops at the wall's face — no unit beyond it is struck
            var p = _world.GetComponent<BallisticProjectile>(round);
            Assert.NotEqual(0u, p.StoppedTick);
            Assert.Equal(9.9f, _world.GetComponent<SimTransform>(round).Position.Y, 3);    // frozen at the wall
            Assert.Equal(Vector3.Zero, _world.GetComponent<SimVelocity>(round).Linear);
            Assert.Equal(25f, p.Damage);                           // the muzzle values are never rewritten

            // ⭐ kept for the grace (its segments' raycasts take three ticks to resolve) — no further raycasts — then removed
            for (uint t = 1; t < CombatConstants.StoppedRoundGraceTicks; t++)
            {
                SetCurrentTick(t);
                Assert.Empty(RunAndReadEvents());
                Assert.True(_world.IsAlive(round), $"removed at tick {t}, inside the grace");
            }
            SetCurrentTick(CombatConstants.StoppedRoundGraceTicks + 1);
            RunAndReadEvents();
            Assert.False(_world.IsAlive(round));
        }

        [Fact]
        public void R217_AWoodenFence_PassesARifleRound_TheFrontIsReduced_TheMuzzleValuesAreNot_TheSecondFenceStopsIt()
        {
            SeedTerrain();
            var round = SpawnRound(new Vector3(25, 15, 1.6f), new Vector3(25, 25, 1.6f), damage: 25f, penetration: 5f);

            var req = Assert.Single(RunAndReadEvents());
            Assert.Equal(25f, req.End.Y, 3);                       // passes: 5 mm vs 3 mm ⇒ chance 1
            var p = _world.GetComponent<BallisticProjectile>(round);
            Assert.Equal(25f, p.Damage);                           // ⚠ the muzzle values: a hit carries from the muzzle, whenever it resolves
            Assert.Equal(5f, p.Penetration);
            Assert.Equal(new Vector3(25, 15, 1.6f), p.Muzzle);
            Assert.Equal(25f, p.FrontDamage, 3);
            Assert.Equal(2f, p.FrontPenetration, 3);               // 5 − 3
            Assert.Equal(0u, p.StoppedTick);

            // next segment through the second fence: 2 mm vs 3 mm ⇒ pen/armour 0.67 < 0.8 ⇒ stopped
            ref var tf = ref _world.GetComponentRW<SimTransform>(round);
            tf.Position = new Vector3(25, 35, 1.6f);
            SetCurrentTick(1);
            var req2 = Assert.Single(RunAndReadEvents());
            Assert.Equal(29.975f, req2.End.Y, 3);
            Assert.NotEqual(0u, _world.GetComponent<BallisticProjectile>(round).StoppedTick);
        }

        [Fact]
        public void R217_AnUnknownRound_ThroughChainLink_KeepsFullDamage_AndStaysUnknown_OverTheWallNothingHappens()
        {
            SeedTerrain();
            var through = SpawnRound(new Vector3(45, 35, 1.6f), new Vector3(45, 45, 1.6f), damage: 25f, penetration: 0f);
            var over    = SpawnRound(new Vector3(5, 0, 2.5f), new Vector3(5, 15, 2.5f), damage: 25f, penetration: 5f);
            var reqs = RunAndReadEvents();
            Assert.Equal(2, reqs.Length);
            var a = _world.GetComponent<BallisticProjectile>(through);
            Assert.Equal(0u, a.StoppedTick);
            Assert.Equal(25f, a.FrontDamage, 3);                   // 5 mm (the unknown-round fallback) vs 0.025 mm ⇒ chance 1
            Assert.Equal(0f, a.FrontPenetration);                  // still unknown ⇒ still ignores armour
            var b = _world.GetComponent<BallisticProjectile>(over);
            Assert.Equal(0u, b.StoppedTick);
            Assert.Equal(5f, b.FrontPenetration);
        }

        [Fact]
        public void R217_Cross_IsTheArmourRamp_DamageTimesChance_ASpentKnownRoundKeepsATracePositivePenetration()
        {
            float dmg = 100f, pen = 330f;                          // vs 300: ratio 1.1 ⇒ chance 0.75
            Assert.True(TerrainPenetration.Cross(300f, ref dmg, ref pen));
            Assert.Equal(75f, dmg, 3);
            Assert.Equal(30f, pen, 3);

            dmg = 100f; pen = 270f;                                // ratio 0.9 ⇒ chance 0.25, pen − 300 < 0 ⇒ spent, NOT unknown
            Assert.True(TerrainPenetration.Cross(300f, ref dmg, ref pen));
            Assert.Equal(25f, dmg, 3);
            Assert.Equal(TerrainPenetration.SpentPenetrationMm, pen);

            dmg = 100f; pen = 230f;                                // ratio 0.77 ⇒ stopped, values untouched
            Assert.False(TerrainPenetration.Cross(300f, ref dmg, ref pen));
            Assert.Equal(100f, dmg);
        }

        [Fact]
        public void R217_NoTerrainResident_BehavesAsBefore()
        {
            var round = SpawnRound(new Vector3(5, 0, 1.6f), new Vector3(5, 15, 1.6f), damage: 25f, penetration: 5f);
            var req = Assert.Single(RunAndReadEvents());
            Assert.Equal(15f, req.End.Y);
            Assert.Equal(0u, _world.GetComponent<BallisticProjectile>(round).StoppedTick);
            Assert.Equal(0, _world.GetComponent<BallisticProjectile>(round).TerrainFlags);   // untouched without terrain
        }
    }
}