using System;
using System.Collections.Generic;
using System.Text.Json;
using Fdp.Presentation.Editing;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Inspector;
using Hrot.Editor.AiShared.Selection;
using StructEdit.Core;

namespace Hrot.Editor.AiShared.Shell;

/// <summary>
/// ⭐⭐⭐ <b><c>S2</c> — <c>InspectorWindow</c>'S NODE ARMS, AS A DETAILS VIEW.</b>
/// 📄 <c>DESIGN_Details_Panel_View_Switching.md</c> §7.3's catalogue *(`details.nodeproperties`,
/// <b>Rank 20</b>)* · §7.4 *(<c>InspectorWindow ..&gt; NodePropertiesDetailsView : content EXTRACTED
/// to</c>)* · §7.6 ②.
///
/// <para>🔒 <b>The user's ask, verbatim (<c>2026-08-22</c>):</b> <i>"the 'Inspector' window there shows
/// the selected-node details — and this is exactly what should be the default view shown in the Details
/// window for BTree. The 'Blackboard Variables' view would be the other view selectable via Details
/// window toolbar."</i></para>
///
/// <para>⛔⛔ <b>TWO ARMS CAME ACROSS, NOT ONE — and that is a measured decision, not scope creep.</b>
/// 📄 <c>TASKS_One_Shell_BP399.md</c> §6 / <c>BP-431</c>: §7.6 named only the <b>facet</b> arm, but
/// 📐 the <b>default-value</b> arm reads the SAME <c>_currentFacet</c> field *(the retired
/// <c>InspectorWindow.cs:356</c>)* ⇒ ⛔ extracting one alone would force a <b>second facet cache</b>
/// while the window kept its own — ruling 9, the duplication §7 exists to end. ⭐ And Batch 74's own
/// record of the second arm settles that it belongs here: <i>"the surface earns itself: it is
/// <b>NODE-scoped</b> (you see the default of the variable this node writes) where Track C's table is
/// <b>ASSET-scoped</b>."</i> ⇒ ⭐⭐ <b>it IS node properties.</b></para>
///
/// <para>⭐⭐ <b>What each arm is:</b>
/// <list type="number">
///   <item><b>The FACET</b> — the selected node's editable fields, rendered through StructEdit, committed
///   on every dirty frame so edits flow back to the asset continuously.</item>
///   <item><b>The DEFAULT VALUE (<c>B-3</c>)</b> — when the facet carries an <c>ExpressionTargetField</c>
///   *(the blackboard variable the action WRITES)*, this edits <b>that variable's default</b> inline and
///   persists through <c>UpdateVariableDefaultValueJson</c>. ⚠ Kept, not retired — 📌 the user's
///   <c>2026-08-17</c> <i>"no rush removals"</i>, and Batch 68 already collapsed its duplicate CODE half
///   through <c>DefaultValueAuthoring.OpenSession</c>.</item>
/// </list></para>
///
/// <para>⭐⭐⭐ <b>WHAT CHANGED IN THE MOVE — three things, stated rather than discovered later:</b>
/// <list type="number">
///   <item>⭐ <b>Selection and asset come from the CONTEXT</b>, not from <c>EditorSelectionStore</c> —
///   📌 §2: <i>"only the workspace builds a context"</i>. ⛔ This view reads no store.</item>
///   <item>⭐ <b>The services and the facet cache live on <see cref="NodePropertiesSource"/></b>, shared
///   per perspective, so the PREDICATE and the DRAW cannot disagree about whether there is anything to
///   show *(see that type's remarks)</item>
///   <item>⛔ <b>The <i>"select an asset to begin"</i> and <i>"no node selected"</i> lines did NOT come
///   across.</b> 📌 <c>R-117</c> — an empty panel is the SHELL's answer *(<c>DetailsEmptyState</c>)*, and
///   a view that claims the panel in order to apologise is that defect one level down. ⇒ this view
///   simply does not claim.</item>
/// </list></para>
///
/// <para>⚠ <b>Untouched by this batch, on purpose:</b> the retired window's asset header
/// *(Find References · Rename…)*, its parameter-synchronisation arm *(<c>S4</c>, deferred by
/// <c>R-99</c>)*, its utility stub *(<c>S3</c>)* and its collision strip. ⛔ <c>InspectorWindow</c>
/// therefore still exists; <c>S5</c> is what retires it, and 📌 <c>BP-431</c> records that two of those
/// arms have no home yet.</para>
/// </summary>
public sealed class NodePropertiesDetailsView : IDetailsViewInstance
{
    private readonly NodePropertiesSource _source;

    // The facet's StructEdit session, keyed by facet TYPE and the sub-selection it was opened for —
    // selecting a different node of the SAME type (Wait(1s) -> Wait(2s)) must rebuild against that
    // node's values rather than reuse the first node's session.
    private IEditSession?        _facetSession;
    private Type?                _facetSessionType;
    private IAssetSubSelection?  _facetSessionSub;

    // B-3: the bound variable's default-value session.
    private IEditSession? _defaultValueSession;
    private string?       _defaultValueSessionVarName;

    // ⭐ The source's generation when these sessions were opened. ⚠ A re-wired edit service opens
    //   sessions differently, and the retired window dropped its session inside SetFacetEditService;
    //   there is no such call to hook now, so the sessions compare instead (R-126's pull).
    private int _sessionGeneration = -1;

    public NodePropertiesDetailsView(NodePropertiesSource source)
        => _source = source ?? throw new ArgumentNullException(nameof(source));

    /// <summary>⭐ The live facet session, or <see langword="null"/>. ⚠ A rail surface, carried over
    /// from the retired window's <c>GetFacetSession()</c>.</summary>
    public IEditSession? FacetSession => _facetSession;

    /// <summary>⭐ The live default-value session, or <see langword="null"/> — the retired window's
    /// <c>GetDefaultValueSession()</c>.</summary>
    public IEditSession? DefaultValueSession => _defaultValueSession;

    /// <summary>
    /// ⭐⭐ <b>The variable this node WRITES, or <see langword="null"/>.</b> ⛔ No ImGui — this is
    /// <c>B-3</c>'s whole precondition as a value, so a rail can assert it without a frame.
    /// ⚠ The retired window's <c>GetCurrentExpressionTargetField()</c>, re-pointed at the context.
    /// </summary>
    public string? BoundVariableName(DetailsContext context)
    {
        if (_source.ExpressionTargetFieldAccessor is not { } accessor) return null;
        return accessor(_source.FacetFor(context));
    }

    /// <inheritdoc/>
    public void Draw(DetailsContext context, string idScope)
    {
        ArgumentNullException.ThrowIfNull(context);

        // ⚠ A re-wired service invalidates both sessions — see _sessionGeneration.
        if (_sessionGeneration != _source.Generation)
        {
            DisposeFacetSession();
            DisposeDefaultValueSession();
            _sessionGeneration = _source.Generation;
        }

        var facet = _source.FacetFor(context);
        if (facet is null)
        {
            // ⚠ Reachable only through a FLOAT/PIN whose live context moved off the node (L4.2) —
            //   the predicate keeps the docked shell off this path. ⛔ Says WHICH nothing it is.
            DisposeFacetSession();
            DisposeDefaultValueSession();
            ImGuiNET.ImGui.TextDisabled("No node is selected.");
            return;
        }

        DrawFacetArm(context, facet, idScope);
        DrawDefaultValueArm(context, idScope);
    }

    // ══ arm ① — the facet ════════════════════════════════════════════════════

    private void DrawFacetArm(DetailsContext context, object facet, string idScope)
    {
        if (_source.EditService is not { } editService)
        {
            // ⭐ The honest stub the retired window drew when no edit service is wired. ⛔ NOT a fake
            //   editor: a host with no StructEdit service must not look like one that has it.
            ImGuiNET.ImGui.Text($"[{facet.GetType().Name}]");
            if (ImGuiNET.ImGui.Button($"Apply##{idScope}_facet"))
                _source.CommitFacet(context, facet);
            return;
        }

        var facetType = facet.GetType();
        var sub       = context.Selection[0];

        // Rebuild when the facet TYPE changes OR the selected node changes (records => value equality),
        // so per-node values are shown and edited correctly.
        if (_facetSession is null
            || _facetSessionType != facetType
            || !Equals(_facetSessionSub, sub))
        {
            DisposeFacetSession();
            _facetSession     = editService.Open(facet, facetType);
            _facetSessionType = facetType;
            _facetSessionSub  = sub;
        }

        if (_facetSession.RebuildState == EditRebuildState.RebuildRequired)
            _facetSession.RebuildDocument();

        var drawers = _source.CustomDrawers ?? new Dictionary<Type, IImGuiFieldDrawer>();
        var drawer  = new ComponentEditDrawer(_facetSession, pickerCtx: null, drawers);

        // Two-column "Property | Value" table (matches ComponentEditWindow layout).
        if (ImGuiNET.ImGui.BeginTable($"##{idScope}_facet_edit", 2,
            ImGuiNET.ImGuiTableFlags.SizingStretchProp))
        {
            ImGuiNET.ImGui.TableSetupColumn("Property", ImGuiNET.ImGuiTableColumnFlags.WidthStretch, 0.4f);
            ImGuiNET.ImGui.TableSetupColumn("Value",    ImGuiNET.ImGuiTableColumnFlags.WidthStretch, 0.6f);

            drawer.DrawEditNode(_facetSession.Document.Root);

            ImGuiNET.ImGui.EndTable();
        }

        // Commit on every dirty frame so edits flow back to the asset continuously.
        if (_facetSession.IsDirty)
        {
            var committed = _facetSession.Commit();
            _source.CommitFacet(context, committed);
            // ⚠ CommitFacet bumps the generation; the next frame's check drops this session.
            DisposeFacetSession();
        }
    }

    // ══ arm ② — B-3, the bound variable's DEFAULT VALUE ══════════════════════

    private void DrawDefaultValueArm(DetailsContext context, string idScope)
    {
        if (_source.EditService is not { } editService
            || context.Asset is not IBlackboardManagedAsset bbAsset)
        {
            DisposeDefaultValueSession();
            return;
        }

        var boundVarName = BoundVariableName(context);
        if (string.IsNullOrEmpty(boundVarName))
        {
            DisposeDefaultValueSession();
            return;
        }

        BlackboardVariableEntry? varEntry = null;
        foreach (var v in bbAsset.BlackboardVariables)
        {
            if (v.Name == boundVarName) { varEntry = v; break; }
        }

        if (varEntry is null)
        {
            // Variable referenced by the facet is not in the asset's blackboard — drop any stale session.
            DisposeDefaultValueSession();
            return;
        }

        if (_defaultValueSession is null || _defaultValueSessionVarName != boundVarName)
        {
            DisposeDefaultValueSession();
            // ⭐⭐ Batch 68 (BP-267): ROUTED through DefaultValueAuthoring, not rebuilt — this used to
            //    inline its own deserialize-or-Activator copy, so a default-value session had TWO
            //    implementations. §9's rail is that exactly ONE call site opens one.
            _defaultValueSession        = DefaultValueAuthoring.OpenSession(editService, varEntry);
            _defaultValueSessionVarName = boundVarName;
        }

        if (_defaultValueSession.RebuildState == EditRebuildState.RebuildRequired)
            _defaultValueSession.RebuildDocument();

        ImGuiNET.ImGui.Separator();
        ImGuiNET.ImGui.Text($"DEFAULT VALUE — {boundVarName}");
        ImGuiNET.ImGui.TextDisabled("the variable this node writes (ExpressionTargetField)");

        var drawers = _source.CustomDrawers ?? new Dictionary<Type, IImGuiFieldDrawer>();
        var drawer  = new ComponentEditDrawer(_defaultValueSession, pickerCtx: null, drawers);

        if (ImGuiNET.ImGui.BeginTable($"##{idScope}_defval_edit", 2,
            ImGuiNET.ImGuiTableFlags.SizingStretchProp))
        {
            ImGuiNET.ImGui.TableSetupColumn("Field", ImGuiNET.ImGuiTableColumnFlags.WidthStretch, 0.4f);
            ImGuiNET.ImGui.TableSetupColumn("Value", ImGuiNET.ImGuiTableColumnFlags.WidthStretch, 0.6f);

            drawer.DrawEditNode(_defaultValueSession.Document.Root);

            ImGuiNET.ImGui.EndTable();
        }

        if (_defaultValueSession.IsDirty)
        {
            var committed = _defaultValueSession.Commit();
            string json;
            try   { json = JsonSerializer.Serialize(committed, varEntry.FieldType, DefaultValueAuthoring.JsonOptions); }
            catch { json = "{}"; }
            bbAsset.UpdateVariableDefaultValueJson(boundVarName, json);
            // Drop and rebuild next frame so the session reflects the persisted JSON.
            DisposeDefaultValueSession();
        }
    }

    // ── lifetime ─────────────────────────────────────────────────────────────

    private void DisposeFacetSession()
    {
        _facetSession?.Dispose();
        _facetSession     = null;
        _facetSessionType = null;
        _facetSessionSub  = null;
    }

    private void DisposeDefaultValueSession()
    {
        _defaultValueSession?.Dispose();
        _defaultValueSession        = null;
        _defaultValueSessionVarName = null;
    }

    /// <summary>⭐ Both sessions are per-INSTANCE state *(§1: "an uncommitted edit buffer … the view
    /// instance, legitimately")*, so this instance disposes them. ⛔ The SOURCE is borrowed and is not
    /// touched — it belongs to the perspective.</summary>
    public void Dispose()
    {
        DisposeFacetSession();
        DisposeDefaultValueSession();
    }
}

/// <summary>
/// ⭐⭐ <b><c>S2</c> — the descriptor for <see cref="NodePropertiesDetailsView"/>.</b>
/// 📄 §7.3's catalogue row.
/// </summary>
public static class NodePropertiesDetailsViewDescriptor
{
    /// <summary>
    /// ⭐⭐⭐ <b>THE SAME id Blueprint's node view uses</b> — 📄 <c>TASKS_One_Shell_BP399.md</c> §4:
    /// <i>"`details.nodeproperties` is ONE view id across BTree/HSM/Blueprint … each perspective
    /// registers its own instance, and the registries are per-perspective, so one id collides with
    /// nothing."</i>
    /// </summary>
    public const string ViewId = "details.nodeproperties";

    /// <summary>⭐⭐ <b>Rank 20</b> — above Blackboard (5) and Variables (10), which is what makes a
    /// selected node the DEFAULT. 📄 §7.3, and the user's ask verbatim.</summary>
    public const int Rank = 20;

    /// <summary>⭐ Build the descriptor. ⚠ A FRESH view per window *(<c>R-120</c>)*; the SOURCE is
    /// shared, and that is deliberate — see its remarks.</summary>
    public static DetailsViewDescriptor For(NodePropertiesSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new DetailsViewDescriptor(
            Id:        ViewId,
            Title:     "Node Properties",
            Rank:      Rank,
            // ⭐⭐ TWO halves, and both are needed:
            //   ① the generic shape (exactly one node, not working in the outline) — BP-429's rule,
            //      shared with Blueprint's node view so there is ONE definition (R-13);
            //   ② can this perspective actually MAP that selection to a facet? ⛔ Without ② the view
            //      would claim the panel for, say, a utility consideration and render nothing —
            //      R-117's blank one level down (R-116: the predicate ships with the view).
            AppliesTo: ctx => Applies(ctx) && source.CanShow(ctx),
            Create:    () => new NodePropertiesDetailsView(source));
    }

    /// <summary>⭐ The selection-shaped half, extracted so a rail can assert it without a dispatcher.
    /// ⚠ See <see cref="DetailsViewPredicates.ExactlyOneNodeNotInTheOutline{T}"/> for why the outline
    /// clause is there and why §7.3's one-line table entry does not carry it.</summary>
    public static bool Applies(DetailsContext context)
        => DetailsViewPredicates.ExactlyOneNodeNotInTheOutline<IAssetSubSelection>(context);
}
