using System.Numerics;
using Hrot.IG.Components;
using Hrot.IG.UI;
using Fdp.Kernel;
using Fdp.ModuleHost.Core.Abstractions;

namespace Hrot.IG.Tests;

/// <summary>
/// Unit tests for Task IG.5.4: <see cref="PerformanceMetrics"/>.
///
/// Validates that <see cref="PerformanceMetrics.Snapshot"/> correctly distinguishes
/// between visible and culled entities:
/// <list type="bullet">
///   <item>An empty world returns zero for both counters.</item>
///   <item>Entities with <see cref="CullingState.IsVisible"/> = <c>true</c> are included
///         in <see cref="PerformanceMetrics.VisibleEntityCount"/>.</item>
///   <item>Entities with <see cref="CullingState.IsVisible"/> = <c>false</c> are counted
///         in <see cref="PerformanceMetrics.TotalEntityCount"/> but not in
///         <see cref="PerformanceMetrics.VisibleEntityCount"/>.</item>
///   <item>Entities without a <see cref="CullingState"/> component are counted in
///         <see cref="PerformanceMetrics.TotalEntityCount"/> but not in
///         <see cref="PerformanceMetrics.VisibleEntityCount"/>.</item>
///   <item>FPS and frame-time passed to Snapshot are preserved on the properties.</item>
/// </list>
///
/// No DDS or Raylib window context required.
/// </summary>
public class PerformanceMetricsTests
{
    // ── Test constants (§CODE-STANDARDS §1) ───────────────────────────────────

    private const int   TestFps         = 60;
    private const float TestFrameTimeMs = 16.67f;

    // ── World factory ─────────────────────────────────────────────────────────

    private static EntityRepository CreateRepo()
    {
        var repo = new EntityRepository();
        repo.RegisterComponent<SimTransform>();
        repo.RegisterComponent<CullingState>();
        return repo;
    }

    /// <summary>Creates an entity with a SimTransform and an optional CullingState.</summary>
    private static Entity CreateEntity(EntityRepository repo, bool? visible)
    {
        var entity = repo.CreateEntity();
        repo.AddComponent(entity, new SimTransform
        {
            Position = Vector3.Zero,
            Rotation = Quaternion.Identity,
        });

        if (visible.HasValue)
        {
            repo.AddComponent(entity, new CullingState
            {
                IsVisible = visible.Value,
                LodLevel  = CullingStateConstants.LodFull,
            });
        }

        return entity;
    }

    private static void RunSnapshot(EntityRepository repo, PerformanceMetrics metrics)
        => metrics.Snapshot(repo, TestFps, TestFrameTimeMs);

    // ═══════════════════════════════════════════════════════════════════════════
    // Empty world
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>A world with no entities must return TotalEntityCount = 0.</summary>
    [Fact]
    public void Snapshot_EmptyWorld_TotalEntityCountIsZero()
    {
        var repo    = CreateRepo();
        var metrics = new PerformanceMetrics();

        RunSnapshot(repo, metrics);

        Assert.Equal(0, metrics.TotalEntityCount);
    }

    /// <summary>A world with no entities must return VisibleEntityCount = 0.</summary>
    [Fact]
    public void Snapshot_EmptyWorld_VisibleEntityCountIsZero()
    {
        var repo    = CreateRepo();
        var metrics = new PerformanceMetrics();

        RunSnapshot(repo, metrics);

        Assert.Equal(0, metrics.VisibleEntityCount);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // All entities visible
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>When all entities are visible, TotalEntityCount equals VisibleEntityCount.</summary>
    [Fact]
    public void Snapshot_AllVisible_TotalEqualsVisible()
    {
        var repo    = CreateRepo();
        var metrics = new PerformanceMetrics();

        CreateEntity(repo, visible: true);
        CreateEntity(repo, visible: true);
        CreateEntity(repo, visible: true);

        RunSnapshot(repo, metrics);

        Assert.Equal(3, metrics.TotalEntityCount);
        Assert.Equal(3, metrics.VisibleEntityCount);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Mixed visible / culled
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// TotalEntityCount must equal the total number of entities with a SimTransform,
    /// regardless of culling.
    /// </summary>
    [Fact]
    public void Snapshot_MixedVisibility_TotalCountsAllEntities()
    {
        var repo    = CreateRepo();
        var metrics = new PerformanceMetrics();

        CreateEntity(repo, visible: true);
        CreateEntity(repo, visible: true);
        CreateEntity(repo, visible: false);  // culled

        RunSnapshot(repo, metrics);

        Assert.Equal(3, metrics.TotalEntityCount);
    }

    /// <summary>
    /// VisibleEntityCount must count only entities with IsVisible = true.
    /// </summary>
    [Fact]
    public void Snapshot_MixedVisibility_VisibleCountOnlyCountsVisibleEntities()
    {
        var repo    = CreateRepo();
        var metrics = new PerformanceMetrics();

        CreateEntity(repo, visible: true);
        CreateEntity(repo, visible: true);
        CreateEntity(repo, visible: false); // culled

        RunSnapshot(repo, metrics);

        Assert.Equal(2, metrics.VisibleEntityCount);
    }

    /// <summary>
    /// When all entities are culled (IsVisible = false), VisibleEntityCount must be 0.
    /// </summary>
    [Fact]
    public void Snapshot_AllCulled_VisibleEntityCountIsZero()
    {
        var repo    = CreateRepo();
        var metrics = new PerformanceMetrics();

        CreateEntity(repo, visible: false);
        CreateEntity(repo, visible: false);

        RunSnapshot(repo, metrics);

        Assert.Equal(0, metrics.VisibleEntityCount);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Entities without CullingState
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// An entity with SimTransform but no CullingState must be counted in
    /// TotalEntityCount but not in VisibleEntityCount.
    /// </summary>
    [Fact]
    public void Snapshot_EntityWithoutCullingState_CountedInTotalNotVisible()
    {
        var repo    = CreateRepo();
        var metrics = new PerformanceMetrics();

        CreateEntity(repo, visible: null);  // No CullingState

        RunSnapshot(repo, metrics);

        Assert.Equal(1, metrics.TotalEntityCount);
        Assert.Equal(0, metrics.VisibleEntityCount);
    }

    /// <summary>
    /// A mix of entities with/without CullingState is handled correctly.
    /// </summary>
    [Fact]
    public void Snapshot_MixedWithAndWithoutCullingState_CountsCorrectly()
    {
        var repo    = CreateRepo();
        var metrics = new PerformanceMetrics();

        CreateEntity(repo, visible: true);   // visible
        CreateEntity(repo, visible: false);  // culled
        CreateEntity(repo, visible: null);   // no culling state

        RunSnapshot(repo, metrics);

        Assert.Equal(3, metrics.TotalEntityCount);
        Assert.Equal(1, metrics.VisibleEntityCount);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // FPS / frame-time passthrough
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>Fps property must reflect the value passed to Snapshot.</summary>
    [Fact]
    public void Snapshot_FpsPassedIn_StoredOnProperty()
    {
        var repo    = CreateRepo();
        var metrics = new PerformanceMetrics();

        metrics.Snapshot(repo, fps: 120, frameTimeMs: 8.3f);

        Assert.Equal(120, metrics.Fps);
    }

    /// <summary>FrameTimeMs property must reflect the value passed to Snapshot.</summary>
    [Fact]
    public void Snapshot_FrameTimePassedIn_StoredOnProperty()
    {
        var repo    = CreateRepo();
        var metrics = new PerformanceMetrics();

        metrics.Snapshot(repo, fps: 120, frameTimeMs: 8.3f);

        Assert.Equal(8.3f, metrics.FrameTimeMs);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Snapshot update semantics
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A second Snapshot call must overwrite the results from the first call
    /// to reflect the current world state.
    /// </summary>
    [Fact]
    public void Snapshot_CalledTwice_ReflectsMostRecentWorldState()
    {
        var repo    = CreateRepo();
        var metrics = new PerformanceMetrics();

        // First snapshot: 1 visible entity
        CreateEntity(repo, visible: true);
        RunSnapshot(repo, metrics);
        Assert.Equal(1, metrics.TotalEntityCount);

        // Second snapshot: 2 more entities added (now 3 total)
        CreateEntity(repo, visible: false);
        CreateEntity(repo, visible: true);
        RunSnapshot(repo, metrics);

        Assert.Equal(3, metrics.TotalEntityCount);
        Assert.Equal(2, metrics.VisibleEntityCount);
    }
}
