using System;
using Fdp.Core;
using Fdp.Diagnostics.Contracts.Panels;
using Fdp.Toolkit.Blueprints;
using Hrot.Editor.AiShared.Shell;

namespace Hrot.Blueprints.Editor.EntityBlueprints;

/// <summary>
/// ⭐⭐⭐ <b><c>CE-302</c> — the Entity Blueprints panel becomes a DETAILS VIEW, so it follows the
/// unified selection when docked and the PINNED entity when pinned.</b>
/// 🔒 User, <c>2026-09-21</c>: <i>"`EntityBlueprintsManagedWindow` … sound[s] like [it] needs
/// converting into [a] proper details panel view[] with all the pinning support."</i>
/// 📄 <c>DESIGN_Editor_Entity_Selection_Source.md</c> §5 · <c>DESIGN_Details_Panel_View_Switching.md</c>
/// §2 (<c>L1.1</c>) and §L4 (float/pin).
///
/// <para>⭐⭐⭐ <b>THE WHOLE POINT IS WHERE THE ENTITY COMES FROM.</b> 🔴 As a standalone window this
/// panel was built with <c>entityResolver: () =&gt; _aiEditorSelectionStore?.SelectedEntity</c> — a
/// GLOBAL read. ⇒ it could only ever show <i>"whoever is selected"</i>, and a pinned copy was
/// impossible to express. ⭐ As a view it is handed a <see cref="DetailsContext"/> per draw, and
/// <c>context.Entities</c> is <b>LIVE when docked and FROZEN when pinned</b> *(<c>R-100</c>'s
/// snapshot)* — so pinning costs this class nothing and is not opted into anywhere.</para>
///
/// <para>⚠ <b>The panel still takes a <c>Func&lt;Entity?&gt;</c>, and that is deliberate.</b> ⛔ The
/// alternative — giving <c>EntityBlueprintsPanel</c> a <c>DetailsContext</c> parameter — would drag the
/// shell's context type into a panel that has no other use for it, and would break the standalone
/// construction its own rails use. ⭐ The view owns a cell, points it at the context each draw, and the
/// panel keeps reading a delegate exactly as before *(the same shape <c>ScenarioMissionView</c> uses to
/// point <c>MissionPanel</c> at <c>ctx.Entities[0]</c>)</para>
///
/// <para>⛔⛔ <b>One instance per WINDOW, never shared</b> — 📌 <c>R-120</c>: the registry hands out
/// factories, so two windows showing this view build two panels and there is nothing to arbitrate.
/// ⚠ That is what makes <i>"docked AND pinned at the same time, on two entities"</i> work at all.</para>
/// </summary>
public sealed class EntityBlueprintsDetailsView : IDetailsViewInstance
{
    private readonly EntityBlueprintsPanel _panel;

    /// <summary>
    /// ⭐ The entity THIS instance is drawing about, written from the context on every draw.
    /// ⛔ Not a latch anyone else can see: it is per-instance, so a pinned view and a docked view hold
    /// different values at the same time, which is the feature.
    /// </summary>
    private Entity? _entity;

    /// <param name="world">⚠ The panel needs a world to read the entity's blueprints; the view holds none itself.</param>
    /// <param name="registry">⭐ The blueprint registry the panel resolves names through.</param>
    public EntityBlueprintsDetailsView(EntityRepository world, BlueprintRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(registry);

        var model = new EntityBlueprintsEditModel(world, registry, Entity.Null);
        _panel = new EntityBlueprintsPanel(model, world, registry, entityResolver: () => _entity);
    }


    /// <summary>⭐ Exposed so a rail can assert WHICH entity this instance was pointed at — 📌 the
    /// <c>R-78</c> hazard: <i>"the right view drawn about the wrong entity"</i> is invisible on screen
    /// whenever the two selections happen to coincide, which in this editor they usually do.</summary>
    internal Entity? CurrentEntity => _entity;

    /// <inheritdoc/>
    public void Draw(DetailsContext context, string idScope)
    {
        ArgumentNullException.ThrowIfNull(context);

        PointAt(context);

        var panelId = $"{idScope}/{EntityBlueprintsDetailsViewDescriptor.ViewId}";
        PanelSnapshot.DeclareInstrumented(panelId);

        _panel.DrawUI();
    }

    /// <summary>
    /// ⭐⭐⭐ <b>WHERE THE ENTITY IS DECIDED — extracted so a rail can drive it without ImGui.</b>
    /// 📌 The same shape as <c>RuntimeDetailsView.SimulateDraw</c>: ⛔ <c>Draw</c> ends in a real panel
    /// render, so a rail calling it headlessly would assert nothing about the decision this method IS.
    ///
    /// <para>⭐⭐ THE CONTEXT DECIDES, not a global. Docked ⇒ this is the unified selection; PINNED ⇒ it
    /// is the entity captured at pin time, because the frozen source hands back a snapshot.</para>
    ///
    /// <para>⚠ The predicate already guarantees exactly one, but this does not ASSUME it: a view must
    /// not throw from <c>Draw</c>, and <c>null</c> is a state the panel already renders honestly.
    /// ⛔ An empty context clears the cell rather than keeping the last entity — a view that remembered
    /// would go on showing a selection that no longer exists.</para>
    /// </summary>
    internal void PointAt(DetailsContext context)
        => _entity = context.Entities is { Count: > 0 } ? context.Entities[0] : null;

    /// <summary>⚠ The panel owns no unmanaged resources and no subscriptions — nothing to release.
    /// ⛔ Kept because <see cref="IDetailsViewInstance"/> requires it, and an empty body that SAYS it is
    /// empty is better than one a reader has to verify.</summary>
    public void Dispose() { }
}

/// <summary>
/// ⭐⭐ <b><c>CE-302</c> — the descriptor for <see cref="EntityBlueprintsDetailsView"/>.</b>
/// </summary>
public static class EntityBlueprintsDetailsViewDescriptor
{
    /// <summary>⭐ Stable identity — the layout key and the designer's remembered pick.</summary>
    public const string ViewId = "details.entityblueprints";

    /// <summary>
    /// ⭐ <b>Rank 15 — above Variables (10), below Node Properties (20).</b>
    /// ⚠ Reasoned, not guessed: with a node selected the node's own properties are what the designer
    /// asked for *(§7.3)*, ⛔ but this view is ENTITY-scoped and a bare entity selection offers no node
    /// arm at all, so 15 makes it the default exactly when nothing more specific applies.
    /// ⭐ <c>details.runtime.*</c> stays at 50, so a live session still outranks it.
    /// </summary>
    public const int Rank = 15;

    /// <summary>
    /// ⭐ Extracted so a rail can assert the predicate directly, with no world and no registry.
    /// ⚠ <b>Exactly one</b> — 📌 two entities selected would mean silently showing the first, which is
    /// the collapse <c>R-118</c> deletes; no offer is <c>R-117</c>'s honest grey line.
    /// </summary>
    public static bool Applies(DetailsContext context)
        => DetailsViewPredicates.ExactlyOneEntity(context);

    /// <summary>
    /// ⭐ Build the descriptor. ⚠ A FRESH view per window *(<c>R-120</c>)*.
    ///
    /// <para>⛔⛔ <b>The world and the registry arrive as DELEGATES, and that is a construction-order
    /// fact, not a style choice.</b> 🔴 Measured: the first draft took them by value and
    /// <c>TheBlueprintCatalogue_OffersTheEntityBlueprintsView</c> threw
    /// <c>ArgumentNullException(world)</c> — <c>EditorSubsystem.RegisterWindows</c> runs BEFORE
    /// <c>Initialize</c> assigns <c>_world</c>. ⭐ The retired window hid this inside a lazy factory
    /// lambda, so the eager form looked equivalent and was not. 📌 The same construction-order shape as
    /// <c>L0.4</c>'s world and <c>L3.3</c>'s first wiring, and the reason every other descriptor here
    /// takes <c>Func&lt;…&gt;</c> *(<c>R-126</c>'s pull)</para>
    ///
    /// <para>⚠ <b>Resolved at <c>Create</c>, which cannot be reached before a world exists</b>: the
    /// predicate needs an entity in the context, and the context's entities are read FROM the world.
    /// ⛔ The throw below is therefore a wiring assertion, not a runtime path.</para>
    /// </summary>
    public static DetailsViewDescriptor For(
        Func<EntityRepository?> world,
        Func<BlueprintRegistry?> registry)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(registry);

        return new DetailsViewDescriptor(
            Id:        ViewId,
            Title:     "Entity Blueprints",
            Rank:      Rank,
            AppliesTo: Applies,
            Create:    () => new EntityBlueprintsDetailsView(
                world()    ?? throw new InvalidOperationException(
                    "The Entity Blueprints view was created before the editor had a World. Its "
                  + "predicate requires an entity in the DetailsContext, and those are read from the "
                  + "World, so this means the context was built from something else."),
                registry() ?? throw new InvalidOperationException(
                    "The Entity Blueprints view was created before the editor had a BlueprintRegistry.")));
    }
}
