using System.Numerics;
using Hrot.IG.Components;
using Hrot.IG.Systems;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.IG.Tests;

/// <summary>
/// Unit tests for <see cref="MapCullingSystem"/> (IG.2.4).
///
/// Validates:
/// <list type="bullet">
///   <item>Entities inside the viewport bounds are tagged <c>IsVisible = true</c>.</item>
///   <item>Entities outside (in every cardinal direction) are tagged <c>IsVisible = false</c>.</item>
///   <item>Inclusive boundary — entities exactly on the edge are considered visible.</item>
///   <item>LOD levels are assigned correctly from zoom thresholds in <see cref="CullingStateConstants"/>.</item>
///   <item>The <c>SetComponent</c> update path: a previously-visible entity that leaves the
///         viewport is correctly updated to <c>IsVisible = false</c>.</item>
/// </list>
/// </summary>
public class MapCullingSystemTests
{
    // ── Test constants (§CODE-STANDARDS §1) ───────────────────────────────────

    // Viewport: [0, 1000] × [0, 1000]  (a 1 km² area centred around origin)
    private const float VpMinX = 0f;
    private const float VpMaxX = 1000f;
    private const float VpMinY = 0f;
    private const float VpMaxY = 1000f;

    // ── Fixture helpers ───────────────────────────────────────────────────────

    private static EntityRepository CreateRepo()
    {
        var repo = new EntityRepository();
        repo.RegisterComponent<SimTransform>();
        repo.RegisterComponent<CullingState>();
        return repo;
    }

    private static MapCameraViewport MakeViewport(
        float minX = VpMinX, float maxX = VpMaxX,
        float minY = VpMinY, float maxY = VpMaxY,
        float zoom = IgCameraConstants.InitialZoom)
        => new MapCameraViewport
        {
            WorldMinX = minX,
            WorldMaxX = maxX,
            WorldMinY = minY,
            WorldMaxY = maxY,
            Zoom      = zoom,
        };

    private static Entity CreateEntityAt(EntityRepository repo, float x, float y)
    {
        var entity = repo.CreateEntity();
        repo.AddComponent(entity, new SimTransform
        {
            Position = new Vector3(x, y, 0f),
            Rotation = Quaternion.Identity,
        });
        return entity;
    }

    private static void RunSystem(EntityRepository repo, MapCullingSystem system)
    {
        system.Execute(repo, 0f);
        var cb = (EntityCommandBuffer)((ISimulationView)repo).GetCommandBuffer();
        cb.Playback(repo);
    }

    // ── Visibility: inside bounds ─────────────────────────────────────────────

    /// <summary>An entity at the centre of the viewport must be marked visible.</summary>
    [Fact]
    public void Execute_EntityInsideBounds_IsVisible()
    {
        var repo    = CreateRepo();
        var entity  = CreateEntityAt(repo, 500f, 500f);
        var viewport = MakeViewport();
        var system  = new MapCullingSystem(viewport);

        RunSystem(repo, system);

        Assert.True(repo.GetComponent<CullingState>(entity).IsVisible);
    }

    /// <summary>Entity to the left of <c>WorldMinX</c> must not be visible.</summary>
    [Fact]
    public void Execute_EntityLeftOfBounds_NotVisible()
    {
        var repo    = CreateRepo();
        var entity  = CreateEntityAt(repo, VpMinX - 1f, 500f);
        var viewport = MakeViewport();
        var system  = new MapCullingSystem(viewport);

        RunSystem(repo, system);

        Assert.False(repo.GetComponent<CullingState>(entity).IsVisible);
    }

    /// <summary>Entity to the right of <c>WorldMaxX</c> must not be visible.</summary>
    [Fact]
    public void Execute_EntityRightOfBounds_NotVisible()
    {
        var repo    = CreateRepo();
        var entity  = CreateEntityAt(repo, VpMaxX + 1f, 500f);
        var viewport = MakeViewport();
        var system  = new MapCullingSystem(viewport);

        RunSystem(repo, system);

        Assert.False(repo.GetComponent<CullingState>(entity).IsVisible);
    }

    /// <summary>Entity below <c>WorldMinY</c> must not be visible.</summary>
    [Fact]
    public void Execute_EntityBelowBounds_NotVisible()
    {
        var repo    = CreateRepo();
        var entity  = CreateEntityAt(repo, 500f, VpMinY - 1f);
        var viewport = MakeViewport();
        var system  = new MapCullingSystem(viewport);

        RunSystem(repo, system);

        Assert.False(repo.GetComponent<CullingState>(entity).IsVisible);
    }

    /// <summary>Entity above <c>WorldMaxY</c> must not be visible.</summary>
    [Fact]
    public void Execute_EntityAboveBounds_NotVisible()
    {
        var repo    = CreateRepo();
        var entity  = CreateEntityAt(repo, 500f, VpMaxY + 1f);
        var viewport = MakeViewport();
        var system  = new MapCullingSystem(viewport);

        RunSystem(repo, system);

        Assert.False(repo.GetComponent<CullingState>(entity).IsVisible);
    }

    // ── Boundary edge tests (mandatory per §IG-BATCH-04 test requirements) ───

    /// <summary>
    /// An entity exactly on the left boundary (<c>x == WorldMinX</c>) is inside
    /// the inclusive bounds — must be visible.
    /// </summary>
    [Fact]
    public void Execute_EntityExactlyOnLeftBoundary_IsVisible()
    {
        var repo    = CreateRepo();
        var entity  = CreateEntityAt(repo, VpMinX, 500f);
        var viewport = MakeViewport();
        var system  = new MapCullingSystem(viewport);

        RunSystem(repo, system);

        Assert.True(repo.GetComponent<CullingState>(entity).IsVisible);
    }

    /// <summary>
    /// An entity exactly on the right boundary (<c>x == WorldMaxX</c>) is inside
    /// the inclusive bounds — must be visible.
    /// </summary>
    [Fact]
    public void Execute_EntityExactlyOnRightBoundary_IsVisible()
    {
        var repo    = CreateRepo();
        var entity  = CreateEntityAt(repo, VpMaxX, 500f);
        var viewport = MakeViewport();
        var system  = new MapCullingSystem(viewport);

        RunSystem(repo, system);

        Assert.True(repo.GetComponent<CullingState>(entity).IsVisible);
    }

    /// <summary>
    /// An entity exactly on the top boundary (<c>y == WorldMaxY</c>) is inside
    /// the inclusive bounds — must be visible.
    /// </summary>
    [Fact]
    public void Execute_EntityExactlyOnTopBoundary_IsVisible()
    {
        var repo    = CreateRepo();
        var entity  = CreateEntityAt(repo, 500f, VpMaxY);
        var viewport = MakeViewport();
        var system  = new MapCullingSystem(viewport);

        RunSystem(repo, system);

        Assert.True(repo.GetComponent<CullingState>(entity).IsVisible);
    }

    /// <summary>
    /// An entity one unit beyond the right boundary (<c>x == WorldMaxX + 1</c>)
    /// must NOT be visible.  Confirms the boundary is exclusive of values past it.
    /// </summary>
    [Fact]
    public void Execute_EntityJustOutsideRightBoundary_NotVisible()
    {
        var repo    = CreateRepo();
        var entity  = CreateEntityAt(repo, VpMaxX + 0.001f, 500f);
        var viewport = MakeViewport();
        var system  = new MapCullingSystem(viewport);

        RunSystem(repo, system);

        Assert.False(repo.GetComponent<CullingState>(entity).IsVisible);
    }

    // ── LOD level assignment ──────────────────────────────────────────────────

    /// <summary>
    /// Zoom below <see cref="CullingStateConstants.LodIconOnlyZoomThreshold"/>
    /// must assign <see cref="CullingStateConstants.LodIconOnly"/> (2).
    /// </summary>
    [Fact]
    public void Execute_LowZoom_AssignsLodIconOnly()
    {
        const float lowZoom = CullingStateConstants.LodIconOnlyZoomThreshold - 0.01f; // 0.09f
        var repo    = CreateRepo();
        var entity  = CreateEntityAt(repo, 500f, 500f);
        var viewport = MakeViewport(zoom: lowZoom);
        var system  = new MapCullingSystem(viewport);

        RunSystem(repo, system);

        Assert.Equal(CullingStateConstants.LodIconOnly, repo.GetComponent<CullingState>(entity).LodLevel);
    }

    /// <summary>
    /// Zoom between <see cref="CullingStateConstants.LodIconOnlyZoomThreshold"/>
    /// and <see cref="CullingStateConstants.LodSimplifiedZoomThreshold"/> must
    /// assign <see cref="CullingStateConstants.LodSimplified"/> (1).
    /// </summary>
    [Fact]
    public void Execute_MidZoom_AssignsLodSimplified()
    {
        // In the middle of the [0.1, 0.5) window
        const float midZoom = 0.3f;
        var repo    = CreateRepo();
        var entity  = CreateEntityAt(repo, 500f, 500f);
        var viewport = MakeViewport(zoom: midZoom);
        var system  = new MapCullingSystem(viewport);

        RunSystem(repo, system);

        Assert.Equal(CullingStateConstants.LodSimplified, repo.GetComponent<CullingState>(entity).LodLevel);
    }

    /// <summary>
    /// Zoom at or above <see cref="CullingStateConstants.LodSimplifiedZoomThreshold"/>
    /// must assign <see cref="CullingStateConstants.LodFull"/> (0).
    /// </summary>
    [Fact]
    public void Execute_HighZoom_AssignsLodFull()
    {
        const float highZoom = 1.0f;
        var repo    = CreateRepo();
        var entity  = CreateEntityAt(repo, 500f, 500f);
        var viewport = MakeViewport(zoom: highZoom);
        var system  = new MapCullingSystem(viewport);

        RunSystem(repo, system);

        Assert.Equal(CullingStateConstants.LodFull, repo.GetComponent<CullingState>(entity).LodLevel);
    }

    /// <summary>
    /// Zoom exactly at <see cref="CullingStateConstants.LodSimplifiedZoomThreshold"/>
    /// must produce <see cref="CullingStateConstants.LodFull"/>, not Simplified
    /// (threshold is exclusive from below).
    /// </summary>
    [Fact]
    public void Execute_ZoomExactlyAtSimplifiedThreshold_AssignsLodFull()
    {
        const float zoom = CullingStateConstants.LodSimplifiedZoomThreshold; // 0.5f
        var repo    = CreateRepo();
        var entity  = CreateEntityAt(repo, 500f, 500f);
        var viewport = MakeViewport(zoom: zoom);
        var system  = new MapCullingSystem(viewport);

        RunSystem(repo, system);

        Assert.Equal(CullingStateConstants.LodFull, repo.GetComponent<CullingState>(entity).LodLevel);
    }

    // ── Update path — entity leaves view ─────────────────────────────────────

    /// <summary>
    /// An entity that was visible in the first frame and then moves outside the
    /// viewport in the second frame must be updated to <c>IsVisible = false</c>.
    ///
    /// This tests the <c>cmd.SetComponent</c> update path (component already exists).
    /// </summary>
    [Fact]
    public void Execute_EntityLeavesViewport_IsVisibleUpdatedToFalse()
    {
        var repo    = CreateRepo();
        var entity  = CreateEntityAt(repo, 500f, 500f); // inside
        var viewport = MakeViewport();
        var system  = new MapCullingSystem(viewport);

        // First run — entity is inside the viewport.
        RunSystem(repo, system);
        Assert.True(repo.GetComponent<CullingState>(entity).IsVisible,
            "Entity should be visible before moving outside the viewport.");

        // Move entity outside the viewport.
        repo.SetComponent(entity, new SimTransform
        {
            Position = new Vector3(VpMaxX + 500f, 500f, 0f),
            Rotation = Quaternion.Identity,
        });

        // Second run — entity is now outside.
        RunSystem(repo, system);
        Assert.False(repo.GetComponent<CullingState>(entity).IsVisible,
            "Entity should be invisible after moving outside the viewport.");
    }

    /// <summary>
    /// An entity that starts outside and then moves inside must be updated to
    /// <c>IsVisible = true</c> on the subsequent frame.
    /// </summary>
    [Fact]
    public void Execute_EntityEntersViewport_IsVisibleUpdatedToTrue()
    {
        var repo    = CreateRepo();
        var entity  = CreateEntityAt(repo, VpMaxX + 500f, 500f); // outside
        var viewport = MakeViewport();
        var system  = new MapCullingSystem(viewport);

        RunSystem(repo, system);
        Assert.False(repo.GetComponent<CullingState>(entity).IsVisible);

        // Move entity inside the viewport.
        repo.SetComponent(entity, new SimTransform
        {
            Position = new Vector3(500f, 500f, 0f),
            Rotation = Quaternion.Identity,
        });

        RunSystem(repo, system);
        Assert.True(repo.GetComponent<CullingState>(entity).IsVisible);
    }

    // ── Multi-entity independence ─────────────────────────────────────────────

    /// <summary>
    /// Multiple entities in different positions must each receive independent
    /// <see cref="CullingState"/> values — no cross-contamination between entities.
    /// </summary>
    [Fact]
    public void Execute_MultipleEntities_TaggedIndependently()
    {
        var repo     = CreateRepo();
        var inside1  = CreateEntityAt(repo, 100f, 100f);  // inside
        var inside2  = CreateEntityAt(repo, 900f, 900f);  // inside
        var outside1 = CreateEntityAt(repo, VpMaxX + 100f, 500f);  // outside right
        var outside2 = CreateEntityAt(repo, 500f, VpMaxY + 100f);  // outside above

        var viewport = MakeViewport();
        var system   = new MapCullingSystem(viewport);

        RunSystem(repo, system);

        Assert.True (repo.GetComponent<CullingState>(inside1 ).IsVisible, "inside1  should be visible");
        Assert.True (repo.GetComponent<CullingState>(inside2 ).IsVisible, "inside2  should be visible");
        Assert.False(repo.GetComponent<CullingState>(outside1).IsVisible, "outside1 should not be visible");
        Assert.False(repo.GetComponent<CullingState>(outside2).IsVisible, "outside2 should not be visible");
    }

    // ── Component creation path ───────────────────────────────────────────────

    /// <summary>
    /// An entity that did not previously have a <see cref="CullingState"/> must
    /// have one added (not thrown) after the first run.
    /// </summary>
    [Fact]
    public void Execute_EntityWithNoExistingCullingState_ComponentAdded()
    {
        var repo    = CreateRepo();
        var entity  = CreateEntityAt(repo, 500f, 500f);
        var viewport = MakeViewport();
        var system  = new MapCullingSystem(viewport);

        Assert.False(repo.HasComponent<CullingState>(entity),
            "CullingState must not exist before the system runs.");

        RunSystem(repo, system);

        Assert.True(repo.HasComponent<CullingState>(entity),
            "CullingState must be added after the system runs.");
    }

    // ── CE-131: an UNSET viewport must not blank the map ──────────────────────

    /// <summary>
    /// CE-131. A default-constructed MapCameraViewport has all four bounds at 0f - a degenerate point at
    /// the origin - because they are auto-properties with no initializer. It is filled from the projected
    /// screen corners in IgApplication.Update, INSIDE `if (!_headless)`, so on a headless node it is never
    /// filled at all. Culling against it marked EVERY entity invisible, every tick.
    ///
    /// <para>This stayed invisible for a long time: IG's map was in fact drawn by SimHost's and CGF's
    /// entity projectors, which ignored culling entirely. UXI-23 S2a merged those away and the blanking
    /// surfaced immediately. Absence of a viewport must mean VISIBLE, matching the rule
    /// EntityPresentationGizmo and CullingStateVisibilityPolicy already follow.</para>
    /// </summary>
    [Fact]
    public void Execute_WithAnUnsetViewport_LeavesEveryEntityVisible()
    {
        var repo   = CreateRepo();
        var far    = CreateEntityAt(repo, 5000f, 5000f);
        var origin = CreateEntityAt(repo, 0f, 0f);

        // Exactly what a headless node has: never filled in.
        var system = new MapCullingSystem(new MapCameraViewport());

        RunSystem(repo, system);

        Assert.True(repo.GetComponentRO<CullingState>(far).IsVisible,
            "An unset viewport carries no information. Culling against it is not culling, it is blanking "
          + "- and it blanked IG's map (CE-131).");
        Assert.True(repo.GetComponentRO<CullingState>(origin).IsVisible);
    }

    /// <summary>
    /// The other half: a REAL viewport still culls exactly as before. Without this, "fixing" CE-131 by
    /// disabling culling would pass the rail above and quietly delete the capability (R-137).
    /// </summary>
    [Fact]
    public void Execute_WithARealViewport_StillCullsOutsideEntities()
    {
        var repo    = CreateRepo();
        var inside  = CreateEntityAt(repo, 500f, 500f);
        var outside = CreateEntityAt(repo, 5000f, 5000f);

        RunSystem(repo, new MapCullingSystem(MakeViewport()));

        Assert.True(repo.GetComponentRO<CullingState>(inside).IsVisible);
        Assert.False(repo.GetComponentRO<CullingState>(outside).IsVisible,
            "A host with a real viewport must still cull - CE-131's fix must not cost the capability.");
    }

    /// <summary>A collapsed rect (min == max) is degenerate too, and must not blank the map.</summary>
    [Fact]
    public void Execute_WithACollapsedViewport_LeavesEveryEntityVisible()
    {
        var repo   = CreateRepo();
        var entity = CreateEntityAt(repo, 500f, 500f);

        RunSystem(repo, new MapCullingSystem(
            MakeViewport(minX: 300f, maxX: 300f, minY: 300f, maxY: 300f)));

        Assert.True(repo.GetComponentRO<CullingState>(entity).IsVisible);
    }
}
