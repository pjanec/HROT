using Hrot.Editor.AiShared.Catalog;
using Hrot.Editor.AiShared.Refactor;
using Hrot.Editor.AiShared.References;

namespace Hrot.Editor.AiShared.Tests.Refactor;

// ---- Fakes ----

internal sealed class FakeReferenceCatalog : IReferenceCatalog
{
    private readonly List<IAssetSubElement> _elements = new();
    private readonly List<AssetReference> _references = new();

    public event Action? Changed { add { } remove { } }

    public IReadOnlyList<IAssetSubElement> AllElements => _elements;

    public IAssetSubElement? FindElement(string key) =>
        _elements.FirstOrDefault(e => e.Key == key);

    public IReadOnlyList<AssetReference> FindReferences(string targetKey) =>
        _references.Where(r => r.TargetKey == targetKey).ToList();

    public IReadOnlyList<AssetReference> AllReferencesIn(Guid hostAssetId) =>
        _references.Where(r => r.HostAssetId == hostAssetId).ToList();

    public void AddReference(AssetReference r) => _references.Add(r);
    public void AddElement(IAssetSubElement e) => _elements.Add(e);
}

internal sealed class FakeAssetCatalog : IAssetCatalog
{
    private readonly Dictionary<Guid, IEditableAsset> _assets = new();

    public event Action<AssetKind>? Changed { add { } remove { } }

    public IReadOnlyList<IEditableAsset> All => _assets.Values.ToList();

    public IEditableAsset? FindByAssetId(Guid assetId) =>
        _assets.GetValueOrDefault(assetId);

    public IEditableAsset? FindByName(string name) =>
        _assets.Values.FirstOrDefault(a => a.Name == name);

    public IReadOnlyList<IEditableAsset> WhereDependsOn(Guid assetId) =>
        Array.Empty<IEditableAsset>();

    public void AddAsset(IEditableAsset asset) => _assets[asset.AssetId] = asset;
}

internal sealed class FakeAsset : IEditableAsset
{
    public Guid AssetId { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = "FakeAsset";
    public AssetKind Kind { get; init; } = AssetKind.Blueprint;
    public string SourceFilePath { get; init; } = string.Empty;
    public bool IsDirty => false;
    public bool IsEditorOwned => false;

    public event Action? Changed { add { } remove { } }
}

internal sealed class FakeSubElement : IAssetSubElement
{
    public string Key { get; init; } = string.Empty;
    public SubElementKind Kind { get; init; } = SubElementKind.ActionFqn;
    public string DisplayName { get; init; } = string.Empty;
    public Guid? SourceAssetId { get; init; }
}

// ---- Tests ----

public sealed class RefactorServiceTests
{
    private static string GetTempFilePath() =>
        Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

    private static RefactorService MakeService(
        FakeReferenceCatalog? refCat = null,
        FakeAssetCatalog? assetCat = null,
        AtomicMultiFileWriter? writer = null) =>
        new RefactorService(
            refCat ?? new FakeReferenceCatalog(),
            assetCat ?? new FakeAssetCatalog(),
            writer ?? new AtomicMultiFileWriter());

    [Fact]
    public void FindReferences_returns_empty_when_no_references()
    {
        var svc = MakeService();
        var result = svc.FindReferences("action://Missing");
        Assert.Empty(result);
    }

    [Fact]
    public void FindReferences_returns_matching_references_with_source_path()
    {
        var refCat = new FakeReferenceCatalog();
        var assetCat = new FakeAssetCatalog();

        var assetId = Guid.NewGuid();
        var asset = new FakeAsset { AssetId = assetId, SourceFilePath = "/some/path.json" };
        assetCat.AddAsset(asset);

        refCat.AddReference(new AssetReference(
            assetId, AssetKind.Blueprint,
            Guid.NewGuid(), "path", "action://Foo", SubElementKind.ActionFqn));

        var svc = MakeService(refCat, assetCat);
        var result = svc.FindReferences("action://Foo");

        Assert.Single(result);
        Assert.Equal("/some/path.json", result[0].SourceFilePath);
    }

    [Fact]
    public void FindReferencesInAsset_returns_references_for_host_asset()
    {
        var refCat = new FakeReferenceCatalog();
        var assetCat = new FakeAssetCatalog();

        var assetId = Guid.NewGuid();
        var asset = new FakeAsset { AssetId = assetId, SourceFilePath = "/some/path.json" };
        assetCat.AddAsset(asset);

        refCat.AddReference(new AssetReference(
            assetId, AssetKind.Blueprint,
            Guid.NewGuid(), "path", "action://Foo", SubElementKind.ActionFqn));
        refCat.AddReference(new AssetReference(
            Guid.NewGuid(), AssetKind.Blueprint,
            Guid.NewGuid(), "path", "action://Bar", SubElementKind.ActionFqn));

        var svc = MakeService(refCat, assetCat);
        var result = svc.FindReferencesInAsset(assetId);

        Assert.Single(result);
        Assert.Equal(assetId, result[0].HostAssetId);
    }

    [Fact]
    public void PreviewRename_empty_catalog_returns_empty_edits()
    {
        var svc = MakeService();
        var preview = svc.PreviewRename("action://Foo", "action://Bar", new RefactorOptions());
        Assert.Empty(preview.Edits);
    }

    [Fact]
    public void PreviewRename_finds_key_in_source_file_and_creates_line_edit()
    {
        var refCat = new FakeReferenceCatalog();
        var assetCat = new FakeAssetCatalog();
        var path = GetTempFilePath();
        try
        {
            File.WriteAllText(path, "var x = \"action://Foo\";");

            var assetId = Guid.NewGuid();
            var asset = new FakeAsset { AssetId = assetId, SourceFilePath = path };
            assetCat.AddAsset(asset);
            refCat.AddReference(new AssetReference(
                assetId, AssetKind.Blueprint,
                Guid.NewGuid(), "path", "action://Foo", SubElementKind.ActionFqn));

            var svc = MakeService(refCat, assetCat);
            var preview = svc.PreviewRename("action://Foo", "action://Bar", new RefactorOptions());

            Assert.Single(preview.Edits);
            Assert.Single(preview.Edits[0].LineEdits);
            Assert.Contains("action://Bar", preview.Edits[0].LineEdits[0].ReplacementText);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void PreviewRename_skips_reference_when_source_file_missing()
    {
        var refCat = new FakeReferenceCatalog();
        var assetCat = new FakeAssetCatalog();

        var assetId = Guid.NewGuid();
        var asset = new FakeAsset { AssetId = assetId, SourceFilePath = "/nonexistent/file.json" };
        assetCat.AddAsset(asset);
        refCat.AddReference(new AssetReference(
            assetId, AssetKind.Blueprint,
            Guid.NewGuid(), "path", "action://Foo", SubElementKind.ActionFqn));

        var svc = MakeService(refCat, assetCat);
        var preview = svc.PreviewRename("action://Foo", "action://Bar", new RefactorOptions());

        Assert.Empty(preview.Edits);
        Assert.Single(preview.Issues);
        Assert.Equal(RefactorIssueSeverity.Warning, preview.Issues[0].Severity);
    }

    [Fact]
    public void PreviewRename_respects_IncludeBTree_false_option()
    {
        var refCat = new FakeReferenceCatalog();
        var assetCat = new FakeAssetCatalog();

        var assetId = Guid.NewGuid();
        var asset = new FakeAsset { AssetId = assetId, Kind = AssetKind.BTree, SourceFilePath = "/some.json" };
        assetCat.AddAsset(asset);
        refCat.AddReference(new AssetReference(
            assetId, AssetKind.BTree,
            Guid.NewGuid(), "path", "action://Foo", SubElementKind.ActionFqn));

        var options = new RefactorOptions(IncludeBTree: false);
        var svc = MakeService(refCat, assetCat);
        var preview = svc.PreviewRename("action://Foo", "action://Bar", options);

        Assert.Empty(preview.Edits);
        Assert.Empty(preview.Issues);
    }

    [Fact]
    public void ApplyRename_writes_modified_files()
    {
        var refCat = new FakeReferenceCatalog();
        var assetCat = new FakeAssetCatalog();
        var path = GetTempFilePath();
        try
        {
            File.WriteAllText(path, "var x = \"action://Foo\";");

            var assetId = Guid.NewGuid();
            var asset = new FakeAsset { AssetId = assetId, SourceFilePath = path };
            assetCat.AddAsset(asset);
            refCat.AddReference(new AssetReference(
                assetId, AssetKind.Blueprint,
                Guid.NewGuid(), "path", "action://Foo", SubElementKind.ActionFqn));

            var svc = MakeService(refCat, assetCat);
            var preview = svc.PreviewRename("action://Foo", "action://Bar", new RefactorOptions());
            var result = svc.ApplyRename(preview);

            Assert.True(result.Success);
            var content = File.ReadAllText(path);
            Assert.Contains("action://Bar", content);
            Assert.DoesNotContain("action://Foo", content);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void ApplyRename_returns_failure_when_file_write_fails()
    {
        // Preview references a file that does not exist, causing read to fail.
        var lineEdit = new RefactorLineEdit(1, "old", "new", "desc");
        var hostId = Guid.NewGuid();
        var fileEdit = new RefactorFileEdit(
            Path.Combine(Path.GetTempPath(),
                "nonexistent_batch33_" + Guid.NewGuid().ToString("N"), "file.txt"),
            hostId,
            new[] { lineEdit });
        var preview = new RefactorPreview(
            "old", "new",
            new[] { fileEdit },
            Array.Empty<RefactorIssue>());

        var svc = MakeService();
        var result = svc.ApplyRename(preview);

        Assert.False(result.Success);
    }

    [Fact]
    public void PreviewDelete_returns_dangling_references()
    {
        var refCat = new FakeReferenceCatalog();
        var assetCat = new FakeAssetCatalog();

        var assetId = Guid.NewGuid();
        var asset = new FakeAsset { AssetId = assetId };
        assetCat.AddAsset(asset);

        var element = new FakeSubElement
        {
            Key = "action://ToDelete",
            SourceAssetId = assetId,
        };
        refCat.AddElement(element);

        var refHostId = Guid.NewGuid();
        var refHost = new FakeAsset { AssetId = refHostId };
        assetCat.AddAsset(refHost);
        refCat.AddReference(new AssetReference(
            refHostId, AssetKind.Blueprint,
            Guid.NewGuid(), "path", "action://ToDelete", SubElementKind.ActionFqn));

        var svc = MakeService(refCat, assetCat);
        var deletePreview = svc.PreviewDelete(assetId, new DeleteOptions());

        Assert.Single(deletePreview.DanglingReferences);
        Assert.Equal("action://ToDelete", deletePreview.DanglingReferences[0].TargetKey);
    }
}
