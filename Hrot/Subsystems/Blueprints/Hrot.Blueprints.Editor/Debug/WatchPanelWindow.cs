using ImGuiNET;
using Hrot.Blueprints.Core.Debug;
using Hrot.Editor.AiShared.Variables;

namespace Hrot.Blueprints.Editor.Debug;

/// <summary>
/// ⭐⭐⭐ <b>Row 59b — the Watch panel becomes real.</b>
///
/// <para>📌 <b>Row 59b, verbatim:</b> <i>"make <c>HandlePinValueChanged</c> real · <b>EDITING through
/// the same dialog</b> · <b>show NOTHING before the run</b>"</i> · 📌 <b>ruling 11:</b> <i>"the runtime
/// value change is the same mechanism the Watch panel should provide — <b>SHARE it</b>."</i></para>
///
/// <para>🔴🔴 <b>Three things were wrong, and all three were the same mistake — a private copy of
/// something shared:</b>
/// <list type="number">
///   <item><c>HandlePinValueChanged</c> was <b>an empty body with a comment</b>
///   (<c>/* refresh row data */</c>) — the event arrived and nothing happened.</item>
///   <item>the value column rendered <c>Convert.ToHexString(...)</c> — ⛔ <b><c>BP-01</c>'s original
///   symptom, still live</b>, while <c>MarshalFromBytes</c> sat complete and tested in the same
///   assembly.</item>
///   <item>"nothing before the run" was spelled <c>"--"</c> — a second vocabulary for the state the
///   shared formatter already calls <c>(pending)</c>.</item>
/// </list></para>
///
/// <para>⭐ <b>All three collapse into one change:</b> render through the shared
/// <see cref="VariableTableControl"/> over <see cref="WatchRowBridge"/> rows. ⛔ The hand-rolled
/// <c>BeginTable</c> is gone — it was the fourth variable table in the editor.</para>
///
/// <para>⚠ <b>Ruling 12's immediacy gate is NOT this item's.</b> <i>"Visible in BOTH panels within one
/// frame while frozen"</i> runs through the RUNNING write, which is row <c>59c</c>. ⭐ What this item
/// makes true is that both panels now read the same rows through the same formatter, so when 59c's
/// write lands there is nothing further to share.</para>
/// </summary>
public sealed class WatchPanelWindow : BlueprintEditorWindowBase,
                                       Hrot.Editor.AiShared.Variables.IVariableTableHost
{
    private readonly IBlueprintDebugSession _session;
    private readonly VariableTableControl   _table;
    private readonly VariableTableModel     _model;
    private readonly FixedVariableRowSource _empty = new(Array.Empty<VariableRow>());

    public override string Title => "Watches";

    // Captured on each DrawUI call -- readable by tests without an ImGui context.
    public IReadOnlyList<Watch>? LastRenderedWatches { get; private set; }

    /// <summary>
    /// ⭐⭐ How many times the session has told us a watched value moved. 🔴 <c>HandlePinValueChanged</c>
    /// used to be an empty body; this is the observable that makes it real, and a rail asserts it
    /// advances rather than asserting "the handler was subscribed".
    /// </summary>
    public int ValueChangeCount { get; private set; }

    /// <summary>
    /// ⭐ The last pin the session reported changing. ⛔ Kept so a refresh can be targeted later; today
    /// the panel re-reads every row, which is correct and cheap for a pinned list.
    /// </summary>
    public PinValueChanged? LastValueChange { get; private set; }

    /// <summary>⭐ The rows as the panel would draw them — the headless view of what a designer sees.</summary>
    public VariableTableView LastView { get; private set; }

    public WatchPanelWindow(IBlueprintDebugSession session, DecodeRawValue? decode = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));

        // ⭐⭐ THE one formatter, with the ONE decoder. ⛔ BlueprintDebugSession.MarshalFromBytes is
        //    exactly what BP-01 says was "complete, tested, and used at 4 other sites in the same
        //    file" — the panel simply never called it. Injected rather than referenced so the
        //    formatter stays in the shared assembly (it cannot see this one).
        _formatter = new VariableValueFormatter(decode ?? BlueprintDebugSession.MarshalFromBytes);
        _table     = new VariableTableControl(_formatter);

        // ⭐ Watch hides Type (VariableTableColumns.Watch) — 📌 §1b's stated difference between the
        //   Details table and this one, and the whole of that difference.
        _model = new VariableTableModel(_empty, VariableTableColumns.Watch)
        {
            // ⚠ A Watch only ever shows RUNTIME values: there is no "initial" arm for a pinned pin.
            //   ⭐ Fixed deliberately rather than derived, so a paused sim still reads Current.
            RunState = VariableRunState.Running,
        };
        LastView = _model.Build();
    }

    /// <summary>
    /// ⭐⭐ The two row gestures, bound to the SAME dialog the Details table uses *(row 59b: "EDITING
    /// through the same dialog")*. ⛔ A Watch-local editor is precisely what ruling 11 forbids.
    /// </summary>
    public void BindEditGestures(VariableEditGestureBinder binder)
        => (binder ?? throw new ArgumentNullException(nameof(binder))).Attach(_table);

    /// <summary>⭐ The constructed control, so a host can bind gestures or a rail can raise them.</summary>
    public VariableTableControl Table => _table;

    /// <inheritdoc/>
    /// <remarks>
    /// ⭐⭐⭐ <b>Batch 87 — the FOURTH table host, and the handoff did not know it existed.</b> Gate 8's
    /// <c>search_graph</c> enumeration over everything that constructs a
    /// <see cref="VariableTableControl"/> returned four; the handoff named three and said <i>"if the
    /// graph finds a fourth, that is a finding."</i> ⇒ 📌 <c>R-74</c> again — <b>only the graph
    /// enumerates</b>; a grep for the two known ones would have confirmed the guess and missed this.
    /// </remarks>
    public VariableTableControl? VariableTable => _table;

    /// <summary>
    /// ⭐⭐ <b>Batch 100 (<c>100f</c>) — the row gestures this surface offers.</b>
    /// ⛔⛔ MONITORING, so NO "Properties…" — the same answer <c>AiWatchWindow</c> gives.
    ///
    /// <para>⭐⭐⭐ <b>THIS is why the gesture set is DECLARED rather than type-tested.</b> The handoff
    /// named ONE watch surface; ⚠ <b>there are TWO</b>, and an <c>if (host is AiWatchWindow)</c> in the
    /// registrar would have silently left this one with the authoring menu — the same
    /// enumerate-don't-assume miss 📌 <c>R-74</c> keeps filing.</para>
    ///
    /// <para>⛔ Answered explicitly because <c>IVariableTableHost.Gestures</c> has <b>no default
    /// body</b> — 📌 <c>U-5</c>/<c>BP-230</c>.</para>
    /// </summary>
    public VariableTableGestures Gestures => VariableTableGestures.Watch;

    /// <summary>
    /// ⭐⭐⭐ <b>What THIS PANEL would render for a row</b>, through its own formatter.
    ///
    /// <para>⚠ <b>Why this exists rather than a test building its own formatter:</b> a revert probe
    /// swapping the panel's decoder back to <c>Convert.ToHexString</c> left every rail GREEN, because
    /// they each formatted the row themselves. ⛔ That is the vacuous-rail shape — <i>ask the artefact,
    /// not something that merely resembles it</i>. This asks the panel.</para>
    /// </summary>
    public string CellText(VariableRow row) => _formatter.Cell(row, LastView.ValueMode);

    private readonly VariableValueFormatter _formatter;

    public override void OnActivated()
        => _session.OnPinValueChangedEvent += HandlePinValueChanged;

    public override void OnDeactivated()
        => _session.OnPinValueChangedEvent -= HandlePinValueChanged;

    /// <summary>
    /// ⭐⭐⭐ <b>Real.</b> 🔴 This was <c>{ /* refresh row data */ }</c> — the event fired and the panel
    /// did nothing, so a value that moved while the sim was frozen never appeared.
    /// </summary>
    private void HandlePinValueChanged(PinValueChanged evt)
    {
        LastValueChange = evt;
        ValueChangeCount++;
        Refresh();
    }

    /// <summary>
    /// ⭐ Re-reads the session's watches into rows. ⛔ Called from the change event AND from the draw
    /// path, because a watch can appear without a value ever changing (the designer just pinned it).
    /// </summary>
    public void Refresh()
    {
        var watches = _session.GetWatches();
        LastRenderedWatches = watches;
        _model.Source = new FixedVariableRowSource(WatchRowBridge.ToRows(watches));
        LastView      = _model.Build();
    }

    public override void DrawUI()
    {
        Refresh();

        // ImGui rendering requires a live context; skip in headless / test environments.
        if (ImGui.GetCurrentContext() == IntPtr.Zero) return;

        if (LastView.AllRows.Count == 0)
        {
            // ⚠ EMPTY rather than ABSENT, and it says WHY — 📌 the 2026-08-17 user ruling: an
            //   explanatory refusal beats a surface that is simply blank.
            ImGui.TextDisabled("No watches pinned.");
            return;
        }

        _table.Draw("##watchTable", LastView);
    }
}
