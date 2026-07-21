using System;
using System.Collections.Generic;
using System.Linq;
using Hrot.Blueprints.Core.Assets;

namespace Hrot.Blueprints.Editor.GraphEditor;

public sealed class AddNodeCommand : IGraphCommand
{
    private readonly Graph _graph;
    private readonly Node _node;

    public string Description => $"Add Node {_node.Id}";

    public AddNodeCommand(Graph graph, Node node)
    {
        _graph = graph;
        _node  = node;
    }

    public void Execute() => _graph.Nodes.Add(_node);
    public void Undo()    => _graph.Nodes.Remove(_node);
}

public sealed class DeleteNodeCommand : IGraphCommand
{
    private readonly Graph _graph;
    private readonly Node _node;
    // Incident links removed alongside the node, captured on Execute so Undo restores the node AND its
    // wires as one step. (Previously the sink removed incident links directly, outside history, so undo
    // brought the node back with no connections.)
    private List<(int Index, Link Link)> _incident = new();

    public string Description => $"Delete Node {_node.Id}";

    public DeleteNodeCommand(Graph graph, Node node)
    {
        _graph = graph;
        _node  = node;
    }

    public void Execute()
    {
        _incident = new List<(int, Link)>();
        for (int i = _graph.Links.Count - 1; i >= 0; i--)
        {
            var l = _graph.Links[i];
            if (l.FromNodeId == _node.Id || l.ToNodeId == _node.Id)
            {
                _incident.Add((i, l));
                _graph.Links.RemoveAt(i);
            }
        }
        _graph.Nodes.Remove(_node);
    }

    public void Undo()
    {
        _graph.Nodes.Add(_node);
        foreach (var (idx, link) in _incident.OrderBy(r => r.Index))
            _graph.Links.Insert(idx <= _graph.Links.Count ? idx : _graph.Links.Count, link);
    }
}

/// <summary>
/// Undoable link edit: removes a set of links (captured with their positions) and/or adds new ones, as a
/// single history step. Covers plain wire-add, plain wire-delete, and the replace-then-add path (dropping
/// an existing exec-out / data-in link before wiring the new one). Undo restores the removed links at their
/// original indices (order matters for positional-projection assets) and drops the added ones.
/// </summary>
public sealed class LinkEditCommand : IGraphCommand
{
    private readonly Graph _graph;
    private readonly List<Link> _toRemove;
    private readonly List<Link> _toAdd;
    private List<(int Index, Link Link)> _removedSnapshot = new();

    public string Description { get; }

    public LinkEditCommand(Graph graph, IEnumerable<Link>? toRemove, IEnumerable<Link>? toAdd, string description)
    {
        _graph       = graph ?? throw new ArgumentNullException(nameof(graph));
        _toRemove    = toRemove?.ToList() ?? new List<Link>();
        _toAdd       = toAdd?.ToList()    ?? new List<Link>();
        Description  = description;
    }

    public void Execute()
    {
        _removedSnapshot = new List<(int, Link)>();
        for (int i = _graph.Links.Count - 1; i >= 0; i--)
        {
            // Reference identity — the sink passes the exact Link objects it matched.
            if (_toRemove.Contains(_graph.Links[i]))
            {
                _removedSnapshot.Add((i, _graph.Links[i]));
                _graph.Links.RemoveAt(i);
            }
        }
        foreach (var l in _toAdd) _graph.Links.Add(l);
    }

    public void Undo()
    {
        foreach (var l in _toAdd) _graph.Links.Remove(l);
        foreach (var (idx, link) in _removedSnapshot.OrderBy(r => r.Index))
            _graph.Links.Insert(idx <= _graph.Links.Count ? idx : _graph.Links.Count, link);
    }
}
