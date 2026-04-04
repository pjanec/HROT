using System.Numerics;
using Hrot.ScenarioEditor.Adapters;
using Hrot.IG.Components;
using Fdp.Kernel;

namespace Hrot.IG.Tests;

/// <summary>
/// Unit tests for <see cref="NedVisualizerAdapter"/>.
///
/// Covers the non-Raylib-rendering surface of the adapter:
/// <see cref="NedVisualizerAdapter.GetPosition"/> (culling gate + coordinate extraction),
/// <see cref="NedVisualizerAdapter.GetHitRadius"/>, and
/// <see cref="NedVisualizerAdapter.GetHoverLabel"/>.
///
/// <c>Render</c> issues Raylib draw calls and requires a window context;
/// those paths are validated by visual smoke-tests, not automated unit tests.
/// </summary>
public class NedVisualizerAdapterTests
{
    // â”€â”€ Fixture helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>
    /// Creates a repository pre-registered with the components exercised by
    /// <see cref="NedVisualizerAdapter"/>.
    /// </summary>
    private static EntityRepository CreateRepo()
    {
        var repo = new EntityRepository();
        repo.RegisterComponent<SimTransform>();
        repo.RegisterComponent<CullingState>();
        repo.RegisterComponent<ResolvedStyle>();
        return repo;
    }

    // â”€â”€ GetPosition: SimTransform absence â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>
    /// An entity without a <see cref="SimTransform"/> must return <c>null</c>.
    /// <c>EntityRenderLayer</c> skips the entity entirely when <c>null</c> is returned.
    /// </summary>
    [Fact]
    public void GetPosition_EntityWithNoSimTransform_ReturnsNull()
    {
        var repo    = CreateRepo();
        var entity  = repo.CreateEntity();
        // No SimTransform added.
        var adapter = new NedVisualizerAdapter();

        var result = adapter.GetPosition(repo, entity);

        Assert.Null(result);
    }

    // â”€â”€ GetPosition: CullingState absence / visibility â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>
    /// An entity with a <see cref="SimTransform"/> but no <see cref="CullingState"/>
    /// must return <c>null</c>.
    /// The culling system has not run yet, so the entity is treated as invisible
    /// to avoid spurious renders before the first frame.
    /// </summary>
    [Fact]
    public void GetPosition_EntityWithNoIsCullingState_ReturnsNull()
    {
        var repo   = CreateRepo();
        var entity = repo.CreateEntity();
        repo.AddComponent(entity, new SimTransform
        {
            Position = new Vector3(100f, 200f, 0f),
            Rotation = Quaternion.Identity,
        });
        // No CullingState added.
        var adapter = new NedVisualizerAdapter();

        var result = adapter.GetPosition(repo, entity);

        Assert.Null(result);
    }

    /// <summary>
    /// An entity with <see cref="CullingState.IsVisible"/> = <c>false</c> must
    /// return <c>null</c> to prevent off-screen draw calls.
    /// </summary>
    [Fact]
    public void GetPosition_CullingStateInvisible_ReturnsNull()
    {
        var repo   = CreateRepo();
        var entity = repo.CreateEntity();
        repo.AddComponent(entity, new SimTransform
        {
            Position = new Vector3(100f, 200f, 0f),
            Rotation = Quaternion.Identity,
        });
        repo.AddComponent(entity, new CullingState { IsVisible = false, LodLevel = CullingStateConstants.LodFull });

        var adapter = new NedVisualizerAdapter();

        var result = adapter.GetPosition(repo, entity);

        Assert.Null(result);
    }

    /// <summary>
    /// An entity with <see cref="CullingState.IsVisible"/> = <c>true</c> must
    /// return its XY world position from <see cref="SimTransform"/>.
    /// </summary>
    [Fact]
    public void GetPosition_CullingStateVisible_ReturnsXYFromSimTransform()
    {
        var repo   = CreateRepo();
        var entity = repo.CreateEntity();
        repo.AddComponent(entity, new SimTransform
        {
            Position = new Vector3(350f, 750f, 0f),
            Rotation = Quaternion.Identity,
        });
        repo.AddComponent(entity, new CullingState { IsVisible = true, LodLevel = CullingStateConstants.LodFull });

        var adapter = new NedVisualizerAdapter();

        var result = adapter.GetPosition(repo, entity);

        Assert.NotNull(result);
        Assert.Equal(350f, result!.Value.X, precision: 4);
        Assert.Equal(750f, result!.Value.Y, precision: 4);
    }

    /// <summary>
    /// Altitude (Z component) must NOT appear in the returned 2-D screen position.
    /// </summary>
    [Fact]
    public void GetPosition_CullingStateVisible_ZComponentNotIncludedInResult()
    {
        var repo   = CreateRepo();
        var entity = repo.CreateEntity();
        repo.AddComponent(entity, new SimTransform
        {
            Position = new Vector3(0f, 0f, 9999f), // only altitude set
            Rotation = Quaternion.Identity,
        });
        repo.AddComponent(entity, new CullingState { IsVisible = true, LodLevel = CullingStateConstants.LodFull });

        var adapter = new NedVisualizerAdapter();

        var result = adapter.GetPosition(repo, entity);

        Assert.NotNull(result);
        Assert.Equal(0f, result!.Value.X, precision: 4);
        Assert.Equal(0f, result!.Value.Y, precision: 4);
    }

    // â”€â”€ GetPosition: correct tint affiliation passthrough (negative path) â”€â”€â”€â”€â”€

    /// <summary>
    /// Entities with LOD 1 (simplified) must still be returned as visible.
    /// Verifies that a non-zero LodLevel does not incorrectly suppress rendering.
    /// </summary>
    [Fact]
    public void GetPosition_VisibleWithLod1_ReturnsPosition()
    {
        var repo   = CreateRepo();
        var entity = repo.CreateEntity();
        repo.AddComponent(entity, new SimTransform
        {
            Position = new Vector3(500f, 600f, 0f),
            Rotation = Quaternion.Identity,
        });
        repo.AddComponent(entity, new CullingState
        {
            IsVisible = true,
            LodLevel  = CullingStateConstants.LodSimplified,
        });

        var adapter = new NedVisualizerAdapter();

        var result = adapter.GetPosition(repo, entity);

        Assert.NotNull(result);
        Assert.Equal(500f, result!.Value.X, precision: 4);
    }

    // â”€â”€ GetHitRadius â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>
    /// Must return the named constant so pick-tests remain consistent with the
    /// rendered circle/texture size.
    /// </summary>
    [Fact]
    public void GetHitRadius_ReturnsConstantWorldUnits()
    {
        var repo    = CreateRepo();
        var entity  = repo.CreateEntity();
        var adapter = new NedVisualizerAdapter();

        float radius = adapter.GetHitRadius(repo, entity);

        Assert.Equal(NedVisualizerAdapterConstants.HitRadiusWorldUnits, radius);
    }

    /// <summary>
    /// Pins the calculated hit radius against camera constants to detect
    /// accidental changes to either <see cref="NedVisualizerAdapterConstants.FallbackCircleRadiusPx"/>
    /// or <see cref="IgCameraConstants.InitialZoom"/>.
    /// </summary>
    [Fact]
    public void GetHitRadius_MatchesExpectedWorldUnitsForDefaultZoom()
    {
        const float expected =
            (float)NedVisualizerAdapterConstants.FallbackCircleRadiusPx
            / IgCameraConstants.InitialZoom;

        Assert.Equal(expected, NedVisualizerAdapterConstants.HitRadiusWorldUnits, precision: 4);
    }

    // â”€â”€ GetHoverLabel â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>
    /// An entity without <see cref="ResolvedStyle"/> must return <c>null</c>
    /// (no tooltip to show).
    /// </summary>
    [Fact]
    public void GetHoverLabel_NoResolvedStyle_ReturnsNull()
    {
        var repo    = CreateRepo();
        var entity  = repo.CreateEntity();
        var adapter = new NedVisualizerAdapter();

        string? label = adapter.GetHoverLabel(repo, entity);

        Assert.Null(label);
    }

    /// <summary>
    /// An entity with <see cref="ResolvedStyle"/> whose label is empty must
    /// return <c>null</c> (suppresses an empty tooltip box in the UI).
    /// </summary>
    [Fact]
    public void GetHoverLabel_StyleWithEmptyLabel_ReturnsNull()
    {
        var repo   = CreateRepo();
        var entity = repo.CreateEntity();

        var style = ResolvedStyle.CreateDefault();
        style.SetLabelText(string.Empty);
        repo.AddComponent(entity, style);

        var adapter = new NedVisualizerAdapter();

        string? label = adapter.GetHoverLabel(repo, entity);

        Assert.Null(label);
    }

    /// <summary>
    /// An entity with a non-empty label in <see cref="ResolvedStyle"/> must
    /// return that label string verbatim.
    /// </summary>
    [Fact]
    public void GetHoverLabel_StyleWithNonEmptyLabel_ReturnsLabel()
    {
        const string testLabel = "Bravo-7";

        var repo   = CreateRepo();
        var entity = repo.CreateEntity();

        var style = ResolvedStyle.CreateDefault();
        style.SetLabelText(testLabel);
        repo.AddComponent(entity, style);

        var adapter = new NedVisualizerAdapter();

        string? label = adapter.GetHoverLabel(repo, entity);

        Assert.Equal(testLabel, label);
    }

    /// <summary>
    /// Two entities with different labels must produce distinct tooltip strings.
    /// Ensures no shared-state bug between successive <c>GetHoverLabel</c> calls.
    /// </summary>
    [Fact]
    public void GetHoverLabel_TwoEntitiesWithDifferentLabels_ProduceDifferentLabels()
    {
        const string labelA = "Alpha-1";
        const string labelB = "Bravo-2";

        var repo    = CreateRepo();
        var entityA = repo.CreateEntity();
        var entityB = repo.CreateEntity();

        var styleA = ResolvedStyle.CreateDefault();
        styleA.SetLabelText(labelA);
        repo.AddComponent(entityA, styleA);

        var styleB = ResolvedStyle.CreateDefault();
        styleB.SetLabelText(labelB);
        repo.AddComponent(entityB, styleB);

        var adapter = new NedVisualizerAdapter();

        Assert.Equal(labelA, adapter.GetHoverLabel(repo, entityA));
        Assert.Equal(labelB, adapter.GetHoverLabel(repo, entityB));
        Assert.NotEqual(
            adapter.GetHoverLabel(repo, entityA),
            adapter.GetHoverLabel(repo, entityB));
    }
}
