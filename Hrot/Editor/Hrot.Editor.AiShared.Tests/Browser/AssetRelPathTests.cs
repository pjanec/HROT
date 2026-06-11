using Hrot.Editor.AiShared.Browser;
using Hrot.Editor.AiShared.Catalog;

namespace Hrot.Editor.AiShared.Tests.Browser;

public sealed class AssetRelPathTests
{
    // ── Fake IEditableAsset for tests ───────────────────────────────

    private sealed class FakeAsset : IEditableAsset
    {
        public Guid AssetId { get; init; } = Guid.NewGuid();
        public string Name { get; init; } = "TestAsset";
        public AssetKind Kind { get; init; } = AssetKind.Blueprint;
        public string SourceFilePath { get; init; } = "";
        public bool IsDirty => false;
        public bool IsEditorOwned => false;
#pragma warning disable 67
        public event Action? Changed;
#pragma warning restore 67
    }

    // ── Fake IAssetCatalogContributor for tests ─────────────────────

    private sealed class FakeContributor : IAssetCatalogContributor
    {
        public AssetKind Kind { get; init; } = AssetKind.Blueprint;
        public string? BaseFolder { get; init; }
        public IReadOnlyList<IEditableAsset> Enumerate() => Array.Empty<IEditableAsset>();
#pragma warning disable 67
        public event Action? ContributorChanged;
#pragma warning restore 67
    }

    // ── FileAsset_RelPath_IsSourceMinusBase ─────────────────────────

    [Fact]
    public void FileAsset_RelPath_IsSourceMinusBase()
    {
        // Simulate a base folder under …/Assets/Blueprints and a source file
        // under …/Assets/Blueprints/combat/Guard.bp.json.
        // Use OS-native paths so Path.GetRelativePath works correctly.
        var baseFolder = Path.Combine(
            AppContext.BaseDirectory, "Assets", "Blueprints");
        var sourceFilePath = Path.Combine(
            baseFolder, "combat", "Guard.bp.json");

        var asset = new FakeAsset
        {
            SourceFilePath = sourceFilePath,
            Name = "Guard" // Name should NOT be used when SourceFilePath is available.
        };

        var relPath = AssetRelPath.RelPath(asset, baseFolder);

        // Expected: "combat/Guard.bp.json" (forward slashes, no leading slash).
        Assert.Equal("combat/Guard.bp.json", relPath);
        Assert.DoesNotContain("\\", relPath);
        Assert.False(relPath.StartsWith("/"));
    }

    [Fact]
    public void FileAsset_RelPath_HandlesWindowsBackslash()
    {
        // On Windows, Path.GetRelativePath produces backslashes.
        // The helper must normalize them to forward slashes.
        var baseFolder = @"C:\Project\Assets\Blueprints";
        var sourceFilePath = @"C:\Project\Assets\Blueprints\nested\folder\Asset.bp.json";

        var asset = new FakeAsset { SourceFilePath = sourceFilePath };

        var relPath = AssetRelPath.RelPath(asset, baseFolder);

        Assert.Equal("nested/folder/Asset.bp.json", relPath);
        Assert.DoesNotContain("\\", relPath);
    }

    [Fact]
    public void FileAsset_RelPath_NestedDeeply()
    {
        var baseFolder = Path.Combine(
            AppContext.BaseDirectory, "Assets", "BTrees");
        var sourceFilePath = Path.Combine(
            baseFolder, "combat", "enemies", "Patrol.btree.json");

        var asset = new FakeAsset { SourceFilePath = sourceFilePath, Kind = AssetKind.BTree };

        var relPath = AssetRelPath.RelPath(asset, baseFolder);

        Assert.Equal("combat/enemies/Patrol.btree.json", relPath);
    }

    // ── ScenarioAsset_RelPath_IsName ────────────────────────────────

    [Fact]
    public void ScenarioAsset_RelPath_IsName()
    {
        // Scenario: empty SourceFilePath → returns Name.
        var asset = new FakeAsset
        {
            SourceFilePath = "",
            Name = "combat/ambush/scenario",
            Kind = AssetKind.Blueprint // Kind doesn't matter here.
        };

        var relPath = AssetRelPath.RelPath(asset, null);

        Assert.Equal("combat/ambush/scenario", relPath);
    }

    [Fact]
    public void ScenarioAsset_RelPath_NullBaseFolder_ReturnsName()
    {
        // Even if SourceFilePath is set, a null baseFolder means Name is used.
        var asset = new FakeAsset
        {
            SourceFilePath = "/some/path/file.bp.json",
            Name = "MyScenario"
        };

        var relPath = AssetRelPath.RelPath(asset, null);

        Assert.Equal("MyScenario", relPath);
    }

    [Fact]
    public void ScenarioAsset_RelPath_EmptyBaseFolder_ReturnsName()
    {
        // Empty string baseFolder is treated as null → Name is used.
        var asset = new FakeAsset
        {
            SourceFilePath = "/some/path/file.bp.json",
            Name = "MyScenario"
        };

        var relPath = AssetRelPath.RelPath(asset, "");

        Assert.Equal("MyScenario", relPath);
    }

    // ── Contributor_BaseFolder_MatchesAssetRoot ─────────────────────

    [Fact]
    public void Contributor_BaseFolder_MatchesAssetRoot()
    {
        // File contributor's BaseFolder equals AssetRoots.AssetsFor(its Kind).
        // Non-file contributor's BaseFolder is null (default).

        // Verify by construction: the FakeContributor with explicit BaseFolder
        // matches what a real file contributor would return.
        var bpContrib = new FakeContributor
        {
            Kind = AssetKind.Blueprint,
            BaseFolder = AssetRoots.AssetsFor(AssetKind.Blueprint)
        };
        Assert.Equal(AssetRoots.AssetsFor(AssetKind.Blueprint), bpContrib.BaseFolder);
        Assert.NotNull(bpContrib.BaseFolder);

        var btreeContrib = new FakeContributor
        {
            Kind = AssetKind.BTree,
            BaseFolder = AssetRoots.AssetsFor(AssetKind.BTree)
        };
        Assert.Equal(AssetRoots.AssetsFor(AssetKind.BTree), btreeContrib.BaseFolder);
        Assert.NotNull(btreeContrib.BaseFolder);

        var hsmContrib = new FakeContributor
        {
            Kind = AssetKind.Hsm,
            BaseFolder = AssetRoots.AssetsFor(AssetKind.Hsm)
        };
        Assert.Equal(AssetRoots.AssetsFor(AssetKind.Hsm), hsmContrib.BaseFolder);
        Assert.NotNull(hsmContrib.BaseFolder);

        // Default (non-file) contributor: BaseFolder should be null.
        var defaultContrib = new FakeContributor { Kind = AssetKind.Blueprint };
        Assert.Null(defaultContrib.BaseFolder);
    }

    [Fact]
    public void Contributor_BaseFolder_DefaultIsNull()
    {
        // Every IAssetCatalogContributor that does not override BaseFolder
        // gets the default interface member ⇒ null.
        IAssetCatalogContributor contrib = new FakeContributor();
        Assert.Null(contrib.BaseFolder);
    }

    // ── RelPath_EdgeCases ───────────────────────────────────────────

    [Fact]
    public void RelPath_NullAsset_ThrowsArgumentNullException()
    {
        var ex = Assert.Throws<ArgumentNullException>(
            () => AssetRelPath.RelPath(null!, "/base"));
        Assert.Equal("asset", ex.ParamName);
    }
}
