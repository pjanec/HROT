using System;
using System.Collections.Generic;
using System.Numerics;
using Hrot.BTree.Editor.Model;

namespace Hrot.BTree.Editor.Layout;

/// <summary>
/// Reingold-Tilford tidy-tree layout for BTree editor graphs.
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

        // Run first pass (bottom-up: compute prelim x + modifier).
        FirstPass(nodeMap);

        // Run second pass (top-down: finalize absolute x positions).
        SecondPass(nodeMap, root.VisualId, 0f, 0f);
    }

    // ---- Tree building ----

    private sealed class TreeNode
    {
        public BTreeEditorNode Editor;
        public List<TreeNode> Children = new();
        public TreeNode? Parent;
        public float PrelimX;
        public float Modifier;
        public float FinalX;
        public float FinalY;
        public int Depth;
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

    // ---- Reingold-Tilford algorithm (simplified Walker variant) ----

    private static void FirstPass(Dictionary<Guid, TreeNode> map)
    {
        // Do a post-order traversal: leaves get prelim=0, internal nodes center over children.
        foreach (var tn in map.Values)
        {
            if (tn.Parent == null)
            {
                PostOrder(tn);
                break;
            }
        }
    }

    private static void PostOrder(TreeNode node)
    {
        foreach (var child in node.Children)
            PostOrder(child);

        if (node.Children.Count == 0)
        {
            // Leaf: prelim is set by sibling separation.
            if (node.Parent != null)
            {
                int idx = node.Parent.Children.IndexOf(node);
                if (idx == 0)
                {
                    node.PrelimX = 0f;
                }
                else
                {
                    var leftSib = node.Parent.Children[idx - 1];
                    node.PrelimX = leftSib.PrelimX + NodeWidth + HorizontalSpacing;
                }
            }
        }
        else
        {
            // Internal: center over children span, but position after left sibling.
            float childrenLeft  = node.Children[0].PrelimX + node.Children[0].Modifier;
            float childrenRight = node.Children[^1].PrelimX + node.Children[^1].Modifier;
            float midpoint = (childrenLeft + childrenRight) / 2f;

            if (node.Parent != null)
            {
                int idx = node.Parent.Children.IndexOf(node);
                if (idx == 0)
                {
                    node.PrelimX = midpoint;
                }
                else
                {
                    var leftSib = node.Parent.Children[idx - 1];
                    float needed = leftSib.PrelimX + NodeWidth + HorizontalSpacing;
                    node.PrelimX = needed;
                    node.Modifier = node.PrelimX - midpoint;
                }
            }
            else
            {
                node.PrelimX = midpoint;
            }
        }
    }

    private static void SecondPass(
        Dictionary<Guid, TreeNode> map,
        Guid rootId, float modSum, float rootY)
    {
        if (!map.TryGetValue(rootId, out var node)) return;

        node.FinalX = node.PrelimX + modSum;
        node.FinalY = rootY;
        node.Editor.Position = new Vector2(node.FinalX, node.FinalY);

        // Compute y for children of this node.
        float extraY = PillRowHeight * node.PillCount;
        float childY = rootY + NodeHeaderHeight + VerticalSpacing + extraY;

        foreach (var child in node.Children)
        {
            SecondPass(map, child.Editor.VisualId, modSum + node.Modifier, childY);
        }
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
