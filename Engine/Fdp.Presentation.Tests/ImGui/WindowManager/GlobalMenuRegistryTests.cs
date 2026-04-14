using System;
using Fdp.Toolkit.ImGui.WindowManager;
using Xunit;

namespace Fdp.Toolkit.ImGui.Tests.WindowManager;

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
}
