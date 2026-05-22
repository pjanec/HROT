namespace NodeEditor.UI.Picker;

/// <summary>
/// In-memory store for starred ("favorite") picker entries, keyed by context key + item key.
/// When IEditorHostServices.Preferences is available the host may persist these externally;
/// this fallback keeps them for the duration of the session only.
/// </summary>
public sealed class FavoritesStore
{
    private readonly Dictionary<string, HashSet<string>> _data = [];

    /// <summary>Return true if the given item is starred in the specified context.</summary>
    public bool IsStarred(string contextKey, string itemKey)
        => _data.TryGetValue(contextKey, out var set) && set.Contains(itemKey);

    /// <summary>Toggle the star state. Returns the new state.</summary>
    public bool Toggle(string contextKey, string itemKey)
    {
        if (!_data.TryGetValue(contextKey, out var set))
        {
            set = [];
            _data[contextKey] = set;
        }

        if (set.Contains(itemKey))
        {
            set.Remove(itemKey);
            return false;
        }

        set.Add(itemKey);
        return true;
    }

    /// <summary>Star an item explicitly.</summary>
    public void Star(string contextKey, string itemKey)
    {
        if (!_data.TryGetValue(contextKey, out var set))
            _data[contextKey] = set = [];
        set.Add(itemKey);
    }

    /// <summary>Unstar an item explicitly.</summary>
    public void Unstar(string contextKey, string itemKey)
    {
        if (_data.TryGetValue(contextKey, out var set))
            set.Remove(itemKey);
    }

    /// <summary>Return all starred item keys for a context.</summary>
    public IReadOnlySet<string> GetStarred(string contextKey)
        => _data.TryGetValue(contextKey, out var set) ? set : (IReadOnlySet<string>)new HashSet<string>();
}
