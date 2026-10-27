using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Hrot.BTree.Editor.Model;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;
using NodeEditor.UI.Canvas;

namespace Hrot.BTree.Editor.Layout;

/// <summary>
/// Tidy-tree layout for BTree editor graphs (⭐ CE-1273: subtree-width placement — every subtree gets the span its leaves need).
/// Positions nodes in a downward-growing tree (root at top, children below).
///
/// Parameters (from BTH §3.4):
///   Horizontal sibling spacing: 40 px
///   Vertical clearance between a parent's bottom edge and the pills / top of its children: 60 px (⭐ CE-1367: the node's real drawn height and
///   its pills' real height — <see cref="NodeSizing"/>, the canvas's own sizing — replace the old 24 + 80 + 23-per-pill-row estimate)
///   Root centered at (0, 0)
/// </summary>
public static class BTreeAutoLayout
{
    // Layout constants
    private const float HorizontalSpacing   = 40f;
    private const float VerticalClearance   = 60f;  // between a parent's drawn bottom and the pills / top of its children
    private const float NodeWidth           = 160f; // the canvas's minimum node width (CanvasLayoutBuilder.NodeMinWidthGu)
    /// <summary>The canvas theme's <c>NodeHeaderHeight</c> when no theme is at hand (the engine editor theme's value).</summary>
    public const float DefaultHeaderHeight  = 28f;

    /// <summary>
    /// Runs tidy-tree layout on the given asset starting from the root node.
    /// Writes computed positions directly to each BTreeEditorNode.Position.
    /// Only processes nodes that are reachable from the root.
    /// </summary>
    public static void Layout(BehaviorTreeAsset asset) => Layout(asset, null);

    /// <summary>⭐ <c>CE-1360</c> — the same, measuring each title with <paramref name="measureTitle"/> (the canvas font's text width) instead of
    /// the per-character estimate: the document factory passes ImGui's, so a node is placed at the width it is drawn at.</summary>
    public static void Layout(BehaviorTreeAsset asset, Func<string, float>? measureTitle, float headerHeight = DefaultHeaderHeight)
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
        var pillsOf = asset.Pills.GroupBy(p => p.HostNodeVisualId).ToDictionary(g => g.Key, g => (IReadOnlyList<IAttachmentModel>)g.Select(p => (IAttachmentModel)new BTreePillAttachmentModel(p)).ToList());
        Width(top, new Sizer(measureTitle, headerHeight, pillsOf));
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

    /// <summary>What the canvas draws for a node — ⭐ CE-1367: asked of <see cref="NodeSizing"/>, the implementation <c>CanvasLayoutBuilder</c> itself
    /// sizes every node with (title width, pin widths, header + pin rows, the pills above), not a second estimate.</summary>
    private sealed class Sizer
    {
        private readonly Func<string, float> _measure;
        private readonly float _header;
        private readonly Dictionary<Guid, IReadOnlyList<IAttachmentModel>> _pills;
        public Sizer(Func<string, float>? measureTitle, float header, Dictionary<Guid, IReadOnlyList<IAttachmentModel>> pills)
        { _measure = measureTitle ?? (t => t.Length * CharWidth); _header = header; _pills = pills; }

        public NodeSizing.Drawn Of(BTreeEditorNode node)
            => NodeSizing.Measure(new BTreeNodeModel(node, NodeState.Normal, null),
                _pills.TryGetValue(node.VisualId, out var p) ? p : Array.Empty<IAttachmentModel>(),
                PinOrientation.Vertical, _header, _measure, _ => 0f);
    }

    /// <summary>How wide the canvas draws <paramref name="node"/> (its body, from its title, as <see cref="NodeSizing"/> measures it).</summary>
    public static float WidthOf(BTreeEditorNode node, Func<string, float>? measureTitle = null)
        => new Sizer(measureTitle, DefaultHeaderHeight, new()).Of(node).Width;

    /// <summary>How many layers a parent's children are spread over: one, or 2–3 when there are more than three and each is SMALL — a
    /// leaf, or (⭐ CE-1360) a node whose own children are all leaves (the common <c>Sequence(IsOption, Action)</c> pair).</summary>
    private static int LayersOf(TreeNode node)
        => node.Children.Count <= 3 || node.Children.Any(c => c.Children.Any(g => g.Children.Count > 0)) ? 1
         : node.Children.Count <= 8 ? 2 : 3;

    // ⭐ CE-1360 — a staggered layer is as tall as the tallest subtree in the row, so a subtree on the second layer starts below
    //   everything the first layer draws. ⭐ CE-1367 — "as tall" now includes the pills above the row's nodes.
    private const float LayerGap = 20f;   // clear space between two staggered layers

    private static float Width(TreeNode node, Sizer sizer)
    {
        var drawn = sizer.Of(node.Editor);
        node.OwnWidth = drawn.Width;
        node.BodyHeight = drawn.Height;
        node.PillsAbove = drawn.PillsAbove;
        node.TotalWidth = drawn.TotalWidth;
        if (node.Children.Count == 0) { node.Height = node.BodyHeight; return node.SpanWidth = node.TotalWidth; }
        foreach (var child in node.Children) Width(child, sizer);
        int layers = LayersOf(node);
        node.Layers = layers;
        node.ChildPillsAbove = node.Children.Max(c => c.PillsAbove);
        node.LayerStep = layers == 1 ? 0f : MathF.Max(LayerStep, node.Children.Max(c => c.Height) + node.ChildPillsAbove + LayerGap);
        float below = node.Children.Select((c, i) => c.Height + (i % layers) * node.LayerStep).Max();
        node.Height = node.BodyHeight + VerticalClearance + node.ChildPillsAbove + below;
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
        return node.SpanWidth = MathF.Max(node.TotalWidth, block);
    }

    /// <summary>Places <paramref name="node"/>'s subtree in the span starting at <paramref name="left"/>, its top at <paramref name="y"/>.</summary>
    private static void Place(TreeNode node, float left, float y)
    {
        // the children's tops: below this node's drawn bottom, the clearance, and the tallest pill stack among them (the pills sit ABOVE a node)
        float childY = y + node.BodyHeight + VerticalClearance + node.ChildPillsAbove;
        float x = left + (node.SpanWidth - node.BlockWidth) / 2f;
        for (int i = 0; i < node.Children.Count; i++)
        {
            var child = node.Children[i];
            if (node.Layers == 1)
            {
                Place(child, x, childY);
                x += child.SpanWidth + HorizontalSpacing;
            }
            else Place(child, left + (node.SpanWidth - node.BlockWidth) / 2f + child.Offset, childY + (i % node.Layers) * node.LayerStep);
        }
        // centred over its children (or its own span when it has none); Position is the node's left edge, as before.
        // ⭐ CE-1367 — kept inside its span even when its pills are wider than its body (they extend right from the left edge)
        float centre = node.Children.Count == 0
            ? left + node.SpanWidth / 2f
            : (Centre(node.Children[0]) + Centre(node.Children[^1])) / 2f;
        node.FinalX = MathF.Max(left, MathF.Min(centre - node.OwnWidth / 2f, left + node.SpanWidth - node.TotalWidth));
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
        public float LayerStep;   // ⭐ CE-1360 — between two of its children's layers (the tallest child subtree, plus a gap)
        public float Height;      // ⭐ CE-1360 — how tall its whole subtree draws (from its top edge down)
        public float BodyHeight;  // ⭐ CE-1367 — the node's own drawn height (header + pin padding)
        public float PillsAbove;  // ⭐ CE-1367 — how far its decorator pills reach ABOVE it (0 without pills)
        public float TotalWidth;  // ⭐ CE-1367 — its width including a pill row wider than its body
        public float ChildPillsAbove; // the tallest pill stack among its children

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
