using System.Text;
using System.Text.RegularExpressions;

namespace Fdp.Core.Serialization.Migrations.Internal;

/// <summary>
/// Parses path strings in the restricted FDP JSONPath dialect and produces
/// canonical path strings from segment lists.
/// </summary>
internal static class JsonPathParser
{
    // Regex for a plain dotted identifier.
    private static readonly Regex s_identifierRegex =
        new Regex(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);

    /// <summary>
    /// Parses a JSONPath string into a <see cref="JsonPath"/>.
    /// </summary>
    /// <param name="path">The path to parse, e.g. <c>$.foo['bar'][0]</c>.</param>
    /// <returns>A fully parsed <see cref="JsonPath"/>.</returns>
    /// <exception cref="MigrationException">
    /// The path is malformed or uses an unsupported construct (wildcards,
    /// recursive descent, filters, negative indexes, slices).
    /// </exception>
    public static JsonPath Parse(string path)
    {
        if (path is null)
            throw new ArgumentNullException(nameof(path));

        if (!path.StartsWith("$", StringComparison.Ordinal))
            throw new MigrationException($"JSONPath must start with '$'; got: {path}");

        var segments = new List<JsonPathSegment>();
        int pos = 1; // skip '$'

        while (pos < path.Length)
        {
            char c = path[pos];

            if (c == '.')
            {
                pos++; // consume '.'

                if (pos >= path.Length)
                    throw new MigrationException($"Unexpected end of path after '.': {path}");

                // Recursive descent check.
                if (path[pos] == '.')
                    throw new MigrationException(
                        $"Recursive-descent operator '..' is not supported in the FDP JSONPath dialect: {path}");

                // Wildcard check.
                if (path[pos] == '*')
                    throw new MigrationException(
                        $"Wildcard '*' is not supported in the FDP JSONPath dialect: {path}");

                // Parse identifier.
                int start = pos;
                while (pos < path.Length && path[pos] != '.' && path[pos] != '[')
                    pos++;

                string identifier = path.Substring(start, pos - start);
                if (identifier.Length == 0)
                    throw new MigrationException($"Empty identifier after '.': {path}");

                if (!s_identifierRegex.IsMatch(identifier))
                    throw new MigrationException(
                        $"Identifier '{identifier}' is not a valid FDP JSONPath identifier: {path}");

                segments.Add(new DottedSegment(identifier));
            }
            else if (c == '[')
            {
                pos++; // consume '['

                if (pos >= path.Length)
                    throw new MigrationException($"Unclosed '[': {path}");

                char next = path[pos];

                // Wildcard: [*]
                if (next == '*')
                    throw new MigrationException(
                        $"Wildcard '*' is not supported in the FDP JSONPath dialect: {path}");

                // Filter: [?(...)
                if (next == '?')
                    throw new MigrationException(
                        $"Filter expressions '[?(...)']' are not supported in the FDP JSONPath dialect: {path}");

                // Quoted key: ['...']
                if (next == '\'')
                {
                    pos++; // consume opening '\''
                    var key = new StringBuilder();

                    while (pos < path.Length)
                    {
                        char ch = path[pos];
                        if (ch == '\\')
                        {
                            pos++;
                            if (pos >= path.Length)
                                throw new MigrationException($"Unexpected end of escape sequence in path: {path}");
                            char escaped = path[pos];
                            if (escaped == '\'') key.Append('\'');
                            else if (escaped == '\\') key.Append('\\');
                            else throw new MigrationException($"Unsupported escape '\\{escaped}' in path: {path}");
                            pos++;
                        }
                        else if (ch == '\'')
                        {
                            break; // end of key
                        }
                        else
                        {
                            key.Append(ch);
                            pos++;
                        }
                    }

                    if (pos >= path.Length || path[pos] != '\'')
                        throw new MigrationException($"Unclosed quoted key in path: {path}");
                    pos++; // consume closing '\''

                    if (pos >= path.Length || path[pos] != ']')
                        throw new MigrationException($"Expected ']' after quoted key in path: {path}");
                    pos++; // consume ']'

                    segments.Add(new QuotedKeySegment(key.ToString()));
                }
                else if (char.IsDigit(next))
                {
                    // Numeric index.
                    int start = pos;
                    while (pos < path.Length && char.IsDigit(path[pos]))
                        pos++;

                    // Slice check: [N:M]
                    if (pos < path.Length && path[pos] == ':')
                        throw new MigrationException(
                            $"Slice expressions are not supported in the FDP JSONPath dialect: {path}");

                    if (pos >= path.Length || path[pos] != ']')
                        throw new MigrationException($"Expected ']' after array index in path: {path}");

                    string indexStr = path.Substring(start, pos - start);
                    if (!int.TryParse(indexStr, out int index))
                        throw new MigrationException($"Invalid array index '{indexStr}' in path: {path}");

                    pos++; // consume ']'
                    segments.Add(new ArrayIndexSegment(index));
                }
                else if (next == '-')
                {
                    // Negative index.
                    throw new MigrationException(
                        $"Negative array indexes are not supported in the FDP JSONPath dialect: {path}");
                }
                else
                {
                    throw new MigrationException($"Unexpected character '{next}' after '[' in path: {path}");
                }
            }
            else
            {
                throw new MigrationException($"Unexpected character '{c}' at position {pos} in path: {path}");
            }
        }

        return new JsonPath(path, segments.AsReadOnly());
    }

    /// <summary>
    /// Builds the canonical string representation of a segment list.
    /// The result always starts with <c>$</c>.
    /// </summary>
    public static string BuildCanonical(IEnumerable<JsonPathSegment> segments)
    {
        var sb = new StringBuilder("$");

        foreach (var seg in segments)
        {
            switch (seg)
            {
                case DottedSegment d:
                    sb.Append('.');
                    sb.Append(d.Identifier);
                    break;

                case QuotedKeySegment q:
                    // Use dotted form if the key is a valid plain identifier.
                    if (s_identifierRegex.IsMatch(q.Key))
                    {
                        sb.Append('.');
                        sb.Append(q.Key);
                    }
                    else
                    {
                        sb.Append("['");
                        foreach (char ch in q.Key)
                        {
                            if (ch == '\'') sb.Append("\\'");
                            else if (ch == '\\') sb.Append("\\\\");
                            else sb.Append(ch);
                        }
                        sb.Append("']");
                    }
                    break;

                case ArrayIndexSegment a:
                    sb.Append('[');
                    sb.Append(a.Index);
                    sb.Append(']');
                    break;
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Builds a canonical JSONPath string from a sequence of segment values.
    /// Each element must be a <see cref="string"/> (object key) or an
    /// <see cref="int"/> (array index).
    /// </summary>
    /// <param name="segments">Sequence of string or int segment values.</param>
    /// <returns>Canonical JSONPath string starting with <c>$</c>.</returns>
    /// <exception cref="ArgumentException">
    /// An element is neither <see cref="string"/> nor <see cref="int"/>.
    /// </exception>
    public static string Build(IEnumerable<object> segments)
    {
        var converted = new List<JsonPathSegment>();
        foreach (var seg in segments)
        {
            if (seg is string key)
                converted.Add(new QuotedKeySegment(key));
            else if (seg is int idx)
                converted.Add(new ArrayIndexSegment(idx));
            else
                throw new ArgumentException(
                    $"Unsupported segment type {seg?.GetType().Name ?? "null"}; expected string or int.",
                    nameof(segments));
        }
        return BuildCanonical(converted);
    }
}
