namespace NodeEditor.Primitives;

/// <summary>Unique identifier for a pin on a node.</summary>
public readonly record struct PinId(Guid Value)
{
    public static PinId Empty => default;
    public static PinId NewId() => new(Guid.NewGuid());
    public override string ToString() => $"Pin({Value:N}[..8])"[..16];
}
