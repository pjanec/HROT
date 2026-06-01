namespace NodeEditor.Demo.FakeBlueprint;

/// <summary>
/// Lightweight container that tracks multiple named graphs for multi-tab scenarios.
/// </summary>
public sealed class FakeGraphContainer
{
    private readonly List<FakeGraphModel> _graphs;

    /// <summary>All graphs in the container.</summary>
    public IReadOnlyList<FakeGraphModel> Graphs => _graphs;

    /// <summary>Index of the currently active graph.</summary>
    public int ActiveIndex { get; private set; }

    /// <summary>Currently active graph.</summary>
    public FakeGraphModel Active => _graphs[ActiveIndex];

    /// <summary>Create a container from the given graphs. First graph is active.</summary>
    public FakeGraphContainer(params FakeGraphModel[] graphs)
    {
        if (graphs.Length == 0)
            throw new ArgumentException("At least one graph required.", nameof(graphs));
        _graphs = new List<FakeGraphModel>(graphs);
        ActiveIndex = 0;
    }

    /// <summary>Activate the next graph, wrapping around.</summary>
    public void ActivateNext() => Activate((ActiveIndex + 1) % _graphs.Count);

    /// <summary>Activate the previous graph, wrapping around.</summary>
    public void ActivatePrev() => Activate((ActiveIndex - 1 + _graphs.Count) % _graphs.Count);

    /// <summary>Activate the graph at the given index.</summary>
    public void Activate(int index)
    {
        if (index < 0 || index >= _graphs.Count)
            throw new ArgumentOutOfRangeException(nameof(index));
        ActiveIndex = index;
    }

    /// <summary>Append a new graph tab to the container.</summary>
    public void AddGraph(FakeGraphModel graph)
    {
        _graphs.Add(graph);
    }
}
