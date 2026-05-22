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

    // ── IPickerRegistry ───────────────────────────────────────────────────────

    /// <inheritdoc/>
    public void Register<TItem>(string sourceKey, IPickerSource<TItem> source)
        => _adapters[sourceKey] = new PickerSourceAdapter<TItem>(source);

    /// <inheritdoc/>
    public IPickerSource<TItem>? Get<TItem>(string sourceKey)
    {
        if (_adapters.TryGetValue(sourceKey, out var raw) &&
            raw is PickerSourceAdapter<TItem> typed)
            return null; // adapter wraps the source; use Open() instead

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
