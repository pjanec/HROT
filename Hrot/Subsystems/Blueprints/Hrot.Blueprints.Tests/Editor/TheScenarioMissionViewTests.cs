using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Presentation.Icons;
using Fdp.Presentation.WindowManager;
using Hrot.Core.Mission;
using Hrot.Editor;
using Hrot.Editor.AiShared.Selection;
using Hrot.Editor.AiShared.Shell;
using Hrot.Editor.AiShared.Variables;
using Hrot.Editor.Scenario;
using Hrot.Presentation.Behavior;
using Hrot.UI.Common.Facades;
using Hrot.UI.Common.Models;
using Hrot.UI.Common.Panels;
using Xunit;

namespace Hrot.Blueprints.Tests.Editor;

/// <summary>
/// ⭐⭐⭐ <b><c>L6.4</c>'s rails — THE MISSION PLAN VIEW.</b>
/// 📄 <c>DESIGN_Details_Panel_View_Switching.md</c> §6 <c>L6</c> stage 5; the handoff's gate:
/// <i>"offer set on a brain-equipped entity includes Mission; empty otherwise."</i>
///
/// <para>⭐⭐ <b><c>MissionPanel.DrawContent</c> is headless-safe, measured</b> — it calls
/// <c>GetAvailableBehaviors</c> and the two pollers <b>before</b> its
/// <c>ImGui.GetCurrentContext() == IntPtr.Zero</c> guard, deliberately *(its own doc comment says so:
/// "so that tests can verify the call without a render context")*. ⇒ ⭐ these rails drive the REAL
/// panel through the REAL view, ⛔ not a drawing stand-in.</para>
/// </summary>
public sealed class TheScenarioMissionViewTests
{
    // ══ helpers ══════════════════════════════════════════════════════════════

    private static EditorSubsystem RealEditor()
    {
        var editor = new EditorSubsystem();
        editor.RegisterWindows(new WindowManager(new IconAtlas(IntPtr.Zero, 16f, 16f)));
        return editor;
    }

    private sealed class Selected : IEntitySelectionSource
    {
        private readonly Entity[] _entities;
        public Selected(params Entity[] entities) => _entities = entities;
        IReadOnlyList<Entity> IEntitySelectionSource.Selected() => _entities;
    }

    private static DetailsContext Context(params Entity[] entities)
        => DetailsContextBuilder.Build(
            new EditorSelectionStore(), "Editor", VariableRunState.Planning, new Selected(entities));

    /// <summary>⭐ A mission service that records who it was asked about — the brain signal and the
    /// panel both go through it, so one recorder covers both directions.</summary>
    private sealed class RecordingMissions : IMissionEditorService
    {
        private readonly string[] _behaviors;
        public RecordingMissions(params string[] behaviors) => _behaviors = behaviors;

        public readonly List<long> AskedAbout = new();

        public IReadOnlyList<string> GetAvailableBehaviors(long entityId)
        {
            AskedAbout.Add(entityId);
            return _behaviors;
        }

        public (MissionPlan? Plan, long Version) GetMissionSnapshot(long entityId) => (null, 0);

        public Task<MissionCommitResult> CommitMissionAsync(long id, MissionPlan plan, long baseVersion)
            => throw new NotSupportedException();
        public Task<MissionCommitResult> SendControlCommandAsync(long id, eMissionCommandType t, Guid taskId)
            => throw new NotSupportedException();
    }

    /// <summary>⭐ Picking is an operator gesture — ⛔ nothing in these rails should reach it, so
    /// every member throws rather than returning a benign default that could hide a wrong call.</summary>
    private sealed class NoPicking : IMapPickService
    {
        public Task<GeoPoint> PickLocationAsync(System.Threading.CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<int> PickEntityAsync(string[]? filterPresets = null, System.Threading.CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<IReadOnlyList<int>> PickAreaEntitiesAsync(string[]? filterPresets = null, System.Threading.CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private static MissionPanel NewPanel() => new(0, BehaviorUiSetup.CreateRegistry());

    private static readonly Entity One = new(21, 1);
    private static readonly Entity Two = new(22, 1);

    // ══ the composition root ═════════════════════════════════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>THE PRODUCTION EDITOR REGISTERED IT.</b> 📌 <c>R-67</c> — and this is the root that has
    /// forgotten a service nine times, so the registration itself is worth a rail.
    /// </summary>
    [Fact]
    public void TheScenarioCatalogue_OffersTheMissionView()
        => Assert.Contains(ScenarioMissionViewDescriptor.ViewId,
                           RealEditor().ScenarioWorkspace!.DetailsViews.All.Select(d => d.Id));

    /// <summary>
    /// ⭐⭐ <b>Mission outranks Components, and both sit below Runtime.</b> 📌 <c>R-98</c>: rank is the
    /// DEFAULT only. ⚠ Railed because the ordering is a claim — *an entity with a mission plan is a
    /// more specific fact than "it has components"* — ⛔ not an arbitrary constant.
    /// </summary>
    [Fact]
    public void ItsRank_SitsBetweenComponentsAndRuntime()
    {
        Assert.True(ScenarioMissionViewDescriptor.Rank > ScenarioComponentsViewDescriptor.Rank);
        Assert.True(ScenarioMissionViewDescriptor.Rank < RuntimeDetailsViewDescriptor.Rank);
    }

    // ══ the predicate — the item's own gate ══════════════════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>THE GATE: a brain-equipped entity OFFERS Mission; one without does not.</b>
    /// ⭐ Asserted through the registry's <c>OfferSet</c>, which is the shape the toolbar asks.
    /// </summary>
    [Fact]
    public void OnlyABrainEquippedEntity_OffersMission()
    {
        var withBrain    = Catalogue(hasBrain: _ => true);
        var withoutBrain = Catalogue(hasBrain: _ => false);

        Assert.Contains      (ScenarioMissionViewDescriptor.ViewId,
                              withBrain.OfferSet(Context(One)).Select(d => d.Id));
        Assert.DoesNotContain(ScenarioMissionViewDescriptor.ViewId,
                              withoutBrain.OfferSet(Context(One)).Select(d => d.Id));
    }

    /// <summary>
    /// ⛔⛔ <b>The ENTITY half still binds — none and two both offer nothing, even with a signal that
    /// says yes to everything.</b> 📌 <c>R-118</c>: <c>MissionPanel</c> shows ONE
    /// <c>SelectedEntityId</c>, so offering on a multi-selection would silently drop the rest.
    /// </summary>
    [Fact]
    public void NoEntityAndTwoEntities_OfferNothing()
    {
        var views = Catalogue(hasBrain: _ => true);

        Assert.Empty(views.OfferSet(Context()));
        Assert.Empty(views.OfferSet(Context(One, Two)));
    }

    /// <summary>
    /// ⭐⭐ <b>No mission service ⇒ no offer.</b> ⚠ The root passes
    /// <c>_missionService is { } svc &amp;&amp; …</c>, so a host without one yields a <c>false</c>
    /// signal — ⛔ NOT a claim that the entity has behaviours. 📌 <c>R-117</c>: declining hands the
    /// answer to the shell's one grey line.
    /// </summary>
    [Fact]
    public void WithNoMissionServiceAtAll_ItNeverOffers()
        => Assert.Empty(Catalogue(hasBrain: null).OfferSet(Context(One)));

    // ══ the seam ═════════════════════════════════════════════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>THE PANEL IS POINTED AT <c>ctx.Entities[0]</c>'s NETWORK ID — the translation lands.</b>
    ///
    /// <para>⛔⛔ <b>Two distinct facts, and the second is the one that bites.</b>
    /// ① the entity is the CONTEXT's, not the update loop's; ② <c>SelectedEntityId</c> is an
    /// <b>int network id</b>, not <c>Entity.Index</c> — 📐 <c>MissionPanel.cs:103</c> and
    /// <c>EditorSubsystem.Update:1816</c>. ⚠ Handing it <c>entity.Index</c> would look right in every
    /// single-entity scenario where the two happen to coincide, and show the wrong unit's mission
    /// everywhere else.</para>
    /// </summary>
    [Fact]
    public void ItPointsThePanelAtTheSelectedEntitysNetworkId()
    {
        var panel      = NewPanel();
        var missions   = new RecordingMissions("Patrol");
        var descriptor = ScenarioMissionViewDescriptor.For(
            panel:       panel,
            service:     () => missions,
            pick:        () => new NoPicking(),
            // ⭐ NOT Entity.Index — a deliberately different number, so a view that passed the entity
            //   through unmapped cannot pass this rail by coincidence.
            networkIdOf: e => e == One ? 4242 : 0,
            hasBrain:    _ => true);

        descriptor.Create().Draw(Context(One), "scope");

        Assert.Equal(4242, panel.SelectedEntityId);
        Assert.Contains(4242L, missions.AskedAbout);   // ⭐ and it reached the service, not just the field
    }

    /// <summary>
    /// ⭐⭐ <b>A two-entity context does not touch the panel.</b> ⚠ The predicate already declines, so
    /// this is the belt-and-braces half — ⛔ but without it, a draw that ran anyway would leave the
    /// panel pointing at a stale entity while the shell drew the grey line over it.
    /// </summary>
    [Fact]
    public void AMultiSelection_LeavesThePanelUntouched()
    {
        var panel = NewPanel();
        panel.SelectedEntityId = 7;

        ScenarioMissionViewDescriptor.For(
            panel, () => new RecordingMissions("Patrol"), () => new NoPicking(),
            networkIdOf: _ => 4242, hasBrain: _ => true)
            .Create().Draw(Context(One, Two), "scope");

        Assert.Equal(7, panel.SelectedEntityId);
    }

    /// <summary>
    /// ⭐⭐⭐ <b>The view owns its panel — it is NOT the editor's Mission Editor window's panel.</b>
    ///
    /// <para>⛔⛔ 📐 Measured: <c>EditorSubsystem.Update</c> writes <c>_missionPanel.SelectedEntityId</c>
    /// EVERY FRAME from the legacy <c>DefaultSelectionState</c>, ⛔ not from the World's
    /// <c>SelectionState</c> that <c>ctx.Entities</c> reads *(<c>R-122</c>)*. ⇒ ⚠ a SHARED panel would
    /// make the Details view and the Mission Editor window overwrite each other's selection within one
    /// frame. ⭐ This rail pins the separation; 📌 the two selection models converge under
    /// <c>UXI-11</c>, not here.</para>
    /// </summary>
    [Fact]
    public void TwoViewInstances_DoNotShareAPanel()
    {
        var a = new ScenarioMissionView(NewPanel(), () => new RecordingMissions(), () => new NoPicking(), _ => 1);
        var b = new ScenarioMissionView(NewPanel(), () => new RecordingMissions(), () => new NoPicking(), _ => 2);

        Assert.NotSame(a.Panel, b.Panel);
    }

    // ══ ══════════════════════════════════════════════════════════════════════

    /// <summary>⭐ A catalogue holding the production descriptor, with the one thing a headless host
    /// cannot supply — a live mission service — stubbed. See
    /// <c>TheScenarioComponentsViewTests.LiveCatalogue</c> for why the gate splits this way.</summary>
    private static DetailsViewRegistry Catalogue(Func<Entity, bool>? hasBrain)
    {
        var views = new DetailsViewRegistry();
        views.Add(ScenarioMissionViewDescriptor.For(
            panel:       NewPanel(),
            service:     () => new RecordingMissions("Patrol"),
            pick:        () => new NoPicking(),
            networkIdOf: _ => 1,
            hasBrain:    hasBrain));
        return views;
    }
}
