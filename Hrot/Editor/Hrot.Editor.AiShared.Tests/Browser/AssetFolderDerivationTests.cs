using Hrot.Editor.AiShared.Browser;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Browser;

// ─────────────────────────────────────────────────────────────────────────────
// BATCH-39 — AssetFolderDerivation.KnownSubfolders tests
// ─────────────────────────────────────────────────────────────────────────────

public sealed class AssetFolderDerivationTests
{
    // ── Fake IEditableAsset for tests ─────────────────────────────────

    private sealed class FakeEditableAsset : IEditableAsset
    {
        public Guid AssetId { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = "";
        public AssetKind Kind { get; set; }
        public string SourceFilePath { get; set; } = "";
        public bool IsDirty => false;
        public bool IsEditorOwned => false;
#pragma warning disable 67
        public event Action? Changed;
#pragma warning restore 67
    }

    // ── KnownSubfolders_ReturnsDistinctDirsForKind ─────────────────────

    [Fact]
    public void KnownSubfolders_ReturnsDistinctDirsForKind()
    {
        var baseFolder = "C:/proj/Assets/Blueprints";

        var assets = new IEditableAsset[]
        {
            new FakeEditableAsset
            {
                Name = "Foo", Kind = AssetKind.Blueprint,
                SourceFilePath = $"{baseFolder}/AI/Foo.bp.json"
            },
            new FakeEditableAsset
            {
                Name = "Bar", Kind = AssetKind.Blueprint,
                SourceFilePath = $"{baseFolder}/AI/Bar.bp.json"
            },
            new FakeEditableAsset
            {
                Name = "Root", Kind = AssetKind.Blueprint,
                SourceFilePath = $"{baseFolder}/Root.bp.json"
            },
        };

        var result = AssetFolderDerivation.KnownSubfolders(
            assets, AssetKind.Blueprint, _ => baseFolder);

        // Contains "AI" and "" (root).
        Assert.Contains("AI", result);
        Assert.Contains("", result);

        // "AI" appears once (distinct).
        Assert.Single(result.Where(s => s == "AI"));

        // Sorted: "" before "AI" (OrdinalIgnoreCase).
        var list = result.ToList();
        Assert.True(list.IndexOf("") < list.IndexOf("AI"), "root should sort before AI");
    }

    // ── KnownSubfolders_FiltersByKind ──────────────────────────────────

    [Fact]
    public void KnownSubfolders_FiltersByKind()
    {
        var bpBase = "C:/proj/Assets/Blueprints";
        var hsmBase = "C:/proj/Assets/HSMs";

        var assets = new IEditableAsset[]
        {
            new FakeEditableAsset
            {
                Name = "BP1", Kind = AssetKind.Blueprint,
                SourceFilePath = $"{bpBase}/AI/BP1.bp.json"
            },
            new FakeEditableAsset
            {
                Name = "HSM1", Kind = AssetKind.Hsm,
                SourceFilePath = $"{hsmBase}/Combat/HSM1.hsm"
            },
        };

        var result = AssetFolderDerivation.KnownSubfolders(
            assets, AssetKind.Blueprint,
            kind => kind == AssetKind.Blueprint ? bpBase
                : kind == AssetKind.Hsm ? hsmBase : null);

        // Only Blueprint subfolders contribute.
        Assert.Contains("AI", result);
        Assert.Contains("", result);
        Assert.DoesNotContain("Combat", result);
        Assert.Equal(2, result.Count);
    }

    // ── KnownSubfolders_IncludesRoot_WhenAssetsAtRoot ───────────────────

    [Fact]
    public void KnownSubfolders_IncludesRoot_WhenAssetsAtRoot()
    {
        var baseFolder = "C:/proj/Assets/Blueprints";

        var assets = new IEditableAsset[]
        {
            new FakeEditableAsset
            {
                Name = "AtRoot", Kind = AssetKind.Blueprint,
                SourceFilePath = $"{baseFolder}/AtRoot.bp.json"
            },
        };

        var result = AssetFolderDerivation.KnownSubfolders(
            assets, AssetKind.Blueprint, _ => baseFolder);

        // Asset with no subfolder → "" (root) present.
        Assert.Contains("", result);
        Assert.Single(result); // only root
    }

    // ── KnownSubfolders_EmptyKind_YieldsRootOnly ───────────────────────

    [Fact]
    public void KnownSubfolders_EmptyKind_YieldsRootOnly()
    {
        var baseFolder = "C:/proj/Assets/Blueprints";

        // Assets exist, but none of the requested kind.
        var assets = new IEditableAsset[]
        {
            new FakeEditableAsset
            {
                Name = "HSM1", Kind = AssetKind.Hsm,
                SourceFilePath = "C:/proj/Assets/HSMs/HSM1.hsm"
            },
        };

        var result = AssetFolderDerivation.KnownSubfolders(
            assets, AssetKind.Blueprint,
            kind => kind == AssetKind.Blueprint ? baseFolder : null);

        // No assets of the kind → returns [""] (just root).
        Assert.Single(result);
        Assert.Equal("", result[0]);
    }

    // ── ToCategoryNode_BuildsNestedTree ───────────────────────────────

    [Fact]
    public void ToCategoryNode_BuildsNestedTree()
    {
        var relPaths = new[] { "", "AI", "AI/Combat", "Patrol" };

        var root = AssetFolderDerivation.ToCategoryNode(relPaths);

        // Root has empty name.
        Assert.Equal("", root.Name);

        // Root children: "AI" and "Patrol" (sorted by name, ordinal).
        Assert.Equal(2, root.Children.Count);
        Assert.Equal("AI", root.Children[0].Name);
        Assert.Equal("Patrol", root.Children[1].Name);

        // "AI" has one child: "Combat".
        var aiNode = root.Children[0];
        Assert.Single(aiNode.Children);
        Assert.Equal("Combat", aiNode.Children[0].Name);

        // "Combat" has no children.
        var combatNode = aiNode.Children[0];
        Assert.Empty(combatNode.Children);

        // "Patrol" has no children.
        var patrolNode = root.Children[1];
        Assert.Empty(patrolNode.Children);
    }
}
