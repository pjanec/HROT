namespace NodeEditor.Primitives;

/// <summary>
/// Unique identifier for an attachment in a graph. Wraps a <see cref="Guid"/>
/// to provide type safety; never expose raw Guids in the public API.
/// </summary>
public readonly record struct AttachmentId(Guid Value)
{
    /// <summary>The empty (default-constructed) AttachmentId.</summary>
    public static AttachmentId Empty => default;

    /// <summary>Generate a new, random AttachmentId.</summary>
    public static AttachmentId NewId() => new(Guid.NewGuid());

    /// <inheritdoc/>
    public override string ToString() => $"Attachment({Value:N}[..8])"[..19];
}
