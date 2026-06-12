using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Fbt;
using Hrot.BTree.Editor.Model;
using NodeEditor.Core.Commands;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;

namespace Hrot.BTree.Editor.Host;

/// <summary>
/// Translates NodeEditor GraphCommand records into mutations on BehaviorTreeAsset.
/// Uses the reversed-pin convention: From=child output pin, To=parent input pin.
/// </summary>
internal sealed class BTreeCommandSink : IGraphCommandSink
{
    private readonly BehaviorTreeAsset _asset;
    private readonly IGraphModel       _graph;

    // Maps link Guid -> (childVisualId, parentVisualId) for RemoveLinks lookup.
    private readonly Dictionary<Guid, (Guid child, Guid parent)> _links = new();

    internal BTreeCommandSink(BehaviorTreeAsset asset, IGraphModel graph)
    {
        _asset = asset;
        _graph = graph;
    }

    // ---- IGraphCommandSink --------------------------------------------------

    public GraphCommandResult Apply(GraphCommand command)
    {
        switch (command)
        {
            case GraphCommand.MoveNodes m:
                ApplyNodeMoves(m.Moves);
                break;

            case GraphCommand.AddNode add:
                ApplyAddNode(add);
                break;

            case GraphCommand.RemoveNodes rem:
                ApplyRemoveNodes(rem.Nodes);
                break;

            case GraphCommand.AddLink link:
                ApplyAddLink(link.AssignedId, link.From, link.To);
                break;

            case GraphCommand.RemoveLinks unlink:
                ApplyRemoveLinks(unlink.Links);
                break;

            case GraphCommand.SetNodeProperty setProp:
                ApplySetNodeProperty(setProp.Node, setProp.Key, setProp.Value);
                break;

            case GraphCommand.AddAttachment att:
                ApplyAddPill(att);
                break;

            case GraphCommand.RemoveAttachments remAtt:
                ApplyRemovePills(remAtt.AttachmentIds);
                break;

            case GraphCommand.SetAttachmentProperty setAtt:
                ApplySetPillProperty(setAtt.Id, setAtt.Key, setAtt.Value);
                break;

            case GraphCommand.ReorderAttachments reorder:
                ApplyReorderPills(reorder.HostNodeId, reorder.NewOrder);
                break;

            case GraphCommand.ChangeParentMultiple cpm:
                // Canvas always sends ChangeParentMultiple for node drops (BPF-029).
                // Reuse the existing MoveNodes path: persist NewLocalPosition for each move.
                ApplyNodeMoves(cpm.Moves.Select(m => new NodeMove(m.NodeId, m.NewLocalPosition)).ToList());
                break;

            case GraphCommand.Batch batch:
                foreach (var sub in batch.Commands)
                    Apply(sub);
                break;

            default:
                return new GraphCommandResult(false, $"Unsupported: {command.GetType().Name}");
        }

        return new GraphCommandResult(true, null);
    }

    // ---- Mutation helpers ---------------------------------------------------

    private void ApplyNodeMoves(IReadOnlyList<NodeMove> moves)
    {
        foreach (var m in moves)
        {
            var node = _asset.FindNode(m.Node.Value);
            if (node != null)
                node.Position = m.NewPosition;
        }
        _asset.MarkDirty();
    }

    private void ApplyAddNode(GraphCommand.AddNode add)
    {
        var nodeType = BTreeKinds.KindIdToNodeType(add.Kind.Id);
        var node = new BTreeEditorNode
        {
            VisualId        = add.AssignedId.Value,
            KernelType      = nodeType,
            KernelBlobIndex = -1,
            Position        = add.Position,
            DisplayLabel    = add.Kind.Id,
        };

        if (BTreeKinds.TryParseLeafActionKind(add.Kind.Id, out var fqn, out var isCond))
        {
            node.KernelType = isCond ? NodeType.Condition : NodeType.Action;
            node.DisplayLabel = fqn.Substring(fqn.LastIndexOf('.') + 1);
            if (isCond)
                node.Condition = new BTreeConditionPayload { MethodFqn = fqn };
            else
                node.Action = new BTreeActionPayload { MethodFqn = fqn };
        }

        _asset.AddNode(node);
        _asset.MarkDirty();
    }

    private void ApplyRemoveNodes(IReadOnlyList<NodeId> nodeIds)
    {
        foreach (var id in nodeIds)
        {
            // B-4 lifecycle: if the node owns an auto-managed variable (via ExpressionTargetField),
            // remove that variable before removing the node.
            // Only deletes a variable that is BOTH IsAutoManaged AND named by THIS node's field —
            // never touches a shared/hand-authored variable.
            var node = _asset.FindNode(id.Value);
            if (node is not null)
            {
                string? etf = node.Action?.ExpressionTargetField
                           ?? node.Condition?.ExpressionTargetField;
                if (!string.IsNullOrEmpty(etf))
                {
                    var varEntry = _asset.BlackboardVariables
                        .FirstOrDefault(v => v.Name == etf);
                    if (varEntry is { IsAutoManaged: true })
                        _asset.RemoveVariable(etf);
                }
            }
            _asset.RemoveNode(id.Value);
        }
        _asset.MarkDirty();
    }

    private void ApplyAddLink(LinkId linkId, PinId from, PinId to)
    {
        var fromPin = _graph.FindPin(from);
        var toPin   = _graph.FindPin(to);
        if (fromPin == null || toPin == null)
            return;

        // Reversed convention: From = child output, To = parent input.
        var childId  = fromPin.OwnerNodeId.Value;
        var parentId = toPin.OwnerNodeId.Value;

        var parent = _asset.FindNode(parentId);
        if (parent == null)
            return;

        // Self-parent is always invalid.
        if (childId == parentId)
            return;

        // Reject if attaching childId under parentId would create a cycle
        // (parentId is already in childId's subtree).
        if (SubtreeContains(childId, parentId))
            return;

        // Single-parent: detach child from any previous parent.
        foreach (var node in _asset.Nodes)
        {
            if (node != parent && node.ChildVisualIds.Contains(childId))
                node.ChildVisualIds.Remove(childId);
        }

        if (!parent.ChildVisualIds.Contains(childId))
            parent.ChildVisualIds.Add(childId);

        _links[linkId.Value] = (childId, parentId);
        _asset.MarkDirty();
    }

    /// <summary>
    /// Returns true if <paramref name="targetId"/> is reachable by following
    /// <c>ChildVisualIds</c> from <paramref name="rootId"/> (including rootId itself).
    /// Uses BFS with a visited set to guard against cycles.
    /// </summary>
    private bool SubtreeContains(Guid rootId, Guid targetId)
    {
        var visited = new HashSet<Guid>();
        var queue   = new Queue<Guid>();
        queue.Enqueue(rootId);

        while (queue.Count > 0)
        {
            var currentId = queue.Dequeue();
            if (currentId == targetId)
                return true;
            if (!visited.Add(currentId))
                continue;

            var node = _asset.FindNode(currentId);
            if (node == null)
                continue;

            foreach (var childId in node.ChildVisualIds)
            {
                if (!visited.Contains(childId))
                    queue.Enqueue(childId);
            }
        }

        return false;
    }

    private void ApplyRemoveLinks(IReadOnlyList<LinkId> linkIds)
    {
        foreach (var id in linkIds)
        {
            if (_links.TryGetValue(id.Value, out var pair))
            {
                var parent = _asset.FindNode(pair.parent);
                parent?.ChildVisualIds.Remove(pair.child);
                _links.Remove(id.Value);
            }
        }
        _asset.MarkDirty();
    }

    private void ApplySetNodeProperty(NodeId nodeId, string key, object? value)
    {
        var node = _asset.FindNode(nodeId.Value);
        if (node == null)
            return;

        switch (key)
        {
            case "comment":
                node.Comment = value as string;
                break;
            case "isBreakpoint":
                node.IsBreakpoint = value is bool b && b;
                break;
        }
        _asset.MarkDirty();
    }

    private void ApplyAddPill(GraphCommand.AddAttachment att)
    {
        if (att.HostProperties == null)
            return;
        if (!att.HostProperties.TryGetValue("decoratorType", out var dtObj))
            return;
        if (dtObj is not NodeType dt)
            return;

        var pill = new BTreeEditorPill
        {
            VisualId         = att.NewId.Value,
            HostNodeVisualId = att.HostNodeId.Value,
            DecoratorType    = dt,
            StackIndex       = att.StackIndex,
        };

        if (att.HostProperties.TryGetValue("intParam", out var ip) && ip is int intVal)
            pill.IntParam = intVal;
        if (att.HostProperties.TryGetValue("floatParam", out var fp) && fp is float floatVal)
            pill.FloatParam = floatVal;
        if (att.HostProperties.TryGetValue("comment", out var cp) && cp is string comment)
            pill.Comment = comment;

        _asset.AddPill(pill);
        _asset.MarkDirty();
    }

    private void ApplyRemovePills(IReadOnlyList<AttachmentId> ids)
    {
        foreach (var id in ids)
            _asset.RemovePill(id.Value);
        _asset.MarkDirty();
    }

    private void ApplySetPillProperty(AttachmentId id, string key, object? value)
    {
        var pill = _asset.FindPill(id.Value);
        if (pill == null)
            return;

        switch (key)
        {
            case "intParam":
                pill.IntParam = value is int i ? i : null;
                break;
            case "floatParam":
                pill.FloatParam = value is float f ? f : null;
                break;
            case "comment":
                pill.Comment = value as string;
                break;
        }
        _asset.MarkDirty();
    }

    private void ApplyReorderPills(NodeId hostNodeId, IReadOnlyList<AttachmentId> newOrder)
    {
        for (int i = 0; i < newOrder.Count; i++)
        {
            var pill = _asset.FindPill(newOrder[i].Value);
            if (pill != null && pill.HostNodeVisualId == hostNodeId.Value)
                pill.StackIndex = i;
        }
        _asset.MarkDirty();
    }
}
