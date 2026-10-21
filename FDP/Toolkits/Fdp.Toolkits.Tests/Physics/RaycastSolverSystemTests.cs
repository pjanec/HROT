using System;
using System.Numerics;
using CarKinem.Spatial;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Physics.Components;
using Fdp.Toolkit.Physics.Systems;
using Xunit;

namespace Fdp.Toolkit.Physics.Tests
{
    /// <summary>
    /// Unit tests for <see cref="RaycastSolverSystem"/> (BCS-P4-T3).
    /// Each test uses a real <see cref="SpatialHashGrid"/> to exercise the broadphase path.
    /// Tests use the full event pipeline: publish RaycastRequestEvent, swap, solve, playback,
    /// swap, materialize — then check the ring buffer slot.
    /// </summary>
    public class RaycastSolverSystemTests : IDisposable
    {
        private readonly EntityRepository _world;
        private SpatialHashGrid _grid;

        public RaycastSolverSystemTests()
        {
            _world = PhysicsTestWorldFactory.Create();
            _grid  = PhysicsTestWorldFactory.CreateTestGrid();
        }

        public void Dispose()
        {
            _grid.Dispose();
            PhysicsTestWorldFactory.DisposeBatch(_world);
        }

        // ── Helpers ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Creates an entity at <paramref name="pos2D"/> (Z=0) with a
        /// <see cref="PhysicsCollider"/> and a <see cref="SimTransform"/>,
        /// adds it to the grid, then publishes the grid as the
        /// <see cref="SpatialGridData"/> singleton on the world.
        /// </summary>
        private Entity SpawnCollider(Vector2 pos2D, float radius, int layer)
        {
            var entity = _world.CreateEntity();
            _world.AddComponent(entity, new SimTransform
            {
                Position = new Vector3(pos2D.X, pos2D.Y, 0f),
                Rotation = Quaternion.Identity,
            });
            _world.AddComponent(entity, new SimVelocity());
            _world.AddComponent(entity, new PhysicsCollider { Radius = radius, CollisionLayer = layer });

            // Add to spatial grid so broadphase can find it.
            _grid.Add(entity, pos2D);

            // Publish updated grid as singleton.
            _world.SetSingleton(new SpatialGridData { Grid = _grid });

            return entity;
        }

        /// <summary>
        /// Publishes a <see cref="RaycastRequestEvent"/>, runs the full solver pipeline,
        /// and returns the <see cref="RaycastHit"/> from the ring buffer slot for this request.
        /// </summary>
        private RaycastHit RunSolver(RaycastRequestEvent req)
        {
            var view = (ISimulationView)_world;

            // Publish request to the write buffer.
            _world.Bus.Publish(req);

            // Swap: requests are now in the read buffer for the solver.
            _world.Bus.SwapBuffers();

            // Solve: reads events, writes results to cmd buffer.
            var sys = new RaycastSolverSystem();
            sys.Execute(view, 0.016f);

            // Playback: result events move from cmd buffer to the bus write buffer.
            var ecb = (EntityCommandBuffer)view.GetCommandBuffer();
            ecb.Playback(_world);

            // Swap: result events are now in the read buffer for materialization.
            _world.Bus.SwapBuffers();

            // Materialize: writes results into the RaycastBatchData ring buffer.
            new RaycastResultMaterializationSystem().Execute(view, 0.016f);

            // Return the ring buffer slot for this request.
            int slot = (int)((uint)req.RayId % (uint)PhysicsConstants.RaycastBatchCapacity);
            return _world.GetSingleton<RaycastBatchData>().Hits[slot];
        }

        // ── Test 1 ────────────────────────────────────────────────────────────────

        /// <summary>
        /// A ray that crosses an entity's bounding circle must produce a hit result
        /// with <c>HasHit == 1</c> and <c>HitEntity</c> equal to the spawned entity.
        /// </summary>
        [Fact]
        public void RaycastSolver_DetectsHit_WhenBulletPathCrossesCollider()
        {
            // Arrange: entity at (5,0), radius 1, layer bit 0.
            var entity = SpawnCollider(new Vector2(5f, 0f), radius: 1f, layer: 1);

            var hit = RunSolver(new RaycastRequestEvent
            {
                Start     = new Vector3(-5f, 0f, 0f),
                End       = new Vector3(10f, 0f, 0f),
                RayId     = PhysicsConstants.PackBulletRayId(99),
                LayerMask = 1,
            });

            Assert.Equal(1, hit.HasHit);
            Assert.Equal(entity, hit.HitEntity);
        }

        /// <summary>A PERSON (a pedestrian-class unit) in <paramref name="stance"/>, standing on the ground at (10, 0).</summary>
        private Entity SpawnPerson(Hrot.MuscleCharacter.Animation.Components.StanceIntent? stance)
        {
            if (!_world.IsComponentTypeRegistered<global::CarKinem.Core.VehicleParams>()) _world.RegisterComponent<global::CarKinem.Core.VehicleParams>();
            if (!_world.IsComponentTypeRegistered<Hrot.MuscleCharacter.Animation.Components.StanceIntent>())
                _world.RegisterComponent<Hrot.MuscleCharacter.Animation.Components.StanceIntent>();
            var e = SpawnCollider(new Vector2(10f, 0f), radius: 0.4f, layer: 1);
            _world.AddComponent(e, new global::CarKinem.Core.VehicleParams { Class = global::CarKinem.Core.VehicleClass.Pedestrian });
            if (stance is { } s) _world.AddComponent(e, s);
            return e;
        }

        private RaycastHit Shoot(float atHeight, bool bullet = true) => RunSolver(new RaycastRequestEvent
        {
            Start     = new Vector3(0f, 0f, atHeight),
            End       = new Vector3(20f, 0f, atHeight),
            RayId     = bullet ? PhysicsConstants.PackBulletRayId(77) : PhysicsConstants.PackLosRayId(1, 2),
            LayerMask = 1,
        });

        /// <summary>
        /// ⭐⭐ <c>CE-3136</c> P-2 (peek-and-fire D2) — a ROUND hits a person only inside the body band of its LOGICAL stance (feet …
        /// highest body point): a standing man is hit at 1.2 m (⭐ CE-3209: his torso — at 0.9 m the round now passes between his legs)
        /// and missed at 2.5 m; a PRONE man (top ≈ 0.3 m) is missed at 0.9 m —
        /// the round through a window passes over a man lying below the sill — and hit at 0.2 m. 🔴 Red-proof: the circle alone
        /// hit at every height. ⭐ A sight/query ray keeps the circle (any height), and so does a vehicle.
        /// </summary>
        [Fact]
        public void P2_ARoundHitsAPersonOnlyInsideTheBodyBandOfItsStance()
        {
            var standing = SpawnPerson(null);
            Assert.Equal(standing, Shoot(1.2f).HitEntity);
            Assert.Equal(0, Shoot(2.5f).HasHit);
            Assert.Equal(1, Shoot(2.5f, bullet: false).HasHit);   // a sight ray: unchanged

            _world.AddComponent(standing, new Hrot.MuscleCharacter.Animation.Components.StanceIntent
                { TargetStance = Fdp.Toolkit.Tkb.Domain.StanceId.Prone });
            Assert.Equal(0, Shoot(0.9f).HasHit);                   // over a prone man
            Assert.Equal(standing, Shoot(0.2f).HitEntity);
        }

        /// <summary>
        /// ⭐⭐ M1 (DESIGN_Armour_Hit_Model §2, a decided change — replaces <c>P2_AVehicleKeepsTheCircle</c>) — a ROUND hits a vehicle
        /// only inside its height band (feet … feet + collider Height): a round at 1 m strikes a 2.4 m hull, one at 5 m passes OVER
        /// it, and one dropping straight down meets its roof (a segment wholly inside the circle — the circle test alone never saw
        /// it). A sight ray keeps the circle; a collider of Height 0 (unknown) keeps the circle too.
        /// </summary>
        [Fact]
        public void M1_ARoundPassingOverAHull_DoesNotHitIt()
        {
            var hull = SpawnCollider(new Vector2(10f, 0f), radius: 2f, layer: 1);
            _world.GetComponentRW<PhysicsCollider>(hull).Height = 2.4f;
            Assert.Equal(hull, Shoot(1f).HitEntity);
            Assert.Equal(0, Shoot(5f).HasHit);
            Assert.Equal(1, Shoot(5f, bullet: false).HasHit);   // a sight ray: unchanged
            var drop = RunSolver(new RaycastRequestEvent
            {
                Start = new Vector3(10.5f, 0f, 10f), End = new Vector3(10.5f, 0f, 0f),
                RayId = PhysicsConstants.PackBulletRayId(78), LayerMask = 1,
            });
            Assert.Equal(hull, drop.HitEntity);
            Assert.Equal(0.76f, drop.T, 3);                        // where the drop reaches the roof (z 2.4)

            _world.GetComponentRW<PhysicsCollider>(hull).Height = 0f;
            Assert.Equal(hull, Shoot(5f).HitEntity);               // unknown height: the circle, as before
        }

        // ── Test 2 ────────────────────────────────────────────────────────────────

        /// <summary>
        /// With no entities in the world, every ray must return <c>HasHit == 0</c>.
        /// </summary>
        [Fact]
        public void RaycastSolver_ReturnsNoHit_WhenNoEntitiesInPath()
        {
            // No entities -- just publish an empty grid singleton.
            _world.SetSingleton(new SpatialGridData { Grid = _grid });

            var hit = RunSolver(new RaycastRequestEvent
            {
                Start     = new Vector3(-5f, 0f, 0f),
                End       = new Vector3( 5f, 0f, 0f),
                RayId     = PhysicsConstants.PackBulletRayId(0),
                LayerMask = 1,
            });

            Assert.Equal(0, hit.HasHit);
        }

        // ── Test 3 ────────────────────────────────────────────────────────────────

        /// <summary>
        /// An entity on <c>CollisionLayer = 2</c> (bit 1) must not be hit by a ray with
        /// <c>LayerMask = 1</c> (bit 0) because the bitmask AND is zero.
        /// </summary>
        [Fact]
        public void RaycastSolver_RespectsLayerMask()
        {
            // Entity on layer 2 (bit 1). Ray uses mask 1 (bit 0) -- no shared bits.
            SpawnCollider(new Vector2(5f, 0f), radius: 1f, layer: 2);

            var hit = RunSolver(new RaycastRequestEvent
            {
                Start     = new Vector3(-5f, 0f, 0f),
                End       = new Vector3(10f, 0f, 0f),
                RayId     = PhysicsConstants.PackBulletRayId(1),
                LayerMask = 1,  // bit 0 -- does not overlap with CollisionLayer=2 (bit 1)
            });

            Assert.Equal(0, hit.HasHit);
        }

        // ── Test 4 ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Setting <c>IgnoreEntity</c> to the spawned entity must cause the
        /// solver to skip that entity (no hit), simulating a shooter ignoring itself.
        /// </summary>
        [Fact]
        public void RaycastSolver_IgnoresIgnoreEntity()
        {
            var entity = SpawnCollider(new Vector2(5f, 0f), radius: 1f, layer: 1);

            var hit = RunSolver(new RaycastRequestEvent
            {
                Start        = new Vector3(-5f, 0f, 0f),
                End          = new Vector3(10f, 0f, 0f),
                RayId        = PhysicsConstants.PackBulletRayId(2),
                LayerMask    = 1,
                IgnoreEntity = entity,  // exclude the only entity in the world
            });

            Assert.Equal(0, hit.HasHit);
        }

        /// <summary>
        /// ⭐ CE-1238 — a ROUND never meets what rides with its shooter: a crewed tube's gunner (embarked on the tube, standing at its origin)
        /// is not struck by the tube's own bomb; a man NOT riding with it is; and a SIGHT ray is not changed (it still sees the gunner).
        /// 📐 Live bt-mortar-roof: both 81 mm gunners were killed by their own bombs 0.14 s out of the muzzle.
        /// </summary>
        [Fact]
        public void CE1238_ARoundNeverMeetsWhatRidesWithItsShooter_ButOthersAndSightAreUnchanged()
        {
            if (!_world.IsComponentTypeRegistered<Fdp.Toolkit.Behavior.Components.IsEmbarkedTag>())
                _world.RegisterComponent<Fdp.Toolkit.Behavior.Components.IsEmbarkedTag>();
            var tube = _world.CreateEntity();   // the shooter: the weapon itself, no collider of its own here
            var gunner = SpawnCollider(new Vector2(5f, 0f), radius: 1f, layer: 1);
            _world.AddComponent(gunner, new Fdp.Toolkit.Behavior.Components.IsEmbarkedTag { VehicleEntity = tube, Exposed = 1 });
            var round = new RaycastRequestEvent
            {
                Start = new Vector3(-5f, 0f, 0f), End = new Vector3(10f, 0f, 0f),
                RayId = PhysicsConstants.PackBulletRayId(4), LayerMask = 1, IgnoreEntity = tube,
            };
            Assert.Equal(0, RunSolver(round).HasHit);   // his own tube's round passes him

            var sight = round with { RayId = PhysicsConstants.PackLosRayId(1, 2) };
            Assert.Equal(1, RunSolver(sight).HasHit);   // sight still meets him

            var stranger = SpawnCollider(new Vector2(7f, 0f), radius: 1f, layer: 1);
            var hit = RunSolver(round with { RayId = PhysicsConstants.PackBulletRayId(5) });
            Assert.Equal((1, stranger), (hit.HasHit, hit.HitEntity));   // anyone else is met as before
        }

        // ── Test 5 ────────────────────────────────────────────────────────────────

        /// <summary>
        /// When two entities lie along the ray, the solver must return the one closest
        /// to the ray origin (smallest t).
        /// </summary>
        [Fact]
        public void RaycastSolver_ReturnsClosestHit_WhenMultipleInPath()
        {
            // Two entities along the X-axis; near one at x=3, far one at x=7.
            var nearEntity = SpawnCollider(new Vector2(3f, 0f), radius: 0.5f, layer: 1);
            var farEntity  = SpawnCollider(new Vector2(7f, 0f), radius: 0.5f, layer: 1);

            var hit = RunSolver(new RaycastRequestEvent
            {
                Start     = new Vector3(0f, 0f, 0f),
                End       = new Vector3(10f, 0f, 0f),
                RayId     = PhysicsConstants.PackBulletRayId(3),
                LayerMask = 1,
            });

            Assert.Equal(1, hit.HasHit);
            // Closest entity (at x=3) must be selected, not the farther one (x=7).
            Assert.Equal(nearEntity, hit.HitEntity);
            Assert.NotEqual(farEntity, hit.HitEntity);
        }

        // ── Test 6: SourceNodeId propagation ─────────────────────────────────────

        /// <summary>
        /// Verifies that <see cref="RaycastSolverSystem"/> copies
        /// <see cref="RaycastRequestEvent.SourceNodeId"/> verbatim into the corresponding
        /// <see cref="RaycastHit.SourceNodeId"/>.
        /// </summary>
        [Fact]
        public void RaycastSolver_PropagatesSourceNodeId_ToHit()
        {
            // Arrange: entity at (5,0) so we get a real hit; SourceNodeId = 7.
            SpawnCollider(new Vector2(5f, 0f), radius: 1f, layer: 1);

            var hit = RunSolver(new RaycastRequestEvent
            {
                Start        = new Vector3(-5f, 0f, 0f),
                End          = new Vector3(10f, 0f, 0f),
                RayId        = PhysicsConstants.PackBulletRayId(1),
                LayerMask    = 1,
                SourceNodeId = 7,
            });

            Assert.Equal(7, hit.SourceNodeId);
        }

        /// <summary>
        /// SourceNodeId is propagated even on a miss (HasHit == 0), so the
        /// solver-egress translator can route the empty hit record back to the correct Brain.
        /// </summary>
        [Fact]
        public void RaycastSolver_PropagatesSourceNodeId_OnMiss()
        {
            // No entities -- ray will miss.
            _world.SetSingleton(new SpatialGridData { Grid = _grid });

            var hit = RunSolver(new RaycastRequestEvent
            {
                Start        = new Vector3(-5f, 0f, 0f),
                End          = new Vector3(5f, 0f, 0f),
                RayId        = PhysicsConstants.PackBulletRayId(2),
                LayerMask    = 1,
                SourceNodeId = 42,
            });

            Assert.Equal(0, hit.HasHit);
            Assert.Equal(42, hit.SourceNodeId);
        }
    
        // ── CE-2182 (AT1, R-2021) — a round has its OWN collision bit ───────────────────────────────────────────────────────────

        /// <summary>The ray a round casts: everything but the round's own bit (<c>BallisticsSystem</c>'s mask).</summary>
        private RaycastHit FireRound(Vector3 from, Vector3 to) => RunSolver(new RaycastRequestEvent
        {
            Start     = from,
            End       = to,
            RayId     = PhysicsConstants.PackBulletRayId(2182),
            LayerMask = ~Fdp.Toolkit.Combat.CombatConstants.BulletCollisionLayer,
        });

        /// <summary>
        /// ⭐⭐ <c>CE-2182</c> (AT1, DESIGN_Air_Operations §7c) — a body on the COMBAT layer alone (every aircraft: its collider comes
        /// from the flight translator with no entity layer) is met by a round fired up at it. 🔴 Red-proof: the round's bit back on 2
        /// (the combat layer's bit) ⇒ the round's mask skips the helicopter ⇒ no hit.
        /// </summary>
        [Fact]
        public void CE2182_ARoundFiredUpAtAnAircraft_MeetsIt()
        {
            var heli = SpawnCollider(new Vector2(10f, 0f), radius: 1.2f, layer: Fdp.Toolkit.Combat.CombatConstants.CombatCollisionLayer);
            ref var tf = ref _world.GetComponentRW<SimTransform>(heli);
            tf.Position = new Vector3(10f, 0f, 60f);                      // 60 m up
            ref var col = ref _world.GetComponentRW<PhysicsCollider>(heli);
            col.Height = 3f;

            var hit = FireRound(new Vector3(0f, 0f, 1.5f), new Vector3(20f, 0f, 121.5f));   // climbing through (10, 0, 61.5)
            Assert.Equal(1, hit.HasHit);
            Assert.Equal(heli, hit.HitEntity);
        }

        /// <summary>⭐ <c>CE-2182</c> — rounds still never meet each other: a round sits on the round's bit, which its own mask skips.</summary>
        [Fact]
        public void CE2182_RoundsNeverMeetEachOther()
        {
            SpawnCollider(new Vector2(5f, 0f), radius: 0.5f, layer: Fdp.Toolkit.Combat.CombatConstants.BulletCollisionLayer);
            Assert.Equal(0, FireRound(new Vector3(0f, 0f, 0f), new Vector3(10f, 0f, 0f)).HasHit);
        }

        /// <summary>
        /// ⭐⭐ <c>CE-2182</c> — a WEAPON SYSTEM is not met by a round (Weapon Systems §6: it stands on its carrier or at its host's seat,
        /// a 2.5 m fallback collider with no height) — the man who carries it is. 🔴 Red-proof: without the skip the 2.5 m disc, now on
        /// a hittable layer, takes the round meant for him.
        /// </summary>
        [Fact]
        public void CE2182_AWeaponSystemOnItsCarrier_IsNotMet_TheManIs()
        {
            if (!_world.IsComponentTypeRegistered<Fdp.Toolkit.Combat.WeaponSystemParams>())
                _world.RegisterComponent<Fdp.Toolkit.Combat.WeaponSystemParams>();
            var man = SpawnPerson(null);                                                     // at (10, 0), radius 0.4
            var javelin = SpawnCollider(new Vector2(10f, 0f), radius: 2.5f, layer: Fdp.Toolkit.Combat.CombatConstants.CombatCollisionLayer);
            _world.AddComponent(javelin, new Fdp.Toolkit.Combat.WeaponSystemParams());

            Assert.Equal(man, FireRound(new Vector3(0f, 0f, 1f), new Vector3(20f, 0f, 1f)).HitEntity);
        }
        // ── ⭐⭐ CE-3209 (docs/designs/hit-location/DESIGN_Body_Parts_And_Hit_Location.md S1a) — a round meets a person's PARTS ─────────

        /// <summary>A round from x = 0 to x = 20 at (<paramref name="y"/>, <paramref name="z"/>) — across a man standing at (10, 0) facing +x,
        /// so it arrives on his BACK.</summary>
        private RaycastHit ShootAt(float y, float z, RaycastSolverSystem? sys = null) => FireRoundWith(sys, new Vector3(0f, y, z), new Vector3(20f, y, z));

        private RaycastHit FireRoundWith(RaycastSolverSystem? sys, Vector3 from, Vector3 to)
        {
            var view = (ISimulationView)_world;
            _world.Bus.Publish(new RaycastRequestEvent { Start = from, End = to, RayId = PhysicsConstants.PackBulletRayId(91), LayerMask = 1 });
            _world.Bus.SwapBuffers();
            (sys ?? new RaycastSolverSystem()).Execute(view, 0.016f);
            ((EntityCommandBuffer)view.GetCommandBuffer()).Playback(_world);
            _world.Bus.SwapBuffers();
            new RaycastResultMaterializationSystem().Execute(view, 0.016f);
            int slot = (int)((uint)PhysicsConstants.PackBulletRayId(91) % (uint)PhysicsConstants.RaycastBatchCapacity);
            return _world.GetSingleton<RaycastBatchData>().Hits[slot];
        }

        /// <summary>
        /// ⭐⭐ <c>CE-3209</c> (HL-2, HL-5) — a round meets the PART it crosses: at head height his Head, at chest height his Torso, through
        /// one thigh that Thigh; the face entered is his REAR for a round from behind and his FRONT for one from ahead. 🔴 Red-proof: with
        /// <c>PersonParts</c> off the hit carries no part (the old circle and band).
        /// </summary>
        [Fact]
        public void CE3209_ARound_MeetsThePartItCrosses_AndTheFaceItEntered()
        {
            var man = SpawnPerson(null);
            var head = ShootAt(0f, 1.6f);
            Assert.Equal((man, (byte)1, Fdp.Toolkit.Physics.BodyParts.PersonPart.Head), (head.HitEntity, head.HasBodyPart, head.BodyPart));
            Assert.Equal((byte)Fdp.Toolkit.Combat.Facing.Rear, head.Face);                 // from behind him
            Assert.Equal(Fdp.Toolkit.Physics.BodyParts.PersonPart.Torso, ShootAt(0f, 1.2f).BodyPart);
            Assert.Equal(Fdp.Toolkit.Physics.BodyParts.PersonPart.ThighL, ShootAt(0.11f, 0.68f).BodyPart);
            var ahead = FireRoundWith(null, new Vector3(20f, 0f, 1.2f), new Vector3(0f, 0f, 1.2f));   // from ahead of him
            Assert.Equal((Fdp.Toolkit.Physics.BodyParts.PersonPart.Torso, (byte)Fdp.Toolkit.Combat.Facing.Front), (ahead.BodyPart, ahead.Face));

            var old = ShootAt(0f, 1.6f, new RaycastSolverSystem { PersonParts = false });
            Assert.Equal((man, (byte)0), (old.HitEntity, old.HasBodyPart));
        }

        /// <summary>
        /// ⭐ Airborne insertion B-1 (ui steer, design §9 / D-12) — a man coming DOWN is still a PERSON target: a <c>Descent</c> makes his
        /// class Parachute (<c>Locomotions.Of</c>), but <c>IsPerson</c> keys on his <c>VehicleParams</c>, so a round through his torso under
        /// the canopy, 300 m up, meets his Torso. ⛔ Were <c>IsPerson</c> rewritten over <c>Locomotions.Of</c>, a paratrooper would be
        /// untouchable — this rail is the fence.
        /// </summary>
        [Fact]
        public void B1_AManUnderACanopy_IsStillAPerson_AndARoundThroughHisTorsoHitsTorso()
        {
            var man = SpawnPerson(null);
            if (!_world.IsComponentTypeRegistered<Fdp.Toolkit.Motion.Descent>()) _world.RegisterComponent<Fdp.Toolkit.Motion.Descent>();
            _world.AddComponent(man, new Fdp.Toolkit.Motion.Descent
                { Kind = Fdp.Toolkit.Motion.DescentKind.StaticLine, Phase = Fdp.Toolkit.Motion.DescentPhase.Canopy, Canopy = Fdp.Toolkit.Motion.CanopyKind.Round });
            _world.GetComponentRW<SimTransform>(man).Position = new Vector3(10f, 0f, 300f);
            Assert.Equal(Fdp.Toolkit.Tkb.Domain.LocomotionClass.Parachute, Fdp.Toolkit.Motion.Locomotions.Of(_world, man));
            Assert.True(PhysicsColliderReaders.IsPerson(_world, man));
            var hit = ShootAt(0f, 301.2f);
            Assert.Equal((man, Fdp.Toolkit.Physics.BodyParts.PersonPart.Torso), (hit.HitEntity, hit.BodyPart));
        }

        /// <summary>
        /// ⭐ <c>CE-3209</c> — the body has GAPS the old band did not: a round between a standing man's legs at knee height misses him, and so
        /// does one past his shoulder; a round over a PRONE man at 0.4 m misses, at 0.15 m meets his torso. 🔴 Red-proof: the circle + band
        /// hit the round between the legs.
        /// </summary>
        [Fact]
        public void CE3209_ARoundBetweenHisLegs_OrPastHisShoulder_MissesHim()
        {
            var man = SpawnPerson(null);
            Assert.Equal(0, ShootAt(0f, 0.5f).HasHit);                       // between the shins
            Assert.Equal(0, ShootAt(0.4f, 1.55f).HasHit);                    // past his shoulder, beside his head
            Assert.Equal(man, ShootAt(0f, 0.5f, new RaycastSolverSystem { PersonParts = false }).HitEntity);   // the old band hit it

            _world.AddComponent(man, new Hrot.MuscleCharacter.Animation.Components.StanceIntent { TargetStance = Fdp.Toolkit.Tkb.Domain.StanceId.Prone });
            Assert.Equal(0, ShootAt(0f, 0.4f).HasHit);
            Assert.Equal(Fdp.Toolkit.Physics.BodyParts.PersonPart.Torso, ShootAt(0f, 0.15f).BodyPart);
        }

        /// <summary>
        /// ⭐ <c>CE-3209</c> (HL-2 tier ②) — a round that passes 3 m from a man is rejected by his bounding SPHERE: no part is tested.
        /// </summary>
        [Fact]
        public void CE3209_TheBoundingSphere_RejectsARoundThatPassesBy_BeforeAnyPartTest()
        {
            SpawnPerson(null);
            var sys = new RaycastSolverSystem();
            Assert.Equal(0, ShootAt(3f, 1.2f, sys).HasHit);
            Assert.Equal((1L, 0L), (sys.SphereRejects, sys.PartTests));
            ShootAt(0f, 1.2f, sys);
            Assert.Equal(1L, sys.PartTests);
        }

        /// <summary>
        /// ⭐ <c>CE-3209</c> (HL-2 tier ①) — the cell walk finds the man on the line in a CROWD of 100 standing just off it (the old
        /// midpoint disc stopped at 64 candidates, silently), and counts no overflow.
        /// </summary>
        [Fact]
        public void CE3209_InACrowd_TheManOnTheLine_IsStillFound()
        {
            if (!_world.IsComponentTypeRegistered<global::CarKinem.Core.VehicleParams>()) _world.RegisterComponent<global::CarKinem.Core.VehicleParams>();
            for (int i = 0; i < 100; i++)
            {
                var e = SpawnCollider(new Vector2(5f + (i % 10) * 0.9f, 2.5f + (i / 10) * 0.5f), radius: 0.4f, layer: 1);
                _world.AddComponent(e, new global::CarKinem.Core.VehicleParams { Class = global::CarKinem.Core.VehicleClass.Pedestrian });
            }
            var man = SpawnPerson(null);                                       // at (10, 0), added last
            _world.SetSingleton(new SpatialGridData { Grid = _grid, MaxRadius = 0.4f });
            var sys = new RaycastSolverSystem();
            Assert.Equal(man, ShootAt(0f, 1.2f, sys).HitEntity);
            Assert.Equal(0L, sys.CandidateOverflows);
        }


        // ── ⭐⭐ CE-3211 (docs/designs/hit-location/DESIGN_Body_Parts_And_Hit_Location.md §9 S2) — a round meets a VEHICLE's parts ──────────

        private const long TankType = 9311;

        /// <summary>A tank (the Tank kit at its default 7.9 × 3.7 × 2.4 m, no TKB turret ⇒ the kit's own pivots) at (10, 0) facing +x, with
        /// its turret part child posed at <paramref name="azimuth"/> (radians, counter-clockwise).</summary>
        private Entity SpawnTank(float azimuth = 0f)
        {
            if (!_world.IsComponentTypeRegistered<Fdp.Toolkit.Replication.Components.TkbIdentity>()) _world.RegisterComponent<Fdp.Toolkit.Replication.Components.TkbIdentity>();
            if (!_world.IsComponentTypeRegistered<Fdp.Toolkit.Replication.Components.PartMetadata>()) _world.RegisterComponent<Fdp.Toolkit.Replication.Components.PartMetadata>();
            if (!_world.IsComponentTypeRegistered<Fdp.Toolkit.Combat.Components.TurretPose>()) _world.RegisterComponent<Fdp.Toolkit.Combat.Components.TurretPose>();
            var db = new Fdp.Toolkit.Tkb.TkbDatabase();
            var t = new Fdp.Interfaces.TkbTemplate("T-test tank", TankType) { DisType = new DISEntityType { Kind = 1, Domain = 1, Category = 1 } };
            t.AddDescriptor(new Fdp.Toolkit.Tkb.Domain.CombatPlatformDefDto { MaxHealth = 1000f, ArmorFront = 500f, ArmorSide = 80f, ArmorRear = 40f, ArmorTop = 30f });
            db.Register(t);
            _world.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(db);
            var tank = SpawnCollider(new Vector2(10f, 0f), radius: 4f, layer: 1);
            _world.GetComponentRW<PhysicsCollider>(tank).Height = 2.4f;
            _world.AddComponent(tank, new Fdp.Toolkit.Replication.Components.TkbIdentity { TkbType = TankType });
            var turret = Fdp.Toolkit.Combat.TurretParts.Ensure(_world, tank, 0);
            _world.GetComponentRW<Fdp.Toolkit.Combat.Components.TurretPose>(turret).Azimuth = azimuth;
            return tank;
        }

        /// <summary>A round from the tank's LEFT (+y) across it at (<paramref name="x"/>, <paramref name="z"/>).</summary>
        private RaycastHit FromLeft(float x, float z, RaycastSolverSystem? sys = null) => FireRoundWith(sys, new Vector3(x, 20f, z), new Vector3(x, -20f, z));

        /// <summary>
        /// ⭐⭐ <c>CE-3211</c> (S2-3, S2-4) — a round from the side meets the part it crosses: low, the left track (the running gear); waist high,
        /// the hull's side; high, the turret's side; and in the hull's REAR third, the engine. A round from behind enters the engine; from
        /// ahead, the hull's front. 🔴 Red-proof: with <c>VehicleParts</c> off the hit carries no part.
        /// </summary>
        [Fact]
        public void CE3211_ARound_MeetsTheTankPartItCrosses()
        {
            var tank = SpawnTank();
            var gear = FromLeft(10f, 0.4f);
            Assert.Equal((tank, (byte)1, Fdp.Toolkit.Physics.BodyParts.VehiclePart.RunningGearL), (gear.HitEntity, gear.HasBodyPart, gear.BodyPart));
            var hull = FromLeft(10f, 1.0f);
            Assert.Equal((Fdp.Toolkit.Physics.BodyParts.VehiclePart.Hull, (byte)Fdp.Toolkit.Combat.Facing.Side), (hull.BodyPart, hull.Face));
            Assert.Equal(Fdp.Toolkit.Physics.BodyParts.VehiclePart.Turret, FromLeft(9.45f, 1.8f).BodyPart);
            Assert.Equal(Fdp.Toolkit.Physics.BodyParts.VehiclePart.Engine, FromLeft(7f, 1.0f).BodyPart);

            var behind = FireRoundWith(null, new Vector3(-10f, 0f, 1.0f), new Vector3(30f, 0f, 1.0f));
            Assert.Equal((Fdp.Toolkit.Physics.BodyParts.VehiclePart.Engine, (byte)Fdp.Toolkit.Combat.Facing.Rear), (behind.BodyPart, behind.Face));
            var ahead = FireRoundWith(null, new Vector3(30f, 0f, 1.0f), new Vector3(-10f, 0f, 1.0f));
            Assert.Equal((Fdp.Toolkit.Physics.BodyParts.VehiclePart.Hull, (byte)Fdp.Toolkit.Combat.Facing.Front), (ahead.BodyPart, ahead.Face));

            var old = FromLeft(10f, 1.0f, new RaycastSolverSystem { VehicleParts = false });
            Assert.Equal((tank, (byte)0), (old.HitEntity, old.HasBodyPart));
        }

        /// <summary>
        /// ⭐ <c>CE-2217</c> — a round 30° off the tank's nose that meets the hull is scored on the FRONT arc, as the danger model predicts it
        /// (<c>ArmorModel.FacingOfArrival</c>: front within 60°): the armour is stated per arc, and the box face it entered (the side plate,
        /// struck at 60° obliquity) is not the side arc. 70° off the nose is the side. 📐 Swept on the M1 kit at 190 m before the fix:
        /// predicted Front, entered Side, from 16° to 58° off the nose — the live Javelin rated 0.11 and scored 3000 → 1500 on the side.
        /// 🔴 Red-proof: <c>arcFaces</c> off in the solver ⇒ Side ⇒ red.
        /// </summary>
        [Fact]
        public void CE2217_ARound30DegreesOffTheNose_IsScoredOnTheFrontArc_70DegreesOnTheSide()
        {
            var tank = SpawnTank();   // at (10, 0), facing +x
            foreach (var (deg, face) in new[] { (30f, Fdp.Toolkit.Combat.Facing.Front), (70f, Fdp.Toolkit.Combat.Facing.Side) })
            {
                float b = deg * MathF.PI / 180f;
                var from = new Vector3(10f, 0f, 1.0f) + new Vector3(MathF.Cos(b), MathF.Sin(b), 0f) * 30f;
                var hit = FireRoundWith(null, from, new Vector3(10f, 0f, 1.0f) - (from - new Vector3(10f, 0f, 1.0f)) * 0.2f);
                Assert.Equal((tank, (byte)face), (hit.HitEntity, hit.Face));
            }
        }

        /// <summary>
        /// ⭐⭐ <c>CE-3211</c> (S2-7, D-G lifted) — a turret TURNED toward a side shot shows that shot its FRONT; a round dropping onto it meets its
        /// roof (Top); a round just over the turret misses the tank the old height band would have hit.
        /// </summary>
        [Fact]
        public void CE3211_ATurnedTurret_ShowsASideShotItsFront_AndARoundOverItMisses()
        {
            var tank = SpawnTank(azimuth: MathF.PI / 2f);   // the turret faces the tank's left
            var side = FromLeft(9.45f, 1.6f);   // under the barrel, which now points at the shooter
            Assert.Equal((tank, Fdp.Toolkit.Physics.BodyParts.VehiclePart.Turret, (byte)Fdp.Toolkit.Combat.Facing.Front), (side.HitEntity, side.BodyPart, side.Face));
            var roof = FireRoundWith(null, new Vector3(9.45f, 0f, 10f), new Vector3(9.45f, 0f, -1f));
            Assert.Equal((Fdp.Toolkit.Physics.BodyParts.VehiclePart.Turret, (byte)Fdp.Toolkit.Combat.Facing.Top), (roof.BodyPart, roof.Face));
            Assert.Equal(Fdp.Toolkit.Physics.BodyParts.VehiclePart.Gun, FromLeft(9.45f, 1.85f).BodyPart);   // down the barrel's muzzle
            // the gun now points left: a round across its barrel, 3.5 m out from the pivot, meets the gun
            Assert.Equal(Fdp.Toolkit.Physics.BodyParts.VehiclePart.Gun,
                FireRoundWith(null, new Vector3(-10f, 3.5f, 1.87f), new Vector3(30f, 3.5f, 1.87f)).BodyPart);

            Assert.Equal(0, FromLeft(10f, 2.3f).HasHit);                                                 // over the hull and turret
            Assert.Equal(tank, FromLeft(10f, 2.3f, new RaycastSolverSystem { VehicleParts = false }).HitEntity);   // the old band hit it
        }

        /// <summary>⭐ <c>CE-3211</c> (S2-9) — a round 12 m from the tank is rejected by its bound sphere, before any box is tested.</summary>
        [Fact]
        public void CE3211_TheVehicleBoundSphere_RejectsARoundThatPassesBy()
        {
            SpawnTank();
            var sys = new RaycastSolverSystem();
            Assert.Equal(0, FireRoundWith(sys, new Vector3(22f, 20f, 1f), new Vector3(22f, -20f, 1f)).HasHit);
            Assert.Equal(0L, sys.PartTests);
            FromLeft(10f, 1.0f, sys);
            Assert.Equal(1L, sys.PartTests);
        }

        // ── ⭐⭐ CE-3214 (hit location §11 S2b) — a round meets a WEAPON SYSTEM's parts ─────────────────────────────────────────────

        private const long HmgType = 9314;

        /// <summary>An M2-like HMG (the Hmg kits at their default 1.65 × 0.9 × 0.75 m, no turret part) at (10, 0) facing +x, in
        /// <paramref name="deploy"/>; <paramref name="embarked"/> ⇒ aboard something (carried or in a station).</summary>
        private Entity SpawnHmg(Fdp.Toolkit.Combat.WeaponDeploy deploy, bool embarked = false)
        {
            if (!_world.IsComponentTypeRegistered<Fdp.Toolkit.Replication.Components.TkbIdentity>()) _world.RegisterComponent<Fdp.Toolkit.Replication.Components.TkbIdentity>();
            if (!_world.IsComponentTypeRegistered<Fdp.Toolkit.Combat.WeaponSystemParams>()) _world.RegisterComponent<Fdp.Toolkit.Combat.WeaponSystemParams>();
            if (!_world.IsComponentTypeRegistered<Fdp.Toolkit.Combat.WeaponSystemState>()) _world.RegisterComponent<Fdp.Toolkit.Combat.WeaponSystemState>();
            if (!_world.IsComponentTypeRegistered<Fdp.Toolkit.Behavior.Components.IsEmbarkedTag>()) _world.RegisterComponent<Fdp.Toolkit.Behavior.Components.IsEmbarkedTag>();
            var db = new Fdp.Toolkit.Tkb.TkbDatabase();
            var t = new Fdp.Interfaces.TkbTemplate("T-test HMG", HmgType) { DisType = new DISEntityType { Kind = 1, Domain = 1, Category = 50, Subcategory = 1 } };
            t.AddDescriptor(new Fdp.Toolkit.Tkb.Domain.CombatPlatformDefDto { MaxHealth = 80f, ArmorFront = 1.5f, ArmorSide = 1.5f, ArmorRear = 1.5f, ArmorTop = 1.5f });
            db.Register(t);
            _world.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(db);
            var hmg = SpawnCollider(new Vector2(10f, 0f), radius: 2.5f, layer: 1);   // the 2.5 m fallback collider, no height (CE-2182)
            _world.AddComponent(hmg, new Fdp.Toolkit.Replication.Components.TkbIdentity { TkbType = HmgType });
            _world.AddComponent(hmg, new Fdp.Toolkit.Combat.WeaponSystemParams { NeedsOperator = 1 });
            _world.AddComponent(hmg, new Fdp.Toolkit.Combat.WeaponSystemState { Deploy = deploy });
            if (embarked) _world.AddComponent(hmg, new Fdp.Toolkit.Behavior.Components.IsEmbarkedTag());
            return hmg;
        }

        /// <summary>
        /// ⭐⭐ <c>CE-3214</c> (S2b-1, S2b-3) — a DEPLOYED HMG on its tripod: a round across the receiver meets the Weapon; a round low across
        /// the middle passes between the tripod's legs; a round over it misses. 🔴 Red before: CE-2182's skip — no round ever met it.
        /// </summary>
        [Fact]
        public void CE3214_ADeployedHmg_IsMetByItsParts()
        {
            var hmg = SpawnHmg(Fdp.Toolkit.Combat.WeaponDeploy.Deployed);
            var receiver = FromLeft(9.95f, 0.6f);
            Assert.Equal((hmg, (byte)1, Fdp.Toolkit.Physics.BodyParts.WeaponPart.Weapon, (byte)Fdp.Toolkit.Combat.Facing.Side),
                         (receiver.HitEntity, receiver.HasBodyPart, receiver.BodyPart, receiver.Face));
            Assert.Equal(Fdp.Toolkit.Physics.BodyParts.WeaponPart.Weapon, FromLeft(10.9f, 0.62f).BodyPart);   // the barrel
            Assert.Equal(0, FromLeft(10f, 0.2f).HasHit);   // between the legs
            Assert.Equal(0, FromLeft(10f, 1.2f).HasHit);   // over it
            Assert.Equal(0, FromLeft(9.95f, 0.6f, new RaycastSolverSystem { VehicleParts = false }).HasHit);   // the parts off: CE-2182's skip
        }

        /// <summary>
        /// ⭐ <c>CE-3214</c> (S2b-1, S2b-3) — the same HMG STOWED lies on the ground: a round at its height meets it (the gun lying beside the
        /// folded tripod is still the Weapon); the receiver's old height is empty. EMBARKED (carried, in a station) no round meets it — the
        /// man or the host does (CE-2182, kept).
        /// </summary>
        [Fact]
        public void CE3214_AStowedHmg_LiesLow_AndAnEmbarkedOne_IsNeverMet()
        {
            var hmg = SpawnHmg(Fdp.Toolkit.Combat.WeaponDeploy.Stowed);
            var low = FromLeft(9.95f, 0.11f);
            Assert.Equal((hmg, Fdp.Toolkit.Physics.BodyParts.WeaponPart.Weapon), (low.HitEntity, low.BodyPart));
            Assert.Equal(0, FromLeft(9.95f, 0.6f).HasHit);
            _world.AddComponent(hmg, new Fdp.Toolkit.Behavior.Components.IsEmbarkedTag());
            Assert.Equal(0, FromLeft(9.95f, 0.11f).HasHit);
        }

        // ── ⭐⭐ CE-3215 (hit location §12 S2c) — a round meets an AIRCRAFT's parts ────────────────────────────────────────────────────

        private const long HeliType = 9315;

        /// <summary>A UH-60-like helicopter (the Helicopter kit at its default 19.8 × 16.4 × 5.1 m) on the ground at (10, 0), facing +x.</summary>
        private Entity SpawnHelicopter()
        {
            if (!_world.IsComponentTypeRegistered<Fdp.Toolkit.Replication.Components.TkbIdentity>()) _world.RegisterComponent<Fdp.Toolkit.Replication.Components.TkbIdentity>();
            var db = new Fdp.Toolkit.Tkb.TkbDatabase();
            var t = new Fdp.Interfaces.TkbTemplate("T-test helicopter", HeliType) { DisType = new DISEntityType { Kind = 1, Domain = 2, Category = 21 } };
            t.AddDescriptor(new Fdp.Toolkit.Tkb.Domain.CombatPlatformDefDto { MaxHealth = 1000f, ArmorFront = 5f, ArmorSide = 5f, ArmorRear = 5f, ArmorTop = 5f });
            db.Register(t);
            _world.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(db);
            var heli = SpawnCollider(new Vector2(10f, 0f), radius: 8f, layer: 1);
            _world.GetComponentRW<PhysicsCollider>(heli).Height = 5.1f;
            _world.AddComponent(heli, new Fdp.Toolkit.Replication.Components.TkbIdentity { TkbType = HeliType });
            _world.SetSingleton(new SpatialGridData { Grid = _grid, MaxRadius = 8f });   // a measured grid (production's), not the hand-built pad
            return heli;
        }

        /// <summary>
        /// ⭐⭐ <c>CE-3215</c> (S2c-1, S2c-2) — a round from the side meets the helicopter part it crosses: the cabin is the Fuselage, the glass
        /// nose the Cockpit, the boom the Tail, the mast the Engine (the drive); a round through the rotor disc away from the mast misses
        /// (rotors do not collide). 🔴 Red before: one cylinder, every hit "the body".
        /// </summary>
        [Fact]
        public void CE3215_ARound_MeetsTheHelicopterPartItCrosses_TheRotorDiscIsEmpty()
        {
            var heli = SpawnHelicopter();
            var cabin = FromLeft(10f, 1.8f);
            Assert.Equal((heli, (byte)1, Fdp.Toolkit.Physics.BodyParts.AircraftPart.Fuselage), (cabin.HitEntity, cabin.HasBodyPart, cabin.BodyPart));
            Assert.Equal(Fdp.Toolkit.Physics.BodyParts.AircraftPart.Cockpit, FromLeft(16.8f, 1.94f).BodyPart);
            var boom = FromLeft(4.46f, 2.55f);   // 5.5 m off its point: the cell walk's pad must reach it
            Assert.Equal((heli, Fdp.Toolkit.Physics.BodyParts.AircraftPart.Tail), (boom.HitEntity, boom.BodyPart));
            Assert.True(boom.BodyPart == Fdp.Toolkit.Physics.BodyParts.AircraftPart.Tail, $"boom: hit {boom.HasHit} part {boom.BodyPart} entity {boom.HitEntity} t {boom.T}");
            Assert.Equal(Fdp.Toolkit.Physics.BodyParts.AircraftPart.Engine, FromLeft(11.19f, 3.4f).BodyPart);
            Assert.Equal(0, FromLeft(8f, 3.7f).HasHit);   // the blades' height, away from the mast
            Assert.Equal((byte)0, FromLeft(10f, 1.8f, new RaycastSolverSystem { VehicleParts = false }).HasBodyPart);   // the parts off: one body
        }
}
}

