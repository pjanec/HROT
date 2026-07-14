using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Fbt;
using Hrot.BTree.Editor.Model;
using Hrot.Editor.AiShared.Blackboard;
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
    private readonly BehaviorTreeAsset     _asset;
    private readonly IGraphModel           _graph;
    private readonly IActionSchemaExporter? _actionSchema;

    // Maps link Guid -> (childVisualId, parentVisualId) for RemoveLinks lookup.
    private readonly Dictionary<Guid, (Guid child, Guid parent)> _links = new();

    /// <param name="actionSchema">
    /// E2: optional schema exporter used to detect Blueprint-compiled AiPrimitive actions when
    /// placing a node from the palette (<see cref="ApplyAddNode"/>). Null in call sites/tests that
    /// don't care about AiPrimitive composition — the non-AiPrimitive placement path is unaffected.
    /// </param>
    internal BTreeCommandSink(BehaviorTreeAsset asset, IGraphModel graph, IActionSchemaExporter? actionSchema = null)
    {
        _asset        = asset;
        _graph        = graph;
        _actionSchema = actionSchema;
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

            case GraphCommand.InsertReroute insertReroute:
                ApplyInsertReroute(insertReroute.Link, insertReroute.Position);
                break;

            case GraphCommand.MoveReroute moveReroute:
                ApplyMoveReroute(moveReroute.Link, moveReroute.WaypointIndex, moveReroute.NewPosition);
                break;

            case GraphCommand.RemoveReroute removeReroute:
                ApplyRemoveReroute(removeReroute.Link, removeReroute.WaypointIndex);
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
            {
                node.Condition = new BTreeConditionPayload { MethodFqn = fqn };
            }
            else
            {
                var action = new BTreeActionPayload { MethodFqn = fqn };
                node.Action = action;

                // E2: a Blueprint-compiled AiPrimitive action must be placed as a fully composed
                // host-BTree node (T31 shape) — not the bare MethodFqn-only payload above. Detect
                // via the schema entry (not a new kind prefix): schema entries with IsAiPrimitive
                // are discovered from [Fbt.Kernel.GeneratedAiPrimitiveAction] on the generated
                // TickCore (see ActionSchemaExporter). Non-AiPrimitive actions fall through
                // unchanged (byte-identical to the pre-E2 behavior).
                var entry = _actionSchema?.Lookup(fqn);
                if (entry is { IsAiPrimitive: true })
                    ComposeAiPrimitiveAction(action, entry);
            }
        }
        else
        {
            // Give the node a human-readable title (not the raw kind id like
            // "bt.leaf.wait") and initialize the kind-specific payload so the node
            // is valid + editable on creation. A Wait created without a payload has
            // a null Wait -> uneditable Duration and an error (red) frame.
            node.DisplayLabel = FriendlyLabel(nodeType);
            switch (nodeType)
            {
                case NodeType.Wait:
                    node.Wait = new BTreeWaitPayload { Duration = 1f };
                    break;
                case NodeType.Subtree:
                    node.Subtree = new BTreeSubtreePayload();
                    break;
            }
        }

        // Canvas drag-to-create pre-generates pin IDs and bakes them into the
        // auto-wire link. Adopt them so the link's pins resolve to this node;
        // otherwise the node is created but the wire is silently dropped.
        AdoptPreGeneratedPinIds(node, add.InitialProperties);

        _asset.AddNode(node);
        _asset.MarkDirty();
    }

    /// <summary>
    /// E2: composes a placed Action node onto a Blueprint-compiled AiPrimitive (T31 shape).
    /// Sets <see cref="BTreeActionDelegateShape.AiPrimitiveTickCore"/>, derives the generated
    /// WorkingState type FQN from the schema entry's Params type, and auto-creates a blackboard
    /// variable (mirroring the "Promote to new variable" IsAutoManaged convention — see
    /// <see cref="ApplyRemoveNodes"/>) to hold the Params, wiring it up via ExpressionTargetField.
    /// </summary>
    private void ComposeAiPrimitiveAction(BTreeActionPayload action, ActionSchemaEntry entry)
    {
        action.DelegateShape = BTreeActionDelegateShape.AiPrimitiveTickCore;

        // The generated class nests both Params (entry.DtoType) and WorkingState as sibling
        // struct types; derive the WorkingState FQN from Params' declaring type. Left null (node
        // still placed) if the generated shape doesn't match — never throws.
        action.WorkingStateTypeId = entry.DtoType.DeclaringType?.GetNestedType("WorkingState")?.FullName;

        string varName = GenerateUniqueVariableName("bpParams");
        _asset.AddVariable(new BlackboardVariableEntry(
            varName, entry.DtoType, Comment: null, IsAutoManaged: true));

        action.ExpressionTargetField = varName;
    }

    /// <summary>Returns baseName if unused, else baseName_2, baseName_3, … — first unused wins.</summary>
    private string GenerateUniqueVariableName(string baseName)
    {
        if (_asset.BlackboardVariables.All(v => v.Name != baseName))
            return baseName;

        int suffix = 2;
        string candidate;
        do
        {
            candidate = $"{baseName}_{suffix}";
            suffix++;
        } while (_asset.BlackboardVariables.Any(v => v.Name == candidate));

        return candidate;
    }

    /// <summary>Human-readable default title for a freshly-created node of the given kind.</summary>
    private static string FriendlyLabel(NodeType type) => type switch
    {
        NodeType.Root             => "Root",
        NodeType.Sequence         => "Sequence",
        NodeType.Selector         => "Selector",
        NodeType.Parallel         => "Parallel",
        NodeType.ObserverSelector => "Observer Selector",
        NodeType.Wait             => "Wait",
        NodeType.Subtree          => "Subtree",
        NodeType.Action           => "Action",
        NodeType.Condition        => "Condition",
        _                         => type.ToString(),
    };

    /// <summary>
    /// Maps the canvas-supplied <c>PinIds</c> list onto the node's input/output pins,
    /// mirroring the catalog entry's pin order ([inputs…, outputs…]). A node declares
    /// an input pin unless it is a leaf or decorator; it declares an output pin unless
    /// it is the Root or a decorator — matching <c>BTreeNodeCatalog</c>.
    /// </summary>
    private static void AdoptPreGeneratedPinIds(
        BTreeEditorNode node, IReadOnlyDictionary<string, object?>? props)
    {
        if (props == null ||
            !props.TryGetValue("PinIds", out var raw) ||
            raw is not IReadOnlyList<PinId> pinIds ||
            pinIds.Count == 0)
            return;

        bool declaresInput  = !node.IsLeaf && !node.IsDecorator;
        bool declaresOutput = node.KernelType != NodeType.Root && !node.IsDecorator;

        int idx = 0;
        Guid? inputOverride = null, outputOverride = null;
        if (declaresInput  && idx < pinIds.Count) inputOverride  = pinIds[idx++].Value;
        if (declaresOutput && idx < pinIds.Count) outputOverride = pinIds[idx++].Value;

        node.SetExplicitPinIds(outputOverride, inputOverride);
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

        // Reversed convention: child links via its Output pin, parent via its Input.
        // Resolve roles by pin DIRECTION, not by From/To order: a drag can start on
        // either endpoint (e.g. from a parent's bottom Input pin), so From is not
        // necessarily the child's output. Reject same-direction (invalid) drags.
        if (fromPin.Direction == toPin.Direction)
            return;

        var childPin  = fromPin.Direction == PinDirection.Output ? fromPin : toPin;
        var parentPin = fromPin.Direction == PinDirection.Output ? toPin   : fromPin;
        var childId   = childPin.OwnerNodeId.Value;
        var parentId  = parentPin.OwnerNodeId.Value;

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
            // Resolve via the graph model first — works for both projected
            // (JSON-loaded) and session-added links.
            var link = _graph.FindLink(id);
            if (link != null)
            {
                var fromPin = _graph.FindPin(link.FromPin);
                var toPin   = _graph.FindPin(link.ToPin);
                if (fromPin != null && toPin != null)
                {
                    var childId  = fromPin.OwnerNodeId.Value;
                    var parentId = toPin.OwnerNodeId.Value;
                    _asset.FindNode(parentId)?.ChildVisualIds.Remove(childId);
                }
            }
            else if (_links.TryGetValue(id.Value, out var pair))
            {
                // Fallback: session-only lookup (defensive).
                var parent = _asset.FindNode(pair.parent);
                parent?.ChildVisualIds.Remove(pair.child);
            }
            _links.Remove(id.Value);
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

        NodeType dt;
        if (att.HostProperties.TryGetValue("decoratorType", out var dtObj) && dtObj is NodeType existingDt)
        {
            // Existing programmatic/test path — keep working.
            dt = existingDt;
        }
        else if (att.HostProperties.TryGetValue(AttachmentHostPropertyKeys.Kind, out var kindObj)
                 && kindObj is string kindStr
                 && BTreeKinds.IsDecorator(new NodeKindKey(kindStr)))
        {
            // Picker path: resolve from paletteKind.
            dt = BTreeKinds.KindIdToNodeType(kindStr);
        }
        else
        {
            // Non-decorator kind or missing props — safe no-op.
            return;
        }

        // DEC-06 Part 4 (L3 defense-in-depth): refuse to stack a second Repeater pill
        // on the same host node.  The context-menu already disables the item, but this
        // guard catches any programmatic path (redo, paste, etc.).
        if (dt == NodeType.Repeater)
        {
            bool alreadyHasRepeater = _asset.Pills.Any(
                p => p.HostNodeVisualId == att.HostNodeId.Value &&
                     p.DecoratorType   == NodeType.Repeater);
            if (alreadyHasRepeater)
                return;
        }

        var pill = new BTreeEditorPill
        {
            VisualId         = att.NewId.Value,
            HostNodeVisualId = att.HostNodeId.Value,
            DecoratorType    = dt,
            StackIndex       = att.StackIndex,
        };

        if (att.HostProperties.TryGetValue("intParam", out var ip) && ip is int intVal)
            pill.IntParam = intVal;
        else if (dt == NodeType.Repeater)
            pill.IntParam = 1;

        if (att.HostProperties.TryGetValue("floatParam", out var fp) && fp is float floatVal)
            pill.FloatParam = floatVal;
        else if (dt == NodeType.Cooldown)
            pill.FloatParam = 1f;

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

    private void ApplyInsertReroute(LinkId linkId, Vector2 position)
    {
        var childVisualId = BTreeParentChildLink.ChildVisualIdFromLinkId(linkId);
        var node = _asset.FindNode(childVisualId);
        if (node == null) return;
        node.Waypoints.Add(position);
        _asset.MarkDirty();
    }

    private void ApplyMoveReroute(LinkId linkId, int index, Vector2 newPosition)
    {
        var childVisualId = BTreeParentChildLink.ChildVisualIdFromLinkId(linkId);
        var node = _asset.FindNode(childVisualId);
        if (node == null) return;
        if (index < 0 || index >= node.Waypoints.Count) return;
        node.Waypoints[index] = newPosition;
        _asset.MarkDirty();
    }

    private void ApplyRemoveReroute(LinkId linkId, int index)
    {
        var childVisualId = BTreeParentChildLink.ChildVisualIdFromLinkId(linkId);
        var node = _asset.FindNode(childVisualId);
        if (node == null) return;
        if (index < 0 || index >= node.Waypoints.Count) return;
        node.Waypoints.RemoveAt(index);
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
