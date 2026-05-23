using Hrot.Editor.AiShared.Catalog;
using Hrot.Editor.AiShared.Refactor;
using Hrot.Editor.AiShared.References;

namespace Hrot.Editor.AiShared.Tests.Refactor;

public sealed class RefactorEndToEndTests
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
    public void RenameAction_across_multiple_files_updates_all_files()
    {
        var refCat = new FakeReferenceCatalog();
        var assetCat = new FakeAssetCatalog();

        var paths = new[] { GetTempFilePath(), GetTempFilePath(), GetTempFilePath() };
        try
        {
            foreach (var p in paths)
                File.WriteAllText(p, "ref: \"action://OldName\"");

            for (int i = 0; i < paths.Length; i++)
            {
                var assetId = Guid.NewGuid();
                var asset = new FakeAsset { AssetId = assetId, SourceFilePath = paths[i] };
                assetCat.AddAsset(asset);
                refCat.AddReference(new AssetReference(
                    assetId, AssetKind.Blueprint,
                    Guid.NewGuid(), "path", "action://OldName", SubElementKind.ActionFqn));
            }

            var svc = MakeService(refCat, assetCat);
            var preview = svc.PreviewRename("action://OldName", "action://NewName", new RefactorOptions());
            svc.ApplyRename(preview);

            foreach (var p in paths)
            {
                var content = File.ReadAllText(p);
                Assert.Contains("action://NewName", content);
                Assert.DoesNotContain("action://OldName", content);
            }
        }
        finally
        {
            foreach (var p in paths)
                if (File.Exists(p)) File.Delete(p);
        }
    }

    [Fact]
    public void RenameAction_leaves_non_matching_lines_unchanged()
    {
        var refCat = new FakeReferenceCatalog();
        var assetCat = new FakeAssetCatalog();
        var path = GetTempFilePath();
        try
        {
            File.WriteAllLines(path, new[]
            {
                "ref: \"action://OldName\"",
                "ref: \"action://OtherAction\""
            });

            var assetId = Guid.NewGuid();
            var asset = new FakeAsset { AssetId = assetId, SourceFilePath = path };
            assetCat.AddAsset(asset);
            refCat.AddReference(new AssetReference(
                assetId, AssetKind.Blueprint,
                Guid.NewGuid(), "path", "action://OldName", SubElementKind.ActionFqn));

            var svc = MakeService(refCat, assetCat);
            var preview = svc.PreviewRename("action://OldName", "action://NewName", new RefactorOptions());
            svc.ApplyRename(preview);

            var lines = File.ReadAllLines(path);
            Assert.Contains("action://NewName", lines[0]);
            Assert.Contains("action://OtherAction", lines[1]);
            Assert.DoesNotContain("action://OldName", lines[0]);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void PreviewDelete_with_dangling_refs_across_two_assets_reports_all_refs()
    {
        var refCat = new FakeReferenceCatalog();
        var assetCat = new FakeAssetCatalog();

        var targetAssetId = Guid.NewGuid();
        var targetAsset = new FakeAsset
        {
            AssetId = targetAssetId,
            Name = "TargetAsset",
            SourceFilePath = GetTempFilePath()
        };
        assetCat.AddAsset(targetAsset);

        var subElem = new FakeSubElement
        {
            Key = "action://ToDel",
            Kind = SubElementKind.ActionFqn,
            SourceAssetId = targetAssetId
        };
        refCat.AddElement(subElem);

        for (int i = 0; i < 2; i++)
        {
            var refAssetId = Guid.NewGuid();
            var refAsset = new FakeAsset
            {
                AssetId = refAssetId,
                SourceFilePath = GetTempFilePath()
            };
            assetCat.AddAsset(refAsset);
            refCat.AddReference(new AssetReference(
                refAssetId, AssetKind.Blueprint,
                Guid.NewGuid(), $"path{i}", "action://ToDel", SubElementKind.ActionFqn));
        }

        var svc = MakeService(refCat, assetCat);
        var deletePreview = svc.PreviewDelete(targetAssetId, new DeleteOptions());

        Assert.Equal(2, deletePreview.DanglingReferences.Count);
    }

    [Fact]
    public async Task PreviewRenameAsync_returns_same_result_as_sync()
    {
        var refCat = new FakeReferenceCatalog();
        var assetCat = new FakeAssetCatalog();
        var path = GetTempFilePath();
        try
        {
            File.WriteAllText(path, "ref: \"action://Foo\"");

            var assetId = Guid.NewGuid();
            var asset = new FakeAsset { AssetId = assetId, SourceFilePath = path };
            assetCat.AddAsset(asset);
            refCat.AddReference(new AssetReference(
                assetId, AssetKind.Blueprint,
                Guid.NewGuid(), "path", "action://Foo", SubElementKind.ActionFqn));

            var svc = MakeService(refCat, assetCat);
            var syncPreview = svc.PreviewRename("action://Foo", "action://Bar", new RefactorOptions());
            var asyncPreview = await svc.PreviewRenameAsync(
                "action://Foo", "action://Bar", new RefactorOptions());

            Assert.Equal(syncPreview.Edits.Count, asyncPreview.Edits.Count);
            Assert.Equal(syncPreview.FromKey, asyncPreview.FromKey);
            Assert.Equal(syncPreview.ToKey, asyncPreview.ToKey);
            Assert.Equal(
                syncPreview.Edits[0].LineEdits[0].ReplacementText,
                asyncPreview.Edits[0].LineEdits[0].ReplacementText);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
