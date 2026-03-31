using System.Numerics;
using Hrot.IG.Adapters;
using Hrot.IG.Components;
using Fdp.Kernel;
using FDP.Toolkit.Vis2D.Defaults;
using FDP.Toolkit.Vis2D.Layers;

namespace Hrot.IG.Tests;

/// <summary>
/// Unit tests for selection and entity-picking mechanics backing
/// <see cref="Tools.StandardInteractionTool"/> (IG.3.1).
///
/// These tests exercise the underlying <see cref="EntityRenderLayer.PickEntity"/> logic
/// and the <see cref="NedVisualizerAdapter.GetHitRadius"/> contract without requiring a
/// Raylib window context or a real <see cref="FDP.Toolkit.Vis2D.MapCanvas"/>.
///
/// Coverage:
/// <list type="bullet">
///   <item>Picking the sole entity within hit radius.</item>
///   <item>Picking the closest entity when two entities are both within hit radius.</item>
///   <item>Returning <c>null</c> when no entity is within hit radius.</item>
///   <item>Entities outside the viewport (IsVisible = false) are not pickable.</item>
/// </list>
/// </summary>
public class StandardInteractionToolTests
{
    // ── Constants (§CODE-STANDARDS §1) ───────────────────────────────────────

    /// <summary>
    /// Hit radius from <see cref="NedVisualizerAdapterConstants.HitRadiusWorldUnits"/>.
    /// Stored as a test-local named constant so an assertion change is a one-line edit.
    /// </summary>
    private const float HitRadius = NedVisualizerAdapterConstants.HitRadiusWorldUnits;

    // ── Fixture helpers ───────────────────────────────────────────────────────

    private static EntityRepository CreateRepo()
    {
        var repo = new EntityRepository();
        repo.RegisterComponent<SimTransform>();
        repo.RegisterComponent<CullingState>();
        repo.RegisterComponent<ResolvedStyle>();
        repo.RegisterComponent<SelectionState>();
        return repo;
    }

    /// <summary>
    /// Creates an entity at the given world position that is tagged as visible by
    /// <see cref="MapCullingSystem"/> so that <see cref="NedVisualizerAdapter.GetPosition"/>
    /// returns a valid position and the entity participates in hit-testing.
    /// </summary>
    private static Entity CreateVisibleEntityAt(EntityRepository repo, float x, float y)
    {
        var entity = repo.CreateEntity();
        repo.AddComponent(entity, new SimTransform
        {
            Position = new Vector3(x, y, 0f),
            Rotation = SimMath.FacingEast,
        });
        repo.AddComponent(entity, new CullingState { IsVisible = true, LodLevel = CullingStateConstants.LodFull });
        return entity;
    }

    private static EntityRenderLayer CreateLayer(EntityRepository repo, EntityQuery query, NedVisualizerAdapter adapter)
        => new EntityRenderLayer("Entities", layerBitIndex: 0, repo, query, adapter, new DefaultSelectionState());

    // ── Pick single entity in radius ──────────────────────────────────────────

    /// <summary>
    /// Clicking exactly on an entity's world position resolves that entity.
    /// </summary>
    [Fact]
    public void PickEntity_ClickOnEntityCenter_ReturnsEntity()
    {
        var repo    = CreateRepo();
        var adapter = new NedVisualizerAdapter();
        var entity  = CreateVisibleEntityAt(repo, 100f, 200f);
        var query   = repo.Query().With<SimTransform>().Build();
        var layer   = CreateLayer(repo, query, adapter);

        var result = layer.PickEntity(new Vector2(100f, 200f));

        Assert.NotNull(result);
        Assert.Equal(entity, result!.Value);
    }

    /// <summary>
    /// Clicking at the edge of the hit radius (inclusive) still resolves the entity.
    /// </summary>
    [Fact]
    public void PickEntity_ClickAtHitRadiusBoundary_ReturnsEntity()
    {
        var repo    = CreateRepo();
        var adapter = new NedVisualizerAdapter();
        var entity  = CreateVisibleEntityAt(repo, 0f, 0f);
        var query   = repo.Query().With<SimTransform>().Build();
        var layer   = CreateLayer(repo, query, adapter);

        // Click exactly at the hit radius distance from center.
        var result = layer.PickEntity(new Vector2(HitRadius, 0f));

        Assert.NotNull(result);
        Assert.Equal(entity, result!.Value);
    }

    /// <summary>
    /// Clicking outside the hit radius returns <c>null</c> (no entity picked).
    /// </summary>
    [Fact]
    public void PickEntity_ClickOutsideHitRadius_ReturnsNull()
    {
        var repo    = CreateRepo();
        var adapter = new NedVisualizerAdapter();
        CreateVisibleEntityAt(repo, 0f, 0f);
        var query   = repo.Query().With<SimTransform>().Build();
        var layer   = CreateLayer(repo, query, adapter);

        // Click just beyond the hit radius.
        var result = layer.PickEntity(new Vector2(HitRadius + 1f, 0f));

        Assert.Null(result);
    }

    // ── Closest-entity resolution ─────────────────────────────────────────────

    /// <summary>
    /// When two entities overlap (both within hit radius of the click point), the
    /// entity whose centre is <em>closest</em> to the click is returned.
    /// This confirms that picking is deterministic and distance-ordered.
    /// </summary>
    [Fact]
    public void PickEntity_TwoOverlappingEntities_ReturnsClosestToClickPoint()
    {
        var repo    = CreateRepo();
        var adapter = new NedVisualizerAdapter();

        // Place two entities within hit radius of the origin.
        float nearDist = HitRadius * 0.25f;
        float farDist  = HitRadius * 0.75f;

        var near = CreateVisibleEntityAt(repo, nearDist, 0f);
        var far  = CreateVisibleEntityAt(repo, farDist,  0f);

        var query = repo.Query().With<SimTransform>().Build();
        var layer = CreateLayer(repo, query, adapter);

        // Click at the origin — both entities are within radius, but `near` is closer.
        var result = layer.PickEntity(Vector2.Zero);

        Assert.NotNull(result);
        Assert.Equal(near, result!.Value);
        Assert.NotEqual(far, result.Value);
    }

    // ── Culled / invisible entity not pickable ────────────────────────────────

    /// <summary>
    /// An entity marked <c>IsVisible = false</c> by <see cref="MapCullingSystem"/> must
    /// not be returned by <see cref="EntityRenderLayer.PickEntity"/> because
    /// <see cref="NedVisualizerAdapter.GetPosition"/> returns <c>null</c> for invisible
    /// entities — they are off-screen and should not be selectable.
    /// </summary>
    [Fact]
    public void PickEntity_InvisibleEntity_ReturnsNull()
    {
        var repo    = CreateRepo();
        var adapter = new NedVisualizerAdapter();
        var entity  = repo.CreateEntity();
        repo.AddComponent(entity, new SimTransform
        {
            Position = new Vector3(0f, 0f, 0f),
            Rotation = SimMath.FacingEast,
        });
        // Visible = false — entity is culled.
        repo.AddComponent(entity, new CullingState { IsVisible = false, LodLevel = CullingStateConstants.LodFull });

        var query = repo.Query().With<SimTransform>().Build();
        var layer = CreateLayer(repo, query, adapter);

        var result = layer.PickEntity(Vector2.Zero);

        Assert.Null(result);
    }
}
