using System.Text.Json;
using NodeEditor.Core.IO;
using NodeEditor.Core.Interfaces;

namespace NodeEditor.UI.Picker;

/// <summary>
/// ⭐ CE-4168 (AQ108 D26 P1/P4) — the per-picker VIEW choices a user makes and expects to find again: the layout they toggled
/// to (<see cref="PickerRequest.LayoutChoices"/>) and the width they dragged the side pane to, both keyed by
/// <see cref="PickerRequest.ContextKey"/>. Same persistence style as <see cref="RecentStore"/>: <see cref="Load"/> binds a file,
/// every change rewrites it, a missing/unreadable file is an empty store, a failed write is ignored; <c>new PickerViewStore()</c> is in memory.
/// </summary>
public sealed class PickerViewStore
{
    private const int FormatVersion = 1;

    private readonly Dictionary<string, PickerLayout> _layouts = [];
    private readonly Dictionary<string, float> _paneWidths = [];

    /// <summary>The file this store is bound to (<see cref="Load"/>); null for an in-memory store.</summary>
    public string? PersistPath { get; private set; }

    /// <summary>The layout remembered for <paramref name="contextKey"/>, or null.</summary>
    public PickerLayout? GetLayout(string contextKey)
        => _layouts.TryGetValue(contextKey, out var l) ? l : null;

    /// <summary>Remembers <paramref name="layout"/> for <paramref name="contextKey"/> (and saves).</summary>
    public void SetLayout(string contextKey, PickerLayout layout)
    {
        if (_layouts.TryGetValue(contextKey, out var cur) && cur == layout) return;
        _layouts[contextKey] = layout;
        Save();
    }

    /// <summary>The side-pane width remembered for <paramref name="contextKey"/>, or null.</summary>
    public float? GetPaneWidth(string contextKey)
        => _paneWidths.TryGetValue(contextKey, out var w) ? w : null;

    /// <summary>Remembers the side-pane width for <paramref name="contextKey"/> (and saves).</summary>
    public void SetPaneWidth(string contextKey, float width)
    {
        if (_paneWidths.TryGetValue(contextKey, out var cur) && Math.Abs(cur - width) < 0.5f) return;
        _paneWidths[contextKey] = width;
        Save();
    }

    /// <summary>
    /// The layout a picker opens in: the remembered one when it is among <paramref name="choices"/>, else <paramref name="requested"/>.
    /// No choices ⇒ always <paramref name="requested"/> (a picker that does not opt in never reads the store).
    /// </summary>
    public PickerLayout ResolveLayout(string contextKey, PickerLayout requested, IReadOnlyList<PickerLayout>? choices)
        => choices is { Count: > 0 } && GetLayout(contextKey) is { } l && choices.Contains(l) ? l : requested;

    /// <summary>Opens the store persisted at <paramref name="path"/>; later changes are written back.</summary>
    public static PickerViewStore Load(string path)
    {
        var store = new PickerViewStore { PersistPath = path };
        try
        {
            if (SafeTextFile.TryRead(path) is { } json) store.ReadJson(json);
        }
        catch (Exception) { /* unreadable ⇒ empty */ }
        return store;
    }

    /// <summary>Writes the store to <see cref="PersistPath"/>; false when unbound or it failed. Never throws.</summary>
    public bool Save() => SafeTextFile.TryWrite(PersistPath, ToJson());

    /// <summary>The store as JSON.</summary>
    public string ToJson()
        => JsonSerializer.Serialize(
            new Dto
            {
                Version = FormatVersion,
                Layouts = _layouts.ToDictionary(kv => kv.Key, kv => kv.Value.ToString()),
                PaneWidths = new Dictionary<string, float>(_paneWidths),
            },
            new JsonSerializerOptions { WriteIndented = true });

    private void ReadJson(string json)
    {
        var dto = JsonSerializer.Deserialize<Dto>(json);
        if (dto is null) return;
        if (dto.Layouts is not null)
            foreach (var (k, v) in dto.Layouts)
                if (Enum.TryParse<PickerLayout>(v, out var l)) _layouts[k] = l;
        if (dto.PaneWidths is not null)
            foreach (var (k, v) in dto.PaneWidths)
                if (v > 0f) _paneWidths[k] = v;
    }

    private sealed class Dto
    {
        public int Version { get; set; }
        public Dictionary<string, string>? Layouts { get; set; }
        public Dictionary<string, float>? PaneWidths { get; set; }
    }
}
