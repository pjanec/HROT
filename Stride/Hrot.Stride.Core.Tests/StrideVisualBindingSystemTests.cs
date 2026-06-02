using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Physics.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Tkb;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.Stride.Core;
using Xunit;
using SNum = Stride.Core.Mathematics;

namespace Hrot.Stride.Core.Tests;

/// <summary>
/// Headless unit tests for <see cref="StrideVisualBindingSystem"/> (STR-P0-T7).
///
/// Uses a <see cref="RecordingFakeFactory"/> to record every factory call with exact
/// argument values.  All assertions verify real runtime values, not string-presence or
/// object-not-null.  Tests map directly to the batch success conditions.
/// </summary>
public sealed class StrideVisualBindingSystemTests : IDisposable
{
    // ── Fake factory ─────────────────────────────────────────────────────────

    /// <summary>
    /// Recording fake that captures every factory call for assertion.
    /// All calls record the thread ID so the single-thread invariant can be verified.
    /// </summary>
    private sealed class RecordingFakeFactory : IStrideVisualFactory
    {
        public record CreateModelCall(
            string ModelRef, string SkeletonRef, float Scale, Vector3 OffsetFdp,
            SimTransform InitialPose, int ThreadId, object Handle);

        public record CreateProceduralCall(
            CollisionShapeKind Kind, ShapeDims Dims, float Scale, Vector3 OffsetFdp,
            SimTransform InitialPose, int ThreadId, object Handle);

        public record UpdatePoseCall(object Handle, SimTransform Pose, int ThreadId);

        public record DestroyCall(object Handle, int ThreadId);

        public List<CreateModelCall>      ModelCalls      { get; } = new();
        public List<CreateProceduralCall> ProceduralCalls { get; } = new();
        public List<UpdatePoseCall>       UpdateCalls     { get; } = new();
        public List<DestroyCall>          DestroyCalls    { get; } = new();

        // Monotonic handle counter — each visual gets a unique handle object.
        private int _handleCounter;

        public object CreateModelVisual(
            string modelRef, string skeletonRef, float scale, Vector3 offsetFdp,
            in SimTransform initialPose)
        {
            var handle = $"ModelHandle_{++_handleCounter}";
            ModelCalls.Add(new CreateModelCall(
                modelRef, skeletonRef, scale, offsetFdp, initialPose,
                Environment.CurrentManagedThreadId, handle));
            return handle;
        }

        public object CreateProceduralVisual(
            CollisionShapeKind kind, ShapeDims dims, float scale, Vector3 offsetFdp,
            in SimTransform initialPose)
        {
            var handle = $"ProcHandle_{++_handleCounter}";
            ProceduralCalls.Add(new CreateProceduralCall(
                kind, dims, scale, offsetFdp, initialPose,
                Environment.CurrentManagedThreadId, handle));
            return handle;
        }

        public void UpdatePose(object visualHandle, in SimTransform pose)
        {
            UpdateCalls.Add(new UpdatePoseCall(
                visualHandle, pose, Environment.CurrentManagedThreadId));
        }

        public void Destroy(object visualHandle)
        {
            DestroyCalls.Add(new DestroyCall(
                visualHandle, Environment.CurrentManagedThreadId));
        }
    }

    // ── Test world helpers ───────────────────────────────────────────────────

    private static EntityRepository CreateWorld()
    {
        var world = new EntityRepository();
        world.RegisterComponent<SimTransform>();
        world.RegisterComponent<TkbIdentity>();
        world.RegisterComponent<PhysicsCollider>();
        return world;
    }

    private static TkbDatabase CreateTkb()
    {
        return new TkbDatabase();
    }

    /// <summary>Registers all TkbIdentity + StrideRenderModelDefDto component types.</summary>
    private static void RegisterComponents(EntityRepository world)
    {
        // Already done in CreateWorld(); additional types if needed can go here.
    }

    private static Entity SpawnEntity(
        EntityRepository world,
        long tkbType,
        Vector3 position,
        float yaw = 0f)
    {
        var entity = world.CreateEntity();
        world.AddComponent(entity, new TkbIdentity { TkbType = tkbType });
        world.AddComponent(entity, new SimTransform
        {
            Position = position,
            Rotation = Quaternion.CreateFromYawPitchRoll(yaw, 0f, 0f),
        });
        return entity;
    }

    private static Entity SpawnEntityWithCollider(
        EntityRepository world,
        long tkbType,
        Vector3 position,
        float radius)
    {
        var entity = SpawnEntity(world, tkbType, position);
        world.AddComponent(entity, new PhysicsCollider { Radius = radius });
        return entity;
    }

    // ── Dispose helpers ──────────────────────────────────────────────────────

    private readonly List<EntityRepository> _worlds = new();

    public void Dispose()
    {
        foreach (var w in _worlds) w.Dispose();
    }

    private EntityRepository Track(EntityRepository w) { _worlds.Add(w); return w; }

    // ════════════════════════════════════════════════════════════════════════
    // T7-SC1: ModelAssetRef non-empty → CreateModelVisual with exact args
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void ModelAssetRef_NonEmpty_CallsCreateModelVisual_WithExactArgs()
    {
        // Arrange
        var world   = Track(CreateWorld());
        var tkbDb   = CreateTkb();
        var factory = new RecordingFakeFactory();
        var sut     = new StrideVisualBindingSystem(factory, tkbDb);

        const long TkbType = 2002L;
        var def = new StrideRenderModelDefDto
        {
            ModelAssetRef    = "Models/mannequinModel",
            SkeletonAssetRef = "Models/mannequinModel Skeleton",
            Scale            = 1.5f,
            OffsetX          = 0.1f,
            OffsetY          = 0.2f,
            OffsetZ          = 0.05f,
            ShapeKind        = CollisionShapeKind.Capsule,
            ShapeRadius      = 0.35f,
            ShapeHeight      = 1.8f,
        };
        var template = new TkbTemplate("InfantrySoldier", TkbType);
        template.AddDescriptor(def);
        tkbDb.Register(template);

        var spawnPos = new Vector3(10f, 20f, 0.5f);
        var entity   = SpawnEntity(world, TkbType, spawnPos);

        // Act
        sut.Sync(world);

        // Assert — exactly one CreateModelVisual, no procedural calls
        Assert.Equal(1, factory.ModelCalls.Count);
        Assert.Equal(0, factory.ProceduralCalls.Count);

        var call = factory.ModelCalls[0];
        Assert.Equal("Models/mannequinModel",          call.ModelRef);
        Assert.Equal("Models/mannequinModel Skeleton", call.SkeletonRef);
        Assert.Equal(1.5f,  call.Scale);
        Assert.Equal(0.1f,  call.OffsetFdp.X);
        Assert.Equal(0.2f,  call.OffsetFdp.Y);
        Assert.Equal(0.05f, call.OffsetFdp.Z);

        // Visual reference must be recorded for the entity
        Assert.True(sut.Visuals.ContainsKey(entity),
            "Visuals dictionary must contain the spawned entity after Sync.");
        Assert.Same(call.Handle, sut.Visuals[entity].VisualHandle);
        Assert.True(sut.Visuals[entity].IsModelVisual);
    }

    // ════════════════════════════════════════════════════════════════════════
    // T7-SC2a: Empty ModelAssetRef + Capsule + ShapeRadius=0 → radius from PhysicsCollider
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void EmptyModelRef_Capsule_ShapeRadius0_ResolvesFromPhysicsCollider()
    {
        const float ColliderRadius = 0.45f;

        var world   = Track(CreateWorld());
        var tkbDb   = CreateTkb();
        var factory = new RecordingFakeFactory();
        var sut     = new StrideVisualBindingSystem(factory, tkbDb);

        const long TkbType = 1001L;
        var def = new StrideRenderModelDefDto
        {
            ModelAssetRef = "",                         // procedural path
            ShapeKind     = CollisionShapeKind.Capsule,
            ShapeRadius   = 0f,                        // trigger default
            ShapeHeight   = 1.75f,
        };
        var template = new TkbTemplate("CivilianPedestrian", TkbType);
        template.AddDescriptor(def);
        tkbDb.Register(template);

        var entity = SpawnEntityWithCollider(world, TkbType, Vector3.Zero, ColliderRadius);

        // Act
        sut.Sync(world);

        // Assert — exactly one procedural call, capsule shape, radius == ColliderRadius
        Assert.Equal(0, factory.ModelCalls.Count);
        Assert.Equal(1, factory.ProceduralCalls.Count);

        var call = factory.ProceduralCalls[0];
        Assert.Equal(CollisionShapeKind.Capsule, call.Kind);
        Assert.Equal(ColliderRadius, call.Dims.Radius,
            precision: 5);
        Assert.Equal(1.75f, call.Dims.Height, precision: 5);
    }

    // ════════════════════════════════════════════════════════════════════════
    // T7-SC2b: Empty ModelAssetRef + OrientedBox + BoxHalf=0 → from VehicleParametersDto
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void EmptyModelRef_OrientedBox_BoxHalf0_ResolvesFromVehicleParametersDto()
    {
        var world   = Track(CreateWorld());
        var tkbDb   = CreateTkb();
        var factory = new RecordingFakeFactory();
        var sut     = new StrideVisualBindingSystem(factory, tkbDb);

        const long TkbType = 1002L;
        const float VehicleLength = 4.5f;
        const float VehicleWidth  = 2.0f;
        const float ShapeHeight   = 1.5f;

        var def = new StrideRenderModelDefDto
        {
            ModelAssetRef = "",
            ShapeKind     = CollisionShapeKind.OrientedBox,
            BoxHalfX      = 0f,      // → VehicleParameters.Length / 2
            BoxHalfY      = 0f,      // → VehicleParameters.Width / 2
            BoxHalfZ      = 0f,      // → ShapeHeight / 2
            ShapeHeight   = ShapeHeight,
        };
        var vehicleDto = new VehicleParametersDto { Length = VehicleLength, Width = VehicleWidth };
        var template   = new TkbTemplate("CivilianCar", TkbType);
        template.AddDescriptor(def);
        template.AddDescriptor(vehicleDto);
        tkbDb.Register(template);

        SpawnEntity(world, TkbType, Vector3.Zero);

        // Act
        sut.Sync(world);

        Assert.Equal(1, factory.ProceduralCalls.Count);
        var call = factory.ProceduralCalls[0];
        Assert.Equal(CollisionShapeKind.OrientedBox, call.Kind);

        float expectedHalfX = VehicleLength / 2f;   // 2.25
        float expectedHalfY = VehicleWidth  / 2f;   // 1.0
        float expectedHalfZ = ShapeHeight   / 2f;   // 0.75

        Assert.Equal(expectedHalfX, call.Dims.HalfX, precision: 5);
        Assert.Equal(expectedHalfY, call.Dims.HalfY, precision: 5);
        Assert.Equal(expectedHalfZ, call.Dims.HalfZ, precision: 5);
    }

    // ════════════════════════════════════════════════════════════════════════
    // T7-SC3: Explicit non-zero ShapeRadius overrides PhysicsCollider default
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void ExplicitShapeRadius_Wins_OverPhysicsColliderDefault()
    {
        const float ExplicitRadius  = 0.5f;
        const float ColliderRadius  = 9.99f; // must not win

        var world   = Track(CreateWorld());
        var tkbDb   = CreateTkb();
        var factory = new RecordingFakeFactory();
        var sut     = new StrideVisualBindingSystem(factory, tkbDb);

        const long TkbType = 999L;
        var def = new StrideRenderModelDefDto
        {
            ModelAssetRef = "",
            ShapeKind     = CollisionShapeKind.Capsule,
            ShapeRadius   = ExplicitRadius,  // explicit → must win
            ShapeHeight   = 1.0f,
        };
        var template = new TkbTemplate("TestCapsule", TkbType);
        template.AddDescriptor(def);
        tkbDb.Register(template);

        SpawnEntityWithCollider(world, TkbType, Vector3.Zero, ColliderRadius);

        sut.Sync(world);

        Assert.Equal(1, factory.ProceduralCalls.Count);
        Assert.Equal(ExplicitRadius, factory.ProceduralCalls[0].Dims.Radius, precision: 5);
    }

    // ════════════════════════════════════════════════════════════════════════
    // T7-SC3b: Explicit non-zero BoxHalfX/Y/Z override VehicleParametersDto defaults
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void ExplicitBoxHalves_Win_OverVehicleParametersDtoDefaults()
    {
        var world   = Track(CreateWorld());
        var tkbDb   = CreateTkb();
        var factory = new RecordingFakeFactory();
        var sut     = new StrideVisualBindingSystem(factory, tkbDb);

        const long TkbType = 998L;
        var def = new StrideRenderModelDefDto
        {
            ModelAssetRef = "",
            ShapeKind     = CollisionShapeKind.OrientedBox,
            BoxHalfX      = 3.0f,   // explicit
            BoxHalfY      = 1.5f,   // explicit
            BoxHalfZ      = 1.2f,   // explicit
        };
        var vehicleDto = new VehicleParametersDto { Length = 99f, Width = 99f };
        var template   = new TkbTemplate("ExplicitBox", TkbType);
        template.AddDescriptor(def);
        template.AddDescriptor(vehicleDto);
        tkbDb.Register(template);

        SpawnEntity(world, TkbType, Vector3.Zero);

        sut.Sync(world);

        Assert.Equal(1, factory.ProceduralCalls.Count);
        var call = factory.ProceduralCalls[0];
        Assert.Equal(3.0f, call.Dims.HalfX, precision: 5);
        Assert.Equal(1.5f, call.Dims.HalfY, precision: 5);
        Assert.Equal(1.2f, call.Dims.HalfZ, precision: 5);
    }

    // ════════════════════════════════════════════════════════════════════════
    // T7-SC4: Pose placement — UpdatePose receives swizzled transform
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void PosePlacement_InitialCreate_UsesSwizzledTransform()
    {
        // Verify that the initial pose passed to CreateModelVisual/CreateProceduralVisual
        // equals the entity's SimTransform, and the factory is responsible for swizzling.
        // We also verify that calling FdpStrideTransform.ToStridePosition on the initial
        // pose gives the expected Stride coordinates.
        var world   = Track(CreateWorld());
        var tkbDb   = CreateTkb();
        var factory = new RecordingFakeFactory();
        var sut     = new StrideVisualBindingSystem(factory, tkbDb);

        const long TkbType = 777L;
        var def = new StrideRenderModelDefDto
        {
            ModelAssetRef    = "Models/mannequinModel",
            SkeletonAssetRef = "",
        };
        var template = new TkbTemplate("PoseTest", TkbType);
        template.AddDescriptor(def);
        tkbDb.Register(template);

        // A known FDP position: X=10 (East), Y=20 (North), Z=3 (Up)
        var fdpPos = new Vector3(10f, 20f, 3f);
        var entity = SpawnEntity(world, TkbType, fdpPos);

        sut.Sync(world);

        Assert.Equal(1, factory.ModelCalls.Count);
        var call = factory.ModelCalls[0];

        // The initial pose passed to the factory must carry the entity's SimTransform position.
        Assert.Equal(fdpPos, call.InitialPose.Position);

        // Verify that the factory would place the visual at the correct Stride position.
        // FdpStrideTransform.ToStridePosition(X=10, Y=20, Z=3) → Stride (10, 3, 20)
        var stridePos = FdpStrideTransform.ToStridePosition(call.InitialPose.Position);
        Assert.Equal(10f, stridePos.X, precision: 5);
        Assert.Equal(3f,  stridePos.Y, precision: 5);  // altitude → Stride Y
        Assert.Equal(20f, stridePos.Z, precision: 5);  // North → Stride Z
    }

    [Fact]
    public void PosePlacement_UpdatePose_ReceivesCurrentSimTransform()
    {
        // After initial create, subsequent Sync calls must call UpdatePose with the
        // entity's current SimTransform.
        var world   = Track(CreateWorld());
        var tkbDb   = CreateTkb();
        var factory = new RecordingFakeFactory();
        var sut     = new StrideVisualBindingSystem(factory, tkbDb);

        const long TkbType = 888L;
        var def = new StrideRenderModelDefDto { ModelAssetRef = "Models/mannequinModel" };
        var template = new TkbTemplate("UpdatePoseTest", TkbType);
        template.AddDescriptor(def);
        tkbDb.Register(template);

        var entity = SpawnEntity(world, TkbType, new Vector3(5f, 5f, 0f));

        // Frame 1 — create
        sut.Sync(world);
        Assert.Equal(1, factory.ModelCalls.Count);
        Assert.Equal(0, factory.UpdateCalls.Count);

        // Move entity to new position (simulate forward-sync moving it)
        ref var xform = ref world.GetComponentRW<SimTransform>(entity);
        var newPos = new Vector3(15f, 25f, 2f);
        xform.Position = newPos;

        // Frame 2 — must call UpdatePose, not CreateModelVisual again
        sut.Sync(world);
        Assert.Equal(1, factory.ModelCalls.Count);  // still only 1 create
        Assert.Equal(1, factory.UpdateCalls.Count); // exactly 1 update

        var updateCall = factory.UpdateCalls[0];
        Assert.Equal(newPos, updateCall.Pose.Position);

        // The handle passed to UpdatePose must be the one from CreateModelVisual
        Assert.Same(factory.ModelCalls[0].Handle, updateCall.Handle);
    }

    // ════════════════════════════════════════════════════════════════════════
    // T7-SC5: Reconciliation — create once (idempotent), destroy on death, no desc → skip
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void NewEntity_CallsCreate_ExactlyOnce_AcrossMultipleTicks()
    {
        var world   = Track(CreateWorld());
        var tkbDb   = CreateTkb();
        var factory = new RecordingFakeFactory();
        var sut     = new StrideVisualBindingSystem(factory, tkbDb);

        const long TkbType = 555L;
        var def = new StrideRenderModelDefDto { ModelAssetRef = "Models/mannequinModel" };
        var template = new TkbTemplate("IdempotentTest", TkbType);
        template.AddDescriptor(def);
        tkbDb.Register(template);

        SpawnEntity(world, TkbType, Vector3.Zero);

        // Pump 5 frames
        for (int i = 0; i < 5; i++)
            sut.Sync(world);

        // Create called exactly once, not re-created each frame
        Assert.Equal(1, factory.ModelCalls.Count);
        // UpdatePose called 4 more times (frames 2-5)
        Assert.Equal(4, factory.UpdateCalls.Count);
    }

    [Fact]
    public void DeadEntity_CallsDestroy_ExactlyOnce_And_RemovesVisualReference()
    {
        var world   = Track(CreateWorld());
        var tkbDb   = CreateTkb();
        var factory = new RecordingFakeFactory();
        var sut     = new StrideVisualBindingSystem(factory, tkbDb);

        const long TkbType = 333L;
        var def = new StrideRenderModelDefDto { ModelAssetRef = "Models/mannequinModel" };
        var template = new TkbTemplate("DestroyTest", TkbType);
        template.AddDescriptor(def);
        tkbDb.Register(template);

        var entity = SpawnEntity(world, TkbType, Vector3.Zero);

        sut.Sync(world);
        Assert.Equal(1, factory.ModelCalls.Count);
        Assert.True(sut.Visuals.ContainsKey(entity));

        var handle = sut.Visuals[entity].VisualHandle;

        // Kill the entity
        world.DestroyEntity(entity);

        sut.Sync(world);

        // Destroy called exactly once with the correct handle
        Assert.Equal(1, factory.DestroyCalls.Count);
        Assert.Same(handle, factory.DestroyCalls[0].Handle);

        // Visual reference removed
        Assert.False(sut.Visuals.ContainsKey(entity),
            "StrideVisualReference must be removed after entity death.");
    }

    [Fact]
    public void EntityWithNoStrideDescriptor_ProducesNoFactoryCalls()
    {
        var world   = Track(CreateWorld());
        var tkbDb   = CreateTkb();
        var factory = new RecordingFakeFactory();
        var sut     = new StrideVisualBindingSystem(factory, tkbDb);

        // Template with NO StrideRenderModelDefDto
        const long TkbType = 111L;
        var template = new TkbTemplate("NoDescriptor", TkbType);
        // Intentionally no StrideRenderModelDefDto added
        tkbDb.Register(template);

        SpawnEntity(world, TkbType, Vector3.Zero);

        sut.Sync(world);

        // No factory calls at all — entity silently skipped
        Assert.Equal(0, factory.ModelCalls.Count);
        Assert.Equal(0, factory.ProceduralCalls.Count);
        Assert.Equal(0, factory.UpdateCalls.Count);
        Assert.Equal(0, factory.DestroyCalls.Count);
    }

    [Fact]
    public void EntityWithUnknownTkbType_ProducesNoFactoryCalls()
    {
        var world   = Track(CreateWorld());
        var tkbDb   = CreateTkb();
        var factory = new RecordingFakeFactory();
        var sut     = new StrideVisualBindingSystem(factory, tkbDb);

        // TKB type 9999 is not registered in tkbDb
        SpawnEntity(world, tkbType: 9999L, Vector3.Zero);

        sut.Sync(world);

        Assert.Equal(0, factory.ModelCalls.Count);
        Assert.Equal(0, factory.ProceduralCalls.Count);
    }

    // ════════════════════════════════════════════════════════════════════════
    // T7-SC6: Multi-entity reconciliation — N entities → N visuals
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void MultipleEntities_AllGetVisuals_AndAreTrackedByEntity()
    {
        var world   = Track(CreateWorld());
        var tkbDb   = CreateTkb();
        var factory = new RecordingFakeFactory();
        var sut     = new StrideVisualBindingSystem(factory, tkbDb);

        const long TkbTypePed = 1001L;
        const long TkbTypeCar = 1002L;

        var pedDef = new StrideRenderModelDefDto
        {
            ModelAssetRef    = "Models/mannequinModel",
            SkeletonAssetRef = "Models/mannequinModel Skeleton",
            ShapeKind        = CollisionShapeKind.Capsule,
            ShapeRadius      = 0.3f,
            ShapeHeight      = 1.8f,
        };
        var carDef = new StrideRenderModelDefDto
        {
            ModelAssetRef = "",   // procedural
            ShapeKind     = CollisionShapeKind.OrientedBox,
            ShapeHeight   = 1.5f,
        };
        var vehicleDto = new VehicleParametersDto { Length = 4.5f, Width = 2.0f };

        var pedTemplate = new TkbTemplate("Pedestrian", TkbTypePed);
        pedTemplate.AddDescriptor(pedDef);
        tkbDb.Register(pedTemplate);

        var carTemplate = new TkbTemplate("Car", TkbTypeCar);
        carTemplate.AddDescriptor(carDef);
        carTemplate.AddDescriptor(vehicleDto);
        tkbDb.Register(carTemplate);

        // Spawn 3 pedestrians and 2 cars
        var peds = new Entity[3];
        for (int i = 0; i < 3; i++)
            peds[i] = SpawnEntity(world, TkbTypePed, new Vector3(i * 5f, 0f, 0f));

        var cars = new Entity[2];
        for (int i = 0; i < 2; i++)
            cars[i] = SpawnEntity(world, TkbTypeCar, new Vector3(0f, i * 10f, 0f));

        sut.Sync(world);

        // 3 model visuals, 2 procedural visuals
        Assert.Equal(3, factory.ModelCalls.Count);
        Assert.Equal(2, factory.ProceduralCalls.Count);
        Assert.Equal(5, sut.Visuals.Count);

        // Each pedestrian → model visual
        foreach (var ped in peds)
        {
            Assert.True(sut.Visuals.ContainsKey(ped));
            Assert.True(sut.Visuals[ped].IsModelVisual);
        }

        // Each car → procedural visual (OrientedBox)
        foreach (var car in cars)
        {
            Assert.True(sut.Visuals.ContainsKey(car));
            Assert.False(sut.Visuals[car].IsModelVisual);
            Assert.Equal(CollisionShapeKind.OrientedBox, sut.Visuals[car].ShapeKind);
        }

        // Kill one pedestrian
        world.DestroyEntity(peds[0]);
        sut.Sync(world);

        Assert.Equal(4, sut.Visuals.Count);
        Assert.Equal(1, factory.DestroyCalls.Count);
        Assert.False(sut.Visuals.ContainsKey(peds[0]));
    }

    // ════════════════════════════════════════════════════════════════════════
    // T7-SC7: Single-thread invariant — all factory calls on the same thread
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void AllFactoryCalls_HappenOnSameThread_AsSyncCaller()
    {
        var world   = Track(CreateWorld());
        var tkbDb   = CreateTkb();
        var factory = new RecordingFakeFactory();
        var sut     = new StrideVisualBindingSystem(factory, tkbDb);

        const long TkbType = 444L;
        var def = new StrideRenderModelDefDto
        {
            ModelAssetRef = "",
            ShapeKind     = CollisionShapeKind.Capsule,
            ShapeRadius   = 0.4f,
            ShapeHeight   = 1.7f,
        };
        var template = new TkbTemplate("ThreadTest", TkbType);
        template.AddDescriptor(def);
        tkbDb.Register(template);

        var entity = SpawnEntity(world, TkbType, new Vector3(1f, 2f, 0f));
        var callerThreadId = Environment.CurrentManagedThreadId;

        // Frame 1 — create
        sut.Sync(world);
        // Frame 2 — update
        sut.Sync(world);
        // Kill and run frame 3 — destroy
        world.DestroyEntity(entity);
        sut.Sync(world);

        // All create calls on caller's thread
        foreach (var call in factory.ProceduralCalls)
            Assert.Equal(callerThreadId, call.ThreadId);

        // All update calls on caller's thread
        foreach (var call in factory.UpdateCalls)
            Assert.Equal(callerThreadId, call.ThreadId);

        // All destroy calls on caller's thread
        foreach (var call in factory.DestroyCalls)
            Assert.Equal(callerThreadId, call.ThreadId);
    }

    // ════════════════════════════════════════════════════════════════════════
    // T7-SC8: DestroyAll tears down all visuals
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void DestroyAll_CallsDestroy_ForEveryLiveVisual_AndClearsSet()
    {
        var world   = Track(CreateWorld());
        var tkbDb   = CreateTkb();
        var factory = new RecordingFakeFactory();
        var sut     = new StrideVisualBindingSystem(factory, tkbDb);

        const long TkbType = 222L;
        var def = new StrideRenderModelDefDto { ModelAssetRef = "Models/mannequinModel" };
        var template = new TkbTemplate("DestroyAllTest", TkbType);
        template.AddDescriptor(def);
        tkbDb.Register(template);

        for (int i = 0; i < 4; i++)
            SpawnEntity(world, TkbType, new Vector3(i * 1f, 0f, 0f));

        sut.Sync(world);
        Assert.Equal(4, sut.Visuals.Count);

        sut.DestroyAll();

        Assert.Equal(4, factory.DestroyCalls.Count);
        Assert.Equal(0, sut.Visuals.Count);
    }
}
