using NodeEditor.Core.Search;
using NodeEditor.Core.Interfaces;

namespace NodeEditor.UI.Picker;

/// <summary>
/// Internal per-frame state for an open picker session. Holds search text,
/// filtered/ranked item list, keyboard focus index, and per-context
/// favorites/recent stores.
/// </summary>
internal sealed class PickerState
{
    // ── Session identity ─────────────────────────────────────────────────────
    public string ContextKey = "";
    public PickerSelectionMode SelectionMode = PickerSelectionMode.Single;
    public FavoritesStore Favorites = new();
    public RecentStore Recent = new();

    // ── Source items ─────────────────────────────────────────────────────────
    /// <summary>All entries from the source (cached after first query).</summary>
    public PickerEntry[] AllEntries = [];

    /// <summary>Filtered and ranked entries for the current query.</summary>
    public List<RankedEntry> Filtered = [];

    // ── Search state ─────────────────────────────────────────────────────────
    public string SearchText = "";
    public string LastQuery = "\u0000"; // intentional mismatch to force first refilter

    // ── Selection ────────────────────────────────────────────────────────────
    public HashSet<int> SelectedFilteredIndices = [];
    public HashSet<int> HighlightedIndices      = [];
    public int KeyboardFocusIndex;

    // ── Wide layout sidebar ───────────────────────────────────────────────────
    public string SelectedCategory = "";

    // ── Misc ─────────────────────────────────────────────────────────────────
    public bool Confirmed;
    public bool FocusSearchNextFrame = true;
    public bool IsFirstFrame = true;

    // ── Methods ──────────────────────────────────────────────────────────────

    /// <summary>Recompute <see cref="Filtered"/> from <see cref="AllEntries"/> using the current query.</summary>
    public void Refilter()
    {
        LastQuery = SearchText;
        Filtered.Clear();

        var q = SearchText;

        foreach (var entry in AllEntries)
        {
            // Architecturally critical: Project the full path to support VS-style deep hierarchy fuzzy matching
            string fullPath = string.IsNullOrEmpty(entry.Category)
                ? entry.Name
                : $"{entry.Category}/{entry.Name}";

            var result = FuzzyMatcher.Score(q, fullPath, entry.Keywords);
            if (!result.HasMatch) continue;

            // Shift match positions from the full path coordinate space back to the Name's coordinate space.
            // This prevents out-of-bounds exceptions and misalignment during UI highlight rendering.
            int nameStartIndex = fullPath.Length - entry.Name.Length;
            var adjustedPositions = new List<int>();

            foreach (int pos in result.MatchPositions)
            {
                if (pos >= nameStartIndex)
                    adjustedPositions.Add(pos - nameStartIndex);
            }

            bool isFav = Favorites.IsStarred(ContextKey, entry.Id);
            bool isRec = Recent.IsRecent(ContextKey, entry.Id);

            Filtered.Add(new RankedEntry(entry, result.Score, adjustedPositions, isFav, isRec));
        }

        // Sort: favorites first, then recents, then by score desc, then name asc.
        Filtered.Sort((a, b) =>
        {
            if (a.IsFavorite != b.IsFavorite) return a.IsFavorite ? -1 : 1;
            if (a.IsRecent   != b.IsRecent)   return a.IsRecent   ? -1 : 1;
            int scoreCmp = b.Score.CompareTo(a.Score);
            if (scoreCmp != 0) return scoreCmp;
            return string.Compare(a.Entry.Name, b.Entry.Name, StringComparison.OrdinalIgnoreCase);
        });

        // Clamp keyboard focus.
        if (KeyboardFocusIndex >= Filtered.Count)
            KeyboardFocusIndex = Filtered.Count - 1;
        if (KeyboardFocusIndex < 0)
            KeyboardFocusIndex = 0;
    }

    /// <summary>Reset all transient state for a new picker session.</summary>
    public void Reset(string contextKey, string initialQuery, PickerSelectionMode mode)
    {
        ContextKey              = contextKey;
        SearchText              = initialQuery;
        LastQuery               = "\u0000";
        SelectionMode           = mode;
        AllEntries              = [];
        Filtered                = [];
        SelectedFilteredIndices = [];
        HighlightedIndices      = [];
        KeyboardFocusIndex      = 0;
        SelectedCategory        = "";
        Confirmed               = false;
        FocusSearchNextFrame    = true;
        IsFirstFrame            = true;
    }
}

/// <summary>A single entry combined with its ranking data for the current query.</summary>
internal sealed record RankedEntry(
    PickerEntry Entry,
    int Score,
    IReadOnlyList<int> MatchPositions,
    bool IsFavorite,
    bool IsRecent);
