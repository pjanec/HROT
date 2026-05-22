namespace NodeEditor.Core.Search;

/// <summary>
/// Fuzzy string matcher used by the picker and My Blueprint search.
/// Tiered scoring: exact &gt; prefix &gt; word-start &gt; camelCase &gt;
/// substring &gt; keyword-substring &gt; char-order.
/// </summary>
public static class FuzzyMatcher
{
    /// <summary>Score and match positions for a candidate.</summary>
    public readonly record struct Result(int Score, IReadOnlyList<int> MatchPositions)
    {
        public bool HasMatch => Score > 0;
        public static Result NoMatch => new(0, Array.Empty<int>());
    }

    /// <summary>
    /// Score a candidate string against a query, returning matched character
    /// positions in <paramref name="candidate"/> when scoring is non-zero.
    /// </summary>
    /// <param name="query">User search text. Empty = match-all with score 1.</param>
    /// <param name="candidate">String being scored.</param>
    /// <param name="keywords">Optional keyword aliases — boosted as secondary.</param>
    public static Result Score(string query, string candidate, IReadOnlyList<string>? keywords = null)
    {
        if (string.IsNullOrEmpty(query)) return new Result(1, Array.Empty<int>());
        if (string.IsNullOrEmpty(candidate)) return Result.NoMatch;

        var q = query.ToLowerInvariant();
        var c = candidate.ToLowerInvariant();

        // Tier 1: exact match.
        if (c == q)
        {
            return new Result(10000, RangePositions(0, candidate.Length));
        }

        // Tier 2: prefix match.
        if (c.StartsWith(q, StringComparison.Ordinal))
        {
            // Bonus for longer prefix matches relative to candidate length.
            int bonus = (int)(500.0 * q.Length / candidate.Length);
            return new Result(5000 + bonus, RangePositions(0, q.Length));
        }

        // Tier 3 & 4: word-start and camelCase boundary matches.
        if (TryMatchAtBoundaries(q, candidate, out var boundaryScore, out var boundaryPositions))
        {
            return new Result(boundaryScore, boundaryPositions);
        }

        // Tier 5: substring in display name.
        int subIdx = c.IndexOf(q, StringComparison.Ordinal);
        if (subIdx >= 0)
        {
            // Penalty for being deeper in the string.
            int penalty = subIdx * 5;
            return new Result(Math.Max(1500 - penalty, 1100),
                              RangePositions(subIdx, q.Length));
        }

        // Tier 6: substring in keywords.
        if (keywords is { Count: > 0 })
        {
            foreach (var kw in keywords)
            {
                var lk = kw.ToLowerInvariant();
                if (lk.Contains(q, StringComparison.Ordinal))
                {
                    return new Result(1000, Array.Empty<int>()); // can't highlight in candidate
                }
            }
        }

        // Tier 7: fuzzy char-order match.
        var fuzzyResult = ScoreFuzzy(q, c, candidate);
        return fuzzyResult;
    }

    /// <summary>
    /// Try to match query against boundary characters (start of words or
    /// camelCase boundaries). Returns true if at least one boundary
    /// match found and the entire query consumed.
    /// </summary>
    private static bool TryMatchAtBoundaries(
        string q, string candidate, out int score, out IReadOnlyList<int> positions)
    {
        score = 0;
        positions = Array.Empty<int>();

        // Collect boundary indices.
        var boundaries = new List<int> { 0 };
        for (int i = 1; i < candidate.Length; i++)
        {
            var prev = candidate[i - 1];
            var cur = candidate[i];

            if (prev is ' ' or '_' or '-' or '/' or '.' or '\\')
            {
                boundaries.Add(i);
            }
            else if (char.IsLower(prev) && char.IsUpper(cur))
            {
                boundaries.Add(i);
            }
            else if (char.IsLetter(prev) && char.IsDigit(cur))
            {
                boundaries.Add(i);
            }
        }

        // Try matching query chars to consecutive boundary chars.
        var lower = candidate.ToLowerInvariant();
        var matched = new List<int>(q.Length);
        int qi = 0;
        foreach (int b in boundaries)
        {
            if (qi >= q.Length) break;
            if (lower[b] == q[qi])
            {
                matched.Add(b);
                qi++;
            }
        }

        if (qi == q.Length && matched.Count == q.Length)
        {
            // Decide between word-start (3000) and camelCase (2500).
            // If first boundary is at index 0, classify as word-start;
            // otherwise camelCase.
            bool startsAtZero = matched[0] == 0;
            score = startsAtZero ? 3000 : 2500;
            positions = matched;
            return true;
        }

        return false;
    }

    /// <summary>Char-order match: each query char appears in candidate, in order.</summary>
    private static Result ScoreFuzzy(string q, string lower, string original)
    {
        var positions = new List<int>(q.Length);
        int ci = 0;
        for (int qi = 0; qi < q.Length; qi++)
        {
            int found = lower.IndexOf(q[qi], ci);
            if (found < 0) return Result.NoMatch;
            positions.Add(found);
            ci = found + 1;
        }

        // Score: 500 minus distance penalty. Tighter clusters score higher.
        int spread = positions[^1] - positions[0];
        int penalty = spread * 2;
        return new Result(Math.Max(500 - penalty, 100), positions);
    }

    private static IReadOnlyList<int> RangePositions(int start, int count)
    {
        var result = new int[count];
        for (int i = 0; i < count; i++) result[i] = start + i;
        return result;
    }
}
