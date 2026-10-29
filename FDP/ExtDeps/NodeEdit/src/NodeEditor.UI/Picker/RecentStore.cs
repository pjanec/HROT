using System.Text.Json;
using NodeEditor.Core.IO;

namespace NodeEditor.UI.Picker;

/// <summary>
/// Store for recently picked items, capped to <see cref="MaxItems"/> per context.
/// Pushed on every confirmed pick; duplicates are moved to front.
///
/// <para>⭐ CE-4098 (AQ108 D19) — <b>optionally persisted.</b> <see cref="Load"/> opens a store bound to a file;
/// every <see cref="Push"/> then rewrites it, so the items last picked survive a restart (the user folder, beside
/// the layout's files). ⛔ A store built with <c>new RecentStore()</c> stays in memory, as before. A missing or
/// unreadable file gives an empty store and a failed write is ignored — a recent list must never stop a host.</para>
///
/// <para>⭐ The keys are whatever identity the picker already uses for an item (never an absolute path), so a
/// folder move does not stale them; an entry that no longer resolves is simply never matched by
/// <see cref="IsRecent"/> / <see cref="RankOf"/> because the picker only asks about entries it lists.</para>
/// </summary>
public sealed class RecentStore
{
    /// <summary>Maximum number of recent entries retained per context key.</summary>
    public const int MaxItems = 16;

    private const int FormatVersion = 1;

    private readonly Dictionary<string, List<string>> _data = [];

    /// <summary>The file this store is bound to (<see cref="Load"/>); <see langword="null"/> for an in-memory store.</summary>
    public string? PersistPath { get; private set; }

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

        Save();
    }

    /// <summary>Return recent item keys in most-recent-first order.</summary>
    public IReadOnlyList<string> GetRecent(string contextKey)
        => _data.TryGetValue(contextKey, out var list)
            ? list
            : (IReadOnlyList<string>)Array.Empty<string>();

    /// <summary>Return true if <paramref name="itemKey"/> appears in the recents list.</summary>
    public bool IsRecent(string contextKey, string itemKey)
        => _data.TryGetValue(contextKey, out var list) && list.Contains(itemKey);

    /// <summary>0 = the most recent pick; -1 = not in the list.</summary>
    public int RankOf(string contextKey, string itemKey)
        => _data.TryGetValue(contextKey, out var list) ? list.IndexOf(itemKey) : -1;

    // ── persistence ──────────────────────────────────────────────────────────

    /// <summary>
    /// Opens the store persisted at <paramref name="path"/>: its lists are read (when the file exists and parses;
    /// otherwise the store starts empty) and every later <see cref="Push"/> writes it back.
    /// </summary>
    public static RecentStore Load(string path)
    {
        var store = new RecentStore { PersistPath = path };
        try
        {
            if (SafeTextFile.TryRead(path) is { } json)
                store.ReadJson(json);
        }
        catch (Exception) { /* unreadable ⇒ empty */ }
        return store;
    }

    /// <summary>Writes the lists to <see cref="PersistPath"/> (temp file + move); false when unbound or it failed. Never throws.</summary>
    public bool Save()
    {
        return SafeTextFile.TryWrite(PersistPath, ToJson());
    }

    /// <summary>The lists as JSON (<c>{"version":1,"lists":{"context":["newest", …]}}</c>).</summary>
    public string ToJson()
        => JsonSerializer.Serialize(
            new Dto { Version = FormatVersion, Lists = _data.ToDictionary(kv => kv.Key, kv => kv.Value.ToList()) },
            new JsonSerializerOptions { WriteIndented = true });

    private void ReadJson(string json)
    {
        var dto = JsonSerializer.Deserialize<Dto>(json);
        if (dto?.Lists is null) return;
        foreach (var (context, items) in dto.Lists)
        {
            if (items is null) continue;
            var list = new List<string>(MaxItems);
            foreach (var item in items)
                if (!string.IsNullOrEmpty(item) && !list.Contains(item) && list.Count < MaxItems)
                    list.Add(item);
            if (list.Count > 0) _data[context] = list;
        }
    }

    private sealed class Dto
    {
        public int Version { get; set; }
        public Dictionary<string, List<string>>? Lists { get; set; }
    }
}
