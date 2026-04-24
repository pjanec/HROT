using StructEdit.Core;

namespace StructEdit.Json;

/// <summary>
/// Extension methods on <see cref="IEditSession"/> that add JSON serialization support.
/// Implemented as extension methods to keep <c>StructEdit.Json</c> as a peer-level dependency
/// of <c>StructEdit.Reflection</c> rather than introducing a circular project reference.
/// </summary>
public static class EditSessionJsonExtensions
{
    /// <summary>
    /// Serializes the current binding state of the session's document to a JSON string.
    /// </summary>
    /// <returns>Indented JSON conforming to the StructEdit 1.0 schema.</returns>
    public static string ToJson(this IEditSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return EditDocumentJsonSerializer.Serialize(session.Document);
    }

    /// <summary>
    /// Deserializes <paramref name="json"/> into the current session's bindings using the
    /// existing buffer.  Does NOT open a new session or discard unserialized fields.
    /// Call <see cref="IEditSession.MarkStructuralChange"/> and
    /// <see cref="IEditSession.RebuildDocument"/> afterwards when DynamicArray sizes changed.
    /// </summary>
    /// <exception cref="EditJsonMismatchException">
    /// When the JSON schema version or root type name do not match the session's document.
    /// </exception>
    public static void LoadJson(this IEditSession session, string json)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(json);
        EditDocumentJsonSerializer.Deserialize(json, session.Document);
    }
}
