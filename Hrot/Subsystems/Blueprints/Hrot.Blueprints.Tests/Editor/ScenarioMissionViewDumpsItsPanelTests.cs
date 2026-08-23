using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Diagnostics.Contracts.Panels;
using Hrot.Core.Mission;
using Hrot.Editor.AiShared.Shell;
using Hrot.Editor.Scenario;
using Hrot.Presentation.Behavior;
using Hrot.UI.Common.Facades;
using Hrot.UI.Common.Models;
using Hrot.UI.Common.Panels;
using Xunit;

namespace Hrot.Blueprints.Tests.Editor;

/// <summary>
/// ⭐⭐⭐ <b>U-obs-5 (group 6) — <c>ScenarioMissionView</c> converted to the <c>PanelSnapshot</c>
/// contract.</b> 📄 <c>docs/DESIGN_UI_Observability_Snapshot.md</c> §Example. ⭐ Reuses
/// <c>MissionPanel.BuildViewModel</c> verbatim (ruling 9) rather than re-modelling the panel's state —
/// see <c>ScenarioMissionView.BuildAndPublish</c>'s own remarks.
/// </summary>
[Collection(PanelSnapshotTestCollection.Name)]
public sealed class ScenarioMissionViewDumpsItsPanelTests : IDisposable
{
    public ScenarioMissionViewDumpsItsPanelTests()
    {
        PanelSnapshot.Clear();
        PanelSnapshot.CaptureEnabled = false;
    }

    public void Dispose()
    {
        PanelSnapshot.Clear();
        PanelSnapshot.CaptureEnabled = false;
    }

    private static string Addr(string idScope) => $"{idScope}/{ScenarioMissionViewDescriptor.ViewId}";

    private static MissionPanel NewPanel() => new(0, BehaviorUiSetup.CreateRegistry());

    private static readonly Entity One = new(41, 1);

    private sealed class RecordingMissions : IMissionEditorService
    {
        private readonly string[] _behaviors;
        public RecordingMissions(params string[] behaviors) => _behaviors = behaviors;
        public IReadOnlyList<string> GetAvailableBehaviors(long entityId) => _behaviors;
        public (MissionPlan? Plan, long Version) GetMissionSnapshot(long entityId) => (null, 0);
        public Task<MissionCommitResult> CommitMissionAsync(long id, MissionPlan plan, long baseVersion)
            => throw new NotSupportedException();
        public Task<MissionCommitResult> SendControlCommandAsync(long id, eMissionCommandType t, Guid taskId)
            => throw new NotSupportedException();
    }

    private sealed class NoPicking : IMapPickService
    {
        public Task<GeoPoint> PickLocationAsync(System.Threading.CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<int> PickEntityAsync(string[]? filterPresets = null, System.Threading.CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<IReadOnlyList<int>> PickAreaEntitiesAsync(string[]? filterPresets = null, System.Threading.CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    [Fact]
    public void FirstDraw_DeclaresItInstrumented_AtTheComposedAddress()
    {
        var view = new ScenarioMissionView(NewPanel(), () => new RecordingMissions(), () => new NoPicking(), _ => 0);
        var addr = Addr("host1");
        Assert.DoesNotContain(addr, PanelSnapshot.RegisteredPanels);   // ⛔ anti-vacuity

        view.SimulateDraw("host1");

        Assert.Contains(addr, PanelSnapshot.RegisteredPanels);
    }

    [Fact]
    public void AfterADraw_TheDumpCarriesThePanelsSelectedEntityId()
    {
        PanelSnapshot.CaptureEnabled = true;
        var view = new ScenarioMissionView(
            NewPanel(), () => new RecordingMissions("Patrol"), () => new NoPicking(), e => e == One ? 4242 : 0);

        view.Draw(DetailsContext.Empty("Editor") with { Entities = new[] { One } }, "host1");

        var stored = PanelSnapshot.TryGet(Addr("host1"));
        Assert.NotNull(stored);
        Assert.Equal(ScenarioMissionViewDescriptor.ViewId, stored!.PanelKind);
        Assert.Equal(4242, stored.Dump()["selectedEntityId"]!.GetValue<int>());
    }

    [Fact]
    public void WithCaptureOff_ProducesNothing_ButStaysRegistered()
    {
        var view = new ScenarioMissionView(NewPanel(), () => new RecordingMissions(), () => new NoPicking(), _ => 0);

        var vm = view.SimulateDraw("host1");

        Assert.Empty(PanelSnapshot.CapturedPanels);
        Assert.Contains(Addr("host1"), PanelSnapshot.RegisteredPanels);
        Assert.NotNull(vm);
    }

    [Fact]
    public void ADeclinedDraw_StillPublishesTheViewsCurrentPanelState()
    {
        PanelSnapshot.CaptureEnabled = true;
        var panel = NewPanel();
        panel.SelectedEntityId = 7;
        var view = new ScenarioMissionView(panel, () => new RecordingMissions(), () => new NoPicking(), _ => 4242);

        // ⭐ A multi-selection declines to draw (predicate territory) — capture must still run, per the
        // recipe: BUILD → CAPTURE happens before any of Draw's guards.
        view.Draw(DetailsContext.Empty("Editor") with { Entities = new[] { One, One } }, "host1");

        var stored = PanelSnapshot.TryGet(Addr("host1"));
        Assert.NotNull(stored);
        Assert.Equal(7, stored!.Dump()["selectedEntityId"]!.GetValue<int>());   // untouched — the guard skipped the write
    }
}
