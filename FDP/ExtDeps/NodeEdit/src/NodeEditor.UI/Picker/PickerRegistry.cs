using System.Numerics;
using NodeEditor.Core.Interfaces;

namespace NodeEditor.UI.Picker;

/// <summary>
/// Default implementation of <see cref="IPickerRegistry"/> for the UI layer.
/// Hosts register typed <see cref="IPickerSource{TItem}"/> instances here at startup;
/// the editor opens them via <see cref="Open"/>.
///
/// Additionally exposes <see cref="OpenPicker"/> (entry-driven convenience API) and
/// <see cref="DrawFrame"/> which must be called once per ImGui frame by the host.
/// </summary>
public sealed class PickerRegistry : IPickerRegistry
{
    private readonly PickerWindow _window = new();
    private readonly Dictionary<string, IPickerSourceAdapter> _adapters = [];

    // Optional host services for rendering.
    private IIconProvider? _icons;
    private IEditorTheme?  _theme;

    /// <summary>
    /// Provide rendering services (icons, theme) to the picker window.
    /// Call once after construction if available.
    /// </summary>
    public void SetServices(IIconProvider icons, IEditorTheme theme)
    {
        _icons = icons;
        _theme = theme;
        _window.Icons = icons;
        _window.Theme = theme;
    }

    /// <summary>
    /// Provide only an icon provider, keeping the default theme — for a host that has icons but no editor theme.
    /// </summary>
    public void SetIcons(IIconProvider icons)
    {
        _icons = icons;
        _window.Icons = icons;
    }

    /// <summary>
    /// ⭐ CE-4098 (AQ108 D19) — the Recent lists every picker of this registry shows first and every confirmed pick pushes to.
    /// A host sets its persisted store (<see cref="RecentStore.Load"/>); the default is in-memory.
    /// </summary>
    public RecentStore Recent
    {
        get => _window.Recent;
        set => _window.Recent = value;
    }

    /// <summary>⭐ CE-4168 (AQ108 D26 P4/P1) — the remembered layout toggles and side-pane widths per picker context; a host sets a persisted store
    /// (<see cref="PickerViewStore.Load"/>), the default is in-memory.</summary>
    public PickerViewStore View
    {
        get => _window.View;
        set => _window.View = value ?? new PickerViewStore();
    }

    /// <summary>⭐ CE-4168 (AQ108 D26 P5) — THE shared lazy image cache of every picker of this registry; a host swaps in one built with its
    /// texture loader/unloader. The default loads nothing.</summary>
    public PickerImageCache Images
    {
        get => _window.Images;
        set => _window.Images = value;
    }

    /// <summary>The one picker window — for the headless rails.</summary>
    internal PickerWindow Window => _window;

    /// <summary>⭐ CE-4098 — the Recent section of the picker open now, last picked first (what every layout shows before any other row);
    /// empty when none is open.</summary>
    public IReadOnlyList<PickerEntry> RecentSection() => _window.RecentSection();

    /// <summary>⭐ CE-4098 — confirms the open picker's entry <paramref name="id"/> as a click on OK would (the pick reaches the host's
    /// callback and the entry's Recent list). False when none is open or the entry is not listed.</summary>
    public bool ConfirmEntry(string id, string? actionId = null) => _window.ConfirmEntry(id, actionId);

    /// <summary>⭐ CE-4359 (rails) — the open Tree picker's rows as last drawn: folders and leaves in order with their depth.</summary>
    public IReadOnlyList<PickerTreeRow> TreeRows() => _window.TreeRows();

    /// <summary>⭐ CE-4359 (rails) — selects an entry of the open picker as a click on it would.</summary>
    public bool SelectEntry(string id) => _window.SelectEntry(id);

    /// <summary>⭐ CE-4359 (rails) — opens a Tree folder of the open picker.</summary>
    public void ExpandFolder(string path) => _window.ExpandFolder(path);

    /// <summary>⭐ CE-4398 — types into the open picker's search box (a rail's stand-in).</summary>
    public void SetSearch(string text) => _window.SetSearch(text);

    /// <summary>⭐ CE-4359 (rails) — the open picker's hide tick (<see cref="PickerHideToggle"/>) and its listed entry count.</summary>
    public bool ShowHidden { get => _window.ShowHidden; set => _window.ShowHidden = value; }
    public int ListedCount => _window.ListedCount;

    // ── IPickerRegistry ───────────────────────────────────────────────────────

    /// <inheritdoc/>
    public void Register<TItem>(string sourceKey, IPickerSource<TItem> source)
        => _adapters[sourceKey] = new PickerSourceAdapter<TItem>(source);

    /// <inheritdoc/>
    public IPickerSource<TItem>? Get<TItem>(string sourceKey)
    {
        if (_adapters.TryGetValue(sourceKey, out var raw) &&
            raw is PickerSourceAdapter<TItem> typed)
            return typed.Source;

        return null;
    }

    /// <inheritdoc/>
    public void Open(
        string sourceKey,
        Vector2 screenPos,
        System.Action<object> onPick,
        System.Action? onCancel = null,
        IReadOnlyDictionary<string, object?>? context = null)
    {
        if (!_adapters.TryGetValue(sourceKey, out var adapter))
        {
            // Unknown source key: cancel immediately.
            onCancel?.Invoke();
            return;
        }

        _window.OpenFromAdapter(adapter, sourceKey, screenPos, onPick, onCancel, context);
    }

    // ── Additional public surface ─────────────────────────────────────────────

    /// <summary>
    /// Open the picker from a fully data-driven <see cref="PickerRequest"/>.
    /// Calls <paramref name="onChosen"/> when the user confirms or cancels.
    /// Cancels any previously open picker session.
    /// </summary>
    public void OpenPicker(PickerRequest request, System.Action<PickerResult> onChosen)
        => _window.Open(request, onChosen);

    /// <summary>
    /// Per-frame draw call. Must be invoked every ImGui frame by the host.
    /// Renders the active picker window (if any); no-ops when picker is closed.
    /// </summary>
    public void DrawFrame()
        => _window.DrawFrame();
}
