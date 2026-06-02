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
/// Tests for <see cref="PickerRegistry"/> — specifically DEBT-003: Get&lt;TItem&gt;
/// previously returned null for a registered source.
/// </summary>
public sealed class PickerRegistryTests
{
    [Fact]
    public void PickerRegistry_Get_ReturnsRegisteredSource()
    {
        var registry = new PickerRegistry();
        var source   = new StringPickerSource(new[] { "Alpha", "Beta", "Gamma" });

        registry.Register("strings", source);

        var retrieved = registry.Get<string>("strings");

        retrieved.Should().NotBeNull("Get must return the registered source");
        retrieved.Should().BeSameAs(source, "Get must return the exact same instance");
    }

    [Fact]
    public void PickerRegistry_Get_ReturnsNull_WhenNotRegistered()
    {
        var registry = new PickerRegistry();

        var result = registry.Get<string>("not_registered");

        result.Should().BeNull("unknown key must yield null");
    }

    [Fact]
    public void PickerRegistry_Get_ReturnsNull_WhenTypeMismatch()
    {
        // Register as string; request as int.
        var registry = new PickerRegistry();
        registry.Register("strings", new StringPickerSource(Array.Empty<string>()));

        var result = registry.Get<int>("strings");

        result.Should().BeNull("type mismatch must yield null, not throw");
    }

    [Fact]
    public void PickerRegistry_Get_ReturnsCorrectSource_AfterMultipleRegistrations()
    {
        var registry = new PickerRegistry();
        var src1 = new StringPickerSource(new[] { "A" });
        var src2 = new StringPickerSource(new[] { "X", "Y" });

        registry.Register("src1", src1);
        registry.Register("src2", src2);

        registry.Get<string>("src1").Should().BeSameAs(src1);
        registry.Get<string>("src2").Should().BeSameAs(src2);
    }

    [Fact]
    public void PickerRegistry_Get_ReturnsNewSource_AfterReregistration()
    {
        var registry = new PickerRegistry();
        var old = new StringPickerSource(new[] { "old" });
        var newSrc = new StringPickerSource(new[] { "new" });

        registry.Register("key", old);
        registry.Register("key", newSrc);   // re-register overwrites

        registry.Get<string>("key").Should().BeSameAs(newSrc);
    }

    // ── minimal stub ──────────────────────────────────────────────────────────

    private sealed class StringPickerSource : IPickerSource<string>
    {
        private readonly IReadOnlyList<string> _items;

        public StringPickerSource(IReadOnlyList<string> items) => _items = items;

        public string Title => "Strings";
        public string EmptyResultText => "(none)";
        public PickerLayout PreferredLayout => PickerLayout.Standard;
        public PickerSelectionMode SelectionMode => PickerSelectionMode.Single;
        public QueryCost Cost => QueryCost.Cheap;
        public bool IsAsync => false;
        public bool AllowsDragOut => false;
        public bool AllowsDragIn => false;
        public bool AllowArbitraryTextInput => false;

        public IReadOnlyList<string> Query(string text, IReadOnlyDictionary<string, object?>? ctx)
            => _items.Where(s => s.Contains(text, StringComparison.OrdinalIgnoreCase)).ToList();

        public Task<IReadOnlyList<string>> QueryAsync(string text, IReadOnlyDictionary<string, object?>? ctx, CancellationToken ct)
            => Task.FromResult(Query(text, ctx));

        public void RenderItem(string item, bool selected, bool keyboardFocused, IPickerRenderContext ctx) { }
        public void RenderPreview(string item, IPickerRenderContext ctx) { }
        public bool IsPreviewExpensive(string item) => false;
        public string GetSearchableText(string item) => item;
        public string GetItemKey(string item) => item;
        public bool CanAcceptDrop(object payload) => false;
    }
}
