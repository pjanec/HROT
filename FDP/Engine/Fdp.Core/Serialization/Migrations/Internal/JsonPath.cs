using System.Text.Json.Nodes;

namespace Fdp.Core.Serialization.Migrations.Internal;

// ---------------------------------------------------------------
// Segment discriminated union
// ---------------------------------------------------------------

/// <summary>Base type for a single parsed segment of a JSONPath expression.</summary>
internal abstract record JsonPathSegment;

/// <summary>A dotted identifier segment: <c>.identifier</c>.</summary>
internal sealed record DottedSegment(string Identifier) : JsonPathSegment;

/// <summary>A bracket-quoted key segment: <c>['key']</c>.</summary>
internal sealed record QuotedKeySegment(string Key) : JsonPathSegment;

/// <summary>An array-index segment: <c>[N]</c>.</summary>
internal sealed record ArrayIndexSegment(int Index) : JsonPathSegment;

// ---------------------------------------------------------------
// JsonPath
// ---------------------------------------------------------------

/// <summary>
/// An immutable, pre-parsed JSONPath in the restricted FDP dialect.
/// Provides <see cref="Read"/>, <see cref="TryWrite"/>, and
/// <see cref="TryRemove"/> operations against a <see cref="JsonObject"/>
/// DOM root.
/// </summary>
internal sealed class JsonPath
{
    /// <summary>The original path string as supplied to the parser.</summary>
    public string Original { get; }

    /// <summary>The parsed segments (excluding the root <c>$</c> anchor).</summary>
    public IReadOnlyList<JsonPathSegment> Segments { get; }

    internal JsonPath(string original, IReadOnlyList<JsonPathSegment> segments)
    {
        Original = original;
        Segments = segments;
    }

    /// <summary>
    /// Reads the node at this path from <paramref name="root"/>.
    /// Returns <c>null</c> when the path does not exist.
    /// Returns a <see cref="JsonValue"/> wrapping <c>null</c> when the
    /// JSON value at that path is a JSON null literal.
    /// </summary>
    public JsonNode? Read(JsonObject root)
        => JsonPathApplicator.Read(root, this);

    /// <summary>
    /// Writes <paramref name="value"/> at this path in <paramref name="root"/>.
    /// Returns <c>true</c> when the write succeeded.
    /// Returns <c>false</c> (silently) when an intermediate parent is missing
    /// (user-deletion-wins; no parent creation).
    /// </summary>
    public bool TryWrite(JsonObject root, JsonNode? value)
        => JsonPathApplicator.TryWrite(root, this, value);

    /// <summary>
    /// Removes the node at this path from <paramref name="root"/>.
    /// Returns <c>true</c> if the node was removed or was already absent.
    /// Returns <c>false</c> if an intermediate parent is missing.
    /// </summary>
    public bool TryRemove(JsonObject root)
        => JsonPathApplicator.TryRemove(root, this);

    /// <summary>Returns the canonical form of this path.</summary>
    public override string ToString()
        => JsonPathParser.BuildCanonical(Segments);
}
