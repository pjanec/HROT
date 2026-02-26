using System.Numerics;
using Bagira.IG.Adapters;
using Fdp.Kernel;
using FDP.Toolkit.Replication.Components;

namespace Bagira.IG.Tests;

/// <summary>
/// Unit tests for <see cref="StubVisualizerAdapter"/>.
///
/// Validates coordinate mapping (<see cref="StubVisualizerAdapter.GetPosition"/>),
/// hit-radius constant alignment (<see cref="StubVisualizerAdapter.GetHitRadius"/>),
/// and hover-label formatting (<see cref="StubVisualizerAdapter.GetHoverLabel"/>).
///
/// <c>Render</c> draws directly to Raylib and requires a window context; those
/// paths are validated by visual smoke-tests, not automated unit tests.
/// </summary>
public class StubVisualizerAdapterTests
{
    // ── Fixture helpers ───────────────────────────────────────────────────────

    private static EntityRepository CreateRepo()
    {
        var repo = new EntityRepository();
        repo.RegisterComponent<SimTransform>();
        repo.RegisterComponent<NetworkIdentity>();
        return repo;
    }

    // ── GetPosition ───────────────────────────────────────────────────────────

    /// <summary>
    /// An entity without a <see cref="SimTransform"/> must not be positioned.
    /// Returning <c>null</c> signals to <c>EntityRenderLayer</c> to skip rendering.
    /// </summary>
    [Fact]
    public void GetPosition_EntityWithNoSimTransform_ReturnsNull()
    {
        var repo    = CreateRepo();
        var entity  = repo.CreateEntity();
        var adapter = new StubVisualizerAdapter();

        var result = adapter.GetPosition(repo, entity);

        Assert.Null(result);
    }

    /// <summary>
    /// An entity with a <see cref="SimTransform"/> must return the XY position
    /// from the flat-Earth Cartesian components (Z is ignored by the 2-D canvas).
    /// </summary>
    [Fact]
    public void GetPosition_EntityWithSimTransform_ReturnsXYFromEcsComponent()
    {
        var repo   = CreateRepo();
        var entity = repo.CreateEntity();
        repo.SetComponent(entity, new SimTransform
        {
            Position = new Vector3(150.0f, 275.0f, 10.0f), // Z = altitude (ignored)
            Rotation = Quaternion.Identity,
        });
        var adapter = new StubVisualizerAdapter();

        var result = adapter.GetPosition(repo, entity);

        Assert.NotNull(result);
        Assert.Equal(150.0f, result!.Value.X, precision: 4);
        Assert.Equal(275.0f, result!.Value.Y, precision: 4);
    }

    /// <summary>
    /// Altitude (Z component) must NOT bleed into the returned 2-D position.
    /// </summary>
    [Fact]
    public void GetPosition_EntityWithSimTransform_ZComponentNotIncludedInResult()
    {
        var repo   = CreateRepo();
        var entity = repo.CreateEntity();
        repo.SetComponent(entity, new SimTransform
        {
            Position = new Vector3(0.0f, 0.0f, 999.0f), // only altitude set
            Rotation = Quaternion.Identity,
        });
        var adapter = new StubVisualizerAdapter();

        var result = adapter.GetPosition(repo, entity);

        Assert.NotNull(result);
        // Both X and Y must be zero — altitude must not appear in either
        Assert.Equal(0.0f, result!.Value.X, precision: 4);
        Assert.Equal(0.0f, result!.Value.Y, precision: 4);
    }

    // ── GetHitRadius ──────────────────────────────────────────────────────────

    /// <summary>
    /// The hit radius must equal <see cref="StubVisualizerConstants.HitRadiusWorldUnits"/>
    /// so that pick-tests remain consistent with the rendered circle size.
    /// </summary>
    [Fact]
    public void GetHitRadius_ReturnsStubVisualizerConstant()
    {
        var repo    = CreateRepo();
        var entity  = repo.CreateEntity();
        var adapter = new StubVisualizerAdapter();

        float radius = adapter.GetHitRadius(repo, entity);

        Assert.Equal(StubVisualizerConstants.HitRadiusWorldUnits, radius);
    }

    /// <summary>
    /// Pins the calculated hit radius against the camera constants so that
    /// changing <see cref="IgCameraConstants.InitialZoom"/> or
    /// <see cref="StubVisualizerConstants.CircleRadiusPx"/> immediately breaks
    /// this test and forces a conscious review.
    /// </summary>
    [Fact]
    public void GetHitRadius_MatchesExpectedWorldUnitsForDefaultZoom()
    {
        // At the initial 0.5 px/m zoom, 10 px covers 20 m.
        const float expectedRadius =
            (float)StubVisualizerConstants.CircleRadiusPx / IgCameraConstants.InitialZoom;

        Assert.Equal(expectedRadius, StubVisualizerConstants.HitRadiusWorldUnits, precision: 4);
    }

    // ── GetHoverLabel ─────────────────────────────────────────────────────────

    /// <summary>
    /// An entity without a <see cref="NetworkIdentity"/> must return <c>null</c>
    /// from GetHoverLabel (no tooltip to show).
    /// </summary>
    [Fact]
    public void GetHoverLabel_EntityWithNoNetworkIdentity_ReturnsNull()
    {
        var repo    = CreateRepo();
        var entity  = repo.CreateEntity();
        var adapter = new StubVisualizerAdapter();

        string? label = adapter.GetHoverLabel(repo, entity);

        Assert.Null(label);
    }

    /// <summary>
    /// An entity with a <see cref="NetworkIdentity"/> must return a label of the
    /// form <c>"Entity #&lt;id&gt;"</c> so the operator can identify the entity
    /// by its network ID on hover.
    /// </summary>
    [Fact]
    public void GetHoverLabel_EntityWithNetworkIdentity_ReturnsFormattedLabel()
    {
        const long testId = 999L;
        var repo   = CreateRepo();
        var entity = repo.CreateEntity();
        repo.SetComponent(entity, new NetworkIdentity(testId));
        var adapter = new StubVisualizerAdapter();

        string? label = adapter.GetHoverLabel(repo, entity);

        Assert.Equal($"Entity #{testId}", label);
    }

    /// <summary>
    /// Two entities with different network IDs must produce distinct labels.
    /// Ensures no shared-state bug between successive GetHoverLabel calls.
    /// </summary>
    [Fact]
    public void GetHoverLabel_TwoEntitiesWithDifferentIds_ProduceDifferentLabels()
    {
        var repo    = CreateRepo();
        var entityA = repo.CreateEntity();
        var entityB = repo.CreateEntity();

        repo.SetComponent(entityA, new NetworkIdentity(100L));
        repo.SetComponent(entityB, new NetworkIdentity(200L));

        var adapter = new StubVisualizerAdapter();

        string? labelA = adapter.GetHoverLabel(repo, entityA);
        string? labelB = adapter.GetHoverLabel(repo, entityB);

        Assert.NotEqual(labelA, labelB);
        Assert.Equal("Entity #100", labelA);
        Assert.Equal("Entity #200", labelB);
    }
}
