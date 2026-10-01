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
    public int SelectionAnchorIndex;

    // ── Wide layout sidebar ───────────────────────────────────────────────────
    public string SelectedCategory = "";

    // ── Tree layout state ─────────────────────────────────────────────────────

    /// <summary>One-shot toggle request: folder path to expand/collapse next frame (Tree layout only).</summary>
    public string? PendingToggleFolderPath;

    /// <summary>Target open state for <see cref="PendingToggleFolderPath"/>.</summary>
    public bool PendingToggleOpen;

    /// <summary>Per-frame visual row list built by TreeLayout in render order (DFS).</summary>
    public List<TreeRow> VisualRows = new();

    /// <summary>Keyboard focus index into <see cref="VisualRows"/> (Tree layout only).</summary>
    public int TreeFocusRow;

    internal readonly record struct TreeRow(bool IsFolder, string FolderPath, int FilteredIndex, int Depth);

    // ── Scroll ───────────────────────────────────────────────────────────────
    /// <summary>One-shot flag: set by keyboard navigation, consumed by the next
    /// focused-row render to scroll it into view. Cleared after the scroll fires,
    /// so mouse-wheel scrolling isn't snapped back on subsequent frames.</summary>
    public bool ScrollToFocus;

    // ── Initial selection (PickerRequest.InitialSelectionId) ─────────────────
    /// <summary>Category path whose folders the Tree layout force-opens on its next draw, then clears.</summary>
    public string? RevealCategory;

    /// <summary>Filtered index of a leaf the Tree layout must make the focused visual row on its next draw.</summary>
    public int FocusLeafOnDraw = -1;

    // ── Misc ─────────────────────────────────────────────────────────────────
    public bool Confirmed;
    public bool FocusSearchNextFrame = true;
    public bool IsFirstFrame = true;

    // ── Methods ──────────────────────────────────────────────────────────────

    /// <summary>Recompute <see cref="Filtered"/> from <see cref="AllEntries"/> using the current query.</summary>
    /// <summary>
    /// Pre-selects the (enabled) entry whose <see cref="PickerEntry.Id"/> is <paramref name="id"/> in the
    /// current filtered list. Returns <see langword="false"/> — and changes nothing — when there is none.
    /// </summary>
    public bool ApplyInitialSelection(string? id)
    {
        if (string.IsNullOrEmpty(id)) return false;
        int idx = Filtered.FindIndex(r => r.Entry.Id == id && r.Entry.IsEnabled);
        if (idx < 0) return false;

        SelectedFilteredIndices.Clear();
        SelectedFilteredIndices.Add(idx);
        KeyboardFocusIndex = idx;
        SelectionAnchorIndex = idx;
        RevealCategory = Filtered[idx].Entry.Category;
        FocusLeafOnDraw = idx;
        ScrollToFocus = true;
        return true;
    }

    /// <summary>
    /// What a confirm returns: the selected entries (or, with none selected, the focused one), in list
    /// order, with DISABLED entries removed. <paramref name="onlyDisabled"/> is true when something was
    /// selected but all of it was disabled — the window then stays open instead of returning nothing.
    /// </summary>
    public List<RankedEntry> ConfirmableSelection(out bool onlyDisabled)
    {
        var picked = SelectedFilteredIndices.Count > 0
            ? SelectedFilteredIndices.Where(i => i >= 0 && i < Filtered.Count).OrderBy(i => i).ToList()
            : Filtered.Count > 0 ? new List<int> { KeyboardFocusIndex >= 0 ? KeyboardFocusIndex : 0 } : new List<int>();

        var enabled = picked.Select(i => Filtered[i]).Where(r => r.Entry.IsEnabled).ToList();
        onlyDisabled = picked.Count > 0 && enabled.Count == 0;
        return enabled;
    }

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
        SelectionAnchorIndex    = 0;
        SelectedCategory        = "";
        PendingToggleFolderPath = null;
        PendingToggleOpen       = false;
        VisualRows.Clear();
        RevealCategory          = null;
        FocusLeafOnDraw         = -1;
        TreeFocusRow            = 0;
        ScrollToFocus           = false;
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
