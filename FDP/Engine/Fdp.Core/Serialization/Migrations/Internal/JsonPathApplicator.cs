using System.Text.Json;
using System.Text.Json.Nodes;

namespace Fdp.Core.Serialization.Migrations.Internal;

/// <summary>
/// Applies <see cref="JsonPath"/> expressions to a <see cref="JsonObject"/>
/// DOM. All methods are purely structural — they never allocate schema or
/// throw on missing paths (missing-path semantics are described per method).
/// </summary>
internal static class JsonPathApplicator
{
    // ---------------------------------------------------------------
    // Read
    // ---------------------------------------------------------------

    /// <summary>
    /// Returns the node at <paramref name="path"/> within <paramref name="root"/>.
    /// <list type="bullet">
    ///   <item>Returns <c>null</c> when the path does not exist (key missing).</item>
    ///   <item>Returns a <see cref="JsonValue"/> wrapping <c>null</c> for JSON null literals.</item>
    /// </list>
    /// Never throws due to a missing path.
    /// </summary>
    public static JsonNode? Read(JsonObject root, JsonPath path)
    {
        JsonNode? current = root;

        foreach (var seg in path.Segments)
        {
            if (current is null)
                return null;

            switch (seg)
            {
                case DottedSegment d:
                    if (current is not JsonObject obj)
                        return null;
                    if (!obj.TryGetPropertyValue(d.Identifier, out JsonNode? child))
                        return null;
                    // If value is stored as null node, return a JsonValue null.
                    current = child ?? JsonValue.Create((object?)null);
                    break;

                case QuotedKeySegment q:
                    if (current is not JsonObject qobj)
                        return null;
                    if (!qobj.TryGetPropertyValue(q.Key, out JsonNode? qchild))
                        return null;
                    current = qchild ?? JsonValue.Create((object?)null);
                    break;

                case ArrayIndexSegment a:
                    if (current is not JsonArray arr)
                        return null;
                    if (a.Index < 0 || a.Index >= arr.Count)
                        return null;
                    current = arr[a.Index] ?? JsonValue.Create((object?)null);
                    break;
            }
        }

        return current;
    }

    // ---------------------------------------------------------------
    // TryWrite
    // ---------------------------------------------------------------

    /// <summary>
    /// Writes <paramref name="value"/> at <paramref name="path"/> in
    /// <paramref name="root"/>.
    /// <list type="bullet">
    ///   <item>Returns <c>true</c> if the write succeeded.</item>
    ///   <item>Returns <c>false</c> if an intermediate parent is missing
    ///         (user-deletion-wins — no parent creation).</item>
    /// </list>
    /// </summary>
    public static bool TryWrite(JsonObject root, JsonPath path, JsonNode? value)
    {
        if (path.Segments.Count == 0)
            return false; // Writing to "$" (root) is not supported.

        JsonNode? parent = root;

        // Navigate to the parent of the final segment.
        for (int i = 0; i < path.Segments.Count - 1; i++)
        {
            var seg = path.Segments[i];
            parent = Descend(parent, seg);
            if (parent is null)
                return false; // intermediate parent missing
        }

        // Apply the final segment.
        var last = path.Segments[path.Segments.Count - 1];

        // Detach the new value from any existing parent before assigning.
        JsonNode? nodeToSet = value?.DeepClone();

        switch (last)
        {
            case DottedSegment d:
                if (parent is not JsonObject obj)
                    return false;
                obj[d.Identifier] = nodeToSet;
                return true;

            case QuotedKeySegment q:
                if (parent is not JsonObject qobj)
                    return false;
                qobj[q.Key] = nodeToSet;
                return true;

            case ArrayIndexSegment a:
                if (parent is not JsonArray arr)
                    return false;
                if (a.Index < 0 || a.Index >= arr.Count)
                    return false; // out of bounds — parent exists but slot does not
                arr[a.Index] = nodeToSet;
                return true;

            default:
                return false;
        }
    }

    // ---------------------------------------------------------------
    // TryRemove
    // ---------------------------------------------------------------

    /// <summary>
    /// Removes the node at <paramref name="path"/> from <paramref name="root"/>.
    /// <list type="bullet">
    ///   <item>Returns <c>true</c> if removed OR already absent.</item>
    ///   <item>Returns <c>false</c> if an intermediate parent is missing.</item>
    /// </list>
    /// </summary>
    public static bool TryRemove(JsonObject root, JsonPath path)
    {
        if (path.Segments.Count == 0)
            return false; // Cannot remove root.

        JsonNode? parent = root;

        for (int i = 0; i < path.Segments.Count - 1; i++)
        {
            var seg = path.Segments[i];
            parent = Descend(parent, seg);
            if (parent is null)
                return false; // intermediate parent missing
        }

        var last = path.Segments[path.Segments.Count - 1];

        switch (last)
        {
            case DottedSegment d:
                if (parent is not JsonObject obj)
                    return false;
                obj.Remove(d.Identifier);
                return true;

            case QuotedKeySegment q:
                if (parent is not JsonObject qobj)
                    return false;
                qobj.Remove(q.Key);
                return true;

            case ArrayIndexSegment a:
                if (parent is not JsonArray arr)
                    return false;
                if (a.Index < 0 || a.Index >= arr.Count)
                    return true; // already absent
                arr.RemoveAt(a.Index);
                return true;

            default:
                return false;
        }
    }

    // ---------------------------------------------------------------
    // Private helpers
    // ---------------------------------------------------------------

    /// <summary>
    /// Descends one step into <paramref name="node"/> using <paramref name="seg"/>.
    /// Returns <c>null</c> if the child does not exist or the node type is wrong.
    /// </summary>
    private static JsonNode? Descend(JsonNode? node, JsonPathSegment seg)
    {
        if (node is null)
            return null;

        switch (seg)
        {
            case DottedSegment d:
                if (node is not JsonObject obj)
                    return null;
                return obj.TryGetPropertyValue(d.Identifier, out JsonNode? child) ? child : null;

            case QuotedKeySegment q:
                if (node is not JsonObject qobj)
                    return null;
                return qobj.TryGetPropertyValue(q.Key, out JsonNode? qchild) ? qchild : null;

            case ArrayIndexSegment a:
                if (node is not JsonArray arr)
                    return null;
                if (a.Index < 0 || a.Index >= arr.Count)
                    return null;
                return arr[a.Index];

            default:
                return null;
        }
    }
}
