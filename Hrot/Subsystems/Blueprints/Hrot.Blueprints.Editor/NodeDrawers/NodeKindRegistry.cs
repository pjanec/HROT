namespace Hrot.Blueprints.Editor.NodeDrawers;

/// <summary>Palette registry: maps node-kind strings to descriptors.</summary>
public sealed class NodeKindRegistry
{
    private readonly Dictionary<string, NodeKindDescriptor> _map = new();

    public void Register(NodeKindDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        _map[descriptor.Kind] = descriptor;
    }

    public IReadOnlyCollection<NodeKindDescriptor> EnumerateAll() => _map.Values;

    public NodeKindDescriptor? TryGet(string kind)
        => _map.TryGetValue(kind, out var d) ? d : null;
}
