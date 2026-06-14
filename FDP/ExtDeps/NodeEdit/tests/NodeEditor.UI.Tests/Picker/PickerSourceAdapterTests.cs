using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NodeEditor.Core.Interfaces;
using NodeEditor.UI.Picker;
using Xunit;

namespace NodeEditor.UI.Tests.Picker;

/// <summary>
/// Tests that <see cref="PickerSourceAdapter{TItem}"/> correctly populates
/// <see cref="AdaptedItem.Category"/> and <see cref="AdaptedItem.IconKey"/>
/// from the source's <c>GetCategory</c>/<c>GetIconKey</c> members (DEC-08 Part A).
/// </summary>
public sealed class PickerSourceAdapterTests
{
    // ── helpers ───────────────────────────────────────────────────────────────

    private sealed record FakeItem(string Name, string? Category, string? Icon);

    /// <summary>Picker source that exposes category and icon data.</summary>
    private sealed class FakePickerSource : IPickerSource<FakeItem>
    {
        private readonly IReadOnlyList<FakeItem> _items;

        public FakePickerSource(IReadOnlyList<FakeItem> items) => _items = items;

        public string Title => "Fake";
        public string EmptyResultText => "(none)";
        public PickerLayout PreferredLayout => PickerLayout.Standard;
        public PickerSelectionMode SelectionMode => PickerSelectionMode.Single;
        public QueryCost Cost => QueryCost.Cheap;
        public bool IsAsync => false;
        public bool AllowsDragOut => false;
        public bool AllowsDragIn => false;
        public bool AllowArbitraryTextInput => false;

        public IReadOnlyList<FakeItem> Query(string text, IReadOnlyDictionary<string, object?>? ctx) => _items;
        public Task<IReadOnlyList<FakeItem>> QueryAsync(string text, IReadOnlyDictionary<string, object?>? ctx, CancellationToken ct)
            => Task.FromResult(Query(text, ctx));

        public void RenderItem(FakeItem item, bool selected, bool keyboardFocused, IPickerRenderContext ctx) { }
        public void RenderPreview(FakeItem item, IPickerRenderContext ctx) { }
        public bool IsPreviewExpensive(FakeItem item) => false;
        public string GetSearchableText(FakeItem item) => item.Name;
        public string GetItemKey(FakeItem item) => item.Name;
        public bool CanAcceptDrop(object payload) => false;

        // DEC-08: override default members to supply real data
        public string? GetCategory(FakeItem item) => item.Category;
        public string? GetIconKey(FakeItem item) => item.Icon;
    }

    /// <summary>Source that does NOT override GetCategory/GetIconKey (relies on defaults).</summary>
    private sealed class MinimalPickerSource : IPickerSource<string>
    {
        public string Title => "Minimal";
        public string EmptyResultText => "(none)";
        public PickerLayout PreferredLayout => PickerLayout.Standard;
        public PickerSelectionMode SelectionMode => PickerSelectionMode.Single;
        public QueryCost Cost => QueryCost.Cheap;
        public bool IsAsync => false;
        public bool AllowsDragOut => false;
        public bool AllowsDragIn => false;
        public bool AllowArbitraryTextInput => false;

        public IReadOnlyList<string> Query(string text, IReadOnlyDictionary<string, object?>? ctx) => new[] { "item" };
        public Task<IReadOnlyList<string>> QueryAsync(string text, IReadOnlyDictionary<string, object?>? ctx, CancellationToken ct)
            => Task.FromResult(Query(text, ctx));

        public void RenderItem(string item, bool selected, bool keyboardFocused, IPickerRenderContext ctx) { }
        public void RenderPreview(string item, IPickerRenderContext ctx) { }
        public bool IsPreviewExpensive(string item) => false;
        public string GetSearchableText(string item) => item;
        public string GetItemKey(string item) => item;
        public bool CanAcceptDrop(object payload) => false;
        // GetCategory / GetIconKey: NOT overridden — use interface defaults (return null)
    }

    // ── tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Query_PopulatesCategory_FromSource()
    {
        var source = new FakePickerSource(new[]
        {
            new FakeItem("Alpha", "Composites", "bt/sequence"),
            new FakeItem("Beta",  "Leaves",     "bt/action"),
        });
        var adapter = new PickerSourceAdapter<FakeItem>(source);

        var items = adapter.Query("", null);

        items[0].Category.Should().Be("Composites");
        items[1].Category.Should().Be("Leaves");
    }

    [Fact]
    public void Query_PopulatesIconKey_FromSource()
    {
        var source = new FakePickerSource(new[]
        {
            new FakeItem("Alpha", "Composites", "bt/sequence"),
            new FakeItem("Beta",  null,          null),
        });
        var adapter = new PickerSourceAdapter<FakeItem>(source);

        var items = adapter.Query("", null);

        items[0].IconKey.Should().Be("bt/sequence");
        items[1].IconKey.Should().BeNull();
    }

    [Fact]
    public async Task QueryAsync_PopulatesCategory_FromSource()
    {
        var source = new FakePickerSource(new[]
        {
            new FakeItem("X", "Decorators", "bt/decorator"),
        });
        var adapter = new PickerSourceAdapter<FakeItem>(source);

        var items = await adapter.QueryAsync("", null, CancellationToken.None);

        items[0].Category.Should().Be("Decorators");
        items[0].IconKey.Should().Be("bt/decorator");
    }

    [Fact]
    public void Query_Category_IsNull_WhenSourceDoesNotOverride()
    {
        var source = new MinimalPickerSource();
        var adapter = new PickerSourceAdapter<string>(source);

        var items = adapter.Query("", null);

        items[0].Category.Should().BeNull("default IPickerSource.GetCategory returns null");
        items[0].IconKey.Should().BeNull("default IPickerSource.GetIconKey returns null");
    }

    [Fact]
    public void Query_NullCategory_WhenItemHasNullCategory()
    {
        var source = new FakePickerSource(new[]
        {
            new FakeItem("Gamma", null, null),
        });
        var adapter = new PickerSourceAdapter<FakeItem>(source);

        var items = adapter.Query("", null);

        items[0].Category.Should().BeNull();
        items[0].IconKey.Should().BeNull();
    }
}
