using System;
using System.Linq;
using Fdp.Presentation.WindowManager;
using Xunit;

namespace Fdp.Presentation.Tests.WindowManager;

/// <summary>
/// Unit tests for <see cref="GlobalMenuRegistry"/> and <see cref="MenuItemNode"/> — WM-S301 success conditions.
/// No ImGui context required; these are pure trie-traversal logic tests.
/// </summary>
public class GlobalMenuRegistryTests
{
    // ── WM-S301 condition 1: Single-level path creates one root child ──────────

    [Fact]
    public void RegisterItem_SingleLevel_CreatesOneRootChild()
    {
        var registry = new GlobalMenuRegistry();
        registry.RegisterItem("File", () => { });

        Assert.True(registry.Root.Children.ContainsKey("File"));
        Assert.Single(registry.Root.Children);
    }

    // ── WM-S301 condition 2: Multi-level path creates full chain ──────────────

    [Fact]
    public void RegisterItem_MultiLevel_CreatesFullChain()
    {
        var registry = new GlobalMenuRegistry();
        registry.RegisterItem("Tools/Radar/Show", () => { });

        Assert.True(registry.Root.Children.ContainsKey("Tools"));

        var tools = registry.Root.Children["Tools"];
        Assert.True(tools.Children.ContainsKey("Radar"));

        var radar = tools.Children["Radar"];
        Assert.True(radar.Children.ContainsKey("Show"));
        Assert.NotNull(radar.Children["Show"].OnClick);
    }

    // ── WM-S301 condition 3: Shared parent nodes ───────────────────────────────

    [Fact]
    public void RegisterItem_TwoPathsSameParent_ShareParentNode()
    {
        var registry = new GlobalMenuRegistry();
        registry.RegisterItem("Tools/A", () => { });
        registry.RegisterItem("Tools/B", () => { });

        var tools = registry.Root.Children["Tools"];
        // Same node is shared — both children exist under one parent.
        Assert.True(tools.Children.ContainsKey("A"));
        Assert.True(tools.Children.ContainsKey("B"));
        Assert.Single(registry.Root.Children); // only one "Tools" child
    }

    // ── WM-S301 condition 4: OnClick assigned to leaf only ────────────────────

    [Fact]
    public void RegisterItem_IntermediateNodes_HaveNullOnClick()
    {
        var registry = new GlobalMenuRegistry();
        registry.RegisterItem("Tools/Radar/Show", () => { });

        var toolsNode = registry.Root.Children["Tools"];
        var radarNode = toolsNode.Children["Radar"];

        Assert.Null(toolsNode.OnClick);
        Assert.Null(radarNode.OnClick);
        Assert.NotNull(radarNode.Children["Show"].OnClick);
    }

    // ── WM-S301 condition 5: Re-registration — last write wins ────────────────

    [Fact]
    public void RegisterItem_ReRegistration_LastWriteWins()
    {
        var registry = new GlobalMenuRegistry();
        bool action1Called = false;
        bool action2Called = false;

        registry.RegisterItem("Tools/A", () => { action1Called = true; });
        registry.RegisterItem("Tools/A", () => { action2Called = true; });

        var leaf = registry.Root.Children["Tools"].Children["A"];
        leaf.OnClick!();

        Assert.False(action1Called);
        Assert.True(action2Called);
    }

    // ── WM-S301 condition 6: RegisterCheckableItem sets correct properties ─────

    [Fact]
    public void RegisterCheckableItem_SetsGetCheckedAndOnChangedOnLeaf()
    {
        var registry = new GlobalMenuRegistry();
        bool isChecked = false;
        registry.RegisterCheckableItem(
            "View/Grid",
            () => isChecked,
            value => { isChecked = value; });

        var leaf = registry.Root.Children["View"].Children["Grid"];
        Assert.NotNull(leaf.GetCheckedState);
        Assert.NotNull(leaf.OnCheckedChanged);
        Assert.Null(leaf.OnClick);
        Assert.False(leaf.IsSeparator);

        // Verify the delegates actually work.
        leaf.OnCheckedChanged!(true);
        Assert.True(leaf.GetCheckedState!());
    }

    // ── WM-S301 condition 7: RegisterSeparator sets IsSeparator = true ─────────

    [Fact]
    public void RegisterSeparator_SetsSeparatorFlag()
    {
        var registry = new GlobalMenuRegistry();
        registry.RegisterSeparator("File/---");

        var leaf = registry.Root.Children["File"].Children["---"];
        Assert.True(leaf.IsSeparator);
        Assert.Null(leaf.OnClick);
        Assert.Null(leaf.GetCheckedState);
        Assert.Null(leaf.OnCheckedChanged);
    }

    // ── WM-S301 condition 8: Empty path throws ArgumentException ──────────────

    [Fact]
    public void RegisterItem_EmptyPath_ThrowsArgumentException()
    {
        var registry = new GlobalMenuRegistry();
        Assert.Throws<ArgumentException>(() => registry.RegisterItem("", () => { }));
    }

    [Fact]
    public void RegisterItem_NullPath_ThrowsArgumentException()
    {
        var registry = new GlobalMenuRegistry();
        Assert.Throws<ArgumentException>(() => registry.RegisterItem(null!, () => { }));
    }

    // ── WM-S301 condition 9: Trailing slash handled gracefully ─────────────────

    [Fact]
    public void RegisterItem_TrailingSlash_IgnoresEmptyTrailingSegment()
    {
        var registry = new GlobalMenuRegistry();

        // "File/" splits to ["File", ""] — empty segment is skipped, so the leaf is "File".
        // This should not throw and should register at the last non-empty segment.
        registry.RegisterItem("File/", () => { });

        Assert.True(registry.Root.Children.ContainsKey("File"));
        // The leaf "File" has OnClick set (or we at minimum verify no exception and the node exists).
        var leaf = registry.Root.Children["File"];
        Assert.NotNull(leaf.OnClick);
    }

    // ── CE-1443 (U2a, AQ108 §2.15a): Order, GetVisible and SetLayout ───────────────

    /// <summary>
    /// ⭐ <c>CE-1443</c> — <c>SetLayout</c> ranks siblings whoever registers them and WHENEVER: items registered in the order C, A, B draw as A, B, C when ranked 10 / 20 / 30, a rule declared BEFORE an item exists applies when it is registered,
    /// and an unranked menu draws in registration order (every other menu is unchanged). ⛔ A rule for an item nobody registers creates no node.
    /// 🔴 Red-proofs: <c>OrderedChildren</c> returning the raw dictionary order ⇒ the ranked rows; <c>TraversePath</c> not applying the stored rule ⇒ the declared-before row; <c>SetLayout</c> creating the node ⇒ the phantom row.
    /// </summary>
    [Fact]
    public void SetLayout_RanksSiblings_WhoeverRegistersThem_AndWhenever_AndCreatesNoPhantomNode()
    {
        var registry = new GlobalMenuRegistry();
        registry.RegisterItem("File/C", () => { });
        registry.SetLayout("File/A", 10);                  // declared BEFORE the node exists
        registry.SetLayout("File/B", 20);
        registry.SetLayout("File/C", 30);                  // declared AFTER: applied to the existing node
        registry.SetLayout("File/Nobody", 40);             // nobody registers it
        registry.RegisterItem("File/B", () => { });
        registry.RegisterItem("File/A", () => { });

        Assert.Equal(new[] { "A", "B", "C" }, registry.Root.Children["File"].OrderedChildren().Select(c => c.Name).ToArray());
        Assert.False(registry.Root.Children["File"].Children.ContainsKey("Nobody"));

        var plain = new GlobalMenuRegistry();
        plain.RegisterItem("Tools/Z", () => { });
        plain.RegisterItem("Tools/Y", () => { });
        Assert.Equal(new[] { "Z", "Y" }, plain.Root.Children["Tools"].OrderedChildren().Select(c => c.Name).ToArray());   // unranked: registration order, as always
    }

    /// <summary>
    /// ⭐ <c>CE-1443</c> — <c>GetVisible</c> is part of the ONE visibility test: <c>Offers</c> is false while the node says hidden (and true again when it says visible), the published model (<c>BuildViewModel</c>) agrees, and the Start surface's
    /// idle list never offers a hidden item. A rule set through <c>SetLayout</c> hides the same way. 🔴 Red-proof: <c>Offers</c> without the visibility term ⇒ the first assertion; <c>AllowedInIdleItems</c> without it ⇒ the idle row.
    /// </summary>
    [Fact]
    public void GetVisible_IsPartOfTheOneVisibilityTest_ForTheBarTheModelAndTheStartSurface()
    {
        var registry = new GlobalMenuRegistry();
        bool shown = false;
        registry.RegisterItem("File/Always", () => { }, allowedInIdle: true);
        registry.RegisterItem("File/Edit me", () => { }, allowedInIdle: true);
        registry.SetLayout("File/Edit me", 5, () => shown);

        var node = registry.Root.Children["File"].Children["Edit me"];
        Assert.False(registry.Offers(node, null));
        Assert.True(registry.Offers(registry.Root.Children["File"].Children["Always"], null));
        Assert.DoesNotContain(registry.AllowedInIdleItems(), a => a.Path == "File/Edit me");
        Assert.False(registry.BuildViewModel("Scenario").Items.Single(i => i.Path == "File/Edit me").Visible);

        shown = true;
        Assert.True(registry.Offers(node, null));
        Assert.Contains(registry.AllowedInIdleItems(), a => a.Path == "File/Edit me");
        Assert.True(registry.BuildViewModel("Scenario").Items.Single(i => i.Path == "File/Edit me").Visible);
        Assert.Equal(new[] { "File/Always", "File/Edit me" }, registry.AllowedInIdleItems().Select(a => a.Path).ToArray());   // unranked (0) first, then rank 5: the Start surface walks the same order
    }
}
