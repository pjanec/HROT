using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using Fbt;
using Hrot.AiEditor.Persistence.BTree;
using Hrot.BTree.Editor.Catalog;
using Hrot.BTree.Editor.Model;
using Hrot.BTree.Editor.Persistence;
using Hrot.Editor.AiShared;
using Xunit;

namespace Hrot.BTree.Editor.Tests.Catalog;

/// <summary>
/// PU-301: Tests for <see cref="BTreeJsonAssetContributor"/>.
/// Exercises header-lazy discovery, lazy LoadFull, malformed-skip, IsEditorOwned,
/// SourceFilePath, and AssetId collision (JSON wins).
/// </summary>
public sealed class BTreeJsonAssetContributorTests : IDisposable
{
    private readonly string _tempDir;

    public BTreeJsonAssetContributorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "BTreeJsonContrib_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static BehaviorTreeAssetDto MakeDto(string name = "TestTree")
    {
        var assetId = Guid.NewGuid();
        var dto = new BehaviorTreeAssetDto
        {
            AssetId = assetId,
            Name    = name,
            TargetNamespace = "Test.Trees",
        };
        // Add two nodes: Root + Sequence
        var rootVisualId = Guid.NewGuid();
        var seqVisualId  = Guid.NewGuid();
        var rootNode = new BTreeRootNodeDto
        {
            VisualId     = rootVisualId,
            EditorMetadata = new NodeEditorMetadataDto { X = 100f, Y = 200f },
        };
        rootNode.ChildVisualIds.Add(seqVisualId);
        dto.Nodes.Add(rootNode);
        dto.Nodes.Add(new BTreeSequenceNodeDto
        {
            VisualId     = seqVisualId,
            EditorMetadata = new NodeEditorMetadataDto { X = 150f, Y = 300f },
        });
        return dto;
    }

    private string WriteJson(BehaviorTreeAssetDto dto, string? fileName = null)
    {
        var json = BTreeJsonServices.Serialize(dto);
        var path = Path.Combine(_tempDir, fileName ?? (dto.Name + ".btree.json"));
        File.WriteAllText(path, json);
        return path;
    }

    // ── PU-301 SC1: Discover reads header (AssetId + Name) ───────────────────

    [Fact]
    public void Discover_ValidFile_HeaderContainsAssetIdAndName()
    {
        var dto  = MakeDto("Scout");
        var path = WriteJson(dto);

        var contrib = new BTreeJsonAssetContributor();
        contrib.Discover(rootDirectory: _tempDir);

        // LoadAll not yet called — but enumerate should be empty; header is internal
        // Verify by doing a full Refresh and checking the loaded asset
        contrib.Refresh(rootDirectory: _tempDir);
        var assets = contrib.Enumerate();
        assets.Should().HaveCount(1, "one .btree.json was written");
        assets[0].AssetId.Should().Be(dto.AssetId, "AssetId must match the JSON header");
        assets[0].Name.Should().Be("Scout",          "Name must match the JSON header");
    }

    // ── PU-301 SC2: LoadFull → model with correct topology ───────────────────

    [Fact]
    public void LoadFull_ValidFile_ModelHasCorrectTopologyAndOwnership()
    {
        var dto  = MakeDto("Scout");
        var path = WriteJson(dto);

        var contrib = new BTreeJsonAssetContributor();
        contrib.Refresh(rootDirectory: _tempDir);

        var assets = contrib.Enumerate();
        assets.Should().HaveCount(1);
        var asset = (BehaviorTreeAsset)assets[0];

        asset.Nodes.Count.Should().Be(2, "two nodes were written to the DTO");
        asset.IsEditorOwned.Should().BeTrue("JSON-loaded assets are always editor-owned");
        asset.SourceFilePath.Should().Be(path, "SourceFilePath must point at the .btree.json file");
        asset.IsDirty.Should().BeFalse("load must not mark the asset dirty");
    }

    // ── PU-301 SC3: malformed file is skipped; sibling still discovered ───────

    [Fact]
    public void Discover_MalformedFile_IsSkipped_SiblingStillDiscovered()
    {
        // Write one valid and one malformed file
        var validDto = MakeDto("ValidTree");
        WriteJson(validDto, "valid.btree.json");
        File.WriteAllText(Path.Combine(_tempDir, "malformed.btree.json"), "{ NOT VALID JSON !!!");

        var contrib = new BTreeJsonAssetContributor();
        // Should not throw
        var ex = Record.Exception(() => contrib.Refresh(rootDirectory: _tempDir));
        ex.Should().BeNull("malformed files must be silently skipped");

        var assets = contrib.Enumerate();
        assets.Should().HaveCount(1, "only the valid file should be loaded");
        assets[0].Name.Should().Be("ValidTree");
    }

    // ── PU-301 SC4: IsEditorOwned=true, SourceFilePath set ───────────────────

    [Fact]
    public void LoadFull_IsEditorOwned_True_And_SourceFilePath_EqualsJsonPath()
    {
        var dto  = MakeDto("Patrol");
        var path = WriteJson(dto);

        var contrib = new BTreeJsonAssetContributor();
        contrib.Refresh(rootDirectory: _tempDir);

        var asset = (BehaviorTreeAsset)contrib.Enumerate()[0];
        asset.IsEditorOwned.Should().BeTrue();
        asset.SourceFilePath.Should().Be(path);
    }

    // ── PU-301 SC5: IsDirty remains false after load ──────────────────────────

    [Fact]
    public void LoadFull_DoesNotMarkDirty()
    {
        var dto  = MakeDto("NoDirty");
        WriteJson(dto);

        var contrib = new BTreeJsonAssetContributor();
        contrib.Refresh(rootDirectory: _tempDir);

        contrib.Enumerate()[0].IsDirty.Should().BeFalse(
            "load+stitch must never call MarkDirty (PU-602 constraint)");
    }

    // ── PU-301 SC6: ContributorChanged fires on Refresh ───────────────────────

    [Fact]
    public void Refresh_FiresContributorChanged()
    {
        var dto  = MakeDto("Foo");
        WriteJson(dto);

        int fired = 0;
        var contrib = new BTreeJsonAssetContributor();
        contrib.ContributorChanged += () => fired++;

        contrib.Refresh(rootDirectory: _tempDir);
        fired.Should().Be(1, "ContributorChanged should fire once per Refresh");
    }

    // ── PU-301 SC7: JSON wins AssetId collision over assembly contributor ──────

    [Fact]
    public void Catalog_JsonWins_OnAssetIdCollision_WithAssemblyContributor()
    {
        // Build an assembly-projected asset with a known AssetId
        var assemblyContrib = new BTreeAssetContributor();
        assemblyContrib.LoadFrom(typeof(Hrot.AI.Behaviors.Trees.SampleScout).Assembly);
        var asmAssets = assemblyContrib.Enumerate();
        asmAssets.Should().NotBeEmpty("SampleScout must be in the assembly");

        var asmAsset = (BehaviorTreeAsset)asmAssets[0];

        // Build a JSON DTO with the same AssetId (simulate collision)
        var dto = MakeDto(asmAsset.Name);
        dto.AssetId = asmAsset.AssetId;  // same AssetId
        WriteJson(dto, "collision.btree.json");

        var jsonContrib = new BTreeJsonAssetContributor();
        jsonContrib.Refresh(rootDirectory: _tempDir);

        // Simulate AssetCatalog collision resolution: add JSON after assembly → JSON wins
        var catalog = new Hrot.Editor.AiShared.Catalog.AssetCatalog();
        catalog.AddContributor(assemblyContrib);
        catalog.AddContributor(jsonContrib);

        var found = catalog.FindByAssetId(asmAsset.AssetId);
        found.Should().NotBeNull();
        found!.IsEditorOwned.Should().BeTrue(
            "JSON contributor (added last) wins collision; JSON assets are IsEditorOwned=true");
        found.SourceFilePath.Should().EndWith(".btree.json",
            "JSON-loaded asset has a .btree.json SourceFilePath");
    }
}
