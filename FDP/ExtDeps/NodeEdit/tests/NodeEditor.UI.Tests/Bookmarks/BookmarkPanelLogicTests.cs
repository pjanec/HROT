using System.Numerics;
using NodeEditor.Core.Bookmarks;
using NodeEditor.Primitives;
using NodeEditor.UI.Bookmarks;
using Xunit;

namespace NodeEditor.UI.Tests.Bookmarks;

/// <summary>
/// BP-03 — the non-ImGui half of <see cref="BookmarksPanel"/>: how rows are ordered and labelled.
/// Split out of the panel precisely so these rules are testable without an ImGui context.
/// </summary>
public sealed class BookmarkPanelLogicTests
{
    private static Bookmark Make(string id, string label, int slot = 0)
        => new(id, new GraphId(Guid.NewGuid()), label, Vector2.Zero, 1f, slot, DateTime.UnixEpoch);

    /// <summary>
    /// Slot-bound bookmarks come first in slot order; unslotted ones follow, ordered by label so
    /// the list is stable rather than dictionary-ordered.
    /// </summary>
    [Fact]
    public void DisplayOrder_PutsSlottedFirst_ThenUnslottedByLabel()
    {
        var items = new[]
        {
            Make("u2", "zebra"),
            Make("s3", "third",  3),
            Make("u1", "apple"),
            Make("s1", "first",  1),
        };

        var ordered = BookmarkPanelLogic.InDisplayOrder(items).Select(b => b.BookmarkId).ToList();

        Assert.Equal(new[] { "s1", "s3", "u1", "u2" }, ordered);
    }

    [Fact]
    public void DisplayOrder_IsCaseInsensitiveForUnslotted()
    {
        var items = new[] { Make("b", "Beta"), Make("a", "alpha") };

        Assert.Equal(new[] { "a", "b" },
            BookmarkPanelLogic.InDisplayOrder(items).Select(x => x.BookmarkId));
    }

    [Fact]
    public void SlotLabel_ShowsTheNumber_OrBlankBrackets()
    {
        Assert.Equal("[7]",  BookmarkPanelLogic.SlotLabel(Make("b", "x", 7)));
        Assert.Equal("[  ]", BookmarkPanelLogic.SlotLabel(Make("b", "x")));
    }
}
