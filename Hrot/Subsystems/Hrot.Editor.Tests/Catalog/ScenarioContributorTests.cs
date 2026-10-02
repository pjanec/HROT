using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Catalog;

namespace Hrot.Editor.Tests.Catalog;

/// <summary>
/// Tests for <see cref="ScenarioCatalogContributor"/> (MTB-P5-T2).
/// </summary>
public sealed class ScenarioContributorTests
{
    // ── Helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Creates a contributor backed by a mutable list. Changes to the list
    /// are reflected via the source delegate on the next Enumerate/Refresh call.
    /// </summary>
    private static (ScenarioCatalogContributor Contributor, List<string> Source)
        CreateContributor(IReadOnlyList<string>? initial = null)
    {
        var source = initial != null ? new List<string>(initial) : new List<string>();
        var contributor = new ScenarioCatalogContributor(() => source);
        return (contributor, source);
    }

    // ── Kind ─────────────────────────────────────────────────────────

    [Fact]
    public void Kind_IsScenario()
    {
        var (contributor, _) = CreateContributor();
        Assert.Equal(AssetKind.Scenario, contributor.Kind);
    }

    // ── BaseFolder ───────────────────────────────────────────────────

    [Fact]
    public void BaseFolder_IsNull()
    {
        var (contributor, _) = CreateContributor();
        Assert.Null(contributor.BaseFolder);
    }

    // ── Enumerate ────────────────────────────────────────────────────

    [Fact]
    public void Enumerate_EmptyList_ReturnsEmpty()
    {
        var (contributor, _) = CreateContributor();
        var assets = contributor.Enumerate();
        Assert.Empty(assets);
    }

    [Fact]
    public void Enumerate_OneAssetPerScenario_NameIsRelPath()
    {
        var scenarios = new List<string>
        {
            "alpha",
            "campaign/beta",
            "campaign/sub/gamma",
        };
        var (contributor, _) = CreateContributor(scenarios);

        var assets = contributor.Enumerate();
        Assert.Equal(3, assets.Count);

        // Names are the verbatim relative paths.
        Assert.Contains(assets, a => a.Name == "alpha");
        Assert.Contains(assets, a => a.Name == "campaign/beta");
        Assert.Contains(assets, a => a.Name == "campaign/sub/gamma");

        foreach (var asset in assets)
        {
            Assert.Equal(AssetKind.Scenario, asset.Kind);
            Assert.Equal("", asset.SourceFilePath);
            Assert.False(asset.IsEditorOwned);
            Assert.False(asset.IsDirty);
        }
    }

    [Fact]
    public void Enumerate_AssetIds_AreStable()
    {
        var scenarios = new[] { "alpha", "campaign/beta" };
        var (contributor, source) = CreateContributor(scenarios);

        var first  = contributor.Enumerate();
        var second = contributor.Enumerate();

        Assert.Equal(first.Count, second.Count);
        for (int i = 0; i < first.Count; i++)
        {
            Assert.Equal(first[i].AssetId, second[i].AssetId);
            Assert.Equal(first[i].Name, second[i].Name);
        }
    }

    [Fact]
    public void Enumerate_AssetIds_AreDeterministic()
    {
        // Same name → same AssetId, even across different contributor instances.
        var source1 = new List<string> { "alpha" };
        var source2 = new List<string> { "alpha" };
        var c1 = new ScenarioCatalogContributor(() => source1);
        var c2 = new ScenarioCatalogContributor(() => source2);

        var id1 = c1.Enumerate()[0].AssetId;
        var id2 = c2.Enumerate()[0].AssetId;

        Assert.Equal(id1, id2);
    }

    [Fact]
    public void Enumerate_DifferentNames_DifferentAssetIds()
    {
        var source1 = new List<string> { "alpha" };
        var source2 = new List<string> { "beta" };
        var c1 = new ScenarioCatalogContributor(() => source1);
        var c2 = new ScenarioCatalogContributor(() => source2);

        var id1 = c1.Enumerate()[0].AssetId;
        var id2 = c2.Enumerate()[0].AssetId;

        Assert.NotEqual(id1, id2);
    }

    // ── ContributorChanged ───────────────────────────────────────────

    [Fact]
    public void ContributorChanged_FiresOnListChange()
    {
        var (contributor, source) = CreateContributor(new[] { "alpha" });
        contributor.Enumerate(); // seed the last-known list

        var fired = false;
        contributor.ContributorChanged += () => fired = true;

        // Change the underlying list and refresh.
        source.Add("beta");
        contributor.Refresh();

        Assert.True(fired, "ContributorChanged should fire when the list changes.");
    }

    [Fact]
    public void ContributorChanged_NoEventWhenListUnchanged()
    {
        var (contributor, source) = CreateContributor(new[] { "alpha" });
        contributor.Enumerate(); // seed the last-known list

        var fired = false;
        contributor.ContributorChanged += () => fired = true;

        // Refresh without changing the list.
        contributor.Refresh();

        Assert.False(fired, "ContributorChanged should NOT fire when the list is unchanged.");
    }

    [Fact]
    public void ContributorChanged_FiresOnListShrink()
    {
        var (contributor, source) = CreateContributor(new[] { "alpha", "beta" });
        contributor.Enumerate(); // seed

        var fired = false;
        contributor.ContributorChanged += () => fired = true;

        source.RemoveAt(1); // remove "beta"
        contributor.Refresh();

        Assert.True(fired, "ContributorChanged should fire when the list shrinks.");
    }

    [Fact]
    public void ContributorChanged_FiresOnOrderChange()
    {
        var (contributor, source) = CreateContributor(new[] { "alpha", "beta" });
        contributor.Enumerate(); // seed

        var fired = false;
        contributor.ContributorChanged += () => fired = true;

        // Reverse the list.
        source.Reverse();
        contributor.Refresh();

        Assert.True(fired, "ContributorChanged should fire when the list order changes.");
    }

    // ── Assigned assets ──────────────────────────────────────────────

    [Fact]
    public void EachEnumeratedAsset_HasExpectedKind()
    {
        var (contributor, _) = CreateContributor(new[] { "scenarioA" });
        var assets = contributor.Enumerate();

        Assert.Single(assets);
        Assert.Equal(AssetKind.Scenario, assets[0].Kind);
    }

    [Fact]
    public void Enumerate_AcceptsSlashInName()
    {
        var (contributor, _) = CreateContributor(new[] { "deep/nested/path" });
        var assets = contributor.Enumerate();

        Assert.Single(assets);
        Assert.Equal("deep/nested/path", assets[0].Name);
    }
}
