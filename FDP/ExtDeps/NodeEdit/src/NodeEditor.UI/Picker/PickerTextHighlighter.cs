namespace NodeEditor.UI.Picker;

/// <summary>Splits a display name into alternating highlighted / plain runs
/// for fuzzy-match rendering. Pure (no ImGui) so it is unit-testable.</summary>
internal static class PickerTextHighlighter
{
    public readonly record struct HighlightRun(string Text, bool IsMatch);

    /// <summary>Split <paramref name="name"/> into consecutive runs where each run
    /// is either fully matched (positions in <paramref name="matchPositions"/>) or
    /// fully unmatched. Order preserved; concatenating Text yields the original name.
    /// Null/empty matchPositions ⇒ a single plain run (or empty list for empty name).</summary>
    public static IReadOnlyList<HighlightRun> SplitRuns(string name, IReadOnlyCollection<int>? matchPositions)
    {
        var runs = new List<HighlightRun>();

        if (name.Length == 0)
            return runs;

        if (matchPositions is not { Count: > 0 })
        {
            runs.Add(new HighlightRun(name, false));
            return runs;
        }

        var set = new HashSet<int>(matchPositions);
        int chunkStart = 0;
        bool isMatch = set.Contains(0);

        for (int i = 1; i <= name.Length; i++)
        {
            bool isNextMatch = i < name.Length && set.Contains(i);

            // Flush the current chunk when highlight state changes or we reach the end.
            if (i == name.Length || isMatch != isNextMatch)
            {
                string chunk = name.Substring(chunkStart, i - chunkStart);
                runs.Add(new HighlightRun(chunk, isMatch));

                // Begin the next chunk.
                chunkStart = i;
                isMatch = isNextMatch;
            }
        }

        return runs;
    }
}
