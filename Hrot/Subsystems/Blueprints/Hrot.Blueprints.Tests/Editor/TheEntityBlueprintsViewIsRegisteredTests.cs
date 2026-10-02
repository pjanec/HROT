using System;
using System.Linq;
using Fdp.Core;
using Fdp.Presentation.Icons;
using Fdp.Presentation.WindowManager;
using Hrot.Blueprints.Editor.EntityBlueprints;
using Hrot.Editor;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Shell;
using Xunit;

namespace Hrot.Blueprints.Tests.Editor;

/// <summary>
/// ⭐⭐⭐ <b><c>CE-302</c> — the Entity Blueprints view is REACHABLE, and it draws about the entity its
/// CONTEXT names.</b>
/// 📄 <c>DESIGN_Editor_Entity_Selection_Source.md</c> §5.
///
/// <para>⛔⛔ <b>Why a PRODUCTION-ROOT rail and not only unit rails.</b> 📌 <c>BP-475</c>, verbatim:
/// the <c>HsmEventsWindow</c> view <i>"landed BUILT AND UNREACHABLE and was nearly shipped that
/// way"</i> — <b>every one of its unit rails passed on a view nobody registered.</b> ⇒ ⭐ only an
/// assertion on the CONSTRUCTED editor can see that defect, and this file mirrors
/// <c>TheHsmEventsViewIsRegisteredTests</c> deliberately.</para>
/// </summary>
public sealed class TheEntityBlueprintsViewIsRegisteredTests
{
    private static EditorSubsystem RealEditor()
    {
        var editor = new EditorSubsystem();
        editor.RegisterWindows(new WindowManager(new IconAtlas(IntPtr.Zero, 16f, 16f)));
        return editor;
    }

    /// <summary>⭐ The catalogue the designer actually reaches offers it.</summary>
    [Fact]
    public void TheBlueprintCatalogue_OffersTheEntityBlueprintsView()
    {
        var registrar = RealEditor().RegistrarFor("Blueprint");
        Assert.NotNull(registrar);
        Assert.Contains(EntityBlueprintsDetailsViewDescriptor.ViewId,
                        registrar!.DetailsViews.All.Select(d => d.Id));
    }

    /// <summary>
    /// ⭐⭐ <b>ENTITY-scoped, so it applies on exactly one entity and on nothing else.</b>
    /// ⚠ The negative halves are not decoration: ⛔ an offer with TWO entities selected would mean
    /// silently drawing the first *(the collapse <c>R-118</c> deletes)*, and an offer with NONE would
    /// claim the panel in order to apologise *(<c>R-117</c>'s blank-shaped defect)</summary>
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    public void ItApplies_OnExactlyOneEntity(int count, bool expected)
    {
        var entities = Enumerable.Range(1, count).Select(i => new Entity(i, 1)).ToArray();
        Assert.Equal(expected, EntityBlueprintsDetailsViewDescriptor.Applies(ContextFor(entities)));
    }

    /// <summary>
    /// ⭐⭐⭐ <b>THE PINNING PROPERTY, asserted where it is decided.</b> The view draws about
    /// <c>context.Entities[0]</c> — ⛔ NOT about a global. ⇒ a docked view follows the unified selection
    /// and a PINNED one keeps the entity its frozen snapshot holds, with no flag and no opt-in.
    ///
    /// <para>⚠ <b>Two DIFFERENT contexts through ONE instance</b>, which is precisely the pinned/docked
    /// difference in miniature. ⛔ A rail that fed one context could not tell "reads the context" from
    /// "read a global that happened to match".</para>
    ///
    /// <para>⭐ <b>Red-proof:</b> point the view at a store instead of the context and the second
    /// assertion reddens.</para>
    /// </summary>
    [Fact]
    public void ItDrawsAboutTheEntityItsContextNames_NotAGlobal()
    {
        var world    = new EntityRepository();
        var registry = new Fdp.Toolkit.Blueprints.BlueprintRegistry();
        using var view = new EntityBlueprintsDetailsView(world, registry);


        var first  = new Entity(11, 1);
        var second = new Entity(22, 1);

        view.PointAt(ContextFor(first));
        Assert.Equal(first, view.CurrentEntity);

        view.PointAt(ContextFor(second));
        Assert.Equal(second, view.CurrentEntity);

        // ⚠ An EMPTY context resolves to null rather than keeping the last entity — ⛔ a view that
        //   remembered would go on showing a selection that no longer exists, which is the
        //   "accepted and silently discarded" shape UXI-11 kept finding.
        view.PointAt(ContextFor());
        Assert.Null(view.CurrentEntity);
    }

    /// <summary>
    /// ⭐⭐⭐ <b>THE CAPABILITY THAT REPLACED THE STANDALONE WINDOW, end to end.</b>
    /// 🔒 User, <c>2026-09-21</c>: <i>"no standalone window if this one is related to one single entity
    /// … the standalone window would still be possible, pinned to concrete entity in the same way as
    /// the details panel supports now."</i>
    ///
    /// <para>⭐⭐ <b>A pinned view is a <see cref="FrozenContextSource"/> over the context AT PIN TIME</b>
    /// *(<c>R-100</c>)*, so the selection moving afterwards must not move it. ⛔ This is what makes
    /// deleting <c>EntityBlueprintsManagedWindow</c> a REPLACEMENT rather than a loss — and it is
    /// asserted rather than assumed, because the window is gone either way.</para>
    ///
    /// <para>⚠ <b>The DOCKED half is in the same rail deliberately</b>: a frozen source that never
    /// changed would pass the pinned assertion while breaking the ordinary case. ⛔ One without the
    /// other proves half the claim.</para>
    /// </summary>
    [Fact]
    public void APinnedViewKeepsItsEntity_WhileADockedOneFollows()
    {
        var pinnedTo = new Entity(7, 1);
        var moved     = new Entity(8, 1);

        var world    = new EntityRepository();
        var registry = new Fdp.Toolkit.Blueprints.BlueprintRegistry();

        // ⭐ PINNED: the context is snapshotted once and handed over frozen.
        var frozen = new FrozenContextSource(ContextFor(pinnedTo));
        using var pinned = new EntityBlueprintsDetailsView(world, registry);

        // ⭐ DOCKED: re-reads a live context every draw.
        using var docked = new EntityBlueprintsDetailsView(world, registry);

        pinned.PointAt(frozen.Current());
        docked.PointAt(ContextFor(pinnedTo));
        Assert.Equal(pinnedTo, pinned.CurrentEntity);
        Assert.Equal(pinnedTo, docked.CurrentEntity);

        // ── the selection moves ──────────────────────────────────────────────
        pinned.PointAt(frozen.Current());          // ⭐ still the snapshot
        docked.PointAt(ContextFor(moved));         // ⭐ follows

        Assert.Equal(pinnedTo, pinned.CurrentEntity);
        Assert.Equal(moved,    docked.CurrentEntity);
        Assert.NotEqual(pinned.CurrentEntity, docked.CurrentEntity);
    }

    private static DetailsContext ContextFor(params Entity[] entities)
        => DetailsContextBuilder.Build(
            store:       new Hrot.Editor.AiShared.Selection.EditorSelectionStore(),
            perspective: "Blueprint",
            mode:        Hrot.Editor.AiShared.Variables.VariableRunState.Planning,
            entities:    new FixedEntities(entities));

    /// <summary>⭐ The contract's same-instance rule holds trivially — one array, returned as is.</summary>
    private sealed class FixedEntities : IEntitySelectionSource
    {
        private readonly Entity[] _entities;
        public FixedEntities(Entity[] entities) => _entities = entities;
        public System.Collections.Generic.IReadOnlyList<Entity> Selected() => _entities;
    }
}

/// <summary>
/// ⭐⭐⭐ <b><c>CE-303</c> — the Blueprint RUNTIME pane takes its entity from the CONTEXT, so a pinned
/// copy of <c>details.runtime.Blueprint</c> shows the entity it was pinned to.</b>
/// 📄 <c>DESIGN_Editor_Entity_Selection_Source.md</c> §11.
///
/// <para>⚠⚠ <b>This pane was ALREADY a details view</b> — <c>RegisterPane</c> added a
/// <c>RuntimeDetailsViewDescriptor</c> — ⛔ but <c>RuntimeDetailsView.Draw</c> DISCARDED its context and
/// the pane read <c>EditorSelectionStore.SelectedEntity</c>, a GLOBAL. ⇒ a pinned copy rendered
/// whatever was selected NOW. 🔒 The user ruled the standalone <c>RuntimeInspectorWindow</c> should
/// <i>"dissolve"</i>, which is what made a context-carrying <c>Draw</c> possible at all.</para>
/// </summary>
public sealed class TheRuntimePaneReadsItsContextTests
{
    /// <summary>
    /// ⭐ Two DIFFERENT contexts, one pane — the docked/pinned difference in miniature. ⛔ A rail that
    /// fed one context could not tell "reads the context" from "read a global that happened to match".
    /// ⭐ <b>Red-proof:</b> restore the <c>selectedEntityResolver</c> and this stops varying.
    /// </summary>
    [Fact]
    public void TheEntityComesFromTheContext_NotAGlobal()
    {
        var first  = new Fdp.Core.Entity(41, 1);
        var second = new Fdp.Core.Entity(42, 1);

        Assert.Equal(first,
            Hrot.Blueprints.Editor.Inspector.BlueprintRuntimeInspectorPane.ResolveEntity(Ctx(first)));
        Assert.Equal(second,
            Hrot.Blueprints.Editor.Inspector.BlueprintRuntimeInspectorPane.ResolveEntity(Ctx(second)));

        // ⚠ No entity ⇒ null, which the pane already renders as "No entity or blueprint selected."
        //   ⛔ Not Entity.Null, which would make the session resolve snapshots for entity 0.
        Assert.Null(
            Hrot.Blueprints.Editor.Inspector.BlueprintRuntimeInspectorPane.ResolveEntity(Ctx()));
    }

    private static Hrot.Editor.AiShared.Shell.DetailsContext Ctx(params Fdp.Core.Entity[] entities)
        => Hrot.Editor.AiShared.Shell.DetailsContextBuilder.Build(
            store:       new Hrot.Editor.AiShared.Selection.EditorSelectionStore(),
            perspective: "Blueprint",
            mode:        Hrot.Editor.AiShared.Variables.VariableRunState.Planning,
            entities:    new Fixed(entities));

    private sealed class Fixed : Hrot.Editor.AiShared.Shell.IEntitySelectionSource
    {
        private readonly Fdp.Core.Entity[] _e;
        public Fixed(Fdp.Core.Entity[] e) => _e = e;
        public System.Collections.Generic.IReadOnlyList<Fdp.Core.Entity> Selected() => _e;
    }
}
