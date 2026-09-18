using System;
using System.Collections.Generic;
using Fdp.Core;
using Hrot.Editor.AiShared.Selection;
using Hrot.Editor.AiShared.Shell;
using Hrot.Editor.AiShared.Variables;
using Hrot.Presentation.Zones;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Shell;

/// <summary>
/// ⭐⭐⭐ <c>E3</c> + <c>U5</c> — the ZONES view: when it is offered, and what its actions do.
///
/// <para>⭐⭐ <b><c>U5</c> is the interesting half, and the answer is smaller than the design expected.</b>
/// It recorded the gap as <i>"the details shell has no 'map background selected' CONTEXT"</i> and
/// predicted <i>"a small addition to an existing interface, not new infrastructure"</i>. 📐 Measured: it
/// needed ONE enum member. <c>EditorSelectionStore.NotifySurfaceFocused</c> already latches which surface
/// holds focus, <c>DetailsContext.Focus</c> already carries it, and <c>DetailsViewPredicates.FocusIs</c>
/// already reads it — the map simply becomes another contributing surface.</para>
///
/// <para>⭐ Contexts come from the PRODUCTION builder, not hand-set properties — a predicate railed
/// against a context nobody builds is a predicate about a shape that does not occur.</para>
///
/// 📄 docs/DESIGN_Terrain_Zones_And_Assets.md §9.5, §9.3, §9.6, §9.7 ③b, §3-U5.
/// </summary>
public sealed class TheZonesViewIsOfferedOnTheMapBackgroundTests
{
    private sealed class NoEntities : IEntitySelectionSource
    {
        IReadOnlyList<Entity> IEntitySelectionSource.Selected() => Array.Empty<Entity>();
    }

    /// <summary>A context whose focused surface is whatever a case names, built the production way.</summary>
    private static DetailsContext ContextFocusedOn(SelectionOrigin origin)
    {
        var store = new EditorSelectionStore();
        store.NotifySurfaceFocused(origin);
        return DetailsContextBuilder.Build(
            store, "Scenario", VariableRunState.Planning, new NoEntities());
    }

    // ══ U5 — the predicate ════════════════════════════════════════════════════

    /// <summary>⭐⭐⭐ The whole of <c>U5</c>: the map background is a context the shell can route on.</summary>
    [Fact]
    public void U5_WhenTheMapBackgroundHoldsFocus_TheZonesViewApplies()
        => Assert.True(ZonesDetailsViewDescriptor.Applies(
               ContextFocusedOn(SelectionOrigin.MapBackground)));

    /// <summary>
    /// ⛔ And it does NOT apply on the other surfaces. ⚠ This is the half that matters: a zones list
    /// claiming the panel while a designer works in the graph canvas or the variable outline would be
    /// the routing collapse <c>Q32</c> ruling 2 exists to prevent.
    /// </summary>
    [Theory]
    [InlineData(SelectionOrigin.GraphCanvas)]
    [InlineData(SelectionOrigin.VariableOutline)]
    [InlineData(SelectionOrigin.Unknown)]
    public void U5_OnAnyOtherSurface_TheZonesViewDoesNotApply(SelectionOrigin origin)
        => Assert.False(ZonesDetailsViewDescriptor.Applies(ContextFocusedOn(origin)));

    // ══ E3 — the actions ══════════════════════════════════════════════════════

    private sealed class RecordingRenderer : IZoneRowRenderer
    {
        public readonly List<ZoneStatusRow> Rows = new();
        public bool  HeaderAnyStale;
        public int   HeaderCalls;
        public Action? LoadAllStale;
        public readonly List<Action> RowLoads = new();

        public void Header(string idScope, IReadOnlyList<ZoneStatusRow> rows, bool anyStale, Action loadAllStale)
        {
            HeaderCalls++;
            HeaderAnyStale = anyStale;
            LoadAllStale   = loadAllStale;
        }

        public void Row(string idScope, ZoneStatusRow row, Action load)
        {
            Rows.Add(row);
            RowLoads.Add(load);
        }
    }

    private static ZoneStatusRow Row(string id, ZoneLoadState state)
        => new ZoneStatusRow(id, new Entity(1, 1), state);

    /// <summary>⭐ One row per zone, and the header is told whether anything is actionable.</summary>
    [Fact]
    public void E3_ItDrawsOneRowPerZone_AndTellsTheHeaderWhetherAnythingIsStale()
    {
        var rows = new List<ZoneStatusRow>
        {
            Row("11", ZoneLoadState.Loaded),
            Row("22", ZoneLoadState.Stale),
        };
        var renderer = new RecordingRenderer();
        var view = new ZonesDetailsView(() => rows, _ => { }, renderer);

        view.Draw(ContextFocusedOn(SelectionOrigin.MapBackground), "scope");

        Assert.Equal(2, renderer.Rows.Count);
        Assert.Equal(1, renderer.HeaderCalls);
        Assert.True(renderer.HeaderAnyStale);
    }

    /// <summary>⭐ Nothing stale ⇒ the header says so, which is what disables "Load all stale".</summary>
    [Fact]
    public void E3_WhenEverythingIsLoaded_TheHeaderReportsNothingStale()
    {
        var renderer = new RecordingRenderer();
        var view = new ZonesDetailsView(
            () => new List<ZoneStatusRow> { Row("11", ZoneLoadState.Loaded) }, _ => { }, renderer);

        view.Draw(ContextFocusedOn(SelectionOrigin.MapBackground), "scope");

        Assert.False(renderer.HeaderAnyStale);
    }

    /// <summary>⭐ A per-row Load issues the cluster op for THAT zone and no other.</summary>
    [Fact]
    public void E3_APerRowLoad_IssuesTheOpForThatZoneOnly()
    {
        var asked = new List<string>();
        var renderer = new RecordingRenderer();
        var view = new ZonesDetailsView(
            () => new List<ZoneStatusRow> { Row("11", ZoneLoadState.Loaded), Row("22", ZoneLoadState.Stale) },
            asked.Add, renderer);

        view.Draw(ContextFocusedOn(SelectionOrigin.MapBackground), "scope");
        renderer.RowLoads[1]();

        Assert.Equal(new[] { "22" }, asked);
    }

    /// <summary>
    /// ⭐⭐⭐ <b>THE RAIL THAT MATTERS for E3.</b> "Load all stale" issues <b>ONE OP PER ZONE</b> (§9.3),
    /// not one batch op — and it skips the zones that are already resident for their current shape.
    ///
    /// <para>⛔ One-op-per-zone is not an implementation detail: independent failure, independent
    /// progress and a natural retry granularity all follow from it, and a multi-zone payload was
    /// rejected for exactly that reason.</para>
    /// </summary>
    [Fact]
    public void E3_LoadAllStale_IssuesOneOpPerStaleZone_AndSkipsTheLoadedOnes()
    {
        var asked = new List<string>();
        var renderer = new RecordingRenderer();
        var view = new ZonesDetailsView(
            () => new List<ZoneStatusRow>
            {
                Row("11", ZoneLoadState.Loaded),      // ⛔ skipped — resident for its current shape
                Row("22", ZoneLoadState.Stale),
                Row("33", ZoneLoadState.NotLoaded),
                Row("44", ZoneLoadState.Failed),      // ⭐ a failure is retryable, so it is included
                Row("55", ZoneLoadState.Loading),     // ⛔ skipped — a round is already in flight
            },
            asked.Add, renderer);

        view.Draw(ContextFocusedOn(SelectionOrigin.MapBackground), "scope");
        renderer.LoadAllStale!();

        Assert.Equal(new[] { "22", "33", "44" }, asked);
    }

    // ══ E3 — progress, and NOT from HasInFlightTransaction ════════════════════

    /// <summary>
    /// ⛔⛔ Design §9.4: <c>ClusterMaster.HasInFlightTransaction</c> answers <c>false</c> while rounds are
    /// genuinely pending, because <c>_activeTransaction</c> is cleared in the same method that sets it.
    /// ⇒ progress is tracked by the REQUESTER. ⭐ This rails that the tracker survives what a spinner
    /// needs: a request appears, and only its own completion clears it.
    /// </summary>
    [Fact]
    public void E3_TheProgressTracker_HoldsAZoneUntilItsOwnRequestCompletes()
    {
        var tracker = new ZoneLoadProgressTracker();
        var first  = Guid.NewGuid();
        var second = Guid.NewGuid();

        tracker.Requested(first,  "11");
        tracker.Requested(second, "22");
        Assert.Contains("11", tracker.InFlight);
        Assert.Contains("22", tracker.InFlight);

        // ⭐ Completing ONE round must not clear the other — two zones, two independent rounds (§9.3).
        tracker.Completed(first);
        Assert.DoesNotContain("11", tracker.InFlight);
        Assert.Contains("22", tracker.InFlight);

        // ⚠ An unknown request id is ignored rather than clearing something at random.
        tracker.Completed(Guid.NewGuid());
        Assert.Contains("22", tracker.InFlight);

        tracker.Reset();
        Assert.Empty(tracker.InFlight);
    }
}
