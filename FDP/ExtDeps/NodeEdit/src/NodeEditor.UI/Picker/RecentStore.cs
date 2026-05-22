namespace NodeEditor.UI.Picker;

/// <summary>
/// In-memory store for recently picked items, capped to <see cref="MaxItems"/> per context.
/// Pushed on every confirmed pick; duplicates are moved to front.
/// </summary>
public sealed class RecentStore
{
    /// <summary>Maximum number of recent entries retained per context key.</summary>
    public const int MaxItems = 16;

    private readonly Dictionary<string, List<string>> _data = [];

    /// <summary>Record that <paramref name="itemKey"/> was picked in the given context.</summary>
    public void Push(string contextKey, string itemKey)
    {
        if (!_data.TryGetValue(contextKey, out var list))
        {
            list = new List<string>(MaxItems);
            _data[contextKey] = list;
        }

        // Move to front if already present.
        int existing = list.IndexOf(itemKey);
        if (existing >= 0) list.RemoveAt(existing);

        list.Insert(0, itemKey);

        // Trim to cap.
        while (list.Count > MaxItems)
            list.RemoveAt(list.Count - 1);
    }

    /// <summary>Return recent item keys in most-recent-first order.</summary>
    public IReadOnlyList<string> GetRecent(string contextKey)
        => _data.TryGetValue(contextKey, out var list)
            ? list
            : (IReadOnlyList<string>)Array.Empty<string>();

    /// <summary>Return true if <paramref name="itemKey"/> appears in the recents list.</summary>
    public bool IsRecent(string contextKey, string itemKey)
        => _data.TryGetValue(contextKey, out var list) && list.Contains(itemKey);
}
