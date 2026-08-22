using System;
using Fdp.Core;
using Hrot.Editor.AiShared.Shell;
using Hrot.UI.Common.Facades;
using Hrot.UI.Common.Panels;

namespace Hrot.Editor.Scenario;

/// <summary>
/// ⭐⭐⭐ <b><c>L6.4</c> — THE MISSION PLAN VIEW: the selected entity's mission, as a Details view.</b>
/// 📄 <c>DESIGN_Details_Panel_View_Switching.md</c> §6 <c>L6</c> stage 5 · §3's <b>reference wall</b>.
///
/// <para>⛔⛔ <b>It OWNS its panel, where <see cref="ScenarioComponentsView"/> BORROWS one — and the
/// asymmetry is measured, not stylistic.</b> 📐 <c>EditorSubsystem</c> wires ~60 lines into
/// <c>_fdpEntityInspector</c> after constructing it *(reflector, buffer-view providers, serializer,
/// mutation interceptor, edit-context factory)*, so a fresh entity-inspector panel would be a
/// crippled one. ⚠ <c>_missionPanel</c> gets <b>nothing</b> after
/// <c>new MissionPanel(0, BehaviorUiSetup.CreateRegistry())</c> ⇒ ⭐ a fresh instance is fully
/// equivalent, and it is the <c>R-120</c>-clean choice.</para>
///
/// <para>⭐⭐⭐ <b>And owning it avoids a real conflict, not a theoretical one.</b> 📐 Measured:
/// <c>EditorSubsystem.Update</c> *(:1810–1823)* writes <c>_missionPanel.SelectedEntityId</c> EVERY
/// FRAME from <c>_selectionState.PrimarySelected</c> — the legacy <c>DefaultSelectionState</c>, ⛔ not
/// the World's <c>SelectionState</c> that <c>ctx.Entities</c> reads *(<c>R-122</c>)*. ⇒ ⚠ a view
/// writing that same property at draw time would fight the update loop and, worse, would change what
/// the Mission Editor <b>window</b> shows in the same frame. ⭐ Two panels, two selections, no
/// arbitration — which is exactly what <c>L1.1</c>'s per-instance factory is for.
/// 📌 The two selection models converge under <c>UXI-11</c>, not here.</para>
/// </summary>
public sealed class ScenarioMissionView : IDetailsViewInstance
{
    private readonly Func<IMissionEditorService?> _service;
    private readonly Func<IMapPickService?>       _pick;
    private readonly Func<Entity, int>            _networkIdOf;

    public ScenarioMissionView(
        MissionPanel                 panel,
        Func<IMissionEditorService?> service,
        Func<IMapPickService?>       pick,
        Func<Entity, int>            networkIdOf)
    {
        Panel        = panel       ?? throw new ArgumentNullException(nameof(panel));
        _service     = service     ?? throw new ArgumentNullException(nameof(service));
        _pick        = pick        ?? throw new ArgumentNullException(nameof(pick));
        _networkIdOf = networkIdOf ?? throw new ArgumentNullException(nameof(networkIdOf));
    }

    /// <summary>
    /// ⭐ This view's OWN panel. ⭐ Exposed so a rail can assert which entity it was pointed at —
    /// 📌 <c>R-78</c>: *"the right view drawn about the wrong entity"* is invisible on screen whenever
    /// the two selections happen to coincide, which in this editor they usually do.
    /// </summary>
    public MissionPanel Panel { get; }

    /// <summary>
    /// ⭐⭐⭐ <b>Points the panel at <c>ctx.Entities[0]</c>, then draws it.</b>
    ///
    /// <para>⚠⚠ <b><c>MissionPanel.SelectedEntityId</c> is a NETWORK id, not an <c>Entity</c></b>
    /// — 📐 measured at <c>MissionPanel.cs:103</c> and at the update loop that feeds it. ⇒ ⭐ the
    /// translation is a delegate from the root, because <c>NetworkIdentity</c> lookup needs the World
    /// and this type must not hold one. ⛔ <c>0</c> means *"no selection"* to the panel, which is the
    /// honest answer for an entity that is not replicated.</para>
    ///
    /// <para>⚠ <paramref name="idScope"/> is unused: this panel is this view's alone, so nothing else
    /// can collide with its ImGui ids.</para>
    /// </summary>
    public void Draw(DetailsContext context, string idScope)
    {
        if (context.Entities is not { Count: 1 }) return;

        var service = _service();
        var pick    = _pick();
        if (service is null || pick is null) return;

        Panel.SelectedEntityId = _networkIdOf(context.Entities[0]);
        Panel.DrawContent(service, pick);
    }

    /// <summary>⛔ Deliberately empty — <see cref="MissionPanel"/> holds only draft and pick state,
    /// and owns no unmanaged resource.</summary>
    public void Dispose() { }
}

/// <summary>
/// ⭐⭐ <b><c>L6.4</c> — the Mission descriptor.</b> ⭐ Its predicate is <c>L6.5</c>'s
/// <c>OneEntityWithBrain</c> — ⛔ not a restatement *(<c>R-116</c>: the predicate ships with the view,
/// but the SHAPE is shared)*.
/// </summary>
public static class ScenarioMissionViewDescriptor
{
    /// <summary>⭐ Stable id — the layout key and the remembered pick *(§2)*.</summary>
    public const string ViewId = "details.mission";

    /// <summary>
    /// ⭐ Rank <b>40</b> — above Components' <c>30</c>, below Runtime's <c>50</c>.
    /// ⚠ Deliberate: an entity that HAS a mission plan is a more specific fact than "it has
    /// components", and the predicate already guarantees it. 📌 <c>R-98</c> — the toolbar's remembered
    /// pick still wins.
    /// </summary>
    public const int Rank = 40;

    /// <summary>
    /// ⭐⭐ Build the descriptor.
    /// </summary>
    /// <param name="panel">⭐ This view's own panel — see the class remarks on why it is not shared.</param>
    /// <param name="hasBrain">
    /// ⭐⭐⭐ <b><c>L6.5</c>'s brain signal.</b> ⚠ As-built (c): there is no <c>HasBrain</c> in this
    /// codebase — the root supplies <c>GetAvailableBehaviors(netId).Count > 0</c>. ⛔ <c>null</c> ⇒ the
    /// view never offers, which is the honest answer for a host with no mission service.
    /// </param>
    public static DetailsViewDescriptor For(
        MissionPanel                 panel,
        Func<IMissionEditorService?> service,
        Func<IMapPickService?>       pick,
        Func<Entity, int>            networkIdOf,
        Func<Entity, bool>?          hasBrain)
    {
        var instance = new ScenarioMissionView(panel, service, pick, networkIdOf);

        return new DetailsViewDescriptor(
            Id:        ViewId,
            Title:     "Mission",
            Rank:      Rank,
            AppliesTo: DetailsViewPredicates.OneEntityWithBrain(hasBrain),
            Create:    () => instance);
    }
}
