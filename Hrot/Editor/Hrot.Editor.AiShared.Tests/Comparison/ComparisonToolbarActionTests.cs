using Hrot.Editor.AiShared.Comparison;
using Hrot.Editor.AiShared.Comparison.UI;

namespace Hrot.Editor.AiShared.Tests.Comparison;

/// <summary>
/// Integration tests for the comparison pipeline invoked by <see cref="ComparisonToolbarAction"/>.
/// These tests exercise the pipeline logic (dialog state -> export builder -> delivery state)
/// without any ImGui rendering calls.
/// </summary>
public sealed class ComparisonToolbarActionTests : IDisposable
{
    private readonly string _tempDir;

    public ComparisonToolbarActionTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "CompToolbarTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        Directory.Delete(_tempDir, recursive: true);
    }

    // A fake sanitizer that returns deterministic content for any request.
    private sealed class FakeBTreeSanitizer : IAssetComparisonSanitizer
    {
        public AssetKind TargetKind => AssetKind.BTree;

        public SanitizationResult Sanitize(AssetExportRequest request)
            => new(
                $"sanitized content of {Path.GetFileName(request.AssetMainFilePath)}",
                new AssetMetadataBlock(
                    "OrcGuard_BT",
                    AssetKind.BTree,
                    Guid.Parse("f7c0a1b2-1188-4c5d-9e3a-7b6c5d4e3f21"),
                    request.AssetMainFilePath,
                    Array.Empty<string>(),
                    new DateTime(2026, 1, 14, 11, 23, 8, DateTimeKind.Utc)),
                Array.Empty<SanitizationWarning>());
    }

    private string WriteBTreeFile(string name, Guid assetId)
    {
        var path = Path.Combine(_tempDir, name + "_BT.cs");
        File.WriteAllText(path, $"// AssetId: {assetId:D}\npublic class {name} {{ }}\n");
        return path;
    }

    // ---- Pipeline produces non-empty export -----------------------------------

    [Fact]
    public void Pipeline_ProducesNonEmptyExport_ContainsVersionAAndB()
    {
        var id = Guid.NewGuid();
        var pathA = WriteBTreeFile("OrcA", id);
        var pathB = WriteBTreeFile("OrcB", id);

        var registry = new SanitizerRegistry();
        registry.Register(new FakeBTreeSanitizer());

        var builder = new ComparisonExportBuilder();

        // Simulate what the dialog state would produce.
        var dialogState = new AssetSelectionDialogState { PathA = pathA, PathB = pathB };
        var validationError = dialogState.Validate(AssetKind.BTree);
        Assert.Null(validationError);  // precondition

        var selectionResult = dialogState.BuildResult(AssetKind.BTree);

        // Run the export builder.
        var sanitizer = registry.Get(AssetKind.BTree);
        var exportText = builder.Build(sanitizer, selectionResult.VersionA, selectionResult.VersionB);

        Assert.NotEmpty(exportText);
        Assert.Contains("VERSION A", exportText);
        Assert.Contains("VERSION B", exportText);
    }

    // ---- Export contains instruction block ------------------------------------

    [Fact]
    public void Pipeline_ExportContainsInstructionBlock()
    {
        var id = Guid.NewGuid();
        var pathA = WriteBTreeFile("OrcA", id);
        var pathB = WriteBTreeFile("OrcB", id);

        var registry = new SanitizerRegistry();
        registry.Register(new FakeBTreeSanitizer());

        var builder = new ComparisonExportBuilder();
        var versionA = new AssetExportRequest(pathA, _tempDir, AssetKind.BTree);
        var versionB = new AssetExportRequest(pathB, _tempDir, AssetKind.BTree);
        var sanitizer = registry.Get(AssetKind.BTree);

        var exportText = builder.Build(sanitizer, versionA, versionB);

        Assert.StartsWith("You are comparing", exportText);
    }

    // ---- Export delivery state shows correct preview -------------------------

    [Fact]
    public void Pipeline_DeliveryState_GetPreviewText_Returns30Lines()
    {
        // Build a 40-line export text.
        var lines = Enumerable.Range(1, 40).Select(i => $"line {i}");
        var exportText = string.Join('\n', lines);

        var deliveryState = new ExportDeliveryModalState(exportText, "TestAsset");
        var preview = deliveryState.GetPreviewText();
        var previewLines = preview.Split('\n');

        // 30 content lines + 1 marker = 31.
        Assert.Equal(31, previewLines.Length);
        Assert.Contains("[...]", previewLines[30]);
    }
}
