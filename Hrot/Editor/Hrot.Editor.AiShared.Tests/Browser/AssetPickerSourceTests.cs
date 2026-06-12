using Hrot.Editor.AiShared.Browser;
using Hrot.Editor.AiShared.Catalog;
using NodeEditor.UI.Picker;

namespace Hrot.Editor.AiShared.Tests.Browser;

public sealed class AssetPickerSourceTests
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

    // ── Fake IAssetCatalog for tests ──────────────────────────────────

    private sealed class FakeAssetCatalog : IAssetCatalog
    {
        public IReadOnlyList<IEditableAsset> All { get; set; } =
            Array.Empty<IEditableAsset>();

        public IEditableAsset? FindByAssetId(Guid assetId) => null;
        public IEditableAsset? FindByName(string name) => null;

        public IReadOnlyList<IEditableAsset> WhereDependsOn(Guid assetId) =>
            Array.Empty<IEditableAsset>();

#pragma warning disable 67
        public event Action<AssetKind>? Changed;
#pragma warning restore 67
    }

    // ── MTB-P8-T2-01: Category, IconKey, Tag ──────────────────────────

    [Fact]
    public void Entries_HaveKindGroupedCategory_AndPerKindIcon_AndAssetTag()
    {
        var baseFolder = "C:/proj/Assets/Blueprints";

        var bpInSubfolder = new FakeEditableAsset
        {
            AssetId = Guid.NewGuid(),
            Name = "Foo",
            Kind = AssetKind.Blueprint,
            SourceFilePath = $"{baseFolder}/AI/Foo.bp.json"
        };

        var bpAtRoot = new FakeEditableAsset
        {
            AssetId = Guid.NewGuid(),
            Name = "Root",
            Kind = AssetKind.Blueprint,
            SourceFilePath = $"{baseFolder}/Root.bp.json"
        };

        var catalog = new FakeAssetCatalog
        {
            All = new IEditableAsset[] { bpInSubfolder, bpAtRoot }
        };

        var source = new AssetPickerSource(
            catalog,
            AssetKindFilter.All,
            baseFolderResolver: kind => kind == AssetKind.Blueprint ? baseFolder : null);

        // Asset in subfolder → Category = "Blueprint/AI"
        var entry1 = source.ToEntry(bpInSubfolder);
        Assert.Equal("Blueprint/AI", entry1.Category);
        Assert.Equal(AssetKindIcons.GetIconKey(AssetKind.Blueprint), entry1.IconKey);
        Assert.Equal("asset/blueprint", entry1.IconKey);
        Assert.Same(bpInSubfolder, entry1.Tag);
        Assert.Equal("Foo", entry1.Name);

        // Asset at root → Category = "Blueprint"
        var entry2 = source.ToEntry(bpAtRoot);
        Assert.Equal("Blueprint", entry2.Category);
        Assert.Equal(AssetKindIcons.GetIconKey(AssetKind.Blueprint), entry2.IconKey);
        Assert.Same(bpAtRoot, entry2.Tag);
        Assert.Equal("Root", entry2.Name);
    }

    // ── MTB-P8-T2-02: Scenario-filtered variant ───────────────────────

    [Fact]
    public void ScenarioVariant_YieldsOnlyScenarios()
    {
        var bp = new FakeEditableAsset
        {
            Name = "BP1",
            Kind = AssetKind.Blueprint,
            SourceFilePath = "C:/proj/Assets/Blueprints/BP1.bp.json"
        };
        var scenario = new FakeEditableAsset
        {
            Name = "S1",
            Kind = AssetKind.Scenario,
            SourceFilePath = "C:/proj/Recipes/Scenarios/S1.scenario"
        };
        var hsm = new FakeEditableAsset
        {
            Name = "H1",
            Kind = AssetKind.Hsm,
            SourceFilePath = "C:/proj/Assets/HSMs/H1.hsm"
        };

        var catalog = new FakeAssetCatalog
        {
            All = new IEditableAsset[] { bp, scenario, hsm }
        };

        var source = new AssetPickerSource(catalog, AssetKindFilter.Scenario);

        // Query("") returns only Scenario-kind items.
        var results = source.Query("", null);
        Assert.Single(results);
        Assert.Equal(AssetKind.Scenario, results[0].Kind);
        Assert.Same(scenario, results[0]);

        // BuildEntries also yields only scenarios.
        var entries = source.BuildEntries("", null);
        Assert.Single(entries);
        Assert.Equal("asset/scenario", entries[0].IconKey);
        Assert.Same(scenario, entries[0].Tag);
    }

    // ── MTB-P8-T2-03: Stable item key ─────────────────────────────────

    [Fact]
    public void GetItemKey_StableAcrossQueries()
    {
        var assetId = Guid.NewGuid();
        var asset = new FakeEditableAsset
        {
            AssetId = assetId,
            Name = "Test",
            Kind = AssetKind.Blueprint
        };
        var catalog = new FakeAssetCatalog
        {
            All = new IEditableAsset[] { asset }
        };
        var source = new AssetPickerSource(catalog);

        var key1 = source.GetItemKey(asset);

        // Query again — the same asset should yield the same key.
        var results = source.Query("", null);
        var key2 = source.GetItemKey(results[0]);

        Assert.Equal(key1, key2);
        Assert.Equal(assetId.ToString(), key1);
        Assert.Equal(assetId.ToString(), key2);
    }

    // ── MTB-P8-T2-04: Recipe description ──────────────────────────────

    [Fact]
    public void Description_FromRecipeMetadata_WhenPresent()
    {
        var theAsset = new FakeEditableAsset
        {
            Name = "HasRecipe",
            Kind = AssetKind.Blueprint,
            SourceFilePath = "C:/proj/Assets/Blueprints/HasRecipe.bp.json"
        };
        var otherAsset = new FakeEditableAsset
        {
            Name = "NoRecipe",
            Kind = AssetKind.Blueprint,
            SourceFilePath = "C:/proj/Assets/Blueprints/NoRecipe.bp.json"
        };
        var catalog = new FakeAssetCatalog
        {
            All = new IEditableAsset[] { theAsset, otherAsset }
        };

        var source = new AssetPickerSource(
            catalog,
            describe: a => ReferenceEquals(a, theAsset) ? "Recipe desc" : null);

        var entry1 = source.ToEntry(theAsset);
        Assert.Equal("Recipe desc", entry1.Description);

        var entry2 = source.ToEntry(otherAsset);
        Assert.Null(entry2.Description);
    }

    // ── MTB-P8-T2-05: Single-kind omits kind prefix ───────────────────

    [Fact]
    public void SingleKindVariant_OmitsKindPrefixInCategory()
    {
        var baseFolder = "C:/proj/Assets/Blueprints";

        var bpInSubfolder = new FakeEditableAsset
        {
            Name = "Foo",
            Kind = AssetKind.Blueprint,
            SourceFilePath = $"{baseFolder}/AI/Foo.bp.json"
        };
        var bpAtRoot = new FakeEditableAsset
        {
            Name = "Root",
            Kind = AssetKind.Blueprint,
            SourceFilePath = $"{baseFolder}/Root.bp.json"
        };
        var catalog = new FakeAssetCatalog
        {
            All = new IEditableAsset[] { bpInSubfolder, bpAtRoot }
        };

        var source = new AssetPickerSource(
            catalog,
            AssetKindFilter.Blueprint,
            baseFolderResolver: _ => baseFolder);

        // Blueprint at subfolder "AI" → Category is "AI" (no "Blueprint/" prefix).
        var entry1 = source.ToEntry(bpInSubfolder);
        Assert.Equal("AI", entry1.Category);

        // Blueprint at root → Category is null.
        var entry2 = source.ToEntry(bpAtRoot);
        Assert.Null(entry2.Category);
    }

    // ── MTB-P8-T2-06: BuildEntries covers full query→projection ───────

    [Fact]
    public void BuildEntries_ReturnsEntryPerQueryResult()
    {
        var baseFolder = "C:/proj/Assets/Blueprints";
        var bp1 = new FakeEditableAsset
        {
            AssetId = Guid.NewGuid(),
            Name = "Alpha",
            Kind = AssetKind.Blueprint,
            SourceFilePath = $"{baseFolder}/AI/Alpha.bp.json"
        };
        var bp2 = new FakeEditableAsset
        {
            AssetId = Guid.NewGuid(),
            Name = "Beta",
            Kind = AssetKind.Blueprint,
            SourceFilePath = $"{baseFolder}/AI/Beta.bp.json"
        };
        var hsm = new FakeEditableAsset
        {
            AssetId = Guid.NewGuid(),
            Name = "Gamma",
            Kind = AssetKind.Hsm,
            SourceFilePath = "C:/proj/Assets/HSMs/Gamma.hsm"
        };
        var catalog = new FakeAssetCatalog
        {
            All = new IEditableAsset[] { bp1, bp2, hsm }
        };

        var source = new AssetPickerSource(
            catalog,
            AssetKindFilter.All,
            baseFolderResolver: kind => kind switch
            {
                AssetKind.Blueprint => "C:/proj/Assets/Blueprints",
                AssetKind.Hsm => "C:/proj/Assets/HSMs",
                _ => null
            });

        var entries = source.BuildEntries("", null);

        Assert.Equal(3, entries.Count);

        // All have expected shape.
        foreach (var entry in entries)
        {
            Assert.NotNull(entry.Id);
            Assert.NotNull(entry.Name);
            Assert.NotNull(entry.IconKey);
            Assert.NotNull(entry.Tag);
            Assert.IsType<FakeEditableAsset>(entry.Tag);
        }

        // Filtered BuildEntries.
        var filtered = source.BuildEntries("Alp", null);
        Assert.Single(filtered);
        Assert.Equal("Alpha", filtered[0].Name);
    }
}
