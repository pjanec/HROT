using System;
using System.Linq;
using FluentAssertions;
using Hrot.BTree.Editor.Host;
using NodeEditor.Core.Interfaces;
using NodeEditor.UI.Picker;
using Xunit;

namespace Hrot.BTree.Editor.Tests.Host;

/// <summary>
/// Tests for <see cref="BTreePickerSources"/> and <see cref="BTreeNodePickerSource"/>.
/// </summary>
public sealed class BTreePickerSourceTests
{
    [Fact]
    public void Register_AddsNodesAllSource()
    {
        var registry = new PickerRegistry();
        var catalog  = new BTreeNodeCatalog();

        BTreePickerSources.Register(registry, catalog);

        var source = registry.Get<NodeCatalogEntry>("nodes.all");
        source.Should().NotBeNull("'nodes.all' picker source must be registered");
    }

    [Fact]
    public void Register_AddsNodesByPinSource()
    {
        var registry = new PickerRegistry();
        var catalog  = new BTreeNodeCatalog();

        BTreePickerSources.Register(registry, catalog);

        var source = registry.Get<NodeCatalogEntry>("nodes.by-pin");
        source.Should().NotBeNull("'nodes.by-pin' picker source must be registered");
    }

    [Fact]
    public void PickerSource_Query_ReturnsCatalogEntries()
    {
        var source = new BTreeNodeCatalog();

        // The Sequence entry should be found by text search.
        var results = source.Query(new NodeSearchQuery("Sequence"));

        results.Should().ContainSingle(
            e => e.Kind.Id == "bt.composite.sequence",
            "querying for 'Sequence' should return the Sequence catalog entry");
    }

    [Fact]
    public void PickerSource_Query_ReturnsSequenceByDisplayName()
    {
        var source  = new BTreeNodeCatalog();
        var picker  = new BTreeNodePickerSourceInvoker(source);

        var results = picker.Query("Sequence", null);

        results.Should().Contain(
            e => e.Kind.Id == "bt.composite.sequence",
            "querying 'Sequence' should return the Sequence entry via the picker source");
    }

    [Fact]
    public void PickerSource_Query_Empty_ReturnsManyStatics()
    {
        var source  = new BTreeNodeCatalog();
        var picker  = new BTreeNodePickerSourceInvoker(source);

        var results = picker.Query("", null);

        results.Count.Should().BeGreaterThanOrEqualTo(
            10, "empty query should return all static composite/leaf/decorator entries");
    }

    [Fact]
    public void PickerSource_GetItemKey_IsKindId()
    {
        var source  = new BTreeNodeCatalog();
        var picker  = new BTreeNodePickerSourceInvoker(source);

        var sequence = source.Query(new NodeSearchQuery("Sequence"))
            .Single(e => e.Kind.Id == "bt.composite.sequence");

        var key = picker.GetItemKey(sequence);

        key.Should().Be(sequence.Kind.Id, "GetItemKey must return the Kind.Id");
        key.Should().Be("bt.composite.sequence");
    }

    // ── DEC-08: GetCategory / GetIconKey ─────────────────────────────────────

    [Fact]
    public void PickerSource_GetCategory_ReturnsCategoryPath()
    {
        var source  = new BTreeNodeCatalog();
        var picker  = new BTreeNodePickerSourceInvoker(source);

        var sequence = source.Query(new NodeSearchQuery("Sequence"))
            .Single(e => e.Kind.Id == "bt.composite.sequence");

        var category = picker.GetCategory(sequence);

        category.Should().Be(sequence.CategoryPath,
            "GetCategory must return the entry's CategoryPath");
        category.Should().NotBeNullOrEmpty(
            "bt.composite.sequence is a Composite and must have a non-empty CategoryPath");
    }

    [Fact]
    public void PickerSource_GetIconKey_ReturnsIconKey()
    {
        var source  = new BTreeNodeCatalog();
        var picker  = new BTreeNodePickerSourceInvoker(source);

        var sequence = source.Query(new NodeSearchQuery("Sequence"))
            .Single(e => e.Kind.Id == "bt.composite.sequence");

        var iconKey = picker.GetIconKey(sequence);

        iconKey.Should().Be(sequence.IconKey,
            "GetIconKey must return the entry's IconKey");
    }

    [Fact]
    public void PickerSource_GetCategory_AllEntries_MatchCatalog()
    {
        var source  = new BTreeNodeCatalog();
        var picker  = new BTreeNodePickerSourceInvoker(source);

        var all = picker.Query("", null);

        foreach (var entry in all)
        {
            picker.GetCategory(entry).Should().Be(entry.CategoryPath,
                $"GetCategory for '{entry.Kind.Id}' must equal its CategoryPath");
        }
    }

    [Fact]
    public void PickerSource_GetIconKey_AllEntries_MatchCatalog()
    {
        var source  = new BTreeNodeCatalog();
        var picker  = new BTreeNodePickerSourceInvoker(source);

        var all = picker.Query("", null);

        foreach (var entry in all)
        {
            picker.GetIconKey(entry).Should().Be(entry.IconKey,
                $"GetIconKey for '{entry.Kind.Id}' must equal its IconKey");
        }
    }

    // ── Helper: exposes internal BTreeNodePickerSource members for testing ──

    /// <summary>
    /// Thin wrapper that delegates to the internal <see cref="BTreeNodePickerSource"/>
    /// via a stored <see cref="IPickerSource{NodeCatalogEntry}"/> reference obtained
    /// through <see cref="PickerRegistry.Get{TItem}"/> after registration.
    /// </summary>
    private sealed class BTreeNodePickerSourceInvoker
    {
        private readonly IPickerSource<NodeCatalogEntry> _source;

        public BTreeNodePickerSourceInvoker(BTreeNodeCatalog catalog)
        {
            // Register into a temporary registry to obtain the typed source reference.
            var registry = new PickerRegistry();
            BTreePickerSources.Register(registry, catalog);
            _source = registry.Get<NodeCatalogEntry>("nodes.all")!;
        }

        public IReadOnlyList<NodeCatalogEntry> Query(
            string text,
            IReadOnlyDictionary<string, object?>? context)
            => _source.Query(text, context);

        public string GetItemKey(NodeCatalogEntry item)
            => _source.GetItemKey(item);

        public string? GetCategory(NodeCatalogEntry item)
            => _source.GetCategory(item);

        public string? GetIconKey(NodeCatalogEntry item)
            => _source.GetIconKey(item);
    }
}
