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

    public string Description => $"Delete Node {_node.Id}";

    public DeleteNodeCommand(Graph graph, Node node)
    {
        _graph = graph;
        _node  = node;
    }

    public void Execute() => _graph.Nodes.Remove(_node);
    public void Undo()    => _graph.Nodes.Add(_node);
}
