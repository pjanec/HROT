using Hrot.Editor.AiShared.Catalog;

namespace Hrot.Editor.AiShared.Tests.Catalog;

public sealed class AssetCatalogTests
{
    private sealed class FakeAsset : IEditableAsset
    {
        public Guid AssetId { get; init; } = Guid.NewGuid();
        public string Name { get; init; } = "Asset";
        public AssetKind Kind { get; init; } = AssetKind.Blueprint;
        public string SourceFilePath { get; init; } = "/test.cs";
        public bool IsDirty { get; init; }
        public bool IsEditorOwned { get; init; }
#pragma warning disable 67
        public event Action? Changed;
#pragma warning restore 67
    }

    private sealed class FakeContributor : IAssetCatalogContributor
    {
        private readonly List<IEditableAsset> _assets;

        public FakeContributor(params IEditableAsset[] assets)
        {
            _assets = new List<IEditableAsset>(assets);
        }

        public AssetKind Kind => AssetKind.Blueprint;
        public IReadOnlyList<IEditableAsset> Enumerate() => _assets;
        public event Action? ContributorChanged;

        public void AddAsset(IEditableAsset asset)
        {
            _assets.Add(asset);
            ContributorChanged?.Invoke();
        }
    }

    [Fact]
    public void All_Empty_WhenNoContributors()
    {
        var catalog = new AssetCatalog();
        Assert.Empty(catalog.All);
    }

    [Fact]
    public void All_ReturnsAssets_AfterContributorAdded()
    {
        var catalog = new AssetCatalog();
        var asset = new FakeAsset();
        catalog.AddContributor(new FakeContributor(asset));
        Assert.Contains(asset, catalog.All);
    }

    [Fact]
    public void All_MergesContributors_WhenContributorsRegistered()
    {
        var catalog = new AssetCatalog();
        var a1 = new FakeAsset { Name = "A" };
        var a2 = new FakeAsset { Name = "B" };
        catalog.AddContributor(new FakeContributor(a1));
        catalog.AddContributor(new FakeContributor(a2));
        Assert.Equal(2, catalog.All.Count);
    }

    [Fact]
    public void FindByAssetId_ReturnsCorrectAsset()
    {
        var catalog = new AssetCatalog();
        var asset = new FakeAsset();
        catalog.AddContributor(new FakeContributor(asset));
        Assert.Equal(asset, catalog.FindByAssetId(asset.AssetId));
    }

    [Fact]
    public void FindByAssetId_ReturnsNull_WhenNotFound()
    {
        var catalog = new AssetCatalog();
        Assert.Null(catalog.FindByAssetId(Guid.NewGuid()));
    }

    [Fact]
    public void FindByName_ReturnsCorrectAsset()
    {
        var catalog = new AssetCatalog();
        var asset = new FakeAsset { Name = "MyAsset" };
        catalog.AddContributor(new FakeContributor(asset));
        Assert.Equal(asset, catalog.FindByName("MyAsset"));
    }

    [Fact]
    public void FindByName_CaseSensitive_ReturnsNull()
    {
        var catalog = new AssetCatalog();
        catalog.AddContributor(new FakeContributor(new FakeAsset { Name = "MyAsset" }));
        Assert.Null(catalog.FindByName("myasset"));
    }

    [Fact]
    public void WhereDependsOn_ReturnsEmpty()
    {
        var catalog = new AssetCatalog();
        Assert.Empty(catalog.WhereDependsOn(Guid.NewGuid()));
    }

    [Fact]
    public void Changed_FiresOnce_WhenContributorChanges()
    {
        var catalog = new AssetCatalog();
        var contributor = new FakeContributor();
        catalog.AddContributor(contributor);

        int count = 0;
        catalog.Changed += () => count++;

        contributor.AddAsset(new FakeAsset());
        Assert.Equal(1, count);
    }

    [Fact]
    public void All_Refreshes_WhenContributorChanges()
    {
        var catalog = new AssetCatalog();
        var contributor = new FakeContributor();
        catalog.AddContributor(contributor);

        var asset = new FakeAsset();
        contributor.AddAsset(asset);

        Assert.Contains(asset, catalog.All);
    }
}
