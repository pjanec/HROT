using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Hrot.BTree.Editor.Model;

namespace Hrot.BTree.Editor.Layout;

/// <summary>
/// Tidy-tree layout for BTree editor graphs (⭐ CE-1273: subtree-width placement — every subtree gets the span its leaves need).
/// Positions nodes in a downward-growing tree (root at top, children below).
///
/// Parameters (from BTH §3.4):
///   Horizontal sibling spacing: 40 px
///   Vertical parent-to-child spacing: 80 px (+ 23 px per pill row on the child)
///   Root centered at (0, 0)
/// </summary>
public static class BTreeAutoLayout
{
    // Layout constants
    private const float HorizontalSpacing   = 40f;
    private const float VerticalSpacing     = 80f;
    private const float NodeWidth           = 160f; // estimated node width for spacing
    private const float PillRowHeight       = 23f;
    private const float NodeHeaderHeight    = 24f;

    /// <summary>
    /// Runs tidy-tree layout on the given asset starting from the root node.
    /// Writes computed positions directly to each BTreeEditorNode.Position.
    /// Only processes nodes that are reachable from the root.
    /// </summary>
    public static void Layout(BehaviorTreeAsset asset)
    {
        var root = FindRoot(asset);
        if (root == null) return;

        // Build the tree structure for RT algorithm.
        var nodeMap = BuildNodeMap(asset, root);
        if (nodeMap == null) return;

        // ⭐ CE-1273 — SUBTREE-WIDTH placement: every subtree gets a horizontal span as wide as all its leaves need, its children side by
        //   side inside it, the parent centred over them. Overlap-free by construction. ⛔ SUPERSEDED: the "simplified Walker" passes below
        //   spaced a node one node-width after its left sibling, ignoring how wide the sibling's SUBTREE was — measured on the C#-built
        //   PersonAttack: 48 nodes in 42 places (whole branches drawn on top of each other).
        var top = nodeMap[root.VisualId];
        Width(top);
        Place(top, 0f, 0f);
    }

    // ⭐ CE-1344 — a node is as wide as its TITLE (the canvas draws it so: CanvasLayout's text width + 48, at least 160), and a parent's
    //   many LEAF children are STAGGERED over layers instead of one long row. 🔒 User, 2026-10-25: "the btree auto-layout for csharp only stuff
    //   keeps placing long child nodes on a single line so they overlap heavily … placing the child nodes to different vertical 'layer' to
    //   avoid overlaps." ⛔ SUPERSEDED (CE-1273): every node counted as 160 wide, so long titles ran into their neighbours.
    private const float CharWidth      = 7.5f;   // the editor font's average advance (13 px)
    private const float TitlePadding   = 48f;    // CanvasLayout: title width + 48
    private const float LayerStep      = 70f;    // between two staggered layers of leaves
    private const float MinStagger     = 70f;    // neighbouring leaves on different layers: at least this far apart

    /// <summary>How wide the canvas draws <paramref name="node"/> (estimated from its title, as CanvasLayout measures it).</summary>
    public static float WidthOf(BTreeEditorNode node)
    {
        string title = string.IsNullOrEmpty(node.DisplayLabel) ? node.KernelType.ToString() : node.DisplayLabel;
        return MathF.Max(NodeWidth, title.Length * CharWidth + TitlePadding);
    }

    /// <summary>How many layers a parent's children are spread over: one, or 2–3 when they are all leaves and more than three.</summary>
    private static int LayersOf(TreeNode node)
        => node.Children.Count <= 3 || node.Children.Any(c => c.Children.Count > 0) ? 1 : node.Children.Count <= 8 ? 2 : 3;

    private static float Width(TreeNode node)
    {
        node.OwnWidth = WidthOf(node.Editor);
        if (node.Children.Count == 0) return node.SpanWidth = node.OwnWidth;
        foreach (var child in node.Children) Width(child);
        int layers = LayersOf(node);
        node.Layers = layers;
        float block;
        if (layers == 1)
            block = node.Children.Sum(c => c.SpanWidth) + HorizontalSpacing * (node.Children.Count - 1);
        else
        {
            // staggered: each leaf clears the one before it on ITS layer, and sits at least MinStagger past its neighbour
            var x = new float[node.Children.Count];
            for (int i = 1; i < x.Length; i++)
            {
                x[i] = x[i - 1] + MinStagger;
                if (i >= layers) x[i] = MathF.Max(x[i], x[i - layers] + node.Children[i - layers].SpanWidth + HorizontalSpacing);
            }
            for (int i = 0; i < x.Length; i++) node.Children[i].Offset = x[i];
            block = node.Children.Select((c, i) => x[i] + c.SpanWidth).Max();
        }
        node.BlockWidth = block;
        return node.SpanWidth = MathF.Max(node.OwnWidth, block);
    }

    /// <summary>Places <paramref name="node"/>'s subtree in the span starting at <paramref name="left"/>, its top at <paramref name="y"/>.</summary>
    private static void Place(TreeNode node, float left, float y)
    {
        float childY = y + NodeHeaderHeight + VerticalSpacing + PillRowHeight * node.PillCount;
        float x = left + (node.SpanWidth - node.BlockWidth) / 2f;
        for (int i = 0; i < node.Children.Count; i++)
        {
            var child = node.Children[i];
            if (node.Layers == 1)
            {
                Place(child, x, childY);
                x += child.SpanWidth + HorizontalSpacing;
            }
            else Place(child, left + (node.SpanWidth - node.BlockWidth) / 2f + child.Offset, childY + (i % node.Layers) * LayerStep);
        }
        // centred over its children (or its own span when it has none); Position is the node's left edge, as before
        float centre = node.Children.Count == 0
            ? left + node.SpanWidth / 2f
            : (Centre(node.Children[0]) + Centre(node.Children[^1])) / 2f;
        node.FinalX = centre - node.OwnWidth / 2f;
        node.FinalY = y;
        node.Editor.Position = new Vector2(node.FinalX, node.FinalY);
    }

    private static float Centre(TreeNode n) => n.FinalX + n.OwnWidth / 2f;

    // ---- Tree building ----

    private sealed class TreeNode
    {
        public BTreeEditorNode Editor;
        public List<TreeNode> Children = new();
        public TreeNode? Parent;
        public float FinalX;
        public float FinalY;
        public int Depth;
        public float SpanWidth;   // ⭐ CE-1273 — the width this node's whole subtree needs
        public float OwnWidth;    // ⭐ CE-1344 — the node's own drawn width (its title)
        public float BlockWidth;  // its children's row (or staggered block)
        public float Offset;      // a staggered leaf's x inside its parent's block
        public int Layers = 1;    // how many layers its children are spread over
        // Number of pill rows stacked above this node (affects vertical offset of children).
        public int PillCount;

        public TreeNode(BTreeEditorNode editor) { Editor = editor; }
    }

    private static BTreeEditorNode? FindRoot(BehaviorTreeAsset asset)
    {
        // The root is the node with Kind matching root type — but kind is a NodeKindKey.
        // In the editor model, we detect root by NodeType == NodeType.Root.
        foreach (var node in asset.Nodes)
        {
            if (node.KernelType == Fbt.NodeType.Root)
                return node;
        }
        // ⭐ CE-1273 — a C#-BUILT tree (BTreeBuilder.Compile) has no Root node: its first node IS the top. The layout used to give up here,
        //   so every node kept (0, 0) — the user, 2026-10-23: "all decompiled tree nodes on one single place, no layout whatsoever".
        //   The top is the node nobody lists as a child (the first such, in projection order).
        var children = new HashSet<Guid>();
        foreach (var node in asset.Nodes)
            foreach (var child in node.ChildVisualIds) children.Add(child);
        foreach (var node in asset.Nodes)
        {
            if (!children.Contains(node.VisualId))
                return node;
        }
        return null;
    }

    private static Dictionary<Guid, TreeNode>? BuildNodeMap(
        BehaviorTreeAsset asset, BTreeEditorNode root)
    {
        var map = new Dictionary<Guid, TreeNode>();

        // First pass: create TreeNode wrappers.
        foreach (var node in asset.Nodes)
            map[node.VisualId] = new TreeNode(node);

        // Second pass: wire parent-child relationships.
        foreach (var node in asset.Nodes)
        {
            if (!map.TryGetValue(node.VisualId, out var tn)) continue;
            foreach (var childId in node.ChildVisualIds)
            {
                if (!map.TryGetValue(childId, out var childTn)) continue;
                tn.Children.Add(childTn);
                childTn.Parent = tn;
            }
        }

        // Assign depths and pill counts.
        AssignDepths(map[root.VisualId], 0);

        return map;
    }

    private static void AssignDepths(TreeNode node, int depth)
    {
        node.Depth = depth;
        // Count pills decorating this node.
        node.PillCount = 0; // pills counted separately if needed
        foreach (var child in node.Children)
            AssignDepths(child, depth + 1);
    }

    // ---- Center the whole tree around root ----

    /// <summary>
    /// Runs tidy-tree layout, then shifts all node positions so that the
    /// root node is at canvas origin (0, 0).
    /// </summary>
    public static void LayoutCentered(BehaviorTreeAsset asset)
    {
        Layout(asset);

        var root = FindRoot(asset);
        if (root == null || asset.Nodes.Count == 0) return;

        float offsetX = -root.Position.X;
        float offsetY = -root.Position.Y;

        foreach (var node in asset.Nodes)
            node.Position = new Vector2(node.Position.X + offsetX, node.Position.Y + offsetY);
    }
}
