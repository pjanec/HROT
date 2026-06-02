using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Catalog;
using Hrot.Editor.AiShared.Refactor;
using Hrot.Editor.AiShared.References;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Refactor;

/// <summary>
/// BATCH-14 / AIE-051: <see cref="RefactorService.ApplyRename"/> must write all matching
/// files atomically (via <see cref="AtomicMultiFileWriter"/>): assert the written file set
/// and content.
/// </summary>
public sealed class Batch14RefactorTests
{
    private static string TempFile() =>
        Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

    [Fact]
    public void RefactorService_Rename_WritesAtomically_ExactFileSetAndContent()
    {
        // Arrange: three files all referencing the same target key.
        var paths = new[] { TempFile(), TempFile(), TempFile() };
        try
        {
            // Give each file a slightly different line structure so we can verify per-file.
            File.WriteAllText(paths[0], "line A: \"action://OldKey\"");
            File.WriteAllText(paths[1], "line B:\n  ref: \"action://OldKey\"\n  other: ok");
            File.WriteAllText(paths[2], "line C: \"action://OldKey\" end");

            var refCat   = new FakeReferenceCatalog();
            var assetCat = new FakeAssetCatalog();

            for (int i = 0; i < paths.Length; i++)
            {
                var assetId = Guid.NewGuid();
                assetCat.AddAsset(new FakeAsset { AssetId = assetId, SourceFilePath = paths[i] });
                refCat.AddReference(new AssetReference(
                    assetId, AssetKind.BTree,
                    Guid.NewGuid(), $"node{i}", "action://OldKey", SubElementKind.ActionFqn));
            }

            var writer  = new AtomicMultiFileWriter();
            var svc     = new RefactorService(refCat, assetCat, writer);
            var preview = svc.PreviewRename("action://OldKey", "action://NewKey", new RefactorOptions());

            // Act
            var result = svc.ApplyRename(preview);

            // Assert — operation succeeded.
            Assert.True(result.Success, result.FailureReason ?? "ApplyRename failed");

            // Assert — exact written-file set: all three paths, each containing the new key.
            var writtenPaths = preview.Edits.Select(e => e.FilePath).OrderBy(p => p).ToArray();
            var expectedPaths = paths.OrderBy(p => p).ToArray();
            Assert.Equal(expectedPaths, writtenPaths);

            // Assert — each file content has the new key and NOT the old key.
            foreach (var path in paths)
            {
                var content = File.ReadAllText(path);
                Assert.Contains("action://NewKey", content);
                Assert.DoesNotContain("action://OldKey", content);
            }
        }
        finally
        {
            foreach (var p in paths)
                if (File.Exists(p)) File.Delete(p);
        }
    }

    [Fact]
    public void RefactorService_Rename_PartialMatch_OnlyWritesMatchingFiles()
    {
        // Arrange: two files. Only one references the target key.
        var targetPath  = TempFile();
        var ignoredPath = TempFile();
        try
        {
            File.WriteAllText(targetPath,  "contains: \"action://Target\"");
            File.WriteAllText(ignoredPath, "contains: \"action://Other\"");

            var refCat   = new FakeReferenceCatalog();
            var assetCat = new FakeAssetCatalog();

            var targetId  = Guid.NewGuid();
            var ignoredId = Guid.NewGuid();
            assetCat.AddAsset(new FakeAsset { AssetId = targetId,  SourceFilePath = targetPath });
            assetCat.AddAsset(new FakeAsset { AssetId = ignoredId, SourceFilePath = ignoredPath });
            refCat.AddReference(new AssetReference(
                targetId, AssetKind.BTree,
                Guid.NewGuid(), "node0", "action://Target", SubElementKind.ActionFqn));
            // ignoredId has no reference to action://Target

            var svc     = new RefactorService(refCat, assetCat, new AtomicMultiFileWriter());
            var preview = svc.PreviewRename("action://Target", "action://Renamed", new RefactorOptions());
            var result  = svc.ApplyRename(preview);

            Assert.True(result.Success);

            // Written files: only targetPath.
            Assert.Single(preview.Edits);
            Assert.Equal(targetPath, preview.Edits[0].FilePath);

            // targetPath rewritten, ignoredPath unchanged.
            Assert.Contains("action://Renamed", File.ReadAllText(targetPath));
            Assert.DoesNotContain("action://Target", File.ReadAllText(targetPath));
            Assert.Contains("action://Other", File.ReadAllText(ignoredPath)); // untouched
        }
        finally
        {
            if (File.Exists(targetPath))  File.Delete(targetPath);
            if (File.Exists(ignoredPath)) File.Delete(ignoredPath);
        }
    }
}
