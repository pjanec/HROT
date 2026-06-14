using NodeEditor.Core.Interfaces;

namespace NodeEditor.UI.Picker;

/// <summary>
/// Internal non-generic wrapper over <see cref="IPickerSource{TItem}"/>
/// that erases the item type so the window can work with heterogeneous sources.
/// </summary>
internal interface IPickerSourceAdapter
{
    string Title { get; }
    string EmptyResultText { get; }
    PickerLayout PreferredLayout { get; }
    PickerSelectionMode SelectionMode { get; }
    QueryCost Cost { get; }
    bool IsAsync { get; }
    bool AllowsDragOut { get; }

    /// <summary>Synchronous query; returns adapted items.</summary>
    IReadOnlyList<AdaptedItem> Query(string text, IReadOnlyDictionary<string, object?>? context);

    /// <summary>Async query; previous call should be cancelled via cts first.</summary>
    Task<IReadOnlyList<AdaptedItem>> QueryAsync(
        string text,
        IReadOnlyDictionary<string, object?>? context,
        CancellationToken ct);

    /// <summary>Delegate rendering to the source. Called only in source-driven mode.</summary>
    void RenderItem(AdaptedItem item, bool selected, bool keyboardFocused, IPickerRenderContext ctx);

    /// <summary>Delegate preview rendering to the source.</summary>
    void RenderPreview(AdaptedItem item, IPickerRenderContext ctx);

    /// <summary>Whether preview generation is expensive (should be deferred).</summary>
    bool IsPreviewExpensive(AdaptedItem item);
}

/// <summary>
/// An item returned from an adapter query. Carries the stable key, the
/// searchable text, and the original boxed item as <see cref="Raw"/>.
/// </summary>
/// <param name="Key">Stable string key from <see cref="IPickerSource{TItem}.GetItemKey"/>.</param>
/// <param name="SearchText">Searchable text from <see cref="IPickerSource{TItem}.GetSearchableText"/>.</param>
/// <param name="Raw">The original (unboxed) item.</param>
/// <param name="Category">Category path from <see cref="IPickerSource{TItem}.GetCategory"/>; null if not provided.</param>
/// <param name="IconKey"><see cref="IIconProvider"/> key from <see cref="IPickerSource{TItem}.GetIconKey"/>; null if not provided.</param>
internal sealed record AdaptedItem(string Key, string SearchText, object Raw, string? Category = null, string? IconKey = null);

/// <summary>
/// Generic implementation of <see cref="IPickerSourceAdapter"/> that wraps
/// a concrete <see cref="IPickerSource{TItem}"/>.
/// </summary>
internal sealed class PickerSourceAdapter<TItem> : IPickerSourceAdapter
{
    private readonly IPickerSource<TItem> _source;

    public PickerSourceAdapter(IPickerSource<TItem> source) => _source = source;

    /// <summary>Exposes the underlying typed source for <see cref="PickerRegistry.Get{TItem}"/>.</summary>
    public IPickerSource<TItem> Source => _source;

    public string Title              => _source.Title;
    public string EmptyResultText    => _source.EmptyResultText;
    public PickerLayout PreferredLayout => _source.PreferredLayout;
    public PickerSelectionMode SelectionMode => _source.SelectionMode;
    public QueryCost Cost            => _source.Cost;
    public bool IsAsync              => _source.IsAsync;
    public bool AllowsDragOut        => _source.AllowsDragOut;

    public IReadOnlyList<AdaptedItem> Query(string text, IReadOnlyDictionary<string, object?>? context)
    {
        var items = _source.Query(text, context);
        return items.Select(i => new AdaptedItem(
            _source.GetItemKey(i),
            _source.GetSearchableText(i),
            i!,
            _source.GetCategory(i),
            _source.GetIconKey(i))).ToList();
    }

    public async Task<IReadOnlyList<AdaptedItem>> QueryAsync(
        string text,
        IReadOnlyDictionary<string, object?>? context,
        CancellationToken ct)
    {
        var items = await _source.QueryAsync(text, context, ct);
        return items.Select(i => new AdaptedItem(
            _source.GetItemKey(i),
            _source.GetSearchableText(i),
            i!,
            _source.GetCategory(i),
            _source.GetIconKey(i))).ToList();
    }

    public void RenderItem(AdaptedItem item, bool selected, bool keyboardFocused, IPickerRenderContext ctx)
        => _source.RenderItem((TItem)item.Raw, selected, keyboardFocused, ctx);

    public void RenderPreview(AdaptedItem item, IPickerRenderContext ctx)
        => _source.RenderPreview((TItem)item.Raw, ctx);

    public bool IsPreviewExpensive(AdaptedItem item)
        => _source.IsPreviewExpensive((TItem)item.Raw);
}
