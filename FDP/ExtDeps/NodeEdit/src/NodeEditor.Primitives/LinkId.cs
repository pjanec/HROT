namespace NodeEditor.Primitives;

/// <summary>Unique identifier for a link (wire) between two pins.</summary>
public readonly record struct LinkId(Guid Value)
{
    public static LinkId Empty => default;
    public static LinkId NewId() => new(Guid.NewGuid());
    public override string ToString() => $"Link({Value:N}[..8])"[..16];
}
