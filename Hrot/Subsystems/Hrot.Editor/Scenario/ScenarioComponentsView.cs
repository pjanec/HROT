using System;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Diagnostics.Contracts.Panels;
using Fdp.Presentation.Abstractions;
using Fdp.Presentation.Panels;
using Hrot.Editor.AiShared.Shell;

namespace Hrot.Editor.Scenario;

/// <summary>⭐⭐⭐ U-obs-5 (group 6) — WHICH entity <see cref="ScenarioComponentsView"/> is drawing
/// about, this frame. 📄 <c>DESIGN_UI_Observability_Snapshot.md</c> §Example; mirrors
/// <c>RuntimeDetailsViewPanelViewModel</c>'s composed address. ⭐ Deliberately captures the
/// TARGETED entity, not the components themselves — the panel is BORROWED (see the class remarks) and
/// its component list is already <c>EntityInspectorPanel</c>'s own concern; what THIS view adds is the
/// entity/session routing that <c>R-78</c>'s chameleon failure is about.</summary>
public sealed record ScenarioComponentsViewPanelViewModel(
    string PanelId, string PanelKind, bool HasTarget, int EntityIndex) : IPanelViewModel
{
    /// <inheritdoc/>
    public JsonNode Dump() => PanelDump.Of(this);
}

/// <summary>
/// ⭐⭐⭐ <b><c>L6.3</c> — THE COMPONENTS VIEW: the entity inspector's component column, as a Details
/// view.</b>
/// 📄 <c>DESIGN_Details_Panel_View_Switching.md</c> §6 <c>L6</c> stage 4 · §3's <b>reference wall</b>.
///
/// <para>⛔⛔ <b>Why this type lives in <c>Hrot.Editor</c> and not in <c>Hrot.Editor.AiShared</c>.</b>
/// 📐 <c>EntityInspectorPanel</c> is in <c>Fdp.Presentation</c>; <c>IDetailsViewInstance</c> is in
/// <c>Hrot.Editor.AiShared</c>, which is BELOW it *(§3)*. ⭐ The composition root is the only assembly
/// that sees both — the handoff says so, and it is a reference fact, not a placement preference.</para>
///
/// <para>⭐⭐ <b>It BORROWS the editor's one wired panel</b> — exactly as <c>RuntimeDetailsView</c>
/// borrows its pane. ⚠ That panel carries the reflector, the buffer-view providers, the serializer,
/// the mutation interceptor and the edit-context factory that <c>EditorSubsystem</c> wires over ~60
/// lines. ⛔ Constructing a fresh <c>EntityInspectorPanel</c> per view instance would render components
/// with none of that — 📌 the <c>2026-08-16</c> silent-default shape: <i>the caller HELD the value and
/// did not pass it</i>. ⇒ <see cref="Dispose"/> must not touch the panel.</para>
///
/// <para>⚠ <b>Stated limit of borrowing:</b> two simultaneous Components views would share the panel's
/// component-search filter. ⭐ <c>R-120</c> is not breached — the shared state lives at the composition
/// root and is handed in — ⛔ but it is a real limit, and today it cannot occur *(Scenario hosts one
/// Details window)*.</para>
/// </summary>
public sealed class ScenarioComponentsView : IDetailsViewInstance
{
    private readonly Func<IInspectableSession?>       _session;
    private readonly Action<IInspectableSession, Entity> _draw;

    /// <param name="session">
    /// ⭐ The inspectable session, re-asked EVERY FRAME. ⚠ In the editor it is the repository adapter,
    /// which is <c>null</c> until a scenario is open — ⛔ so caching it once would pin a stale session
    /// across a scenario reload.
    /// </param>
    /// <param name="draw">
    /// ⭐⭐ <b>What to render for the chosen entity.</b> ⭐ Production passes
    /// <c>EntityInspectorPanel.DrawComponentsFor</c>; ⚠ it is a delegate rather than the panel itself so
    /// the *"which entity"* decision is separately observable — 📌 <c>R-78</c>'s chameleon failure is
    /// exactly "the right view drawn about the wrong entity", and a rail can only see that if the
    /// entity crosses a seam.
    /// </param>
    public ScenarioComponentsView(
        Func<IInspectableSession?>          session,
        Action<IInspectableSession, Entity> draw)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _draw    = draw    ?? throw new ArgumentNullException(nameof(draw));
    }

    /// <summary>⭐⭐⭐ U-obs-5: BUILD · CAPTURE — before any of <see cref="Draw"/>'s guards, so a headless
    /// run still observes WHICH entity this view was pointed at, even when it declines to draw.</summary>
    private ScenarioComponentsViewPanelViewModel BuildAndPublish(DetailsContext context, string idScope)
    {
        var panelId = $"{idScope}/{ScenarioComponentsViewDescriptor.ViewId}";
        PanelSnapshot.DeclareInstrumented(panelId);

        bool hasTarget   = context.Entities is { Count: 1 };
        int  entityIndex = hasTarget ? context.Entities[0].Index : -1;
        var vm = new ScenarioComponentsViewPanelViewModel(
            panelId, ScenarioComponentsViewDescriptor.ViewId, hasTarget, entityIndex);

        if (PanelSnapshot.CaptureEnabled) PanelSnapshot.Register(vm);
        return vm;
    }

    /// <summary>⭐⭐ Test hook — the BUILD + CAPTURE portion, callable with no session/panel wired.</summary>
    internal ScenarioComponentsViewPanelViewModel SimulateDraw(DetailsContext context, string idScope) =>
        BuildAndPublish(context, idScope);

    /// <summary>
    /// ⭐⭐⭐ <b>Draws the components of <c>ctx.Entities[0]</c> — the World's selected entity
    /// *(<c>L0.4</c>/<c>R-122</c>)*, not the panel's own <c>HashSet</c>.</b>
    ///
    /// <para>⛔ <b>The guards do not draw an apology.</b> 📌 <c>R-117</c>: a view that claims the panel
    /// in order to say <i>"nothing here"</i> is the blank-shaped defect. ⭐ Both conditions are already
    /// in <see cref="ScenarioComponentsViewDescriptor.Applies"/>, so the shell's grey line answers in
    /// one voice; these are the belt-and-braces half, and reaching them means the predicate and the
    /// draw disagree.</para>
    ///
    /// <para>⚠ <paramref name="idScope"/> is used only to compose the <c>PanelSnapshot</c> address —
    /// the borrowed panel still owns its own ImGui ids. ⛔ Pushing a scope there would change the ids of
    /// a panel the Entity Inspector window also draws, and layout state keyed on them would reset.</para>
    /// </summary>
    public void Draw(DetailsContext context, string idScope)
    {
        BuildAndPublish(context, idScope);

        if (context.Entities is not { Count: 1 }) return;

        var session = _session();
        if (session is null) return;

        var entity = context.Entities[0];
        if (!session.IsAlive(entity)) return;

        _draw(session, entity);
    }

    /// <summary>⛔ Deliberately empty — the panel is BORROWED. See the class remarks.</summary>
    public void Dispose() { }
}

/// <summary>
/// ⭐⭐ <b><c>L6.3</c> — the Components descriptor.</b> ⭐ The predicate ships with the view
/// *(<c>R-116</c>)*, and it is <c>L6.5</c>'s helper rather than a restatement.
/// </summary>
public static class ScenarioComponentsViewDescriptor
{
    /// <summary>⭐ Stable id — the layout key and the remembered pick *(§2)*.</summary>
    public const string ViewId = "details.components";

    /// <summary>
    /// ⭐ Rank <b>30</b> — above <c>Variables</c>' <c>10</c>, below <c>Runtime</c>'s <c>50</c>.
    /// ⚠ Deliberate: when a designer has selected an ENTITY, its components are the more likely
    /// intent than the asset's variable table; ⛔ but a live session still wins. 📌 <c>R-98</c> — rank
    /// decides only the DEFAULT; the toolbar's pick is remembered and wins.
    /// </summary>
    public const int Rank = 30;

    /// <summary>
    /// ⭐⭐ Build the descriptor over the editor's one wired panel.
    /// <para>⚠ <paramref name="panel"/> is a delegate because <c>EditorSubsystem</c> builds the panel
    /// as a field and the descriptor is created during <c>RegisterWindows</c> — ⛔ and because a null
    /// panel must make the view DECLINE rather than throw mid-frame.</para>
    /// </summary>
    public static DetailsViewDescriptor For(
        Func<EntityInspectorPanel?> panel,
        Func<IInspectableSession?>  session)
    {
        ArgumentNullException.ThrowIfNull(panel);
        ArgumentNullException.ThrowIfNull(session);

        return For(
            session: session,
            draw:    (s, e) => panel()?.DrawComponentsFor(s, e),
            canDraw: () => panel() is not null && session() is not null);
    }

    /// <summary>
    /// ⭐ The general form — ⛔ not a test seam: <see cref="For(Func{EntityInspectorPanel},Func{IInspectableSession})"/>
    /// routes through it *(<c>R-13</c>: one implementation)*, and a future host that renders components
    /// some other way binds here without a second descriptor.
    /// </summary>
    /// <param name="canDraw">
    /// ⭐⭐ Folded into the PREDICATE, not into the draw. ⚠ A host with no session or no panel must not
    /// OFFER Components — 📌 <c>R-117</c>: declining yields the shell's grey line, whereas offering and
    /// then drawing nothing is a blank panel that reads as "nothing selected".
    /// </param>
    public static DetailsViewDescriptor For(
        Func<IInspectableSession?>          session,
        Action<IInspectableSession, Entity> draw,
        Func<bool>                          canDraw)
    {
        ArgumentNullException.ThrowIfNull(canDraw);
        var instance = new ScenarioComponentsView(session, draw);

        return new DetailsViewDescriptor(
            Id:        ViewId,
            Title:     "Components",
            Rank:      Rank,
            AppliesTo: ctx => Applies(ctx, canDraw),
            // ⚠ The SAME instance every time — it holds no per-window state, and the panel behind it
            //   is shared anyway. 📌 The factory SHAPE is what keeps L1.1's contract.
            Create:    () => instance);
    }

    /// <summary>⭐ Extracted so a rail can assert the predicate without a panel or a session.</summary>
    public static bool Applies(DetailsContext context, Func<bool> canDraw)
        => DetailsViewPredicates.ExactlyOneEntity(context) && canDraw();
}
