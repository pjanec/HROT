using System;
using System.Linq;
using System.Reflection;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor.NodeDrawers;
using Hrot.Editor.AiShared.Selection;
using Hrot.Editor.AiShared.Shell;

namespace Hrot.Blueprints.Editor.Windows;

/// <summary>
/// ⭐⭐⭐ <b><c>S1</c> — BLUEPRINT'S NODE ARM, AS A DETAILS VIEW.</b>
/// 📄 <c>DESIGN_Details_Panel_View_Switching.md</c> §7.4's <c>classDiagram</c>
/// *(<c>BlueprintDetailsWindow ..&gt; BlueprintNodeDetailsView : content EXTRACTED to</c>)* · §7.3 ①
/// *("<c>BlueprintDetailsWindow</c> DISSOLVES — its arms become views")* · §7.6 ①.
///
/// <para>⛔⛔ <b><c>..&gt;</c> SAYS <i>EXTRACTED</i>, NOT <i>WRAPPED</i>.</b> 📌 §7.4, verbatim:
/// <i>"wrapping would leave both 697-line and 350-line windows standing as the implementation, which is
/// the duplication this section exists to end."</i> ⇒ ⭐ the drawer resolution, the session cache, the
/// <c>PushID</c> scope and the read-only summary <b>moved here</b> and
/// <c>BlueprintDetailsWindow</c> is <b>DELETED</b> in the same commit — ⛔ it is not a base class, a
/// delegate target or a fallback.</para>
///
/// <para>⭐⭐⭐ <b>WHAT CHANGED IN THE MOVE, stated rather than discovered later — three things:</b>
/// <list type="number">
///   <item>⭐⭐ <b>The selection comes from the CONTEXT, not the store.</b> 📌 §2: <i>"only the workspace
///   builds a context"</i>, and §6 <c>L3</c> is <i>"migrate the views"</i>. ⛔ The retired window read
///   <c>_selectionStore.ActiveSubSelection</c> every frame; ⭐ this reads
///   <see cref="DetailsContext.Selection"/>, which the descriptor's predicate has already narrowed to
///   exactly one node.</item>
///   <item>⭐⭐ <b>The asset is PULLED, not pushed.</b> 🔴 The retired window had a <c>Retarget</c> the
///   composition root had to remember to call from <c>ActiveChanged</c> — the same shape as the nine
///   silent defaults this programme has filed. ⭐ 📌 <c>R-126</c>: <i>"no path can forget to raise what
///   is never raised"</i> ⇒ the asset arrives through a <c>Func</c> read on the frame that needs it,
///   and a stale session is detected by comparing the asset rather than by being told.</item>
///   <item>⛔ <b>The <i>"which arm"</i> arbitration is GONE, and it did not move — it DISSOLVED.</b>
///   📐 <c>ShowingVariables</c> asked <i>"has the variables section content ∧ is focus not the canvas ∧
///   is the sub-selection the one I last saw"</i>. ⭐ The registry does that generically now: this view
///   claims only when a node is selected and the designer is not in the outline
///   *(<see cref="DetailsViewPredicates.ExactlyOneNodeNotInTheOutline{T}"/>)*, and <c>Rank</c> decides
///   the rest *(<c>R-98</c>)*.</item>
/// </list></para>
///
/// <para>⚠ <b>One arm did NOT come across, deliberately:</b> the retired window's
/// <c>"No node selected."</c> line. ⭐ 📌 <c>R-117</c> — an empty panel is the SHELL's answer
/// *(<c>DetailsEmptyState</c>)*, and a view that claims the panel in order to apologise is that defect
/// one level down. ⇒ this view simply does not claim when nothing is selected.</para>
/// </summary>
public sealed class BlueprintNodeDetailsView : IDetailsViewInstance
{
    private readonly Func<BlueprintAsset?>       _asset;
    private readonly BlueprintNodeDrawerRegistry _drawerRegistry;

    // Cached session — rebuilt when the selection or the asset changes.
    private INodeEditSession? _session;
    private Guid              _sessionNodeId;
    private Guid              _sessionGraphId;
    private BlueprintAsset?   _sessionAsset;

    /// <summary>⭐ <c>BP-205</c> — the node the cached session belongs to, for its ImGui id scope.</summary>
    private Node? _sessionNode;

    /// <param name="asset">
    /// ⭐⭐ <b>The active Blueprint asset, re-asked every frame.</b> ⚠ Deliberately a <c>Func</c> and not
    /// a value: the document changes under the panel, and the retired <c>Retarget</c> push is the seam
    /// this pull replaces *(see the class remarks)*.
    /// </param>
    /// <param name="drawerRegistry">⭐ Blueprint node-drawer registry — unchanged from the retired window.</param>
    public BlueprintNodeDetailsView(
        Func<BlueprintAsset?> asset,
        BlueprintNodeDrawerRegistry drawerRegistry)
    {
        _asset          = asset          ?? throw new ArgumentNullException(nameof(asset));
        _drawerRegistry = drawerRegistry ?? throw new ArgumentNullException(nameof(drawerRegistry));
    }

    /// <summary>
    /// ⭐ The kind of drawer resolved for the current selection, or <see langword="null"/> when nothing
    /// is selected, the selection is not a blueprint node, or no drawer handles its type.
    /// ⚠ A RAIL SURFACE, carried over verbatim from the retired window — 📌 <c>BF-TA-01</c> asserts on it.
    /// </summary>
    public Type? ResolvedDrawerKind { get; private set; }

    /// <summary>
    /// ⭐⭐⭐ <b>Resolve (or reuse) the drawer + session for the selected node.</b> ⛔ Separated from the
    /// draw so it is railable without ImGui — 📌 <c>R-21</c>/<c>R-62</c>: <i>"the draw is unrailed by
    /// construction"</i>, and this is where every decision lives.
    ///
    /// <para>⚠ <b>The asset is compared, not trusted.</b> 📐 The retired window cleared its session from
    /// <c>Retarget</c>; ⭐ here a document switch is simply a different <see cref="BlueprintAsset"/>
    /// instance, so the same clear happens with nothing to call.</para>
    /// </summary>
    public INodeEditSession? ResolveSession(DetailsContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var asset = _asset();
        if (asset is null) { ClearSession(); return null; }

        if (SelectedNodeRef(context) is not { } sub) { ClearSession(); return null; }

        // Same node, same asset — reuse.
        if (_session != null
            && _sessionNodeId  == sub.NodeId
            && _sessionGraphId == sub.GraphId
            && ReferenceEquals(_sessionAsset, asset))
            return _session;

        ClearSession();

        var graph = asset.Graphs.FirstOrDefault(g => g.Id == sub.GraphId);
        if (graph == null) return null;

        var node = graph.Nodes.FirstOrDefault(n => n.Id == sub.NodeId);
        if (node == null) return null;

        var drawer = _drawerRegistry.GetDrawerFor(node);
        if (drawer == null) { ResolvedDrawerKind = null; return null; }

        ResolvedDrawerKind = drawer.GetType();
        _session        = drawer.CreateSession(node, asset);
        _sessionNode    = node;
        _sessionNodeId  = sub.NodeId;
        _sessionGraphId = sub.GraphId;
        _sessionAsset   = asset;
        return _session;
    }

    /// <summary>
    /// ⭐ The selected blueprint node whether or not a drawer exists for it, or <see langword="null"/>.
    /// ⚠ Kept as its own method *(as in the retired window)* because the two <c>null</c> cases are
    /// different: <i>no node</i> vs <i>a node no drawer handles</i>, and only the second gets the
    /// read-only summary.
    /// </summary>
    public Node? TryGetSelectedNode(DetailsContext context)
    {
        var asset = _asset();
        if (asset is null) return null;
        if (SelectedNodeRef(context) is not { } sub) return null;

        var graph = asset.Graphs.FirstOrDefault(g => g.Id == sub.GraphId);
        return graph?.Nodes.FirstOrDefault(n => n.Id == sub.NodeId);
    }

    /// <summary>
    /// ⭐⭐ <b>The context's node selection, or <see langword="null"/>.</b> ⛔ <c>Count == 1</c> is
    /// re-checked here even though the predicate guarantees it — ⚠ a FLOAT window
    /// *(<c>L4.2</c>)* draws the same view against a live context that may since have widened, and
    /// 📌 <c>R-118</c>'s rule is that two nodes is not "the first one".
    /// </summary>
    private static BlueprintNodeSelection? SelectedNodeRef(DetailsContext context)
        => context.Selection is { Count: 1 } one ? one[0] as BlueprintNodeSelection : null;

    /// <inheritdoc/>
    public void Draw(DetailsContext context, string idScope)
    {
        var session = ResolveSession(context);
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
                session.Draw();
            }
            finally
            {
                ImGuiNET.ImGui.PopID();
            }
            return;
        }

        // ⭐ A node IS selected (the predicate said so) but no drawer handles its type — show a
        //   read-only summary instead of a blank. ⛔ The retired window's "No node selected." arm did
        //   NOT come across: that case is the shell's grey line now (R-117).
        var node = TryGetSelectedNode(context);
        if (node == null)
        {
            // ⚠ Reachable only in the float/pin case above (a context that widened, or an asset that
            //   closed under a frozen pin). ⭐ Says WHICH nothing it is, rather than drawing blank.
            ImGuiNET.ImGui.TextDisabled("No single node is selected.");
            return;
        }

        DrawReadOnlySummary(node);
    }

    /// <summary>
    /// Renders a read-only property summary for a selected node that has no editable drawer.
    /// Reflects the node's simple scalar properties (string/enum/bool/number), skipping the
    /// structural members (Id, Pins, EditorMetadata) that are not user-facing configuration.
    /// </summary>
    private static void DrawReadOnlySummary(Node node)
    {
        var typeName = node.GetType().Name;
        if (typeName.EndsWith("Node", StringComparison.Ordinal))
            typeName = typeName[..^4];
        ImGuiNET.ImGui.TextUnformatted(typeName);
        ImGuiNET.ImGui.Separator();

        foreach (var p in node.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (p.GetIndexParameters().Length != 0) continue;
            if (p.Name is "Id" or "Pins" or "EditorMetadata") continue;

            var t = p.PropertyType;
            var under = Nullable.GetUnderlyingType(t) ?? t;
            bool simple = under == typeof(string) || under.IsEnum || under == typeof(bool)
                          || under == typeof(int) || under == typeof(long) || under == typeof(short)
                          || under == typeof(float) || under == typeof(double) || under == typeof(Guid);
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

    private void ClearSession()
    {
        _session?.Dispose();
        _session        = null;
        _sessionNode    = null;
        _sessionAsset   = null;
        _sessionNodeId  = Guid.Empty;
        _sessionGraphId = Guid.Empty;
        ResolvedDrawerKind = null;
    }

    /// <summary>⭐ Unlike <c>VariablesDetailsView</c>, this instance OWNS its session — ⇒ it disposes it.
    /// ⚠ 📌 <c>R-120</c>: the session is per-instance state, never shared, which is exactly why the
    /// descriptor's factory builds a fresh view per window.</summary>
    public void Dispose() => ClearSession();
}

/// <summary>
/// ⭐⭐ <b><c>S1</c> — the descriptor for <see cref="BlueprintNodeDetailsView"/>.</b>
/// 📄 <c>DESIGN_Details_Panel_View_Switching.md</c> §7.3's catalogue row.
/// </summary>
public static class BlueprintNodeDetailsViewDescriptor
{
    /// <summary>
    /// ⭐⭐⭐ <b>ONE view id across BTree, HSM and Blueprint</b> — 📄 <c>TASKS_One_Shell_BP399.md</c> §4.
    /// ⚠ Each perspective registers its OWN instance, and the registries are per-perspective, so one id
    /// collides with nothing *(unlike <c>details.runtime.&lt;kind&gt;</c>, where one registry holds
    /// several)*. ⭐ <c>S2</c> registers <c>NodePropertiesDetailsView</c> under this same id on BTree
    /// and HSM.
    /// </summary>
    public const string ViewId = "details.nodeproperties";

    /// <summary>
    /// ⭐⭐⭐ <b>Rank 20 — above Blackboard (5) and Variables (10).</b> 📄 §7.3: <i>"with a node selected,
    /// node properties OUTRANKS Blackboard and Variables ⇒ it becomes the DEFAULT"</i>, which is the
    /// user's ask verbatim. ⚠ <c>details.runtime.*</c> stays at <b>50</b>, so a LIVE session still
    /// outranks this — 📄 §7.3 flags that as the user's call, not a build decision.
    /// </summary>
    public const int Rank = 20;

    /// <summary>⭐ Build the descriptor. ⚠ A FRESH view per window *(<c>R-120</c>)* — see
    /// <see cref="BlueprintNodeDetailsView.Dispose"/>.</summary>
    public static Hrot.Editor.AiShared.Shell.DetailsViewDescriptor For(
        Func<BlueprintAsset?> asset,
        BlueprintNodeDrawerRegistry drawerRegistry)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(drawerRegistry);

        return new Hrot.Editor.AiShared.Shell.DetailsViewDescriptor(
            Id:        ViewId,
            Title:     "Node Properties",
            Rank:      Rank,
            AppliesTo: Applies,
            Create:    () => new BlueprintNodeDetailsView(asset, drawerRegistry));
    }

    /// <summary>
    /// ⭐ Extracted so a rail can assert the predicate directly, without a drawer registry.
    /// ⚠ The <i>"not in the outline"</i> half is <b>not</b> in §7.3's one-line table entry — see
    /// <see cref="DetailsViewPredicates.ExactlyOneNodeNotInTheOutline{T}"/> for the measurement that
    /// put it there, and §7.3's <c>S1</c> note.
    /// </summary>
    public static bool Applies(DetailsContext context)
        => DetailsViewPredicates.ExactlyOneNodeNotInTheOutline<BlueprintNodeSelection>(context);
}
