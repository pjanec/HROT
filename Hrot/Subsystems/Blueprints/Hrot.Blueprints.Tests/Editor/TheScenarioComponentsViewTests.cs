using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Core;
using Fdp.Presentation.Abstractions;
using Fdp.Presentation.Icons;
using Fdp.Presentation.WindowManager;
using Hrot.Editor;
using Hrot.Editor.AiShared.Selection;
using Hrot.Editor.AiShared.Shell;
using Hrot.Editor.AiShared.Variables;
using Hrot.Editor.Scenario;
using Xunit;

namespace Hrot.Blueprints.Tests.Editor;

/// <summary>
/// ⭐⭐⭐ <b><c>L6.3</c>'s rails — THE COMPONENTS VIEW.</b>
/// 📄 <c>DESIGN_Details_Panel_View_Switching.md</c> §6 <c>L6</c> stage 4; the handoff's gate:
/// <i>"offer set on an entity context includes Components; it renders the selected entity's
/// components."</i>
///
/// <para>⚠⚠ <b>The second half of that gate is a VISUAL check, and these rails do not claim it.</b>
/// ⛔ Drawing needs a live ImGui context. ⭐ What IS railed here is the half a rail can see and a
/// screenshot cannot: that the entity handed to the renderer is <c>ctx.Entities[0]</c> — 📌 <c>R-78</c>'s
/// chameleon failure is *"the right view, drawn about the wrong entity"*, which looks perfectly fine
/// on screen whenever the two happen to coincide.</para>
/// </summary>
public sealed class TheScenarioComponentsViewTests
{
    // ══ helpers ══════════════════════════════════════════════════════════════

    private static EditorSubsystem RealEditor()
    {
        var editor = new EditorSubsystem();
        editor.RegisterWindows(new WindowManager(new IconAtlas(IntPtr.Zero, 16f, 16f)));
        return editor;
    }

    /// <summary>
    /// ⭐⭐⭐ <b>A registry holding the PRODUCTION descriptor, over a session that exists.</b>
    ///
    /// <para>⛔⛔ <b>Why the offer-set rails cannot use the production editor, measured
    /// <c>2026-08-22</c>:</b> <c>_fdpRepoAdapter</c> — the <c>IInspectableSession</c> the Components
    /// view renders through — is built at <c>EditorSubsystem.cs:1579</c>, <b>inside the
    /// <c>if (!_headless)</c> at :1565</b>. ⇒ ⭐ a headless editor NEVER has one, so a headless offer
    /// set correctly never contains Components. ⚠ The first version of these rails asserted against
    /// <see cref="RealEditor"/> and went red on an EMPTY offer set — ⛔ that was the rail wrong, not
    /// the wiring.</para>
    ///
    /// <para>⭐⭐ <b>So the gate splits, and both halves are stated:</b>
    /// <see cref="TheScenarioCatalogue_OffersTheComponentsView"/> is the <c>R-67</c> half — the REAL
    /// root registered it; ⭐ these rails are the PREDICATE half, over the same descriptor factory the
    /// root calls, with the one thing headless cannot supply stubbed. ⛔ Neither half alone is the
    /// gate.</para>
    /// </summary>
    private static DetailsViewRegistry LiveCatalogue(IInspectableSession session)
    {
        var views = new DetailsViewRegistry();
        views.Add(ScenarioComponentsViewDescriptor.For(
            panel:   () => new Fdp.Presentation.Panels.EntityInspectorPanel(),
            session: () => session));
        return views;
    }

    private sealed class Selected : IEntitySelectionSource
    {
        private readonly Entity[] _entities;
        public Selected(params Entity[] entities) => _entities = entities;
        IReadOnlyList<Entity> IEntitySelectionSource.Selected() => _entities;
    }

    private static DetailsContext Context(params Entity[] entities)
        => DetailsContextBuilder.Build(
            new EditorSelectionStore(), "Scenario", VariableRunState.Planning, new Selected(entities));

    /// <summary>⭐ The smallest session that answers what the view asks: <c>IsAlive</c>.
    /// ⛔ Everything else throws — a rail that let the view wander further would stop being about
    /// the seam.</summary>
    private sealed class OneEntitySession : IInspectableSession
    {
        private readonly Entity _alive;
        public OneEntitySession(Entity alive) => _alive = alive;

        public bool IsAlive(Entity e) => e == _alive;

        public bool IsReadOnly  => false;
        public int  EntityCount => 1;
        public IEnumerable<Entity> GetEntities() => new[] { _alive };
        public bool    HasComponent(Entity e, Type t)              => throw new NotSupportedException();
        public object? GetComponent (Entity e, Type t)             => throw new NotSupportedException();
        public void    SetComponent (Entity e, Type t, object d)   => throw new NotSupportedException();
        public IEnumerable<Type> GetAllComponentTypes()            => throw new NotSupportedException();
        public bool    HasAuthority (Entity e, Type t)             => throw new NotSupportedException();
    }

    private static readonly Entity One = new(11, 1);
    private static readonly Entity Two = new(12, 1);

    // ══ the composition root ═════════════════════════════════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>THE PRODUCTION EDITOR OFFERS IT.</b> 📌 <c>R-67</c>: <i>"a rail that builds its own
    /// composition root cannot see a composition-root defect"</i> — and this root is the one that has
    /// forgotten to pass a service <b>nine</b> times. ⛔ A rail over a hand-built registry would stay
    /// green through exactly the failure that matters.
    /// </summary>
    [Fact]
    public void TheScenarioCatalogue_OffersTheComponentsView()
        => Assert.Contains(ScenarioComponentsViewDescriptor.ViewId,
                           RealEditor().ScenarioWorkspace!.DetailsViews.All.Select(d => d.Id));

    /// <summary>
    /// ⭐⭐ <b>It outranks Variables and is outranked by Runtime.</b> 📌 <c>R-98</c>: rank decides only
    /// the DEFAULT pick. ⚠ Railed because the ordering is a deliberate claim about intent *(an entity
    /// is selected ⇒ its components beat the asset's variable table)*, ⛔ not an arbitrary constant.
    /// </summary>
    [Fact]
    public void ItsRank_SitsBetweenVariablesAndRuntime()
    {
        Assert.True(ScenarioComponentsViewDescriptor.Rank > VariablesDetailsViewDescriptor.Rank);
        Assert.True(ScenarioComponentsViewDescriptor.Rank < RuntimeDetailsViewDescriptor.Rank);
    }

    // ══ the predicate ════════════════════════════════════════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>The item's own gate: an entity context OFFERS Components; a non-entity one does not.</b>
    /// ⭐ Asserted through the REGISTRY's offer set rather than the predicate alone — that is the shape
    /// the shell's toolbar actually asks.
    /// </summary>
    [Fact]
    public void SelectingOneEntity_PutsComponentsInTheOfferSet()
    {
        var views = LiveCatalogue(new OneEntitySession(One));

        Assert.Contains      (ScenarioComponentsViewDescriptor.ViewId,
                              views.OfferSet(Context(One)).Select(d => d.Id));
        Assert.DoesNotContain(ScenarioComponentsViewDescriptor.ViewId,
                              views.OfferSet(Context()).Select(d => d.Id));
    }

    /// <summary>
    /// ⛔⛔ <b>TWO entities offer NOTHING</b> — 📌 <c>R-118</c>. ⚠ The panel renders ONE entity's
    /// components; offering on a multi-selection would show the first and silently ignore the rest,
    /// which is precisely the collapse <c>L0.2</c> deleted from the bridges.
    /// </summary>
    [Fact]
    public void TwoSelectedEntities_DoNotOfferComponents()
        => Assert.DoesNotContain(
               ScenarioComponentsViewDescriptor.ViewId,
               LiveCatalogue(new OneEntitySession(One)).OfferSet(Context(One, Two)).Select(d => d.Id));

    /// <summary>
    /// ⭐⭐⭐ <b>NO SESSION ⇒ NO OFFER, and that belongs in the PREDICATE.</b>
    /// 📐 Measured: <c>_fdpRepoAdapter</c> is <c>null</c> until a scenario is open, so this is the
    /// editor's state every time it starts. ⛔ Offering and then drawing nothing would give
    /// <c>R-117</c>'s blank panel — which reads as *"nothing selected"* when the truth is *"nothing
    /// loaded"*. ⭐ Declining hands the answer to the shell's one grey line.
    /// </summary>
    [Fact]
    public void WithNoSessionYet_ItDoesNotOffer()
    {
        Assert.False(ScenarioComponentsViewDescriptor.Applies(Context(One), canDraw: () => false));
        Assert.True (ScenarioComponentsViewDescriptor.Applies(Context(One), canDraw: () => true));
    }

    // ══ the seam ═════════════════════════════════════════════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>THE ENTITY RENDERED IS <c>ctx.Entities[0]</c> — the World's selection *(<c>R-122</c>)*,
    /// not the panel's own <c>HashSet</c>.</b>
    ///
    /// <para>⛔⛔ <b>This is the rail that pays for the extraction of <c>DrawComponentsFor</c>.</b>
    /// 📐 Measured: <c>EntityInspectorPanel.DrawEntityDetails</c> resolves
    /// <c>selCount == 1 ? _selectedEntities.First() : context.SelectedEntity</c> ⇒ ⚠ a Details view
    /// routed through THAT would render whatever the Entity Inspector window's list was last clicked
    /// on, silently ignoring the World selection. ⭐ <c>DrawComponentsFor</c> takes the entity, so the
    /// caller's decision is the one that lands — and this asserts it crossed the seam intact.</para>
    /// </summary>
    [Fact]
    public void ItRendersTheSelectedEntity_NotTheFirstEntityInTheWorld()
    {
        Entity? rendered = null;
        var descriptor = ScenarioComponentsViewDescriptor.For(
            session: () => new OneEntitySession(Two),
            draw:    (_, e) => rendered = e,
            canDraw: () => true);

        descriptor.Create().Draw(Context(Two), "scope");

        Assert.Equal(Two, rendered);
    }

    /// <summary>
    /// ⭐⭐ <b>A DEAD entity renders nothing.</b> ⚠ Not defensive noise: entity selection lives on the
    /// entity *(<c>R-122</c>)*, so a selected entity destroyed by the sim leaves a stale id in the
    /// context for the rest of the frame. ⛔ <c>ComponentReflector</c> would read a recycled slot.
    /// </summary>
    [Fact]
    public void ADestroyedSelectedEntity_RendersNothing()
    {
        bool drew = false;
        var descriptor = ScenarioComponentsViewDescriptor.For(
            session: () => new OneEntitySession(Two),   // ⭐ One is NOT alive in this session
            draw:    (_, _) => drew = true,
            canDraw: () => true);

        descriptor.Create().Draw(Context(One), "scope");

        Assert.False(drew);
    }
}
