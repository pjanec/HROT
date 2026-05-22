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
