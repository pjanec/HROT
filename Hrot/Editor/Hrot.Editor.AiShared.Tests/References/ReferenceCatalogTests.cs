using Hrot.Editor.AiShared.Catalog;
using Hrot.Editor.AiShared.References;

namespace Hrot.Editor.AiShared.Tests.References;

public sealed class ReferenceCatalogTests
{
    private sealed class FakeElement : IAssetSubElement
    {
        public string Key { get; init; } = "action://Default";
        public SubElementKind Kind { get; init; } = SubElementKind.ActionFqn;
        public string DisplayName { get; init; } = "Default";
        public Guid? SourceAssetId { get; init; }
    }

    private AssetReference MakeRef(Guid hostId, string targetKey) => new AssetReference(
        hostId,
        AssetKind.Blueprint,
        Guid.NewGuid(),
        "SomePath",
        targetKey,
        SubElementKind.ActionFqn);

    [Fact]
    public void AllElements_Empty_Initially()
    {
        var catalog = new ReferenceCatalog();
        Assert.Empty(catalog.AllElements);
    }

    [Fact]
    public void Contribute_AddsElement()
    {
        var catalog = new ReferenceCatalog();
        var elem = new FakeElement { Key = "action://Foo" };
        catalog.Contribute(elem, Array.Empty<AssetReference>());
        Assert.Contains(elem, catalog.AllElements);
    }

    [Fact]
    public void FindElement_ReturnsElement_WhenPresent()
    {
        var catalog = new ReferenceCatalog();
        var elem = new FakeElement { Key = "action://Foo" };
        catalog.Contribute(elem, Array.Empty<AssetReference>());
        Assert.Equal(elem, catalog.FindElement("action://Foo"));
    }

    [Fact]
    public void FindElement_ReturnsNull_WhenAbsent()
    {
        var catalog = new ReferenceCatalog();
        Assert.Null(catalog.FindElement("action://Missing"));
    }

    [Fact]
    public void FindReferences_ReturnsMatchingRefs()
    {
        var catalog = new ReferenceCatalog();
        var hostId = Guid.NewGuid();
        var elem = new FakeElement { Key = "action://Foo" };
        var refs = new[] { MakeRef(hostId, "action://Foo") };
        catalog.Contribute(elem, refs);

        var result = catalog.FindReferences("action://Foo");
        Assert.Single(result);
        Assert.Equal(hostId, result[0].HostAssetId);
    }

    [Fact]
    public void FindReferences_Empty_WhenNoMatch()
    {
        var catalog = new ReferenceCatalog();
        Assert.Empty(catalog.FindReferences("action://Missing"));
    }

    [Fact]
    public void AllReferencesIn_FiltersbyHostAssetId()
    {
        var catalog = new ReferenceCatalog();
        var hostA = Guid.NewGuid();
        var hostB = Guid.NewGuid();
        var elemA = new FakeElement { Key = "action://A" };
        var elemB = new FakeElement { Key = "action://B" };
        catalog.Contribute(elemA, new[] { MakeRef(hostA, "action://A") });
        catalog.Contribute(elemB, new[] { MakeRef(hostB, "action://B") });

        var result = catalog.AllReferencesIn(hostA);
        Assert.Single(result);
        Assert.Equal(hostA, result[0].HostAssetId);
    }

    [Fact]
    public void Changed_Fires_WhenContribute_Called()
    {
        var catalog = new ReferenceCatalog();
        int count = 0;
        catalog.Changed += () => count++;
        catalog.Contribute(new FakeElement(), Array.Empty<AssetReference>());
        Assert.Equal(1, count);
    }

    [Fact]
    public void Contribute_OverwritesElement_WhenKeyReused()
    {
        var catalog = new ReferenceCatalog();
        var elem1 = new FakeElement { Key = "action://Foo", DisplayName = "First" };
        var elem2 = new FakeElement { Key = "action://Foo", DisplayName = "Second" };
        catalog.Contribute(elem1, Array.Empty<AssetReference>());
        catalog.Contribute(elem2, Array.Empty<AssetReference>());

        var found = catalog.FindElement("action://Foo");
        Assert.Equal("Second", found?.DisplayName);
    }

    [Fact]
    public void Changed_Fires_WhenCatalogChanges()
    {
        var fakeCatalog = new FakeAssetCatalog();
        var catalog = new ReferenceCatalog(fakeCatalog);
        int count = 0;
        catalog.Changed += () => count++;

        fakeCatalog.FireChanged();
        Assert.Equal(1, count);
    }

    [Fact]
    public void OnCatalogChanged_RebuildsFromContributors()
    {
        var fakeAsset = new FakeEditableAsset();
        var fakeCatalog = new FakeAssetCatalog(fakeAsset);
        var elem1 = new FakeElement { Key = "action://Foo", DisplayName = "Foo" };
        var elem2 = new FakeElement { Key = "action://Bar", DisplayName = "Bar" };
        var hostId = Guid.NewGuid();
        var contributor = new FakeContributor(
            new[] { elem1, elem2 },
            new[] { MakeRef(hostId, "action://Foo") });

        var catalog = new ReferenceCatalog(fakeCatalog, new[] { contributor });

        fakeCatalog.FireChanged();

        Assert.Equal(2, catalog.AllElements.Count);
        Assert.Single(catalog.FindReferences("action://Foo"));
    }

    [Fact]
    public void OnCatalogChanged_ClearsElements_WhenCatalogEmpty()
    {
        var fakeAsset = new FakeEditableAsset();
        var fakeCatalog = new FakeAssetCatalog(fakeAsset);
        var elem = new FakeElement { Key = "action://Foo" };
        var contributor = new FakeContributor(new[] { elem }, Array.Empty<AssetReference>());

        var catalog = new ReferenceCatalog(fakeCatalog, new[] { contributor });
        fakeCatalog.FireChanged();
        Assert.Single(catalog.AllElements);

        // Remove all assets from catalog and fire again.
        fakeCatalog.ClearAssets();
        fakeCatalog.FireChanged();

        Assert.Empty(catalog.AllElements);
    }

    [Fact]
    public void ScenarioChange_DoesNotRebuild_References()
    {
        var fakeAsset = new FakeEditableAsset();
        var fakeCatalog = new FakeAssetCatalog(fakeAsset);
        var elem = new FakeElement { Key = "action://Foo" };
        var hostId = Guid.NewGuid();
        var recordingContributor = new RecordingContributor(
            new[] { elem },
            new[] { MakeRef(hostId, "action://Foo") });

        var catalog = new ReferenceCatalog(fakeCatalog, new[] { recordingContributor });

        // Populate with a non-scenario change first.
        fakeCatalog.FireChanged(AssetKind.Blueprint);
        Assert.Single(catalog.AllElements);
        Assert.Equal(1, recordingContributor.EnumerateElementsCallCount);
        Assert.Equal(1, recordingContributor.EnumerateReferencesCallCount);

        // Fire a Scenario change — should be ignored.
        int changedCount = 0;
        catalog.Changed += () => changedCount++;

        fakeCatalog.FireChanged(AssetKind.Scenario);

        // Elements must still be present (unchanged).
        Assert.Single(catalog.AllElements);
        // ReferenceCatalog.Changed must NOT have fired.
        Assert.Equal(0, changedCount);
        // Contributor walk must NOT have happened for the Scenario event.
        Assert.Equal(1, recordingContributor.EnumerateElementsCallCount);
        Assert.Equal(1, recordingContributor.EnumerateReferencesCallCount);
    }

    [Fact]
    public void NonScenarioChange_Rebuilds()
    {
        var fakeAsset = new FakeEditableAsset();
        var fakeCatalog = new FakeAssetCatalog(fakeAsset);
        var elem = new FakeElement { Key = "action://Foo" };
        var hostId = Guid.NewGuid();
        var contributor = new FakeContributor(
            new[] { elem },
            new[] { MakeRef(hostId, "action://Foo") });

        var catalog = new ReferenceCatalog(fakeCatalog, new IReferenceCatalogContributor[] { contributor });

        int changedCount = 0;
        catalog.Changed += () => changedCount++;

        fakeCatalog.FireChanged(AssetKind.Blueprint);

        Assert.Single(catalog.AllElements);
        Assert.Single(catalog.FindReferences("action://Foo"));
        Assert.Equal(1, changedCount);
    }

    private sealed class FakeEditableAsset : IEditableAsset
    {
        public Guid AssetId { get; } = Guid.NewGuid();
        public string Name => "FakeAsset";
        public AssetKind Kind => AssetKind.Blueprint;
        public string SourceFilePath => string.Empty;
        public bool IsDirty => false;
        public bool IsEditorOwned => true;
#pragma warning disable CS0067
        public event Action? Changed;
#pragma warning restore CS0067
    }

    private sealed class FakeContributor : IReferenceCatalogContributor
    {
        private readonly IReadOnlyList<IAssetSubElement> _elements;
        private readonly IReadOnlyList<AssetReference> _refs;

        public FakeContributor(IReadOnlyList<IAssetSubElement> elements, IReadOnlyList<AssetReference> refs)
        {
            _elements = elements;
            _refs = refs;
        }

        public IReadOnlyList<IAssetSubElement> EnumerateElements(IEditableAsset asset) => _elements;
        public IReadOnlyList<AssetReference> EnumerateReferences(IEditableAsset asset) => _refs;
    }

    private sealed class RecordingContributor : IReferenceCatalogContributor
    {
        private readonly IReadOnlyList<IAssetSubElement> _elements;
        private readonly IReadOnlyList<AssetReference> _refs;

        public int EnumerateElementsCallCount { get; private set; }
        public int EnumerateReferencesCallCount { get; private set; }

        public RecordingContributor(
            IReadOnlyList<IAssetSubElement> elements,
            IReadOnlyList<AssetReference> refs)
        {
            _elements = elements;
            _refs = refs;
        }

        public IReadOnlyList<IAssetSubElement> EnumerateElements(IEditableAsset asset)
        {
            EnumerateElementsCallCount++;
            return _elements;
        }

        public IReadOnlyList<AssetReference> EnumerateReferences(IEditableAsset asset)
        {
            EnumerateReferencesCallCount++;
            return _refs;
        }
    }

    private sealed class FakeAssetCatalog : IAssetCatalog
    {
        private readonly List<IEditableAsset> _assets;

        public FakeAssetCatalog(params IEditableAsset[] assets)
            => _assets = new List<IEditableAsset>(assets);

        public IReadOnlyList<IEditableAsset> All => _assets;
        public IEditableAsset? FindByAssetId(Guid assetId) => null;
        public IEditableAsset? FindByName(string name) => null;
        public IReadOnlyList<IEditableAsset> WhereDependsOn(Guid assetId) => Array.Empty<IEditableAsset>();
        public event Action<AssetKind>? Changed;
        public void FireChanged(AssetKind kind = AssetKind.Blueprint) => Changed?.Invoke(kind);
        public void ClearAssets() => _assets.Clear();
    }
}
