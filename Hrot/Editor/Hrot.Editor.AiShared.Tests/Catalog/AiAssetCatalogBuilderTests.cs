using System.Reflection;
using Fbt;
using Fhsm.Kernel.Attributes;
using Fhsm.Kernel.Data;
using Hrot.BTree.Editor.Catalog;
using Hrot.Blueprints.Editor.Catalog;
using Hrot.Editor.AiShared.Catalog;
using Hrot.Hsm.Editor.Catalog;

namespace Hrot.Editor.AiShared.Tests.Catalog;

// ── Fixtures embedded in this test assembly so LoadFrom(Assembly.GetExecutingAssembly()) works ──

internal static class AiCatalogBuilderBTreeFixtures
{
    [BTreeDefinition("TestBTree_Alpha")]
    public static BehaviorTreeBlob Alpha() => new BehaviorTreeBlob
    {
        TreeName        = "TestBTree_Alpha",
        Nodes           = Array.Empty<NodeDefinition>(),
        MethodNames     = Array.Empty<string>(),
        FloatParams     = Array.Empty<float>(),
        IntParams       = Array.Empty<int>(),
        SubtreeAssetIds = Array.Empty<string>(),
    };

    [BTreeDefinition("TestBTree_Beta")]
    public static BehaviorTreeBlob Beta() => new BehaviorTreeBlob
    {
        TreeName        = "TestBTree_Beta",
        Nodes           = Array.Empty<NodeDefinition>(),
        MethodNames     = Array.Empty<string>(),
        FloatParams     = Array.Empty<float>(),
        IntParams       = Array.Empty<int>(),
        SubtreeAssetIds = Array.Empty<string>(),
    };
}

internal static class AiCatalogBuilderHsmFixtures
{
    [HsmDefinition("TestHsm_Gamma")]
    public static HsmDefinitionBlob Gamma() => new HsmDefinitionBlob();

    [HsmDefinition("TestHsm_Delta")]
    public static HsmDefinitionBlob Delta() => new HsmDefinitionBlob();
}

/// <summary>
/// Tests for <see cref="AiAssetCatalogBuilder"/> (AIE-010).
/// </summary>
public sealed class AiAssetCatalogBuilderTests : IDisposable
{
    private readonly string _bpDir;

    public AiAssetCatalogBuilderTests()
    {
        _bpDir = Path.Combine(Path.GetTempPath(), "AiCatalogTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_bpDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_bpDir))
            Directory.Delete(_bpDir, recursive: true);
    }

    // Helper: create a fully-wired builder with real contributors.
    private (AiAssetCatalogBuilder builder, BTreeAssetContributor btree,
             HsmAssetContributor hsm, BlueprintAssetContributor bp) MakeBuilder()
    {
        var btree = new BTreeAssetContributor();
        var hsm   = new HsmAssetContributor();
        var bp    = new BlueprintAssetContributor(_bpDir);

        var builder = new AiAssetCatalogBuilder(
            btree, hsm, bp,
            asm => btree.LoadFrom(asm),
            asm => hsm.LoadFrom(asm),
            () => bp.Refresh());

        return (builder, btree, hsm, bp);
    }

    // Helper: write a minimal .bp.json file.
    private void WriteBpJson(string name, Guid id)
    {
        var path = Path.Combine(_bpDir, $"{name}.bp.json");
        File.WriteAllText(path,
            $"{{\"AssetId\":\"{id:D}\",\"Name\":\"{name}\",\"Dispatch\":\"Library\",\"Graphs\":[]}}");
    }

    // ── AIE-010 SC1: After LoadFrom, catalog lists BTree and HSM assets ────────

    /// <summary>
    /// AIE-010 SC1: Given a test assembly containing [BTreeDefinition] and [HsmDefinition]
    /// methods, RefreshFromAssembly fills the unified catalog with BTree and HSM entries.
    /// </summary>
    [Fact]
    public void AssetCatalog_AfterLoadFrom_ListsBTreeAndHsmAssets()
    {
        var (builder, _, _, _) = MakeBuilder();
        builder.RefreshFromAssembly(Assembly.GetExecutingAssembly());

        var all = builder.Catalog.All;

        var btreeEntries = all.Where(a => a.Kind == AssetKind.BTree).ToList();
        var hsmEntries   = all.Where(a => a.Kind == AssetKind.Hsm).ToList();

        // Our test assembly defines 2 BTree fixtures and 2 HSM fixtures.
        Assert.True(btreeEntries.Count >= 2,
            $"Expected ≥2 BTree entries, got {btreeEntries.Count}");
        Assert.True(hsmEntries.Count >= 2,
            $"Expected ≥2 HSM entries, got {hsmEntries.Count}");

        Assert.Contains(btreeEntries, a => a.Name == "TestBTree_Alpha");
        Assert.Contains(btreeEntries, a => a.Name == "TestBTree_Beta");
        Assert.Contains(hsmEntries,   a => a.Name == "TestHsm_Gamma");
        Assert.Contains(hsmEntries,   a => a.Name == "TestHsm_Delta");
    }

    // ── AIE-010 SC2: Changed fires when contributors reload ───────────────────

    /// <summary>
    /// AIE-010 SC2: Calling RefreshFromAssembly fires the catalog's Changed event
    /// (since each contributor's ContributorChanged fires → catalog rebuilds + fires Changed).
    /// </summary>
    [Fact]
    public void AiAssetCatalogBuilder_Refresh_RaisesCatalogChanged()
    {
        var (builder, _, _, _) = MakeBuilder();

        int changeCount = 0;
        builder.Catalog.Changed += () => changeCount++;

        builder.RefreshFromAssembly(Assembly.GetExecutingAssembly());

        // 3 contributors each fire ContributorChanged → catalog fires Changed 3 times.
        Assert.True(changeCount >= 3,
            $"Expected ≥3 Changed events (one per contributor), got {changeCount}");
    }

    // ── AIE-010 SC3: Merges all three kinds ──────────────────────────────────

    /// <summary>
    /// AIE-010 SC3: After RefreshFromAssembly, the catalog contains entries for all three
    /// kinds (BTree, HSM, Blueprint) simultaneously.
    /// </summary>
    [Fact]
    public void AssetCatalog_MergesAllThreeKinds()
    {
        var bpId = new Guid("c3000000-0001-0001-0001-000000000001");
        WriteBpJson("CombinedBp", bpId);

        var (builder, _, _, _) = MakeBuilder();
        builder.RefreshFromAssembly(Assembly.GetExecutingAssembly());

        var all = builder.Catalog.All;

        Assert.True(all.Any(a => a.Kind == AssetKind.BTree),   "No BTree entries");
        Assert.True(all.Any(a => a.Kind == AssetKind.Hsm),     "No HSM entries");
        Assert.True(all.Any(a => a.Kind == AssetKind.Blueprint),"No Blueprint entries");
    }

    // ── Existing catalog behavior not broken ──────────────────────────────────

    /// <summary>
    /// The underlying catalog's Find methods still work correctly after a refresh.
    /// </summary>
    [Fact]
    public void Catalog_FindByName_WorksAfterRefresh()
    {
        var (builder, _, _, _) = MakeBuilder();
        builder.RefreshFromAssembly(Assembly.GetExecutingAssembly());

        var found = builder.Catalog.FindByName("TestBTree_Alpha");
        Assert.NotNull(found);
        Assert.Equal(AssetKind.BTree, found.Kind);
    }

    /// <summary>
    /// Calling RefreshFromAssembly a second time clears the old entries and re-enumerates.
    /// The catalog list does not grow unboundedly.
    /// </summary>
    [Fact]
    public void AiAssetCatalogBuilder_DoubleRefresh_DoesNotDuplicate()
    {
        var (builder, _, _, _) = MakeBuilder();

        builder.RefreshFromAssembly(Assembly.GetExecutingAssembly());
        var countAfterFirst = builder.Catalog.All.Count;

        builder.RefreshFromAssembly(Assembly.GetExecutingAssembly());
        var countAfterSecond = builder.Catalog.All.Count;

        Assert.Equal(countAfterFirst, countAfterSecond);
    }

    /// <summary>
    /// Blueprint contributor entries appear in the catalog via Refresh() (called by RefreshFromAssembly).
    /// </summary>
    [Fact]
    public void AiAssetCatalogBuilder_BlueprintContributor_PopulatedViaRefresh()
    {
        var id = new Guid("d1000000-0001-0001-0001-000000000001");
        WriteBpJson("MyBp", id);

        var (builder, _, _, _) = MakeBuilder();
        builder.RefreshFromAssembly(Assembly.GetExecutingAssembly());

        var bp = builder.Catalog.FindByAssetId(id);
        Assert.NotNull(bp);
        Assert.Equal(AssetKind.Blueprint, bp.Kind);
        Assert.Equal("MyBp", bp.Name);
    }
}
