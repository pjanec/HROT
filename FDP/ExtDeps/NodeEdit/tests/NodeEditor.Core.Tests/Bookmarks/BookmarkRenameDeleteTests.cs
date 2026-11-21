using System.Numerics;
using NodeEditor.Core.Bookmarks;
using NodeEditor.Primitives;
using Xunit;

namespace NodeEditor.Core.Tests.Bookmarks;

/// <summary>
/// BP-03 — bookmarks could be set (Ctrl+Shift+1..9) but never renamed or removed. The panel was a
/// read-only text list, so the only way to reclaim a slot was to overwrite it, and an unslotted
/// bookmark was permanent. <see cref="BookmarkStore.Remove"/> already existed;
/// <see cref="BookmarkStore.Rename"/> is added here because <see cref="Bookmark"/> is a record.
/// </summary>
public sealed class BookmarkRenameDeleteTests
{
    private static Bookmark Make(string id, string label, int slot = 0)
        => new(id, new GraphId(Guid.NewGuid()), label, Vector2.Zero, 1f, slot, DateTime.UnixEpoch);

    // ── Rename ───────────────────────────────────────────────────────────────

    [Fact]
    public void Rename_ChangesTheLabel_KeepingIdSlotAndViewport()
    {
        var store = new BookmarkStore();
        var b = new Bookmark("b1", new GraphId(Guid.NewGuid()), "Old",
            new Vector2(12f, 34f), 2.5f, 3, DateTime.UnixEpoch);
        store.SetSlot(3, b);

        Assert.True(store.Rename("b1", "New"));

        var after = store.GetSlot(3);
        Assert.NotNull(after);
        Assert.Equal("New", after!.Label);
        Assert.Equal("b1", after.BookmarkId);
        Assert.Equal(3, after.SlotNumber);
        Assert.Equal(new Vector2(12f, 34f), after.ViewportPan);
        Assert.Equal(2.5f, after.ViewportZoom);
    }

    [Fact]
    public void Rename_UnslottedBookmark_Works()
    {
        var store = new BookmarkStore();
        store.SetSlot(0, Make("b1", "Old"));

        Assert.True(store.Rename("b1", "New"));
        Assert.Equal("New", store.All.Single().Label);
    }

    /// <summary>
    /// A bookmark with no label is an unclickable blank row, so an empty rename is refused rather
    /// than accepted and rendered as a gap.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Rename_ToBlank_IsRefused(string blank)
    {
        var store = new BookmarkStore();
        store.SetSlot(1, Make("b1", "Original", 1));

        Assert.False(store.Rename("b1", blank));
        Assert.Equal("Original", store.GetSlot(1)!.Label);
    }

    [Fact]
    public void Rename_UnknownId_ReturnsFalse()
    {
        var store = new BookmarkStore();
        Assert.False(store.Rename("nope", "New"));
    }

    [Fact]
    public void Rename_DoesNotDisturbOtherBookmarks()
    {
        var store = new BookmarkStore();
        store.SetSlot(1, Make("b1", "One", 1));
        store.SetSlot(2, Make("b2", "Two", 2));

        store.Rename("b1", "Renamed");

        Assert.Equal("Renamed", store.GetSlot(1)!.Label);
        Assert.Equal("Two",     store.GetSlot(2)!.Label);
    }

    // ── Delete ───────────────────────────────────────────────────────────────

    [Fact]
    public void Remove_FreesTheSlotForReuse()
    {
        var store = new BookmarkStore();
        store.SetSlot(4, Make("b1", "First", 4));

        Assert.True(store.Remove("b1"));
        Assert.Null(store.GetSlot(4));

        store.SetSlot(4, Make("b2", "Second", 4));
        Assert.Equal("Second", store.GetSlot(4)!.Label);
    }

    [Fact]
    public void Remove_UnslottedBookmark_Works()
    {
        var store = new BookmarkStore();
        store.SetSlot(0, Make("b1", "Loose"));

        Assert.True(store.Remove("b1"));
        Assert.Empty(store.All);
    }
}
