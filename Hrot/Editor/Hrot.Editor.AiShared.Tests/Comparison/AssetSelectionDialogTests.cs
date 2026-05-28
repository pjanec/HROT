using Hrot.Editor.AiShared.Comparison;
using Hrot.Editor.AiShared.Comparison.UI;

namespace Hrot.Editor.AiShared.Tests.Comparison;

public sealed class AssetSelectionDialogTests : IDisposable
{
    private readonly string _tempDir;

    public AssetSelectionDialogTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "AssetSelDialogTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        Directory.Delete(_tempDir, recursive: true);
    }

    // Writes a minimal BTree C# file with the given AssetId header.
    private string WriteBTreeFile(string name, Guid assetId)
    {
        var path = Path.Combine(_tempDir, name + "_BT.cs");
        File.WriteAllText(path, $"// AssetId: {assetId:D}\npublic class {name} {{ }}\n");
        return path;
    }

    // ---- Reverse tests --------------------------------------------------------

    [Fact]
    public void Reverse_SwapsPaths()
    {
        var state = new AssetSelectionDialogState { PathA = "A.cs", PathB = "B.cs" };
        state.Reverse();
        Assert.Equal("B.cs", state.PathA);
        Assert.Equal("A.cs", state.PathB);
    }

    [Fact]
    public void DoubleReverse_RestoresOriginal()
    {
        var state = new AssetSelectionDialogState { PathA = "A.cs", PathB = "B.cs" };
        state.Reverse();
        state.Reverse();
        Assert.Equal("A.cs", state.PathA);
        Assert.Equal("B.cs", state.PathB);
        Assert.False(state.Reversed);
    }

    // ---- Validate tests -------------------------------------------------------

    [Fact]
    public void Validate_ExistingFilesOfSameKind_ReturnsNull()
    {
        var id = Guid.NewGuid();
        var pathA = WriteBTreeFile("FooA", id);
        var pathB = WriteBTreeFile("FooB", id);

        var state = new AssetSelectionDialogState { PathA = pathA, PathB = pathB };
        var error = state.Validate(AssetKind.BTree);

        Assert.Null(error);
        Assert.Null(state.ValidationError);
    }

    [Fact]
    public void Validate_MissingFile_ReturnsErrorString()
    {
        var id = Guid.NewGuid();
        var pathA = Path.Combine(_tempDir, "Missing_BT.cs");  // does not exist
        var pathB = WriteBTreeFile("FooB", id);

        var state = new AssetSelectionDialogState { PathA = pathA, PathB = pathB };
        var error = state.Validate(AssetKind.BTree);

        Assert.NotNull(error);
        Assert.NotNull(state.ValidationError);
    }

    // ---- BuildResult tests ----------------------------------------------------

    [Fact]
    public void BuildResult_AfterValidate_SetsCorrectMainFilePaths()
    {
        var id = Guid.NewGuid();
        var pathA = WriteBTreeFile("FooA", id);
        var pathB = WriteBTreeFile("FooB", id);

        var state = new AssetSelectionDialogState { PathA = pathA, PathB = pathB };
        state.Validate(AssetKind.BTree);
        var result = state.BuildResult(AssetKind.BTree);

        Assert.Equal(pathA, result.VersionA.AssetMainFilePath);
        Assert.Equal(pathB, result.VersionB.AssetMainFilePath);
    }

    [Fact]
    public void BuildResult_AfterReverse_HasReversedTrue()
    {
        var id = Guid.NewGuid();
        var pathA = WriteBTreeFile("FooA", id);
        var pathB = WriteBTreeFile("FooB", id);

        var state = new AssetSelectionDialogState { PathA = pathA, PathB = pathB };
        state.Reverse();
        var result = state.BuildResult(AssetKind.BTree);

        Assert.True(result.Reversed);
    }
}
