using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Fdp.Toolkit.Orchestration;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Browser;
using Hrot.Editor.AiShared.Scenarios;
using NodeEditor.UI.Picker;
using Xunit;

namespace Hrot.Editor.Tests.Browser;

/// <summary>
/// ⭐⭐ <c>CE-4098</c> (AQ108 D16 / D19) — the RECORDING picker behind <c>File › Open Recording</c>: it lists the exercises the master recorded, newest
/// first, shows the user's Recent recordings first, and opens the pick through the host's ONE opener
/// (<c>IScenarioSession.OpenRecording</c> → <c>ReplaySessionIntents.EnterReplay</c>; here a fake opener stands for it). A recording is remembered by
/// its EXERCISE ID in the <c>recent.Recording</c> list — never a path — so the content-folder move (CE-4089) cannot stale it.
/// </summary>
public sealed class RecordingPickerLauncherTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "rec-picker-" + Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_dir, "recent-picks.json");

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private static readonly Guid Old = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid Mid = Guid.Parse("00000000-0000-0000-0000-00000000000b");
    private static readonly Guid New = Guid.Parse("00000000-0000-0000-0000-00000000000c");

    private readonly List<Guid> _opened = new();

    private static ExerciseInventoryItem Ex(Guid id, int day, string? scenario = "uc-urban-battle")
        => new(id, new DateTime(2026, 10, day, 9, 0, 0, DateTimeKind.Utc), TimeSpan.FromMinutes(5), scenario);

    private RecordingPickerLauncher Make(PickerRegistry registry, params ExerciseInventoryItem[] inventory)
        => new(registry.OpenPicker, () => inventory, id => _opened.Add(id));

    [Fact]
    public void Entries_ListEveryRecordingNewestFirst_ByExerciseId_InTheRecordingKindsList()
    {
        var launcher = Make(new PickerRegistry(), Ex(Old, 1), Ex(New, 3), Ex(Mid, 2));

        var entries = launcher.BuildEntries();

        Assert.Equal(new[] { New, Mid, Old }.Select(AssetRecents.RecordingKey), entries.Select(e => e.Id));
        Assert.All(entries, e =>
        {
            Assert.Equal(AssetRecents.ContextFor(AssetKind.Recording), e.RecentContext);
            Assert.DoesNotContain(Path.DirectorySeparatorChar, e.Id);   // an id, never a path
            Assert.Contains("uc-urban-battle", e.Name);
        });
    }

    /// <summary>
    /// ⭐ CE-4098 success 4 — a pick goes through the ONE opener, once, with the picked exercise id (the real one is
    /// <c>EditorScenarioSession.OpenRecording</c>, which publishes <c>ReplaySessionIntents.EnterReplay</c>); a cancel opens nothing.
    /// 🔴 Red-proof: call the opener with <c>Guid.Empty</c> instead of <c>picked.ExerciseId</c> ⇒ the id assertion fails.
    /// </summary>
    [Fact]
    public void APick_OpensThatExercise_ThroughTheOneOpener_Once_AndACancelOpensNothing()
    {
        var registry = new PickerRegistry { Recent = new RecentStore() };
        var launcher = Make(registry, Ex(Old, 1), Ex(New, 3));

        launcher.Open();
        Assert.True(registry.ConfirmEntry(AssetRecents.RecordingKey(Old)));
        Assert.Equal(new[] { Old }, _opened);

        _opened.Clear();
        launcher.Open();
        Assert.False(registry.ConfirmEntry("not-listed"));
        Assert.Empty(_opened);
    }

    /// <summary>
    /// ⭐ CE-4098 success 1+2 — the recording picker draws the Recent recordings first (last picked first), a pick pushes to the recording list, and
    /// after a "restart" (fresh store from the file, fresh registry, fresh launcher) the picked one is first. Kinds do not mix: a scenario pick
    /// is not a recent recording. Red-proof: give the entries no <c>RecentContext</c> ⇒ the pick lands in the picker's own context and the restart
    /// assertion (the section is empty under the recording list) fails.
    /// </summary>
    [Fact]
    public void ThePickedRecording_IsFirstInTheRecentSection_AfterARestart()
    {
        var inventory = new[] { Ex(Old, 1), Ex(Mid, 2), Ex(New, 3) };

        var first = new PickerRegistry { Recent = RecentStore.Load(FilePath) };
        first.Recent.Push(AssetRecents.ContextFor(AssetKind.Scenario), AssetRecents.RecordingKey(Mid));   // a different kind, same key: must not show
        var launcher = Make(first, inventory);
        launcher.Open();
        Assert.Empty(first.RecentSection());
        Assert.True(first.ConfirmEntry(AssetRecents.RecordingKey(Mid)));
        launcher.Open();
        Assert.True(first.ConfirmEntry(AssetRecents.RecordingKey(Old)));   // Old is now the newest pick, then Mid

        // "restart"
        var second = new PickerRegistry { Recent = RecentStore.Load(FilePath) };
        Make(second, inventory).Open();

        Assert.Equal(new[] { Old, Mid }.Select(AssetRecents.RecordingKey), second.RecentSection().Select(e => e.Id));
    }

    /// <summary>
    /// ⭐ CE-4098 success 3 — a remembered recording that the master no longer offers is not shown (no ghost row, no crash).
    /// 🔴 Red-proof: make <c>RecentFilteredIndices</c> add a row per remembered key ⇒ fails (see the NodeEditor.UI rail for the model).
    /// </summary>
    [Fact]
    public void ARecordingGoneFromTheInventory_IsNotShownAsRecent()
    {
        var store = RecentStore.Load(FilePath);
        store.Push(AssetRecents.ContextFor(AssetKind.Recording), AssetRecents.RecordingKey(Old));
        store.Push(AssetRecents.ContextFor(AssetKind.Recording), AssetRecents.RecordingKey(Mid));

        var registry = new PickerRegistry { Recent = RecentStore.Load(FilePath) };
        Make(registry, Ex(Mid, 2), Ex(New, 3)).Open();   // Old was archived away

        Assert.Equal(new[] { AssetRecents.RecordingKey(Mid) }, registry.RecentSection().Select(e => e.Id));
    }
}
