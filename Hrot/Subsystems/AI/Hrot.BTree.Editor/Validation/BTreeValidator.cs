using System;
using System.Collections.Generic;
using System.Linq;
using Fbt;
using Hrot.BTree.Editor.Model;
using Hrot.Editor.AiShared.Catalog;
using Hrot.Editor.AiShared.References;

namespace Hrot.BTree.Editor.Validation;

/// <summary>
/// Validates a BehaviorTreeAsset and returns a list of diagnostics.
/// Implements the rules from BTH §11.1.
/// Rules that require external context (BlackboardFieldMissing, MethodSignatureMismatch) are
/// deferred to Slice 2 and emit no diagnostics here.
/// <see cref="BTreeDiagnosticCode.DanglingReferenceAfterReload"/> (Phase C / AIE-053) is now
/// implemented for composed AiPrimitive nodes: see <see cref="CheckDanglingBlueprintReferences"/>.
/// It requires an <see cref="IAssetCatalog"/> — when <see cref="Validate"/> is called without one
/// (the historical single-arg overload), this rule is skipped exactly as before.
/// </summary>
public sealed class BTreeValidator
{
    private const int MaxAllowedDepth = 8;

    /// <summary>
    /// Validates <paramref name="asset"/>. When <paramref name="catalog"/> is supplied, also runs
    /// the dangling composed-Blueprint-reference check (requires resolving other assets, so it is
    /// opt-in via this parameter rather than always-on).
    /// </summary>
    public IReadOnlyList<BTreeDiagnostic> Validate(BehaviorTreeAsset asset, IAssetCatalog? catalog = null)
    {
        var diagnostics = new List<BTreeDiagnostic>();

        CheckComposites(asset, diagnostics);
        CheckLeaves(asset, diagnostics);
        CheckPills(asset, diagnostics);
        CheckDepth(asset, diagnostics);
        CheckCycles(asset, diagnostics);
        CheckOrphanedNodes(asset, diagnostics);
        CheckNestedDecorators(asset, diagnostics);
        if (catalog != null)
        {
            CheckDanglingBlueprintReferences(asset, catalog, diagnostics);
            CheckSubtreeAssetCycles(asset, catalog, diagnostics);
        }

        return diagnostics;
    }

    /// <summary>
    /// ⭐⭐⭐ <b><c>E5</c> item 7 — <c>SubtreeAssetCycle</c>: this asset hosts a sub-tree that hosts
    /// this asset again.</b> 📄 <c>DESIGN_Occurrence_Scoped_Storage.md</c> §32.16.
    ///
    /// <para>⛔⛔ <b>NOT the same rule as <see cref="CheckCycles"/>, and the distinction is the point.</b>
    /// That one walks <c>ChildVisualIds</c> — the NODE graph inside this one asset. This one walks the
    /// ASSET graph through <c>ISubtreeHostingAsset</c>. 📐 A tree can be perfectly acyclic by the first
    /// rule while hosting a sub-tree that hosts it back.</para>
    ///
    /// <para>⭐⭐ <b>This is the arm that can fire on a shipped asset today</b> — <c>BTreeSubtreePayload</c>
    /// is authored and persisted, where the HSM side still has no authoring gesture.</para>
    ///
    /// <para>⭐⭐ <b>It targets THE SUBTREE NODE ON THE RING</b> — the node whose <c>SubtreeAssetId</c>
    /// is the ring's next hop, i.e. the edge a designer can cut HERE. 🔴 The first cut targeted
    /// <c>Guid.Empty</c> always, reasoning that <i>"for <c>A→B→A</c> opened at <c>B</c>, no node of
    /// <c>B</c> is at fault."</i> ⛔ Wrong — a ring has no innocent edge. ⚠ When this asset is only on
    /// the APPROACH path (<c>A→B→C→B</c> validated from <c>A</c>) nothing here IS at fault, and the
    /// diagnostic correctly falls back to tree-level.</para>
    ///
    /// <para>⚠ The walk is shared with <c>HsmValidator</c> so the two cannot drift into two answers for
    /// one question (ruling 9).</para>
    /// </summary>
    private static void CheckSubtreeAssetCycles(
        BehaviorTreeAsset asset, IAssetCatalog catalog, List<BTreeDiagnostic> out_)
    {
        var cycle = Hrot.Editor.AiShared.SubtreeCycleDetector.FindCycleFrom(catalog, asset.AssetId);
        if (cycle.Count == 0) return;

        // ⭐ The edge to cut, if this asset sits on the ring at all.
        var nextHop = Hrot.Editor.AiShared.SubtreeCycleDetector.NextHopInRing(cycle, asset.AssetId);
        var target  = Guid.Empty;
        if (nextHop is not null)
        {
            foreach (var n in asset.Nodes)
            {
                if (n.Subtree?.SubtreeAssetId == nextHop.Value) { target = n.VisualId; break; }
            }
        }

        out_.Add(new BTreeDiagnostic(
            target,
            BTreeDiagnosticSeverity.Error,
            BTreeDiagnosticCode.SubtreeAssetCycle,
            $"Sub-tree hosting forms a cycle: " +
            $"{Hrot.Editor.AiShared.SubtreeCycleDetector.DescribeCycle(catalog, cycle)}. " +
            $"Hosted sub-trees are expanded inline every tick, so a cycle recurses without a base case."));
    }

    // ---- Rule implementations -----------------------------------------------

    // Rule 1: Sequence / Selector / ObserverSelector with zero children.
    // Rule 2: Action with empty MethodFqn.
    // Rule 3: Condition with empty MethodFqn.
    // Rule 6: Subtree with IsResolved == false.
    // Rule 5: Wait with Duration <= 0.
    private static void CheckComposites(BehaviorTreeAsset asset, List<BTreeDiagnostic> out_)
    {
        foreach (var node in asset.Nodes)
        {
            if ((node.KernelType == NodeType.Sequence  ||
                 node.KernelType == NodeType.Selector  ||
                 node.KernelType == NodeType.ObserverSelector) &&
                node.ChildVisualIds.Count == 0)
            {
                out_.Add(new BTreeDiagnostic(
                    node.VisualId,
                    BTreeDiagnosticSeverity.Warning,
                    BTreeDiagnosticCode.EmptyComposite,
                    $"{node.KernelType} has no children."));
            }
        }
    }

    private static void CheckLeaves(BehaviorTreeAsset asset, List<BTreeDiagnostic> out_)
    {
        foreach (var node in asset.Nodes)
        {
            switch (node.KernelType)
            {
                case NodeType.Action:
                    if (node.Action == null || string.IsNullOrEmpty(node.Action.MethodFqn))
                    {
                        out_.Add(new BTreeDiagnostic(
                            node.VisualId,
                            BTreeDiagnosticSeverity.Error,
                            BTreeDiagnosticCode.UnboundActionMethod,
                            "Action node has no bound method."));
                    }
                    break;

                case NodeType.Condition:
                    if (node.Condition == null || string.IsNullOrEmpty(node.Condition.MethodFqn))
                    {
                        out_.Add(new BTreeDiagnostic(
                            node.VisualId,
                            BTreeDiagnosticSeverity.Error,
                            BTreeDiagnosticCode.UnboundConditionMethod,
                            "Condition node has no bound method."));
                    }
                    break;

                case NodeType.Wait:
                    if (node.Wait != null && node.Wait.Duration <= 0f)
                    {
                        out_.Add(new BTreeDiagnostic(
                            node.VisualId,
                            BTreeDiagnosticSeverity.Warning,
                            BTreeDiagnosticCode.WaitDurationInvalid,
                            $"Wait duration must be > 0 (got {node.Wait.Duration})."));
                    }
                    break;

                case NodeType.Subtree:
                    if (node.Subtree != null && !node.Subtree.IsResolved)
                    {
                        out_.Add(new BTreeDiagnostic(
                            node.VisualId,
                            BTreeDiagnosticSeverity.Error,
                            BTreeDiagnosticCode.UnresolvedSubtree,
                            $"Subtree '{node.Subtree.SubtreeName}' could not be resolved."));
                    }
                    break;
            }
        }
    }

    // Phase C (AIE-053): a composed AiPrimitive node (Action or Condition with
    // DelegateShape == AiPrimitiveTickCore) whose MethodFqn no longer resolves to any Blueprint
    // asset in the catalog — the blueprint was renamed or deleted after the node was composed.
    // Identity is by FQN (see ComposedBlueprintResolver), never by a persisted AssetId.
    private static void CheckDanglingBlueprintReferences(
        BehaviorTreeAsset asset, IAssetCatalog catalog, List<BTreeDiagnostic> out_)
    {
        foreach (var node in asset.Nodes)
        {
            string? methodFqn = node.KernelType switch
            {
                NodeType.Action when node.Action?.DelegateShape == BTreeActionDelegateShape.AiPrimitiveTickCore
                    => node.Action.MethodFqn,
                NodeType.Condition when node.Condition?.DelegateShape == BTreeActionDelegateShape.AiPrimitiveTickCore
                    => node.Condition.MethodFqn,
                _ => null,
            };
            if (methodFqn is null)
                continue;

            // Not a composed-AiPrimitive-shaped FQN at all (shouldn't happen given the DelegateShape
            // guard above, but Resolve/TryParse already handles it defensively) — nothing to flag.
            if (!ComposedBlueprintResolver.TryParse(methodFqn, out _, out _))
                continue;

            if (ComposedBlueprintResolver.Resolve(methodFqn, catalog) != null)
                continue; // resolves cleanly — no diagnostic.

            out_.Add(new BTreeDiagnostic(
                node.VisualId,
                BTreeDiagnosticSeverity.Error,
                BTreeDiagnosticCode.DanglingReferenceAfterReload,
                $"Node '{node.DisplayLabel}' references blueprint '{methodFqn}' which no longer exists — reselect or remove."));
        }
    }

    // Rule 4: Repeater pill with Count (IntParam) <= 0.
    private static void CheckPills(BehaviorTreeAsset asset, List<BTreeDiagnostic> out_)
    {
        foreach (var pill in asset.Pills)
        {
            if (pill.DecoratorType == NodeType.Repeater)
            {
                int count = pill.IntParam ?? 0;
                if (count <= 0)
                {
                    out_.Add(new BTreeDiagnostic(
                        pill.VisualId,
                        BTreeDiagnosticSeverity.Warning,
                        BTreeDiagnosticCode.RepeaterCountInvalid,
                        $"Repeater count must be >= 1 (got {count})."));
                }
            }
        }
    }

    // Rule 7: Static nesting depth > MaxAllowedDepth levels.
    private static void CheckDepth(BehaviorTreeAsset asset, List<BTreeDiagnostic> out_)
    {
        var root = FindRoot(asset);
        if (root == null) return;

        int maxDepth = 0;
        var visited = new HashSet<Guid>();
        WalkDepth(asset, root, 0, ref maxDepth, visited);

        if (maxDepth > MaxAllowedDepth)
        {
            out_.Add(new BTreeDiagnostic(
                Guid.Empty,
                BTreeDiagnosticSeverity.Warning,
                BTreeDiagnosticCode.StackDepthExceeded,
                $"Tree static depth {maxDepth} exceeds allowed maximum of {MaxAllowedDepth}."));
        }
    }

    private static void WalkDepth(
        BehaviorTreeAsset asset, BTreeEditorNode node,
        int currentDepth, ref int maxDepth, HashSet<Guid> visited)
    {
        // Guard against cycles so we do not recurse infinitely.
        if (!visited.Add(node.VisualId)) return;

        if (currentDepth > maxDepth)
            maxDepth = currentDepth;

        foreach (var childId in node.ChildVisualIds)
        {
            var child = asset.FindNode(childId);
            if (child != null)
                WalkDepth(asset, child, currentDepth + 1, ref maxDepth, visited);
        }
    }

    // Rule 11: Cycle detected (defensive).
    private static void CheckCycles(BehaviorTreeAsset asset, List<BTreeDiagnostic> out_)
    {
        var root = FindRoot(asset);
        if (root == null) return;

        var visited    = new HashSet<Guid>();
        var inProgress = new HashSet<Guid>();

        if (DetectCycle(asset, root, visited, inProgress))
        {
            out_.Add(new BTreeDiagnostic(
                Guid.Empty,
                BTreeDiagnosticSeverity.Error,
                BTreeDiagnosticCode.CycleDetected,
                "A cycle was detected in the behavior tree graph."));
        }
    }

    private static bool DetectCycle(
        BehaviorTreeAsset asset, BTreeEditorNode node,
        HashSet<Guid> visited, HashSet<Guid> inProgress)
    {
        if (inProgress.Contains(node.VisualId)) return true;
        if (visited.Contains(node.VisualId))    return false;

        inProgress.Add(node.VisualId);

        foreach (var childId in node.ChildVisualIds)
        {
            var child = asset.FindNode(childId);
            if (child != null && DetectCycle(asset, child, visited, inProgress))
                return true;
        }

        inProgress.Remove(node.VisualId);
        visited.Add(node.VisualId);
        return false;
    }

    // Rule 12: Orphaned nodes — not reachable from Root.
    private static void CheckOrphanedNodes(BehaviorTreeAsset asset, List<BTreeDiagnostic> out_)
    {
        var root = FindRoot(asset);
        if (root == null) return;

        var reachable = new HashSet<Guid>();
        CollectReachable(asset, root, reachable);

        foreach (var node in asset.Nodes)
        {
            if (!reachable.Contains(node.VisualId))
            {
                out_.Add(new BTreeDiagnostic(
                    node.VisualId,
                    BTreeDiagnosticSeverity.Warning,
                    BTreeDiagnosticCode.OrphanedNode,
                    $"Node '{node.VisualId}' is not reachable from the root."));
            }
        }
    }

    private static void CollectReachable(
        BehaviorTreeAsset asset, BTreeEditorNode node,
        HashSet<Guid> visited)
    {
        if (!visited.Add(node.VisualId)) return; // already visited (guards against cycles)

        foreach (var childId in node.ChildVisualIds)
        {
            var child = asset.FindNode(childId);
            if (child != null)
                CollectReachable(asset, child, visited);
        }
    }

    // Rule NestedRepeater / NestedParallel (kernel-illegal nesting, DEC-06 Part 3):
    // Walk the tree from root.  A Repeater pill on a node makes that node and its
    // entire subtree "inside Repeater"; two Repeater pills on one node count as
    // nested by themselves.  A Parallel node sets insideParallel for its subtree.
    private static void CheckNestedDecorators(BehaviorTreeAsset asset, List<BTreeDiagnostic> out_)
    {
        var root = FindRoot(asset);
        if (root == null) return;

        var visited = new HashSet<Guid>();
        WalkNestedDecorators(asset, root, insideRepeater: false, insideParallel: false, out_, visited);
    }

    private static void WalkNestedDecorators(
        BehaviorTreeAsset asset,
        BTreeEditorNode   node,
        bool              insideRepeater,
        bool              insideParallel,
        List<BTreeDiagnostic> out_,
        HashSet<Guid>     visited)
    {
        if (!visited.Add(node.VisualId)) return; // cycle guard (CheckCycles will report it separately)

        // Count Repeater pills on this node.
        var nodePills = asset.Pills
            .Where(p => p.HostNodeVisualId == node.VisualId)
            .OrderBy(p => p.StackIndex)
            .ToList();

        bool nodeBecomesInsideRepeater = insideRepeater;
        bool nodeBecomesInsideParallel = insideParallel;

        foreach (var pill in nodePills)
        {
            if (pill.DecoratorType == NodeType.Repeater)
            {
                if (nodeBecomesInsideRepeater)
                {
                    // Already inside Repeater — this pill is a nested Repeater.
                    out_.Add(new BTreeDiagnostic(
                        pill.VisualId,
                        BTreeDiagnosticSeverity.Error,
                        BTreeDiagnosticCode.NestedRepeater,
                        "Nested Repeater decorator is not allowed (kernel-illegal)."));
                }
                else
                {
                    nodeBecomesInsideRepeater = true;
                }
            }
        }

        // A Parallel node sets insideParallel for its children.
        if (node.KernelType == NodeType.Parallel)
        {
            if (nodeBecomesInsideParallel)
            {
                out_.Add(new BTreeDiagnostic(
                    node.VisualId,
                    BTreeDiagnosticSeverity.Error,
                    BTreeDiagnosticCode.NestedParallel,
                    "Nested Parallel node is not allowed (kernel-illegal)."));
            }
            else
            {
                nodeBecomesInsideParallel = true;
            }
        }

        foreach (var childId in node.ChildVisualIds)
        {
            var child = asset.FindNode(childId);
            if (child != null)
                WalkNestedDecorators(asset, child, nodeBecomesInsideRepeater, nodeBecomesInsideParallel, out_, visited);
        }
    }

    // ---- Helpers ------------------------------------------------------------

    private static BTreeEditorNode? FindRoot(BehaviorTreeAsset asset)
    {
        foreach (var node in asset.Nodes)
        {
            if (node.KernelType == NodeType.Root)
                return node;
        }
        return null;
    }
}
