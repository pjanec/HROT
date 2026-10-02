#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Tkb;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.Stride.Core;
using SMath = Stride.Core.Mathematics;
using Xunit;

namespace Hrot.Stride.Core.Tests;

/// <summary>
/// End-to-end headless tests for the character physics velocity chain
/// (BATCH-17 follow-up, F1 walking mannequin animation fix).
///
/// <para>
/// <b>Architecture (post-F1-actual-velocity fix):</b>
/// For kinematic CHARACTER (Capsule) bodies, <see cref="BulletReverseSyncSystem"/>
/// now derives <see cref="SimVelocity.Linear"/> from the frame-to-frame FDP position
/// delta (<c>(currentPos − prevPos) / deltaTime</c>) rather than from
/// <see cref="PhysicsBodyReference.PostCollisionLinearVelocityFdp"/>.
/// This ensures the locomotion blend sees ~zero velocity when the character is blocked
/// by a wall, and nonzero velocity while moving freely — even if the motor keeps
/// commanding movement while blocked.
/// </para>
///
/// <para>
/// The chain under test is:
/// <c>CrowdMotorIntent → BulletCharacterMotor → SetCharacterVelocity (physical drive)
///  → PhysicsProcessor moves entity → BulletReverseSyncSystem reads pose delta
///  → SimVelocity</c>
///
/// In tests, the fake service scripts the body's returned position frame-by-frame to
/// simulate the physics engine's response to the commanded velocity.
/// </para>
/// </summary>
public sealed class SimVelocityChainTests : IDisposable
{
    // ── Scriptable fake IPhysicsBodyService ───────────────────────────────────

    /// <summary>
    /// Scriptable recording fake: exposes whether <c>SetCharacterVelocity</c> was called.
    /// Returns a scriptable position from <c>NextPosition</c> so tests can simulate
    /// the physics engine moving the entity in response to the commanded velocity.
    /// </summary>
    private sealed class KinematicFakeService : IPhysicsBodyService
    {
        private int _counter;

        // Calls recorded for assertions.
        public List<(object Handle, SMath.Vector3 Velocity)> SetVelocityCalls { get; } = new();

        /// <summary>
        /// Position to return from the NEXT GetBodyState call.
        /// Tests update this before each Execute to simulate physics motion.
        /// </summary>
        public SMath.Vector3 NextPosition { get; set; } = SMath.Vector3.Zero;

        public object CreateBody(Entity entity, CollisionShapeKind shapeKind,
                                 ShapeDims dims, in SimTransform initialPose)
            => $"Body_{++_counter}";

        public void RemoveBody(object bodyHandle) { }

        public void SetCharacterVelocity(object bodyHandle, SMath.Vector3 velocity)
            => SetVelocityCalls.Add((bodyHandle, velocity));

        public void Jump(object bodyHandle) { }
        public bool IsGrounded(object bodyHandle) => true;
        public void SetLinearVelocityXZ(object bodyHandle, SMath.Vector3 strideLinearVel) { }
        public void SetYawRate(object bodyHandle, float strideYawRateRadPerSec) { }

        public KinematicMoveResult MoveKinematic(object bodyHandle,
            SMath.Vector3 desiredDelta, SMath.Quaternion desiredRotDelta)
            => new KinematicMoveResult(desiredDelta, desiredRotDelta);

        /// <summary>
        /// Returns IsKinematic=true with the scripted NextPosition — simulates the
        /// physics engine having moved the character to that position.
        /// </summary>
        public BodyState GetBodyState(object bodyHandle)
            => new BodyState(
                NextPosition,
                SMath.Quaternion.Identity,
                SMath.Vector3.Zero,   // solver velocity is always zero for characters
                SMath.Vector3.Zero,
                IsKinematic: true);   // ← kinematic branch in BulletReverseSyncSystem
    }

    // ── Null visual factory ───────────────────────────────────────────────────

    private sealed class NullVisualFactory : IStrideVisualFactory
    {
        private int _counter;
        public object CreateModelVisual(string m, string s, float sc, Vector3 o, in SimTransform t)
            => $"Vis_{++_counter}";
        public object CreateProceduralVisual(CollisionShapeKind k, ShapeDims d, float sc, Vector3 o, in SimTransform t)
            => $"Proc_{++_counter}";
        public void UpdatePose(object h, in SimTransform t) { }
        public void Destroy(object h) { }
    }

    // ── Test infrastructure ───────────────────────────────────────────────────

    private const long CapsuleTkbType = 750L;
    private const float Dt = 1.0f; // 1 s per frame for clean arithmetic

    private readonly EntityRepository          _world;
    private readonly KinematicFakeService      _fakeService;
    private readonly StrideVisualBindingSystem  _visualSystem;
    private readonly PhysicsBodyLifecycleSystem _lifecycle;
    private readonly BulletCharacterMotor       _motor;
    private readonly BulletReverseSyncSystem    _reverseSync;

    public SimVelocityChainTests()
    {
        _world = new EntityRepository();
        _world.RegisterComponent<SimTransform>();
        _world.RegisterComponent<SimVelocity>();
        _world.RegisterComponent<TkbIdentity>();
        _world.RegisterComponent<CrowdMotorIntent>();

        var tkbDb = BuildTkbDb();
        _fakeService  = new KinematicFakeService();
        _visualSystem = new StrideVisualBindingSystem(new NullVisualFactory(), tkbDb);
        _lifecycle    = new PhysicsBodyLifecycleSystem(_fakeService, _visualSystem);
        _motor        = new BulletCharacterMotor(_fakeService, _lifecycle);
        _reverseSync  = new BulletReverseSyncSystem(_fakeService, _lifecycle);
    }

    public void Dispose() => _world.Dispose();

    private static TkbDatabase BuildTkbDb()
    {
        var db = new TkbDatabase();
        var def = new StrideRenderModelDefDto
        {
            ShapeKind   = CollisionShapeKind.Capsule,
            ShapeRadius = 0.3f,
            ShapeHeight = 1.8f,
        };
        var tmpl = new TkbTemplate("CapsuleUnit", CapsuleTkbType);
        tmpl.AddDescriptor(def);
        db.Register(tmpl);
        return db;
    }

    /// <summary>
    /// Spawns an owned entity with a body and CrowdMotorIntent, then creates the body.
    /// </summary>
    private Entity SpawnWithBody(Vector3 pos, Vector3 intentVelocity)
    {
        var entity = _world.CreateEntity();
        _world.AddComponent(entity, new TkbIdentity { TkbType = CapsuleTkbType });
        _world.AddComponent(entity, new SimTransform { Position = pos });
        _world.AddComponent(entity, new SimVelocity());
        _world.AddComponent(entity, new CrowdMotorIntent { Velocity = intentVelocity });
        _world.SetAuthority<SimTransform>(entity, true);

        // Create visual and body (mirrors the two-step in EditorStrideSubsystem.Tick).
        _visualSystem.Sync(_world);
        _lifecycle.Execute(_world, Dt);

        return entity;
    }

    // ── Chain integration tests ───────────────────────────────────────────────

    /// <summary>
    /// Core chain test (BATCH-17 F1 actual-velocity fix + EMA smoothing):
    /// Motor commands 2 m/s north → physics engine moves entity 2 m in 1 s →
    /// ReverseSync measures pose delta = (0,2,0) m/s → EMA smooths →
    /// SimVelocity is nonzero and tracks the actual displacement.
    ///
    /// After many walking frames the EMA converges toward the steady-state velocity.
    /// After 8 frames at 2 m/s: vSmooth ≈ 2 * (1 - 0.75^8) ≈ 2 * 0.9 = 1.8 m/s.
    ///
    /// Breaks if the reverse-sync no longer computes pose-delta velocity for capsule bodies.
    /// </summary>
    [Fact]
    public void MotorAndReverseSync_Chain_SimVelocityEqualsActualDisplacement()
    {
        const float EmaAlpha = 0.25f; // mirrors BulletReverseSyncSystem.EmaAlpha
        var intentVel = new Vector3(0f, 2f, 0f); // walk north at 2 m/s
        var entity = SpawnWithBody(Vector3.Zero, intentVel);

        // Frame 1: seed prevPos — entity at Stride (0,0,0) = FDP(0,0,0).
        _fakeService.NextPosition = SMath.Vector3.Zero;
        _motor.Execute(_world, Dt);
        _reverseSync.Execute(_world, Dt); // seeds prevFdpPos = (0,0,0), SimVelocity = 0

        // Frames 2–9: physics engine moves entity 2 m north each second.
        // The EMA converges toward 2 m/s over multiple frames.
        for (int i = 1; i <= 8; i++)
        {
            _fakeService.NextPosition = new SMath.Vector3(0f, 0f, i * 2f);
            _motor.Execute(_world, Dt);
            _reverseSync.Execute(_world, Dt);
        }

        var vel = _world.GetComponent<SimVelocity>(entity);
        // After 8 frames of steady 2 m/s walking, the EMA should converge near 2 m/s.
        // vSmooth(8) = 2 * (1 - 0.75^8) ≈ 1.81 m/s. Assert it's clearly nonzero.
        Assert.Equal(0f, vel.Linear.X, precision: 4);
        Assert.True(vel.Linear.Y > 1.5f,
            $"After 8 walk frames the EMA-smoothed velocity should converge near 2 m/s (got {vel.Linear.Y:F3}).");
        Assert.Equal(0f, vel.Linear.Z, precision: 4);
        // PostCollision channel must NOT be used for capsule bodies (would be 0 here, irrelevant).
    }

    /// <summary>
    /// SimVelocity is zero when the entity has zero intent and does not move.
    /// When the intent is zeroed (entity stops physically), SimVelocity is written as zero.
    /// </summary>
    [Fact]
    public void MotorAndReverseSync_ZeroIntent_SimVelocityIsZero()
    {
        var entity = SpawnWithBody(Vector3.Zero, Vector3.Zero);

        // Physics engine does not move the entity (no intent).
        _fakeService.NextPosition = SMath.Vector3.Zero;
        _motor.Execute(_world, Dt);
        _reverseSync.Execute(_world, Dt); // seed

        _fakeService.NextPosition = SMath.Vector3.Zero; // no movement
        _motor.Execute(_world, Dt);
        _reverseSync.Execute(_world, Dt);

        var vel = _world.GetComponent<SimVelocity>(entity);
        Assert.Equal(0f, vel.Linear.X);
        Assert.Equal(0f, vel.Linear.Y);
        Assert.Equal(0f, vel.Linear.Z);
    }

    /// <summary>
    /// Motor commands velocity → physics engine scales the actual movement by the
    /// stance multiplier.  Crouched 0.5× intent of 4 m/s → actual 2 m/s displacement.
    /// With EMA smoothing, after multiple frames the smoothed velocity converges toward 2 m/s.
    /// </summary>
    [Fact]
    public void MotorAndReverseSync_CrouchedStance_SimVelocityReflectsActualMovement()
    {
        var entity = SpawnWithBody(Vector3.Zero, new Vector3(4f, 0f, 0f));

        var motorCrouched = new BulletCharacterMotor(
            _fakeService, _lifecycle,
            stanceResolver: _ => CharacterStance.Crouched);
        motorCrouched.CrouchedMultiplier = 0.5f;

        // Frame 1: seed prevPos at origin.
        _fakeService.NextPosition = SMath.Vector3.Zero;
        motorCrouched.Execute(_world, Dt);
        _reverseSync.Execute(_world, Dt);

        // Frames 2–9: physics engine moves entity 2 m east each second (crouched: 4×0.5=2).
        // The EMA converges toward 2 m/s over multiple frames.
        for (int i = 1; i <= 8; i++)
        {
            _fakeService.NextPosition = new SMath.Vector3(i * 2f, 0f, 0f);
            motorCrouched.Execute(_world, Dt);
            _reverseSync.Execute(_world, Dt);
        }

        var vel = _world.GetComponent<SimVelocity>(entity);
        // After 8 frames of steady 2 m/s crouched walking, EMA converges near 2 m/s east.
        Assert.True(vel.Linear.X > 1.5f,
            $"After 8 crouched-walk frames EMA-smoothed velocity should converge near 2 m/s east (got {vel.Linear.X:F3}).");
        Assert.Equal(0f, vel.Linear.Y, precision: 4);
        Assert.Equal(0f, vel.Linear.Z, precision: 4);
    }

    /// <summary>
    /// If the entity does not move even though the motor commands velocity
    /// (blocked at wall), the EMA-smoothed measured pose delta decays toward zero.
    ///
    /// After priming the EMA with walking frames, the entity then hits the wall.
    /// After enough blocked frames the smoothed velocity decays to near zero —
    /// the locomotion blend stops promptly.
    ///
    /// This is the core fix for the walk-animation-overrun symptom.
    /// </summary>
    [Fact]
    public void ReverseSync_CharacterBlockedAtWall_SimVelocityDecaysToZero()
    {
        // Entity has a walk intent but the physics engine blocks it after priming.
        var entity = SpawnWithBody(Vector3.Zero, new Vector3(0f, 2f, 0f));

        // Seed frame.
        _fakeService.NextPosition = SMath.Vector3.Zero;
        _motor.Execute(_world, Dt);
        _reverseSync.Execute(_world, Dt);

        // Walk frames: prime the EMA with 4 frames of 2 m/s north movement.
        for (int i = 1; i <= 4; i++)
        {
            _fakeService.NextPosition = new SMath.Vector3(0f, 0f, i * 2f);
            _motor.Execute(_world, Dt);
            _reverseSync.Execute(_world, Dt);
        }

        // Blocked frames: entity hits wall, position fixed at (0,0,8).
        // After 20 blocked frames (with alpha=0.25, decay=0.75) the EMA decays to near zero.
        const float stoppedZ = 4f * 2f; // = 8
        for (int i = 0; i < 20; i++)
        {
            _fakeService.NextPosition = new SMath.Vector3(0f, 0f, stoppedZ); // SAME position
            _motor.Execute(_world, Dt);
            _reverseSync.Execute(_world, Dt);
        }

        var vel = _world.GetComponent<SimVelocity>(entity);

        // After 20 blocked frames the EMA has decayed sufficiently.
        // The locomotion blend sees near-zero speed → returns to idle.
        const float threshold = 0.01f;
        Assert.True(vel.Linear.LengthSquared() < threshold * threshold,
            $"After 20 blocked frames EMA velocity must be near zero (got |v|={MathF.Sqrt(vel.Linear.LengthSquared()):F4}).");
    }

    /// <summary>
    /// Multi-frame: EMA-smoothed SimVelocity tracks actual displacement direction correctly.
    /// The key invariant: velocity is nonzero during motion and near-zero after sustained stop.
    /// EMA means single-frame values won't exactly equal the raw displacement, but
    /// the blend direction (walk/idle/run) is correctly driven by the convergence behavior.
    ///
    /// Priming (10 walk frames at 1.5 m/s north): vSmooth converges toward 1.5 m/s.
    /// After stop (20 frames fixed): vSmooth decays toward 0.
    /// </summary>
    [Fact]
    public void MotorAndReverseSync_MultiFrame_SimVelocityTracksActualDisplacement()
    {
        var entity = SpawnWithBody(Vector3.Zero, new Vector3(0f, 1.5f, 0f)); // walk north

        // Seed frame.
        _fakeService.NextPosition = SMath.Vector3.Zero;
        _motor.Execute(_world, Dt);
        _reverseSync.Execute(_world, Dt);

        // Walk frames: 10 frames at 1.5 m/s north.
        for (int i = 1; i <= 10; i++)
        {
            _fakeService.NextPosition = new SMath.Vector3(0f, 0f, i * 1.5f);
            _motor.Execute(_world, Dt);
            _reverseSync.Execute(_world, Dt);
        }
        var velWalk = _world.GetComponent<SimVelocity>(entity);
        // After 10 walk frames EMA should converge clearly toward 1.5 m/s.
        Assert.True(velWalk.Linear.Y > 1.0f,
            $"After 10 walk frames vSmooth should be clearly > 1.0 m/s north (got {velWalk.Linear.Y:F3}).");

        // Stop frames: 20 frames with no movement (hit wall).
        float stoppedZ = 10f * 1.5f;
        _world.SetComponent(entity, new CrowdMotorIntent { Velocity = Vector3.Zero });
        for (int i = 0; i < 20; i++)
        {
            _fakeService.NextPosition = new SMath.Vector3(0f, 0f, stoppedZ);
            _motor.Execute(_world, Dt);
            _reverseSync.Execute(_world, Dt);
        }
        var velStop = _world.GetComponent<SimVelocity>(entity);
        // After 20 stop frames the EMA should have decayed to near zero.
        const float threshold = 0.02f;
        Assert.True(velStop.Linear.LengthSquared() < threshold * threshold,
            $"After 20 stop frames EMA velocity must be near zero (got |v|={MathF.Sqrt(velStop.Linear.LengthSquared()):F4}).");
    }
}
