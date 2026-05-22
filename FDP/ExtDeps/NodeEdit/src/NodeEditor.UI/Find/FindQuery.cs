namespace NodeEditor.UI.Find;

/// <summary>
/// A parsed find query, broken into optional prefix filters and a free-text term.
/// </summary>
/// <param name="FreeText">The portion of the query after all prefix tokens have been removed.</param>
/// <param name="Prefixes">
/// Extracted prefix filters (e.g. <c>type</c>, <c>kind</c>, <c>category</c>).
/// Keys are lower-case prefix names; values are the text that followed the colon.
/// </param>
public sealed record FindQuery(
    string FreeText,
    IReadOnlyDictionary<string, string> Prefixes);

/// <summary>
/// Parses a raw query string into a <see cref="FindQuery"/>.
/// Supported prefixes: type, kind, category, var, func, error, warning, breakpoint, watched.
/// </summary>
public static class FindQueryParser
{
    private static readonly HashSet<string> KnownPrefixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "type", "kind", "category", "var", "func",
        "error", "warning", "breakpoint", "watched",
    };

    /// <summary>Parse a raw query string and return the structured result.</summary>
    public static FindQuery Parse(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return new FindQuery(string.Empty, new Dictionary<string, string>());

        var prefixes  = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var freeWords = new List<string>();

        foreach (var token in raw.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var colonIdx = token.IndexOf(':');
            if (colonIdx > 0)
            {
                var key = token[..colonIdx];
                if (KnownPrefixes.Contains(key))
                {
                    var value = colonIdx + 1 < token.Length ? token[(colonIdx + 1)..] : string.Empty;
                    prefixes[key.ToLowerInvariant()] = value;
                    continue;
                }
            }
            freeWords.Add(token);
        }

        var freeText = string.Join(' ', freeWords);
        return new FindQuery(freeText, prefixes);
    }
}
