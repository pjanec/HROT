using System.Reflection;
using Fdp.Presentation.WindowManager;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor.NodeDrawers;
using Hrot.Editor.AiShared.Selection;
using Hrot.Editor.AiShared.Variables;
using AiSelectionStore = Hrot.Editor.AiShared.Selection.EditorSelectionStore;

namespace Hrot.Blueprints.Editor.Windows;

/// <summary>
/// Details window for the Blueprint perspective.
/// Shows the node-drawer UI for the currently-selected Blueprint node.
///
/// <para>
/// The window reads <see cref="EditorSelectionStore.ActiveSubSelection"/> each draw frame.
/// When the selection is a <see cref="BlueprintNodeSelection"/>, it resolves the matching
/// <see cref="IBlueprintNodeDrawer"/> from the <see cref="BlueprintNodeDrawerRegistry"/> and
/// creates (or reuses) an <see cref="INodeEditSession"/> for that node.
/// </para>
///
/// <para>
/// Keep all ImGui calls inside <see cref="DrawClientArea"/> so the interaction/projection
/// logic (selection → session) can be exercised in headless unit tests.
/// </para>
/// </summary>
public sealed class BlueprintDetailsWindow : ManagedWindow, IVariableDetailsHost,
                                             Hrot.Editor.AiShared.Variables.IVariableTableHost,
                                             Hrot.Editor.AiShared.Variables.IVariablePropertiesFormHost
{
    /// <summary>
    /// ⭐⭐⭐ <b>Batch 99 (<c>99a</c>) — the CUSTOM Properties form (📌 <c>R-109</c>, <c>BP-369</c>).</b>
    /// ⛔ Not a StructEdit session: two of its fields are OPERATIONS — see
    /// <see cref="VariablePropertiesModal"/>.
    /// </summary>
    /// <remarks>
    /// ⭐⭐⭐ <b>The refactor service is FORWARDED, not defaulted away</b> — 📌 the silent-default
    /// ruling: <i>"a production caller that HAS a dependency must PASS it."</i> ⚠⚠ <b>The first draft
    /// of <c>99a</c> wrote <c>new()</c> here</b> and justified it as <i>"this window has none to
    /// give"</i> — 🔴 <b>true of the window and false of its CALLER</b>: <c>EditorSubsystem</c> holds a
    /// <c>refactorService</c> and hands it to <c>BlueprintVariablesManagedWindow</c> <b>seven lines
    /// below</b> the line that constructs this one. ⇒ ⭐ exactly the shape that ruling names, and it
    /// was mine.
    ///
    /// <para>⚠ <b>It does not by itself enable renaming</b> — <c>CanRename</c> also needs a schema, and
    /// this window still has none *(see <see cref="OpenVariableProperties"/>)*. ⭐ The point is that the
    /// day a schema reaches the form, the service is ALREADY THERE and no composition-root edit is
    /// needed — ⛔ which is the half a forgotten dependency always costs later.</para>
    /// </remarks>
    private readonly VariablePropertiesModal _propertiesModal;

    /// <summary>⭐ The form, exposed so a rail can assert on the CONSTRUCTED object (📌 <c>M-22</c>).</summary>
    internal VariablePropertiesModal PropertiesForm => _propertiesModal;

    /// <inheritdoc/>
    /// <remarks>
    /// ⚠⚠ <b>The schema is <c>null</c> here, and that is MEASURED rather than lazy.</b> 📐 This window
    /// is constructed with a selection store and a drawer registry — it holds <b>no
    /// <c>IVariablesSchemaSource</c> and no <c>IRefactorService</c></b>; the schema lives in the row
    /// SOURCE the outline builds. ⇒ ⭐ the form draws <c>Name</c> <b>DISABLED with its reason</b>
    /// *(<c>VariablePropertiesModal.RenameUnavailableHere</c>)* — ⛔ never a Name box that silently does
    /// not commit. 📌 <c>M-15</c>: a rename that skips the refactor service dangles the binding.
    /// </remarks>
    public bool OpenVariableProperties(
        Hrot.Editor.AiShared.Variables.VariableRow row, bool editable)
        => _propertiesModal.Open(row, schema: null, row.Origin.AssetId, editable);

    private readonly AiSelectionStore _selectionStore;
    private readonly BlueprintNodeDrawerRegistry _drawerRegistry;

    // ⭐⭐⭐ U-6 — the SHARED variables list, hosted here rather than re-implemented.
    //    📌 Q32 ruling 1: "Details hosts the list of vars, as designed."
    //    📌 ruling 9 (the acceptance criterion): "no keeping two implementations for the same
    //       concept" ⇒ this is Track C's VariableTableControl, not a blueprint copy of it.
    private readonly VariableDetailsSection _variables;

    // The sub-selection the node arm last saw. ⭐ Used to decide when a NODE click should take the
    // panel back from a variable list — see ShowVariables.
    private object? _lastSubSelection;

    // Cached session — rebuilt when selection changes.
    private INodeEditSession? _session;
    private Guid _sessionNodeId;
    private Guid _sessionGraphId;
    /// <summary>BP-205 — the node the cached session belongs to, for its ImGui id scope.</summary>
    private Node? _sessionNode;

    // Active asset supplied by Retarget; needed to create sessions.
    private BlueprintAsset? _asset;

    // Projection helpers (extracted so headless tests can call ResolveDrawerForSelection directly).

    /// <summary>
    /// The kind of drawer that was resolved for the current selection.
    /// Null when nothing is selected, the selection is not a blueprint node, or no drawer
    /// handles the node type.  Used by tests to assert the resolved drawer kind.
    /// </summary>
    public Type? ResolvedDrawerKind { get; private set; }

    // ── ctor ─────────────────────────────────────────────────────────────────

    /// <param name="selectionStore">Per-perspective selection store.</param>
    /// <param name="drawerRegistry">Blueprint node-drawer registry.</param>
    /// <param name="idOverride">Stable ImGui id; defaults to <c>"ai_details_blueprint"</c>.</param>
    /// <param name="owningPerspective">Perspective name; defaults to <c>"Blueprint"</c>.</param>
    /// <param name="refactorService">
    /// ⭐⭐ Batch 99 (<c>99a</c>) — the service a RENAME must run. ⛔ <b>Optional for TESTS and
    /// lightweight hosts, never for the composition root</b>: 📌 the silent-default ruling — the
    /// production caller HAS one, so it passes one. ⚠ Absent ⇒ the form greys <c>Name</c> with its
    /// reason rather than renaming without it, which 📌 <c>M-15</c> makes a dangling binding.
    /// </param>
    public BlueprintDetailsWindow(
        AiSelectionStore selectionStore,
        BlueprintNodeDrawerRegistry drawerRegistry,
        string? idOverride        = null,
        string? owningPerspective = null,
        Hrot.Editor.AiShared.Refactor.IRefactorService? refactorService = null)
        : base(idOverride        ?? "ai_details_blueprint",
               "Details",
               owningPerspective ?? "Blueprint",
               WindowScope.PerspectiveBound)
    {
        _selectionStore  = selectionStore ?? throw new ArgumentNullException(nameof(selectionStore));
        _drawerRegistry  = drawerRegistry ?? throw new ArgumentNullException(nameof(drawerRegistry));
        _propertiesModal = new VariablePropertiesModal(refactorService);

        // ⭐ The formatter is built here rather than required, because a Details panel with no way to
        //   render a value is not a Details panel. ⚠ The value's RUN-STATE meaning is sequencing row
        //   58's — at authoring time a source with no byte reader renders "(pending)", which is true.
        _variables = new VariableDetailsSection(
            new VariableValueFormatter(
                RawValueDecoder.Instance));
    }

    /// <summary>
    /// ⭐ The hosted variables list. Exposed so a rail can assert on the CONSTRUCTED object rather
    /// than on whatever wired it (the 2026-08-16 control).
    /// </summary>
    public VariableDetailsSection Variables => _variables;

    /// <inheritdoc/>
    /// <remarks>
    /// ⭐⭐ <b>Batch 87 — forwarded from the hosted section</b>, so the registrar's attach loop reaches
    /// the Details table without knowing that a Details WINDOW hosts a Details SECTION. ⛔ The window
    /// does not own a second table; this is the same object <see cref="Variables"/> exposes.
    /// </remarks>
    public Hrot.Editor.AiShared.Variables.VariableTableControl? VariableTable
        => ((Hrot.Editor.AiShared.Variables.IVariableTableHost)_variables).VariableTable;

    /// <summary>
    /// ⭐⭐ <b>Batch 100 (<c>100f</c>) — the row gestures this surface offers.</b>
    /// ⭐ An AUTHORING surface: this panel is where a designer edits a declaration, and it hosts the
    /// Properties form itself.
    /// <para>⛔ Answered explicitly because <c>IVariableTableHost.Gestures</c> has <b>no default
    /// body</b> — 📌 <c>U-5</c>/<c>BP-230</c>.</para>
    /// </summary>
    public Hrot.Editor.AiShared.Variables.VariableTableGestures Gestures
        => Hrot.Editor.AiShared.Variables.VariableTableGestures.Default;

    /// <inheritdoc/>
    /// <remarks>
    /// ⭐⭐ <b><c>Q32</c> ruling 2 — "selection routes".</b> An outline click decides what this panel
    /// shows: a global list, a graph-scoped locals list, or *(for a graph or function row)* nothing,
    /// in which case the panel falls back to the node arm.
    ///
    /// <para>⭐ <b>The routing is installed by the registrar, not by the composition root</b> —
    /// <c>PerspectiveWorkspaceRegistrar.RegisterExtraWindow</c> connects any
    /// <c>IVariableOutlineSelectionSource</c> it is handed to any <c>IVariableDetailsHost</c>.
    /// ⛔ Batches 79/80/81 each lost a surface to a "someone must remember to wire it" seam.</para>
    /// </remarks>
    /// <inheritdoc/>
    /// <remarks>⭐ Row 58 — forwarded to the hosted list, which is what actually renders the column.</remarks>
    public void SetRunStateSource(Func<VariableRunState> runState)
        => _variables.SetRunStateSource(runState);

    public void ShowVariables(VariableOutlineSelection selection)
    {
        if (selection.HasRows)
        {
            // ⭐ BATCH 84 (4a/4b) — the WHOLE selection, so the clicked row is highlighted and the
            //   graph-scoped heading follows the canvas instead of naming the graph that was open
            //   when the designer clicked.
            _variables.Show(selection);
            // ⭐ Remember what the node arm was showing, so a LATER node click wins. ⛔ Without this
            //   the variable list would sit over an unrelated node selection forever.
            _lastSubSelection = _selectionStore.ActiveSubSelection;
        }
        else
        {
            _variables.Clear();
        }
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Retarget to a different active Blueprint asset (e.g. when the document changes).
    /// Clears the cached session so the next frame rebuilds it against the new asset.
    /// </summary>
    public void Retarget(BlueprintAsset? asset)
    {
        if (_asset == asset) return;
        _asset = asset;
        ClearSession();
    }

    /// <summary>
    /// Resolves (or returns cached) drawer + session for the currently-selected node.
    /// Returns the resolved <see cref="INodeEditSession"/> or null when nothing is selected.
    /// This is the core projection logic; separated so tests can call it directly without ImGui.
    /// </summary>
    public INodeEditSession? ResolveSession()
    {
        if (_asset == null) { ClearSession(); return null; }

        var sub = _selectionStore.ActiveSubSelection as BlueprintNodeSelection;
        if (sub == null) { ClearSession(); return null; }

        // Same node already has a session — reuse it.
        if (_session != null && _sessionNodeId == sub.NodeId && _sessionGraphId == sub.GraphId)
            return _session;

        // New selection — find the node in the asset graph.
        ClearSession();

        var graph = _asset.Graphs.FirstOrDefault(g => g.Id == sub.GraphId);
        if (graph == null) return null;

        var node = graph.Nodes.FirstOrDefault(n => n.Id == sub.NodeId);
        if (node == null) return null;

        var drawer = _drawerRegistry.GetDrawerFor(node);
        if (drawer == null) { ResolvedDrawerKind = null; return null; }

        ResolvedDrawerKind = drawer.GetType();
        _session       = drawer.CreateSession(node, _asset);
        _sessionNode    = node;
        _sessionNodeId  = sub.NodeId;
        _sessionGraphId = sub.GraphId;
        return _session;
    }

    // ── ManagedWindow ─────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐ Which arm the panel is showing. ⛔ Extracted from the draw path so the PRECEDENCE is
    /// checkable — drawing needs an ImGui context and no headless test can drive it.
    ///
    /// <para>⭐⭐⭐ <b>Batch 87 — the question is WHICH SURFACE, not WHICH NODE</b> *(user ruling,
    /// <c>2026-08-18</c>: <i>"it's not the selection what changes but actually the focus to different
    /// part of the UI"</i>)*. ⭐ Moving focus to the canvas hands the panel back, whether or not the
    /// selection changed — ⛔ which is precisely the case the old value test could not see.</para>
    ///
    /// <para>⭐ <b>Last CLAIM wins, in both directions.</b> Picking a variable in the outline takes the
    /// panel; returning to the canvas takes it back. ⚠ The sub-selection comparison survives as a
    /// SECONDARY path — see the body.</para>
    /// </summary>
    internal bool ShowingVariables
    {
        get
        {
            if (!_variables.HasContent) return false;

            // ⭐⭐⭐ Batch 87 — PRIMARY: which SURFACE is the designer working in (user ruling,
            //    2026-08-18). 🔴 The line below used to be the ONLY test, and it is a VALUE test
            //    standing in for the TIME claim in the comment: re-clicking the SAME node is Equals to
            //    the snapshot, so it could never win the panel back. ⛔ Only a DIFFERENT node could —
            //    which is why every test passed and the designer's real gesture failed.
            //    ⭐ Focus answers it where a click cannot: measured, a re-click is a deliberate no-op
            //    at CanvasInput and produces no signal at any of the four layers below it.
            if (_selectionStore.FocusedSurface == SelectionOrigin.GraphCanvas) return false;

            // ⭐ SECONDARY, and kept deliberately: a selection can move WITHOUT focus moving (a hotkey,
            //   anything programmatic), and the node arm should still win then. ⛔ Replacing this
            //   rather than layering would trade one blind spot for another.
            return Equals(_selectionStore.ActiveSubSelection, _lastSubSelection);
        }
    }

    protected override void DrawClientArea()
    {
        // ⭐⭐⭐ Batch 100 (100d) — WITHOUT THIS LINE THE PROPERTIES FORM DOES NOT EXIST.
        //
        // 🔴🔴 THE THIRD OCCURRENCE OF BP-327: Batch 87 shipped "the modal draws", Batch 89 fixed
        //    "Draw had no caller", and Batch 99 built this form with :50 declaring it, :125
        //    constructing it, :66 OPENING it, :53 exposing it — and ⛔ NO LINE CALLING Draw().
        //    ⚠ Batch 99's rails asserted IsOpen and the commit path. Both were true. Both were useless:
        //    the designer right-clicked "Properties…" and nothing appeared.
        //
        // ⭐⭐ FIRST, and deliberately so. This method has THREE `return`s below it; a modal submitted
        //    after any of them is unreachable on that path — ⛔ which is the same class of mistake as
        //    not calling it at all, and harder to see. ⭐ ImGui popups are their own windows, so
        //    submitting here costs nothing and is reached on every path.
        _propertiesModal.Draw();

        if (ShowingVariables)
        {
            _variables.Draw("bp_details_variables");
            return;
        }

        var session = ResolveSession();
        if (session != null)
        {
            // BP-205: scope every widget the drawer creates to the selected node. Drawers label their
            // widgets by role ("Format", "Level", …) and ImGui derives a widget's identity from its
            // label within the current id stack -- so without this a Print String's "Format" field and
            // a Format String's "Format" field are the SAME widget, and selecting the second hands it
            // the first's live input buffer. See DetailsIdScope for why this belongs here rather than
            // in each drawer.
            ImGuiNET.ImGui.PushID(DetailsIdScope.For(_sessionNode!));
            try
            {
                // Delegate all rendering to the session (which may call ImGui freely).
                session.Draw();
            }
            finally
            {
                ImGuiNET.ImGui.PopID();
            }
            return;
        }

        // ResolveSession returns null in two very different cases: nothing is selected, OR a
        // node IS selected but no drawer handles its type. Distinguish them so a selected but
        // non-editable node (PublishEvent, WaitForChannel, Return, Branch, …) shows a read-only
        // summary instead of the misleading "No node selected."
        var node = TryGetSelectedNode();
        if (node == null)
        {
            ImGuiNET.ImGui.TextDisabled("No node selected.");
            return;
        }

        DrawReadOnlySummary(node);
    }

    /// <summary>
    /// Returns the currently-selected blueprint node whether or not a drawer exists for it,
    /// or null when the active sub-selection is not a resolvable blueprint node.
    /// </summary>
    private Node? TryGetSelectedNode()
    {
        if (_asset == null) return null;
        if (_selectionStore.ActiveSubSelection is not BlueprintNodeSelection sub) return null;
        var graph = _asset.Graphs.FirstOrDefault(g => g.Id == sub.GraphId);
        return graph?.Nodes.FirstOrDefault(n => n.Id == sub.NodeId);
    }

    /// <summary>
    /// Renders a read-only property summary for a selected node that has no editable drawer.
    /// Reflects the node's simple scalar properties (string/enum/bool/number), skipping the
    /// structural members (Id, Pins, EditorMetadata) that are not user-facing configuration.
    /// </summary>
    private static void DrawReadOnlySummary(Node node)
    {
        var typeName = node.GetType().Name;
        if (typeName.EndsWith("Node", System.StringComparison.Ordinal))
            typeName = typeName[..^4];
        ImGuiNET.ImGui.TextUnformatted(typeName);
        ImGuiNET.ImGui.Separator();

        foreach (var p in node.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (p.GetIndexParameters().Length != 0) continue;
            if (p.Name is "Id" or "Pins" or "EditorMetadata") continue;

            var t = p.PropertyType;
            var under = System.Nullable.GetUnderlyingType(t) ?? t;
            bool simple = under == typeof(string) || under.IsEnum || under == typeof(bool)
                          || under == typeof(int) || under == typeof(long) || under == typeof(short)
                          || under == typeof(float) || under == typeof(double) || under == typeof(System.Guid);
            if (!simple) continue;

            object? val;
            try { val = p.GetValue(node); } catch { continue; }
            var text = val?.ToString() ?? "(null)";
            if (text.Length == 0) text = "(empty)";

            ImGuiNET.ImGui.TextDisabled($"{p.Name}:");
            ImGuiNET.ImGui.SameLine();
            ImGuiNET.ImGui.TextUnformatted(text);
        }

        ImGuiNET.ImGui.Spacing();
        ImGuiNET.ImGui.TextDisabled("This node type has no editable properties.");
    }

    // ── Private ───────────────────────────────────────────────────────────────

    private void ClearSession()
    {
        _session?.Dispose();
        _session        = null;
        _sessionNode    = null;
        _sessionNodeId  = Guid.Empty;
        _sessionGraphId = Guid.Empty;
        ResolvedDrawerKind = null;
    }
}
