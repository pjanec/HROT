using System;
using Fdp.Presentation.WindowManager;
using Hrot.Editor.AiShared.Variables;

namespace Hrot.Editor.AiShared.Windows;

/// <summary>
/// ⭐⭐⭐ <b><c>88b</c> / <c>BP-317</c> — the Details panel, for BTree and HSM.</b>
///
/// <para>📄 <b>Design basis, verbatim</b> — <c>Q32</c> ruling 6: <i>"The same Details panel is REUSED
/// for every asset type — HSM, BTree, Blueprint ⇒ this is a cross-host deliverable, not a blueprint
/// one."</i> 📐 <b>Measured</b> *(gate 8, <c>search_graph</c>)*: exactly ONE production window was
/// titled <c>"Details"</c> — <c>BlueprintDetailsWindow</c>, registered on Blueprint only — and exactly
/// ONE type hosted a <see cref="VariableDetailsSection"/>: that same window. ⇒ ⛔ <b>the AI
/// perspectives had no Details panel at all</b>, which is what <c>R-60</c> recorded and what
/// <c>R-62</c> cites for keeping visual checks suspended on those two hosts.</para>
///
/// <para>⭐⭐ <b>ROUTING AND PLACEMENT, not construction.</b> Everything this window shows already
/// shipped: <see cref="VariableDetailsSection"/> is in <c>AiShared</c> and deliberately window-less
/// *("the host draws it; it does not own a window… that is what lets one Details panel per perspective
/// host the same list")*, and <c>BlackboardSectionRowSource</c> already resolves a section to rows.
/// ⛔ <b>No table, no formatter and no row source is built here.</b></para>
///
/// <para>⭐⭐⭐ <b>Why this is NOT <c>BlueprintDetailsWindow</c> generalised (ruling 9 is satisfied, not
/// broken).</b> 📐 That window's OTHER arm is a blueprint node inspector — <c>BlueprintAsset</c>,
/// <c>BlueprintNodeDrawerRegistry</c>, <c>BlueprintNodeSelection</c>, a cached
/// <c>INodeEditSession</c> per node — and it is <c>sealed</c>. ⚠ Unsealing it would drag
/// <c>Hrot.Blueprints.Editor</c> into the AI perspectives to reuse the ONE part that is already
/// shared. ⇒ ⭐ <b>the shared thing is the SECTION, and both windows host the same one.</b></para>
///
/// <para>⚠ <b>One arm, deliberately — and therefore no focus arbitration.</b> Blueprint's Details
/// arbitrates between a variables arm and a node arm through
/// <c>SelectionOrigin</c>/<c>FocusedSurface</c> *(Batch 87)*. ⛔ This window has no node arm: the AI
/// perspectives' node/parameter surface is <c>InspectorWindow</c>, which <b>stays</b> *(<c>BP-295</c>;
/// <c>R-86</c>'s retirement ruling is not in this batch's scope)*. ⇒ ⭐ there is nothing to arbitrate,
/// so this window <b>does not implement <c>IDetailsSurfaceClaimant</c></b> — 📌 the registrar's own
/// rule: <i>"the Watch, the Inspector and Details itself must not claim, or a window that does not
/// drive the panel would steal it."</i></para>
///
/// <para>⭐ <b>The Value column comes free.</b> BTree and HSM already have live-value providers
/// *(<c>EditorSubsystem:2178</c>/<c>:2190</c>)* and the run-state source is installed by the registrar
/// through <see cref="IVariableDetailsHost.SetRunStateSource"/>, not by a composition-root line
/// somebody must remember.</para>
/// </summary>
public sealed class AiDetailsWindow : ManagedWindow, IVariableDetailsHost, Variables.IVariableTableHost
{
    private readonly VariableDetailsSection _variables;
    private readonly string                 _drawId;

    /// <param name="id">Unique ImGui window id (e.g. <c>"ai_details_btree"</c>).</param>
    /// <param name="owningPerspective">Perspective key — <c>"BTree"</c> or <c>"HSM"</c>.</param>
    /// <param name="formatter">
    /// ⭐ <b>The one value formatter</b>, shared with the standalone table and the Watch. ⛔ Building a
    /// second one here would be a second place to fix a rendering rule — 📌 <c>C8</c>/<c>BP-01</c>.
    /// </param>
    /// <param name="columns">
    /// Defaults to <see cref="VariableTableColumns.Details"/>, the same set Blueprint's Details uses.
    /// </param>
    public AiDetailsWindow(
        string id,
        string owningPerspective,
        VariableValueFormatter formatter,
        VariableTableColumns? columns = null)
        : base(id, "Details", owningPerspective, WindowScope.PerspectiveBound)
    {
        if (formatter is null) throw new ArgumentNullException(nameof(formatter));

        _variables = new VariableDetailsSection(formatter, columns);
        _drawId    = $"{id}_variables";
        IsOpen     = false;
    }

    /// <summary>
    /// ⭐ The hosted list. Exposed so a rail can assert on the CONSTRUCTED object rather than on
    /// whatever wired it — 📌 <c>R-67</c>.
    /// </summary>
    public VariableDetailsSection Variables => _variables;

    /// <summary>⭐ What the panel is currently showing, or null. A rail surface.</summary>
    public string? Heading => _variables.Heading;

    /// <summary>⭐ True when a list is shown; false means the empty arm draws instead.</summary>
    public bool ShowingVariables => _variables.HasContent;

    /// <inheritdoc/>
    /// <remarks>
    /// ⭐⭐ Forwarded from the hosted section, so the registrar's ONE attach loop reaches this table
    /// without knowing that a Details WINDOW hosts a Details SECTION — the same forwarding
    /// <c>BlueprintDetailsWindow</c> does. ⛔ Not a second table.
    /// </remarks>
    VariableTableControl? Variables.IVariableTableHost.VariableTable
        => ((Variables.IVariableTableHost)_variables).VariableTable;

    /// <inheritdoc/>
    /// <remarks>⭐ Row 58 — forwarded to the hosted list, which is what renders the Value column.</remarks>
    public void SetRunStateSource(Func<VariableRunState> runState)
        => _variables.SetRunStateSource(runState);

    /// <inheritdoc/>
    /// <remarks>
    /// ⭐⭐ 📌 <c>Q32</c> ruling 2 — <i>"selection routes"</i>. ⛔ A selection with no rows CLEARS the
    /// list rather than leaving a stale one beside an unrelated selection, exactly as on Blueprint.
    /// </remarks>
    public void ShowVariables(VariableOutlineSelection selection)
    {
        if (selection.HasRows) _variables.Show(selection);
        else                   _variables.Clear();
    }

    protected override void DrawClientArea()
    {
        if (_variables.HasContent)
        {
            _variables.Draw(_drawId);
            return;
        }

        // ⚠ An explicit empty state, ⛔ not a blank frame — 📌 the same rule the outline follows:
        //   "a section that appears and disappears reads as a broken feature."
        ImGuiNET.ImGui.TextDisabled("No variable selected.");
    }
}
