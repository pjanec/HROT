using Hrot.Editor.AiShared.Browser;
using Hrot.Editor.AiShared.Catalog;

namespace Hrot.Editor.AiShared.Tests.Browser;

public sealed class AssetRelPathTests
{
    // ── Fake IEditableAsset for tests ───────────────────────────────

    private sealed class FakeAsset : IEditableAsset
    {
        public Guid AssetId { get; init; } = Guid.NewGuid();
        public string Name { get; init; } = "TestAsset";
        public AssetKind Kind { get; init; } = AssetKind.Blueprint;
        public string SourceFilePath { get; init; } = "";
        public bool IsDirty => false;
        public bool IsEditorOwned => false;
#pragma warning disable 67
        public event Action? Changed;
#pragma warning restore 67
    }

    // ── Fake IAssetCatalogContributor for tests ─────────────────────

    private sealed class FakeContributor : IAssetCatalogContributor
    {
        public AssetKind Kind { get; init; } = AssetKind.Blueprint;
        public string? BaseFolder { get; init; }
        public IReadOnlyList<IEditableAsset> Enumerate() => Array.Empty<IEditableAsset>();
#pragma warning disable 67
        public event Action? ContributorChanged;
#pragma warning restore 67
    }

    // ── FileAsset_RelPath_IsSourceMinusBase ─────────────────────────

    [Fact]
    public void FileAsset_RelPath_IsSourceMinusBase()
    {
        // Simulate a base folder under …/Assets/Blueprints and a source file
        // under …/Assets/Blueprints/combat/Guard.bp.json.
        // Use OS-native paths so Path.GetRelativePath works correctly.
        var baseFolder = Path.Combine(
            AppContext.BaseDirectory, "Assets", "Blueprints");
        var sourceFilePath = Path.Combine(
            baseFolder, "combat", "Guard.bp.json");

        var asset = new FakeAsset
        {
            SourceFilePath = sourceFilePath,
            Name = "Guard" // Name should NOT be used when SourceFilePath is available.
        };

        var relPath = AssetRelPath.RelPath(asset, baseFolder);

        // Expected: "combat/Guard.bp.json" (forward slashes, no leading slash).
        Assert.Equal("combat/Guard.bp.json", relPath);
        Assert.DoesNotContain("\\", relPath);
        Assert.False(relPath.StartsWith("/"));
    }

    [Fact]
    public void FileAsset_RelPath_HandlesWindowsBackslash()
    {
        // On Windows, Path.GetRelativePath produces backslashes.
        // The helper must normalize them to forward slashes.
        var baseFolder = @"C:\Project\Assets\Blueprints";
        var sourceFilePath = @"C:\Project\Assets\Blueprints\nested\folder\Asset.bp.json";

        var asset = new FakeAsset { SourceFilePath = sourceFilePath };

        var relPath = AssetRelPath.RelPath(asset, baseFolder);

        Assert.Equal("nested/folder/Asset.bp.json", relPath);
        Assert.DoesNotContain("\\", relPath);
    }

    [Fact]
    public void FileAsset_RelPath_NestedDeeply()
    {
        var baseFolder = Path.Combine(
            AppContext.BaseDirectory, "Assets", "BTrees");
        var sourceFilePath = Path.Combine(
            baseFolder, "combat", "enemies", "Patrol.btree.json");

        var asset = new FakeAsset { SourceFilePath = sourceFilePath, Kind = AssetKind.BTree };

        var relPath = AssetRelPath.RelPath(asset, baseFolder);

        Assert.Equal("combat/enemies/Patrol.btree.json", relPath);
    }

    // ── ScenarioAsset_RelPath_IsName ────────────────────────────────

    [Fact]
    public void ScenarioAsset_RelPath_IsName()
    {
        // Scenario: empty SourceFilePath → returns Name.
        var asset = new FakeAsset
        {
            SourceFilePath = "",
            Name = "combat/ambush/scenario",
            Kind = AssetKind.Blueprint // Kind doesn't matter here.
        };

        var relPath = AssetRelPath.RelPath(asset, null);

        Assert.Equal("combat/ambush/scenario", relPath);
    }

    [Fact]
    public void ScenarioAsset_RelPath_NullBaseFolder_ReturnsName()
    {
        // Even if SourceFilePath is set, a null baseFolder means Name is used.
        var asset = new FakeAsset
        {
            SourceFilePath = "/some/path/file.bp.json",
            Name = "MyScenario"
        };

        var relPath = AssetRelPath.RelPath(asset, null);

        Assert.Equal("MyScenario", relPath);
    }

    [Fact]
    public void ScenarioAsset_RelPath_EmptyBaseFolder_ReturnsName()
    {
        // Empty string baseFolder is treated as null → Name is used.
        var asset = new FakeAsset
        {
            SourceFilePath = "/some/path/file.bp.json",
            Name = "MyScenario"
        };

        var relPath = AssetRelPath.RelPath(asset, "");

        Assert.Equal("MyScenario", relPath);
    }

    // ── Contributor_BaseFolder_IsTheRootItActuallyScans ─────────────
    //
    // ⭐⭐⭐ F2 (asset-management review round 4). THIS REPLACES A RAIL THAT WAS GREEN AND BLIND.
    //
    // ⛔⛔ What was here — `Contributor_BaseFolder_MatchesAssetRoot` — built a FakeContributor with
    //    `BaseFolder = AssetRoots.AssetsFor(kind)` and then asserted that `BaseFolder` equalled
    //    `AssetRoots.AssetsFor(kind)`. That is `Assert.Equal(x, x)` against a test double's own
    //    auto-property: it touched NO production contributor and would have stayed green if every
    //    real `BaseFolder` returned garbage. Its own comment named the assumption it never
    //    tested — "matches what a real file contributor would return".
    //
    // 🔴 The assumption was FALSE. `AssetsFor(kind)` is `ConfiguredRoot ?? AppContext.BaseDirectory`
    //    (AssetRoots.cs:260) — no source walk-up — while the composition roots construct these
    //    contributors from `ResolveAssetsRoot(...)` (ConfiguredRoot → walk-up → output dir). On a
    //    host with a source tree and no configured root the property named the bin dir while the
    //    contributor enumerated the source tree, silently: `ReportBase` warns only when NEITHER
    //    arm answered.
    //
    // ⭐ So the rule this pins is the one the old rail assumed instead of checking:
    //    BaseFolder is THE ROOT THE CONTRIBUTOR WAS GIVEN — for every production contributor,
    //    whatever `AssetsFor` would have said.
    //
    // ⚠ This class does NOT call `AssetRoots.Configure`, so it does not belong in
    //   AssetRootsTestCollection; the fallback assertion below only READS `AssetsFor`, and that
    //   collection is `DisableParallelization = true`, so it cannot mutate the static underneath us.

    [Fact]
    public void Contributor_BaseFolder_IsTheRootItActuallyScans()
    {
        // A root that is deliberately NOT AssetsFor(kind) for any kind — so returning the
        // re-derived root instead of the injected one FAILS rather than coincidentally passing.
        var root = Path.Combine(Path.GetTempPath(), "hrot-f2-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            // ── Blueprint: the root is a constructor argument ──
            var bp = new Hrot.Blueprints.Editor.Catalog.BlueprintAssetContributor(root);
            Assert.Equal(root, bp.BaseFolder);

            // ── BTree JSON: the root arrives via Discover ──
            var btree = new Hrot.BTree.Editor.Catalog.BTreeJsonAssetContributor();
            btree.Discover(rootDirectory: root);
            Assert.Equal(root, btree.BaseFolder);

            // ── HSM JSON: same shape ──
            var hsm = new Hrot.Hsm.Editor.Catalog.HsmJsonAssetContributor();
            hsm.Discover(rootDirectory: root);
            Assert.Equal(root, hsm.BaseFolder);

            // ⭐ And none of them silently answered the re-derived root — the exact F2 regression.
            Assert.NotEqual(AssetRoots.AssetsFor(AssetKind.Blueprint), bp.BaseFolder);
            Assert.NotEqual(AssetRoots.AssetsFor(AssetKind.BTree), btree.BaseFolder);
            Assert.NotEqual(AssetRoots.AssetsFor(AssetKind.Hsm), hsm.BaseFolder);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public void JsonContributor_WithNoRootSupplied_KeepsTheAssetsForFallback()
    {
        // ⭐ The fallback is deliberate, so it is pinned rather than left to drift: a contributor
        //   driven by an explicit jsonPaths list has no single base folder, and `AssetsFor(Kind)`
        //   stays the answer — exactly as before F2.
        var btree = new Hrot.BTree.Editor.Catalog.BTreeJsonAssetContributor();
        Assert.Equal(AssetRoots.AssetsFor(AssetKind.BTree), btree.BaseFolder);

        var hsm = new Hrot.Hsm.Editor.Catalog.HsmJsonAssetContributor();
        Assert.Equal(AssetRoots.AssetsFor(AssetKind.Hsm), hsm.BaseFolder);

        // ⚠ And a jsonPaths-only Discover must NOT invent one.
        btree.Discover(jsonPaths: Array.Empty<string>());
        Assert.Equal(AssetRoots.AssetsFor(AssetKind.BTree), btree.BaseFolder);
    }

    [Fact]
    public void Contributor_BaseFolder_FeedsRelPath_WithoutTheDotDotRecovery()
    {
        // ⭐⭐ WHY F2 mattered, pinned as behaviour rather than asserted in prose.
        //    `AssetRelPath.RelPath` carries a "../"-recovery branch written for this very mismatch
        //    ("the contributor scanned the source project dir … while baseFolder resolves to the
        //    bin/output dir … surfacing as bogus '..' tree levels in the browser").
        //    With BaseFolder naming the scanned root, the relpath is clean by construction and that
        //    branch is never entered.
        var root = Path.Combine(Path.GetTempPath(), "hrot-f2-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var bp = new Hrot.Blueprints.Editor.Catalog.BlueprintAssetContributor(root);
            var asset = new FakeAsset
            {
                SourceFilePath = Path.Combine(root, "combat", "Guard.bp.json"),
                Name = "Guard",
            };

            var relPath = AssetRelPath.RelPath(asset, bp.BaseFolder);

            Assert.Equal("combat/Guard.bp.json", relPath);
            Assert.DoesNotContain("..", relPath);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public void FileContributor_BaseFolder_IsNeverNull()
    {
        // ⭐ The §7.3a predicate the asset-management design keys on — "a kind is syncable iff its
        //   contributor exposes a non-null BaseFolder" — needs the file-backed contributors to keep
        //   answering non-null. F2's fix must not turn one of them into a null.
        var root = Path.Combine(Path.GetTempPath(), "hrot-f2-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            Assert.NotNull(new Hrot.Blueprints.Editor.Catalog.BlueprintAssetContributor(root).BaseFolder);
            Assert.NotNull(new Hrot.BTree.Editor.Catalog.BTreeJsonAssetContributor().BaseFolder);
            Assert.NotNull(new Hrot.Hsm.Editor.Catalog.HsmJsonAssetContributor().BaseFolder);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public void Contributor_BaseFolder_DefaultIsNull()
    {
        // Every IAssetCatalogContributor that does not override BaseFolder
        // gets the default interface member ⇒ null.
        IAssetCatalogContributor contrib = new FakeContributor();
        Assert.Null(contrib.BaseFolder);
    }

    // ── RelPath_EdgeCases ───────────────────────────────────────────

    [Fact]
    public void RelPath_NullAsset_ThrowsArgumentNullException()
    {
        var ex = Assert.Throws<ArgumentNullException>(
            () => AssetRelPath.RelPath(null!, "/base"));
        Assert.Equal("asset", ex.ParamName);
    }
}
