using System;
using System.Linq;
using Fdp.Toolkit.Orchestration;
using Hrot.Common.Infrastructure;
using Hrot.UI.Common.RunMode;
using NodeEditor.UI.Picker;
using Xunit;

namespace Hrot.Presentation.Tests;

/// <summary>
/// ⭐⭐ CE-3414 (AQ108 §0 D31; scripts/gen-live-session-storyboard.py panel ①) — the Start screen's Recent list, as DATA: one row per recent scenario that is still in the inventory, in recent order; each with
/// <i>● Live</i> · <i>Edit</i> (author seat only) · <i>AAR</i> (the latest run); every button is a verb of the owner's ONE guarded door; a steady frame builds nothing (R-2084).
/// 🔴 Mutations: the Edit button ignoring the seat ⇒ the player-seat row reads enabled; the AAR button naming the OLDEST run ⇒ the id assert; the cache keyed on nothing ⇒ the new-run assert; building rows every call ⇒ the 0 B assert.
/// </summary>
public sealed class StartRecentRowsTests
{
    private const string Ctx = "recent.Scenario";
    private static string Key(string name) => "key:" + name;

    private static readonly Guid OldRun = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid NewRun = Guid.Parse("00000000-0000-0000-0000-00000000000b");

    private sealed class Fixture
    {
        public readonly RunChoiceRig.Rig Rig = RunChoiceRig.With(ClusterState.Idle, withDoor: true, inventoryScenario: null);
        public readonly RecentStore Store = new();
        public Seat Seat = Seat.Author;
        public DateTime Now = new(2026, 11, 8, 12, 0, 0, DateTimeKind.Utc);
        public readonly StartRecentRows Rows;
        public Fixture() { Rows = new StartRecentRows(Rig.Choice, Store, Ctx, Key, () => Seat, () => Now); }

        /// <summary>The master's inventory arrives (a NEW set of arrays, as every real inventory is).</summary>
        public void Inventory(string[] scenarios, ExerciseInventoryItem[]? local = null, ExerciseInventoryItem[]? archived = null)
        {
            Rig.Bus.PublishManaged(new AssetInventoryUpdateEvent { LocalScenarios = scenarios, LocalExercises = local, ArchivedExercises = archived });
            Rig.Bus.SwapBuffers(); Rig.View.Update(); Rig.F.Update(); Rig.Bus.SwapBuffers();
        }
        public ExerciseInventoryItem Run(Guid id, string scenario, int minutesAgo)
            => new((Fdp.Core.ExerciseId)id, Now.AddMinutes(-minutesAgo), TimeSpan.Zero, scenario);
    }

    [Fact]
    public void TheRows_FollowTheRecentList_AndShowOnlyWhatTheInventoryStillHas()
    {
        var f = new Fixture();
        f.Inventory(new[] { "alpha", "bravo", "charlie" });
        f.Store.Push(Ctx, Key("alpha")); f.Store.Push(Ctx, Key("gone")); f.Store.Push(Ctx, Key("charlie"));   // most recent first: charlie, gone, alpha
        var rows = f.Rows.Rows();
        Assert.Equal(new[] { "charlie", "alpha" }, rows.Select(r => r.Name));   // "gone" is not in the inventory: not shown
    }

    [Theory]
    [InlineData(Seat.Author, true, true, null, true, null)]
    [InlineData(Seat.Player, true, false, StartRecentRows.NeedsAuthorSeat, true, null)]
    [InlineData(Seat.Author, false, true, null, false, StartRecentRows.NeverRun)]
    [InlineData(Seat.Player, false, false, StartRecentRows.NeedsAuthorSeat, false, StartRecentRows.NeverRun)]
    public void EachButtonState_FollowsTheSeat_AndWhetherTheScenarioEverRan(Seat seat, bool ran, bool editEnabled, string? editWhy, bool aarEnabled, string? aarWhy)
    {
        var f = new Fixture { Seat = seat };
        f.Inventory(new[] { "alpha" }, ran ? new[] { f.Run(OldRun, "alpha", 10) } : null);
        f.Store.Push(Ctx, Key("alpha"));
        var row = Assert.Single(f.Rows.Rows());
        Assert.True(row.Live.Enabled);                                  // Live is the briefing: always offered
        Assert.Equal((editEnabled, editWhy), (row.Edit.Enabled, row.Edit.Why));
        Assert.Equal((aarEnabled, aarWhy), (row.Aar.Enabled, row.Aar.Why));
        Assert.Equal(ran ? "Live · 10 min ago" : StartRecentRows.NeverRun, row.Sub);
    }

    [Fact]
    public void TheButtons_CallTheOwnersDoor_WithTheRightNameOrId_AndTheAarIsTheLatestRun()
    {
        var f = new Fixture();
        f.Inventory(new[] { "alpha" }, new[] { f.Run(OldRun, "alpha", 300) }, new[] { f.Run(NewRun, "alpha", 20) });   // the newer run is in the ARCHIVED list
        f.Store.Push(Ctx, Key("alpha"));
        var row = f.Rows.Rows()[0];
        Assert.Equal("Live · 20 min ago", row.Sub);
        var calls = ((RunChoiceRig.FakeDoorControl)f.Rig.C).Calls;
        row.Live.Invoke(); Assert.Contains("LoadForLive:alpha", calls);
        row.Edit.Invoke(); Assert.Contains("OpenForEdit:alpha", calls);
        row.Aar.Invoke(); Assert.Contains($"OpenRecording:{NewRun}", calls);
    }

    [Fact]
    public void TheRows_AreRebuilt_WhenAnInputChanges_AndSteadyFramesAllocateNothing()
    {
        var f = new Fixture();
        f.Inventory(new[] { "alpha" });
        f.Store.Push(Ctx, Key("alpha"));
        var first = f.Rows.Rows();
        Assert.Same(first, f.Rows.Rows());                                          // nothing moved: the cached list
        Assert.Equal(StartRecentRows.NeverRun, first[0].Sub);

        f.Inventory(new[] { "alpha" }, new[] { f.Run(NewRun, "alpha", 1) });         // a new inventory (a run was recorded)
        Assert.Equal("Live · 1 min ago", f.Rows.Rows()[0].Sub);
        f.Seat = Seat.Player;
        Assert.False(f.Rows.Rows()[0].Edit.Enabled);
        f.Store.Push(Ctx, Key("alpha"));                                              // the list's contents moved (same length)
        f.Now = f.Now.AddMinutes(5);
        Assert.Equal("Live · 6 min ago", f.Rows.Rows()[0].Sub);                      // the minute moved

        for (int i = 0; i < 50; i++) f.Rows.Rows();                                   // warm
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) f.Rows.Rows();
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);            // ⭐ R-2084
    }

    /// <summary>⭐ Storyboard ⑧ — the row whose run ENDED last (within the just-run window) is highlighted and carries the hint; an older run is not. 🔴 Red-proof: the window ignored ⇒ the old row highlights ⇒ red.</summary>
    [Fact]
    public void TheJustRunRow_IsTheOneWhoseRunEndedLast_AndOnlyWithinTheWindow()
    {
        var f = new Fixture();
        f.Inventory(new[] { "alpha", "bravo" }, new[] { f.Run(OldRun, "alpha", 300), f.Run(NewRun, "bravo", 5) });
        f.Store.Push(Ctx, Key("alpha")); f.Store.Push(Ctx, Key("bravo"));
        var rows = f.Rows.Rows();
        Assert.Equal(("bravo", true), (rows[0].Name, rows[0].JustRun));
        Assert.Equal("bravo" + StartRecentRows.JustRunHint, rows[0].Hint);
        Assert.False(rows[1].JustRun); Assert.Null(rows[1].Hint);
        f.Now = f.Now.AddMinutes(30);                                                    // 35 min after its start: no longer "just"
        Assert.All(f.Rows.Rows(), r => Assert.False(r.JustRun));
    }
}
