using Hrot.Blueprints.Editor.Catalog;
using Hrot.Blueprints.Core;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Catalog;

namespace Hrot.Blueprints.Tests.Editor;

/// <summary>
/// Tests for <see cref="BlueprintAssetContributor"/> (AIE-011).
/// </summary>
public sealed class BlueprintAssetContributorTests : IDisposable
{
    private readonly string _tempDir;

    public BlueprintAssetContributorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "BpContributor_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // Helper: write a minimal .bp.json header to a file.
    private string WriteBpJson(string fileName, Guid assetId, string name)
    {
        var path = Path.Combine(_tempDir, fileName);
        var json = $"{{\"AssetId\":\"{assetId:D}\",\"Name\":\"{name}\",\"Dispatch\":\"Library\",\"Graphs\":[]}}";
        File.WriteAllText(path, json);
        return path;
    }

    /// <summary>
    /// AIE-011 SC1: Given a temp dir with ≥2 .bp.json files, Enumerate returns one IEditableAsset
    /// per file with the correct AssetId and Name (header-only, no full deserialize needed).
    /// </summary>
    [Fact]
    public void BlueprintAssetContributor_Enumerate_FindsBpJson()
    {
        var id1 = new Guid("a1000000-0001-0001-0001-000000000001");
        var id2 = new Guid("a2000000-0002-0002-0002-000000000002");
        WriteBpJson("Alpha.bp.json", id1, "Alpha");
        WriteBpJson("Beta.bp.json",  id2, "Beta");

        var contributor = new BlueprintAssetContributor(_tempDir);
        contributor.Refresh();

        var assets = contributor.Enumerate();
        Assert.Equal(2, assets.Count);

        // Verify both expected AssetIds appear.
        var byId = assets.ToDictionary(a => a.AssetId);
        Assert.True(byId.ContainsKey(id1), "Missing AssetId for Alpha");
        Assert.True(byId.ContainsKey(id2), "Missing AssetId for Beta");

        // Verify names were read from header.
        Assert.Equal("Alpha", byId[id1].Name);
        Assert.Equal("Beta",  byId[id2].Name);

        // Verify kind.
        Assert.All(assets, a => Assert.Equal(AssetKind.Blueprint, a.Kind));

        // Verify source file paths point to .bp.json files.
        Assert.All(assets, a => Assert.EndsWith(".bp.json", a.SourceFilePath));
    }

    /// <summary>
    /// AIE-011 SC2: ContributorChanged fires on every Refresh() call.
    /// </summary>
    [Fact]
    public void BlueprintAssetContributor_FiresChanged_OnRefresh()
    {
        WriteBpJson("X.bp.json", Guid.NewGuid(), "X");

        var contributor = new BlueprintAssetContributor(_tempDir);
        int fireCount = 0;
        contributor.ContributorChanged += () => fireCount++;

        contributor.Refresh();
        contributor.Refresh(); // second call also fires

        Assert.Equal(2, fireCount);
    }

    /// <summary>
    /// AIE-011 SC3: Malformed JSON files are silently skipped; no exception is thrown
    /// and the healthy files are still enumerated.
    /// </summary>
    [Fact]
    public void BlueprintAssetContributor_IgnoresMalformedJson()
    {
        var healthyId = new Guid("b1000000-0001-0001-0001-000000000001");
        WriteBpJson("Healthy.bp.json", healthyId, "Healthy");

        // Write a malformed file.
        File.WriteAllText(Path.Combine(_tempDir, "Broken.bp.json"), "{ this is not valid json }");

        // Write a file with missing AssetId.
        File.WriteAllText(Path.Combine(_tempDir, "MissingId.bp.json"),
            "{\"Name\":\"NoId\",\"Dispatch\":\"Library\",\"Graphs\":[]}");

        var contributor = new BlueprintAssetContributor(_tempDir);

        // Must not throw.
        contributor.Refresh();

        var assets = contributor.Enumerate();
        // Only the healthy file with a valid AssetId should appear.
        Assert.Single(assets);
        Assert.Equal(healthyId, assets[0].AssetId);
        Assert.Equal("Healthy", assets[0].Name);
    }

    /// <summary>
    /// Additional: Enumerate returns empty list before first Refresh.
    /// </summary>
    [Fact]
    public void BlueprintAssetContributor_EmptyBeforeRefresh()
    {
        WriteBpJson("SomeAsset.bp.json", Guid.NewGuid(), "SomeAsset");

        var contributor = new BlueprintAssetContributor(_tempDir);
        Assert.Empty(contributor.Enumerate());
    }

    /// <summary>
    /// Additional: Non-existent root directory returns empty list without throwing.
    /// </summary>
    [Fact]
    public void BlueprintAssetContributor_NonExistentDirectory_ReturnsEmpty()
    {
        var contributor = new BlueprintAssetContributor(
            Path.Combine(Path.GetTempPath(), "DoesNotExist_" + Guid.NewGuid().ToString("N")));
        contributor.Refresh();
        Assert.Empty(contributor.Enumerate());
    }

    /// <summary>
    /// Additional: Subdirectory scanning — .bp.json files in sub-folders are found.
    /// </summary>
    [Fact]
    public void BlueprintAssetContributor_ScansSubdirectories()
    {
        var subDir = Directory.CreateDirectory(Path.Combine(_tempDir, "sub")).FullName;
        var id = new Guid("c1000000-0001-0001-0001-000000000001");
        var path = Path.Combine(subDir, "Deep.bp.json");
        File.WriteAllText(path,
            $"{{\"AssetId\":\"{id:D}\",\"Name\":\"Deep\",\"Dispatch\":\"Library\",\"Graphs\":[]}}");

        var contributor = new BlueprintAssetContributor(_tempDir);
        contributor.Refresh();

        var assets = contributor.Enumerate();
        Assert.Single(assets);
        Assert.Equal(id, assets[0].AssetId);
    }

    /// <summary>
    /// Additional: Kind property on the contributor is AssetKind.Blueprint.
    /// </summary>
    [Fact]
    public void BlueprintAssetContributor_Kind_IsBlueprint()
    {
        var contributor = new BlueprintAssetContributor(_tempDir);
        Assert.Equal(AssetKind.Blueprint, contributor.Kind);
    }
}
