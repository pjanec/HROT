using Hrot.Editor;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Browser;

namespace Hrot.Editor.Tests.Browser;

/// <summary>
/// Tests for <see cref="AssetPickActionRouter"/> (MTB-P5-T6).
/// </summary>
public sealed class AssetPickActionRouterTests
{
    // ── Stub IEditableAsset ─────────────────────────────────────────────

    private sealed class StubAsset : IEditableAsset
    {
        public Guid AssetId { get; init; } = Guid.NewGuid();
        public string Name { get; init; } = "";
        public AssetKind Kind { get; init; }
        public string SourceFilePath { get; init; } = "";
        public bool IsDirty { get; init; }
        public bool IsEditorOwned { get; init; }
#pragma warning disable CS0067
        public event Action? Changed;
#pragma warning restore CS0067
    }

    // ── Pick → file kinds open document ─────────────────────────────────

    [Theory]
    [InlineData(AssetKind.Blueprint)]
    [InlineData(AssetKind.BTree)]
    [InlineData(AssetKind.Hsm)]
    public void Pick_FileAsset_OpensDocument(AssetKind kind)
    {
        IEditableAsset? openedAsset = null;
        string? loadedScenario = null;

        var router = new AssetPickActionRouter(
            openDocument: a => openedAsset = a,
            loadScenario: s => loadedScenario = s);

        var asset = new StubAsset { Kind = kind, Name = "test_asset" };

        router.Route(asset);

        Assert.NotNull(openedAsset);
        Assert.Same(asset, openedAsset);
        Assert.Null(loadedScenario);
    }

    /// <summary>
    /// Routing a Scenario asset whose Name is a relpath calls
    /// LoadScenarioByName with that relpath and does NOT call Open.
    /// </summary>
    [Fact]
    public void Pick_Scenario_CallsLoadScenarioByName_WithRelPath()
    {
        IEditableAsset? openedAsset = null;
        string? loadedScenario = null;

        var router = new AssetPickActionRouter(
            openDocument: a => openedAsset = a,
            loadScenario: s => loadedScenario = s);

        var asset = new StubAsset { Kind = AssetKind.Scenario, Name = "Combat/Patrol" };

        router.Route(asset);

        Assert.Equal("Combat/Patrol", loadedScenario);
        Assert.Null(openedAsset);
    }

    /// <summary>
    /// Other/unsupported asset kinds are silently ignored (no-op, no throw).
    /// </summary>
    [Fact]
    public void Pick_UnsupportedKind_IsNoOp()
    {
        IEditableAsset? openedAsset = null;
        string? loadedScenario = null;

        var router = new AssetPickActionRouter(
            openDocument: a => openedAsset = a,
            loadScenario: s => loadedScenario = s);

        var asset = new StubAsset { Kind = AssetKind.Blackboard, Name = "bb" };

        router.Route(asset);

        Assert.Null(openedAsset);
        Assert.Null(loadedScenario);
    }

    /// <summary>
    /// Routing a null asset throws ArgumentNullException.
    /// </summary>
    [Fact]
    public void Route_NullAsset_ThrowsArgumentNullException()
    {
        var router = new AssetPickActionRouter(_ => { }, _ => { });

        Assert.Throws<ArgumentNullException>(() => router.Route(null!));
    }

    /// <summary>
    /// Construction with null openDocument throws.
    /// </summary>
    [Fact]
    public void Ctor_NullOpenDocument_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(
            () => new AssetPickActionRouter(null!, _ => { }));
    }

    /// <summary>
    /// Construction with null loadScenario throws.
    /// </summary>
    [Fact]
    public void Ctor_NullLoadScenario_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(
            () => new AssetPickActionRouter(_ => { }, null!));
    }

    /// <summary>
    /// Scenario routing ignores the asset's SourceFilePath — uses Name only.
    /// </summary>
    [Fact]
    public void Pick_Scenario_UsesName_NotSourceFilePath()
    {
        string? loadedScenario = null;

        var router = new AssetPickActionRouter(
            openDocument: _ => { },
            loadScenario: s => loadedScenario = s);

        var asset = new StubAsset
        {
            Kind = AssetKind.Scenario,
            Name = "deep/nested/scenario",
            SourceFilePath = "/some/other/path/scenario.json",
        };

        router.Route(asset);

        Assert.Equal("deep/nested/scenario", loadedScenario);
    }
}
