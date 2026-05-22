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
                return new GraphCommandResult(true, null);

            case GraphCommand.SetNodeDisabled sd:
                if (_graph.FindNode(sd.Node) is FakeNodeModel dnm)
                {
                    if (sd.Disabled) dnm.State |= NodeState.Disabled;
                    else             dnm.State &= ~NodeState.Disabled;
                }
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
        var node  = _graph.AddNode(add.AssignedId, add.Kind, title, add.Position);

        if (entry is not null)
        {
            foreach (var sig in entry.Inputs)
                node.AddPin(sig.Label, PinDirection.Input, sig.Kind, sig.Type, ResolveShape(sig));
            foreach (var sig in entry.Outputs)
                node.AddPin(sig.Label, PinDirection.Output, sig.Kind, sig.Type, ResolveShape(sig));
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
}
