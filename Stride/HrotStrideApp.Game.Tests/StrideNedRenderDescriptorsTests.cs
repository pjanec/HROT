using System;
using CarKinem.Core;
using CarKinem.Formation;
using CarKinem.Tkb;
using Fdp.Core;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Physics.Components;
using Fdp.Toolkit.Tkb;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.Map.Definitions.Tkb;
using HrotStrideApp;
using Xunit;

namespace HrotStrideApp.Tests;

/// <summary>
/// Tests for <see cref="StrideNedRenderDescriptors"/> (BATCH-S2-E Task 4).
///
/// <para>
/// All tests run CPU-only, no GPU / Stride engine required.
/// </para>
/// </summary>
public sealed class StrideNedRenderDescriptorsTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Build a <see cref="TkbDatabase"/> with the NED catalog registered
    /// (types 100-103, 200-201, 301-303, 8801-8803).
    /// </summary>
    private static TkbDatabase BuildNedDb()
    {
        var db = new TkbDatabase();
        NedTkbCatalog.RegisterAll(db);
        return db;
    }

    // ── Test 1: vehicle augmentation ─────────────────────────────────────────

    [Fact]
    public void Apply_Vehicle_AddsOrientedBoxRenderDef()
    {
        var db = BuildNedDb();
        StrideNedRenderDescriptors.Apply(db);

        Assert.True(db.TryGetByType(100, out var t), "Type 100 (Tank_M1Abrams) must be registered");
        var renderDef = t!.GetDescriptor<StrideRenderModelDefDto>();

        Assert.NotNull(renderDef);
        Assert.Equal(CollisionShapeKind.OrientedBox, renderDef.ShapeKind);
        Assert.Equal("Models/Box2x1x1", renderDef.ModelAssetRef);
        // Half-extents from the batch spec
        Assert.Equal(3.97f, renderDef.BoxHalfX);
        Assert.Equal(1.83f, renderDef.BoxHalfY);
        Assert.Equal(1.22f, renderDef.BoxHalfZ);
        Assert.Equal(2.44f, renderDef.ShapeHeight);
    }

    // ── Test 2: infantry augmentation ─────────────────────────────────────────

    [Fact]
    public void Apply_Infantry_AddsCapsuleRenderDef()
    {
        var db = BuildNedDb();
        StrideNedRenderDescriptors.Apply(db);

        Assert.True(db.TryGetByType(200, out var t), "Type 200 (Infantry_Rifleman) must be registered");
        var renderDef = t!.GetDescriptor<StrideRenderModelDefDto>();

        Assert.NotNull(renderDef);
        Assert.Equal(CollisionShapeKind.Capsule, renderDef.ShapeKind);
        Assert.Equal("Models/mannequinModel", renderDef.ModelAssetRef);
        Assert.NotEmpty(renderDef.SkeletonAssetRef);
        Assert.Equal("Models/mannequinModel Skeleton", renderDef.SkeletonAssetRef);
        Assert.Equal(0.3f, renderDef.ShapeRadius);
        Assert.Equal(1.8f, renderDef.ShapeHeight);
    }

    // ── Test 3: composite / overlay untouched ────────────────────────────────

    [Fact]
    public void Apply_Composite_AndTacGraphic_HaveNoRenderDef()
    {
        var db = BuildNedDb();
        StrideNedRenderDescriptors.Apply(db);

        // Composite unit (303 = Unit_TankPlatoon_Auto)
        Assert.True(db.TryGetByType(303, out var composite303), "Type 303 must be registered");
        Assert.Null(composite303!.GetDescriptor<StrideRenderModelDefDto>());

        // Tactical graphic (8803 = TacGraphic_Area)
        Assert.True(db.TryGetByType(8803, out var tacGraphic), "Type 8803 must be registered");
        Assert.Null(tacGraphic!.GetDescriptor<StrideRenderModelDefDto>());
    }

    // ── Test 4: idempotency ───────────────────────────────────────────────────

    [Fact]
    public void Apply_CalledTwice_DoesNotThrow_AndRenderDefUnchanged()
    {
        var db = BuildNedDb();
        StrideNedRenderDescriptors.Apply(db);
        StrideNedRenderDescriptors.Apply(db); // second call must be no-op

        Assert.True(db.TryGetByType(100, out var t));
        // Still exactly one descriptor — HasDescriptor true
        Assert.True(t!.HasDescriptor<StrideRenderModelDefDto>());
        var renderDef = t.GetDescriptor<StrideRenderModelDefDto>();
        Assert.NotNull(renderDef);
        Assert.Equal(CollisionShapeKind.OrientedBox, renderDef!.ShapeKind);
        Assert.Equal("Models/Box2x1x1", renderDef.ModelAssetRef);
    }

    // ── Test 5: translator integration ───────────────────────────────────────

    /// <summary>
    /// Creates the minimum <see cref="EntityRepository"/> that satisfies the
    /// component-registration guards in <see cref="VehicleKinematicsTkbTranslator.Inject"/>.
    /// </summary>
    private static EntityRepository CreateRepoWithVehicleComponents()
    {
        var repo = new EntityRepository();
        repo.RegisterComponent<VehicleParams>();
        repo.RegisterComponent<VehicleState>();
        repo.RegisterComponent<NavState>();
        repo.RegisterComponent<PhysicsCollider>();
        repo.RegisterComponent<NavigationIntent>();
        repo.RegisterComponent<NavigationStatus>();
        repo.RegisterComponent<FrustrationTicks>();
        repo.RegisterComponent<FormationController>();
        return repo;
    }

    [Fact]
    public void Translator_Vehicle100_InjectsVehicleStateAndParams()
    {
        var db = BuildNedDb();
        StrideNedRenderDescriptors.Apply(db);

        Assert.True(db.TryGetByType(100, out var template100));
        var repo = CreateRepoWithVehicleComponents();
        var entity = repo.CreateEntity();

        var translator = new VehicleKinematicsTkbTranslator();
        translator.Inject(repo, entity, template100!);

        // A vehicle-shaped template (OrientedBox) MUST get VehicleState and VehicleParams
        Assert.True(repo.HasComponent<VehicleState>(entity),
            "VehicleState must be injected for type 100 (OrientedBox vehicle)");
        Assert.True(repo.HasComponent<VehicleParams>(entity),
            "VehicleParams must be injected for type 100 (OrientedBox vehicle)");
    }

    [Fact]
    public void Translator_Infantry200_DoesNotInjectVehicleState()
    {
        var db = BuildNedDb();
        StrideNedRenderDescriptors.Apply(db);

        Assert.True(db.TryGetByType(200, out var template200));
        // type 200 must already carry VehicleParametersDto (from NedTkbCatalog.WithPhysics)
        Assert.NotNull(template200!.GetDescriptor<VehicleParametersDto>());

        var repo = CreateRepoWithVehicleComponents();
        var entity = repo.CreateEntity();

        var translator = new VehicleKinematicsTkbTranslator();
        translator.Inject(repo, entity, template200);

        // Infantry is Capsule-shaped → must NOT get VehicleState
        Assert.False(repo.HasComponent<VehicleState>(entity),
            "VehicleState must NOT be injected for type 200 (Capsule infantry)");
        Assert.False(repo.HasComponent<VehicleParams>(entity),
            "VehicleParams must NOT be injected for type 200 (Capsule infantry)");
    }
}
