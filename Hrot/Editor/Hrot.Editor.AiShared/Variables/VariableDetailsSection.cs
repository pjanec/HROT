using System;

namespace Hrot.Editor.AiShared.Variables;

/// <summary>
/// ⭐⭐⭐ <b><c>U-6</c> — the variables list, as a drawable a DETAILS panel can host.</b>
///
/// <para>📌 <b><c>Q32</c> ruling 1:</b> <i>"Details hosts the list of vars, as designed."</i>
/// 📌 <b><c>Q32</c> ruling 6:</b> <i>"The same Details panel is REUSED for every asset type — HSM,
/// BTree, Blueprint ⇒ this is a cross-host deliverable, not a blueprint one."</i></para>
///
/// <para>⭐⭐ <b>Why this type is thin, and why it is in <c>AiShared</c>.</b> 📌 <b>ruling 9</b> is the
/// acceptance criterion — <i>"no keeping two implementations for the same concept"</i> — and
/// <see cref="VariableTableControl"/> already renders a row list while knowing nothing about its
/// source. ⛔ <b>This batch is PLACEMENT and ROUTING, not construction:</b> a blueprint-local copy of
/// the table would be the exact thing <c>U-6</c> exists to prevent.</para>
///
/// <para>⭐ <b>The host draws it; it does not own a window.</b> That is what lets one Details panel per
/// perspective host the same list — ⛔ a <c>ManagedWindow</c> here would have forced a second Details
/// window on Blueprint, which already has one.</para>
///
/// <para>⚠ <b>The VALUE column's run-state meaning is NOT this batch</b> *(sequencing row 58,
/// <i>"Then values as a second slice"</i>)*. At authoring time there is no entity, so a source without
/// a byte reader renders <c>(pending)</c> — ⛔ not <c>&lt;unreadable&gt;</c>, which would claim a decode
/// failure that never happened.</para>
/// </summary>
public sealed class VariableDetailsSection : IVariableTableHost
{
    private readonly VariableTableControl _control;
    private readonly VariableTableModel   _model;

    /// <param name="formatter">
    /// ⭐ The one value formatter, shared with the standalone table and the Watch — ⛔ two formatters
    /// would be two places to fix a rendering rule.
    /// </param>
    public VariableDetailsSection(VariableValueFormatter formatter, VariableTableColumns? columns = null)
    {
        if (formatter is null) throw new ArgumentNullException(nameof(formatter));

        _control = new VariableTableControl(formatter);
        _model   = new VariableTableModel(
            new FixedVariableRowSource(Array.Empty<VariableRow>()),
            columns ?? VariableTableColumns.Details);
    }

    /// <summary>⭐ The constructed model — a rail asserts on THIS, not on whatever built it.</summary>
    public VariableTableModel Model => _model;

    private Func<VariableRunState>? _runState;

    /// <summary>
    /// ⭐⭐ Supplies the run state, so the ONE Value column switches meaning *(row 58, ruling 3)*.
    /// ⛔ Installed by the registrar from the debug-session registry it already holds — <b>not</b>
    /// another argument for the composition root to remember.
    /// </summary>
    public void SetRunStateSource(Func<VariableRunState> runState)
        => _runState = runState ?? throw new ArgumentNullException(nameof(runState));

    /// <summary>True once a run-state source is installed. ⭐ A rail surface.</summary>
    public bool HasRunStateSource => _runState != null;

    /// <summary>
    /// ⭐ Re-reads the run state onto the model. Called every frame from <see cref="Draw"/>, and
    /// directly by rails — ⛔ the draw path goes through ImGui, which no headless test can drive.
    /// </summary>
    public void SyncRunState()
    {
        if (_runState != null) _model.RunState = _runState();
    }

    /// <summary>⭐ The constructed control, so a host can bind its two gestures (rows 59 / 59c).</summary>
    public VariableTableControl Control => _control;

    /// <inheritdoc/>
    /// <remarks>
    /// 🔴🔴 <b>Batch 87 — this section is the host NOTHING was attached to.</b> The property existed and
    /// the registrar bound only the standalone window's table, so the Details panel drew rows with no
    /// menu and no double-click. ⭐ Declaring the interface is what puts it in the registrar's ONE
    /// attach loop instead of in a second line someone must remember.
    /// </remarks>
    VariableTableControl? IVariableTableHost.VariableTable => _control;

    /// <summary>
    /// What the current list is — e.g. <c>"Variables"</c> or <c>"Local Variables — Tick"</c>.
    /// ⭐ Null when nothing is shown. ⛔ The heading is not decoration: ruling 2 routes between a
    /// GLOBAL list and a GRAPH-SCOPED one, and they are otherwise identical tables.
    /// </summary>
    public string? Heading => _headingAtReadTime?.Invoke() ?? _heading;

    private string?       _heading;
    private Func<string?>? _headingAtReadTime;

    /// <summary>True once a source has been supplied. ⭐ A rail surface, and the host's draw gate.</summary>
    public bool HasContent => _heading != null;

    /// <summary>
    /// ⭐⭐ <b>Batch 84 item <c>4a</c> — which row the outline selected.</b> 📌 §1: <i>"the routing key
    /// is <c>(asset, section)</c> <b>+ a highlight</b>."</i> ⛔ A SEPARATE state from the change
    /// highlight — see <see cref="VariableTableView.IsSelected"/>.
    /// </summary>
    public string? SelectedVariablePath => _model.SelectedVariablePath;

    /// <summary>
    /// ⭐⭐ Points the list at a source. 📌 <b><c>Q32</c> ruling 2</b>, the panel's whole navigation
    /// model: <i>"Selection routes: click a global in My Blueprint ⇒ the list of globals / working
    /// state. Click a local ⇒ the locals of the currently selected graph."</i>
    /// </summary>
    public void Show(string heading, IVariableRowSource source)
        => Show(new VariableOutlineSelection(heading, source));

    /// <summary>
    /// ⭐⭐⭐ <b>Batch 84 — the whole selection, so the heading can follow the canvas and the clicked
    /// row can be highlighted.</b>
    ///
    /// <para>📐 <b>Why the heading needed this and the rows did not:</b> the graph-scoped source
    /// already resolves the graph at READ time, so its ROWS follow the canvas — ⛔ but
    /// <c>$"Local Variables — {graph.Name}"</c> was computed once, at click time. ⚠ The result was
    /// worse than staleness: the rows updated while the label kept naming the OLD graph.</para>
    /// </summary>
    public void Show(VariableOutlineSelection selection)
    {
        if (selection.Source is null)
            throw new ArgumentException("selection has no source", nameof(selection));
        if (string.IsNullOrEmpty(selection.Heading))
            throw new ArgumentException("heading is required", nameof(selection));

        _model.Source               = selection.Source;
        _model.SelectedVariablePath = selection.SelectedVariablePath;
        _heading                    = selection.Heading;
        _headingAtReadTime          = selection.HeadingAtReadTime;
    }

    /// <summary>⭐ Lets go, so a stale list cannot outlive the selection that produced it.</summary>
    public void Clear()
    {
        _heading                    = null;
        _headingAtReadTime          = null;
        _model.SelectedVariablePath = null;
        _model.Source               = new FixedVariableRowSource(Array.Empty<VariableRow>());
    }

    /// <summary>
    /// Draws the heading and the table. ⛔ No-op when nothing is shown — the HOST decides what to draw
    /// instead, because "nothing selected" reads differently in a node inspector than in a table.
    /// </summary>
    public void Draw(string id)
    {
        SyncRunState();

        if (!HasContent) return;
        if (ImGuiNET.ImGui.GetCurrentContext() == IntPtr.Zero) return;

        ImGuiNET.ImGui.TextUnformatted(Heading!);
        ImGuiNET.ImGui.Separator();
        _control.Draw(id, _model.Build());
    }
}
