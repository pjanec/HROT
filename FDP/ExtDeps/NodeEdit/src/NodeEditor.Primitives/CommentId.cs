namespace NodeEditor.Primitives;

/// <summary>Unique identifier for a comment box.</summary>
public readonly record struct CommentId(Guid Value)
{
    public static CommentId Empty => default;
    public static CommentId NewId() => new(Guid.NewGuid());
    public override string ToString() => $"Comment({Value:N}[..8])"[..16];
}
