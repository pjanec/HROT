using FluentAssertions;
using NodeEditor.Core.Interfaces;
using NodeEditor.UI.Picker;
using NodeEditor.UI.Picker.Layouts;
using Xunit;

namespace NodeEditor.UI.Tests.Picker;

/// <summary>
/// ⭐ CE-4098 (AQ108 D19) — the picker's <b>Recent</b> section: the items last picked, per kind, first, remembered in the user folder.
/// Red-proofs (each compiles): <c>RecentStore.Push</c> without <c>Save()</c> ⇒ the restart rail fails · <c>PickerState.RecordPick</c>
/// pushing to <c>ContextKey</c> instead of the entry's context ⇒ the kinds-do-not-mix rail fails · <c>RecentFilteredIndices</c> returning
/// <c>Filtered</c>'s order without the rank sort ⇒ the last-picked-first rail fails.
/// </summary>
public sealed class PickerRecentTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "recent-" + Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_dir, "recent-picks.json");

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private static PickerEntry E(string id, string? recentContext = null, string? category = null)
        => new(id, id, null, category, null, null, id, RecentContext: recentContext);

    private static PickerState StateWith(RecentStore recent, string contextKey, params PickerEntry[] entries)
    {
        var state = new PickerState { Recent = recent };
        state.Reset(contextKey, "", PickerSelectionMode.Single);
        state.AllEntries = entries;
        state.Refilter();
        return state;
    }

    // ── the store: persisted, per kind ───────────────────────────────────────

    [Fact]
    public void A_picked_item_is_first_after_a_restart_per_kind_and_kinds_do_not_mix()
    {
        var first = RecentStore.Load(FilePath);
        first.Push("scenario", "s-old");
        first.Push("scenario", "s-new");
        first.Push("recording", "r-1");

        // "restart": a brand-new store from the same file
        var second = RecentStore.Load(FilePath);

        second.GetRecent("scenario").Should().Equal("s-new", "s-old");
        second.GetRecent("recording").Should().Equal("r-1");
        second.IsRecent("recording", "s-new").Should().BeFalse("a scenario is not a recording");
        second.RankOf("scenario", "s-new").Should().Be(0);
    }

    [Fact]
    public void Move_to_front_and_the_cap_hold_through_a_restart()
    {
        var store = RecentStore.Load(FilePath);
        for (int i = 0; i < RecentStore.MaxItems + 4; i++) store.Push("k", $"item{i}");
        store.Push("k", "item10");   // re-pick an older one

        var reloaded = RecentStore.Load(FilePath);
        reloaded.GetRecent("k").Should().HaveCount(RecentStore.MaxItems);
        reloaded.GetRecent("k")[0].Should().Be("item10", "a re-pick moves to the front");
        reloaded.GetRecent("k").Count(x => x == "item10").Should().Be(1);
        reloaded.GetRecent("k").Should().NotContain("item0", "the cap dropped the oldest");
    }

    [Fact]
    public void A_missing_or_corrupt_file_gives_an_empty_store_and_never_throws()
    {
        RecentStore.Load(FilePath).GetRecent("k").Should().BeEmpty();

        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, "{ not json");
        var store = RecentStore.Load(FilePath);
        store.GetRecent("k").Should().BeEmpty();
        store.Push("k", "a");   // and it recovers by overwriting
        RecentStore.Load(FilePath).GetRecent("k").Should().Equal("a");
    }

    [Fact]
    public void An_in_memory_store_writes_nothing()
    {
        var store = new RecentStore();
        store.Push("k", "a");
        store.PersistPath.Should().BeNull();
        store.Save().Should().BeFalse();
        Directory.Exists(_dir).Should().BeFalse();
    }

    // ── the picker model: Recent first, resolved, per kind ───────────────────

    [Fact]
    public void Recent_entries_come_first_the_last_picked_first()
    {
        var recent = new RecentStore();
        recent.Push("ctx", "a");
        recent.Push("ctx", "c");   // c is the newest, then a
        var state = StateWith(recent, "ctx", E("a"), E("b"), E("c"), E("d"));

        var section = state.RecentFilteredIndices().Select(i => state.Filtered[i].Entry.Id).ToList();
        section.Should().Equal("c", "a");
        state.Filtered.Take(2).Select(r => r.Entry.Id).Should().Equal("c", "a");
        TreeLayout.RecentSectionRows(state).Should().Equal(state.RecentFilteredIndices(), "the Tree layout draws exactly this model");
    }

    [Fact]
    public void A_remembered_entry_that_no_longer_resolves_is_not_shown()
    {
        var recent = new RecentStore();
        recent.Push("ctx", "gone");
        recent.Push("ctx", "b");
        var state = StateWith(recent, "ctx", E("a"), E("b"));   // "gone" was deleted / left the inventory

        var section = state.RecentFilteredIndices().Select(i => state.Filtered[i].Entry.Id).ToList();
        section.Should().Equal("b");
        state.Filtered.Select(r => r.Entry.Id).Should().NotContain("gone");
    }

    [Fact]
    public void A_pick_pushes_to_the_entrys_kind_list_so_pickers_share_one_list_per_kind()
    {
        var recent = new RecentStore();
        // an "all kinds" picker whose entries name their own kind
        var state = StateWith(recent, "assets.open.All", E("s1", "scenario"), E("b1", "asset.Blueprint"), E("plain"));

        state.RecordPick(state.Filtered.Where(r => r.Entry.Id is "s1" or "plain"));

        recent.GetRecent("scenario").Should().Equal("s1");
        recent.GetRecent("asset.Blueprint").Should().BeEmpty("b1 was not picked");
        recent.GetRecent("assets.open.All").Should().Equal("plain");

        // a second picker (another context) over the same store sees the scenario as Recent
        var other = StateWith(recent, "scenario.open", E("s1", "scenario"), E("s2", "scenario"));
        other.RecentFilteredIndices().Select(i => other.Filtered[i].Entry.Id).Should().Equal("s1");
    }

    [Fact]
    public void The_registry_hands_its_store_to_the_window_so_a_host_can_persist_it()
    {
        var registry = new PickerRegistry();
        var persisted = RecentStore.Load(FilePath);
        persisted.Push("ctx", "b");
        registry.Recent = persisted;

        registry.OpenPicker(new PickerRequest
        {
            ContextKey = "ctx",
            Title = "t",
            ItemsProvider = () => new[] { E("a"), E("b") },
        }, _ => { });

        registry.Window.State.RecentFilteredIndices().Select(i => registry.Window.State.Filtered[i].Entry.Id).Should().Equal("b");
        registry.Window.State.RecordPick(registry.Window.State.Filtered.Where(r => r.Entry.Id == "a"));
        RecentStore.Load(FilePath).GetRecent("ctx").Should().Equal("a", "b");
    }
}
