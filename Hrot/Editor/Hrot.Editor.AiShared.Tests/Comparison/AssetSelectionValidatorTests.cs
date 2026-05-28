using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Comparison;

namespace Hrot.Editor.AiShared.Tests.Comparison;

public sealed class AssetSelectionValidatorTests : IDisposable
{
    private readonly string _tempDir;

    public AssetSelectionValidatorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ValidatorTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        Directory.Delete(_tempDir, recursive: true);
    }

    private string WriteBTreeFile(string name, Guid assetId)
    {
        var path = Path.Combine(_tempDir, name + "_BT.cs");
        File.WriteAllText(path, $"// AssetId: {assetId:D}\npublic class {name} {{ }}\n");
        return path;
    }

    private string WriteBlueprintFile(string name, Guid assetId)
    {
        var path = Path.Combine(_tempDir, name + ".bp.json");
        File.WriteAllText(path, $"{{\"AssetId\":\"{assetId:D}\"}}");
        return path;
    }

    private DiscoveredAsset MakeDiscovered(string path) =>
        new DiscoveredAsset(path, Array.Empty<DiscoveredCompanion>());

    [Fact]
    public void Validate_KindMismatch_BTreeAndBlueprint_ReturnsInvalidWithAssetKindsError()
    {
        var id = Guid.NewGuid();
        var btreePath = WriteBTreeFile("Foo", id);
        var bpPath = WriteBlueprintFile("Bar", id);

        var result = AssetSelectionValidator.Validate(
            MakeDiscovered(btreePath),
            MakeDiscovered(bpPath),
            AssetKind.BTree);

        Assert.False(result.IsValid);
        Assert.Single(result.Issues);
        Assert.Contains("asset kinds", result.Issues[0].Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(ValidationSeverity.Error, result.Issues[0].Severity);
    }

    [Fact]
    public void Validate_SameKindSameAssetId_ReturnsValidNoIssues()
    {
        var id = Guid.NewGuid();
        var pathA = WriteBTreeFile("FooA", id);
        var pathB = WriteBTreeFile("FooB", id);

        var result = AssetSelectionValidator.Validate(
            MakeDiscovered(pathA),
            MakeDiscovered(pathB),
            AssetKind.BTree);

        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void Validate_SameKindDifferentAssetIds_ReturnsValidWithWarning()
    {
        var idA = Guid.NewGuid();
        var idB = Guid.NewGuid();
        var pathA = WriteBTreeFile("FooA", idA);
        var pathB = WriteBTreeFile("FooB", idB);

        var result = AssetSelectionValidator.Validate(
            MakeDiscovered(pathA),
            MakeDiscovered(pathB),
            AssetKind.BTree);

        Assert.True(result.IsValid);
        Assert.Single(result.Issues);
        Assert.Equal(ValidationSeverity.Warning, result.Issues[0].Severity);
        Assert.Contains(idA.ToString("D"), result.Issues[0].Message);
        Assert.Contains(idB.ToString("D"), result.Issues[0].Message);
    }

    [Fact]
    public void Validate_MissingFileVersionA_ReturnsInvalidWithFileNotFound()
    {
        var id = Guid.NewGuid();
        var missingPath = Path.Combine(_tempDir, "Missing_BT.cs");
        var realPath = WriteBTreeFile("Real", id);

        var result = AssetSelectionValidator.Validate(
            MakeDiscovered(missingPath),
            MakeDiscovered(realPath),
            AssetKind.BTree);

        Assert.False(result.IsValid);
        Assert.Single(result.Issues);
        Assert.Contains("File not found", result.Issues[0].Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(ValidationSeverity.Error, result.Issues[0].Severity);
    }

    [Fact]
    public void Validate_UnparseableFile_ReturnsInvalidWithCannotParseError()
    {
        // File exists and is readable but contains no // AssetId: header.
        var garbagePath = Path.Combine(_tempDir, "Garbage_BT.cs");
        File.WriteAllText(garbagePath, "this is not a valid BTree file and has no AssetId header\n");

        var id = Guid.NewGuid();
        var realPath = WriteBTreeFile("Real", id);

        var result = AssetSelectionValidator.Validate(
            MakeDiscovered(garbagePath),
            MakeDiscovered(realPath),
            AssetKind.BTree);

        Assert.False(result.IsValid);
        Assert.Single(result.Issues);
        Assert.Contains("Cannot parse", result.Issues[0].Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(ValidationSeverity.Error, result.Issues[0].Severity);
    }
}
