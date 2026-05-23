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

    private sealed class FakeAssetCatalog : IAssetCatalog
    {
        public IReadOnlyList<IEditableAsset> All => Array.Empty<IEditableAsset>();
        public IEditableAsset? FindByAssetId(Guid assetId) => null;
        public IEditableAsset? FindByName(string name) => null;
        public IReadOnlyList<IEditableAsset> WhereDependsOn(Guid assetId) => Array.Empty<IEditableAsset>();
        public event Action? Changed;
        public void FireChanged() => Changed?.Invoke();
    }
}
