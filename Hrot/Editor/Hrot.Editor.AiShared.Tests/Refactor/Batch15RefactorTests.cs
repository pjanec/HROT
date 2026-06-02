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
/// BATCH-15 / AIE-053: Dangling-reference classification and <see cref="RefactorService.ApplyDelete"/>
/// refusal when Critical references exist.
/// </summary>
public sealed class Batch15RefactorTests
{
    private static string TempFile() =>
        Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

    private static RefactorService MakeService(
        FakeReferenceCatalog? refCat = null,
        FakeAssetCatalog? assetCat = null) =>
        new RefactorService(
            refCat  ?? new FakeReferenceCatalog(),
            assetCat ?? new FakeAssetCatalog(),
            new AtomicMultiFileWriter());

    // Helper: add an asset and a sub-element that points to it, then add a reference FROM
    // some other asset TO that sub-element key, with a given SubElementKind.
    private static (Guid assetToDelete, AssetReference incomingRef) BuildScenario(
        FakeReferenceCatalog refCat,
        FakeAssetCatalog assetCat,
        SubElementKind targetKind,
        string? tempPath = null)
    {
        var deleteId = Guid.NewGuid();
        assetCat.AddAsset(new FakeAsset
        {
            AssetId        = deleteId,
            SourceFilePath = tempPath ?? string.Empty
        });

        const string elementKey = "target://Element";
        refCat.AddElement(new FakeSubElement
        {
            Key          = elementKey,
            Kind         = targetKind,
            SourceAssetId = deleteId
        });

        var refHostId = Guid.NewGuid();
        assetCat.AddAsset(new FakeAsset { AssetId = refHostId });

        var incoming = new AssetReference(
            refHostId, AssetKind.BTree,
            Guid.NewGuid(), "node/path", elementKey, targetKind);
        refCat.AddReference(incoming);

        return (deleteId, incoming);
    }

    // ── Classification tests ─────────────────────────────────────────────────

    [Theory]
    [InlineData(SubElementKind.ActionFqn,       ReferenceCriticality.Critical)]
    [InlineData(SubElementKind.ConditionFqn,    ReferenceCriticality.Critical)]
    [InlineData(SubElementKind.GuardFqn,        ReferenceCriticality.Critical)]
    [InlineData(SubElementKind.AssetReference,  ReferenceCriticality.Critical)]
    [InlineData(SubElementKind.BlackboardField, ReferenceCriticality.Critical)]
    [InlineData(SubElementKind.EventName,          ReferenceCriticality.AutoResolvable)]
    [InlineData(SubElementKind.BlackboardVariable, ReferenceCriticality.AutoResolvable)]
    [InlineData(SubElementKind.UtilityInput,       ReferenceCriticality.AutoResolvable)]
    public void PreviewDelete_ClassifiesCriticalVsAutoResolvable(
        SubElementKind kind, ReferenceCriticality expectedCriticality)
    {
        var refCat   = new FakeReferenceCatalog();
        var assetCat = new FakeAssetCatalog();
        var (deleteId, _) = BuildScenario(refCat, assetCat, kind);

        var svc     = MakeService(refCat, assetCat);
        var preview = svc.PreviewDelete(deleteId, new DeleteOptions());

        Assert.Single(preview.DanglingReferences);
        Assert.Single(preview.ClassifiedReferences);

        var classified = preview.ClassifiedReferences[0];
        Assert.Equal(expectedCriticality, classified.Criticality);
        Assert.Equal("target://Element", classified.Reference.TargetKey);
    }

    [Fact]
    public void PreviewDelete_MixedKinds_SplitCriticalAndAuto()
    {
        var refCat   = new FakeReferenceCatalog();
        var assetCat = new FakeAssetCatalog();

        var deleteId = Guid.NewGuid();
        assetCat.AddAsset(new FakeAsset { AssetId = deleteId });

        // Two critical sub-elements and two auto-resolvable
        void AddElement(string key, SubElementKind kind)
        {
            refCat.AddElement(new FakeSubElement
                { Key = key, Kind = kind, SourceAssetId = deleteId });
            var refHostId = Guid.NewGuid();
            assetCat.AddAsset(new FakeAsset { AssetId = refHostId });
            refCat.AddReference(new AssetReference(
                refHostId, AssetKind.BTree, Guid.NewGuid(), "p", key, kind));
        }

        AddElement("act://A",  SubElementKind.ActionFqn);        // Critical
        AddElement("ref://B",  SubElementKind.AssetReference);   // Critical
        AddElement("evt://C",  SubElementKind.EventName);         // Auto
        AddElement("var://D",  SubElementKind.BlackboardVariable); // Auto

        var svc     = MakeService(refCat, assetCat);
        var preview = svc.PreviewDelete(deleteId, new DeleteOptions());

        Assert.Equal(4, preview.DanglingReferences.Count);
        Assert.Equal(4, preview.ClassifiedReferences.Count);

        var critical = preview.ClassifiedReferences
            .Where(c => c.Criticality == ReferenceCriticality.Critical).ToList();
        var auto = preview.ClassifiedReferences
            .Where(c => c.Criticality == ReferenceCriticality.AutoResolvable).ToList();

        Assert.Equal(2, critical.Count);
        Assert.Equal(2, auto.Count);

        // Verify the CriticalReferences convenience property
        Assert.Equal(2, preview.CriticalReferences.Count);
        var critKeys = preview.CriticalReferences.Select(r => r.TargetKey).OrderBy(k => k).ToList();
        Assert.Contains("act://A", critKeys);
        Assert.Contains("ref://B", critKeys);
    }

    // ── ApplyDelete refusal tests ────────────────────────────────────────────

    [Fact]
    public void ApplyDelete_RefusesCritical_WhenDisallowed_DoesNotDeleteFile()
    {
        var tempFile = TempFile();
        File.WriteAllText(tempFile, "dummy content");

        try
        {
            var refCat   = new FakeReferenceCatalog();
            var assetCat = new FakeAssetCatalog();
            var (deleteId, _) = BuildScenario(
                refCat, assetCat, SubElementKind.ActionFqn, tempFile);

            var svc     = MakeService(refCat, assetCat);
            // AllowDanglingReferences = false (default) → Warning issue added → refusal triggered.
            var preview = svc.PreviewDelete(deleteId, new DeleteOptions(AllowDanglingReferences: false));

            var result = svc.ApplyDelete(preview);

            // Must refuse
            Assert.False(result.Success);
            Assert.NotNull(result.FailureReason);
            Assert.Contains("critical", result.FailureReason, StringComparison.OrdinalIgnoreCase);

            // File must NOT be deleted
            Assert.True(File.Exists(tempFile), "File should not be deleted when delete is refused.");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void ApplyDelete_AllowsWhenAccepted_DeletesFile()
    {
        var tempFile = TempFile();
        File.WriteAllText(tempFile, "dummy content");

        var refCat   = new FakeReferenceCatalog();
        var assetCat = new FakeAssetCatalog();
        var (deleteId, _) = BuildScenario(
            refCat, assetCat, SubElementKind.ActionFqn, tempFile);

        var svc = MakeService(refCat, assetCat);
        // AllowDanglingReferences = true → no Warning issue → no refusal even for Critical refs
        var preview = svc.PreviewDelete(deleteId, new DeleteOptions(AllowDanglingReferences: true));

        var result = svc.ApplyDelete(preview);

        Assert.True(result.Success, result.FailureReason ?? "Expected success");
        Assert.False(File.Exists(tempFile), "File should be deleted when AllowDanglingReferences=true");
    }

    [Fact]
    public void PreviewDelete_AutoResolvableOnly_DoesNotBlock()
    {
        var tempFile = TempFile();
        File.WriteAllText(tempFile, "dummy content");

        try
        {
            var refCat   = new FakeReferenceCatalog();
            var assetCat = new FakeAssetCatalog();
            var (deleteId, _) = BuildScenario(
                refCat, assetCat, SubElementKind.EventName, tempFile);

            var svc     = MakeService(refCat, assetCat);
            var preview = svc.PreviewDelete(deleteId, new DeleteOptions(AllowDanglingReferences: false));

            // Verify classification: single Auto-resolvable ref
            Assert.Single(preview.ClassifiedReferences);
            Assert.Equal(ReferenceCriticality.AutoResolvable,
                preview.ClassifiedReferences[0].Criticality);
            Assert.Empty(preview.CriticalReferences);

            // ApplyDelete: Warning issue present (dangling refs not allowed) but no critical refs
            // → still proceeds (no critical refusal)
            var result = svc.ApplyDelete(preview);

            Assert.True(result.Success, result.FailureReason ?? "Expected success for auto-only refs");
            Assert.False(File.Exists(tempFile), "File should be deleted for auto-resolvable only");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void PreviewDelete_NoRefs_ClassifiedReferences_IsEmpty()
    {
        var refCat   = new FakeReferenceCatalog();
        var assetCat = new FakeAssetCatalog();

        var deleteId = Guid.NewGuid();
        assetCat.AddAsset(new FakeAsset { AssetId = deleteId });

        var svc     = MakeService(refCat, assetCat);
        var preview = svc.PreviewDelete(deleteId, new DeleteOptions());

        Assert.Empty(preview.DanglingReferences);
        Assert.Empty(preview.ClassifiedReferences);
        Assert.Empty(preview.CriticalReferences);
    }
}
