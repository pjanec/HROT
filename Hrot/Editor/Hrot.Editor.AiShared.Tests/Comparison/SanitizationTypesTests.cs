using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Comparison;

namespace Hrot.Editor.AiShared.Tests.Comparison;

/// <summary>
/// Verifies that the core record types from the sanitization framework have
/// value-based equality semantics (standard for C# records).
/// </summary>
public sealed class SanitizationTypesTests
{
    private static readonly Guid SomeGuid = new Guid("a1b2c3d4-0000-0000-0000-000000000001");

    [Fact]
    public void AssetExportRequest_RecordEquality_WorksRoundTrip()
    {
        var a = new AssetExportRequest("path/to/file.cs", "path/to/dir", AssetKind.BTree);
        var b = new AssetExportRequest("path/to/file.cs", "path/to/dir", AssetKind.BTree);

        Assert.Equal(a, b);
        Assert.True(a == b);
    }

    [Fact]
    public void SanitizationWarning_RecordEquality_WorksRoundTrip()
    {
        var a = new SanitizationWarning("some warning");
        var b = new SanitizationWarning("some warning");

        Assert.Equal(a, b);
    }

    [Fact]
    public void AssetMetadataBlock_RecordEquality_WorksRoundTrip()
    {
        var a = new AssetMetadataBlock(
            "OrcGuard_BT", AssetKind.BTree, SomeGuid,
            "path.cs", Array.Empty<string>(), null);
        var b = new AssetMetadataBlock(
            "OrcGuard_BT", AssetKind.BTree, SomeGuid,
            "path.cs", Array.Empty<string>(), null);

        Assert.Equal(a, b);
    }

    [Fact]
    public void AssetMetadataBlock_MigrationNotice_DefaultsToNull()
    {
        var block = new AssetMetadataBlock(
            "Test", AssetKind.BTree, SomeGuid, "path.cs", Array.Empty<string>(), null);

        Assert.Null(block.MigrationNotice);
    }

    [Fact]
    public void SanitizationResult_RecordEquality_WorksRoundTrip()
    {
        var meta = new AssetMetadataBlock(
            "OrcGuard_BT", AssetKind.BTree, SomeGuid,
            "path.cs", Array.Empty<string>(), null);
        var warnings = Array.Empty<SanitizationWarning>();

        var a = new SanitizationResult("text", meta, warnings);
        var b = new SanitizationResult("text", meta, warnings);

        Assert.Equal(a, b);
    }
}
