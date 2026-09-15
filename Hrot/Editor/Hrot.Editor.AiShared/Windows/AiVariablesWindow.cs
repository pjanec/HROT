using System;
using System.Collections.Generic;
using Fdp.Diagnostics.Contracts.Panels;
using Fdp.Presentation.WindowManager;
using Hrot.Editor.AiShared.Variables;

namespace Hrot.Editor.AiShared.Windows;

/// <summary>
/// ⭐⭐⭐ <b><c>C-table</c> + <c>C-tick</c> + <c>C-dialog</c>, HOSTED.</b> One variables table per
/// perspective, fed by an <see cref="IVariableRowSource"/>.
///
/// <para><b>Why a window of its own and not a fold into <c>BlueprintDetailsWindow</c>.</b> 📐 That
/// window is the NODE inspector — it takes the selection store, caches a session per selected node and
/// draws node property editors. ⛔ Putting a variables table inside it IS
/// <c>Architect_Question_38</c>'s merge, and <c>Q38</c> is deferred by the user. ⭐ Additive now beats
/// pre-empting a design task; <c>Q38</c> exists to rationalise the window count later.</para>
///
/// <para><b>Source-agnostic by construction.</b> The window never sees an asset — it holds a
/// <see cref="VariableTableModel"/> whose <see cref="VariableTableModel.Source"/> is swapped. That is
/// what makes section routing a one-line assignment and what lets the same window render a
/// heterogeneous set (several assets, several entities) unchanged.</para>
///
/// <para>⚠ <b>Purely additive.</b> <c>VariablesPanelControl</c> and
/// <c>BlackboardAuthoringWindow</c> are untouched and still drawn — the user's ruling for this
/// batch.</para>
/// </summary>
public sealed class AiVariablesWindow : ManagedWindow, Variables.IVariableTableHost
{
    /// <summary>
    /// ⭐⭐ <b>Batch 100 (<c>100f</c>) — the row gestures this surface offers.</b>
    /// ⭐ An AUTHORING surface — the asset's variable list.
    /// <para>⛔ Answered explicitly because <c>IVariableTableHost.Gestures</c> has
    /// <b>no default body</b> — 📌 <c>U-5</c>/<c>BP-230</c>: <i>"a default body is the
    /// interface volunteering to lie on an implementer's behalf."</i></para>
    /// </summary>
    public Hrot.Editor.AiShared.Variables.VariableTableGestures Gestures => Hrot.Editor.AiShared.Variables.VariableTableGestures.Default;

    private readonly VariableTableControl _control;
    private readonly VariableTableModel   _model;

    /// <param name="id">Unique ImGui window id.</param>
    /// <param name="owningPerspective">Perspective key (e.g. <c>"BTree"</c>).</param>
    /// <param name="formatter">The one value formatter — shared with Watch, not duplicated.</param>
    /// <param name="columns">
    /// Defaults to <see cref="VariableTableColumns.Details"/> — ⭐ Details is authoring, so
    /// <c>Type</c> is shown; Watch hides it. That difference is the whole toggle.
    /// </param>
    public AiVariablesWindow(
        string id,
        string owningPerspective,
        VariableValueFormatter formatter,
        VariableTableColumns? columns = null)
        : base(id, "Variable Values", owningPerspective, WindowScope.PerspectiveBound)
    {
        if (formatter is null) throw new ArgumentNullException(nameof(formatter));

        _control = new VariableTableControl(formatter);
        _model   = new VariableTableModel(
            new FixedVariableRowSource(Array.Empty<VariableRow>()),
            columns ?? VariableTableColumns.Details);

        IsOpen = false;

        // ⭐⭐⭐ U1b — DECLARED AT CONSTRUCTION, ALWAYS, and NOT gated on CaptureEnabled.
        //    ⛔ A window nobody opens never draws; if instrumentation were declared by DRAWING, this
        //      panel would be indistinguishable from one nobody converted ⇒ the reader could not tell
        //      "showed nothing" from "not instrumented". 📌 Mirrors EntityBlueprintsPanel (U-obs-1's
        //      pilot) — the address here is the window's own id, unique among the three per-perspective
        //      hosts (📄 PanelIds.cs — Id is the ADDRESS, PanelIds.Variables is the KIND).
        PanelSnapshot.DeclareInstrumented(Id);
    }

    /// <summary>⭐ The constructed model — a rail asserts on THIS, not on the registrar's source.</summary>
    public VariableTableModel Model => _model;

    /// <summary>⭐ The constructed control, so a host can bind its two gestures.</summary>
    public VariableTableControl Control => _control;

    /// <inheritdoc/>
    /// <remarks>⭐ Batch 87 — the same object <see cref="Control"/> returns. ⛔ NOT a second accessor:
    /// the interface is how the registrar reaches EVERY host without knowing their concrete types.</remarks>
    VariableTableControl? Variables.IVariableTableHost.VariableTable => _control;

    /// <inheritdoc/>
    Variables.VariableTableModel? Variables.IVariableTableHost.TableModel => _model;

    /// <summary>
    /// ⭐⭐⭐ <b>Whether this window's row gestures are ATTACHED</b> — 📌 <c>R-67</c>. A rail that pulls
    /// this window out of the real <see cref="Fdp.Presentation.WindowManager.WindowManager"/> can now
    /// ask the artefact itself; ⛔ before, the only way to "check" was to re-do the wiring and assert on
    /// the copy, which is exactly why Batch 83's dialog shipped green and dead.
    /// </summary>
    public bool HasEditGestures => _control.HasEditGestures;

    /// <summary>The section this table is currently filtered to, or null. ⭐ Set by the outline.</summary>
    public string? Section { get; private set; }

    /// <summary>
    /// ⭐⭐ Routes the table to a <c>(source, section)</c> pair. 📄 design §1c — <i>"selection yields a
    /// SECTION, not a variable"</i> ⇒ this is what an outline click calls, and it is the ONLY way the
    /// table changes what it shows.
    /// </summary>
    public void ShowSection(string? section, IVariableRowSource source)
    {
        _model.Source = source ?? throw new ArgumentNullException(nameof(source));
        Section       = section;
    }

    /// <summary>Clears the table without unhosting it. ⭐ An empty table is a state, not an absence.</summary>
    public void Clear()
    {
        _model.Source = new FixedVariableRowSource(Array.Empty<VariableRow>());
        Section       = null;
    }

    /// <summary>Planning / Running / Replay. ⭐ Drives both the highlight and the edit availability.</summary>
    private Func<VariableRunState>? _runStateSource;

    /// <summary>
    /// ⭐⭐ Row 58 — supplies the run state, so the ONE Value column switches meaning
    /// *(<c>Q32</c> ruling 3)*. ⛔ Installed by the registrar from the debug-session registry it
    /// already holds; ⚠ the settable <see cref="RunState"/> below survives for tests and for a host
    /// that genuinely knows better (replay).
    /// </summary>
    public void SetRunStateSource(Func<VariableRunState> runState)
        => _runStateSource = runState ?? throw new ArgumentNullException(nameof(runState));

    /// <summary>True once a run-state source is installed. ⭐ A rail surface.</summary>
    public bool HasRunStateSource => _runStateSource != null;

    /// <summary>⭐ Re-reads the run state onto the model — driven every frame, and by rails.</summary>
    public void SyncRunState()
    {
        if (_runStateSource != null) _model.RunState = _runStateSource();
    }

    public VariableRunState RunState
    {
        get => _model.RunState;
        set => _model.RunState = value;
    }

    /// <summary>The facets rows are grouped by. ⭐ Empty means one flat list.</summary>
    public IReadOnlyList<VariableFacet> GroupBy
    {
        get => _model.GroupBy;
        set => _model.GroupBy = value;
    }

    /// <summary>
    /// ⭐⭐⭐ <b><c>U-obs-1</c>: BUILD · CAPTURE.</b> 📄 <c>docs/DESIGN_UI_Observability_Snapshot.md</c>
    /// §Example, mirroring <c>EntityBlueprintsPanel.DrawUI</c> (the pilot).
    ///
    /// <para>⚠⚠ <b>Runs with NO ImGui context required</b> — unlike the pilot, this window is a
    /// <see cref="ManagedWindow"/>, whose <see cref="ManagedWindow.Render"/> already calls
    /// <c>Gui.Begin</c> before <see cref="DrawClientArea"/> is ever reached, so a headless caller cannot
    /// go through <c>Render</c> at all. ⇒ ⭐ this method IS the headless entry point: it is called first
    /// from <see cref="DrawClientArea"/> (before that method's ImGui-only calls), and directly by
    /// <c>SimulateDrawClientArea</c> for tests — 📌 the same split <c>AiGraphCanvasWindow</c> uses.</para>
    /// </summary>
    private VariableTableView BuildAndPublish()
    {
        SyncRunState();

        var view = _model.Build();
        if (PanelSnapshot.CaptureEnabled)
            PanelSnapshot.Register(new VariableTablePanelViewModel(Id, PanelIds.Variables, view, _control.Formatter));

        return view;
    }

    protected override void DrawClientArea()
    {
        var view = BuildAndPublish();

        if (view.AllRows.Count == 0)
        {
            // ⚠ EMPTY rather than ABSENT — the same rule the sections follow. A table that vanishes
            //    when its section is empty reads as a broken panel.
            ImGuiNET.ImGui.TextDisabled(Section == null
                ? "No section selected. Pick one in My Blueprint."
                : $"No variables in '{Section}'.");
            return;
        }
        _control.Draw(Id, view);
    }

    /// <summary>
    /// ⭐ Test hook — runs the BUILD + CAPTURE portion of <see cref="DrawClientArea"/> without requiring
    /// a live ImGui context. 📌 Mirrors <c>AiGraphCanvasWindow.SimulateDrawClientArea</c>.
    /// </summary>
    internal VariableTableView SimulateDrawClientArea() => BuildAndPublish();
}
