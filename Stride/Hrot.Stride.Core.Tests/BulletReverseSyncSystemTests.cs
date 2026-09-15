#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.ModuleHost.Scheduling;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Tkb;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.Stride.Core;
using SMath = Stride.Core.Mathematics;
using Xunit;

namespace Hrot.Stride.Core.Tests;

/// <summary>
/// Unit tests for <see cref="BulletReverseSyncSystem"/> (STR-P1-T5).
///
/// <para>
/// All tests are headless — no Bullet/Stride runtime required.
/// A scriptable fake <see cref="IPhysicsBodyService"/> drives the
/// <see cref="BodyState"/> returned by <c>GetBodyState</c>.
/// </para>
///
/// <para>
/// Scenarios covered:
/// <list type="bullet">
///   <item>T5-SC1: owned dynamic body → <see cref="SimTransform"/> swizzled correctly.</item>
///   <item>T5-SC2: dynamic body velocity → <see cref="SimVelocity.Linear"/> / <c>.Angular</c> set correctly.</item>
///   <item>T5-SC3: collision arrest (zero velocity from solver) → <see cref="SimVelocity"/> written EXACTLY zero
///     (no stale value from prior frame).</item>
///   <item>T5-SC4: kinematic body → <see cref="SimVelocity"/> sourced from
///     <see cref="PhysicsBodyReference.PostCollisionLinearVelocityFdp"/> /
///     <c>.PostCollisionAngularVelocityFdp</c> (motor channel).</item>
///   <item>T5-SC5: replay severability — with the group <c>Enabled=false</c>, no writes occur.</item>
/// </list>
/// </para>
/// </summary>
public sealed class BulletReverseSyncSystemTests : IDisposable
{
    // ── Scriptable fake IPhysicsBodyService ───────────────────────────────────

    private sealed class ScriptableFake : IPhysicsBodyService
    {
        private int _counter;

        /// <summary>Map: body handle → scripted BodyState to return from GetBodyState.</summary>
        public Dictionary<object, BodyState> StateMap { get; } = new();

        public object CreateBody(Entity entity, CollisionShapeKind shapeKind,
                                 ShapeDims dims, in SimTransform initialPose)
            => $"Body_{++_counter}";

        public void RemoveBody(object bodyHandle) { }
        public void SetCharacterVelocity(object bodyHandle, SMath.Vector3 velocity) { }
        public void Jump(object bodyHandle) { }
        public bool IsGrounded(object bodyHandle) => false;
        public void SetLinearVelocityXZ(object bodyHandle, SMath.Vector3 strideLinearVel) { }
        public void SetYawRate(object bodyHandle, float strideYawRateRadPerSec) { }
        public KinematicMoveResult MoveKinematic(object bodyHandle,
            SMath.Vector3 desiredDelta, SMath.Quaternion desiredRotDelta)
            => new KinematicMoveResult(desiredDelta, desiredRotDelta);

        public BodyState GetBodyState(object bodyHandle)
        {
            if (StateMap.TryGetValue(bodyHandle, out var state))
                return state;
            // Default: zero pose, zero velocity, dynamic.
            return new BodyState(
                SMath.Vector3.Zero,
                SMath.Quaternion.Identity,
                SMath.Vector3.Zero,
                SMath.Vector3.Zero,
                IsKinematic: false);
        }
    }

    // ── Null visual factory ───────────────────────────────────────────────────

    private sealed class NullVisualFactory : IStrideVisualFactory
    {
        public object CreateModelVisual(string m, string s, float sc, Vector3 o, in SimTransform t) => new object();
        public object CreateProceduralVisual(CollisionShapeKind k, ShapeDims d, float sc, Vector3 o, in SimTransform t) => new object();
        public void UpdatePose(object h, in SimTransform t) { }
        public void Destroy(object h) { }
    }

    // ── Test infrastructure ───────────────────────────────────────────────────

    private const long DynTkbType = 901L;  // dynamic (OrientedBox)
    private const long KinTkbType = 902L;  // kinematic (Capsule)
    private const float Dt = 1f / 60f;

    private readonly EntityRepository          _world;
    private readonly ScriptableFake            _fakeService;
    private readonly StrideVisualBindingSystem  _visualSystem;
    private readonly PhysicsBodyLifecycleSystem _lifecycle;
    private readonly BulletReverseSyncSystem    _sut;

    public BulletReverseSyncSystemTests()
    {
        _world = new EntityRepository();
        _world.RegisterComponent<SimTransform>();
        _world.RegisterComponent<SimVelocity>();
        _world.RegisterComponent<TkbIdentity>();

        var tkbDb = BuildTkbDb();
        _fakeService  = new ScriptableFake();
        _visualSystem = new StrideVisualBindingSystem(new NullVisualFactory(), tkbDb);
        _lifecycle    = new PhysicsBodyLifecycleSystem(_fakeService, _visualSystem);
        _sut          = new BulletReverseSyncSystem(_fakeService, _lifecycle);
    }

    public void Dispose() => _world.Dispose();

    private static TkbDatabase BuildTkbDb()
    {
        var db = new TkbDatabase();

        var dynDef = new StrideRenderModelDefDto
        {
            ShapeKind = CollisionShapeKind.OrientedBox,
            BoxHalfX  = 1f, BoxHalfY = 0.5f, BoxHalfZ = 0.5f,
        };
        var dynTmpl = new TkbTemplate("DynamicUnit", DynTkbType);
        dynTmpl.AddDescriptor(dynDef);
        db.Register(dynTmpl);

        var kinDef = new StrideRenderModelDefDto
        {
            ShapeKind   = CollisionShapeKind.Capsule,
            ShapeRadius = 0.3f,
            ShapeHeight = 1.8f,
        };
        var kinTmpl = new TkbTemplate("KinematicUnit", KinTkbType);
        kinTmpl.AddDescriptor(kinDef);
        db.Register(kinTmpl);

        return db;
    }

    /// <summary>
    /// Spawns an owned entity, creates its visual and physics body.
    /// Returns the entity and its body handle string.
    /// </summary>
    private (Entity entity, string handle) SpawnOwned(
        long tkbType,
        Vector3 initialPos = default,
        Quaternion? initialRot = null)
    {
        var entity = _world.CreateEntity();
        _world.AddComponent(entity, new TkbIdentity { TkbType = tkbType });
        _world.AddComponent(entity, new SimTransform
        {
            Position = initialPos,
            Rotation = initialRot ?? Quaternion.Identity,
        });
        _world.AddComponent(entity, new SimVelocity());
        _world.SetAuthority<SimTransform>(entity, true);

        _visualSystem.Sync(_world);
        _lifecycle.Execute(_world, Dt);

        var handle = (string)_lifecycle.Bodies[entity].BodyHandle;
        return (entity, handle);
    }

    private void RunReverseSync() => _sut.Execute(_world, Dt);

    // ── T5-SC1: owned dynamic body pose → SimTransform swizzled correctly ─────

    /// <summary>
    /// An owned dynamic body at a known Stride-space position and rotation must have
    /// its <see cref="SimTransform"/> written with the correct FDP swizzle.
    ///
    /// Stride position (3, 5, 7) → FDP position (3, 7, 5) per the swizzle
    /// (Stride.X → FDP.X, Stride.Y → FDP.Z, Stride.Z → FDP.Y).
    /// </summary>
    [Fact]
    public void OwnedDynamicBody_PoseWrittenToSimTransform_SwizzledCorrectly()
    {
        var (entity, handle) = SpawnOwned(DynTkbType);

        // Script: body is at known Stride position
        var stridePos = new SMath.Vector3(3f, 5f, 7f);
        // Stride 90° yaw-around-Y → unit test uses identity rotation for pose (rotation tested via FdpStrideTransformTests)
        var strideRot = SMath.Quaternion.Identity;

        _fakeService.StateMap[handle] = new BodyState(
            stridePos, strideRot,
            SMath.Vector3.Zero, SMath.Vector3.Zero,
            IsKinematic: false);

        RunReverseSync();

        var tf = _world.GetComponent<SimTransform>(entity);

        // Swizzle: Stride (X=3, Y=5, Z=7) → FDP (X=3, Z=7, Y=5)
        // ToFdpPosition: FDP.X = Stride.X, FDP.Y = Stride.Z, FDP.Z = Stride.Y
        Assert.Equal(3f, tf.Position.X, precision: 5);
        Assert.Equal(7f, tf.Position.Y, precision: 5); // Stride.Z → FDP.Y
        Assert.Equal(5f, tf.Position.Z, precision: 5); // Stride.Y → FDP.Z
    }

    /// <summary>
    /// Rotation round-trip: a Stride quaternion is converted to FDP and written
    /// to <see cref="SimTransform.Rotation"/>. The result must equal the expected
    /// FDP rotation from <see cref="FdpStrideTransform.ToFdpRotation"/>.
    /// </summary>
    [Fact]
    public void OwnedDynamicBody_Rotation_ConvertedViaFdpStrideTransform()
    {
        var (entity, handle) = SpawnOwned(DynTkbType);

        // A non-trivial Stride quaternion: 45° around Stride Y (up).
        var strideRot = SMath.Quaternion.RotationY(MathF.PI / 4f);

        _fakeService.StateMap[handle] = new BodyState(
            SMath.Vector3.Zero, strideRot,
            SMath.Vector3.Zero, SMath.Vector3.Zero,
            IsKinematic: false);

        RunReverseSync();

        var tf = _world.GetComponent<SimTransform>(entity);
        var expectedFdpRot = FdpStrideTransform.ToFdpRotation(strideRot);

        // Assert each quaternion component matches the expected conversion.
        Assert.Equal(expectedFdpRot.X, tf.Rotation.X, precision: 5);
        Assert.Equal(expectedFdpRot.Y, tf.Rotation.Y, precision: 5);
        Assert.Equal(expectedFdpRot.Z, tf.Rotation.Z, precision: 5);
        Assert.Equal(expectedFdpRot.W, tf.Rotation.W, precision: 5);
    }

    // ── T5-SC2: dynamic body velocity → SimVelocity set correctly ─────────────

    /// <summary>
    /// A dynamic body with a known Stride-space velocity must have its
    /// <see cref="SimVelocity.Linear"/> and <see cref="SimVelocity.Angular"/> written
    /// via the correct swizzle / handedness conversion.
    ///
    /// Stride linear velocity (4, 0, 6) → FDP (4, 6, 0) via ToFdpVelocity.
    /// Stride angular velocity (0, 2, 0) → FDP (0, 0, -2) via ToFdpAngularVelocity
    /// (angular velocity negates sign for handedness flip).
    /// </summary>
    [Fact]
    public void DynamicBody_Velocity_WrittenToSimVelocity_CorrectSwizzle()
    {
        var (entity, handle) = SpawnOwned(DynTkbType);

        var strideLinVel = new SMath.Vector3(4f, 0f, 6f);
        var strideAngVel = new SMath.Vector3(0f, 2f, 0f);

        _fakeService.StateMap[handle] = new BodyState(
            SMath.Vector3.Zero, SMath.Quaternion.Identity,
            strideLinVel, strideAngVel,
            IsKinematic: false);

        RunReverseSync();

        var vel = _world.GetComponent<SimVelocity>(entity);

        // Linear swizzle: FDP (X=Stride.X, Y=Stride.Z, Z=Stride.Y)
        var expectedLinear = FdpStrideTransform.ToFdpVelocity(strideLinVel);
        Assert.Equal(expectedLinear.X, vel.Linear.X, precision: 5);
        Assert.Equal(expectedLinear.Y, vel.Linear.Y, precision: 5);
        Assert.Equal(expectedLinear.Z, vel.Linear.Z, precision: 5);

        // Angular: same swizzle + sign-negation for handedness flip.
        var expectedAngular = FdpStrideTransform.ToFdpAngularVelocity(strideAngVel);
        Assert.Equal(expectedAngular.X, vel.Angular.X, precision: 5);
        Assert.Equal(expectedAngular.Y, vel.Angular.Y, precision: 5);
        Assert.Equal(expectedAngular.Z, vel.Angular.Z, precision: 5);
    }

    // ── T5-SC3: collision arrest → SimVelocity written EXACTLY zero (no stale) ─

    /// <summary>
    /// The collision-arrest invariant (design §6.1):
    /// When the solver reports zero velocity for a dynamic body (collision has arrested it),
    /// <see cref="SimVelocity"/> must be written as EXACTLY zero — even if it had a non-zero
    /// value from the prior frame.
    ///
    /// This test sets a non-zero velocity first, then scripts the fake to report zero velocity
    /// on the next frame and asserts the written value is exactly zero (not stale).
    /// </summary>
    [Fact]
    public void CollisionArrest_ZeroVelocityFromSolver_SimVelocityWrittenExactlyZero_NoStale()
    {
        var (entity, handle) = SpawnOwned(DynTkbType);

        // Frame 1: body is moving at 5 m/s in Stride X.
        _fakeService.StateMap[handle] = new BodyState(
            SMath.Vector3.Zero, SMath.Quaternion.Identity,
            new SMath.Vector3(5f, 0f, 0f), SMath.Vector3.Zero,
            IsKinematic: false);

        RunReverseSync();

        // Verify the first frame wrote non-zero velocity.
        var velFrame1 = _world.GetComponent<SimVelocity>(entity);
        Assert.True(velFrame1.Linear.LengthSquared() > 0f,
            "Frame 1 should have a non-zero SimVelocity (pre-arrest).");

        // Frame 2: body is now arrested (collision stops it → solver reports zero).
        _fakeService.StateMap[handle] = new BodyState(
            SMath.Vector3.Zero, SMath.Quaternion.Identity,
            SMath.Vector3.Zero, SMath.Vector3.Zero,
            IsKinematic: false);

        RunReverseSync();

        var velFrame2 = _world.GetComponent<SimVelocity>(entity);

        // EXACTLY zero — no stale velocity from frame 1.
        Assert.Equal(0f, velFrame2.Linear.X);
        Assert.Equal(0f, velFrame2.Linear.Y);
        Assert.Equal(0f, velFrame2.Linear.Z);
        Assert.Equal(0f, velFrame2.Angular.X);
        Assert.Equal(0f, velFrame2.Angular.Y);
        Assert.Equal(0f, velFrame2.Angular.Z);
    }

    // ── T5-SC4a: kinematic VEHICLE (OrientedBox) → SimVelocity from PostCollision ─

    /// <summary>
    /// For kinematic VEHICLE (OrientedBox) bodies, <see cref="SimVelocity"/> must be
    /// sourced from <see cref="PhysicsBodyReference.PostCollisionLinearVelocityFdp"/> /
    /// <see cref="PhysicsBodyReference.PostCollisionAngularVelocityFdp"/>.
    ///
    /// The fake returns <see cref="BodyState.IsKinematic"/> = true.
    /// The test manually sets the post-collision channel on the body reference and
    /// asserts the correct values appear in <see cref="SimVelocity"/>.
    ///
    /// Note: this test uses DynTkbType (OrientedBox) because the vehicle uses the
    /// PostCollision channel — Capsule (character) bodies now use measured pose delta.
    /// </summary>
    [Fact]
    public void VehicleBody_SimVelocity_SourcedFromPostCollisionChannel()
    {
        // DynTkbType = OrientedBox = kinematic vehicle body.
        var (entity, handle) = SpawnOwned(DynTkbType);

        // Script: body is kinematic — solver velocity is ignored.
        _fakeService.StateMap[handle] = new BodyState(
            new SMath.Vector3(1f, 0f, 2f), SMath.Quaternion.Identity,
            new SMath.Vector3(99f, 99f, 99f), new SMath.Vector3(99f, 99f, 99f),  // should be ignored
            IsKinematic: true);

        // Write known post-collision velocities on the body reference (already in FDP space).
        var bodyRef = _lifecycle.Bodies[entity];
        var expectedLinear  = new Vector3(3f, 4f, 0f);   // FDP linear velocity
        var expectedAngular = new Vector3(0f, 0f, 0.5f); // FDP angular velocity (yaw rate)
        bodyRef.PostCollisionLinearVelocityFdp  = expectedLinear;
        bodyRef.PostCollisionAngularVelocityFdp = expectedAngular;

        RunReverseSync();

        var vel = _world.GetComponent<SimVelocity>(entity);

        // OrientedBox kinematic: must use the PostCollision* channel, not solver velocity.
        Assert.Equal(expectedLinear.X,  vel.Linear.X,  precision: 5);
        Assert.Equal(expectedLinear.Y,  vel.Linear.Y,  precision: 5);
        Assert.Equal(expectedLinear.Z,  vel.Linear.Z,  precision: 5);
        Assert.Equal(expectedAngular.X, vel.Angular.X, precision: 5);
        Assert.Equal(expectedAngular.Y, vel.Angular.Y, precision: 5);
        Assert.Equal(expectedAngular.Z, vel.Angular.Z, precision: 5);
    }

    /// <summary>
    /// Kinematic VEHICLE body with a fully blocked move: the motor wrote exactly zero
    /// to the post-collision channel, and the reverse-sync must write exactly zero
    /// to <see cref="SimVelocity"/> (velocity invariant for vehicle bodies, §6.1).
    /// </summary>
    [Fact]
    public void VehicleBody_FullyBlocked_PostCollisionChannelZero_SimVelocityExactlyZero()
    {
        // DynTkbType = OrientedBox = kinematic vehicle body.
        var (entity, handle) = SpawnOwned(DynTkbType);

        // Script: body is kinematic.
        _fakeService.StateMap[handle] = new BodyState(
            SMath.Vector3.Zero, SMath.Quaternion.Identity,
            SMath.Vector3.Zero, SMath.Vector3.Zero,
            IsKinematic: true);

        // Motor wrote zero (fully blocked move).
        var bodyRef = _lifecycle.Bodies[entity];
        bodyRef.PostCollisionLinearVelocityFdp  = Vector3.Zero;
        bodyRef.PostCollisionAngularVelocityFdp = Vector3.Zero;

        RunReverseSync();

        var vel = _world.GetComponent<SimVelocity>(entity);
        Assert.Equal(0f, vel.Linear.X);
        Assert.Equal(0f, vel.Linear.Y);
        Assert.Equal(0f, vel.Linear.Z);
        Assert.Equal(0f, vel.Angular.X);
        Assert.Equal(0f, vel.Angular.Y);
        Assert.Equal(0f, vel.Angular.Z);
    }

    // ── T5-SC4b: kinematic CHARACTER (Capsule) → SimVelocity from measured pose delta ─

    /// <summary>
    /// For kinematic CHARACTER (Capsule) bodies, <see cref="SimVelocity.Linear"/> must
    /// be derived from the frame-to-frame FDP position delta
    /// (<c>(currentPos − prevPos) / deltaTime</c>), NOT from
    /// <see cref="PhysicsBodyReference.PostCollisionLinearVelocityFdp"/>.
    ///
    /// This ensures the locomotion blend sees ~zero velocity when the character is
    /// blocked by a wall (actual motion ≈ 0) rather than the commanded velocity
    /// (which remains nonzero while the motor keeps commanding movement).
    ///
    /// Setup: body moves from Stride (0,0,0) to Stride (0,0,2) in one frame at 60 fps
    /// (= FDP Y=north = 2 m) → raw measured velocity = 2 m/s north in FDP space.
    /// With EMA alpha=0.25, the smoothed velocity = lerp(0, 2, 0.25) = 0.5 m/s.
    /// </summary>
    [Fact]
    public void CapsuleBody_Moving_SimVelocity_FromMeasuredPoseDelta()
    {
        // KinTkbType = Capsule = character body.
        var (entity, handle) = SpawnOwned(KinTkbType, initialPos: Vector3.Zero);

        // Frame 1: entity is at Stride (0,0,0) — this seeds prevFdpPos = (0,0,0), smooth = 0.
        _fakeService.StateMap[handle] = new BodyState(
            SMath.Vector3.Zero, SMath.Quaternion.Identity,
            SMath.Vector3.Zero, SMath.Vector3.Zero,
            IsKinematic: true);

        RunReverseSync(); // seeds prevPos=(0,0,0), smooth=0, SimVelocity=0

        // Frame 2: body has moved 2 m north in Stride space (Stride.Z=north = FDP.Y=north).
        // FDP position = ToFdpPosition(0, 0, 2) = (X=0, Y=2, Z=0).
        // rawVelocity = (0,2,0) / 1.0 = (0, 2, 0) m/s.
        // vSmooth = lerp(prevSmooth=0, raw=2, alpha=0.25) = 0.5 m/s north.
        const float EmaAlpha = 0.25f; // mirrors BulletReverseSyncSystem.EmaAlpha
        float testDt = 1.0f; // 1 second for clean arithmetic
        _fakeService.StateMap[handle] = new BodyState(
            new SMath.Vector3(0f, 0f, 2f), SMath.Quaternion.Identity,
            SMath.Vector3.Zero, SMath.Vector3.Zero,
            IsKinematic: true);

        // Set PostCollisionLinearVelocityFdp to a DIFFERENT value to confirm it is NOT used.
        var bodyRef = _lifecycle.Bodies[entity];
        bodyRef.PostCollisionLinearVelocityFdp = new Vector3(99f, 99f, 99f); // must be ignored

        _sut.Execute(_world, testDt);

        var vel = _world.GetComponent<SimVelocity>(entity);

        // EMA-smoothed velocity: vSmooth.Y = lerp(0, 2, 0.25) = 0.5.
        // The velocity is nonzero (walk blend active) and correctly positive (moving north).
        Assert.Equal(0f,           vel.Linear.X, precision: 4);
        Assert.Equal(EmaAlpha * 2f, vel.Linear.Y, precision: 4); // 0.25 * 2 = 0.5
        Assert.Equal(0f,           vel.Linear.Z, precision: 4);

        // The PostCollisionLinearVelocityFdp (99, 99, 99) must NOT appear in SimVelocity.
        Assert.True(vel.Linear.Y < 1f,
            "Capsule SimVelocity must come from EMA-smoothed pose delta, not PostCollision channel.");

        // Angular is always zero for capsule (no yaw via angular velocity).
        Assert.Equal(0f, vel.Angular.X, precision: 4);
        Assert.Equal(0f, vel.Angular.Y, precision: 4);
        Assert.Equal(0f, vel.Angular.Z, precision: 4);
    }

    /// <summary>
    /// On the FIRST frame a capsule body is seen, the reverse-sync seeds the previous
    /// position and reports ZERO velocity (no velocity spike on spawn / first appearance).
    /// </summary>
    [Fact]
    public void CapsuleBody_FirstFrame_SimVelocityIsZero_NoSpawnSpike()
    {
        var (entity, handle) = SpawnOwned(KinTkbType);

        // Script: body is at a nonzero position.
        _fakeService.StateMap[handle] = new BodyState(
            new SMath.Vector3(5f, 0f, 3f), SMath.Quaternion.Identity,
            SMath.Vector3.Zero, SMath.Vector3.Zero,
            IsKinematic: true);

        // First call — no prevPos seeded yet.
        RunReverseSync();

        var vel = _world.GetComponent<SimVelocity>(entity);

        // MUST be zero on spawn frame: no prior position = no velocity.
        Assert.Equal(0f, vel.Linear.X);
        Assert.Equal(0f, vel.Linear.Y);
        Assert.Equal(0f, vel.Linear.Z);
    }

    /// <summary>
    /// Capsule character blocked at a wall: entity position does not change between frames.
    /// Measured velocity = (currentPos − prevPos) / dt = zero.
    /// The locomotion blend correctly sees idle speed → walk animation stops promptly.
    ///
    /// This is the core fix for the wall-overrun symptom: prior to this fix,
    /// the commanded PostCollisionLinearVelocityFdp stayed nonzero while the character
    /// was blocked, keeping the walk blend active even though the character was stationary.
    /// </summary>
    [Fact]
    public void CapsuleBody_BlockedAtWall_SimVelocityIsZero()
    {
        var (entity, handle) = SpawnOwned(KinTkbType);

        // Frame 1: entity moving to (0, 0, 2) in Stride → seeds prevPos.
        _fakeService.StateMap[handle] = new BodyState(
            new SMath.Vector3(0f, 0f, 2f), SMath.Quaternion.Identity,
            SMath.Vector3.Zero, SMath.Vector3.Zero,
            IsKinematic: true);
        _sut.Execute(_world, 1.0f); // seed prevPos = FDP(0,2,0)

        // Frame 2: entity is blocked — position does NOT change.
        // PostCollisionLinearVelocityFdp is (0,2,0) — commanded velocity, MUST be ignored.
        var bodyRef = _lifecycle.Bodies[entity];
        bodyRef.PostCollisionLinearVelocityFdp = new Vector3(0f, 2f, 0f); // commanded, must be ignored
        _fakeService.StateMap[handle] = new BodyState(
            new SMath.Vector3(0f, 0f, 2f), SMath.Quaternion.Identity, // SAME position
            SMath.Vector3.Zero, SMath.Vector3.Zero,
            IsKinematic: true);

        _sut.Execute(_world, 1.0f);

        var vel = _world.GetComponent<SimVelocity>(entity);

        // Position delta is zero → measured velocity is EXACTLY zero.
        Assert.Equal(0f, vel.Linear.X);
        Assert.Equal(0f, vel.Linear.Y);
        Assert.Equal(0f, vel.Linear.Z);
    }

    /// <summary>
    /// Capsule character walking freely: position advances each frame.
    /// EMA-smoothed measured velocity is nonzero and tracks actual displacement.
    /// Confirms walk blend has nonzero speed input while moving (no regression).
    ///
    /// With EmaAlpha=0.25, after seed frame and one moving frame:
    /// vSmooth = lerp(0, vRaw, 0.25) = 0.25 * vRaw.
    /// For vRaw = (0, 2, 0): vSmooth.Y = 0.5 m/s (nonzero, tracking the walk).
    /// </summary>
    [Fact]
    public void CapsuleBody_FreeWalk_SimVelocityMatchesActualDisplacement()
    {
        var (entity, handle) = SpawnOwned(KinTkbType);

        // Frame 1: seed prevPos at Stride (0, 0, 0).
        _fakeService.StateMap[handle] = new BodyState(
            SMath.Vector3.Zero, SMath.Quaternion.Identity,
            SMath.Vector3.Zero, SMath.Vector3.Zero,
            IsKinematic: true);
        _sut.Execute(_world, 1.0f); // prevPos = FDP(0,0,0), SimVelocity = 0

        // Frame 2: entity has moved 2 m north in Stride (Stride.Z=2 → FDP.Y=2).
        // With dt=1 s: rawVelocity = (0, 2, 0); vSmooth = lerp(0, raw, 0.25) = (0, 0.5, 0).
        _fakeService.StateMap[handle] = new BodyState(
            new SMath.Vector3(0f, 0f, 2f), SMath.Quaternion.Identity,
            SMath.Vector3.Zero, SMath.Vector3.Zero,
            IsKinematic: true);
        _sut.Execute(_world, 1.0f);

        var vel = _world.GetComponent<SimVelocity>(entity);

        // EMA smoothing: vSmooth.Y = lerp(0, 2, 0.25) = 0.5 (nonzero → walk blend active).
        Assert.Equal(0f,   vel.Linear.X, precision: 4);
        Assert.True(vel.Linear.Y > 0f,
            "EMA-smoothed velocity must be nonzero after one moving frame (walk blend input).");
        Assert.Equal(0f,   vel.Linear.Z, precision: 4);
        // Nonzero velocity → walk blend has a speed input.
        Assert.True(vel.Linear.LengthSquared() > 0f,
            "Free-walking character must have nonzero SimVelocity for walk blend.");
    }

    // ── T5-SC8: EMA smoothing — prevents single-frame dip from toggling blend ──

    /// <summary>
    /// EMA smoothing (F1 anim-stutter fix): the measured velocity is smoothed with a
    /// light EMA (vSmooth = lerp(vPrev, vRaw, alpha)) so a single-frame dip near the
    /// idle/walk threshold does not toggle the blend and cause a visible stutter.
    ///
    /// Setup: entity walks at steady 2 m/s north (Stride.Z=2, dt=1s → FDP.Y=2).
    /// After multiple walking frames the smoothed velocity converges toward the raw value.
    /// A single dip-to-zero frame still leaves the smoothed value clearly nonzero.
    /// </summary>
    [Fact]
    public void CapsuleBody_SingleFrameVelocityDip_EmaKeepsBlendStable()
    {
        var (entity, handle) = SpawnOwned(KinTkbType);
        const float dt = 1.0f;
        const float EmaAlpha = 0.25f; // mirror BulletReverseSyncSystem.EmaAlpha

        // Seed frame.
        _fakeService.StateMap[handle] = new BodyState(
            SMath.Vector3.Zero, SMath.Quaternion.Identity,
            SMath.Vector3.Zero, SMath.Vector3.Zero, IsKinematic: true);
        _sut.Execute(_world, dt);  // seeds prevPos=(0,0,0), smooth=0

        // Walk frames: position advances 2 m north each second.
        // After 4 walk frames the smoothed velocity is well above zero.
        for (int i = 1; i <= 4; i++)
        {
            _fakeService.StateMap[handle] = new BodyState(
                new SMath.Vector3(0f, 0f, i * 2f), SMath.Quaternion.Identity,
                SMath.Vector3.Zero, SMath.Vector3.Zero, IsKinematic: true);
            _sut.Execute(_world, dt);
        }

        // After 4 walk frames the EMA should have converged toward ~2 m/s (north).
        var velWalk = _world.GetComponent<SimVelocity>(entity);
        Assert.True(velWalk.Linear.Y > 0.5f,
            $"After 4 walk frames the smoothed velocity should be clearly positive (got {velWalk.Linear.Y:F3}).");

        // Dip frame: entity position does NOT change (raw velocity = 0 for one frame).
        // Simulating the jitter where a single Bullet frame produces zero measured delta.
        _fakeService.StateMap[handle] = new BodyState(
            new SMath.Vector3(0f, 0f, 4f * 2f), SMath.Quaternion.Identity, // same pos as prev
            SMath.Vector3.Zero, SMath.Vector3.Zero, IsKinematic: true);
        _sut.Execute(_world, dt);  // rawVelocity=0; vSmooth = lerp(prev, 0, 0.25)

        var velDip = _world.GetComponent<SimVelocity>(entity);
        // After ONE dip frame the EMA-smoothed value must still be nonzero —
        // the blend must NOT toggle to idle on a single jittery zero frame.
        Assert.True(velDip.Linear.Y > 0f,
            $"After a single dip frame the smoothed velocity must still be nonzero (got {velDip.Linear.Y:F3}). " +
            $"EMA smoothing should prevent a single-frame dip from toggling the walk→idle blend.");
    }

    /// <summary>
    /// EMA convergence (F1 anim-stutter fix): when the character stops at a wall
    /// (position constant across multiple frames), the smoothed velocity decays to
    /// essentially zero within a bounded number of frames.
    ///
    /// With EmaAlpha = 0.25 and initial vSmooth = 2 m/s:
    /// After N frames of zero raw velocity: vSmooth(N) = 2 * (1-0.25)^N.
    /// After 8 frames: 2 * 0.75^8 ≈ 2 * 0.1 = 0.2 m/s.
    /// After 16 frames: 2 * 0.75^16 ≈ 2 * 0.01 = 0.02 m/s (essentially stopped).
    /// This is fast enough that the walk animation stops within a fraction of a second
    /// at 60 fps (8/60 ≈ 0.13 s).
    /// </summary>
    [Fact]
    public void CapsuleBody_StopsAtWall_EmaDecaysToZeroWithinBoundedFrames()
    {
        var (entity, handle) = SpawnOwned(KinTkbType);
        const float dt = 1.0f; // 1 s per frame for easy arithmetic
        const float EmaAlpha = 0.25f;
        const int   WalkFrames = 6;  // prime the EMA with steady walking
        const int   StopFrames = 20; // frames at wall (position fixed)

        // Seed frame.
        _fakeService.StateMap[handle] = new BodyState(
            SMath.Vector3.Zero, SMath.Quaternion.Identity,
            SMath.Vector3.Zero, SMath.Vector3.Zero, IsKinematic: true);
        _sut.Execute(_world, dt);

        // Walking frames: position advances 2 m/s north.
        for (int i = 1; i <= WalkFrames; i++)
        {
            _fakeService.StateMap[handle] = new BodyState(
                new SMath.Vector3(0f, 0f, i * 2f), SMath.Quaternion.Identity,
                SMath.Vector3.Zero, SMath.Vector3.Zero, IsKinematic: true);
            _sut.Execute(_world, dt);
        }

        // Stopped at wall: position fixed at last walking position.
        float stoppedZ = WalkFrames * 2f;
        for (int i = 0; i < StopFrames; i++)
        {
            _fakeService.StateMap[handle] = new BodyState(
                new SMath.Vector3(0f, 0f, stoppedZ), SMath.Quaternion.Identity,
                SMath.Vector3.Zero, SMath.Vector3.Zero, IsKinematic: true);
            _sut.Execute(_world, dt);
        }

        var vel = _world.GetComponent<SimVelocity>(entity);

        // After 20 "stopped" frames with alpha=0.25 the decay is:
        // vSmooth(20) ≈ initialSmooth * 0.75^20 < 0.01 * initialSmooth.
        // The walk animation must stop (effectively idle).
        float threshold = 0.01f; // < 1 cm/s — effectively zero for blend purposes
        Assert.True(vel.Linear.LengthSquared() < threshold * threshold,
            $"After {StopFrames} stopped frames the EMA-smoothed velocity must be near zero " +
            $"(got |v|={MathF.Sqrt(vel.Linear.LengthSquared()):F4} m/s, threshold={threshold:F3} m/s). " +
            $"EMA must decay fast enough to stop the walk animation promptly.");
    }

    // ── T5-SC5: replay severability — Enabled=false → no writes ──────────────

    /// <summary>
    /// When the <see cref="TogglablePostSimulationGroup"/> wrapping
    /// <see cref="BulletReverseSyncSystem"/> has <c>Enabled = false</c>,
    /// no writes to <see cref="SimTransform"/> or <see cref="SimVelocity"/> occur.
    ///
    /// This is the replay severability invariant (design §9, STR-D5 resolution):
    /// during replay, <c>PlaybackTickSystem</c> restores historical positions and the
    /// reverse-sync must not overwrite them.
    /// </summary>
    [Fact]
    public void ReplaySeverability_GroupDisabled_NoWritesOccur()
    {
        var (entity, handle) = SpawnOwned(DynTkbType,
            initialPos: new Vector3(10f, 20f, 30f));

        // Known initial SimTransform and SimVelocity — these must survive the disabled tick.
        var savedTransform = _world.GetComponent<SimTransform>(entity);
        var savedVelocity  = _world.GetComponent<SimVelocity>(entity);

        // Script: body at a different position (should NOT be written when group is disabled).
        _fakeService.StateMap[handle] = new BodyState(
            new SMath.Vector3(999f, 999f, 999f), SMath.Quaternion.Identity,
            new SMath.Vector3(1f, 2f, 3f), SMath.Vector3.Zero,
            IsKinematic: false);

        // Wrap the reverse-sync in a TogglablePostSimulationGroup and disable it.
        var group = new TogglablePostSimulationGroup("ReverseSyncGroup", _sut);
        group.Enabled = false;

        // Execute the group (disabled → inner system should NOT run).
        group.Execute(_world, Dt);

        // SimTransform must be unchanged from before the disabled tick.
        var tfAfter  = _world.GetComponent<SimTransform>(entity);
        var velAfter = _world.GetComponent<SimVelocity>(entity);

        // Position must be unchanged (not overwritten by the disabled reverse-sync).
        Assert.Equal(savedTransform.Position.X, tfAfter.Position.X, precision: 5);
        Assert.Equal(savedTransform.Position.Y, tfAfter.Position.Y, precision: 5);
        Assert.Equal(savedTransform.Position.Z, tfAfter.Position.Z, precision: 5);
        // Velocity must be unchanged.
        Assert.Equal(savedVelocity.Linear.X, velAfter.Linear.X, precision: 5);
        Assert.Equal(savedVelocity.Linear.Y, velAfter.Linear.Y, precision: 5);
        Assert.Equal(savedVelocity.Linear.Z, velAfter.Linear.Z, precision: 5);
    }

    /// <summary>
    /// With the group <c>Enabled = true</c>, writes DO occur —
    /// verifying that the group correctly delegates to the inner system when enabled.
    /// </summary>
    [Fact]
    public void ReplaySeverability_GroupEnabled_WritesOccur()
    {
        var (entity, handle) = SpawnOwned(DynTkbType);

        var newStridePos = new SMath.Vector3(42f, 0f, 17f);
        _fakeService.StateMap[handle] = new BodyState(
            newStridePos, SMath.Quaternion.Identity,
            SMath.Vector3.Zero, SMath.Vector3.Zero,
            IsKinematic: false);

        // Wrap and enable.
        var group = new TogglablePostSimulationGroup("ReverseSyncGroup", _sut);
        group.Enabled = true;

        group.Execute(_world, Dt);

        var tf = _world.GetComponent<SimTransform>(entity);
        var expected = FdpStrideTransform.ToFdpPosition(newStridePos);

        Assert.Equal(expected.X, tf.Position.X, precision: 5);
        Assert.Equal(expected.Y, tf.Position.Y, precision: 5);
        Assert.Equal(expected.Z, tf.Position.Z, precision: 5);
    }

    // ── T5-SC6: non-owned entity is not written ───────────────────────────────

    /// <summary>
    /// An entity without authority (<c>WithoutOwned&lt;SimTransform&gt;</c>) must
    /// NOT have its <see cref="SimTransform"/> written by the reverse-sync.
    /// Only owned entities are physics-driven.
    /// </summary>
    [Fact]
    public void NonOwnedEntity_NotWrittenByReverseSync()
    {
        // Spawn without authority (ghost entity).
        var entity = _world.CreateEntity();
        _world.AddComponent(entity, new TkbIdentity { TkbType = DynTkbType });
        var originalPos = new Vector3(50f, 60f, 70f);
        _world.AddComponent(entity, new SimTransform { Position = originalPos });
        _world.AddComponent(entity, new SimVelocity());
        // Do NOT set authority — entity is not locally owned.

        RunReverseSync();

        // Position must be unchanged.
        var tf = _world.GetComponent<SimTransform>(entity);
        Assert.Equal(originalPos.X, tf.Position.X, precision: 5);
        Assert.Equal(originalPos.Y, tf.Position.Y, precision: 5);
        Assert.Equal(originalPos.Z, tf.Position.Z, precision: 5);
    }

    // ── T5-SC7: entity without body reference is skipped ─────────────────────

    /// <summary>
    /// An owned entity with no <see cref="PhysicsBodyReference"/> (body not yet created)
    /// must be skipped without throwing.
    /// </summary>
    [Fact]
    public void OwnedEntity_WithoutBodyRef_Skipped_NoThrow()
    {
        var entity = _world.CreateEntity();
        _world.AddComponent(entity, new SimTransform { Position = new Vector3(1f, 2f, 3f) });
        _world.AddComponent(entity, new SimVelocity());
        _world.SetAuthority<SimTransform>(entity, true);
        // Deliberately NOT creating visual or body.

        // Must not throw.
        var ex = Record.Exception(() => RunReverseSync());
        Assert.Null(ex);

        // Position unchanged.
        var tf = _world.GetComponent<SimTransform>(entity);
        Assert.Equal(1f, tf.Position.X, precision: 5);
    }
}
