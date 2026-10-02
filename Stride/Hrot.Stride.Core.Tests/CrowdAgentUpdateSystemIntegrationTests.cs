#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;
using CarKinem.Core;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Navigation.Fake;
using Fdp.Toolkit.Navigation.Systems;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Tkb;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.Stride.Core;
using SMath = Stride.Core.Mathematics;
using Xunit;

namespace Hrot.Stride.Core.Tests;

/// <summary>
/// Integration tests for the crowd → motor pipeline (STR-P2-T4):
/// <see cref="CrowdAgentUpdateSystem"/> writes <see cref="CrowdMotorIntent"/> →
/// <see cref="BulletCharacterMotor"/> consumes it.
///
/// <para>
/// Uses <see cref="FakeDtCrowdProvider"/> for deterministic, headless validation.
/// </para>
/// </summary>
public sealed class CrowdAgentUpdateSystemIntegrationTests : IDisposable
{
    // ── Recording fake IPhysicsBodyService ───────────────────────────────────

    private sealed class RecordingPhysicsBodyService : IPhysicsBodyService
    {
        public record SetVelocityCall(object Handle, SMath.Vector3 Velocity);
        public List<SetVelocityCall> VelocityCalls { get; } = new();

        private int _counter;

        public object CreateBody(Entity entity, CollisionShapeKind kind,
                                 ShapeDims dims, in SimTransform pose)
            => $"body_{++_counter}";

        public void RemoveBody(object handle) { }

        public void SetCharacterVelocity(object handle, SMath.Vector3 velocity)
            => VelocityCalls.Add(new SetVelocityCall(handle, velocity));

        public void Jump(object handle) { }
        public bool IsGrounded(object handle) => false;
        public void SetLinearVelocityXZ(object handle, SMath.Vector3 strideLinearVel) { }
        public void SetYawRate(object handle, float strideYawRateRadPerSec) { }

        public KinematicMoveResult MoveKinematic(
            object handle, SMath.Vector3 delta, SMath.Quaternion rot)
            => new KinematicMoveResult(delta, rot);

        public BodyState GetBodyState(object handle)
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

    // ── Infrastructure ────────────────────────────────────────────────────────

    private const long CapsuleTkbType = 901L;

    private readonly EntityRepository               _world;
    private readonly FakeDtCrowdProvider             _crowd;
    private readonly CrowdAgentUpdateSystem          _crowdSystem;
    private readonly RecordingPhysicsBodyService     _bodyService;
    private readonly StrideVisualBindingSystem       _visualSystem;
    private readonly PhysicsBodyLifecycleSystem      _lifecycle;
    private readonly BulletCharacterMotor            _motor;

    public CrowdAgentUpdateSystemIntegrationTests()
    {
        _world = new EntityRepository();
        _world.RegisterComponent<SimTransform>();
        _world.RegisterComponent<SimVelocity>();
        _world.RegisterComponent<NavigationStatus>();
        _world.RegisterComponent<CrowdAgent>();
        _world.RegisterComponent<CrowdMotorIntent>();
        _world.RegisterComponent<TkbIdentity>();

        _crowd        = new FakeDtCrowdProvider();
        _crowdSystem  = new CrowdAgentUpdateSystem(_crowd);

        var tkbDb     = BuildTkbDb();
        _bodyService  = new RecordingPhysicsBodyService();
        _visualSystem = new StrideVisualBindingSystem(new NullVisualFactory(), tkbDb);
        _lifecycle    = new PhysicsBodyLifecycleSystem(_bodyService, _visualSystem);
        _motor        = new BulletCharacterMotor(_bodyService, _lifecycle);
    }

    public void Dispose() => _world.Dispose();

    private static TkbDatabase BuildTkbDb()
    {
        var db   = new TkbDatabase();
        var def  = new StrideRenderModelDefDto
        {
            ShapeKind   = CollisionShapeKind.Capsule,
            ShapeRadius = 0.3f,
            ShapeHeight = 1.8f,
        };
        var tmpl = new TkbTemplate("CrowdUnit", CapsuleTkbType);
        tmpl.AddDescriptor(def);
        db.Register(tmpl);
        return db;
    }

    /// <summary>
    /// Spawns a crowd agent entity with a Bullet body.
    /// Mirrors the pattern in BulletCharacterMotorTests.
    /// </summary>
    private Entity SpawnCrowdAgent(Vector3 pos)
    {
        var entity = _world.CreateEntity();
        _world.AddComponent(entity, new TkbIdentity { TkbType = CapsuleTkbType });
        _world.AddComponent(entity, new SimTransform { Position = pos });
        _world.AddComponent(entity, new SimVelocity());
        _world.AddComponent(entity, new NavigationStatus { Phase = NavigationPhase.Following });
        _world.AddComponent(entity, default(CrowdAgent));
        _world.AddComponent(entity, new CrowdMotorIntent());
        _world.SetAuthority<SimTransform>(entity, true);

        // Materialise the visual reference (gives shape info to lifecycle).
        _visualSystem.Sync(_world);
        // Materialise the Bullet body.
        _lifecycle.Execute(_world, 1f / 60f);

        return entity;
    }

    // ── Integration test: CrowdAgentUpdateSystem → BulletCharacterMotor ──────

    /// <summary>
    /// End-to-end: <see cref="CrowdAgentUpdateSystem"/> writes a steering velocity into
    /// <see cref="CrowdMotorIntent"/>; <see cref="BulletCharacterMotor"/> reads that
    /// intent and drives the Bullet body (verified via <see cref="IPhysicsBodyService"/>).
    ///
    /// <para>
    /// Uses <see cref="FakeDtCrowdProvider.OverrideAgentVelocity"/> to inject a known
    /// velocity so the assertion is deterministic.
    /// </para>
    /// </summary>
    [Fact]
    public void CrowdVelocity_WritesToIntent_ThenMotorDrivesBody()
    {
        // Arrange
        var entity = SpawnCrowdAgent(Vector3.Zero);

        // Register the entity with the crowd and override its velocity deterministically.
        _crowd.RegisterAgent(entity, new CrowdAgentParams
        {
            Radius = 0.4f, Height = 1.8f, MaxSpeed = 5f, MaxAcceleration = 20f,
        });
        var knownFdpVelocity = new Vector3(3f, 0f, 0f);  // East only, FDP space
        _crowd.OverrideAgentVelocity(entity, knownFdpVelocity);

        // Act — step 1: CrowdAgentUpdateSystem writes the intent.
        _world.Bus.SwapBuffers();
        _crowdSystem.Execute(_world, 0.1f);

        // Verify the intent was written correctly.
        var intent = _world.GetComponent<CrowdMotorIntent>(entity);
        Assert.Equal(knownFdpVelocity.X, intent.Velocity.X, precision: 4);
        Assert.Equal(knownFdpVelocity.Y, intent.Velocity.Y, precision: 4);
        Assert.Equal(knownFdpVelocity.Z, intent.Velocity.Z, precision: 4);

        // Act — step 2: BulletCharacterMotor consumes the intent.
        _bodyService.VelocityCalls.Clear();
        _motor.Execute(_world, 0.1f);

        // Assert — the motor must have called SetCharacterVelocity with the swizzled velocity.
        Assert.Single(_bodyService.VelocityCalls);
        var call = _bodyService.VelocityCalls[0];

        // FDP (3,0,0) → Stride: (X=3, Y=fdp.Z=0, Z=fdp.Y=0) via FdpStrideTransform.ToStrideVelocity.
        Assert.Equal(3f, call.Velocity.X, precision: 4);
        Assert.Equal(0f, call.Velocity.Y, precision: 4);
        Assert.Equal(0f, call.Velocity.Z, precision: 4);
    }

    /// <summary>
    /// SimTransform.Position is unchanged by <see cref="CrowdAgentUpdateSystem"/>.
    /// This proves the position-integration removal (STR-D12 fix).
    /// </summary>
    [Fact]
    public void After_CrowdUpdate_SimTransformPosition_IsUnchanged()
    {
        // Arrange
        var startPos = new Vector3(5f, 3f, 0f);
        var entity   = SpawnCrowdAgent(startPos);

        _crowd.RegisterAgent(entity, new CrowdAgentParams
        {
            Radius = 0.4f, Height = 1.8f, MaxSpeed = 5f, MaxAcceleration = 20f,
        });
        _crowd.SetAgentTarget(entity, new Vector3(10f, 0f, 0f));

        // Act
        _world.Bus.SwapBuffers();
        _crowdSystem.Execute(_world, 0.5f);   // 500 ms — would be large integration if still active

        // Assert — position must be exactly unchanged.
        var posAfter = _world.GetComponent<SimTransform>(entity).Position;
        Assert.Equal(startPos.X, posAfter.X, precision: 6);
        Assert.Equal(startPos.Y, posAfter.Y, precision: 6);
        Assert.Equal(startPos.Z, posAfter.Z, precision: 6);
    }
}
