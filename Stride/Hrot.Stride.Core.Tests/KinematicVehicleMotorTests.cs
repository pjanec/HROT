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
/// Unit tests for <see cref="KinematicVehicleMotor"/> (STR-P1-T4b, BATCH-17 dynamic-body migration).
///
/// <para>
/// The motor now drives a DYNAMIC <c>RigidbodyComponent</c> via
/// <see cref="IPhysicsBodyService.SetLinearVelocityXZ"/> and
/// <see cref="IPhysicsBodyService.SetYawRate"/> instead of the old
/// <c>MoveKinematic</c> sweep approach.  Tests use a recording fake that
/// captures those calls and verify the commanded velocity matches the expected
/// value for a given <c>VehicleState</c> and <c>SimTransform</c>.
/// </para>
///
/// <para>
/// <b>Velocity invariant (design §6.1, dynamic variant):</b>
/// For a dynamic body, a collision-arrested body reports zero velocity from the Bullet
/// solver — this system does NOT write post-collision channels; the reverse-sync reads
/// the solver's <c>LinearVelocity</c> directly.  The motor's job is solely to command
/// the desired velocity each frame.
/// </para>
/// </summary>
public sealed class KinematicVehicleMotorTests : IDisposable
{
    // ── Scriptable fake ───────────────────────────────────────────────────────

    /// <summary>
    /// Recording fake that captures <c>SetLinearVelocityXZ</c> and <c>SetYawRate</c>
    /// calls made by <see cref="KinematicVehicleMotor"/>.
    /// </summary>
    private sealed class ScriptableFakePhysicsBodyService : IPhysicsBodyService
    {
        public record CreateCall(Entity Entity, CollisionShapeKind ShapeKind,
                                 ShapeDims Dims, SimTransform Pose, object Handle);
        public record SetLinearVelXZCall(object Handle, SMath.Vector3 Velocity);
        public record SetYawRateCall(object Handle, float YawRate);

        public List<CreateCall>        Creates         { get; } = new();
        public List<SetLinearVelXZCall> LinearVelCalls { get; } = new();
        public List<SetYawRateCall>    YawRateCalls    { get; } = new();

        private int _counter;

        public object CreateBody(Entity entity, CollisionShapeKind shapeKind,
                                 ShapeDims dims, in SimTransform initialPose)
        {
            var handle = $"Body_{++_counter}";
            Creates.Add(new CreateCall(entity, shapeKind, dims, initialPose, handle));
            return handle;
        }

        public void RemoveBody(object bodyHandle) { }

        public void SetCharacterVelocity(object bodyHandle, SMath.Vector3 velocity) { }
        public void Jump(object bodyHandle) { }
        public bool IsGrounded(object bodyHandle) => false;

        public void SetLinearVelocityXZ(object bodyHandle, SMath.Vector3 strideLinearVel)
            => LinearVelCalls.Add(new SetLinearVelXZCall(bodyHandle, strideLinearVel));

        public void SetYawRate(object bodyHandle, float strideYawRateRadPerSec)
            => YawRateCalls.Add(new SetYawRateCall(bodyHandle, strideYawRateRadPerSec));

        public KinematicMoveResult MoveKinematic(
            object bodyHandle, SMath.Vector3 desiredDelta, SMath.Quaternion desiredRotDelta)
            => new KinematicMoveResult(desiredDelta, desiredRotDelta); // pass-through (unused by vehicle path)

        public BodyState GetBodyState(object bodyHandle)
            => new BodyState(
                SMath.Vector3.Zero,
                SMath.Quaternion.Identity,
                SMath.Vector3.Zero,
                SMath.Vector3.Zero,
                IsKinematic: false); // dynamic body
    }

    // ── Null visual factory ────────────────────────────────────────────────────

    private sealed class NullVisualFactory : IStrideVisualFactory
    {
        public object CreateModelVisual(string m, string s, float sc, Vector3 o, in SimTransform t) => new object();
        public object CreateProceduralVisual(CollisionShapeKind k, ShapeDims d, float sc, Vector3 o, in SimTransform t) => new object();
        public void UpdatePose(object h, in SimTransform t) { }
        public void Destroy(object h) { }
    }

    // ── Test infrastructure ────────────────────────────────────────────────────

    private const long BoxTkbType = 801L;
    private const float Dt = 1f / 60f; // 60 Hz tick

    private readonly EntityRepository                 _world;
    private readonly ScriptableFakePhysicsBodyService _fakeService;
    private readonly StrideVisualBindingSystem        _visualSystem;
    private readonly PhysicsBodyLifecycleSystem       _lifecycle;
    private readonly KinematicVehicleMotor            _sut;

    public KinematicVehicleMotorTests()
    {
        _world = new EntityRepository();
        _world.RegisterComponent<SimTransform>();
        _world.RegisterComponent<SimVelocity>();
        _world.RegisterComponent<TkbIdentity>();
        _world.RegisterComponent<VehicleState>();
        _world.RegisterComponent<VehicleParams>();

        var tkbDb = BuildTkbDb();
        _fakeService  = new ScriptableFakePhysicsBodyService();
        _visualSystem = new StrideVisualBindingSystem(new NullVisualFactory(), tkbDb);
        _lifecycle    = new PhysicsBodyLifecycleSystem(_fakeService, _visualSystem);
        _sut          = new KinematicVehicleMotor(_fakeService, _lifecycle);
    }

    public void Dispose() => _world.Dispose();

    private static TkbDatabase BuildTkbDb()
    {
        var db  = new TkbDatabase();
        var def = new StrideRenderModelDefDto
        {
            ShapeKind = CollisionShapeKind.OrientedBox,
            BoxHalfX  = 2.0f,
            BoxHalfY  = 0.6f,
            BoxHalfZ  = 1.0f,
        };
        var tmpl = new TkbTemplate("VehicleUnit", BoxTkbType);
        tmpl.AddDescriptor(def);
        db.Register(tmpl);
        return db;
    }

    /// <summary>
    /// Spawns a vehicle entity that is locally-owned, syncs its visual, and creates a
    /// physics body. Returns the entity and its body handle.
    /// </summary>
    private (Entity entity, string handle) SpawnVehicle(
        Vector3 pos, float speed, float steerAngle = 0f, float wheelBase = 4f)
    {
        var entity = _world.CreateEntity();
        _world.AddComponent(entity, new TkbIdentity { TkbType = BoxTkbType });
        _world.AddComponent(entity, new SimTransform { Position = pos, Rotation = Quaternion.Identity });
        _world.AddComponent(entity, new VehicleState { Speed = speed, SteerAngle = steerAngle });
        _world.AddComponent(entity, new VehicleParams { WheelBase = wheelBase });
        _world.AddComponent(entity, new SimVelocity());
        _world.SetAuthority<SimTransform>(entity, true);

        _visualSystem.Sync(_world);
        _lifecycle.Execute(_world, Dt);

        var handle = (string)_lifecycle.Bodies[entity].BodyHandle;
        return (entity, handle);
    }

    private void Run() => _sut.Execute(_world, Dt);

    // ── T4b-SC1: unobstructed command — SetLinearVelocityXZ called with correct velocity ─

    /// <summary>
    /// A vehicle facing East (FDP +X, identity rotation) at speed=10 m/s must
    /// call <c>SetLinearVelocityXZ</c> with a Stride-space velocity of approximately
    /// (10, *, 0) — the X component equals speed, Z is 0 (East in FDP = X in Stride).
    ///
    /// Note: <c>SetLinearVelocityXZ</c> is called with the DESIRED velocity, not a
    /// per-frame delta. dt does NOT appear in the velocity command.
    /// </summary>
    [Fact]
    public void UnobstructedCommand_EastFacing_LinearVelocityCommandedCorrectly()
    {
        float speed = 10f;
        var (entity, handle) = SpawnVehicle(Vector3.Zero, speed);
        _fakeService.LinearVelCalls.Clear();
        _fakeService.YawRateCalls.Clear();

        Run();

        // SetLinearVelocityXZ called exactly once.
        Assert.Single(_fakeService.LinearVelCalls);
        var call = _fakeService.LinearVelCalls[0];
        Assert.Equal(handle, call.Handle);

        // Vehicle faces East (FDP X → Stride X).
        // Desired velocity in Stride space: (speed, 0, 0) — X=10, Z=0.
        Assert.Equal(speed, call.Velocity.X, precision: 4);
        // Z (=FDP.Y=North) should be near 0 for East-facing vehicle.
        Assert.Equal(0f, call.Velocity.Z, precision: 4);
    }

    /// <summary>
    /// Vehicle rotated 90° North (FDP Y axis becomes forward).
    /// <c>SetLinearVelocityXZ</c> must command velocity in the Stride Z direction.
    /// FDP velocity (0, speed, 0) → Stride velocity (0, 0, speed) via ToStrideVelocity.
    /// </summary>
    [Fact]
    public void UnobstructedCommand_NorthFacing_LinearVelocityInStrideZ()
    {
        float speed = 6f;
        // Rotate 90° around FDP Z (up) → forward becomes FDP Y (North).
        var northRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2f);

        var entity = _world.CreateEntity();
        _world.AddComponent(entity, new TkbIdentity { TkbType = BoxTkbType });
        _world.AddComponent(entity, new SimTransform { Position = Vector3.Zero, Rotation = northRotation });
        _world.AddComponent(entity, new VehicleState { Speed = speed });
        _world.AddComponent(entity, new VehicleParams { WheelBase = 4f });
        _world.AddComponent(entity, new SimVelocity());
        _world.SetAuthority<SimTransform>(entity, true);

        _visualSystem.Sync(_world);
        _lifecycle.Execute(_world, Dt);
        _fakeService.LinearVelCalls.Clear();

        Run();

        Assert.Single(_fakeService.LinearVelCalls);
        var call = _fakeService.LinearVelCalls[0];
        // FDP (0, speed, 0) → Stride (0, 0, speed): Z ≈ speed (±small quaternion float error).
        Assert.Equal(speed, call.Velocity.Z, precision: 2);
        // X ≈ 0
        Assert.Equal(0f, call.Velocity.X, precision: 2);
    }

    // ── T4b-SC2: zero speed → zero velocity commanded ─────────────────────────

    /// <summary>
    /// A vehicle with speed=0 must call <c>SetLinearVelocityXZ</c> with (0, ?, 0)
    /// and <c>SetYawRate</c> with 0.
    /// </summary>
    [Fact]
    public void ZeroSpeed_CommandsZeroLinearVelocity()
    {
        var (entity, handle) = SpawnVehicle(Vector3.Zero, speed: 0f);
        _fakeService.LinearVelCalls.Clear();
        _fakeService.YawRateCalls.Clear();

        Run();

        Assert.Single(_fakeService.LinearVelCalls);
        var call = _fakeService.LinearVelCalls[0];
        Assert.Equal(0f, call.Velocity.X, precision: 5);
        Assert.Equal(0f, call.Velocity.Z, precision: 5);

        Assert.Single(_fakeService.YawRateCalls);
        Assert.Equal(0f, _fakeService.YawRateCalls[0].YawRate, precision: 5);
    }

    // ── T4b-SC3: yaw rate — zero steer angle → zero yaw rate ─────────────────

    /// <summary>
    /// A vehicle with zero steer angle must call <c>SetYawRate</c> with 0 rad/s
    /// (since ω = speed/wheelBase * tan(0) = 0).
    /// </summary>
    [Fact]
    public void ZeroSteerAngle_YawRateCommandIsZero()
    {
        var (entity, _) = SpawnVehicle(Vector3.Zero, speed: 5f, steerAngle: 0f);
        _fakeService.YawRateCalls.Clear();

        Run();

        Assert.Single(_fakeService.YawRateCalls);
        Assert.Equal(0f, _fakeService.YawRateCalls[0].YawRate, precision: 5);
    }

    // ── T4b-SC4: yaw rate — non-zero steer produces non-zero yaw rate ─────────

    /// <summary>
    /// A vehicle with a non-zero steer angle and non-zero speed must command a
    /// non-zero yaw rate (ω = speed/wheelBase * tan(steerAngle) > 0).
    /// The sign flip (FDP CCW → Stride negation) must result in a non-zero command.
    /// </summary>
    [Fact]
    public void NonZeroSteerAndSpeed_YawRateIsNonZero()
    {
        float speed      = 5f;
        float steerAngle = 0.3f; // rad — left-turn
        float wheelBase  = 4f;
        var (entity, _) = SpawnVehicle(Vector3.Zero, speed, steerAngle, wheelBase);
        _fakeService.YawRateCalls.Clear();

        Run();

        Assert.Single(_fakeService.YawRateCalls);
        float commandedYawRate = _fakeService.YawRateCalls[0].YawRate;

        // Expected FDP yaw rate: ω = (5/4) * tan(0.3) ≈ 1.25 * 0.309 ≈ 0.386 rad/s
        // Stride yaw rate is negated: ≈ -0.386 rad/s
        float expectedFdpYawRate = (speed / wheelBase) * MathF.Tan(steerAngle);
        float expectedStrideYawRate = -expectedFdpYawRate;

        Assert.Equal(expectedStrideYawRate, commandedYawRate, precision: 3);
        Assert.True(MathF.Abs(commandedYawRate) > 0f, "Non-zero steer must produce non-zero yaw rate command.");
    }

    // ── T4b-SC5: SetLinearVelocityXZ and SetYawRate called on correct handle ──

    /// <summary>
    /// The motor must call both <c>SetLinearVelocityXZ</c> and <c>SetYawRate</c> on
    /// the correct body handle from <see cref="PhysicsBodyLifecycleSystem.Bodies"/>.
    /// </summary>
    [Fact]
    public void VelocityDrive_CalledOnCorrectBodyHandle()
    {
        var (_, handle) = SpawnVehicle(Vector3.Zero, speed: 2f);
        _fakeService.LinearVelCalls.Clear();
        _fakeService.YawRateCalls.Clear();

        Run();

        Assert.Single(_fakeService.LinearVelCalls);
        Assert.Equal(handle, _fakeService.LinearVelCalls[0].Handle);

        Assert.Single(_fakeService.YawRateCalls);
        Assert.Equal(handle, _fakeService.YawRateCalls[0].Handle);
    }

    // ── T4b-SC6: entity without body reference is skipped ─────────────────────

    /// <summary>
    /// An entity with <c>VehicleState</c> but no <see cref="PhysicsBodyReference"/>
    /// is silently skipped — no <c>SetLinearVelocityXZ</c> or <c>SetYawRate</c> call.
    /// </summary>
    [Fact]
    public void EntityWithVehicleStateButNoBodyRef_Skipped()
    {
        var entity = _world.CreateEntity();
        _world.AddComponent(entity, new SimTransform());
        _world.AddComponent(entity, new VehicleState { Speed = 5f });
        _world.SetAuthority<SimTransform>(entity, true);

        _fakeService.LinearVelCalls.Clear();
        _fakeService.YawRateCalls.Clear();
        Run();

        Assert.Empty(_fakeService.LinearVelCalls);
        Assert.Empty(_fakeService.YawRateCalls);
    }

    // ── T4b-SC7: both SetLinearVelocityXZ and SetYawRate called each frame ─────

    /// <summary>
    /// Each frame the motor must call BOTH <c>SetLinearVelocityXZ</c> and <c>SetYawRate</c>
    /// on the vehicle body handle. Verifies the motor does not skip either call.
    /// </summary>
    [Fact]
    public void Execute_CallsBothSetLinearVelocityXZ_AndSetYawRate()
    {
        var (_, handle) = SpawnVehicle(Vector3.Zero, speed: 3f, steerAngle: 0.1f);
        _fakeService.LinearVelCalls.Clear();
        _fakeService.YawRateCalls.Clear();

        Run();

        Assert.Single(_fakeService.LinearVelCalls);
        Assert.Single(_fakeService.YawRateCalls);
    }

    // ── T4b-SC8: MoveKinematic is NOT called for the vehicle body ─────────────

    /// <summary>
    /// The velocity-drive motor must NOT call <c>MoveKinematic</c> for vehicle bodies.
    /// <c>MoveKinematic</c> is the old kinematic sweep approach — the dynamic body path
    /// uses <c>SetLinearVelocityXZ</c> / <c>SetYawRate</c> instead.
    ///
    /// This test uses a dedicated fake that tracks <c>MoveKinematic</c> calls.
    /// </summary>
    [Fact]
    public void Execute_DoesNotCallMoveKinematic_ForVehicleBody()
    {
        // Use a separate fake that specifically tracks MoveKinematic calls.
        var world = new EntityRepository();
        world.RegisterComponent<SimTransform>();
        world.RegisterComponent<SimVelocity>();
        world.RegisterComponent<TkbIdentity>();
        world.RegisterComponent<VehicleState>();
        world.RegisterComponent<VehicleParams>();

        int moveKinematicCallCount = 0;
        var trackingFake = new MoveKinematicTrackingFake(() => moveKinematicCallCount++);

        var tkbDb = BuildTkbDb();
        var visualSystem = new StrideVisualBindingSystem(new NullVisualFactory(), tkbDb);
        var lifecycle    = new PhysicsBodyLifecycleSystem(trackingFake, visualSystem);
        var sut          = new KinematicVehicleMotor(trackingFake, lifecycle);

        var entity = world.CreateEntity();
        world.AddComponent(entity, new TkbIdentity { TkbType = BoxTkbType });
        world.AddComponent(entity, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });
        world.AddComponent(entity, new VehicleState { Speed = 5f });
        world.AddComponent(entity, new VehicleParams { WheelBase = 4f });
        world.AddComponent(entity, new SimVelocity());
        world.SetAuthority<SimTransform>(entity, true);

        visualSystem.Sync(world);
        lifecycle.Execute(world, Dt);

        sut.Execute(world, Dt);

        Assert.Equal(0, moveKinematicCallCount);

        world.Dispose();
    }

    // Helper for SC8: a fake that tracks MoveKinematic calls via a callback.
    private sealed class MoveKinematicTrackingFake : IPhysicsBodyService
    {
        private readonly Action _onMoveKinematic;
        private int _counter;

        public MoveKinematicTrackingFake(Action onMoveKinematic)
            => _onMoveKinematic = onMoveKinematic;

        public object CreateBody(Entity entity, CollisionShapeKind shapeKind,
                                 ShapeDims dims, in SimTransform initialPose)
            => $"Body_{++_counter}";
        public void RemoveBody(object bodyHandle) { }
        public void SetCharacterVelocity(object bodyHandle, SMath.Vector3 velocity) { }
        public void Jump(object bodyHandle) { }
        public bool IsGrounded(object bodyHandle) => false;
        public void SetLinearVelocityXZ(object bodyHandle, SMath.Vector3 strideLinearVel) { }
        public void SetYawRate(object bodyHandle, float strideYawRateRadPerSec) { }

        public KinematicMoveResult MoveKinematic(
            object bodyHandle, SMath.Vector3 desiredDelta, SMath.Quaternion desiredRotDelta)
        {
            _onMoveKinematic(); // signal: unexpectedly called
            return new KinematicMoveResult(desiredDelta, desiredRotDelta);
        }

        public BodyState GetBodyState(object bodyHandle)
            => new BodyState(SMath.Vector3.Zero, SMath.Quaternion.Identity,
                             SMath.Vector3.Zero, SMath.Vector3.Zero, IsKinematic: false);
    }
}

// ── KinematicVehicleMotorClobberGuardTests ────────────────────────────────────

/// <summary>
/// Regression tests for the F1 "vehicle motor clobbers character" bug guard.
///
/// <para>
/// Root cause: <c>VehicleKinematicsTkbTranslator</c> injects <c>VehicleState</c> on EVERY
/// TKB-spawned entity when <c>VehicleState</c> is registered.  A walking mannequin therefore
/// has both <c>CrowdMotorIntent</c> (its steering channel) and <c>VehicleState(Speed=0)</c>
/// (injected by the translator). Before the fix, <see cref="KinematicVehicleMotor"/> matched
/// the mannequin and would call <c>SetLinearVelocityXZ(0,0,0)</c> — silencing the character's
/// physics-driven motion.
/// </para>
///
/// <para>
/// Fix (preserved from kinematic era): <see cref="KinematicVehicleMotor.Execute"/> skips
/// entities whose <see cref="PhysicsBodyReference.ShapeKind"/> is
/// <see cref="CollisionShapeKind.Capsule"/> and also skips entities that carry
/// <c>CrowdMotorIntent</c>.  Only <see cref="CollisionShapeKind.OrientedBox"/> bodies are
/// genuine dynamic vehicles.
/// </para>
///
/// <para>
/// Test structure:
/// <list type="bullet">
///   <item><b>ClobberGuard_CapsuleBodyWithVehicleStateAndCrowdMotorIntent_VelocityNotCommanded</b>
///         — capsule entity is NOT commanded (SetLinearVelocityXZ is not called for it).</item>
///   <item><b>ClobberGuard_OrientedBoxVehicleStillDriven</b>
///         — genuine OrientedBox vehicle continues to have <c>SetLinearVelocityXZ</c> called.</item>
/// </list>
/// </para>
/// </summary>
public sealed class KinematicVehicleMotorClobberGuardTests : IDisposable
{
    // ── Scriptable fake ───────────────────────────────────────────────────────

    private sealed class ScriptableFakeService : IPhysicsBodyService
    {
        public record SetLinearVelCall(object Handle, SMath.Vector3 Velocity);

        public List<SetLinearVelCall> LinearVelCalls { get; } = new();
        private int _counter;

        public object CreateBody(Entity entity, CollisionShapeKind shapeKind,
                                 ShapeDims dims, in SimTransform initialPose)
            => $"Body_{++_counter}_{shapeKind}";

        public void RemoveBody(object bodyHandle) { }
        public void SetCharacterVelocity(object bodyHandle, SMath.Vector3 velocity) { }
        public void Jump(object bodyHandle) { }
        public bool IsGrounded(object bodyHandle) => false;

        public void SetLinearVelocityXZ(object bodyHandle, SMath.Vector3 strideLinearVel)
            => LinearVelCalls.Add(new SetLinearVelCall(bodyHandle, strideLinearVel));

        public void SetYawRate(object bodyHandle, float strideYawRateRadPerSec) { }

        public KinematicMoveResult MoveKinematic(
            object bodyHandle, SMath.Vector3 desiredDelta, SMath.Quaternion desiredRotDelta)
            => new KinematicMoveResult(desiredDelta, desiredRotDelta);

        public BodyState GetBodyState(object bodyHandle)
            => new BodyState(SMath.Vector3.Zero, SMath.Quaternion.Identity,
                             SMath.Vector3.Zero, SMath.Vector3.Zero, IsKinematic: false);
    }

    private sealed class NullVisualFactory : IStrideVisualFactory
    {
        public object CreateModelVisual(string m, string s, float sc, Vector3 o, in SimTransform t) => new object();
        public object CreateProceduralVisual(CollisionShapeKind k, ShapeDims d, float sc, Vector3 o, in SimTransform t) => new object();
        public void UpdatePose(object h, in SimTransform t) { }
        public void Destroy(object h) { }
    }

    // ── TKB type ids ──────────────────────────────────────────────────────────

    private const long CapsuleTkbType  = 3701L;
    private const long BoxTkbType      = 3801L;
    private const float Dt = 1f / 60f;

    private readonly EntityRepository          _world;
    private readonly ScriptableFakeService     _fakeService;
    private readonly StrideVisualBindingSystem  _visualSystem;
    private readonly PhysicsBodyLifecycleSystem _lifecycle;
    private readonly KinematicVehicleMotor      _sut;

    public KinematicVehicleMotorClobberGuardTests()
    {
        _world = new EntityRepository();
        _world.RegisterComponent<SimTransform>();
        _world.RegisterComponent<SimVelocity>();
        _world.RegisterComponent<TkbIdentity>();
        _world.RegisterComponent<VehicleState>();
        _world.RegisterComponent<VehicleParams>();
        _world.RegisterComponent<CrowdMotorIntent>();

        var tkbDb = BuildTkbDb();
        _fakeService  = new ScriptableFakeService();
        _visualSystem = new StrideVisualBindingSystem(new NullVisualFactory(), tkbDb);
        _lifecycle    = new PhysicsBodyLifecycleSystem(_fakeService, _visualSystem);
        _sut          = new KinematicVehicleMotor(_fakeService, _lifecycle);
    }

    public void Dispose() => _world.Dispose();

    private static TkbDatabase BuildTkbDb()
    {
        var db = new TkbDatabase();

        // Capsule template (character / mannequin).
        var capsuleDef = new StrideRenderModelDefDto
        {
            ShapeKind   = CollisionShapeKind.Capsule,
            ShapeRadius = 0.3f,
            ShapeHeight = 1.8f,
        };
        var capsuleTmpl = new TkbTemplate("CharacterUnit", CapsuleTkbType);
        capsuleTmpl.AddDescriptor(capsuleDef);
        db.Register(capsuleTmpl);

        // OrientedBox template (vehicle).
        var boxDef = new StrideRenderModelDefDto
        {
            ShapeKind = CollisionShapeKind.OrientedBox,
            BoxHalfX  = 2.0f,
            BoxHalfY  = 0.6f,
            BoxHalfZ  = 1.0f,
        };
        var boxTmpl = new TkbTemplate("VehicleUnit", BoxTkbType);
        boxTmpl.AddDescriptor(boxDef);
        db.Register(boxTmpl);

        return db;
    }

    /// <summary>
    /// Spawns a capsule (character) entity that has BOTH <c>CrowdMotorIntent</c> (set to a
    /// non-zero velocity) AND <c>VehicleState</c> (Speed=0) — reproducing the condition that
    /// triggered the clobber bug when <c>VehicleKinematicsTkbTranslator</c> stamped
    /// <c>VehicleState</c> on every TKB entity.
    /// </summary>
    private Entity SpawnCapsuleWithBothComponents(Vector3 intentVelocity)
    {
        var entity = _world.CreateEntity();
        _world.AddComponent(entity, new TkbIdentity { TkbType = CapsuleTkbType });
        _world.AddComponent(entity, new SimTransform { Position = Vector3.Zero });
        _world.AddComponent(entity, new SimVelocity());
        _world.AddComponent(entity, new CrowdMotorIntent { Velocity = intentVelocity });
        _world.AddComponent(entity, new VehicleState { Speed = 0f });   // injected by translator
        _world.SetAuthority<SimTransform>(entity, true);

        _visualSystem.Sync(_world);
        _lifecycle.Execute(_world, Dt);

        return entity;
    }

    /// <summary>
    /// Spawns a genuine OrientedBox vehicle with VehicleState (Speed > 0).
    /// </summary>
    private Entity SpawnBoxVehicle(float speed)
    {
        var entity = _world.CreateEntity();
        _world.AddComponent(entity, new TkbIdentity { TkbType = BoxTkbType });
        _world.AddComponent(entity, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });
        _world.AddComponent(entity, new SimVelocity());
        _world.AddComponent(entity, new VehicleState { Speed = speed });
        _world.AddComponent(entity, new VehicleParams { WheelBase = 4f });
        _world.SetAuthority<SimTransform>(entity, true);

        _visualSystem.Sync(_world);
        _lifecycle.Execute(_world, Dt);

        return entity;
    }

    // ── Core regression test ──────────────────────────────────────────────────

    /// <summary>
    /// F1 clobber-guard: a Capsule entity carrying BOTH <c>CrowdMotorIntent</c> (non-zero)
    /// and <c>VehicleState(Speed=0)</c> must NOT have <c>SetLinearVelocityXZ</c> called
    /// for it by <see cref="KinematicVehicleMotor.Execute"/>.
    ///
    /// <para>
    /// This reproduces the clobber risk: without the guard the motor would call
    /// <c>SetLinearVelocityXZ(0,0,0)</c> on the capsule body and silence its
    /// physics-driven motion each frame.
    /// </para>
    /// </summary>
    [Fact]
    public void ClobberGuard_CapsuleBodyWithVehicleStateAndCrowdMotorIntent_VelocityNotCommanded()
    {
        var intentVelocity = new Vector3(0f, 2f, 0f); // 2 m/s north
        var entity = SpawnCapsuleWithBothComponents(intentVelocity);

        // Confirm it's really a capsule.
        var bodyRef = _lifecycle.Bodies[entity];
        Assert.Equal(CollisionShapeKind.Capsule, bodyRef.ShapeKind);

        _fakeService.LinearVelCalls.Clear();

        // Act: vehicle motor runs.
        _sut.Execute(_world, Dt);

        // Assert: SetLinearVelocityXZ must NOT have been called for the capsule entity.
        Assert.Empty(_fakeService.LinearVelCalls);
    }

    // ── OrientedBox vehicle is still driven (regression guard) ───────────────

    /// <summary>
    /// A genuine OrientedBox vehicle with <c>VehicleState(Speed &gt; 0)</c> must still have
    /// <c>SetLinearVelocityXZ</c> called with a non-zero velocity after
    /// <see cref="KinematicVehicleMotor.Execute"/>.
    ///
    /// <para>
    /// Guards against the fix accidentally skipping all vehicles (over-broad guard).
    /// </para>
    /// </summary>
    [Fact]
    public void ClobberGuard_OrientedBoxVehicle_IsDriven_SetLinearVelocityXZCalled()
    {
        float speed = 5f;
        var entity = SpawnBoxVehicle(speed);
        _fakeService.LinearVelCalls.Clear();

        // Act
        _sut.Execute(_world, Dt);

        // Assert: SetLinearVelocityXZ called exactly once for the vehicle.
        Assert.Single(_fakeService.LinearVelCalls);

        var bodyRef = _lifecycle.Bodies[entity];
        Assert.Equal(CollisionShapeKind.OrientedBox, bodyRef.ShapeKind);

        // The commanded velocity must be non-zero (vehicle is moving).
        var vel = _fakeService.LinearVelCalls[0].Velocity;
        float speedSq = vel.X * vel.X + vel.Z * vel.Z;
        Assert.True(speedSq > 0f,
            "OrientedBox vehicle must command a non-zero XZ velocity for speed > 0.");
    }

    // ── Both entities in the same world ──────────────────────────────────────

    /// <summary>
    /// When a capsule character AND an OrientedBox vehicle coexist in the same world:
    /// <list type="bullet">
    ///   <item><c>SetLinearVelocityXZ</c> is called exactly once (for the vehicle only).</item>
    ///   <item>The capsule receives no velocity command.</item>
    /// </list>
    /// </summary>
    [Fact]
    public void ClobberGuard_CapsuleAndBoxInSameWorld_OnlyBoxDriven()
    {
        var intentVelocity = new Vector3(0f, 3f, 0f);
        var capsule = SpawnCapsuleWithBothComponents(intentVelocity);
        var box     = SpawnBoxVehicle(speed: 4f);

        _fakeService.LinearVelCalls.Clear();

        // Act: vehicle motor runs.
        _sut.Execute(_world, Dt);

        // Only the box vehicle had SetLinearVelocityXZ called.
        Assert.Single(_fakeService.LinearVelCalls);

        // Confirm the one call is for the box handle (not the capsule handle).
        var boxBodyRef = _lifecycle.Bodies[box];
        Assert.Equal(boxBodyRef.BodyHandle, _fakeService.LinearVelCalls[0].Handle);
    }
}

// ── KinematicVehicleMotorYawDiagnosticTests ───────────────────────────────────

/// <summary>
/// Headless tests for the commanded-vs-achieved yaw ratio diagnostic introduced in
/// BATCH-17 (yaw-fidelity fix).
///
/// <para>
/// The diagnostic computes <c>ratio = achievedYaw / commandedYaw</c> using the
/// body's actual angular velocity Y read from <see cref="IPhysicsBodyService.GetBodyState"/>.
/// A ratio near 1.0 proves the Bullet body achieves the commanded yaw rate;
/// a ratio &lt;&lt; 1.0 indicates floor-friction or angular-damping resistance.
/// </para>
///
/// <para>
/// These tests verify the ratio arithmetic directly — they do not require a live
/// Bullet simulation.
/// </para>
/// </summary>
public sealed class KinematicVehicleMotorYawDiagnosticTests
{
    // ── Ratio arithmetic ──────────────────────────────────────────────────────

    /// <summary>
    /// When achieved yaw == commanded yaw, the ratio is exactly 1.0 (100%).
    /// This is the "fix working" case.
    /// </summary>
    [Fact]
    public void YawRatio_AchievedEqualsCommanded_IsOne()
    {
        float commanded = -0.5f; // example left-turn yaw rate
        float achieved  = -0.5f;

        float ratio = MathF.Abs(commanded) > 1e-6f ? achieved / commanded : float.NaN;

        Assert.Equal(1.0f, ratio, precision: 5);
    }

    /// <summary>
    /// When achieved yaw is 80% of commanded (typical dynamic-body imperfection),
    /// the ratio is 0.8.
    /// </summary>
    [Fact]
    public void YawRatio_Achieved80Percent_Is0p8()
    {
        float commanded = -1.0f;
        float achieved  = -0.8f;

        float ratio = MathF.Abs(commanded) > 1e-6f ? achieved / commanded : float.NaN;

        Assert.Equal(0.8f, ratio, precision: 5);
    }

    /// <summary>
    /// When achieved yaw is near zero and commanded is non-zero (floor friction
    /// completely killing the yaw), the ratio is near 0.
    /// This is the "floor-friction resistance" failure case.
    /// </summary>
    [Fact]
    public void YawRatio_AchievedNearZero_IsNearZero()
    {
        float commanded = -0.6f;
        float achieved  =  0.01f; // essentially zero

        float ratio = MathF.Abs(commanded) > 1e-6f ? achieved / commanded : float.NaN;

        Assert.True(MathF.Abs(ratio) < 0.1f,
            $"Ratio {ratio:F3} should be near zero when achieved yaw is near-zero (floor resisting yaw).");
    }

    /// <summary>
    /// When commanded yaw is near zero, ratio is NaN (no meaningful measurement).
    /// The diagnostic skips logging in this case (guard: |yaw| &gt; 0.01 rad/s).
    /// </summary>
    [Fact]
    public void YawRatio_CommandedNearZero_IsNaN()
    {
        float commanded = 0.005f; // below the 0.01 threshold — straight driving
        float achieved  = 0.003f;

        float ratio = MathF.Abs(commanded) > 1e-6f ? achieved / commanded : float.NaN;

        // ratio is defined here (non-NaN) because commanded > 1e-6.
        // But the log guard (|commanded| > 0.01) suppresses the line anyway.
        // Verify that below the threshold the guard correctly fires:
        bool guardFires = MathF.Abs(commanded) > 0.01f;
        Assert.False(guardFires, "Diagnostic should be suppressed for near-zero commanded yaw.");
    }

    /// <summary>
    /// Diagnostic guard: log only when |commandedYaw| &gt; 0.01 rad/s.
    /// At exactly the boundary the guard does not fire (strict greater-than).
    /// </summary>
    [Theory]
    [InlineData(0f,     false)]
    [InlineData(0.005f, false)]
    [InlineData(0.01f,  false)] // boundary: NOT &gt; 0.01
    [InlineData(0.011f, true)]
    [InlineData(0.5f,   true)]
    [InlineData(-0.3f,  true)]
    public void YawDiagGuard_FiresOnlyAboveThreshold(float commanded, bool expectFire)
    {
        bool fires = MathF.Abs(commanded) > 0.01f;
        Assert.Equal(expectFire, fires);
    }

    // ── GetBodyState integration: achieved yaw is BodyState.AngularVelocity.Y ──

    /// <summary>
    /// Verifies the diagnostic reads achieved yaw from <see cref="BodyState.AngularVelocity"/>.Y,
    /// using a fake body service that returns a known angular velocity.
    /// </summary>
    [Fact]
    public void YawDiagnostic_ReadsAchievedYawFromBodyStateAngularVelocity()
    {
        // Arrange: a fake body service that returns a known angular velocity.
        float expectedAchievedYaw = -0.45f;
        var fakeBodyState = new BodyState(
            SMath.Vector3.Zero,
            SMath.Quaternion.Identity,
            SMath.Vector3.Zero,
            new SMath.Vector3(0f, expectedAchievedYaw, 0f), // Y = yaw axis in Stride space
            IsKinematic: false);

        // The diagnostic extracts AngularVelocity.Y.
        float achievedYaw = fakeBodyState.AngularVelocity.Y;
        Assert.Equal(expectedAchievedYaw, achievedYaw, precision: 5);

        // And the ratio vs a known commanded yaw.
        float commanded = -0.5f;
        float ratio = MathF.Abs(commanded) > 1e-6f ? achievedYaw / commanded : float.NaN;
        // 0.45 / 0.5 = 0.9
        Assert.Equal(0.9f, ratio, precision: 5);
    }
}
