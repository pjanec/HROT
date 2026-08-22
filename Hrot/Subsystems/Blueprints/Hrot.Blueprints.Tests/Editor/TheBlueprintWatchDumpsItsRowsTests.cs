using System;
using Fdp.Diagnostics.Contracts.Panels;
using Hrot.Blueprints.Editor.Debug;
using Hrot.Editor.AiShared.Variables;
using Xunit;

namespace Hrot.Blueprints.Tests.Editor;

/// <summary>
/// ⭐⭐⭐ <b><c>U-obs-1</c> / <c>U-obs-2</c> — the Blueprints host's <see cref="WatchPanelWindow"/>
/// converted to the <see cref="PanelSnapshot"/> contract, mirroring the pilot.</b>
/// 📄 <c>docs/DESIGN_UI_Observability_Snapshot.md</c> §Example · §Invariant.
///
/// <para>⭐⭐ <b>Why these rails run HEADLESS, with no ImGui context at all — and why that is the
/// point, not a shortcut.</b> ⛔ <c>WatchPanelWindow.DrawUI</c> publishes BEFORE its
/// <c>ImGui.GetCurrentContext()</c> guard (mirroring
/// <see cref="Hrot.Blueprints.Editor.EntityBlueprints.EntityBlueprintsPanel.DrawUI"/>), so a headless
/// run still observes the panel — these rails prove it by never opening a frame.</para>
///
/// <para>⚠ <b>ONE class</b>: <c>PanelSnapshot</c> is process-global static state and xunit
/// parallelises across CLASSES. Every case opens by resetting it.</para>
/// </summary>
[Collection(PanelSnapshotTestCollection.Name)]
public sealed class TheBlueprintWatchDumpsItsRowsTests : IDisposable
{
    public TheBlueprintWatchDumpsItsRowsTests()
    {
        PanelSnapshot.Clear();
        PanelSnapshot.CaptureEnabled = false;
    }

    public void Dispose()
    {
        PanelSnapshot.Clear();
        PanelSnapshot.CaptureEnabled = false;
    }

    // ── U1b, on the PRODUCTION object ──────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐⭐ <b>The panel is instrumented the moment it is CONSTRUCTED — before it has ever drawn.</b>
    /// ⛔ This is the rail that would go red if <c>DeclareInstrumented</c> drifted into the draw: a
    /// window nobody opened would then look exactly like a panel nobody converted, and the reader
    /// could not tell <i>"showed nothing"</i> from <i>"not instrumented"</i>.
    /// 📌 Asserted on the CONSTRUCTED object, not on the source — <c>R-67</c>.
    /// </summary>
    [Fact]
    public void ThePanelIsInstrumented_BeforeItHasEverDrawn()
    {
        Assert.DoesNotContain(WatchPanelWindow.PanelId, PanelSnapshot.RegisteredPanels);   // ⛔ anti-vacuity

        var session = new MockDebugSession();
        _ = new WatchPanelWindow(session);

        Assert.Contains(WatchPanelWindow.PanelId, PanelSnapshot.RegisteredPanels);
        Assert.DoesNotContain(WatchPanelWindow.PanelId, PanelSnapshot.CapturedPanels);
        Assert.Null(PanelSnapshot.TryGet(WatchPanelWindow.PanelId));
    }

    // ── U1c — the dump carries what the designer sees ──────────────────────────────────────────

    /// <summary>
    /// ⭐⭐⭐ <b>Draw a frame, read the snapshot, assert the KIND and that a model landed.</b>
    /// ⭐ <c>PanelKind</c> is <see cref="PanelIds.Watch"/> — the SAME kind the AiShared watch windows
    /// use, deliberately: they are the same logical panel in two hosts, and that shared kind is what a
    /// cross-host conformance diff groups by. ⛔ <c>PanelId</c> stays this window's own address.
    /// </summary>
    [Fact]
    public void AfterAFrame_TheDumpCarriesTheRowsThroughTheSharedWrapper()
    {
        PanelSnapshot.CaptureEnabled = true;
        var session = new MockDebugSession();
        session.AddWatchWithValue<int>("Health", 4242, tick: 3);
        var window = new WatchPanelWindow(session);

        window.DrawUI();                                    // ⭐ no ImGui context — headless on purpose

        var vm = PanelSnapshot.TryGet(WatchPanelWindow.PanelId);
        Assert.NotNull(vm);
        Assert.Equal(WatchPanelWindow.PanelId, vm!.PanelId);
        Assert.Equal(PanelIds.Watch,            vm.PanelKind);

        var dump = PanelSnapshot.DumpAll()[WatchPanelWindow.PanelId]!;
        Assert.Equal(WatchPanelWindow.PanelId, dump["panelId"]!.GetValue<string>());
        Assert.Equal(PanelIds.Watch,            dump["panelKind"]!.GetValue<string>());

        var rows = dump["rows"]!.AsArray();
        Assert.Single(rows);
        Assert.Equal("Health", rows[0]!["name"]!.GetValue<string>());
    }

    // ── The flag gates the DUMP, not the BUILD ─────────────────────────────────────────────────

    /// <summary>
    /// ⭐ Production default: capture OFF ⇒ nothing is published, ⛔ but the panel is still known to be
    /// instrumented, and the draw itself is unaffected by the flag.
    /// </summary>
    [Fact]
    public void WithCaptureOff_TheProductionPathPublishesNothing()
    {
        var session = new MockDebugSession();
        session.AddWatchWithValue<int>("Health", 4242, tick: 3);
        var window = new WatchPanelWindow(session);         // CaptureEnabled stays false

        window.DrawUI();

        Assert.Empty(PanelSnapshot.CapturedPanels);
        Assert.Contains(WatchPanelWindow.PanelId, PanelSnapshot.RegisteredPanels);
        Assert.Null(PanelSnapshot.TryGet(WatchPanelWindow.PanelId));
    }
}
