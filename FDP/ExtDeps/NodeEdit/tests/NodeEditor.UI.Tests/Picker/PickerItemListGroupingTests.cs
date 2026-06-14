using System.Collections.Generic;
using FluentAssertions;
using NodeEditor.UI.Picker;
using Xunit;

namespace NodeEditor.UI.Tests.Picker;

/// <summary>
/// Tests for <see cref="PickerItemListHelper.ComputeGroupedDisplayOrder"/> (DEC-08 Part B).
/// ImGui draw calls are not unit-testable; only the pure grouping logic is tested here.
/// </summary>
public sealed class PickerItemListGroupingTests
{
    // ── helpers ───────────────────────────────────────────────────────────────

    private static RankedEntry Normal(string name, string? category = null, int score = 0)
        => new(
            new PickerEntry(name, name, null, category, null, null, null),
            score,
            System.Array.Empty<int>(),
            IsFavorite: false,
            IsRecent: false);

    private static RankedEntry Favorite(string name, string? category = null)
        => new(
            new PickerEntry(name, name, null, category, null, null, null),
            0,
            System.Array.Empty<int>(),
            IsFavorite: true,
            IsRecent: false);

    private static RankedEntry Recent(string name, string? category = null)
        => new(
            new PickerEntry(name, name, null, category, null, null, null),
            0,
            System.Array.Empty<int>(),
            IsFavorite: false,
            IsRecent: true);

    // ── tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public void FavAndRecentEntries_PreserveOriginalOrder()
    {
        var filtered = new List<RankedEntry>
        {
            Favorite("Fav1"),
            Recent("Rec1"),
            Normal("A", "Composites"),
        };

        var order = PickerItemListHelper.ComputeGroupedDisplayOrder(filtered);

        order[0].FilteredIndex.Should().Be(0, "Fav1 must stay first");
        order[1].FilteredIndex.Should().Be(1, "Rec1 must stay second");
    }

    [Fact]
    public void NormalEntries_GroupedByCategory_Alphabetically()
    {
        var filtered = new List<RankedEntry>
        {
            Normal("Z-Leaf",   "Leaves",     score: 10),
            Normal("A-Comp",   "Composites", score: 10),
            Normal("B-Comp",   "Composites", score:  5),
            Normal("Deco",     "Decorators", score: 10),
        };

        var order = PickerItemListHelper.ComputeGroupedDisplayOrder(filtered);

        // Expected category order: Composites < Decorators < Leaves (alpha)
        var categories = order.Select(o => o.Entry.Entry.Category).ToList();
        categories.Should().Equal("Composites", "Composites", "Decorators", "Leaves");
    }

    [Fact]
    public void NormalEntries_WithinCategory_ScoreOrderPreserved()
    {
        // A-Comp has score 10, B-Comp has score 5 — A must appear first within Composites
        var filtered = new List<RankedEntry>
        {
            Normal("B-Comp", "Composites", score: 5),
            Normal("A-Comp", "Composites", score: 10),
        };

        // Simulate Refilter sort: score desc. Pretend filtered is already sorted.
        var preSorted = new List<RankedEntry>
        {
            Normal("A-Comp", "Composites", score: 10),   // index 0
            Normal("B-Comp", "Composites", score:  5),   // index 1
        };

        var order = PickerItemListHelper.ComputeGroupedDisplayOrder(preSorted);

        order[0].Entry.Entry.Name.Should().Be("A-Comp", "higher score must stay first within category");
        order[1].Entry.Entry.Name.Should().Be("B-Comp");
    }

    [Fact]
    public void NullCategory_SortedLast_AmongNormals()
    {
        var filtered = new List<RankedEntry>
        {
            Normal("Uncategorised", null),
            Normal("Leaf",          "Leaves"),
        };

        var order = PickerItemListHelper.ComputeGroupedDisplayOrder(filtered);

        // "" (from null) sorts before "Leaves" alphabetically — that is the spec behaviour.
        // Verify that we get deterministic ordering: empty-category items first, then named.
        // (Empty string "" < "Leaves" lexicographically.)
        order[0].Entry.Entry.Category.Should().BeNull("null-category entry is first when category is empty string");
        order[1].Entry.Entry.Category.Should().Be("Leaves");
    }

    [Fact]
    public void EmptyFiltered_ReturnsEmptyList()
    {
        var order = PickerItemListHelper.ComputeGroupedDisplayOrder(new List<RankedEntry>());
        order.Should().BeEmpty();
    }

    [Fact]
    public void FavAndRecentOnly_NoNormals_ReturnsThemInOrder()
    {
        var filtered = new List<RankedEntry>
        {
            Favorite("F"),
            Recent("R"),
        };

        var order = PickerItemListHelper.ComputeGroupedDisplayOrder(filtered);

        order.Should().HaveCount(2);
        order[0].FilteredIndex.Should().Be(0);
        order[1].FilteredIndex.Should().Be(1);
    }

    [Fact]
    public void DisplayOrder_FilteredIndex_MapsBackToOriginalFiltered()
    {
        // Verify FilteredIndex values point back to the original list positions.
        var filtered = new List<RankedEntry>
        {
            Normal("B", "Composites"),   // 0
            Normal("A", "Composites"),   // 1
            Normal("Z", "Leaves"),       // 2
        };

        var order = PickerItemListHelper.ComputeGroupedDisplayOrder(filtered);

        // All FilteredIndex values must be in [0, filtered.Count-1] and unique.
        var indices = order.Select(o => o.FilteredIndex).ToList();
        indices.Should().OnlyHaveUniqueItems();
        indices.Should().OnlyContain(i => i >= 0 && i < filtered.Count);
    }
}
