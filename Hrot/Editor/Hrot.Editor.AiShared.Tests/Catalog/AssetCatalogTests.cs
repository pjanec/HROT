using System.Linq;
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
        /// <summary>A simple Position proxy for testing: non-zero = JSON (layout-bearing) instance.</summary>
        public float PositionX { get; init; }
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

    // ── BATCH-10 Bug #1b: dedup by AssetId, last-writer (JSON) wins ──────────

    /// <summary>
    /// Two contributors expose the SAME AssetId:
    /// assembly contributor first (layout-less, PositionX=0),
    /// JSON contributor second (layout-bearing, PositionX=42).
    /// All must contain exactly ONE entry for that id, and it must be the JSON instance.
    /// </summary>
    [Fact]
    public void All_Deduped_ByAssetId_LastWriterWins()
    {
        var sharedId = Guid.NewGuid();
        var assemblyAsset = new FakeAsset { AssetId = sharedId, Name = "SampleScout", PositionX = 0f };
        var jsonAsset     = new FakeAsset { AssetId = sharedId, Name = "SampleScout", PositionX = 42f };

        var catalog = new AssetCatalog();
        catalog.AddContributor(new FakeContributor(assemblyAsset)); // added first (assembly)
        catalog.AddContributor(new FakeContributor(jsonAsset));      // added second (JSON)

        // There must be exactly one entry for this id.
        var entries = catalog.All.Where(a => a.AssetId == sharedId).ToList();
        Assert.Single(entries);

        // That entry must be the JSON (last-writer) instance.
        var winner = entries[0];
        Assert.Equal(42f, ((FakeAsset)winner).PositionX);
    }

    [Fact]
    public void FindByAssetId_ReturnsJsonInstance_WhenDuplicate()
    {
        var sharedId = Guid.NewGuid();
        var assemblyAsset = new FakeAsset { AssetId = sharedId, Name = "SampleScout", PositionX = 0f };
        var jsonAsset     = new FakeAsset { AssetId = sharedId, Name = "SampleScout", PositionX = 42f };

        var catalog = new AssetCatalog();
        catalog.AddContributor(new FakeContributor(assemblyAsset));
        catalog.AddContributor(new FakeContributor(jsonAsset));

        var result = catalog.FindByAssetId(sharedId);
        Assert.NotNull(result);
        Assert.Equal(42f, ((FakeAsset)result!).PositionX);
    }

    [Fact]
    public void FindByName_ReturnsJsonInstance_WhenDuplicate()
    {
        var sharedId = Guid.NewGuid();
        var assemblyAsset = new FakeAsset { AssetId = sharedId, Name = "SampleScout", PositionX = 0f };
        var jsonAsset     = new FakeAsset { AssetId = sharedId, Name = "SampleScout", PositionX = 42f };

        var catalog = new AssetCatalog();
        catalog.AddContributor(new FakeContributor(assemblyAsset));
        catalog.AddContributor(new FakeContributor(jsonAsset));

        var result = catalog.FindByName("SampleScout");
        Assert.NotNull(result);
        Assert.Equal(42f, ((FakeAsset)result!).PositionX);
    }

    [Fact]
    public void All_TotalCount_IsDeduped_NotRaw()
    {
        // 2 contributors, each with the same asset id → All should have 1 entry, not 2.
        var sharedId = Guid.NewGuid();
        var a1 = new FakeAsset { AssetId = sharedId, Name = "Shared" };
        var a2 = new FakeAsset { AssetId = sharedId, Name = "Shared" };
        var unrelated = new FakeAsset { Name = "Other" };

        var catalog = new AssetCatalog();
        catalog.AddContributor(new FakeContributor(a1, unrelated));
        catalog.AddContributor(new FakeContributor(a2));

        // One deduplicated entry for the shared id + one for the unrelated asset = 2 total.
        Assert.Equal(2, catalog.All.Count);
    }
}
