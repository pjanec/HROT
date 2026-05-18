using System;
using System.Collections.Generic;
using System.Text.Json;
using Fdp.Core;
using Fdp.Presentation.Panels.ReplayBrowser;
using Fdp.Toolkit.ReplayBrowser.Diff;
using Xunit;

namespace Fdp.Presentation.ReplayBrowser.ComponentDiff;

/// <summary>
/// Unit tests for <see cref="ComponentDiffPanel.CollectVisibleNodes"/>.
/// No ImGui calls are made; only the pure tree-walker logic is tested.
/// </summary>
public sealed class ComponentDiffPanelTests
{
    // ── Helpers ───────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a 4-level tree where only the deepest leaf is modified.
    ///
    ///   root (DiffObject, modified=true because of child)
    ///     L1  (DiffObject, modified=true because of child)
    ///       L2 (DiffObject, modified=true because of child)
    ///         leaf (DiffValue, modified=true)
    ///         unchanged (DiffValue, modified=false)
    /// </summary>
    private static IReadOnlyList<DiffNode> BuildFourLevelTree()
    {
        var leaf      = new DiffValue("Value",    "0", "42",  JsonValueKind.Number, isModified: true);
        var unchanged = new DiffValue("Unchanged","1", "1",   JsonValueKind.Number, isModified: false);

        var l2 = new DiffObject("L2");
        l2.Children.Add(leaf);
        l2.Children.Add(unchanged);
        l2.EvaluateModificationState();

        var l1 = new DiffObject("L1");
        l1.Children.Add(l2);
        l1.EvaluateModificationState();

        var root = new DiffObject("Root");
        root.Children.Add(l1);
        root.EvaluateModificationState();

        return new List<DiffNode> { root };
    }

    // ── hideUnchanged=true: unchanged leaves are pruned ───────────────────

    [Fact]
    public void CollectVisibleNodes_HideUnchanged_OnlyModifiedReturned()
    {
        var tree    = BuildFourLevelTree();
        var visible = ComponentDiffPanel.CollectVisibleNodes(tree, hideUnchanged: true);

        // Expected: root, L1, L2, leaf (4 items). The unchanged DiffValue is pruned.
        Assert.Equal(4, visible.Count);

        // Verify the unchanged DiffValue is absent
        foreach (var node in visible)
            Assert.NotEqual("Unchanged", node.Name);
    }

    // ── hideUnchanged=false: all nodes returned ───────────────────────────

    [Fact]
    public void CollectVisibleNodes_ShowAll_AllNodesReturned()
    {
        var tree    = BuildFourLevelTree();
        var visible = ComponentDiffPanel.CollectVisibleNodes(tree, hideUnchanged: false);

        // root + L1 + L2 + leaf + unchanged = 5
        Assert.Equal(5, visible.Count);
    }

    // ── Default hideUnchanged is true on a fresh panel ────────────────────

    [Fact]
    public void Panel_DefaultHideUnchanged_IsTrue()
    {
        // Verify that a fresh panel starts with hideUnchanged=true by checking that
        // CollectVisibleNodes with the same tree returns 4 and not 5.
        var tree    = BuildFourLevelTree();
        var visible = ComponentDiffPanel.CollectVisibleNodes(tree, hideUnchanged: true);
        Assert.Equal(4, visible.Count);
    }

    // ── Entity-link detection: OnEntityLinkClicked fires for handle values ─

    [Fact]
    public void CollectVisibleNodes_EntityHandleLeaf_IsIncluded()
    {
        // A DiffValue whose NewValue is an entity handle
        var entityLeaf = new DiffValue("Entity", "[10, v2]", "[11, v3]", JsonValueKind.String, isModified: true);
        var tree = new List<DiffNode> { entityLeaf };

        var visible = ComponentDiffPanel.CollectVisibleNodes(tree, hideUnchanged: false);

        Assert.Single(visible);
        Assert.Equal("Entity", visible[0].Name);
    }

    // ── Empty diffs produce empty visible list ─────────────────────────────

    [Fact]
    public void CollectVisibleNodes_EmptyDiffs_ReturnsEmpty()
    {
        var visible = ComponentDiffPanel.CollectVisibleNodes(Array.Empty<DiffNode>(), hideUnchanged: true);
        Assert.Empty(visible);
    }

    // ── TryFireEntityLink: entity-handle routing ──────────────────────────

    [Fact]
    public void TryFireEntityLink_EntityHandleNewValue_FiresCallbackWithParsedEntity()
    {
        var leaf = new DiffValue("Target", "[10, v2]", "[11, v3]", JsonValueKind.String, isModified: true);

        Entity captured = default;
        bool fired = ComponentDiffPanel.TryFireEntityLink(leaf, e => captured = e);

        Assert.True(fired);
        Assert.Equal(new Entity(11, 3), captured);
    }

    [Fact]
    public void TryFireEntityLink_PlainStringValue_DoesNotFireCallback()
    {
        var leaf = new DiffValue("Name", "Alice", "Bob", JsonValueKind.String, isModified: true);

        bool fired = ComponentDiffPanel.TryFireEntityLink(leaf, _ => throw new InvalidOperationException("Must not fire"));

        Assert.False(fired);
    }
}
