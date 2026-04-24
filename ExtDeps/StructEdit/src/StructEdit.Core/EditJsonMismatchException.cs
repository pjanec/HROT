namespace StructEdit.Core;

/// <summary>
/// Thrown when a JSON payload is incompatible with the current session — either because the
/// schema version differs or because the serialized type does not match the document's component type.
/// </summary>
public sealed class EditJsonMismatchException : Exception
{
    /// <summary>The JSON path or schema key where the mismatch was detected.</summary>
    public string JsonPath { get; }

    /// <param name="jsonPath">The schema key or JSON path that caused the mismatch.</param>
    /// <param name="message">Human-readable description of the mismatch.</param>
    public EditJsonMismatchException(string jsonPath, string message)
        : base(message)
    {
        JsonPath = jsonPath ?? throw new ArgumentNullException(nameof(jsonPath));
    }
}
