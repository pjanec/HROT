using System;
using System.Collections.Generic;
using Fdp.Presentation.Editing;
using Hrot.Editor.AiShared.Inspector;
using Hrot.Editor.AiShared.Selection;
using StructEdit.Core;

namespace Hrot.Editor.AiShared.Shell;

/// <summary>
/// ⭐⭐⭐ <b><c>S2</c> — THE PER-PERSPECTIVE SERVICES THE NODE-PROPERTIES VIEW NEEDS, AND THE ONE FACET
/// CACHE.</b>
/// 📄 <c>DESIGN_Details_Panel_View_Switching.md</c> §7.3's catalogue · §7.6 ② · <c>TASKS_One_Shell_BP399.md</c> §6.
///
/// <para>⭐⭐ <b>Why the services are NOT on the view.</b> 📌 <c>R-120</c>: a view instance is per-WINDOW
/// *(docked, float, pin)*, but the facet dispatcher and the StructEdit edit service are per-PERSPECTIVE
/// and are re-wired at runtime by the composition root when the document changes. ⇒ ⭐ they live here,
/// once, and every instance reads them. ⛔ Putting them on the instance would mean re-wiring N windows.</para>
///
/// <para>⭐⭐⭐ <b>And the FACET CACHE lives here for a sharper reason: the PREDICATE and the DRAW must
/// agree.</b> 📐 <see cref="NodePropertiesDetailsViewDescriptor"/>'s predicate asks <i>"can I show
/// anything for this selection?"</i>, which means asking the dispatcher — and the draw then asks the
/// same question. ⛔ Two caches would be two answers *(ruling 9)*, and the failure mode is a view that
/// claims the panel and then renders nothing *(<c>R-117</c> one level down)</para>
///
/// <para>⚠ <b>What is deliberately NOT cached here:</b> the StructEdit <b>sessions</b>. Those hold
/// UNCOMMITTED EDITS — §1: <i>"an uncommitted edit buffer … the view instance, legitimately"</i> — so
/// they stay per-instance. ⚠ <b>Stated consequence of <c>L4</c>:</b> a docked panel and a float showing
/// this view therefore hold two sessions over one facet, and the last dirty frame wins. ⭐ Same class of
/// limit <c>VariablesDetailsView</c> already records; ⛔ not introduced by this batch, and not solved by
/// it either.</para>
///
/// <para>⚠⚠ <b>Every setter BUMPS <see cref="Generation"/>.</b> 📐 The retired
/// <c>InspectorWindow.SetFacetDispatcher</c>/<c>SetFacetEditService</c> each cleared the cached facet
/// AND disposed the cached session, because a new dispatcher answers differently and a new edit service
/// opens differently. ⛔ A view instance cannot be reached from here *(there may be several)*, so the
/// generation counter is how they learn — 📌 <c>R-126</c>'s pull: nothing has to remember to notify.</para>
/// </summary>
public sealed class NodePropertiesSource
{
    private object?              _facet;
    private IAssetSubSelection?  _facetSelection;
    private int                  _facetGeneration = -1;

    /// <summary>⭐ Bumped whenever a wired service changes. ⚠ A view instance compares this and drops
    /// its session — ⛔ there is no notification, by design.</summary>
    public int Generation { get; private set; }

    /// <summary>⭐ Per-perspective facet dispatcher, injected from the composition root.</summary>
    public IFacetDispatcher? Dispatcher { get; private set; }

    /// <summary>⭐ StructEdit edit service. ⛔ <see langword="null"/> ⇒ the view draws the honest stub
    /// arm the retired window drew, not a fake editor.</summary>
    public IComponentEditService? EditService { get; private set; }

    /// <summary>⭐ CLR type → field drawer, carrying the HSM and BTree picker drawers.</summary>
    public IReadOnlyDictionary<Type, IImGuiFieldDrawer>? CustomDrawers { get; private set; }

    /// <summary>⭐ <c>B-3</c> — reads <c>ExpressionTargetField</c> off a boxed facet. ⛔ Injected,
    /// because this assembly must not learn what a BTree action is.</summary>
    public Func<object?, string?>? ExpressionTargetFieldAccessor { get; private set; }

    /// <summary>⭐ Rail surfaces, asked of the CONSTRUCTED object — 📌 <c>R-67</c> and the
    /// <c>2026-08-16</c> control.</summary>
    public bool HasDispatcher                     => Dispatcher is not null;
    /// <inheritdoc cref="HasDispatcher"/>
    public bool HasFacetEditService               => EditService is not null;
    /// <inheritdoc cref="HasDispatcher"/>
    public bool HasExpressionTargetFieldAccessor  => ExpressionTargetFieldAccessor is not null;

    /// <summary>⭐ Wires (or replaces) the facet dispatcher at runtime — the composition root calls this
    /// when the active document changes.</summary>
    public void SetFacetDispatcher(IFacetDispatcher? dispatcher)
    {
        Dispatcher = dispatcher;
        Invalidate();
    }

    /// <summary>⭐ Wires (or replaces) the StructEdit edit service and its custom drawers.</summary>
    public void SetFacetEditService(
        IComponentEditService? editService,
        IReadOnlyDictionary<Type, IImGuiFieldDrawer>? customDrawers = null)
    {
        EditService   = editService;
        CustomDrawers = customDrawers;
        Invalidate();
    }

    /// <summary>⭐ <c>B-3</c>'s accessor. ⚠ Separate from the two above because the composition root
    /// supplies it once, at construction, rather than per document.</summary>
    public void SetExpressionTargetFieldAccessor(Func<object?, string?>? accessor)
    {
        ExpressionTargetFieldAccessor = accessor;
        Invalidate();
    }

    private void Invalidate()
    {
        _facet          = null;
        _facetSelection = null;
        Generation++;
    }

    /// <summary>
    /// ⭐⭐ <b>The boxed facet for this context's single sub-selection, or <see langword="null"/>.</b>
    /// ⛔ No ImGui — this is the projection both the predicate and the draw read.
    ///
    /// <para>⚠ <b>The cache key is REFERENCE identity of the sub-selection</b>, exactly as the retired
    /// <c>InspectorWindow.GetCurrentFacet</c> keyed it — 📌 <c>EditorSelectionStore</c> guarantees stable
    /// instances, which is what makes <c>R-115</c>'s <i>"a pan yields the same context"</i> hold too.
    /// ⚠ The generation is compared as well, so re-wiring a service re-reads.</para>
    /// </summary>
    public object? FacetFor(DetailsContext context)
    {
        if (Dispatcher is null) return null;
        if (context.Selection is not { Count: 1 }) return null;

        var sub = context.Selection[0];
        if (!ReferenceEquals(sub, _facetSelection) || _facetGeneration != Generation)
        {
            _facet           = Dispatcher.GetFacet(sub);
            _facetSelection  = sub;
            _facetGeneration = Generation;
        }
        return _facet;
    }

    /// <summary>
    /// ⭐⭐⭐ <b>Commits an edited facet back to the asset, and drops the cache.</b>
    /// ⛔ The view calls this; it never touches the dispatcher itself.
    /// </summary>
    public void CommitFacet(DetailsContext context, object editedFacet)
    {
        if (Dispatcher is null) return;
        if (context.Selection is not { Count: 1 }) return;

        Dispatcher.ApplyFacet(context.Selection[0], editedFacet);

        // ⭐ Invalidate so the next read comes from the freshly committed asset. ⚠ Generation++ is what
        //   makes the view drop its session too — the retired window did both in one method.
        Invalidate();
    }

    /// <summary>
    /// ⭐ <b>Can this perspective show node properties for <paramref name="context"/>?</b>
    /// ⚠ 📌 <c>R-116</c> — the predicate ships with the view, and this is the half only the source can
    /// answer. ⛔ Without it the view would claim the panel for a selection the dispatcher cannot map
    /// and then render nothing, which is <c>R-117</c>'s blank one level down.
    /// </summary>
    public bool CanShow(DetailsContext context) => FacetFor(context) is not null;
}
