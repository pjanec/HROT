using System.Numerics;
using Hrot.NED.Descriptors;
using Hrot.IG.Components;
using Hrot.IG.UI;
using Fdp.Kernel;
using FDP.Toolkit.Replication.Components;
using Fdp.ModuleHost_Core.Abstractions;

namespace Hrot.IG.Tests;

/// <summary>
/// Unit tests for Task IG.5.2: <see cref="EntityInspectorState"/>.
///
/// Validates:
/// <list type="bullet">
///   <item><see cref="EntityInspectorState.Refresh"/> with a live entity extracts
///         <see cref="NetworkIdentity"/>, <see cref="TkbIdentity"/>,
///         <see cref="SimTransform"/>, and <see cref="ResolvedStyle"/> fields correctly.</item>
///   <item>Calling <see cref="EntityInspectorState.Refresh"/> with <see cref="Entity.Null"/>
///         sets <see cref="EntityInspectorState.HasSelection"/> to <c>false</c>.</item>
///   <item><see cref="EntityInspectorState.Clear"/> resets selection state.</item>
///   <item>An entity missing optional components leaves those fields at default without
///         throwing.</item>
/// </list>
///
/// No DDS or Raylib window context required.
/// </summary>
public class EntityInspectorStateTests
{
    // ── Test constants (§CODE-STANDARDS §1) ───────────────────────────────────

    private const int   TestEntityId   = 42;
    private const long  TestTkbType    = 101L;
    private const float TestPosX       = 123.4f;
    private const float TestPosY       = 567.8f;
    private const float TestPosZ       = 9.0f;
    private const float TestDamage     = 37.5f;

    // ── World factory ─────────────────────────────────────────────────────────

    private static EntityRepository CreateRepo()
    {
        var repo = new EntityRepository();
        repo.RegisterComponent<NetworkIdentity>();
        repo.RegisterComponent<TkbIdentity>();
        repo.RegisterComponent<SimTransform>();
        repo.RegisterComponent<ResolvedStyle>();
        return repo;
    }

    /// <summary>Creates an entity with all three inspectable components set to test values.</summary>
    private static Entity CreateFullEntity(EntityRepository repo)
    {
        var entity = repo.CreateEntity();

        repo.AddComponent(entity, new NetworkIdentity(TestEntityId));
        repo.AddComponent(entity, new TkbIdentity { TkbType = TestTkbType });

        repo.AddComponent(entity, new SimTransform
        {
            Position = new Vector3(TestPosX, TestPosY, TestPosZ),
            Rotation = Quaternion.Identity,
        });

        var style = ResolvedStyle.CreateDefault();
        style.Affiliation = ForceId.Hostile;
        style.DamageLevel = TestDamage;
        repo.AddComponent(entity, style);

        return entity;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Refresh — valid entity with all components
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>After Refresh, HasSelection must be true for a live entity.</summary>
    [Fact]
    public void Refresh_LiveEntity_HasSelectionIsTrue()
    {
        var repo   = CreateRepo();
        var entity = CreateFullEntity(repo);
        var state  = new EntityInspectorState();

        state.Refresh(repo, entity);

        Assert.True(state.HasSelection);
    }

    /// <summary>After Refresh, InspectedEntity must equal the supplied entity.</summary>
    [Fact]
    public void Refresh_LiveEntity_InspectedEntitySet()
    {
        var repo   = CreateRepo();
        var entity = CreateFullEntity(repo);
        var state  = new EntityInspectorState();

        state.Refresh(repo, entity);

        Assert.Equal(entity, state.InspectedEntity);
    }

    /// <summary>EntityId must be extracted correctly from NetworkIdentity.</summary>
    [Fact]
    public void Refresh_LiveEntity_ExtractsEntityId()
    {
        var repo   = CreateRepo();
        var entity = CreateFullEntity(repo);
        var state  = new EntityInspectorState();

        state.Refresh(repo, entity);

        Assert.Equal(TestEntityId, state.EntityId);
    }

    /// <summary>TkbType must be extracted correctly from TkbIdentity.</summary>
    [Fact]
    public void Refresh_LiveEntity_ExtractsTkbType()
    {
        var repo   = CreateRepo();
        var entity = CreateFullEntity(repo);
        var state  = new EntityInspectorState();

        state.Refresh(repo, entity);

        Assert.Equal(TestTkbType, state.TkbType);
    }

    /// <summary>PositionX must match the entity's SimTransform X coordinate.</summary>
    [Fact]
    public void Refresh_LiveEntity_ExtractsPositionX()
    {
        var repo   = CreateRepo();
        var entity = CreateFullEntity(repo);
        var state  = new EntityInspectorState();

        state.Refresh(repo, entity);

        Assert.Equal(TestPosX, state.PositionX);
    }

    /// <summary>PositionY must match the entity's SimTransform Y coordinate.</summary>
    [Fact]
    public void Refresh_LiveEntity_ExtractsPositionY()
    {
        var repo   = CreateRepo();
        var entity = CreateFullEntity(repo);
        var state  = new EntityInspectorState();

        state.Refresh(repo, entity);

        Assert.Equal(TestPosY, state.PositionY);
    }

    /// <summary>PositionZ must match the entity's SimTransform Z coordinate.</summary>
    [Fact]
    public void Refresh_LiveEntity_ExtractsPositionZ()
    {
        var repo   = CreateRepo();
        var entity = CreateFullEntity(repo);
        var state  = new EntityInspectorState();

        state.Refresh(repo, entity);

        Assert.Equal(TestPosZ, state.PositionZ);
    }

    /// <summary>Affiliation must be extracted correctly from ResolvedStyle.</summary>
    [Fact]
    public void Refresh_LiveEntity_ExtractsAffiliation()
    {
        var repo   = CreateRepo();
        var entity = CreateFullEntity(repo);
        var state  = new EntityInspectorState();

        state.Refresh(repo, entity);

        Assert.Equal(ForceId.Hostile, state.Affiliation);
    }

    /// <summary>DamageLevel must be extracted correctly from ResolvedStyle.</summary>
    [Fact]
    public void Refresh_LiveEntity_ExtractsDamageLevel()
    {
        var repo   = CreateRepo();
        var entity = CreateFullEntity(repo);
        var state  = new EntityInspectorState();

        state.Refresh(repo, entity);

        Assert.Equal(TestDamage, state.DamageLevel);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Refresh — Entity.Null clears selection
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Passing <see cref="Entity.Null"/> to Refresh must set HasSelection to false.
    /// </summary>
    [Fact]
    public void Refresh_NullEntity_HasSelectionIsFalse()
    {
        var repo  = CreateRepo();
        var state = new EntityInspectorState();

        state.Refresh(repo, Entity.Null);

        Assert.False(state.HasSelection);
    }

    /// <summary>
    /// Passing <see cref="Entity.Null"/> to Refresh must set InspectedEntity to Entity.Null.
    /// </summary>
    [Fact]
    public void Refresh_NullEntity_InspectedEntityIsNull()
    {
        var repo  = CreateRepo();
        var state = new EntityInspectorState();

        state.Refresh(repo, Entity.Null);

        Assert.Equal(Entity.Null, state.InspectedEntity);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Clear
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// After a successful Refresh, calling Clear must reset HasSelection to false.
    /// </summary>
    [Fact]
    public void Clear_AfterRefresh_HasSelectionIsFalse()
    {
        var repo   = CreateRepo();
        var entity = CreateFullEntity(repo);
        var state  = new EntityInspectorState();

        state.Refresh(repo, entity);
        state.Clear();

        Assert.False(state.HasSelection);
    }

    /// <summary>
    /// After Clear, InspectedEntity must be Entity.Null.
    /// </summary>
    [Fact]
    public void Clear_AfterRefresh_InspectedEntityIsNull()
    {
        var repo   = CreateRepo();
        var entity = CreateFullEntity(repo);
        var state  = new EntityInspectorState();

        state.Refresh(repo, entity);
        state.Clear();

        Assert.Equal(Entity.Null, state.InspectedEntity);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Refresh — entity with only SimTransform (no ResolvedStyle / EntityMaster)
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// An entity with only a SimTransform and no ResolvedStyle must still set
    /// HasSelection = true without throwing.
    /// </summary>
    [Fact]
    public void Refresh_EntityWithoutResolvedStyle_DoesNotThrow()
    {
        var repo   = CreateRepo();
        var entity = repo.CreateEntity();
        repo.AddComponent(entity, new SimTransform
        {
            Position = new Vector3(1f, 2f, 3f),
            Rotation = Quaternion.Identity,
        });

        var state = new EntityInspectorState();

        var ex = Record.Exception(() => state.Refresh(repo, entity));

        Assert.Null(ex);
        Assert.True(state.HasSelection);
    }

    /// <summary>
    /// Refresh with a different entity value updates all properties to reflect
    /// the new entity's component values.
    /// </summary>
    [Fact]
    public void Refresh_SecondEntity_OverwritesPreviousData()
    {
        var repo    = CreateRepo();
        var entity1 = CreateFullEntity(repo);

        // Create a second entity with different position
        var entity2 = repo.CreateEntity();
        repo.AddComponent(entity2, new NetworkIdentity(99));
        repo.AddComponent(entity2, new TkbIdentity { TkbType = 0 });
        repo.AddComponent(entity2, new SimTransform
        {
            Position = new Vector3(999f, 888f, 777f),
            Rotation = Quaternion.Identity,
        });

        var state = new EntityInspectorState();
        state.Refresh(repo, entity1);
        state.Refresh(repo, entity2);

        Assert.Equal(999f, state.PositionX);
        Assert.Equal(888f, state.PositionY);
        Assert.Equal(99,   state.EntityId);
    }
}
