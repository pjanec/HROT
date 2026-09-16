#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Lifecycle.Events;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Tkb;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.Stride.Core;
using Xunit;

namespace Hrot.Stride.Core.Tests;

/// <summary>
/// Unit tests for <see cref="PhysicsBodyLifecycleSystem"/> and
/// <see cref="PhysicsBodyReference"/> (STR-P1-T2).
///
/// <para>
/// Headless-Bullet finding (BATCH-04): The Stride <c>Simulation</c> constructor and all
/// Add/RemoveBody methods are <c>internal</c> to <c>Stride.Physics</c> and owned by
/// <c>PhysicsProcessor</c>.  Creating and stepping a headless <c>Simulation</c> (without a
/// running Stride <c>Scene</c> + <c>Game</c>) is not feasible for the lifecycle/authority
/// logic tests. Therefore all tests here use a <see cref="RecordingFakePhysicsBodyService"/>
/// recording fake — exactly the BATCH-03 <c>IStrideVisualFactory</c> pattern — which
/// asserts the exact call args and body handle tracking while remaining headless.
/// </para>
///
/// <para>
/// The concrete Bullet implementation will live in <c>HrotStrideApp.Game</c> (already has a
/// running <c>PhysicsProcessor</c>) and will be exercised during GPU bring-up.
/// </para>
/// </summary>
public sealed class PhysicsBodyLifecycleSystemTests : IDisposable
{
    // ── Recording Fake ────────────────────────────────────────────────────────

    /// <summary>
    /// Recording fake that captures every <see cref="IPhysicsBodyService"/> call
    /// with exact argument values.  No real Bullet objects are created.
    /// </summary>
    private sealed class RecordingFakePhysicsBodyService : IPhysicsBodyService
    {
        public record CreateCall(
            Entity Entity, CollisionShapeKind ShapeKind, ShapeDims Dims, SimTransform Pose, object Handle);

        public record RemoveCall(object Handle);

        public List<CreateCall> Creates { get; } = new();
        public List<RemoveCall> Removes { get; } = new();

        private int _counter;

        public object CreateBody(
            Entity entity, CollisionShapeKind shapeKind, ShapeDims dims, in SimTransform initialPose)
        {
            var handle = $"Body_{++_counter}";
            Creates.Add(new CreateCall(entity, shapeKind, dims, initialPose, handle));
            return handle;
        }

        public void RemoveBody(object bodyHandle)
            => Removes.Add(new RemoveCall(bodyHandle));

        // ── Motor / reverse-sync methods (not used by PhysicsBodyLifecycleSystem) ──
        // Stubbed to satisfy the extended IPhysicsBodyService interface (STR-P1-T3/T4/T4b/T5).
        public void SetCharacterVelocity(object bodyHandle, global::Stride.Core.Mathematics.Vector3 velocity) { }
        public void Jump(object bodyHandle) { }
        public bool IsGrounded(object bodyHandle) => false;
        public void SetLinearVelocityXZ(object bodyHandle, global::Stride.Core.Mathematics.Vector3 strideLinearVel) { }
        public void SetYawRate(object bodyHandle, float strideYawRateRadPerSec) { }
        public KinematicMoveResult MoveKinematic(
            object bodyHandle,
            global::Stride.Core.Mathematics.Vector3     desiredDelta,
            global::Stride.Core.Mathematics.Quaternion  desiredRotDelta)
            => new KinematicMoveResult(desiredDelta, desiredRotDelta);
        public BodyState GetBodyState(object bodyHandle)
            => new BodyState(
                global::Stride.Core.Mathematics.Vector3.Zero,
                global::Stride.Core.Mathematics.Quaternion.Identity,
                global::Stride.Core.Mathematics.Vector3.Zero,
                global::Stride.Core.Mathematics.Vector3.Zero,
                IsKinematic: false);
    }

    // ── Null visual factory (no-op so StrideVisualBindingSystem can be created) ──

    private sealed class NullVisualFactory : IStrideVisualFactory
    {
        public object CreateModelVisual(string m, string s, float sc, Vector3 o, in SimTransform t) => new object();
        public object CreateProceduralVisual(CollisionShapeKind k, ShapeDims d, float sc, Vector3 o, in SimTransform t) => new object();
        public void UpdatePose(object h, in SimTransform t) { }
        public void Destroy(object h) { }
    }

    // ── Test infrastructure ───────────────────────────────────────────────────

    private readonly EntityRepository                _world;
    private readonly RecordingFakePhysicsBodyService  _fakeService;
    private readonly StrideVisualBindingSystem         _visualSystem;
    private readonly PhysicsBodyLifecycleSystem        _sut;
    private readonly TkbDatabase                      _tkbDb;

    private const long CapsuleTkbType   = 501L;
    private const long BoxTkbType        = 502L;

    public PhysicsBodyLifecycleSystemTests()
    {
        _world = new EntityRepository();
        _world.RegisterComponent<SimTransform>();
        _world.RegisterComponent<SimVelocity>();
        _world.RegisterComponent<TkbIdentity>();

        _tkbDb = BuildTkbDb();
        _fakeService  = new RecordingFakePhysicsBodyService();
        _visualSystem = new StrideVisualBindingSystem(new NullVisualFactory(), _tkbDb);
        _sut          = new PhysicsBodyLifecycleSystem(_fakeService, _visualSystem);
    }

    public void Dispose() => _world.Dispose();

    // Build a TkbDatabase with Capsule and Box templates.
    private static TkbDatabase BuildTkbDb()
    {
        var db = new TkbDatabase();

        var capsuleDef = new StrideRenderModelDefDto
        {
            ShapeKind   = CollisionShapeKind.Capsule,
            ShapeRadius = 0.3f,
            ShapeHeight = 1.8f,
        };
        var capTemplate = new TkbTemplate("CapsuleUnit", CapsuleTkbType);
        capTemplate.AddDescriptor(capsuleDef);
        db.Register(capTemplate);

        var boxDef = new StrideRenderModelDefDto
        {
            ShapeKind = CollisionShapeKind.OrientedBox,
            BoxHalfX  = 1.0f,
            BoxHalfY  = 0.5f,
            BoxHalfZ  = 2.0f,
        };
        var boxTemplate = new TkbTemplate("BoxUnit", BoxTkbType);
        boxTemplate.AddDescriptor(boxDef);
        db.Register(boxTemplate);

        return db;
    }

    // Spawn an entity with the given TkbType and a SimTransform.
    // Marks it as locally authoritative for SimTransform (== WithOwned<SimTransform>).
    private Entity SpawnOwned(long tkbType, Vector3 pos)
    {
        var entity = _world.CreateEntity();
        _world.AddComponent(entity, new TkbIdentity { TkbType = tkbType });
        _world.AddComponent(entity, new SimTransform { Position = pos });
        // SetAuthority<SimTransform>(entity, true) sets the authority-mask bit that
        // WithOwned<SimTransform>() queries. This is the canonical way to make an
        // entity "locally authoritative" in the ECS.
        _world.SetAuthority<SimTransform>(entity, true);
        return entity;
    }

    // Revoke authority — entity becomes WithoutOwned<SimTransform>.
    private void RevokeAuthority(Entity entity)
        => _world.SetAuthority<SimTransform>(entity, false);

    // Call StrideVisualBindingSystem.Sync so StrideVisualReferences are populated.
    private void SyncVisuals() => _visualSystem.Sync(_world);

    // Run the lifecycle system (implements ISimulationView via EntityRepository).
    private void RunSystem() => _sut.Execute(_world, 1f / 60f);

    // Publish a DestructionOrder event on the world bus, then swap so it is readable.
    private void PublishDestruction(Entity entity)
    {
        // Register the event type if not already registered.
        _world.Bus.Register<DestructionOrder>();
        _world.Bus.Publish(new DestructionOrder { Entity = entity, FrameNumber = 1 });
        _world.Bus.SwapBuffers();
    }

    // ── T2-SC1: Creation (owned entity + visual ref present) ─────────────────

    /// <summary>
    /// An owned entity with a <see cref="StrideVisualReference"/> triggers
    /// <see cref="IPhysicsBodyService.CreateBody"/> and appears in
    /// <see cref="PhysicsBodyLifecycleSystem.Bodies"/>.
    /// </summary>
    [Fact]
    public void OwnedEntity_WithVisualRef_CreatesBody_AndRefAdded()
    {
        var entity = SpawnOwned(CapsuleTkbType, new Vector3(1f, 2f, 0f));
        SyncVisuals(); // creates StrideVisualReference in _visualSystem.Visuals

        Assert.True(_visualSystem.Visuals.ContainsKey(entity),
            "StrideVisualReference must exist before running the lifecycle system.");

        RunSystem();

        Assert.Single(_fakeService.Creates);
        Assert.True(_sut.Bodies.ContainsKey(entity),
            "PhysicsBodyReference must be recorded after body creation.");

        var call = _fakeService.Creates[0];
        Assert.Equal(entity, call.Entity);
        Assert.Equal(CollisionShapeKind.Capsule, call.ShapeKind);
    }

    // ── T2-SC2: Capsule shape — correct dims ─────────────────────────────────

    /// <summary>
    /// A Capsule entity's body is created with the exact radius and height from
    /// the <see cref="StrideVisualReference"/> — not re-derived from the TKB descriptor.
    /// </summary>
    [Fact]
    public void CapsuleShape_CreatedWithCorrectRadiusAndHeight()
    {
        var entity = SpawnOwned(CapsuleTkbType, Vector3.Zero);
        SyncVisuals();
        RunSystem();

        var call = Assert.Single(_fakeService.Creates);
        Assert.Equal(CollisionShapeKind.Capsule, call.ShapeKind);
        Assert.Equal(0.3f, call.Dims.Radius, precision: 4);
        Assert.Equal(1.8f, call.Dims.Height, precision: 4);
    }

    // ── T2-SC3: OrientedBox shape — correct dims ─────────────────────────────

    /// <summary>
    /// An OrientedBox entity's body is created with the exact half-extents from
    /// the <see cref="StrideVisualReference"/>.
    /// </summary>
    [Fact]
    public void BoxShape_CreatedWithCorrectHalfExtents()
    {
        var entity = SpawnOwned(BoxTkbType, Vector3.Zero);
        SyncVisuals();
        RunSystem();

        var call = Assert.Single(_fakeService.Creates);
        Assert.Equal(CollisionShapeKind.OrientedBox, call.ShapeKind);
        Assert.Equal(1.0f, call.Dims.HalfX, precision: 4);
        Assert.Equal(0.5f, call.Dims.HalfY, precision: 4);
        Assert.Equal(2.0f, call.Dims.HalfZ, precision: 4);
    }

    // ── T2-SC4: PhysicsBodyReference carries shape kind + dims ───────────────

    /// <summary>
    /// <see cref="PhysicsBodyReference"/> stores the exact shape kind and dims
    /// passed to the service — assertions can be made without touching the handle.
    /// </summary>
    [Fact]
    public void PhysicsBodyReference_StoresShapeKindAndDims()
    {
        var entity = SpawnOwned(CapsuleTkbType, Vector3.Zero);
        SyncVisuals();
        RunSystem();

        var bodyRef = _sut.Bodies[entity];
        Assert.Equal(CollisionShapeKind.Capsule, bodyRef.ShapeKind);
        Assert.Equal(0.3f, bodyRef.Dims.Radius, precision: 4);
        Assert.Equal(1.8f, bodyRef.Dims.Height, precision: 4);
    }

    // ── T2-SC5: Idempotency — no double-create ────────────────────────────────

    /// <summary>
    /// A second <see cref="PhysicsBodyLifecycleSystem.Execute"/> pass for an entity
    /// that already has a <see cref="PhysicsBodyReference"/> is a complete no-op.
    /// </summary>
    [Fact]
    public void SecondPass_DoesNotDoubleCreateBody()
    {
        var entity = SpawnOwned(CapsuleTkbType, Vector3.Zero);
        SyncVisuals();
        RunSystem(); // first pass: creates body
        RunSystem(); // second pass: must be no-op

        Assert.Single(_fakeService.Creates); // exactly 1 create call
        Assert.Empty(_fakeService.Removes);
    }

    // ── T2-SC6: Authority revocation ─────────────────────────────────────────

    /// <summary>
    /// When an entity's authority is revoked (becomes <c>WithoutOwned&lt;SimTransform&gt;</c>)
    /// while holding a <see cref="PhysicsBodyReference"/>, the body is removed and
    /// the reference is cleared from <see cref="PhysicsBodyLifecycleSystem.Bodies"/>.
    /// The exact body handle is passed to <see cref="IPhysicsBodyService.RemoveBody"/>.
    /// </summary>
    [Fact]
    public void AuthorityRevoked_RemovesBodyAndClearsRef()
    {
        var entity = SpawnOwned(CapsuleTkbType, Vector3.Zero);
        SyncVisuals();
        RunSystem(); // body created

        Assert.True(_sut.Bodies.ContainsKey(entity));
        var originalHandle = _fakeService.Creates[0].Handle;

        RevokeAuthority(entity);
        RunSystem(); // revocation detected → body removed

        Assert.False(_sut.Bodies.ContainsKey(entity),
            "PhysicsBodyReference must be removed after authority revocation.");
        Assert.Single(_fakeService.Removes);
        Assert.Equal(originalHandle, _fakeService.Removes[0].Handle);
    }

    // ── T2-SC7: DestructionOrder consumed ────────────────────────────────────

    /// <summary>
    /// Consuming a <see cref="DestructionOrder"/> event tears down the body and
    /// removes the reference.
    /// </summary>
    [Fact]
    public void DestructionOrder_TearsDownBodyAndClearsRef()
    {
        var entity = SpawnOwned(CapsuleTkbType, Vector3.Zero);
        SyncVisuals();
        RunSystem(); // body created

        var originalHandle = _fakeService.Creates[0].Handle;

        PublishDestruction(entity);
        RunSystem(); // destruction consumed

        Assert.False(_sut.Bodies.ContainsKey(entity),
            "PhysicsBodyReference must be removed after DestructionOrder.");
        Assert.Single(_fakeService.Removes);
        Assert.Equal(originalHandle, _fakeService.Removes[0].Handle);
    }

    // ── T2-SC8: No visual ref → skip (retry next frame) ──────────────────────

    /// <summary>
    /// If no <see cref="StrideVisualReference"/> exists yet for an owned entity,
    /// body creation is skipped silently — no service call, no entry in
    /// <see cref="PhysicsBodyLifecycleSystem.Bodies"/>. Retried next frame.
    /// </summary>
    [Fact]
    public void OwnedEntity_WithoutVisualRef_SkipsBodyCreation()
    {
        SpawnOwned(CapsuleTkbType, Vector3.Zero); // no SyncVisuals call

        RunSystem();

        Assert.Empty(_fakeService.Creates);
        Assert.Empty(_sut.Bodies);
    }

    // ── T2-SC9: Multiple entities ─────────────────────────────────────────────

    /// <summary>
    /// Two owned entities of different shapes each get their own body with the
    /// correct shape kind and distinct handles.
    /// </summary>
    [Fact]
    public void TwoOwnedEntities_EachGetSeparateBodyWithCorrectShape()
    {
        var capEntity = SpawnOwned(CapsuleTkbType, Vector3.Zero);
        var boxEntity = SpawnOwned(BoxTkbType,     new Vector3(5f, 0f, 0f));
        SyncVisuals();
        RunSystem();

        Assert.Equal(2, _fakeService.Creates.Count);
        Assert.Equal(2, _sut.Bodies.Count);

        bool capOk = false, boxOk = false;
        foreach (var c in _fakeService.Creates)
        {
            if (c.Entity == capEntity) { capOk = true; Assert.Equal(CollisionShapeKind.Capsule,    c.ShapeKind); }
            if (c.Entity == boxEntity) { boxOk = true; Assert.Equal(CollisionShapeKind.OrientedBox, c.ShapeKind); }
        }
        Assert.True(capOk, "Capsule entity must have a CreateBody call.");
        Assert.True(boxOk, "Box entity must have a CreateBody call.");
    }
}
