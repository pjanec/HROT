namespace NodeEditor.Primitives;

/// <summary>Unique identifier for a single graph (event graph, function body, macro body, …).</summary>
public readonly record struct GraphId(Guid Value)
{
    public static GraphId Empty => default;
    public static GraphId NewId() => new(Guid.NewGuid());
    public override string ToString() => $"Graph({Value:N}[..8])"[..16];
}
