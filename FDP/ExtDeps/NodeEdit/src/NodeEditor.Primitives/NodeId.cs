namespace NodeEditor.Primitives;

/// <summary>
/// Unique identifier for a node in a graph. Wraps a <see cref="Guid"/>
/// to provide type safety; never expose raw Guids in the public API.
/// </summary>
public readonly record struct NodeId(Guid Value)
{
    /// <summary>The empty (default-constructed) NodeId.</summary>
    public static NodeId Empty => default;

    /// <summary>Generate a new, random NodeId.</summary>
    public static NodeId NewId() => new(Guid.NewGuid());

    /// <inheritdoc/>
    public override string ToString() => $"Node({Value:N}[..8])"[..16];
}
