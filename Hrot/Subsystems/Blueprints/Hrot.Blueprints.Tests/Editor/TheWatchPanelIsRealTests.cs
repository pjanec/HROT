using Hrot.Blueprints.Core.Debug;
using Hrot.Blueprints.Editor;
using Hrot.Blueprints.Editor.Debug;
using Hrot.Editor.AiShared.Variables;
using StructEdit.Reflection;

namespace Hrot.Blueprints.Tests.Editor;

/// <summary>
/// ⭐⭐⭐ <b>Row 59b — the Watch panel becomes real.</b>
///
/// <para>📌 <b>Row 59b, verbatim:</b> <i>"make <c>HandlePinValueChanged</c> real · <b>EDITING through
/// the same dialog</b> · <b>show NOTHING before the run</b>"</i> · 📌 <b>ruling 11:</b> <i>"the runtime
/// value change is the same mechanism the Watch panel should provide — <b>SHARE it</b>."</i></para>
///
/// <para>🔴🔴 <b>Three defects, one mistake — a private copy of something shared:</b>
/// <c>HandlePinValueChanged</c> was <b>an empty body</b>; the value column rendered
/// <c>Convert.ToHexString(...)</c> — ⛔ <b><c>BP-01</c>'s original symptom, still live</b>; and
/// "nothing before the run" was spelled <c>"--"</c> rather than the shared <c>(pending)</c>.</para>
/// </summary>
public sealed class TheWatchPanelIsRealTests
{
    // ══ HandlePinValueChanged is real ════════════════════════════════════════

    /// <summary>
    /// 🔴 <b>RED before row 59b.</b> The handler body was <c>{ /* refresh row data */ }</c> — the event
    /// arrived and the panel did nothing, so a value that moved while frozen never appeared.
    /// ⭐ Asserted on the OBSERVABLE, not on "the handler was subscribed".
    /// </summary>
    [Fact]
    public void APinValueChange_RefreshesThePanel()
    {
        var session = new MockDebugSession();
        var window  = new WatchPanelWindow(session, Decode);
        window.OnActivated();

        Assert.Equal(0, window.ValueChangeCount);

        session.AddWatchWithValue<int>("Health", 5, tick: 1);
        session.RaisePinValueChanged();

        Assert.Equal(1, window.ValueChangeCount);
        Assert.Equal("5", ValueOf(window, "Health"));
    }

    /// <summary>⭐ And the panel stops listening when it goes away — no ghost refreshes.</summary>
    [Fact]
    public void AfterDeactivation_ChangesAreIgnored()
    {
        var session = new MockDebugSession();
        var window  = new WatchPanelWindow(session, Decode);
        window.OnActivated();
        session.RaisePinValueChanged();
        window.OnDeactivated();

        session.RaisePinValueChanged();

        Assert.Equal(1, window.ValueChangeCount);
    }

    // ══ BP-01 — NEVER raw hex ════════════════════════════════════════════════

    /// <summary>
    /// 🔴🔴 <b><c>BP-01</c>, closed.</b> 📌 The tracker's own words: <i>"Watch panel shows raw hex
    /// bytes; <c>MarshalFromBytes</c> is complete, tested, and used at 4 other sites in the same
    /// file."</i> ⭐ It still is — the panel simply never called it.
    /// </summary>
    [Fact]
    public void AWatchedValueIsDecoded_NotRenderedAsHex()
    {
        var session = new MockDebugSession();
        session.AddWatchWithValue<int>("Health", 4242, tick: 3);
        var window = new WatchPanelWindow(session, Decode);

        window.Refresh();

        var text = ValueOf(window, "Health");
        Assert.Equal("4242", text);
        // ⛔ The exact shape of the old bug: the hex of those bytes must not be what a designer sees.
        Assert.NotEqual(Convert.ToHexString(BitConverter.GetBytes(4242)), text);
    }

    /// <summary>
    /// ⭐⭐⭐ <b>And the PRODUCTION default decodes too.</b> ⚠ Found by my own revert probe: every test
    /// above injects its own decoder, so swapping the panel's default back to
    /// <c>Convert.ToHexString</c> left them ALL GREEN. ⛔ That is the vacuous-rail shape — a test that
    /// proves the formatter works and says nothing about what the panel actually uses.
    /// ⭐ This one passes NO decoder, so it asserts the constructor's own choice.
    /// </summary>
    [Fact]
    public void ThePanelsDefaultDecoder_IsTheRealOne_NotHex()
    {
        var session = new MockDebugSession();
        session.AddWatchWithValue<int>("Health", 4242, tick: 3);
        var window = new WatchPanelWindow(session);          // ⭐ no decoder injected

        window.Refresh();

        // ⭐ Ask the PANEL what it would render — ⛔ not a formatter the test built itself.
        var text = window.CellText(RowOf(window, "Health"));

        Assert.Equal("4242", text);
        Assert.NotEqual(Convert.ToHexString(BitConverter.GetBytes(4242)), text);
    }

    /// <summary>
    /// ⭐⭐ <b>"Show NOTHING before the run" is the SHARED state</b>, not a second vocabulary. ⛔ The
    /// panel used to print <c>"--"</c> for the same thing the Details table calls <c>(pending)</c>.
    /// </summary>
    [Fact]
    public void BeforeTheRun_AWatchReadsPending()
    {
        var session = new MockDebugSession();
        session.AddWatchWithValue<int>("Health", null, tick: 0);
        var window = new WatchPanelWindow(session, Decode);

        window.Refresh();

        Assert.Equal(VariableValueFormatter.PendingFirstWrite, ValueOf(window, "Health"));
    }

    // ══ ruling 11 — SHARE the mechanism ══════════════════════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>One table implementation, not a fourth.</b> The panel's hand-rolled <c>BeginTable</c>
    /// is gone; it renders through Track C's control — which is what makes the two panels agree by
    /// construction rather than by two people writing the same rules twice.
    /// </summary>
    [Fact]
    public void ThePanelRendersThroughTheSharedControl()
    {
        var window = new WatchPanelWindow(new MockDebugSession(), Decode);

        Assert.IsType<VariableTableControl>(window.Table);
        Assert.Equal("Hrot.Editor.AiShared", typeof(VariableTableControl).Assembly.GetName().Name);
    }

    /// <summary>
    /// ⭐⭐ <b>EDITING through the SAME dialog</b> *(row 59b)*. ⛔ A Watch-local editor is precisely what
    /// ruling 11 forbids — the binder here is item 2's, unchanged.
    /// </summary>
    [Fact]
    public void TheRowGesturesGoThroughTheSharedDialog()
    {
        var session = new MockDebugSession();
        session.AddWatchWithValue<int>("Health", 1, tick: 1);
        var window = new WatchPanelWindow(session, Decode);
        window.Refresh();

        var binder = new VariableEditGestureBinder(
            new VariableEditLauncher(new ComponentEditServiceBuilder().Build()),
            entryResolver: _ => null,                       // ⛔ fail closed; the gesture still routes
            runState:      () => VariableRunState.Running);
        window.BindEditGestures(binder);

        window.Table.RaiseEditValueRequested(window.LastView.AllRows[0]);

        Assert.Equal(VariableEditAction.EditValue, binder.LastAction);
    }

    /// <summary>
    /// ⭐ <b>The Watch hides Type</b> — 📌 §1b's stated difference from the Details table, and the whole
    /// of that difference. ⛔ Not a different table; a different column set on one table.
    /// </summary>
    [Fact]
    public void TheWatchHidesTheTypeColumn()
        => Assert.False(new WatchPanelWindow(new MockDebugSession(), Decode).LastView.Columns.ShowType);

    /// <summary>
    /// ⚠ <b>Rows tick at DIFFERENT rates</b> — 📌 <i>"in Watch, rows tick at different rates"</i>, so
    /// the tick is per ROW. ⛔ A panel-wide tick would be a single clock over independent observations.
    /// </summary>
    [Fact]
    public void EachRowCarriesItsOwnTick()
    {
        var session = new MockDebugSession();
        session.AddWatchWithValue<int>("A", 1, tick: 10);
        session.AddWatchWithValue<int>("B", 2, tick: 77);
        var window = new WatchPanelWindow(session, Decode);

        window.Refresh();

        Assert.Equal(10u, RowOf(window, "A").AssetTick!());
        Assert.Equal(77u, RowOf(window, "B").AssetTick!());
    }

    /// <summary>
    /// ⚠ <b>The bytes are COPIED, not aliased.</b> The watch reuses one 64-byte buffer, so a row
    /// holding the span would show whatever it contains at DRAW time rather than at OBSERVE time —
    /// ⛔ a race that reads as flicker and is very hard to see.
    /// </summary>
    [Fact]
    public void ARowsBytesAreCopiedFromTheWatchBuffer()
    {
        var session = new MockDebugSession();
        var watch   = session.AddWatchWithValue<int>("Health", 1, tick: 1);
        var row     = WatchRowBridge.ToRow(watch);

        watch.WriteValue(999, default, 2);

        // ⭐ The row still reports what it observed — it did not follow the reused buffer.
        Assert.Equal(1, BitConverter.ToInt32(row.ReadValue()));
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    private static object? Decode(byte[] bytes, Type type)
        => bytes.Length == 4 && type == typeof(int) ? BitConverter.ToInt32(bytes) : null;

    private static VariableRow RowOf(WatchPanelWindow w, string name)
        => w.LastView.AllRows.Single(r => r.ShortName == name);

    /// <summary>⭐ What the PANEL renders — ⛔ never a formatter the test built for itself.</summary>
    private static string ValueOf(WatchPanelWindow w, string name) => w.CellText(RowOf(w, name));

}
