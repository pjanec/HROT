using NodeEditor.Core.Commands;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;
using System.Numerics;

namespace NodeEditor.Demo.FakeBlueprint;

/// <summary>Applies graph commands to the mutable FakeGraphModel.</summary>
public sealed class FakeCommandSink : IGraphCommandSink
{
    private readonly FakeGraphModel _graph;
    private readonly FakeNodeCatalog _catalog;
    private readonly ITypeSystem _typeSystem;

    public FakeCommandSink(FakeGraphModel graph, FakeNodeCatalog catalog, ITypeSystem typeSystem)
    {
        _graph   = graph;
        _catalog = catalog;
        _typeSystem = typeSystem;
    }

    public GraphCommandResult Apply(GraphCommand command)
    {
        switch (command)
        {
            case GraphCommand.AddNode add:
                ApplyAddNode(add);
                _graph.NotifyChanged(GraphChangeKind.NodesAdded);
                return new GraphCommandResult(true, null);

            case GraphCommand.RemoveNodes remove:
                // Remove links that reference removed nodes first
                var linkIds = _graph.Links
                    .Where(l => remove.Nodes.Any(nid =>
                        _graph.FindPin(l.FromPin)?.OwnerNodeId == nid ||
                        _graph.FindPin(l.ToPin)?.OwnerNodeId   == nid))
                    .Select(l => l.Id)
                    .ToList();
                foreach (var lid in linkIds) _graph.RemoveLink(lid);
                foreach (var nid in remove.Nodes) _graph.RemoveNode(nid);
                _graph.NotifyChanged(GraphChangeKind.NodesRemoved);
                return new GraphCommandResult(true, null);

            case GraphCommand.MoveNodes move:
                foreach (var m in move.Moves) _graph.SetNodePosition(m.Node, m.NewPosition);
                _graph.NotifyChanged(GraphChangeKind.NodesMoved);
                return new GraphCommandResult(true, null);

            case GraphCommand.ChangeParentMultiple cpm:
                foreach (var m in cpm.Moves)
                {
                    var node = _graph.FindNode(m.NodeId);
                    if (node == null) continue;

                    NodeId? oldParentId = (node as FakeNodeModel)?.ParentContainerId
                                       ?? (node as FakeContainerModel)?.ParentContainerId;

                    // 1. Remove from old parent.
                    if (oldParentId.HasValue &&
                        _graph.FindNode(oldParentId.Value) is FakeContainerModel oldContainer)
                    {
                        oldContainer.RemoveChild(m.NodeId);
                    }

                    // 2. Add to new parent.
                    if (m.NewParentContainerId.HasValue &&
                        _graph.FindNode(m.NewParentContainerId.Value) is FakeContainerModel newContainer)
                    {
                        newContainer.AddChild(m.NodeId, m.NewRegionIndex ?? -1);
                    }

                    // 3. Update node's parent pointer and local position.
                    if (node is FakeNodeModel fnm)
                    {
                        fnm.ParentContainerId = m.NewParentContainerId;
                        fnm.SetPosition(m.NewLocalPosition);
                    }
                    else if (node is FakeContainerModel containerNode)
                    {
                        containerNode.ParentContainerId = m.NewParentContainerId;
                        containerNode.Position = m.NewLocalPosition;
                    }
                }

                _graph.NotifyChanged(GraphChangeKind.NodesMoved);
                return new GraphCommandResult(true, null);

            case GraphCommand.AddLink link:
                _graph.AddLink(link.AssignedId, link.From, link.To);
                _graph.NotifyChanged(GraphChangeKind.LinksAdded);
                return new GraphCommandResult(true, null);

            case GraphCommand.RemoveLinks remove:
                foreach (var lid in remove.Links) _graph.RemoveLink(lid);
                _graph.NotifyChanged(GraphChangeKind.LinksRemoved);
                return new GraphCommandResult(true, null);

            case GraphCommand.SetNodeCollapsed sc:
                if (_graph.FindNode(sc.Node) is FakeNodeModel sn) sn.IsCollapsed = sc.Collapsed;
                _graph.NotifyChanged(GraphChangeKind.NodesModified);
                return new GraphCommandResult(true, null);

            case GraphCommand.SetNodeDisabled sd:
                if (_graph.FindNode(sd.Node) is FakeNodeModel dnm)
                {
                    if (sd.Disabled) dnm.State |= NodeState.Disabled;
                    else             dnm.State &= ~NodeState.Disabled;
                }
                return new GraphCommandResult(true, null);

            case GraphCommand.SetContainerCollapsed scc:
                if (_graph.FindNode(scc.ContainerId) is FakeContainerModel fcm)
                    fcm.IsCollapsed = scc.IsCollapsed;
                _graph.NotifyChanged(GraphChangeKind.NodesModified);
                return new GraphCommandResult(true, null);

            case GraphCommand.SetPinDefault spd:
                if (_graph.FindPin(spd.Pin) is FakePinModel pm && pm.Default is FakePinDefaultValue def)
                    def.Value = spd.NewValue;
                return new GraphCommandResult(true, null);

            case GraphCommand.AddComment ac:
                _graph.AddComment(ac.AssignedId, ac.Text, ac.Position, ac.Size, ac.Color, ac.MoveWithContents);
                return new GraphCommandResult(true, null);

            case GraphCommand.UpdateComment uc:
                _graph.UpdateComment(uc.Id, uc.Text, uc.Position, uc.Size, uc.Color, uc.ZOrder, uc.MoveWithContents);
                return new GraphCommandResult(true, null);

            case GraphCommand.RemoveComment rc:
                _graph.RemoveComment(rc.Id);
                return new GraphCommandResult(true, null);

            case GraphCommand.InsertReroute ir:
                if (_graph.FindLink(ir.Link) is FakeLinkModel flm)
                    flm.AddWaypoint(ir.Position);
                return new GraphCommandResult(true, null);

            case GraphCommand.MoveReroute mr:
                if (_graph.FindLink(mr.Link) is FakeLinkModel mrm)
                    mrm.MoveWaypoint(mr.WaypointIndex, mr.NewPosition);
                return new GraphCommandResult(true, null);

            case GraphCommand.RemoveReroute rr:
                if (_graph.FindLink(rr.Link) is FakeLinkModel rrm)
                    rrm.RemoveWaypoint(rr.WaypointIndex);
                return new GraphCommandResult(true, null);

            case GraphCommand.PromoteToVariable promote:
                ApplyPromoteToVariable(promote);
                _graph.NotifyChanged(GraphChangeKind.VariablesChanged);
                return new GraphCommandResult(true, null);

            case GraphCommand.Batch batch:
                foreach (var inner in batch.Commands) Apply(inner);
                return new GraphCommandResult(true, null);

            default:
                // Unhandled commands are silently accepted in demo
                return new GraphCommandResult(true, null);
        }
    }

    private void ApplyAddNode(GraphCommand.AddNode add)
    {
        var entry = _catalog.All.FirstOrDefault(e => e.Kind == add.Kind);
        var title = entry?.DisplayName ?? add.Kind.Id;

        if (add.InitialProperties != null &&
            add.InitialProperties.TryGetValue("VariableName", out var nameObj) &&
            nameObj is string varName)
        {
            title = add.Kind.Id == "Util.GetVar" ? $"Get {varName}" : $"Set {varName}";
        }

        var node  = _graph.AddNode(add.AssignedId, add.Kind, title, add.Position);

        if (entry is not null)
        {
            var pinIds = add.InitialProperties?.GetValueOrDefault("PinIds") as List<PinId>;
            int pinIdx = 0;

            foreach (var sig in entry.Inputs)
            {
                var pId = (pinIds != null && pinIdx < pinIds.Count) ? pinIds[pinIdx] : (PinId?)null;
                node.AddPin(sig.Label, PinDirection.Input, sig.Kind, sig.Type, ResolveShape(sig), pId);
                pinIdx++;
            }
            foreach (var sig in entry.Outputs)
            {
                var pId = (pinIds != null && pinIdx < pinIds.Count) ? pinIds[pinIdx] : (PinId?)null;
                node.AddPin(sig.Label, PinDirection.Output, sig.Kind, sig.Type, ResolveShape(sig), pId);
                pinIdx++;
            }
        }
    }

    private PinShape ResolveShape(PinSignature sig)
    {
        if (sig.Kind != PinKind.Data || !sig.Type.HasValue)
            return PinShape.Circle;

        var container = InferContainerKind(sig.Label);
        return _typeSystem.GetPinShape(sig.Type.Value, container);
    }

    private static ContainerKind InferContainerKind(string label)
    {
        if (label.Contains("Array", StringComparison.OrdinalIgnoreCase)) return ContainerKind.Array;
        if (label.Contains("Map", StringComparison.OrdinalIgnoreCase))   return ContainerKind.Map;
        if (label.Contains("Set", StringComparison.OrdinalIgnoreCase))   return ContainerKind.Set;
        return ContainerKind.Single;
    }

    private void ApplyPromoteToVariable(GraphCommand.PromoteToVariable promote)
    {
        if (_graph.FindPin(promote.Pin) is not FakePinModel targetPin)
            return;
        if (targetPin.Kind != PinKind.Data)
            return;
        if (_graph.FindNode(targetPin.OwnerNodeId) is not FakeNodeModel owner)
            return;

        string variableName = string.IsNullOrWhiteSpace(promote.VariableName) ? "NewVariable" : promote.VariableName.Trim();
        string variableId = $"var.{Guid.NewGuid():N}";

        if (targetPin.Direction == PinDirection.Input)
        {
            var getNodeId = IdGenerator.NewNodeId();
            var getPos = owner.Position + new Vector2(-240f, 0f);
            ApplyAddNode(new GraphCommand.AddNode(
                getNodeId,
                new NodeKindKey("Util.GetVar"),
                getPos,
                new Dictionary<string, object?> { ["VariableId"] = variableId, ["VariableName"] = variableName }));

            var fromPin = FindPinByLabelAndDirection(getNodeId, "Value", PinDirection.Output)
                       ?? FindFirstPinByDirection(getNodeId, PinDirection.Output);
            if (fromPin is not null)
                _graph.AddLink(IdGenerator.NewLinkId(), fromPin.Id, targetPin.Id);
        }
        else
        {
            var setNodeId = IdGenerator.NewNodeId();
            var setPos = owner.Position + new Vector2(240f, 0f);
            ApplyAddNode(new GraphCommand.AddNode(
                setNodeId,
                new NodeKindKey("Util.SetVar"),
                setPos,
                new Dictionary<string, object?> { ["VariableId"] = variableId, ["VariableName"] = variableName }));

            var toPin = FindPinByLabelAndDirection(setNodeId, "Value", PinDirection.Input)
                     ?? FindFirstPinByDirection(setNodeId, PinDirection.Input);
            if (toPin is not null)
                _graph.AddLink(IdGenerator.NewLinkId(), targetPin.Id, toPin.Id);
        }
    }

    private FakePinModel? FindPinByLabelAndDirection(NodeId nodeId, string label, PinDirection direction)
    {
        if (_graph.FindNode(nodeId) is not FakeNodeModel node)
            return null;

        return node.Pins
            .OfType<FakePinModel>()
            .FirstOrDefault(p => p.Direction == direction &&
                                 p.Label.Equals(label, StringComparison.OrdinalIgnoreCase));
    }

    private FakePinModel? FindFirstPinByDirection(NodeId nodeId, PinDirection direction)
    {
        if (_graph.FindNode(nodeId) is not FakeNodeModel node)
            return null;

        return node.Pins
            .OfType<FakePinModel>()
            .FirstOrDefault(p => p.Direction == direction);
    }
}
