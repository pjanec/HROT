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

}
}

