using System;
using System.Collections.Generic;
using System.Numerics;
using Fbt;
using Hrot.BTree.Editor.Layout;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Layout;

namespace Hrot.BTree.Editor.Model;

/// <summary>
/// Projects a BehaviorTreeBlob + optional debug metadata + optional layout
/// into a BehaviorTreeAsset editor model.
/// </summary>
internal static class BehaviorTreeAssetProjector
{
    public static BehaviorTreeAsset Project(
        BehaviorTreeBlob blob,
        NodeDebugMetadata[]? debugMetadata,
        BTreeEditorLayout? layout,
        Guid assetId,
        string name,
        string sourceFilePath,
        bool isEditorOwned,
        string blackboardTypeName,
        string contextTypeName,
        string targetNamespace = "",
        IReadOnlyList<Fdp.Toolkit.Behavior.ManagedBlackboardVariable>? variables = null)
    {
        var asset = new BehaviorTreeAsset(
            assetId, name, sourceFilePath, isEditorOwned,
            blackboardTypeName, contextTypeName, blob, targetNamespace);

        var nodes = new List<BTreeEditorNode>();
        var pills = new List<BTreeEditorPill>();
        var byId  = new Dictionary<Guid, BTreeEditorNode>();

        if (blob.Nodes.Length > 0)
        {
            var pending = new List<(int blobIndex, NodeType type, int payloadIndex, Guid visualId)>();
            VisitNode(blob, 0, null, pending, nodes, pills, debugMetadata, byId);
        }

        asset.ReplaceAll(nodes, pills, blob);

        if (layout != null)
        {
            foreach (var node in nodes)
            {
                if (layout.Nodes.TryGetValue(node.VisualId, out var entry))
                {
                    node.Position = entry.Position;
                    if (entry.Comment != null)
                        node.Comment = entry.Comment;
                }
                if (layout.LinkWaypoints.TryGetValue(node.VisualId, out var waypoints))
                {
                    node.Waypoints.Clear();
                    node.Waypoints.AddRange(waypoints);
                }
            }
            asset.CanvasPanOffset = layout.PanOffset;
            asset.CanvasZoomLevel = layout.ZoomLevel > 0f ? layout.ZoomLevel : 1f;
        }
        else
        {
            BTreeAutoLayout.Layout(asset);
            asset.IsAutoLaidOut = true;   // ⭐ CE-1360 — redone with the real font when a document opens it
        }

        asset.LoadSyncBindings(layout?.SyncBindings);

        if (layout?.BlackboardConflictSuppressions != null)
        {
            foreach (var kvp in layout.BlackboardConflictSuppressions)
            {
                asset.SetConflictSuppressed(kvp.VariableName, kvp.WriterPairKey, true);
            }
        }

        if (layout?.UnusedWarningSuppressions != null)
        {
            foreach (var variableName in layout.UnusedWarningSuppressions)
            {
                asset.SetUnusedWarningSuppressed(variableName, true);
            }
        }

        // ⭐ W7b (§9.4) -- per-VARIABLE "allow concurrent writes".
        if (layout?.ConcurrentWritesAllowed != null)
        {
            foreach (var variableName in layout.ConcurrentWritesAllowed)
            {
                asset.SetConcurrentWritesAllowed(variableName, true);
            }
        }

        // ⭐ CE-1430 — the blackboard variables of a C#-built tree ARE its published manifest (SharedNodeBinder.PublishedVariablesOf, the
        //   list the registry holds as ManagedBlackboardVariables): one row per manifest entry, its name and type, in manifest order — so
        //   the Variables window, the Details rows and the Watch ask the live provider for exactly the names it answers. ⛔ No second list.
        //   No default / comment: the C# block's constructor is the default and the code its documentation (read-only — CE-1252).
        if (variables is { Count: > 0 })
        {
            bool wasDirty = asset.IsDirty;
            var entries = new List<Hrot.Editor.AiShared.Blackboard.BlackboardVariableEntry>(variables.Count);
            foreach (var v in variables)
                entries.Add(new Hrot.Editor.AiShared.Blackboard.BlackboardVariableEntry(v.Name, v.Type, Comment: null));
            asset.SetBlackboardVariables(entries);
            if (!wasDirty) asset.ClearDirty();   // a projection is not an edit
        }

        return asset;
    }

    // ---- DFS traversal ----

    private static bool IsDecorator(NodeType type) =>
        type == NodeType.Inverter     ||
        type == NodeType.Repeater     ||
        type == NodeType.Cooldown     ||
        type == NodeType.ForceSuccess ||
        type == NodeType.ForceFailure ||
        type == NodeType.UntilSuccess ||
        type == NodeType.UntilFailure;

    private static Guid MintVisualId(int index, NodeDebugMetadata[]? meta)
    {
        if (meta != null && index < meta.Length)
        {
            string raw = meta[index].VisualId;
            if (!string.IsNullOrEmpty(raw) && Guid.TryParse(raw, out var parsed))
                return parsed;
        }
        return Guid.NewGuid();
    }

    private static void VisitNode(
        BehaviorTreeBlob blob,
        int index,
        Guid? parentVisualId,
        List<(int blobIndex, NodeType type, int payloadIndex, Guid visualId)> pendingDecorators,
        List<BTreeEditorNode> nodes,
        List<BTreeEditorPill> pills,
        NodeDebugMetadata[]? meta,
        Dictionary<Guid, BTreeEditorNode> byId)
    {
        var nodeDef  = blob.Nodes[index];
        var visualId = MintVisualId(index, meta);

        if (IsDecorator(nodeDef.Type))
        {
            // Collect and recurse into the single child.
            pendingDecorators.Add((index, nodeDef.Type, nodeDef.PayloadIndex, visualId));
            VisitNode(blob, index + 1, parentVisualId, pendingDecorators, nodes, pills, meta, byId);
            return;
        }

        // Non-decorator: create the editor node.
        string label   = (meta != null && index < meta.Length) ? meta[index].Label         : string.Empty;
        string comment = (meta != null && index < meta.Length) ? meta[index].CustomComment  : string.Empty;

        var editorNode = new BTreeEditorNode
        {
            VisualId       = visualId,
            KernelType     = nodeDef.Type,
            KernelBlobIndex = index,
            DisplayLabel   = label,
            Comment        = string.IsNullOrEmpty(comment) ? null : comment,
        };

        switch (nodeDef.Type)
        {
            case NodeType.Action:
                editorNode.Action        = new BehaviorActionBinding { MethodFqn = blob.MethodNames[nodeDef.PayloadIndex] };
                // ⭐ CE-504 slice 4 — a compiled blob carries the node's key, not its variable binding, so the projection
                //   binds no variable (it was FourParamFull, the retired whole-block shape, for the same reason).
                editorNode.DelegateShape = BTreeActionDelegateShape.NoParams;
                break;
            case NodeType.Condition:
                editorNode.Condition = new BehaviorActionBinding { MethodFqn = blob.MethodNames[nodeDef.PayloadIndex] };
                break;
            case NodeType.Wait:
                editorNode.Wait = new BTreeWaitPayload
                {
                    Duration = blob.FloatParams[nodeDef.PayloadIndex],
                };
                break;
            case NodeType.Parallel:
                // ⭐ CE-2073 — the compiler keeps a Parallel's policy in IntParams (TreeCompiler), so a compiled tree
                //   projects with the policy it was authored with.
                if (nodeDef.PayloadIndex >= 0 && nodeDef.PayloadIndex < blob.IntParams.Length)
                    editorNode.ParallelPolicy = blob.IntParams[nodeDef.PayloadIndex];
                break;
            case NodeType.Subtree:
                editorNode.Subtree = new BTreeSubtreePayload
                {
                    SubtreeName = blob.SubtreeAssetIds[nodeDef.PayloadIndex],
                    IsResolved  = false,
                };
                break;
        }

        // ⭐ CE-1360 (§3.4) — a leaf whose label is its registry KEY (every C#-built tree's: "Ns.Type.Method@24@936") shows the method's
        //   name in words instead; the key stays its binding (MethodFqn). An authored label is kept as it is.
        string? leafKey = editorNode.Action?.MethodFqn ?? editorNode.Condition?.MethodFqn;
        if (!string.IsNullOrEmpty(leafKey) && BTreeNodeTitles.IsGenerated(label, leafKey))
            editorNode.DisplayLabel = BTreeNodeTitles.For(leafKey, condition: editorNode.Condition != null,
                Fdp.Toolkit.Behavior.SharedNodeBinder.ParamFieldOf(leafKey));

        nodes.Add(editorNode);
        byId[visualId] = editorNode;

        // Flush pending decorators as pills.
        int pendingCount = pendingDecorators.Count;
        for (int i = 0; i < pendingCount; i++)
        {
            var (decBlobIndex, decType, decPayloadIndex, decVisualId) = pendingDecorators[i];
            var pill = new BTreeEditorPill
            {
                VisualId       = decVisualId,
                HostNodeVisualId = visualId,
                DecoratorType  = decType,
                // pendingDecorators[0] is outermost => StackIndex = pendingCount - 1
                StackIndex     = pendingCount - 1 - i,
            };

            switch (decType)
            {
                case NodeType.Repeater:
                    pill.IntParam   = blob.IntParams[decPayloadIndex];
                    break;
                case NodeType.Cooldown:
                    pill.FloatParam = blob.FloatParams[decPayloadIndex];
                    break;
            }

            pills.Add(pill);
        }
        pendingDecorators.Clear();

        // Attach to parent composite node.
        if (parentVisualId.HasValue && byId.TryGetValue(parentVisualId.Value, out var parentNode))
            parentNode.ChildVisualIds.Add(visualId);

        // Recurse into children with a fresh pending list for each branch.
        int childIndex = index + 1;
        for (int c = 0; c < nodeDef.ChildCount; c++)
        {
            var childPending = new List<(int, NodeType, int, Guid)>();
            VisitNode(blob, childIndex, visualId, childPending, nodes, pills, meta, byId);
            childIndex += blob.Nodes[childIndex].SubtreeOffset;
        }
    }
}
