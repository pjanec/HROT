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
    }
}
