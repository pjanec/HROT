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
public sealed class AiWatchWindow : ManagedWindow
{
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
    }

    /// <summary>Exposes the manager for test verification (shared-instance check).</summary>
    public IDataBreakpointManager Manager => _manager;

    /// <summary>⭐ The pinned-variable feed. Pin / Unpin / MarkStale are called by the host.</summary>
    public PinnedVariableRowSource Pinned => _pinned;

    /// <summary>The variables half's model, or null when no formatter was supplied.</summary>
    public VariableTableModel? Variables => _variables;

    /// <summary>True when the variables half is wired. ⭐ A rail asserts on this, not on the registrar.</summary>
    public bool HasVariableWatch => _variables != null;

    protected override void DrawClientArea()
    {
        DrawBreakpointWatches();

        if (_variables == null || _control == null) return;

        ImGuiNET.ImGui.Separator();
        // ⭐ Named, so the two lists cannot read as one feature with an odd column set.
        ImGuiNET.ImGui.TextDisabled("Pinned variables");

        var view = _variables.Build();
        if (view.AllRows.Count == 0)
        {
            ImGuiNET.ImGui.TextDisabled("No pinned variables. Pin one from the Variables table.");
            return;
        }
        _control.Draw(Id + "_vars", view);
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
