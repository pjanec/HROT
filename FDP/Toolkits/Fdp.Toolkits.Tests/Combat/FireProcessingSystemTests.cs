using System.Linq;
using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Combat.Events;
using Fdp.Toolkit.Combat.Systems;
using Fdp.Toolkit.Physics.Components;
using Fdp.Toolkit.Replication.Components;
using Xunit;

namespace Fdp.Toolkit.Combat.Tests
{
    /// <summary>
    /// Unit tests for <see cref="FireProcessingSystem"/> after BS1-T007 / PACK-P003 refactor.
    ///
    /// The system now consumes <see cref="WeaponFireIntent"/> carrying local ECS
    /// <see cref="Entity"/> handles (PACK-P003) instead of resolving via NetworkEntityMap.
    /// After spawning the bullet it publishes a <see cref="WeaponFireNotification"/>.
    /// </summary>
    public class FireProcessingSystemTests : IDisposable
    {
        private readonly EntityRepository    _world;
        private readonly FireProcessingSystem _sys;

        public FireProcessingSystemTests()
        {
            _world = new EntityRepository();
            _world.RegisterComponent<SimTransform>();
            _world.RegisterComponent<SimVelocity>();
            _world.RegisterComponent<WeaponState>();
            _world.RegisterComponent<BallisticProjectile>();
            _world.RegisterComponent<PhysicsCollider>();
            _world.RegisterComponent<NetworkAuthority>();
            _world.RegisterEvent<WeaponFireIntent>();
            _world.RegisterEvent<WeaponFireNotification>();

            _sys = new FireProcessingSystem();
        }

        public void Dispose()
        {
            _world.Dispose();
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private Entity SpawnShooter(Vector3 position, float muzzleVelocity = 800f)
        {
            var entity = _world.CreateEntity();
            _world.AddComponent(entity, new SimTransform { Position = position, Rotation = Quaternion.Identity });
            _world.AddComponent(entity, new WeaponState  { MuzzleVelocity = muzzleVelocity, Ammo = 10 });
            return entity;
        }

        private Entity SpawnShooterNonAuthoritative(Vector3 position, float muzzleVelocity = 800f)
        {
            var entity = _world.CreateEntity();
            _world.AddComponent(entity, new SimTransform { Position = position, Rotation = Quaternion.Identity });
            _world.AddComponent(entity, new WeaponState  { MuzzleVelocity = muzzleVelocity, Ammo = 10 });
            // Remote owner: primary owner ID differs from the local node ID.
            _world.AddComponent(entity, new NetworkAuthority(primaryOwnerId: 2, localNodeId: 1));
            return entity;
        }

        private static void AssertNear(Vector3 expected, Vector3 actual, float tolerance = 1e-3f)
            => Assert.True(Vector3.Distance(expected, actual) < tolerance, $"expected {expected}, got {actual}");

        /// <summary>⭐ Buildings §3d P2 (R-217; AQ85 §D's first half) — the shot flies along the SIGHT line: eye height of the
        /// shooter's LOGICAL stance to half the target's collider height (a vehicle), else half its eye height (a soldier).</summary>
        [Fact]
        public void R217_TheShot_FliesFromTheShootersEye_ToTheMiddleOfTheTargetsSilhouette()
        {
            _world.RegisterComponent<Hrot.MuscleCharacter.Animation.Components.StanceIntent>();
            var shooter = SpawnShooter(new Vector3(0f, 0f, 3f));                                   // on an upper floor
            _world.AddComponent(shooter, new Hrot.MuscleCharacter.Animation.Components.StanceIntent { TargetStance = Fdp.Toolkit.Tkb.Domain.StanceId.Prone });
            var vehicle = SpawnTarget(new Vector3(50f, 0f, 0f));
            _world.AddComponent(vehicle, new PhysicsCollider { Radius = 2f, Height = 2.4f });

            PublishIntent(shooter, vehicle);
            _sys.Execute(_world, 0.016f);

            foreach (var e in _world.Query().With<BallisticProjectile>().Build())
            {
                var eye = new Vector3(0f, 0f, 3f + Fdp.Toolkit.Tkb.Parameters.EngineFallbacks.EyeHeightProne);
                var dir = Vector3.Normalize(new Vector3(50f, 0f, 1.2f) - eye);
                AssertNear(eye + dir * CombatConstants.MuzzleOffsetMeters, _world.GetComponent<SimTransform>(e).Position);
                AssertNear(dir * 800f, _world.GetComponent<SimVelocity>(e).Linear, 1e-2f);
                return;
            }
            Assert.Fail("No bullet entity found.");
        }

        /// <summary>⭐ T-4 — the fire chain writes the round's record with the inputs it fired with.</summary>
        /// <summary>A 0.9 m sill (a wall) across the line at x ∈ [10, 10.5].</summary>
        private void SillWorld()
        {
            _world.RegisterManagedComponent<Fdp.Toolkit.Terrain.TerrainWorld>();   // idempotent
            var min = new Vector2(10f, -5f); var max = new Vector2(10.5f, 5f);
            _world.SetSingletonManaged(new Fdp.Toolkit.Terrain.TerrainWorld
            {
                BoundsMin = new Vector2(-50, -50), BoundsMax = new Vector2(50, 50),
                Prisms = new[] { new Fdp.Toolkit.Terrain.TerrainPrism
                {
                    Kind = Fdp.Toolkit.Terrain.TerrainPrismKind.Wall,
                    Footprint = new[] { min, new Vector2(max.X, min.Y), max, new Vector2(min.X, max.Y) },
                    BaseZ = 0, TopZ = 0.9f, Min = min, Max = max,
                } },
            });
        }

        private Vector3 FlightOfTheRound()
        {
            foreach (var e in _world.Query().With<BallisticProjectile>().Build())
                return Vector3.Normalize(_world.GetComponent<SimVelocity>(e).Linear);
            Assert.Fail("No bullet entity found.");
            return default;
        }

        /// <summary>Where the round aims at <paramref name="x"/>, from a standing eye at the origin: the flight line's height there.</summary>
        private float AimHeightAt(float x)
        {
            var d = FlightOfTheRound();
            return Fdp.Toolkit.Tkb.Parameters.EngineFallbacks.EyeHeightStanding + d.Z / d.X * x;
        }

        /// <summary>
        /// ⭐⭐ <c>CE-3136</c> P-2 (D1, revised by the user) — a crouched man behind a 0.5 m-thick CONCRETE sill 0.9 m high: his middle
        /// (0.6 m) is hidden and a round cannot go through, so it aims at the middle of what the shooter SEES — halfway between the
        /// lowest visible height (≈ 0.79 m, where the line clears the sill's far edge) and his top (≈ 1.0 m) ⇒ ≈ 0.89 m.
        /// 🔴 Red-proofs: the original aim (0.6 m) met the concrete (G1); the first P-2 build aimed at his crown (1.0 m) — 🔒 user:
        /// <i>"aiming at highest point meant we wont hit"</i>.
        /// </summary>
        [Fact]
        public void P2_ACrouchedManBehindAConcreteSill_IsAimedAtTheMiddleOfWhatTheShooterSees()
        {
            _world.RegisterComponent<Hrot.MuscleCharacter.Animation.Components.StanceIntent>();
            SillWorld();
            var shooter = SpawnShooter(Vector3.Zero);                                   // standing eye 1.7 m
            var target  = SpawnTarget(new Vector3(12f, 0f, 0f));
            _world.AddComponent(target, new Hrot.MuscleCharacter.Animation.Components.StanceIntent { TargetStance = Fdp.Toolkit.Tkb.Domain.StanceId.Crouched });

            PublishIntent(shooter, target);
            _sys.Execute(_world, 0.016f);

            float eye = Fdp.Toolkit.Tkb.Parameters.EngineFallbacks.EyeHeightStanding;
            float lowestSeen = eye - 0.8f * 12f / 10.5f;                                  // the line just clears (10.5, 0.9)
            float top = 1.0f * Fdp.Toolkit.Tkb.Parameters.EngineFallbacks.EyeHeightCrouched;    // BodyProfile's crouched top = the eye (R-246)
            float expected = 0.5f * (lowestSeen + top);
            Assert.InRange(AimHeightAt(12f), expected - 0.01f, expected + 0.01f);   // the visible edge is bisected to ≈ 1 cm
        }

        /// <summary>
        /// ⭐ <c>CE-3136</c> P-2 (D1, revised) — the same man behind a thin WOODEN fence (5 cm of fence-wood ⇒ 3 mm RHA against the
        /// light round's 5 mm): the shooter cannot see his middle, but a round goes through, so it aims at the MIDDLE. 🔒 User:
        /// <i>"if the body is hidden behind a weak penetrable obstacle, we could aim to body center"</i>.
        /// </summary>
        [Fact]
        public void P2_BehindAWeakFence_TheRoundAimsAtTheMiddle_Through_It()
        {
            _world.RegisterComponent<Hrot.MuscleCharacter.Animation.Components.StanceIntent>();
            _world.RegisterManagedComponent<Fdp.Toolkit.Terrain.TerrainWorld>();
            Assert.True(Fdp.Toolkit.Terrain.TerrainMaterialLibrary.Shared.TryGet("fence-wood", out var wood));
            var min = new Vector2(10f, -5f); var max = new Vector2(10.05f, 5f);
            _world.SetSingletonManaged(new Fdp.Toolkit.Terrain.TerrainWorld
            {
                BoundsMin = new Vector2(-50, -50), BoundsMax = new Vector2(50, 50),
                Prisms = new[] { new Fdp.Toolkit.Terrain.TerrainPrism
                {
                    Kind = Fdp.Toolkit.Terrain.TerrainPrismKind.Wall, Material = wood,
                    Footprint = new[] { min, new Vector2(max.X, min.Y), max, new Vector2(min.X, max.Y) },
                    BaseZ = 0, TopZ = 0.9f, Min = min, Max = max,
                } },
            });
            var shooter = SpawnShooter(Vector3.Zero);
            var target  = SpawnTarget(new Vector3(12f, 0f, 0f));
            _world.AddComponent(target, new Hrot.MuscleCharacter.Animation.Components.StanceIntent { TargetStance = Fdp.Toolkit.Tkb.Domain.StanceId.Crouched });

            PublishIntent(shooter, target);
            _sys.Execute(_world, 0.016f);

            float middle = Fdp.Toolkit.Perception.LineOfSight.TerrainWorldLosStrategy.AimHeightFor(_world, target,
                Fdp.Toolkit.Tkb.Domain.StanceId.Crouched, 0f);
            Assert.Equal(middle, AimHeightAt(12f), 2);
        }

        /// <summary>⭐ <c>CE-3136</c> P-2 — with terrain resident but nothing in the way, the round still aims at the MIDDLE (as-built
        /// refinement of D1: a target in the open keeps today's aim, so existing engagements do not move).</summary>
        [Fact]
        public void P2_ATargetInTheOpen_KeepsTheMiddleAim()
        {
            SillWorld();
            var shooter = SpawnShooter(new Vector3(0f, 20f, 0f));                       // beside the sill, nothing between
            var target  = SpawnTarget(new Vector3(12f, 20f, 0f));

            PublishIntent(shooter, target);
            _sys.Execute(_world, 0.016f);

            var eye = new Vector3(0f, 20f, Fdp.Toolkit.Tkb.Parameters.EngineFallbacks.EyeHeightStanding);
            var mid = new Vector3(12f, 20f, 0.5f * Fdp.Toolkit.Tkb.Parameters.EngineFallbacks.EyeHeightStanding);
            AssertNear(Vector3.Normalize(mid - eye), FlightOfTheRound(), 1e-3f);
        }

        [Fact]
        public void T4_EveryRound_IsRecordedInTheShotLog_WithItsInputs()
        {
            var shooter = SpawnShooter(Vector3.Zero);
            var target  = SpawnTarget(new Vector3(10f, 0f, 0f));
            PublishIntent(shooter, target);
            _sys.Execute(_world, 0.016f);
            var shot = Assert.Single(ShotLog.Peek(_world)!.Recent(10));
            Assert.Equal(shooter, shot.Shooter);
            Assert.Equal(target, shot.Target);
            Assert.Equal(ShotOutcome.InFlight, shot.Outcome);
            Assert.Equal(CombatConstants.DefaultBulletDamage, shot.Damage);
            Assert.Contains("unknown munition", shot.PenetrationSource);
            foreach (var e in _world.Query().With<BallisticProjectile>().Build()) Assert.Equal(e, shot.Bullet);
        }

        /// <summary>⭐ Air Ops slice 4 (docs/designs/air-attack/DESIGN_Air_Operations.md §8, A4) — an AIRCRAFT's bomb leaves from the body
        /// with the carrier's velocity plus 1 m/s down and falls (Arc), whatever it was aimed at; a GROUND shooter that is moving keeps
        /// today's round exactly (its muzzle speed along the aim line, nothing of its own velocity).</summary>
        [Fact]
        public void A4_AnAircraftsBomb_KeepsItsVelocity_AGroundShotDoesNot()
        {
            _world.RegisterComponent<Fdp.Toolkit.Motion.Locomotion>();
            _world.RegisterComponent<TkbIdentity>();
            var db = new Fdp.Toolkit.Tkb.TkbDatabase();
            var plane = new Fdp.Interfaces.TkbTemplate("plane", 9405);
            plane.AddDescriptor(new Fdp.Toolkit.Tkb.Domain.WeaponSuiteDto { Mounts = { new Fdp.Toolkit.Tkb.Domain.WeaponMountDto
                { Category = Fdp.Toolkit.Tkb.Domain.WeaponCategory.Bomb, AmmoGuid = 9022, InitialAmmunition = 4, DamagePerHit = 1200 } } });
            db.Register(plane);
            var bomb = new Fdp.Interfaces.TkbTemplate("bomb", 9022);
            bomb.AddDescriptor(new Fdp.Toolkit.Tkb.Domain.WarheadDto
                { Kind = Fdp.Toolkit.Tkb.Domain.WarheadKind.HighExplosive, Indirect = true, BlastInjuryRadiusM = 20f, BlastLethalRadiusM = 8f, BlastLethalDamage = 1000f });
            db.Register(bomb);
            _world.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(db);

            var carrier = new Vector3(150f, 0f, 0f);
            var aircraft = SpawnShooter(new Vector3(0f, 0f, 300f));
            _world.AddComponent(aircraft, new SimVelocity { Linear = carrier });
            _world.AddComponent(aircraft, new Fdp.Toolkit.Motion.Locomotion { Class = Fdp.Toolkit.Tkb.Domain.LocomotionClass.FixedWing });
            _world.AddComponent(aircraft, new TkbIdentity { TkbType = 9405 });
            PublishIntent(aircraft, SpawnTarget(new Vector3(900f, 40f, 0f)));
            _sys.Execute(_world, 0.016f);

            var round = Assert.Single(Rounds());
            AssertNear(carrier + new Vector3(0f, 0f, -Fdp.Toolkit.Combat.BombBallistics.EjectMps), _world.GetComponent<SimVelocity>(round).Linear);
            AssertNear(new Vector3(0f, 0f, 300f), _world.GetComponent<SimTransform>(round).Position);   // from the body, not an eye
            Assert.NotEqual(0, _world.GetComponent<BallisticProjectile>(round).Warhead & WarheadRound.Arc);   // it falls
            _world.DestroyEntity(round);

            // a ground shooter on the move: unchanged — its muzzle speed along the aim line
            var truck = SpawnShooter(new Vector3(0f, 0f, 0f));
            _world.AddComponent(truck, new SimVelocity { Linear = new Vector3(0f, 15f, 0f) });
            PublishIntent(truck, SpawnTarget(new Vector3(100f, 0f, 0f)));
            _sys.Execute(_world, 0.016f);
            var shot = Assert.Single(Rounds());
            var v = _world.GetComponent<SimVelocity>(shot).Linear;
            Assert.Equal(800f, v.Length(), 2);
            Assert.True(MathF.Abs(v.Y) < 1e-3f, $"no carrier velocity in a ground shot: {v}");
        }

        /// <summary>
        /// ⭐ Air Ops slice 6a F1 + F3 (docs/designs/air-attack/DESIGN_Air_Operations.md §7a, AW-4; A4 unchanged) — from an aircraft in
        /// flight, the cannon's SHELL leaves at its muzzle speed along the aim plus the carrier's velocity; a Hydra-class ROCKET at its own
        /// 700 m/s along the aim plus the carrier's velocity, UNGUIDED (<see cref="GuidedRoundSystem"/> never turns it); and a ripple of six
        /// scatters round the aim line within the pod's dispersion (10 mils, doubled on the move) — no two rounds on one line.
        /// </summary>
        [Fact]
        public void F3_FromTheAir_TheShellAndTheRocket_KeepTheCarriersVelocity_TheRocketFliesUnguided_ARippleScatters()
        {
            const long Heli = 9406, Rocket = 9025;
            _world.RegisterComponent<Fdp.Toolkit.Motion.Locomotion>();
            _world.RegisterComponent<TkbIdentity>();
            _world.RegisterComponent<GuidedRound>();
            _world.RegisterComponent<ShotOrdinal>();
            var db = new Fdp.Toolkit.Tkb.TkbDatabase();
            var heli = new Fdp.Interfaces.TkbTemplate("heli", Heli);
            heli.AddDescriptor(new Fdp.Toolkit.Tkb.Domain.WeaponSuiteDto { Mounts =
            {
                new Fdp.Toolkit.Tkb.Domain.WeaponMountDto { Category = Fdp.Toolkit.Tkb.Domain.WeaponCategory.Autocannon, MuzzleVelocity = 1000f,
                                                            InitialAmmunition = 100, DamagePerHit = 30 },
                new Fdp.Toolkit.Tkb.Domain.WeaponMountDto { Category = Fdp.Toolkit.Tkb.Domain.WeaponCategory.Rocket, AmmoGuid = Rocket,
                                                            DispersionMils = 10f, InitialAmmunition = 38, DamagePerHit = 600 },
            } });
            db.Register(heli);
            var rocket = new Fdp.Interfaces.TkbTemplate("rocket", Rocket);
            rocket.AddDescriptor(new Fdp.Toolkit.Tkb.Domain.MissileDto { Guidance = Fdp.Toolkit.Tkb.Domain.GuidanceKind.Unguided, SpeedMps = 700f, MaxFlightSeconds = 6f });
            rocket.AddDescriptor(new Fdp.Toolkit.Tkb.Domain.WarheadDto
                { Kind = Fdp.Toolkit.Tkb.Domain.WarheadKind.HighExplosive, BlastLethalRadiusM = 2f, BlastInjuryRadiusM = 6f, BlastLethalDamage = 150f });
            db.Register(rocket);
            _world.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(db);

            var carrier = new Vector3(40f, 10f, 0f);
            var aircraft = SpawnShooter(new Vector3(0f, 0f, 100f), muzzleVelocity: 1000f);   // mount 0 fires at its WeaponState's speed
            _world.AddComponent(aircraft, new SimVelocity { Linear = carrier });
            _world.AddComponent(aircraft, new Fdp.Toolkit.Motion.Locomotion { Class = Fdp.Toolkit.Tkb.Domain.LocomotionClass.Rotorcraft });
            _world.AddComponent(aircraft, new TkbIdentity { TkbType = Heli });
            var target = SpawnTarget(new Vector3(1500f, 0f, 0f));

            // the shell: its muzzle speed along the aim, plus the carrier
            PublishIntent(aircraft, target, weaponIndex: 0);
            _sys.Execute(_world, 0.016f);
            var shell = Assert.Single(Rounds());
            var vs = _world.GetComponent<SimVelocity>(shell).Linear - carrier;
            Assert.Equal(1000f, vs.Length(), 1);
            Assert.True(vs.X > 990f, $"along the aim: {vs}");
            _world.DestroyEntity(shell);

            // a ripple of six rockets
            var yaws = new System.Collections.Generic.List<float>();
            for (int i = 0; i < 6; i++)
            {
                PublishIntent(aircraft, target, weaponIndex: 1);
                _sys.Execute(_world, 0.016f);
            }
            var rockets = Rounds();
            Assert.Equal(6, rockets.Count);
            float sigma = 10f / 1000f * HitModel.MovingFactor;
            foreach (var r in rockets)
            {
                var own = _world.GetComponent<SimVelocity>(r).Linear - carrier;
                Assert.Equal(700f, own.Length(), 1);                                                   // its own speed …
                Assert.Equal(Fdp.Toolkit.Tkb.Domain.GuidanceKind.Unguided, _world.GetComponent<GuidedRound>(r).Guidance);
                float yaw = MathF.Atan2(own.Y, own.X);                                                 // the aim line is +X (horizontal)
                Assert.InRange(MathF.Abs(yaw), 0f, sigma + 1e-4f);                                    // … within the dispersion
                yaws.Add(yaw);
            }
            Assert.True(System.Linq.Enumerable.Count(System.Linq.Enumerable.Distinct(yaws)) >= 5, "a ripple scatters: no two rounds on one line");

            // unguided: a guidance step leaves every rocket's velocity as it was
            var before = new System.Collections.Generic.Dictionary<Entity, Vector3>();
            foreach (var r in rockets) before[r] = _world.GetComponent<SimVelocity>(r).Linear;
            new GuidedRoundSystem().Execute(_world, 0.5f);
            foreach (var r in rockets) AssertNear(before[r], _world.GetComponent<SimVelocity>(r).Linear);
        }

        /// <summary>
        /// ⭐ <c>CE-2173</c> slice 6d (docs/designs/air-attack/DESIGN_Air_Operations.md §9f; A4, WS-3) — a DOOR GUN on a flying helicopter:
        /// the shooter of the intent is the weapon SYSTEM (an M134 in the 404's door station), which has no locomotion of its own; its round
        /// still leaves at its muzzle speed along the aim PLUS the helicopter's velocity — the carrier is the body it sits in
        /// (<see cref="HitModel.BodyOf"/>, the one WS-3 already aims from). ✅ Red-proof: the A4 rule read from the weapon itself ⇒ the round
        /// leaves at the muzzle speed alone, 70 m/s short along the track.
        /// </summary>
        [Fact]
        public void S6d_ADoorGunOnAFlyingHelicopter_ItsRoundKeepsTheHelicoptersVelocity()
        {
            _world.RegisterComponent<Fdp.Toolkit.Motion.Locomotion>();
            _world.RegisterComponent<WeaponSystemParams>();
            _world.RegisterComponent<Fdp.Toolkit.Behavior.Components.IsEmbarkedTag>();
            var carrier = new Vector3(70f, 0f, 0f);
            var heli = SpawnTarget(new Vector3(0f, 0f, 100f));
            _world.AddComponent(heli, new SimVelocity { Linear = carrier });
            _world.AddComponent(heli, new Fdp.Toolkit.Motion.Locomotion { Class = Fdp.Toolkit.Tkb.Domain.LocomotionClass.Rotorcraft });
            var m134 = SpawnShooter(new Vector3(3f, 1.35f, 99.3f), muzzleVelocity: 850f);
            _world.AddComponent(m134, new WeaponSystemParams { NeedsOperator = 1 });
            _world.AddComponent(m134, new Fdp.Toolkit.Behavior.Components.IsEmbarkedTag { VehicleEntity = heli, Seat = 9 });
            var target = SpawnTarget(new Vector3(0f, 400f, 0f));   // abeam to the left

            PublishIntent(m134, target);
            _sys.Execute(_world, 0.016f);
            var round = Assert.Single(Rounds());
            var own = _world.GetComponent<SimVelocity>(round).Linear - carrier;
            Assert.Equal(850f, own.Length(), 1);                                        // its own speed along the aim …
            Assert.True(own.Y > 800f, $"towards the target abeam: {own}");             // … which points left, at the target …
            Assert.True(MathF.Abs(own.X) < 1f, $"… so the helicopter's 70 m/s is ALL carrier: {own}");
        }

        /// <summary>
        /// ⭐⭐ Indirect fire IF-1 (I3, I4; DESIGN_Indirect_Fire §3.1, IF-F) — the SimHost flies the brain's fire solution exactly: the intent's
        /// CHARGE (its speed from the round's charge table) on the intent's ARC — no second decision; charge 0 keeps today's arc. A tube's
        /// rounds scatter round the point by the mount's dispersion (azimuth AND range), the SAME on every run (no random source).
        /// ✅ Red-proof: the arc ignoring the intent ⇒ ThrowArc's 60 m/s ⇒ red; dropping the dispersion ⇒ one landing point ⇒ red.
        /// </summary>
        [Fact]
        public void IF1_TheIntentsChargeAndArc_AreFlown_AndATubesRoundsScatter_TheSameEveryRun()
        {
            const long Tube = 9610, Bomb = 9002;
            Vector3[] Fire(int rounds, byte charge, byte high)
            {
                using var w = new EntityRepository();
                w.RegisterComponent<SimTransform>();
                w.RegisterComponent<SimVelocity>();
                w.RegisterComponent<WeaponState>();
                w.RegisterComponent<BallisticProjectile>();
                w.RegisterComponent<PhysicsCollider>();
                w.RegisterComponent<TkbIdentity>();
                w.RegisterComponent<ShotOrdinal>();
                w.RegisterEvent<WeaponFireIntent>();
                w.RegisterEvent<WeaponFireNotification>();
                var db = new Fdp.Toolkit.Tkb.TkbDatabase();
                var tube = new Fdp.Interfaces.TkbTemplate("tube", Tube);
                tube.AddDescriptor(new Fdp.Toolkit.Tkb.Domain.WeaponSuiteDto { Mounts = { new Fdp.Toolkit.Tkb.Domain.WeaponMountDto
                    { Category = Fdp.Toolkit.Tkb.Domain.WeaponCategory.Mortar, AmmoGuid = Bomb, InitialAmmunition = 30, DamagePerHit = 100,
                      DispersionMils = 5f, Arc = Fdp.Toolkit.Tkb.Domain.IndirectArc.HighOnly, MuzzleVelocity = 60f } } });
                db.Register(tube);
                var bomb = new Fdp.Interfaces.TkbTemplate("bomb", Bomb);
                bomb.AddDescriptor(new Fdp.Toolkit.Tkb.Domain.WarheadDto { Kind = Fdp.Toolkit.Tkb.Domain.WarheadKind.HighExplosive, Indirect = true,
                    BlastInjuryRadiusM = 10f, BlastLethalRadiusM = 3f, BlastLethalDamage = 150f });
                bomb.AddDescriptor(new Fdp.Toolkit.Tkb.Domain.AmmoWeaponBallisticsDto { ChargeSpeedsMps = new() { 99f, 147f, 183f } });
                db.Register(bomb);
                w.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(db);
                var shooter = w.CreateEntity();
                w.AddComponent(shooter, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });
                w.AddComponent(shooter, new WeaponState { MuzzleVelocity = 60f, Ammo = 30 });
                w.AddComponent(shooter, new TkbIdentity { TkbType = Tube });
                var sys = new FireProcessingSystem();
                var velocities = new Vector3[rounds];
                for (int i = 0; i < rounds; i++)
                {
                    w.Bus.Publish(new WeaponFireIntent { Shooter = shooter, AtPoint = true, TargetPoint = new Vector3(2000f, 0f, 0f), Charge = charge, HighArc = high });
                    w.Bus.SwapBuffers();
                    sys.Execute(w, 0.016f);
                    foreach (var e in w.Query().With<BallisticProjectile>().Build()) { velocities[i] = w.GetComponent<SimVelocity>(e).Linear; w.DestroyEntity(e); }
                }
                return velocities;
            }

            // the solution flown: charge 3 (183 m/s), high — within the spread of the exact launch
            var first = Fire(8, charge: 3, high: 1);
            foreach (var v in first)
            {
                Assert.InRange(v.Length(), 183f * 0.99f, 183f * 1.01f);
                Assert.True(v.Z > new Vector2(v.X, v.Y).Length(), $"high: {v}");
            }
            // it scatters: no two rounds on one launch, both azimuth (Y) and range (speed) vary, within ±σ
            Assert.Equal(8, System.Linq.Enumerable.Count(System.Linq.Enumerable.Distinct(first)));
            Assert.Contains(first, v => v.Y > 0f);
            Assert.Contains(first, v => v.Y < 0f);
            foreach (var v in first) Assert.InRange(MathF.Abs(MathF.Atan2(v.Y, v.X)), 0f, 0.005f + 1e-4f);
            // the same on every run
            Assert.Equal(first, Fire(8, charge: 3, high: 1));
            // charge 0 = today's arc: ThrowArc at the mount's 60 m/s (high, a launcher)
            Assert.Equal(60f, Fire(1, charge: 0, high: 0)[0].Length(), 0);
        }

        /// <summary>
        /// ⭐⭐ <c>CE-2183</c> (AT2, R-2021, DESIGN_Air_Operations §7c) — a rifleman's round at a helicopter crossing at 60 m/s, 300 m out, is
        /// aimed AHEAD of it by its velocity × the round's flight time (± 1 m, <see cref="TargetLead"/>); a ground vehicle at the same speed
        /// is aimed where it is (unchanged). 🔴 Red-proof: without the lead the round passes where the helicopter WAS — ~23 m behind it.
        /// </summary>
        [Fact]
        public void CE2183_ARoundAtACrossingHelicopter_IsLed_AGroundVehicleIsNot()
        {
            _world.RegisterComponent<Fdp.Toolkit.Motion.Locomotion>();
            var crossing = new Vector3(0f, 60f, 0f);

            float LateralAtTheTarget(Entity target)
            {
                PublishIntent(SpawnShooter(Vector3.Zero), target);
                _sys.Execute(_world, 0.016f);
                var round = Assert.Single(Rounds());
                var start = _world.GetComponent<SimTransform>(round).Position;
                var v = _world.GetComponent<SimVelocity>(round).Linear;
                float y = start.Y + v.Y / v.X * (300f - start.X);   // where the round's line crosses the target's track
                _world.DestroyEntity(round);
                return y;
            }

            var heli = SpawnTarget(new Vector3(300f, 0f, 60f));
            _world.AddComponent(heli, new SimVelocity { Linear = crossing });
            _world.AddComponent(heli, new Fdp.Toolkit.Motion.Locomotion { Class = Fdp.Toolkit.Tkb.Domain.LocomotionClass.Rotorcraft });
            float t = TargetLead.FlightTime(Vector3.Zero, Vector3.Zero, 800f, new Vector3(300f, 0f, 60f), crossing);
            Assert.InRange(LateralAtTheTarget(heli), 60f * t - 1f, 60f * t + 1f);

            var truck = SpawnTarget(new Vector3(300f, 0f, 0f));
            _world.AddComponent(truck, new SimVelocity { Linear = crossing });
            Assert.InRange(LateralAtTheTarget(truck), -0.5f, 0.5f);
        }

        /// <summary>
        /// ⭐ <c>CE-3198</c> (DESIGN_Missiles_And_Ammo §3b) — an NLAW (overfly) fired at a HULL flies the overfly top attack; fired at a
        /// target with no hull (a man) it flies direct (<see cref="GuidedRound.Overfly"/> 0) — the downward charge is the anti-armour mode.
        /// </summary>
        [Fact]
        public void CE3198_AnNlawAtAHull_FliesTheOverfly_AtAManItFliesDirect()
        {
            _world.RegisterComponent<GuidedRound>();
            _world.RegisterComponent<TkbIdentity>();
            const long Launcher = 9500, Nlaw = 9513;
            var db = new Fdp.Toolkit.Tkb.TkbDatabase();
            var launcher = new Fdp.Interfaces.TkbTemplate("nlaw gunner", Launcher);
            launcher.AddDescriptor(new Fdp.Toolkit.Tkb.Domain.WeaponSuiteDto { Mounts = { new Fdp.Toolkit.Tkb.Domain.WeaponMountDto
                { Category = Fdp.Toolkit.Tkb.Domain.WeaponCategory.Atgm, AmmoGuid = Nlaw, InitialAmmunition = 2, DamagePerHit = 900, Penetration = 500 } } });
            db.Register(launcher);
            var nlaw = new Fdp.Interfaces.TkbTemplate("nlaw", Nlaw);
            nlaw.AddDescriptor(new Fdp.Toolkit.Tkb.Domain.MissileDto
                { Guidance = Fdp.Toolkit.Tkb.Domain.GuidanceKind.FireAndForget, SpeedMps = 200f, MaxFlightSeconds = 5f, MinRangeM = 20f, Overfly = true });
            db.Register(nlaw);
            _world.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(db);

            byte OverflyAt(Entity target)
            {
                var gunner = SpawnShooter(Vector3.Zero, muzzleVelocity: 200f);
                _world.AddComponent(gunner, new TkbIdentity { TkbType = Launcher });
                PublishIntent(gunner, target);
                _sys.Execute(_world, 0.016f);
                var round = Assert.Single(Rounds());
                byte overfly = _world.GetComponent<GuidedRound>(round).Overfly;
                _world.DestroyEntity(round);
                return overfly;
            }

            var tank = SpawnTarget(new Vector3(200f, 0f, 0f));
            _world.AddComponent(tank, new PhysicsCollider { Radius = 2f, Height = 2.4f });
            Assert.Equal(1, OverflyAt(tank));
            Assert.Equal(0, OverflyAt(SpawnTarget(new Vector3(200f, 0f, 0f))));   // no hull: direct
        }

        /// <summary>
        /// ⭐⭐ <c>CE-2189</c> (DESIGN_Missiles_And_Ammo §3c, AD4) — an IR missile launched at a HELICOPTER warns it at launch: one
        /// <see cref="MissileWarningEvent"/> on the helicopter, from the launch point, with its seeker and the flight time (distance /
        /// speed); none for the same missile at a ground target. 🔴 Red-proof: without the publish no warning is ever read.
        /// </summary>
        [Fact]
        public void CE2189_AMissileAtAHelicopter_WarnsIt_NoneAtAGroundTarget()
        {
            _world.RegisterComponent<GuidedRound>();
            _world.RegisterComponent<TkbIdentity>();
            _world.RegisterComponent<Fdp.Toolkit.Motion.Locomotion>();
            _world.RegisterEvent<MissileWarningEvent>();
            const long Launcher = 9600, Sam = 9630;
            var db = new Fdp.Toolkit.Tkb.TkbDatabase();
            var launcher = new Fdp.Interfaces.TkbTemplate("sam gunner", Launcher);
            launcher.AddDescriptor(new Fdp.Toolkit.Tkb.Domain.WeaponSuiteDto { Mounts = { new Fdp.Toolkit.Tkb.Domain.WeaponMountDto
                { Category = Fdp.Toolkit.Tkb.Domain.WeaponCategory.Sam, AmmoGuid = Sam, InitialAmmunition = 1, DamagePerHit = 400 } } });
            db.Register(launcher);
            var sam = new Fdp.Interfaces.TkbTemplate("sam", Sam);
            sam.AddDescriptor(new Fdp.Toolkit.Tkb.Domain.MissileDto
                { Guidance = Fdp.Toolkit.Tkb.Domain.GuidanceKind.InfraRed, SpeedMps = 700f, MaxFlightSeconds = 12f, TargetDomain = Fdp.Toolkit.Tkb.Domain.TargetDomain.Air });
            db.Register(sam);
            _world.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(db);

            MissileWarningEvent[] WarningsAt(Entity target)
            {
                var gunner = SpawnShooter(Vector3.Zero, muzzleVelocity: 700f);
                _world.AddComponent(gunner, new TkbIdentity { TkbType = Launcher });
                PublishIntent(gunner, target);
                _sys.Execute(_world, 0.016f);
                _world.Bus.SwapBuffers();
                var read = _world.Bus.Read<MissileWarningEvent>().ToArray();
                foreach (var r in Rounds()) _world.DestroyEntity(r);
                return read;
            }

            var heli = SpawnTarget(new Vector3(1400f, 0f, 300f));
            _world.AddComponent(heli, new Fdp.Toolkit.Motion.Locomotion { Class = Fdp.Toolkit.Tkb.Domain.LocomotionClass.Rotorcraft });
            var w = Assert.Single(WarningsAt(heli));
            Assert.Equal(heli, w.Unit);
            Assert.Equal((byte)Fdp.Toolkit.Tkb.Domain.GuidanceKind.InfraRed, w.Seeker);
            Assert.InRange(w.TimeToImpactS, 1.9f, 2.2f);   // ≈ 1430 m / 700 m/s
            Assert.Empty(WarningsAt(SpawnTarget(new Vector3(300f, 0f, 0f))));
        }

        /// <summary>
        /// ⭐ <c>CE-2190</c> (Weapon Systems §5c, AD5) — a ZU-23-2 round (no proximity fuze, no dispersion here) at a helicopter crossing at
        /// 60 m/s, 150 m up and 1 km out, is AIMED AHEAD by AT2's lead so that its straight path passes within the helicopter's 1.5 m body
        /// radius of where the helicopter IS at that moment — it hits by geometry alone. 🔴 Red-proof: unled, it passes ~50 m behind.
        /// </summary>
        [Fact]
        public void CE2190_AZu23RoundAtAHelicopterCrossingAtAKilometre_MeetsIt()
        {
            _world.RegisterComponent<Fdp.Toolkit.Motion.Locomotion>();
            var heliAt = new Vector3(1000f, 0f, 150f);
            var crossing = new Vector3(0f, 60f, 0f);
            var heli = SpawnTarget(heliAt);
            _world.AddComponent(heli, new SimVelocity { Linear = crossing });
            _world.AddComponent(heli, new Fdp.Toolkit.Motion.Locomotion { Class = Fdp.Toolkit.Tkb.Domain.LocomotionClass.Rotorcraft });
            PublishIntent(SpawnShooter(Vector3.Zero, muzzleVelocity: 1250f), heli);   // the BDC mount's speed (range 2 500 × 0.5)
            _sys.Execute(_world, 0.016f);
            var round = Assert.Single(Rounds());
            var p0 = _world.GetComponent<SimTransform>(round).Position;
            var v = _world.GetComponent<SimVelocity>(round).Linear;
            var aimAt = AimPoint.Middle(_world, heli, out _, out _);
            float closest = float.MaxValue;
            for (float t = 0f; t < 2f; t += 0.0005f)
                closest = MathF.Min(closest, Vector3.Distance(p0 + v * t, aimAt + crossing * t));
            Assert.True(closest <= 1.5f, $"the round passes {closest:0.0} m from the helicopter");
        }

        private System.Collections.Generic.List<Entity> Rounds()
        {
            var rounds = new System.Collections.Generic.List<Entity>();
            foreach (var e in _world.Query().With<BallisticProjectile>().Build()) rounds.Add(e);
            return rounds;
        }

        /// <summary>
        /// ⭐⭐ <c>CE-3232</c> (B-10, docs/DESIGN_Civilians_And_Crowd_Control.md §7.11 LD-3) — a LAID-DOWN mount makes no round, whoever asked:
        /// on the brain's node from his own mask, on any other node from the replicated <see cref="WeaponAmmo"/> bit; taken up, it fires again.
        /// ✅ Red-proof: the FireProcessing gate dropped ⇒ a round is made ⇒ red.
        /// </summary>
        [Fact]
        public void CE3232_ALaidDownMount_MakesNoRound_HereOrAsAReplica_AndTakenUpItFires()
        {
            int Rounds() { int n = 0; foreach (var _ in _world.Query().With<BallisticProjectile>().Build()) n++; return n; }
            var shooter = SpawnShooter(Vector3.Zero);
            var target = SpawnTarget(new Vector3(10f, 0f, 0f));
            Assert.True(Fdp.Toolkit.Combat.WeaponLaying.LayDown(_world, shooter, 0));
            PublishIntent(shooter, target);
            _sys.Execute(_world, 0.016f);
            Assert.Equal(0, Rounds());

            Fdp.Toolkit.Combat.WeaponLaying.LayDown(_world, shooter, 0, down: false);
            _world.Bus.SwapBuffers();
            PublishIntent(shooter, target);
            _sys.Execute(_world, 0.016f);
            Assert.Equal(1, Rounds());

            // a replica (his brain runs elsewhere): the bit arrives in WeaponAmmo
            var replica = SpawnShooter(new Vector3(0f, 5f, 0f));
            _world.RegisterComponent<WeaponAmmo>();
            _world.AddComponent(replica, new WeaponAmmo { Count = 1, LaidDown = 1 });
            _world.Bus.SwapBuffers();
            PublishIntent(replica, target);
            _sys.Execute(_world, 0.016f);
            Assert.Equal(1, Rounds());
        }

        private Entity SpawnTarget(Vector3 position)
        {
            var entity = _world.CreateEntity();
            _world.AddComponent(entity, new SimTransform { Position = position, Rotation = Quaternion.Identity });
            return entity;
        }

        private void PublishIntent(Entity shooter, Entity target, int weaponIndex = 0)
        {
            _world.Bus.Publish(new WeaponFireIntent
            {
                Shooter     = shooter,
                Target      = target,
                WeaponIndex = weaponIndex,
            });
            _world.Bus.SwapBuffers();
        }

        // ── T007 SC-1: WeaponFireIntent spawns bullet + fires notification ──────

        /// <summary>
        /// BS1-T007 SC-1: A <see cref="WeaponFireIntent"/> with both entities alive must
        /// produce exactly one bullet entity with <see cref="BallisticProjectile"/> and
        /// position <see cref="CombatConstants.MuzzleOffsetMeters"/> along the aim line (CE-3059), with the shooter field set correctly.
        /// </summary>
        [Fact]
        public void FireProcessing_SpawnsBulletEntity_WhenWeaponFireIntentReceived()
        {
            var shooterPos = new Vector3(10f, 20f, 0f);
            var shooter    = SpawnShooter(shooterPos);
            var target     = SpawnTarget(new Vector3(20f, 20f, 0f));

            PublishIntent(shooter, target);
            _sys.Execute(_world, 0.016f);

            var q = _world.Query().With<BallisticProjectile>().Build();
            int bulletCount = 0;
            Entity bulletEntity = default;
            foreach (var e in q)
            {
                bulletCount++;
                bulletEntity = e;
            }

            Assert.Equal(1, bulletCount);

            // ⭐ R-217 — the shot leaves from the shooter's EYE toward the middle of the target's silhouette (default standing soldier)
            var eye = shooterPos + new Vector3(0f, 0f, Fdp.Toolkit.Tkb.Parameters.EngineFallbacks.EyeHeightStanding);
            var aim = new Vector3(20f, 20f, Fdp.Toolkit.Tkb.Parameters.EngineFallbacks.EyeHeightStanding * 0.5f);
            var tf = _world.GetComponent<SimTransform>(bulletEntity);
            AssertNear(eye + Vector3.Normalize(aim - eye) * CombatConstants.MuzzleOffsetMeters, tf.Position);

            var proj = _world.GetComponent<BallisticProjectile>(bulletEntity);
            Assert.Equal(shooter, proj.Shooter);
            Assert.Equal(tf.Position, proj.PreviousPosition);   // the swept ray starts at the muzzle, not inside the shooter's spot
        }

        /// <summary>⭐ CE-3059 — a target closer than twice the muzzle offset: the bullet starts half way, so it is never
        /// spawned past the target (the swept ray would skip it).</summary>
        [Fact]
        public void FireProcessing_PointBlank_BulletStartsHalfWay_CE3059()
        {
            var shooter = SpawnShooter(new Vector3(0f, 0f, 0f));
            var target  = SpawnTarget(new Vector3(1f, 0f, 0f));

            PublishIntent(shooter, target);
            _sys.Execute(_world, 0.016f);

            foreach (var e in _world.Query().With<BallisticProjectile>().Build())
                AssertNear(new Vector3(0.5f, 0f, 1.7f * 0.75f), _world.GetComponent<SimTransform>(e).Position);   // half way, eye 1.7 → aim 0.85
        }

        /// <summary>
        /// After spawning the bullet a <see cref="WeaponFireNotification"/> with the correct
        /// shooter and target entity handles must appear on the event bus.
        /// </summary>
        [Fact]
        public void FireProcessing_PublishesWeaponFireNotification_AfterBulletSpawned()
        {
            var shooter = SpawnShooter(Vector3.Zero);
            var target  = SpawnTarget(new Vector3(10f, 0f, 0f));

            PublishIntent(shooter, target);
            _sys.Execute(_world, 0.016f);

            // Notifications are published to the write buffer; swap to expose them.
            _world.Bus.SwapBuffers();

            var notifications = _world.Bus.Read<WeaponFireNotification>();
            Assert.Equal(1, notifications.Length);
            // PACK-P003: notification carries Entity handles, not network IDs.
            Assert.Equal(shooter, notifications[0].Shooter);
            Assert.Equal(target,  notifications[0].Target);
        }

        /// <summary>
        /// ⭐⭐ <c>CE-1111</c> (BS-1-DESIGN §5.4b F1) — the notification carries THE SHOT: the round entity spawned, where it left and how
        /// fast, one round, the sim time — so a viewer draws the shot fired, never the slot's default. 🔴 Red-proof: before F1 the
        /// notification carried only shooter, target and slot (every field below read default).
        /// </summary>
        [Fact]
        public void CE1111_TheNotification_CarriesTheShot()
        {
            _world.SetSingleton(new GlobalTime { TotalTime = 42.0 });
            var shooter = SpawnShooter(Vector3.Zero);
            var target  = SpawnTarget(new Vector3(10f, 0f, 0f));
            PublishIntent(shooter, target);
            _sys.Execute(_world, 0.016f);
            _world.Bus.SwapBuffers();

            var n = Assert.Single(_world.Bus.Read<WeaponFireNotification>().ToArray());
            var round = Entity.Null;
            foreach (var e in _world.Query().With<BallisticProjectile>().Build()) round = e;
            Assert.Equal(round, n.Round);
            AssertNear(_world.GetComponent<SimTransform>(round).Position, n.Origin);
            AssertNear(_world.GetComponent<SimVelocity>(round).Linear, n.Velocity);
            Assert.Equal(1, n.Quantity);
            Assert.Equal(42.0, n.FiredAt, 6);
            Assert.Equal(_world.GetComponent<BallisticProjectile>(round).Ammo, n.AmmoGuid);
        }

        // ── T007 SC-3: Dead entity → skip gracefully ────────────────────────

        /// <summary>
        /// BS1-T007 SC-3: When the shooter entity is destroyed before the system runs
        /// the event must be skipped silently (no bullet, no exception).
        /// </summary>
        [Fact]
        public void FireProcessing_SkipsEvent_WhenShooterEntityDead()
        {
            var shooter = SpawnShooter(Vector3.Zero);
            var target  = SpawnTarget(new Vector3(10f, 0f, 0f));

            // Destroy the shooter before the system ticks.
            PublishIntent(shooter, target);
            _world.DestroyEntity(shooter);

            var ex = Record.Exception(() => _sys.Execute(_world, 0.016f));
            Assert.Null(ex);

            var q = _world.Query().With<BallisticProjectile>().Build();
            int bulletCount = 0;
            foreach (var _ in q) bulletCount++;
            Assert.Equal(0, bulletCount);
        }

        /// <summary>
        /// When the target entity is dead, the system also skips without spawning a bullet.
        /// </summary>
        [Fact]
        public void FireProcessing_SkipsEvent_WhenTargetEntityDead()
        {
            var shooter = SpawnShooter(Vector3.Zero);
            var target  = SpawnTarget(new Vector3(10f, 0f, 0f));

            PublishIntent(shooter, target);
            _world.DestroyEntity(target);
            _sys.Execute(_world, 0.016f);

            var q = _world.Query().With<BallisticProjectile>().Build();
            int bulletCount = 0;
            foreach (var _ in q) bulletCount++;
            Assert.Equal(0, bulletCount);
        }

        // ── Bullet velocity uses muzzle velocity from computed direction ───────

        /// <summary>
        /// Bullet <see cref="SimVelocity.Linear"/> equals <c>direction × MuzzleVelocity</c>
        /// where direction is computed from the shooter-to-target vector.
        /// </summary>
        [Fact]
        public void FireProcessing_SetsBulletVelocity_UsingMuzzleVelocityAndComputedDirection()
        {
            const float muzzleVelocity = 800f;
            var shooter = SpawnShooter(Vector3.Zero, muzzleVelocity);
            var target  = SpawnTarget(new Vector3(10f, 0f, 0f));

            PublishIntent(shooter, target);
            _sys.Execute(_world, 0.016f);

            var q = _world.Query().With<SimVelocity>().With<BallisticProjectile>().Build();
            foreach (var e in q)
            {
                var vel = _world.GetComponent<SimVelocity>(e);
                AssertNear(Vector3.Normalize(new Vector3(10f, 0f, 0.85f - 1.7f)) * muzzleVelocity, vel.Linear);   // ⭐ R-217 eye → aim
                return;
            }

            Assert.Fail("No bullet entity found.");
        }

        // ── TD-5: Ordering proxy — bullet must exist when notification is consumed ──

        /// <summary>
        /// TD-5 ordering-constraint proxy for BS1-T007:
        /// The <see cref="WeaponFireNotification"/> must be published AFTER the bullet entity
        /// has been created.
        ///
        /// <para>
        /// <b>Behavioral proxy:</b> This test asserts that EVERY <see cref="WeaponFireNotification"/>
        /// consumed after the system runs has a corresponding live <see cref="BallisticProjectile"/>
        /// entity whose <c>Shooter</c> matches the <c>Shooter</c> in the notification.
        /// This would fail if:
        ///   (a) the notification is emitted but bullet creation is removed or skipped,
        ///   (b) the notification <c>Shooter</c> is wrong, or
        ///   (c) the bullet is created after an already-consumed notification.
        /// </para>
        /// </summary>
        [Fact]
        public void FireProcessing_BulletExistsWhenNotificationIsConsumed_OrderingProxy()
        {
            var shooterEntity = SpawnShooter(Vector3.Zero);
            var targetEntity  = SpawnTarget(new Vector3(10f, 0f, 0f));

            PublishIntent(shooterEntity, targetEntity);
            _sys.Execute(_world, 0.016f);
            _world.Bus.SwapBuffers();

            var notifications = _world.Bus.Read<WeaponFireNotification>();
            Assert.Equal(1, notifications.Length);

            // PACK-P003: notification carries Entity handles directly.
            Assert.Equal(shooterEntity, notifications[0].Shooter);

            var query = _world.Query().With<BallisticProjectile>().Build();
            bool bulletFound = false;
            foreach (var bulletEntity in query)
            {
                var proj = _world.GetComponent<BallisticProjectile>(bulletEntity);
                if (proj.Shooter == shooterEntity)
                {
                    bulletFound = true;
                    break;
                }
            }

            Assert.True(bulletFound,
                "No BallisticProjectile with the expected Shooter was found in the world when " +
                "WeaponFireNotification was consumed.  This indicates the bullet was either not " +
                "created or was created for the wrong shooter.");
        }

        // ── Physics collider ──────────────────────────────────────────────────

        /// <summary>
        /// Bullet <see cref="PhysicsCollider.CollisionLayer"/> and <see cref="PhysicsCollider.Radius"/>
        /// must match the combat constants.
        /// </summary>
        [Fact]
        public void FireProcessing_SetsPhysicsCollider_WithBulletLayer()
        {
            var shooter = SpawnShooter(Vector3.Zero);
            var target  = SpawnTarget(new Vector3(10f, 0f, 0f));

            PublishIntent(shooter, target);
            _sys.Execute(_world, 0.016f);

            var q = _world.Query().With<PhysicsCollider>().With<BallisticProjectile>().Build();
            foreach (var e in q)
            {
                var collider = _world.GetComponent<PhysicsCollider>(e);
                Assert.Equal(CombatConstants.BulletCollisionLayer, collider.CollisionLayer);
                Assert.Equal(CombatConstants.BulletColliderRadius, collider.Radius);
                return;
            }

            Assert.Fail("No bullet entity with PhysicsCollider found.");
        }

        // ── CE-198: the Muscle executes the Brain's order, whoever owns the shooter ───
        //
        // These three rails REPLACE TD-6's pair, which asserted the opposite
        // (FireProcessing_SkipsBullet_WhenShooterNotAuthoritative /
        //  FireProcessing_SpawnsBullet_WhenShooterIsAuthoritative).
        //
        // ⚠ WHY THE OLD PAIR STAYED GREEN WHILE THE FEATURE WAS DEAD: both constructed
        //   NetworkAuthority by hand — primaryOwnerId 2 (a KNOWN other owner) and 1 (self).
        //   Production on the Muscle produces NEITHER: EntityMasterIngressTranslator stamps
        //   ghosts with the unknown-owner sentinel primaryOwnerId = -1, so HasAuthority was
        //   false for every combatant and no bullet was ever spawned on any topology.
        //   The shape the suite never built is the shape production always has.

        /// <summary>
        /// ⭐ THE RAIL THAT WOULD HAVE CAUGHT CE-198. A ghost shooter carrying the Muscle's real
        /// production shape — the unknown-owner sentinel <c>PrimaryOwnerId = -1</c> — still spawns
        /// a bullet, because the Muscle executes the shot the Brain ordered.
        /// </summary>
        [Fact]
        public void FireProcessing_SpawnsBullet_ForAGhostShooterWithTheUnknownOwnerSentinel()
        {
            var shooter = SpawnShooter(Vector3.Zero);
            // Exactly what EntityMasterIngressTranslator writes for a ghost on the Muscle.
            _world.AddComponent(shooter, new NetworkAuthority(primaryOwnerId: -1, localNodeId: 1));
            var target = SpawnTarget(new Vector3(10f, 0f, 0f));

            PublishIntent(shooter, target);
            _sys.Execute(_world, 0.016f);

            var q = _world.Query().With<BallisticProjectile>().Build();
            int bulletCount = 0;
            foreach (var _ in q) bulletCount++;
            Assert.Equal(1, bulletCount);

            _world.Bus.SwapBuffers();
            Assert.Equal(1, _world.Bus.Read<WeaponFireNotification>().Length);
        }

        /// <summary>
        /// A shooter owned by a DIFFERENT node also fires: the Brain that sent the order is the
        /// owner, and the Muscle that received it is not. Gating on ownership here is what broke
        /// the kill chain — see the comment block in <c>FireProcessingSystem</c>.
        /// </summary>
        [Fact]
        public void FireProcessing_SpawnsBullet_WhenAnotherNodeOwnsTheShooter()
        {
            var shooter = SpawnShooterNonAuthoritative(Vector3.Zero);
            var target  = SpawnTarget(new Vector3(10f, 0f, 0f));

            PublishIntent(shooter, target);
            _sys.Execute(_world, 0.016f);

            var q = _world.Query().With<BallisticProjectile>().Build();
            int bulletCount = 0;
            foreach (var _ in q) bulletCount++;
            Assert.Equal(1, bulletCount);
        }

        /// <summary>
        /// The self-owned case (AllInOne / a locally created entity) is unchanged.
        /// </summary>
        [Fact]
        public void FireProcessing_SpawnsBullet_WhenShooterIsAuthoritative()
        {
            var entity = _world.CreateEntity();
            _world.AddComponent(entity, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });
            _world.AddComponent(entity, new WeaponState { MuzzleVelocity = 800f, Ammo = 10 });
            _world.AddComponent(entity, new NetworkAuthority(primaryOwnerId: 1, localNodeId: 1));

            var target = SpawnTarget(new Vector3(10f, 0f, 0f));

            PublishIntent(entity, target);
            _sys.Execute(_world, 0.016f);

            var q = _world.Query().With<BallisticProjectile>().Build();
            int bulletCount = 0;
            foreach (var _ in q) bulletCount++;
            Assert.Equal(1, bulletCount);
        }

        /// <summary>
        /// ⭐ <c>CE-3236</c> (B-14, §7.15 VC-2/VC-7) — a canister fired fresh carries its warhead's burn; one thrown back carries the burn the
        /// intent names (what it had left in the hand). ✅ Red-proof: the intent's burn ignored ⇒ the thrown one burns 90 s again ⇒ red.
        /// </summary>
        [Fact]
        public void CE3236_ACanister_FliesWithItsBurn_AThrownBackOneWithWhatItHadLeft()
        {
            const long Canister = 9601;
            _world.RegisterComponent<TkbIdentity>();
            var db = new Fdp.Toolkit.Tkb.TkbDatabase();
            var round = new Fdp.Interfaces.TkbTemplate("test gas canister", Canister);
            round.AddDescriptor(new Fdp.Toolkit.Tkb.Domain.WarheadDto { Kind = Fdp.Toolkit.Tkb.Domain.WarheadKind.Irritant, CloudRadiusM = 8f, CloudSeconds = 90f });
            db.Register(round);
            _world.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(db);
            var fresh  = SpawnShooter(Vector3.Zero);
            var thrown = SpawnShooter(new Vector3(0, 50, 0));
            var target = SpawnTarget(new Vector3(100, 0, 0));

            _world.Bus.Publish(new WeaponFireIntent { Shooter = fresh,  Target = target, AmmoGuid = Canister });
            _world.Bus.Publish(new WeaponFireIntent { Shooter = thrown, Target = target, AmmoGuid = Canister, BurnSeconds = 33f });
            _world.Bus.SwapBuffers();
            _sys.Execute(_world, 0.016f);

            var burn = new System.Collections.Generic.Dictionary<Entity, (float Burn, bool Vents)>();
            foreach (var e in _world.Query().With<BallisticProjectile>().Build())
            {
                var bp = _world.GetComponent<BallisticProjectile>(e);
                burn[bp.Shooter] = (bp.BurnSeconds, (bp.Warhead & WarheadRound.Vents) != 0);
            }
            Assert.Equal((90f, true), burn[fresh]);
            Assert.Equal((33f, true), burn[thrown]);
        }

        /// <summary>
        /// ⭐ <c>CE-3071</c> — the bullet carries the FIRED mount's munition, read from the TKB by the shooter's type and the
        /// request's WeaponIndex (mount 1 = the TOW here). A shooter with no TKB numbers fires an unknown munition: the flat
        /// default damage and penetration 0.
        /// </summary>
        [Fact]
        public void CE3071_TheBullet_CarriesTheFiredMountsMunition()
        {
            _world.RegisterComponent<TkbIdentity>();
            var db = new Fdp.Toolkit.Tkb.TkbDatabase();
            var t  = new Fdp.Interfaces.TkbTemplate("Bradley", 101);
            t.AddDescriptor(new Fdp.Toolkit.Tkb.Domain.WeaponSuiteDto { Mounts =
            {
                new Fdp.Toolkit.Tkb.Domain.WeaponMountDto { Penetration = 60,  DamagePerHit = 60 },
                new Fdp.Toolkit.Tkb.Domain.WeaponMountDto { Penetration = 800, DamagePerHit = 2000 },
            } });
            db.Register(t);
            _world.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(db);

            var bradley = SpawnShooter(Vector3.Zero);
            _world.AddComponent(bradley, new TkbIdentity { TkbType = 101 });
            var plain   = SpawnShooter(new Vector3(0, 50, 0));
            var target  = SpawnTarget(new Vector3(100, 0, 0));

            _world.Bus.Publish(new WeaponFireIntent { Shooter = bradley, Target = target, WeaponIndex = 1 });
            _world.Bus.Publish(new WeaponFireIntent { Shooter = plain,   Target = target, WeaponIndex = 0 });
            _world.Bus.SwapBuffers();
            _sys.Execute(_world, 0.016f);

            var bullets = new System.Collections.Generic.Dictionary<Entity, BallisticProjectile>();
            foreach (var e in _world.Query().With<BallisticProjectile>().Build())
            {
                var bp = _world.GetComponent<BallisticProjectile>(e);
                bullets[bp.Shooter] = bp;
            }
            Assert.Equal(800f,  bullets[bradley].Penetration);
            Assert.Equal(2000f, bullets[bradley].Damage);
            Assert.Equal(0f,    bullets[plain].Penetration);
            Assert.Equal(CombatConstants.DefaultBulletDamage, bullets[plain].Damage);
        }

        /// <summary>
        /// ⭐⭐ M8 (D-I) — a mount with NO loadout fires its one round exactly as before (the intent names none: the mount's
        /// <c>AmmoGuid</c>, penetration and damage); the intent's LOADED round is fired with ITS numbers — HE-FRAG: 50 mm, 600, and its
        /// warhead (the round is an area round).
        /// </summary>
        [Fact]
        public void M8_AMountWithNoLoadout_FiresItsOneRound_AsBefore_AndTheLoadedRound_IsFiredWithItsNumbers()
        {
            _world.RegisterComponent<TkbIdentity>();
            var db = new Fdp.Toolkit.Tkb.TkbDatabase();
            M8Rounds.Register(db);
            var t = new Fdp.Interfaces.TkbTemplate("Tank", M8Rounds.TankGunType);
            t.AddDescriptor(new Fdp.Toolkit.Tkb.Domain.WeaponSuiteDto { Mounts = { M8Rounds.TankGun(withLoadout: false) } });
            db.Register(t);
            _world.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(db);

            var asBefore = SpawnShooter(Vector3.Zero);
            _world.AddComponent(asBefore, new TkbIdentity { TkbType = M8Rounds.TankGunType });
            var loaded = SpawnShooter(new Vector3(0, 50, 0));
            _world.AddComponent(loaded, new TkbIdentity { TkbType = M8Rounds.TankGunType });
            var target = SpawnTarget(new Vector3(100, 0, 0));

            _world.Bus.Publish(new WeaponFireIntent { Shooter = asBefore, Target = target });
            _world.Bus.Publish(new WeaponFireIntent { Shooter = loaded,   Target = target, AmmoGuid = M8Rounds.He });
            _world.Bus.SwapBuffers();
            _sys.Execute(_world, 0.016f);

            var bullets = new System.Collections.Generic.Dictionary<Entity, BallisticProjectile>();
            foreach (var e in _world.Query().With<BallisticProjectile>().Build())
            {
                var bp = _world.GetComponent<BallisticProjectile>(e);
                bullets[bp.Shooter] = bp;
            }
            Assert.Equal(M8Rounds.Apfsds, bullets[asBefore].Ammo);
            Assert.Equal(600f,  bullets[asBefore].Penetration);
            Assert.Equal(1100f, bullets[asBefore].Damage);
            Assert.Equal(0, bullets[asBefore].Warhead & WarheadRound.Area);
            Assert.Equal(M8Rounds.He, bullets[loaded].Ammo);
            Assert.Equal(50f,  bullets[loaded].Penetration);
            Assert.Equal(600f, bullets[loaded].Damage);
            Assert.NotEqual(0, bullets[loaded].Warhead & WarheadRound.Area);
        }

        /// <summary>⭐ LR-1 + LR-2 (Hrot docs/DESIGN_Visual_Effects.md §8.5, R-271) — a mount with a tracer MIX fires its tracer munition
        /// one shot in <c>TracerEvery</c>; the tracer round (its munition has a look) becomes a lightweight replicated entity —
        /// <c>TkbIdentity</c> = the munition + <c>LightweightRound</c>; an ordinary ball round gets neither. The mix changes WHAT the round
        /// is, never its damage or penetration (both stay the mount's).</summary>
        [Fact]
        public void LR1_ATracerMixedOneInFive_IsALightweightRound_BallStaysLocal_DamageUnchanged()
        {
            _world.RegisterComponent<TkbIdentity>();
            _world.RegisterComponent<LightweightRound>();
            _world.RegisterComponent<ShotOrdinal>();
            const long Ball = 6003, Tracer = 6004;
            var db = new Fdp.Toolkit.Tkb.TkbDatabase();
            var rifleman = new Fdp.Interfaces.TkbTemplate("Rifleman", 101);
            rifleman.AddDescriptor(new Fdp.Toolkit.Tkb.Domain.WeaponSuiteDto { Mounts =
            {
                new Fdp.Toolkit.Tkb.Domain.WeaponMountDto { Penetration = 5, DamagePerHit = 25, AmmoGuid = Ball, TracerAmmoGuid = Tracer, TracerEvery = 5 },
            } });
            db.Register(rifleman);
            db.Register(new Fdp.Interfaces.TkbTemplate("5.56x45 ball", Ball));
            var tracer = new Fdp.Interfaces.TkbTemplate("5.56x45 tracer", Tracer) { DisType = new DISEntityType { Kind = 2, Domain = 8 } };
            tracer.AddDescriptor(new Fdp.Toolkit.Tkb.Domain.MunitionVisualDto { Shape = Fdp.Toolkit.Tkb.Domain.MunitionShape.Bullet, Tracer = true });
            db.Register(tracer);
            _world.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(db);

            var shooter = SpawnShooter(Vector3.Zero);
            _world.AddComponent(shooter, new TkbIdentity { TkbType = 101 });
            var target = SpawnTarget(new Vector3(100, 0, 0));
            var types = new System.Collections.Generic.List<long>();
            for (int i = 0; i < 10; i++)
            {
                PublishIntent(shooter, target);
                _sys.Execute(_world, 0.016f);
            }
            int lightweight = 0;
            foreach (var e in _world.Query().With<BallisticProjectile>().Build())
            {
                var bp = _world.GetComponent<BallisticProjectile>(e);
                types.Add(bp.Ammo);
                Assert.Equal(25f, bp.Damage);
                Assert.Equal(5f, bp.Penetration);
                bool isTracer = bp.Ammo == Tracer;
                Assert.Equal(isTracer, _world.HasComponent<LightweightRound>(e));
                Assert.Equal(isTracer, _world.HasComponent<TkbIdentity>(e));
                if (isTracer)
                {
                    lightweight++;
                    Assert.Equal(Tracer, _world.GetComponent<TkbIdentity>(e).TkbType);
                    Assert.Equal(2, _world.GetDisType(e).Kind);
                }
            }
            Assert.Equal(10, types.Count);
            Assert.Equal(2, lightweight);                                            // shots 5 and 10
            Assert.Equal(8, types.FindAll(t => t == Ball).Count);
        }

        /// <summary>⭐ <c>CE-3089</c> (G7, W5) — the bullet flies at the FIRED mount's muzzle velocity (TKB); mount 0, or a mount that
        /// declares none, keeps the shooter's WeaponState velocity. ✅ Red-proof: read <c>weapon.MuzzleVelocity</c> only ⇒ 800, red.</summary>
        [Fact]
        public void CE3089_TheBullet_FliesAtTheFiredMountsMuzzleVelocity()
        {
            _world.RegisterComponent<TkbIdentity>();
            var db = new Fdp.Toolkit.Tkb.TkbDatabase();
            var t  = new Fdp.Interfaces.TkbTemplate("Bradley", 101);
            t.AddDescriptor(new Fdp.Toolkit.Tkb.Domain.WeaponSuiteDto { Mounts =
            {
                new Fdp.Toolkit.Tkb.Domain.WeaponMountDto { Penetration = 60,  DamagePerHit = 60 },
                new Fdp.Toolkit.Tkb.Domain.WeaponMountDto { Penetration = 800, DamagePerHit = 2000, MuzzleVelocity = 300f },
            } });
            db.Register(t);
            _world.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(db);

            var tow = SpawnShooter(Vector3.Zero, muzzleVelocity: 800f);
            _world.AddComponent(tow, new TkbIdentity { TkbType = 101 });
            var gun = SpawnShooter(new Vector3(0, 50, 0), muzzleVelocity: 800f);
            _world.AddComponent(gun, new TkbIdentity { TkbType = 101 });
            var target = SpawnTarget(new Vector3(100, 0, 0));

            _world.Bus.Publish(new WeaponFireIntent { Shooter = tow, Target = target, WeaponIndex = 1 });
            _world.Bus.Publish(new WeaponFireIntent { Shooter = gun, Target = target, WeaponIndex = 0 });
            _world.Bus.SwapBuffers();
            _sys.Execute(_world, 0.016f);

            var speed = new System.Collections.Generic.Dictionary<Entity, float>();
            foreach (var e in _world.Query().With<BallisticProjectile>().With<SimVelocity>().Build())
                speed[_world.GetComponent<BallisticProjectile>(e).Shooter] = _world.GetComponent<SimVelocity>(e).Linear.Length();
            Assert.Equal(300f, speed[tow], 2);
            Assert.Equal(800f, speed[gun], 2);
        }

        // ── E4 (docs/DESIGN_Visual_Effects.md VE-K, §6b) — the round leaves from the posed barrel ─────────────────────────────

        /// <summary>A tank type with one turret (pivot −0.5, 1.9 m up, trunnion 1.6 m ahead of it) and a 5 m barrel on it; the turret
        /// part is built and its pose set by the caller.</summary>
        private (Entity Tank, Entity Turret) TurretedTank(Vector3 at, float azimuth, float elevation)
        {
            _world.RegisterComponent<Fdp.Toolkit.Replication.Components.TkbIdentity>();
            _world.RegisterComponent<Fdp.Toolkit.Replication.Components.PartMetadata>();
            _world.RegisterComponent<TurretPose>();
            var db = new Fdp.Toolkit.Tkb.TkbDatabase();
            var t = new Fdp.Interfaces.TkbTemplate("Tank", 77);
            t.AddDescriptor(new Fdp.Toolkit.Tkb.Domain.TurretSuiteDto { Turrets =
                { new Fdp.Toolkit.Tkb.Domain.TurretDto { PivotX = -0.5f, PivotZ = 1.9f, TrunnionX = 1.6f, ElevationMaxDeg = 30f } } });
            t.AddDescriptor(new Fdp.Toolkit.Tkb.Domain.WeaponSuiteDto { Mounts =
                { new Fdp.Toolkit.Tkb.Domain.WeaponMountDto { TurretIndex = 0, MuzzleOffsetX = 5f, MuzzleVelocity = 1500f } } });
            db.Register(t);
            _world.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(db);
            var tank = SpawnShooter(at, 1500f);
            _world.AddComponent(tank, new Fdp.Toolkit.Replication.Components.TkbIdentity { TkbType = 77 });
            var turret = TurretParts.Ensure(_world, tank, 0);
            _world.SetComponent(turret, new TurretPose { Azimuth = azimuth, Elevation = elevation });
            return (tank, turret);
        }

        /// <summary>⭐ E4 — the trunnion-then-pivot posing: turret turned 90° left and the gun 10° up puts the muzzle LEFT of the hull, 5 m
        /// out from a trunnion that itself swung left, and 5·sin 10° up.</summary>
        [Fact]
        public void E4_TheMuzzle_IsTheBarrelTip_PosedByTheTurret()
        {
            var (tank, _) = TurretedTank(new Vector3(100f, 50f, 0f), MathF.PI / 2f, 10f * MathF.PI / 180f);
            Assert.True(Muzzle.TryOf(_world, tank, 0, out var muzzle));
            float reach = 1.6f + 5f * MathF.Cos(10f * MathF.PI / 180f);   // trunnion + the barrel's horizontal reach, from the pivot axis
            AssertNear(new Vector3(100f - 0.5f, 50f + reach, 1.9f + 5f * MathF.Sin(10f * MathF.PI / 180f)), muzzle);

            var rifleman = SpawnShooter(Vector3.Zero);
            Assert.False(Muzzle.TryOf(_world, rifleman, 0, out _));   // no muzzle data: the caller keeps its own answer
        }

        /// <summary>⭐ E4 — the round of a turreted type leaves from that point (the flash and the tracer use the same one), flying to the
        /// target from there; a target closer than twice the barrel's distance keeps today's start (CE-3059's half-way clamp).</summary>
        [Fact]
        public void E4_ATurretedRound_LeavesFromTheBarrel_ButNotPastHalfWay()
        {
            var (tank, _) = TurretedTank(Vector3.Zero, 0f, 0f);
            var far = SpawnTarget(new Vector3(400f, 0f, 0f));
            PublishIntent(tank, far);
            _sys.Execute(_world, 0.016f);
            Assert.True(Muzzle.TryOf(_world, tank, 0, out var barrel));
            Vector3? start = null;
            foreach (var e in _world.Query().With<BallisticProjectile>().Build()) start = _world.GetComponent<SimTransform>(e).Position;
            Assert.NotNull(start);
            AssertNear(barrel, start!.Value);
            AssertNear(new Vector3(6.1f, 0f, 1.9f), barrel);

            foreach (var e in _world.Query().With<BallisticProjectile>().Build()) _world.DestroyEntity(e);
            var close = SpawnTarget(new Vector3(8f, 0f, 0f));   // the muzzle (~6 m from the eye) is past half way to it
            _world.Bus.SwapBuffers();
            PublishIntent(tank, close);
            _sys.Execute(_world, 0.016f);
            foreach (var e in _world.Query().With<BallisticProjectile>().Build())
                Assert.True(Vector3.Distance(_world.GetComponent<SimTransform>(e).Position, barrel) > 1f, "a close target: today's start, not the barrel");
        }
    }
}
