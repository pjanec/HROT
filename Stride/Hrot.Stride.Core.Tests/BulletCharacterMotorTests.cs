#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;
using CarKinem.Core;
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
/// Unit tests for <see cref="BulletCharacterMotor"/> (STR-P1-T3).
///
/// <para>
/// All tests are headless — no Bullet/Stride runtime required. A recording fake
/// <see cref="IPhysicsBodyService"/> captures every <c>SetCharacterVelocity</c>,
/// <c>Jump</c>, and <c>IsGrounded</c> call with exact argument values and allows
/// scripting of <c>IsGrounded</c> return values per body handle.
/// </para>
/// </summary>
public sealed class BulletCharacterMotorTests : IDisposable
{
    // ── Recording fake IPhysicsBodyService ────────────────────────────────────

    /// <summary>
    /// Scriptable recording fake: captures all calls and allows IsGrounded to be
    /// scripted per body handle. Also implements the lifecycle methods so it can
    /// be passed to <see cref="PhysicsBodyLifecycleSystem"/>.
    /// </summary>
    private sealed class ScriptableFakePhysicsBodyService : IPhysicsBodyService
    {
        // Lifecycle call records
        public record CreateCall(Entity Entity, CollisionShapeKind ShapeKind, ShapeDims Dims,
                                 SimTransform Pose, object Handle);
        public record RemoveCall(object Handle);

        // Motor call records
        public record SetVelocityCall(object Handle, SMath.Vector3 Velocity);
        public record JumpCall(object Handle);
        public record IsGroundedCall(object Handle);

        public List<CreateCall>       Creates       { get; } = new();
        public List<RemoveCall>       Removes       { get; } = new();
        public List<SetVelocityCall>  VelocityCalls { get; } = new();
        public List<JumpCall>         JumpCalls     { get; } = new();
        public List<IsGroundedCall>   IsGroundedCalls { get; } = new();

        /// <summary>
        /// Map from body handle → scripted IsGrounded return value.
        /// Defaults to false (not grounded) if not set.
        /// </summary>
        public Dictionary<object, bool> GroundedMap { get; } = new();

        private int _counter;

        public object CreateBody(Entity entity, CollisionShapeKind shapeKind,
                                 ShapeDims dims, in SimTransform initialPose)
        {
            var handle = $"Body_{++_counter}";
            Creates.Add(new CreateCall(entity, shapeKind, dims, initialPose, handle));
            return handle;
        }

        public void RemoveBody(object bodyHandle)
            => Removes.Add(new RemoveCall(bodyHandle));

        public void SetCharacterVelocity(object bodyHandle, SMath.Vector3 velocity)
            => VelocityCalls.Add(new SetVelocityCall(bodyHandle, velocity));

        public void Jump(object bodyHandle)
            => JumpCalls.Add(new JumpCall(bodyHandle));

        public bool IsGrounded(object bodyHandle)
        {
            IsGroundedCalls.Add(new IsGroundedCall(bodyHandle));
            return GroundedMap.TryGetValue(bodyHandle, out var grounded) && grounded;
        }

        // Dynamic vehicle motor methods not used by BulletCharacterMotor
        public void SetLinearVelocityXZ(object bodyHandle, SMath.Vector3 strideLinearVel) { }
        public void SetYawRate(object bodyHandle, float strideYawRateRadPerSec) { }

        // KinematicMove not used by BulletCharacterMotor
        public KinematicMoveResult MoveKinematic(
            object bodyHandle, SMath.Vector3 desiredDelta, SMath.Quaternion desiredRotDelta)
            => new KinematicMoveResult(desiredDelta, desiredRotDelta);

        // GetBodyState not used by BulletCharacterMotor
        public BodyState GetBodyState(object bodyHandle)
            => new BodyState(
                SMath.Vector3.Zero,
                SMath.Quaternion.Identity,
                SMath.Vector3.Zero,
                SMath.Vector3.Zero,
                IsKinematic: false);
    }

    // ── Null visual factory ────────────────────────────────────────────────────

    private sealed class NullVisualFactory : IStrideVisualFactory
    {
        public object CreateModelVisual(string m, string s, float sc, Vector3 o, in SimTransform t) => new object();
        public object CreateProceduralVisual(CollisionShapeKind k, ShapeDims d, float sc, Vector3 o, in SimTransform t) => new object();
        public void UpdatePose(object h, in SimTransform t) { }
        public void Destroy(object h) { }
    }

    // ── Test infrastructure ───────────────────────────────────────────────────

    private const long CapsuleTkbType = 701L;

    private readonly EntityRepository                 _world;
    private readonly ScriptableFakePhysicsBodyService _fakeService;
    private readonly StrideVisualBindingSystem        _visualSystem;
    private readonly PhysicsBodyLifecycleSystem       _lifecycle;
    private readonly BulletCharacterMotor             _sut;

    public BulletCharacterMotorTests()
    {
        _world = new EntityRepository();
        _world.RegisterComponent<SimTransform>();
        _world.RegisterComponent<SimVelocity>();
        _world.RegisterComponent<TkbIdentity>();
        _world.RegisterComponent<CrowdMotorIntent>();

        var tkbDb = BuildTkbDb();
        _fakeService  = new ScriptableFakePhysicsBodyService();
        _visualSystem = new StrideVisualBindingSystem(new NullVisualFactory(), tkbDb);
        _lifecycle    = new PhysicsBodyLifecycleSystem(_fakeService, _visualSystem);
        _sut          = new BulletCharacterMotor(_fakeService, _lifecycle);
    }

    public void Dispose() => _world.Dispose();

    private static TkbDatabase BuildTkbDb()
    {
        var db  = new TkbDatabase();
        var def = new StrideRenderModelDefDto
        {
            ShapeKind   = CollisionShapeKind.Capsule,
            ShapeRadius = 0.3f,
            ShapeHeight = 1.8f,
        };
        var tmpl = new TkbTemplate("CharacterUnit", CapsuleTkbType);
        tmpl.AddDescriptor(def);
        db.Register(tmpl);
        return db;
    }

    private Entity SpawnOwnedWithBody(Vector3 pos)
    {
        var entity = _world.CreateEntity();
        _world.AddComponent(entity, new TkbIdentity { TkbType = CapsuleTkbType });
        _world.AddComponent(entity, new SimTransform { Position = pos });
        _world.AddComponent(entity, new CrowdMotorIntent());
        _world.SetAuthority<SimTransform>(entity, true);

        // Create visual reference + body
        _visualSystem.Sync(_world);
        _lifecycle.Execute(_world, 1f / 60f);

        return entity;
    }

    private string GetBodyHandle(Entity entity)
    {
        return (string)_lifecycle.Bodies[entity].BodyHandle;
    }

    private void SetIntent(Entity entity, Vector3 fdpVelocity, bool jump = false)
    {
        _world.SetComponent(entity, new CrowdMotorIntent { Velocity = fdpVelocity, Jump = jump });
    }

    private void Run() => _sut.Execute(_world, 1f / 60f);

    // ── T3-SC1: velocity magnitude and direction preserved ────────────────────

    /// <summary>
    /// A <see cref="CrowdMotorIntent"/> with a given FDP velocity v must result in
    /// <c>SetCharacterVelocity</c> being called with a Stride velocity of the same
    /// magnitude and the correct swizzle (X=East, Y=Up(FDP.Z), Z=North(FDP.Y)).
    /// Standing stance → multiplier 1.0.
    /// </summary>
    [Fact]
    public void Intent_StandingStance_VelocityPassedToService_MagnitudeAndDirectionPreserved()
    {
        // Arrange: FDP velocity = (3, 4, 0) — magnitude 5 in the XY (horizontal) plane.
        var fdpVel = new Vector3(3f, 4f, 0f);
        float expectedMag = fdpVel.Length(); // 5.0

        var entity = SpawnOwnedWithBody(Vector3.Zero);
        SetIntent(entity, fdpVel);
        _fakeService.VelocityCalls.Clear(); // clear calls made during setup

        // Act
        Run();

        // Assert
        Assert.Single(_fakeService.VelocityCalls);
        var call = _fakeService.VelocityCalls[0];

        // Magnitude preserved through the swizzle (no scaling in Standing stance).
        float actualMag = call.Velocity.Length();
        Assert.Equal(expectedMag, actualMag, precision: 4);

        // Direction preserved: FDP (3,4,0) → Stride (X=3, Y=0(Z), Z=4(Y)) swizzle.
        // FdpStrideTransform.ToStrideVelocity: Stride = (fdp.X, fdp.Z, fdp.Y)
        Assert.Equal(3f, call.Velocity.X, precision: 4);
        Assert.Equal(0f, call.Velocity.Y, precision: 4); // fdp.Z = 0
        Assert.Equal(4f, call.Velocity.Z, precision: 4); // fdp.Y = 4
    }

    /// <summary>
    /// Verifies the coordinate swizzle for a vertical (Z=Up) FDP velocity component:
    /// FDP Z maps to Stride Y.
    /// </summary>
    [Fact]
    public void Intent_FdpZ_MapsToStrideY()
    {
        var entity = SpawnOwnedWithBody(Vector3.Zero);
        SetIntent(entity, new Vector3(0f, 0f, 1f)); // FDP Z=Up component only
        _fakeService.VelocityCalls.Clear();

        Run();

        Assert.Single(_fakeService.VelocityCalls);
        var call = _fakeService.VelocityCalls[0];
        Assert.Equal(0f, call.Velocity.X, precision: 4);
        Assert.Equal(1f, call.Velocity.Y, precision: 4); // FDP.Z → Stride.Y
        Assert.Equal(0f, call.Velocity.Z, precision: 4);
    }

    // ── T3-SC2: stance speed multiplier ───────────────────────────────────────

    /// <summary>
    /// Stance Standing: multiplier 1.0 → applied velocity magnitude equals intent magnitude.
    /// </summary>
    [Fact]
    public void Stance_Standing_MultiplierOne_SpeedUnchanged()
    {
        var entity = SpawnOwnedWithBody(Vector3.Zero);
        SetIntent(entity, new Vector3(5f, 0f, 0f)); // magnitude 5
        _fakeService.VelocityCalls.Clear();

        // Default stance resolver returns Standing.
        Run();

        Assert.Single(_fakeService.VelocityCalls);
        Assert.Equal(5f, _fakeService.VelocityCalls[0].Velocity.Length(), precision: 4);
    }

    /// <summary>
    /// Stance Crouched: multiplier 0.5 → applied velocity magnitude is half of intent.
    /// </summary>
    [Fact]
    public void Stance_Crouched_HalfMultiplier_SpeedHalved()
    {
        var entity = SpawnOwnedWithBody(Vector3.Zero);
        SetIntent(entity, new Vector3(4f, 0f, 0f)); // magnitude 4

        // Wire a Crouched stance resolver for this entity.
        var motor = new BulletCharacterMotor(
            _fakeService, _lifecycle,
            stanceResolver: _ => CharacterStance.Crouched);
        motor.CrouchedMultiplier = 0.5f;

        _fakeService.VelocityCalls.Clear();
        motor.Execute(_world, 1f / 60f);

        Assert.Single(_fakeService.VelocityCalls);
        float actualMag = _fakeService.VelocityCalls[0].Velocity.Length();
        Assert.Equal(2f, actualMag, precision: 4); // 4 * 0.5 = 2
    }

    /// <summary>
    /// Stance Prone: multiplier 0.25 → applied velocity magnitude is quarter of intent.
    /// </summary>
    [Fact]
    public void Stance_Prone_QuarterMultiplier_SpeedQuartered()
    {
        var entity = SpawnOwnedWithBody(Vector3.Zero);
        SetIntent(entity, new Vector3(8f, 0f, 0f)); // magnitude 8

        var motor = new BulletCharacterMotor(
            _fakeService, _lifecycle,
            stanceResolver: _ => CharacterStance.Prone);
        motor.ProneMultiplier = 0.25f;

        _fakeService.VelocityCalls.Clear();
        motor.Execute(_world, 1f / 60f);

        Assert.Single(_fakeService.VelocityCalls);
        float actualMag = _fakeService.VelocityCalls[0].Velocity.Length();
        Assert.Equal(2f, actualMag, precision: 4); // 8 * 0.25 = 2
    }

    /// <summary>
    /// Configurable multipliers: setting CrouchedMultiplier = 0.6 and asserting the
    /// scaled magnitude is exactly 0.6 × intent speed.
    /// </summary>
    [Fact]
    public void StanceMultipliers_AreConfigurable_AssertScaledMagnitude()
    {
        var entity = SpawnOwnedWithBody(Vector3.Zero);
        SetIntent(entity, new Vector3(10f, 0f, 0f)); // magnitude 10

        var motor = new BulletCharacterMotor(
            _fakeService, _lifecycle,
            stanceResolver: _ => CharacterStance.Crouched);
        motor.CrouchedMultiplier = 0.6f;

        _fakeService.VelocityCalls.Clear();
        motor.Execute(_world, 1f / 60f);

        Assert.Single(_fakeService.VelocityCalls);
        float actualMag = _fakeService.VelocityCalls[0].Velocity.Length();
        Assert.Equal(6f, actualMag, precision: 4); // 10 * 0.6 = 6
    }

    // ── T3-SC3: jump gated by IsGrounded ─────────────────────────────────────

    /// <summary>
    /// Jump intent + entity IS grounded → <c>Jump</c> must be called exactly once.
    /// </summary>
    [Fact]
    public void JumpIntent_WhenGrounded_JumpCalled()
    {
        var entity = SpawnOwnedWithBody(Vector3.Zero);
        string handle = GetBodyHandle(entity);

        // Script the fake: this body is grounded.
        _fakeService.GroundedMap[handle] = true;

        SetIntent(entity, new Vector3(1f, 0f, 0f), jump: true);
        _fakeService.JumpCalls.Clear();
        _fakeService.IsGroundedCalls.Clear();

        Run();

        Assert.Single(_fakeService.JumpCalls);
        Assert.Equal(handle, _fakeService.JumpCalls[0].Handle);
    }

    /// <summary>
    /// Jump intent + entity is NOT grounded → <c>Jump</c> must NOT be called.
    /// </summary>
    [Fact]
    public void JumpIntent_WhenNotGrounded_JumpNotCalled()
    {
        var entity = SpawnOwnedWithBody(Vector3.Zero);
        string handle = GetBodyHandle(entity);

        // Script the fake: not grounded.
        _fakeService.GroundedMap[handle] = false;

        SetIntent(entity, new Vector3(1f, 0f, 0f), jump: true);
        _fakeService.JumpCalls.Clear();

        Run();

        Assert.Empty(_fakeService.JumpCalls);
    }

    /// <summary>
    /// No jump intent → <c>IsGrounded</c> must NOT be queried and <c>Jump</c> must NOT be called.
    /// </summary>
    [Fact]
    public void NoJumpIntent_IsGroundedNotQueried_JumpNotCalled()
    {
        var entity = SpawnOwnedWithBody(Vector3.Zero);

        SetIntent(entity, new Vector3(1f, 0f, 0f), jump: false);
        _fakeService.JumpCalls.Clear();
        _fakeService.IsGroundedCalls.Clear();

        Run();

        Assert.Empty(_fakeService.JumpCalls);
        Assert.Empty(_fakeService.IsGroundedCalls);
    }

    // ── T3-SC4: zero intent → zero velocity passed to service ─────────────────

    /// <summary>
    /// A zero-velocity intent must pass a zero vector to <c>SetCharacterVelocity</c>.
    /// </summary>
    [Fact]
    public void ZeroVelocityIntent_ZeroPassedToService()
    {
        var entity = SpawnOwnedWithBody(Vector3.Zero);
        SetIntent(entity, Vector3.Zero);
        _fakeService.VelocityCalls.Clear();

        Run();

        Assert.Single(_fakeService.VelocityCalls);
        Assert.Equal(0f, _fakeService.VelocityCalls[0].Velocity.Length(), precision: 6);
    }

    // ── T3-SC5: entity without body reference is skipped ─────────────────────

    /// <summary>
    /// An entity with a <see cref="CrowdMotorIntent"/> but no <see cref="PhysicsBodyReference"/>
    /// is silently skipped — no service calls.
    /// </summary>
    [Fact]
    public void EntityWithIntentButNoBodyRef_Skipped()
    {
        // Create entity without going through the body lifecycle
        var entity = _world.CreateEntity();
        _world.AddComponent(entity, new SimTransform());
        _world.AddComponent(entity, new CrowdMotorIntent { Velocity = new Vector3(5f, 0f, 0f) });
        _world.SetAuthority<SimTransform>(entity, true);

        _fakeService.VelocityCalls.Clear();

        Run();

        Assert.Empty(_fakeService.VelocityCalls);
    }

    // ── T3-SC6: GetMultiplier helper ──────────────────────────────────────────

    /// <summary>
    /// <see cref="BulletCharacterMotor.GetMultiplier"/> returns the correct
    /// configured values for each stance.
    /// </summary>
    [Fact]
    public void GetMultiplier_ReturnsConfiguredValues()
    {
        _sut.StandingMultiplier = 1.0f;
        _sut.CrouchedMultiplier = 0.5f;
        _sut.ProneMultiplier    = 0.25f;

        Assert.Equal(1.0f,  _sut.GetMultiplier(CharacterStance.Standing));
        Assert.Equal(0.5f,  _sut.GetMultiplier(CharacterStance.Crouched));
        Assert.Equal(0.25f, _sut.GetMultiplier(CharacterStance.Prone));
    }

    // ── T3-SC7: PostCollisionLinearVelocityFdp is written (FIX-1 regression) ──

    /// <summary>
    /// After <see cref="BulletCharacterMotor.Execute"/>, the entity's
    /// <see cref="PhysicsBodyReference.PostCollisionLinearVelocityFdp"/> must equal
    /// the FDP-space intent velocity scaled by the stance multiplier.
    ///
    /// <para>
    /// Root cause of the bug: the motor called <c>SetCharacterVelocity</c> but never wrote
    /// the post-collision velocity channel, so <c>BulletReverseSyncSystem</c> read zero →
    /// <c>SimVelocity.Linear = 0</c> → <c>StrideAnimationBridge</c> saw idle speed → no walk
    /// blend while the character was physically moving.
    /// </para>
    /// </summary>
    [Fact]
    public void Execute_WritesPostCollisionLinearVelocityFdp_EqualToScaledIntentVelocity()
    {
        // Arrange: FDP velocity = (3, 4, 0) — magnitude 5, horizontal walk direction.
        var fdpVel = new Vector3(3f, 4f, 0f);
        var entity = SpawnOwnedWithBody(Vector3.Zero);
        SetIntent(entity, fdpVel);

        // Act
        Run();

        // Assert: PostCollisionLinearVelocityFdp must equal the FDP intent (Standing mult=1.0).
        var bodyRef = _lifecycle.Bodies[entity];
        Assert.Equal(fdpVel.X, bodyRef.PostCollisionLinearVelocityFdp.X, precision: 4);
        Assert.Equal(fdpVel.Y, bodyRef.PostCollisionLinearVelocityFdp.Y, precision: 4);
        Assert.Equal(fdpVel.Z, bodyRef.PostCollisionLinearVelocityFdp.Z, precision: 4);
    }

    /// <summary>
    /// After <see cref="BulletCharacterMotor.Execute"/>, the entity's
    /// <see cref="PhysicsBodyReference.PostCollisionAngularVelocityFdp"/> must be zero
    /// (character controllers do not rotate via angular velocity).
    /// </summary>
    [Fact]
    public void Execute_WritesPostCollisionAngularVelocityFdp_AsZero()
    {
        var entity = SpawnOwnedWithBody(Vector3.Zero);
        SetIntent(entity, new Vector3(2f, 1f, 0f));

        Run();

        var bodyRef = _lifecycle.Bodies[entity];
        Assert.Equal(0f, bodyRef.PostCollisionAngularVelocityFdp.X, precision: 6);
        Assert.Equal(0f, bodyRef.PostCollisionAngularVelocityFdp.Y, precision: 6);
        Assert.Equal(0f, bodyRef.PostCollisionAngularVelocityFdp.Z, precision: 6);
    }

    /// <summary>
    /// Stance multiplier is applied BEFORE writing <see cref="PhysicsBodyReference.PostCollisionLinearVelocityFdp"/>.
    /// Crouched (0.5×) intent of 4 m/s must produce velocity 2 m/s in the channel.
    /// </summary>
    [Fact]
    public void Execute_CrouchedStance_PostCollisionVelocity_IsHalfOfIntent()
    {
        var entity = SpawnOwnedWithBody(Vector3.Zero);
        SetIntent(entity, new Vector3(4f, 0f, 0f)); // 4 m/s east

        var motor = new BulletCharacterMotor(
            _fakeService, _lifecycle,
            stanceResolver: _ => CharacterStance.Crouched);
        motor.CrouchedMultiplier = 0.5f;

        motor.Execute(_world, 1f / 60f);

        var bodyRef = _lifecycle.Bodies[entity];
        // 4 * 0.5 = 2 m/s in FDP X (East).
        Assert.Equal(2f, bodyRef.PostCollisionLinearVelocityFdp.X, precision: 4);
        Assert.Equal(0f, bodyRef.PostCollisionLinearVelocityFdp.Y, precision: 4);
        Assert.Equal(0f, bodyRef.PostCollisionLinearVelocityFdp.Z, precision: 4);
    }

    /// <summary>
    /// A zero-velocity intent writes zero to <see cref="PhysicsBodyReference.PostCollisionLinearVelocityFdp"/>.
    /// This satisfies the velocity invariant: stopped character → SimVelocity=0 → idle blend.
    /// </summary>
    [Fact]
    public void Execute_ZeroVelocityIntent_PostCollisionVelocityIsZero()
    {
        var entity = SpawnOwnedWithBody(Vector3.Zero);
        SetIntent(entity, Vector3.Zero);

        Run();

        var bodyRef = _lifecycle.Bodies[entity];
        Assert.Equal(0f, bodyRef.PostCollisionLinearVelocityFdp.Length(), precision: 6);
    }
}
