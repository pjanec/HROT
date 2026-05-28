using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Comparison;

namespace Hrot.Editor.AiShared.Tests.Comparison;

public sealed class CompanionFileDiscoveryTests : IDisposable
{
    private readonly string _tempDir;

    public CompanionFileDiscoveryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "CompanionDiscoveryTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void DiscoverFromMainFile_BTree_ReturnsMainPlusTwoMissingOnePresentCompanion()
    {
        // Arrange: Foo_BT.cs and Foo_BT.Blackboard.cs present; heavy and orchestrators absent.
        var mainPath = Path.Combine(_tempDir, "Foo_BT.cs");
        var blackboardPath = Path.Combine(_tempDir, "Foo_BT.Blackboard.cs");
        File.WriteAllText(mainPath, "// content");
        File.WriteAllText(blackboardPath, "// content");

        // Act
        var result = CompanionFileDiscovery.DiscoverFromMainFile(mainPath, AssetKind.BTree);

        // Assert: 3 companions (Blackboard, HeavyBlackboard, Orchestrators)
        Assert.Equal(mainPath, result.MainFilePath);
        Assert.Equal(3, result.Companions.Count);

        var blackboard = result.Companions.Single(c => c.Path.EndsWith("Foo_BT.Blackboard.cs"));
        var heavy = result.Companions.Single(c => c.Path.EndsWith("Foo_BT.HeavyBlackboard.cs"));
        var orchestrators = result.Companions.Single(c => c.Path.EndsWith("Foo_BT.Orchestrators.g.cs"));

        Assert.True(blackboard.IsPresent);
        Assert.False(heavy.IsPresent);
        Assert.False(orchestrators.IsPresent);
    }

    [Fact]
    public void DiscoverFromMainFile_Blackboard_ReturnsPresentAndMissingCompanions()
    {
        // Arrange: Foo.Blackboard.cs present, Foo.HeavyBlackboard.cs also present.
        var mainPath = Path.Combine(_tempDir, "Foo.Blackboard.cs");
        var heavyPath = Path.Combine(_tempDir, "Foo.HeavyBlackboard.cs");
        File.WriteAllText(mainPath, "// content");
        File.WriteAllText(heavyPath, "// heavy content");

        // Act
        var result = CompanionFileDiscovery.DiscoverFromMainFile(mainPath, AssetKind.Blackboard);

        // Assert: 1 companion (HeavyBlackboard) and it is present.
        Assert.Equal(mainPath, result.MainFilePath);
        Assert.Single(result.Companions);
        Assert.True(result.Companions[0].IsPresent);
        Assert.EndsWith("Foo.HeavyBlackboard.cs", result.Companions[0].Path);
    }

    [Fact]
    public void DiscoverFromMainFile_Blueprint_ReturnsNoCompanions()
    {
        var mainPath = Path.Combine(_tempDir, "Foo.bp.json");
        File.WriteAllText(mainPath, "{\"AssetId\":\"00000000-0000-0000-0000-000000000001\"}");

        var result = CompanionFileDiscovery.DiscoverFromMainFile(mainPath, AssetKind.Blueprint);

        Assert.Equal(mainPath, result.MainFilePath);
        Assert.Empty(result.Companions);
    }

    [Fact]
    public void DiscoverFromFolder_FindsFileSkippingDotPrefixedSubdir()
    {
        // Arrange: real file in root, same-AssetId file inside .migration-snapshots (should be skipped).
        var targetId = Guid.NewGuid();
        var mainPath = Path.Combine(_tempDir, "Foo_BT.cs");
        File.WriteAllText(mainPath, $"// AssetId: {targetId:D}\n");

        var snapshotDir = Path.Combine(_tempDir, ".migration-snapshots");
        Directory.CreateDirectory(snapshotDir);
        var snapshotPath = Path.Combine(snapshotDir, "Foo_BT.cs");
        File.WriteAllText(snapshotPath, $"// AssetId: {targetId:D}\n");

        // Act
        var result = CompanionFileDiscovery.DiscoverFromFolder(_tempDir, targetId, AssetKind.BTree);

        // Assert: returns the main file, NOT the snapshot.
        Assert.NotNull(result);
        Assert.Equal(mainPath, result!.MainFilePath);
    }

    [Fact]
    public void DiscoverFromFolder_ExcludesDotGitDirectory()
    {
        // Arrange: only file with target AssetId is inside .git/ -- should not be found.
        var targetId = Guid.NewGuid();
        var gitDir = Path.Combine(_tempDir, ".git");
        Directory.CreateDirectory(gitDir);
        File.WriteAllText(Path.Combine(gitDir, "Bar.cs"), $"// AssetId: {targetId:D}\n");

        // Act
        var result = CompanionFileDiscovery.DiscoverFromFolder(_tempDir, targetId, AssetKind.BTree);

        // Assert: not found.
        Assert.Null(result);
    }

    [Fact]
    public void DiscoverFromFolder_EmptyFolder_ReturnsNull()
    {
        var emptyDir = Path.Combine(_tempDir, "empty");
        Directory.CreateDirectory(emptyDir);

        var result = CompanionFileDiscovery.DiscoverFromFolder(emptyDir, Guid.NewGuid(), AssetKind.BTree);

        Assert.Null(result);
    }

    [Fact]
    public void DiscoverFromFolder_PreferAssetIdOverOwningAssetId()
    {
        // Both files share the same GUID; the BTree main file uses AssetId:, the Blackboard companion uses OwningAssetId:.
        var targetId = Guid.NewGuid();

        var blackboardPath = Path.Combine(_tempDir, "MyAsset_BT.Blackboard.cs");
        File.WriteAllText(blackboardPath, $"// OwningAssetId: {targetId:D}\npublic struct MyAsset_BT_Blackboard {{ }}\n");

        var mainPath = Path.Combine(_tempDir, "MyAsset_BT.cs");
        File.WriteAllText(mainPath, $"// AssetId: {targetId:D}\npublic static class MyAsset_BT {{ }}\n");

        var result = CompanionFileDiscovery.DiscoverFromFolder(_tempDir, targetId, AssetKind.BTree);

        Assert.NotNull(result);
        Assert.Equal(mainPath, result!.MainFilePath);
    }
}
