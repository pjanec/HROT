namespace StructEdit.Core;

/// <summary>
/// Lightweight struct wrapping a JSONPath-like string (e.g. "$.Field.SubField").
/// Used only for JSON output, diagnostics, and configuration — never resolved at runtime.
/// </summary>
public readonly record struct EditPath
{
    public string Value { get; }

    private EditPath(string value) => Value = value;

    /// <summary>Parses the path string. Throws <see cref="ArgumentException"/> for null or empty.</summary>
    public static EditPath Parse(string path)
    {
        if (string.IsNullOrEmpty(path))
            throw new ArgumentException("Path must not be null or empty.", nameof(path));
        return new EditPath(path);
    }

    /// <summary>Synthetic root path "$".</summary>
    public static EditPath Root { get; } = new EditPath("$");

    /// <summary>Implicit conversion from string.</summary>
    public static implicit operator EditPath(string path) => Parse(path);

    public override string ToString() => Value;
}
