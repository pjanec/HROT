using Fdp.Toolkit.Orchestration;
using FluentAssertions;
using Hrot.AiEditor.Persistence;
using Xunit;

namespace Hrot.AiEditor.Generators.Tests.Generator;

/// <summary>
/// CE-3340 (4) - the generators' picker-group rule (<c>BehaviorGroups</c>, netstandard2.0) and the runtime path authority (<c>ContentLayout</c>, net8.0) spell the behaviour kind roots
/// from ONE file (<c>ContentTreeNames</c>, LINKED into the Persistence project - the earlier claim "the analyzer assembly cannot reference ContentLayout" was true of a
/// project REFERENCE only; the project already shares <c>OccurrenceSlotKey</c>, <c>HsmActionKey</c> ... by link). The rail drives <c>BehaviorGroups</c> with paths MINTED BY
/// <c>ContentLayout</c>, so a literal that drifts from the layout (a kind folder renamed, the tree moved) reddens it.
/// Red proof: <c>BehaviorGroups</c>' prefix hand-spelled as <c>"asset/behaviors/BTrees/"</c> => every row returns <c>null</c>.
/// </summary>
public sealed class BehaviorGroupsSpellTheContentLayoutTests
{
    [Theory]
    [InlineData(ContentLayout.BTreesFolder)]
    [InlineData(ContentLayout.HsmsFolder)]
    [InlineData(ContentLayout.BlueprintsFolder)]
    public void AnAssetUnderTheLayoutsKindRoot_GetsItsFolderAsTheGroup(string kindFolder)
    {
        var root = ContentLayout.BehaviorAssets("/repo", kindFolder);   // what the layout says the kind's root is

        BehaviorGroups.FromAssetPath(root + "/Demos/Urban/Ambush.json").Should().Be("Demos/Urban");
        BehaviorGroups.FromAssetPath(root.Replace('/', '\\') + "\\Demos\\Ambush.json").Should().Be("Demos");
        BehaviorGroups.FromAssetPath(root + "/RootLevel.json").Should().BeNull("a file at the kind root has no group");
    }

    /// <summary>A path under <c>recipes/</c> (not an asset root) or under <c>assets/behaviors/Params</c> (not a group-bearing kind) gets no group.</summary>
    [Fact]
    public void ARecipeOrAParamsFile_GetsNoGroup()
    {
        BehaviorGroups.FromAssetPath(ContentLayout.BehaviorRecipes("/repo", ContentLayout.BTreesFolder) + "/Demos/x.json").Should().BeNull();
        BehaviorGroups.FromAssetPath(ContentLayout.BehaviorAssets("/repo", ContentLayout.ParamsFolder) + "/Demos/x.json").Should().BeNull();
    }
}
