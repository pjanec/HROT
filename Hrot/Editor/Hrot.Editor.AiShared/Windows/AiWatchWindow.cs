using Fdp.Diagnostics.Contracts.Panels;
using Fdp.Presentation.WindowManager;
using Hrot.Diagnostics.Breakpoints;
using Hrot.Editor.AiShared.Variables;

namespace Hrot.Editor.AiShared.Windows;

/// <summary>
/// Per-perspective Watch window for the AI editor. Registered with
/// <see cref="WindowScope.PerspectiveBound"/> so each AI perspective (BTree, HSM, Blueprint) has its
/// own docking slot.
///
/// <para>⭐⭐⭐ <b><c>C-watch</c>: it draws TWO lists, because there are TWO concepts — measured, not
/// assumed.</b></para>
///
/// <list type="bullet">
///   <item><b>Breakpoint watches</b> — entries from the shared <see cref="IDataBreakpointManager"/>
///         with <c>IsWatch</c> set. A <c>Breakpoint</c> is a CONDITION that fires: it carries a
///         predicate, <c>Enabled</c> and <c>HitCount</c>, and its identity is a <c>Guid</c>.</item>
///   <item><b>Pinned variables</b> — <c>VariableRow</c>s in a <see cref="PinnedVariableRowSource"/>.
///         A row is an OBSERVED IDENTITY: <c>(AssetId, Entity, Section, VariablePath)</c>, with bytes,
///         staleness and a row kind. It has no condition and cannot fire.</item>
/// </list>
///
/// <para>⛔ <b>They are not the same entity, and merging them silently would have been wrong.</b>
/// 📐 The evidence is in the persistence layer, which already treats them as separate lists:
/// <c>DebugSessionPersistence.Save</c> takes <c>dbmBreakpoints</c> (where <c>IsWatch</c> lives) AND
/// <c>watches</c> (<c>Blueprints.Core.Debug.Watch</c>, persisted as <c>WatchEntry
/// { AssetId, GraphId, PinId, … }</c>) as two parameters into two fields of one file. ⚠ There are in
/// fact <b>three</b> watch-shaped things in the codebase, not two — the blueprint PIN watch is the
/// third.</para>
///
/// <para>⭐ So the breakpoint list is untouched and the variable watch is wired as its own feed, under
/// its own heading. ⛔ Unifying them is a design question, not a wiring one.</para>
///
/// <para>⭐ The pinned rows do NOT go through this window's old value carrier — <c>PinnedSource</c>
/// reads bytes through the row's own <c>ReadValue</c>, so a 136-byte struct pins and renders. And
/// <c>Type</c> is hidden here by default (<see cref="VariableTableColumns.Watch"/>): monitoring is not
/// authoring.</para>
/// </summary>
public sealed class AiWatchWindow : ManagedWindow, Variables.IVariableTableHost
{
    /// <summary>
    /// ⭐⭐ <b>Batch 100 (<c>100f</c>) — the row gestures this surface offers.</b>
    /// ⛔⛔ MONITORING, so NO &quot;Properties…&quot; — 📌 the user: <i>"no one is interested in the other properties than the value in the Watch window."</i> ⭐ &quot;Edit value…&quot; STAYS: Batch 84 built writing a live value while frozen, and this is exactly where a designer does it.
    /// <para>⛔ Answered explicitly because <c>IVariableTableHost.Gestures</c> has
    /// <b>no default body</b> — 📌 <c>U-5</c>/<c>BP-230</c>: <i>"a default body is the
    /// interface volunteering to lie on an implementer's behalf."</i></para>
    /// </summary>
    public Hrot.Editor.AiShared.Variables.VariableTableGestures Gestures => Hrot.Editor.AiShared.Variables.VariableTableGestures.Watch;

    private readonly IDataBreakpointManager   _manager;
    private readonly PinnedVariableRowSource  _pinned = new();
    private readonly VariableTableModel?      _variables;
    private readonly VariableTableControl?    _control;

    /// <summary>
    /// Constructs the window.
    /// </summary>
    /// <param name="id">Unique ImGui window id.</param>
    /// <param name="owningPerspective">Perspective key (e.g. "BTree").</param>
    /// <param name="manager">Shared data breakpoint manager (shared, not duplicated).</param>
    /// <param name="formatter">
    /// The value formatter for pinned variable rows. ⚠ Optional so an existing host that has none
    /// keeps working — ⛔ but a production caller that HAS one must pass it, per the
    /// silent-default rule; the registrar does.
    /// </param>
    public AiWatchWindow(
        string id,
        string owningPerspective,
        IDataBreakpointManager manager,
        VariableValueFormatter? formatter = null)
        : base(id, "Watch", owningPerspective, WindowScope.PerspectiveBound)
    {
        _manager = manager ?? throw new ArgumentNullException(nameof(manager));
        if (formatter != null)
        {
            _control   = new VariableTableControl(formatter);
            _variables = new VariableTableModel(_pinned, VariableTableColumns.Watch);
        }
        IsOpen = false;

        // ⭐⭐⭐ U-obs-2 / U1b — DECLARED AT CONSTRUCTION, ALWAYS: never gated on CaptureEnabled, and
        //    never gated on whether a formatter was supplied. ⛔ A window whose formatter is null has
        //    nothing to PUBLISH (no VariableTableModel — see HasVariableWatch), but it is still an
        //    INSTRUMENTED watch: a reader must be able to tell "this host never converted its Watch"
        //    from "this host's Watch has nothing to show" — 📄 DESIGN_UI_Observability_Snapshot.md
        //    AS-BUILT deviation ④, mirrored from EntityBlueprintsPanel's pilot.
        PanelSnapshot.DeclareInstrumented(Id);
    }

    /// <summary>Exposes the manager for test verification (shared-instance check).</summary>
    public IDataBreakpointManager Manager => _manager;

    /// <summary>⭐ The pinned-variable feed. Pin / Unpin / MarkStale are called by the host.</summary>
    public PinnedVariableRowSource Pinned => _pinned;

    /// <summary>The variables half's model, or null when no formatter was supplied.</summary>
    public VariableTableModel? Variables => _variables;

    /// <summary>True when the variables half is wired. ⭐ A rail asserts on this, not on the registrar.</summary>
    public bool HasVariableWatch => _variables != null;

    /// <inheritdoc/>
    /// <remarks>
    /// ⭐⭐ <b>Batch 87 — <c>BP-330</c> closed.</b> <c>_control</c> was <b>private with no accessor</b>,
    /// so nothing outside this class could bind the row gestures to the Watch's table — the same
    /// no-caller shape as the Details panel, one window over. ⚠ <b>Null when the Watch has no variable
    /// panel</b> *(no formatter/source was supplied)*, which is a shape, not a defect.
    /// </remarks>
    public VariableTableControl? VariableTable => _control;

    /// <inheritdoc/>
    /// <remarks>⭐⭐ <c>W4</c> — this model is the WATCH half of §7's <i>"both panels show the SAME
    /// staged bytes"</i>. ⚠ <c>null</c> when the Watch has no variable panel, exactly as
    /// <see cref="VariableTable"/> is.</remarks>
    public VariableTableModel? TableModel => _variables;

    private Func<VariableRunState>? _runState;

    /// <summary>
    /// ⭐⭐⭐ <b>Batch 100 (<c>100e</c>) — WITHOUT THIS THE WATCH SHOWS <c>0</c> FOR EVER.</b>
    ///
    /// <para>🔴🔴 <b>The defect, and the row was never the problem.</b> The pinned row IS a live
    /// camera — <c>SectionVariableRowSource</c> and <c>BlackboardSectionRowSource</c> both pass
    /// <c>AssetTick</c>, and <c>PinnedVariableRowSource.Pin</c> stores the row object unchanged.
    /// ⛔ <b>This window built its <c>VariableTableModel</c> and was never given a run-state source</b>
    /// ⇒ the model sat at <c>Planning</c> ⇒ <c>VariableValue.ModeFor(Planning)</c> picks the
    /// <b>INITIAL</b> arm *(<c>Q32</c> ruling 3)</b> ⇒ it rendered <c>DefaultValueJson</c>, always.</para>
    ///
    /// <para>📌 <b><c>CLAUDE.md</c> verbatim: <i>"a production caller that HAS a dependency must PASS
    /// it."</i></b> ⚠⚠ <b>The registrar holds <c>_runState</c>, hands it to the details host, and holds
    /// this window — and did not.</b> ⭐ <b>The ninth instance of that shape.</b></para>
    ///
    /// <para>⭐ Same seam as <c>IVariableDetailsHost.SetRunStateSource</c>, deliberately: ⛔ one
    /// concept, one method name *(ruling 9)*, and ⛔ <b>nothing new for <c>EditorSubsystem</c> to
    /// forget</b> — the registrar already reaches this window *(📌 <c>R-67</c>)*.</para>
    /// </summary>
    public void SetRunStateSource(Func<VariableRunState> runState)
        => _runState = runState ?? throw new ArgumentNullException(nameof(runState));

    /// <summary>⭐ True once a run-state source is installed. ⭐ A rail surface — asserted on the
    /// CONSTRUCTED window, ⛔ never on the registrar's source *(📌 <c>R-67</c>)*.</summary>
    public bool HasRunStateSource => _runState != null;

    /// <summary>
    /// ⭐ Re-reads the run state onto the model. ⚠ Called every frame from <see cref="DrawClientArea"/>
    /// AND directly by rails — ⛔ the draw path goes through ImGui, and a rail that could only reach
    /// this through a rendered frame would be testing the harness rather than the wiring.
    /// </summary>
    public void SyncRunState()
    {
        if (_runState != null && _variables != null) _variables.RunState = _runState();
    }

    protected override void DrawClientArea() => DrawContent();

    /// <summary>
    /// ⭐⭐⭐ <b>U-obs-2 — BUILD · CAPTURE · the ImGui-context guard · RENDER.</b>
    /// 📄 <c>docs/DESIGN_UI_Observability_Snapshot.md</c> §Example, mirroring
    /// <c>EntityBlueprintsPanel.DrawUI</c> exactly.
    ///
    /// <para>⛔⛔ <b>Extracted from <see cref="DrawClientArea"/> so it is callable HEADLESS</b> — the
    /// base <c>ManagedWindow.Render</c> calls <c>Gui.Begin</c> before <c>DrawClientArea</c>, so a test
    /// cannot reach the protected override without a live context. 📌 Same shape as
    /// <c>GraphSignatureWindow.DrawContent</c> / <c>AiGraphCanvasWindow.SimulateDrawClientArea</c> —
    /// an established seam in this codebase, not a new idiom.</para>
    ///
    /// <para>⚠⚠ <b>ONE DEVIATION FROM THE OBVIOUS ORDER, and it is deliberate — the pinned-variables VIEW
    /// is built and PUBLISHED before the ImGui-context guard, not after.</b> ⛔ If the capture sat after
    /// the guard, a headless run would observe NOTHING and the dump would depend on a live GPU context —
    /// exactly the pilot's own correction (§Example's AS-BUILT deviation ①). ⇒ ⭐ the model is this
    /// panel's truth whether or not anyone paints it.</para>
    /// </summary>
    internal void DrawContent()
    {
        // 1. BUILD — a pure-ish projection of the pinned rows. This IS the dumpable model.
        //    ⛔ null when the variables half is not wired (no formatter was supplied) — there is
        //    nothing to publish, but the window is still DECLARED instrumented (constructor).
        VariableTableView? view = null;
        if (_variables != null && _control != null)
        {
            // ⭐⭐ Batch 100 (100e) — per frame, ⛔ not captured once: the sim starts and pauses under
            //    the designer, and a stale snapshot would keep showing authored defaults during a run.
            SyncRunState();
            view = _variables.Build();
        }

        // 2. CAPTURE — flag-gated, and BEFORE the render guard so a headless run still observes it.
        if (PanelSnapshot.CaptureEnabled && view != null)
            PanelSnapshot.Register(new VariableTablePanelViewModel(Id, PanelIds.Watch, view));

        // 3. RENDER — only from here on do we touch ImGui, and only with a live context.
        if (ImGuiNET.ImGui.GetCurrentContext() == IntPtr.Zero) return;

        DrawBreakpointWatches();

        if (view == null) return;

        ImGuiNET.ImGui.Separator();
        // ⭐ Named, so the two lists cannot read as one feature with an odd column set.
        ImGuiNET.ImGui.TextDisabled("Pinned variables");

        if (view.AllRows.Count == 0)
        {
            ImGuiNET.ImGui.TextDisabled("No pinned variables. Pin one from the Variables table.");
            return;
        }
        _control!.Draw(Id + "_vars", view);
    }

    private void DrawBreakpointWatches()
    {
        // Headless-safe: only called when an ImGui frame is active.
        var watches = _manager.AllBreakpoints.Where(bp => bp.IsWatch).ToList();
        if (watches.Count == 0)
        {
            ImGuiNET.ImGui.TextDisabled("No watch entries. Right-click a breakpoint → Mark as Watch.");
            return;
        }

        if (ImGuiNET.ImGui.BeginTable("##watches", 3,
            ImGuiNET.ImGuiTableFlags.Borders | ImGuiNET.ImGuiTableFlags.RowBg))
        {
            ImGuiNET.ImGui.TableSetupColumn("Name");
            ImGuiNET.ImGui.TableSetupColumn("Enabled");
            ImGuiNET.ImGui.TableSetupColumn("Hits");
            ImGuiNET.ImGui.TableHeadersRow();

            foreach (var w in watches)
            {
                ImGuiNET.ImGui.TableNextRow();
                ImGuiNET.ImGui.TableNextColumn();
                ImGuiNET.ImGui.TextUnformatted(w.DisplayName ?? w.Id.ToString());
                ImGuiNET.ImGui.TableNextColumn();
                ImGuiNET.ImGui.TextUnformatted(w.Enabled ? "Yes" : "No");
                ImGuiNET.ImGui.TableNextColumn();
                ImGuiNET.ImGui.TextUnformatted(w.HitCount.ToString());
            }

            ImGuiNET.ImGui.EndTable();
        }
    }
}
