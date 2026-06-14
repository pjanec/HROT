using System.Numerics;

namespace NodeEditor.Core.Interfaces;

/// <summary>
/// Registry of picker sources. The host registers per-context sources
/// (variables, types, assets, etc.) at startup; the editor opens them via
/// <see cref="Open"/>.
/// </summary>
public interface IPickerRegistry
{
    /// <summary>Register a typed picker source under a string key.</summary>
    void Register<TItem>(string sourceKey, IPickerSource<TItem> source);

    /// <summary>Look up a typed source by key.</summary>
    IPickerSource<TItem>? Get<TItem>(string sourceKey);

    /// <summary>
    /// Open the picker for the given source. The picker calls
    /// <paramref name="onPick"/> with the selected item (or list, for multi-select).
    /// </summary>
    void Open(
        string sourceKey,
        Vector2 screenPos,
        System.Action<object> onPick,
        System.Action? onCancel = null,
        IReadOnlyDictionary<string, object?>? context = null);

    /// <summary>
    /// Per-frame draw call. Must be invoked once every ImGui frame by the host so that
    /// an open picker window is rendered and can close. No-ops when no picker is open.
    /// </summary>
    void DrawFrame();
}

/// <summary>A source of pickable items. Generic on item type.</summary>
public interface IPickerSource<TItem>
{
    string Title { get; }
    string EmptyResultText { get; }
    PickerLayout PreferredLayout { get; }
    PickerSelectionMode SelectionMode { get; }
    QueryCost Cost { get; }
    bool IsAsync { get; }
    bool AllowsDragOut { get; }
    bool AllowsDragIn { get; }
    bool AllowArbitraryTextInput { get; }

    IReadOnlyList<TItem> Query(string text, IReadOnlyDictionary<string, object?>? context);

    Task<IReadOnlyList<TItem>> QueryAsync(
        string text,
        IReadOnlyDictionary<string, object?>? context,
        CancellationToken ct);

    void RenderItem(TItem item, bool selected, bool keyboardFocused, IPickerRenderContext ctx);
    void RenderPreview(TItem item, IPickerRenderContext ctx);
    bool IsPreviewExpensive(TItem item);

    string GetSearchableText(TItem item);
    string GetItemKey(TItem item);
    bool CanAcceptDrop(object payload);

    /// <summary>
    /// Returns the category path for the item (e.g. "Composites"). Used for
    /// grouped display in the flat picker list. Default: null (no grouping).
    /// </summary>
    string? GetCategory(TItem item) => null;

    /// <summary>
    /// Returns an <see cref="IIconProvider"/> key for the item's inline row icon.
    /// Default: null (no icon).
    /// </summary>
    string? GetIconKey(TItem item) => null;
}

public enum PickerLayout { Standard, Compact, Wide, Grid, Tree }
public enum PickerSelectionMode { Single, Multi, MultiOrdered }
public enum QueryCost { Cheap, Moderate, Heavy }

/// <summary>Rendering context handed to picker source's RenderItem/RenderPreview.</summary>
public interface IPickerRenderContext
{
    IIconProvider Icons { get; }
    IEditorTheme Theme { get; }
    IReadOnlyList<int>? MatchPositions { get; }
}
