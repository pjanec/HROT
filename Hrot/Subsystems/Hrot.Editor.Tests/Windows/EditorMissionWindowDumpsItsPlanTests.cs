using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Diagnostics.Contracts.Panels;
using Hrot.Core.Mission;
using Hrot.Editor.Windows;
using Hrot.UI.Common.Facades;
using Hrot.UI.Common.Models;
using Hrot.UI.Common.Panels;
using Xunit;

namespace Hrot.Editor.Tests.Windows;

/// <summary>
/// ⭐⭐⭐ <b>U-obs-5 — <c>MissionPanel</c>/<c>EditorMissionWindow</c> converted to the
/// <c>PanelSnapshot</c> contract.</b> 📄 <c>docs/DESIGN_UI_Observability_Snapshot.md</c> §Example ·
/// <c>docs/blueprints/batches/QUEUE_Panel_Observability_Sweep.md</c> group 5 (not a twin — measured
/// zero ExCon host for this panel, so the kind stays a local literal on the window).
/// </summary>
[Collection(PanelSnapshotTestCollection.Name)]
public sealed class EditorMissionWindowDumpsItsPlanTests : IDisposable
{
    public EditorMissionWindowDumpsItsPlanTests()
    {
        PanelSnapshot.Clear();
        PanelSnapshot.CaptureEnabled = false;
    }

    public void Dispose()
    {
        PanelSnapshot.Clear();
        PanelSnapshot.CaptureEnabled = false;
    }

    private sealed class FakeMissionEditorService : IMissionEditorService
    {
        public IReadOnlyList<string> GetAvailableBehaviors(long entityId) => Array.Empty<string>();
        public (MissionPlan? Plan, long Version) GetMissionSnapshot(long entityId) => (null, 0);
        public Task<MissionCommitResult> CommitMissionAsync(long entityId, MissionPlan plan, long baseVersion)
            => Task.FromResult(new MissionCommitResult(true, 1, null));
        public Task<MissionCommitResult> SendControlCommandAsync(long entityId, eMissionCommandType type, Guid taskId)
            => Task.FromResult(new MissionCommitResult(true, 1, null));
    }

    private sealed class FakeMapPickService : IMapPickService
    {
        public Task<GeoPoint> PickLocationAsync(CancellationToken ct = default) => new TaskCompletionSource<GeoPoint>().Task;
        public Task<int> PickEntityAsync(string[]? filterPresets = null, CancellationToken ct = default) => new TaskCompletionSource<int>().Task;
        public Task<IReadOnlyList<int>> PickAreaEntitiesAsync(string[]? filterPresets = null, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<int>>(Array.Empty<int>());
    }

    // ⭐ CE-061 — the wrapper is the SHARED one now; the ids/titles are arguments, so this rail
//   pins the editor's historical id explicitly instead of relying on a hard-coded ctor.
    private static Hrot.Presentation.Windows.MissionPanelWindow MakeWindow(MissionPanel panel) =>
        new Hrot.Presentation.Windows.MissionPanelWindow(
            panel, new FakeMissionEditorService(), new FakeMapPickService(),
            Hrot.Presentation.Windows.ScenarioPanelWindowIds.EditorMission, "Scenario", default);

    // ── Rail 1 — instrumented at construction, on the PRODUCTION object ─────────────────────────

    [Fact]
    public void ConstructingTheWindow_DeclaresItInstrumented_BeforeItHasEverDrawn()
    {
        Assert.DoesNotContain("editor_mission", PanelSnapshot.RegisteredPanels);   // ⛔ anti-vacuity

        var window = MakeWindow(new MissionPanel());

        Assert.Contains("editor_mission", PanelSnapshot.RegisteredPanels);
        Assert.DoesNotContain("editor_mission", PanelSnapshot.CapturedPanels);
        Assert.Null(PanelSnapshot.TryGet("editor_mission"));
        Assert.NotNull(window);
    }

    // ── Rail 2 — the dump carries a real field ───────────────────────────────────────────────────

    [Fact]
    public void AfterABuild_TheDumpCarriesTheSelectedEntity()
    {
        PanelSnapshot.CaptureEnabled = true;
        var panel = new MissionPanel { SelectedEntityId = 42 };
        var window = MakeWindow(panel);

        window.SimulateDrawClientArea();

        var vm = PanelSnapshot.TryGet("editor_mission");
        Assert.NotNull(vm);
        Assert.Equal("editor_mission", vm!.PanelId);
        Assert.Equal(Hrot.Presentation.Windows.MissionPanelWindow.Kind, vm.PanelKind);
        Assert.Equal(42, vm.Dump()["selectedEntityId"]!.GetValue<int>());
    }

    // ── Rail 3 — the flag gates the DUMP, not the BUILD ──────────────────────────────────────────

    [Fact]
    public void WithCaptureOff_TheProductionPathPublishesNothing()
    {
        var window = MakeWindow(new MissionPanel());   // CaptureEnabled stays false

        var vm = window.SimulateDrawClientArea();

        Assert.Empty(PanelSnapshot.CapturedPanels);
        Assert.Contains("editor_mission", PanelSnapshot.RegisteredPanels);
        Assert.NotNull(vm);   // ⭐ the BUILD is unaffected by the flag
    }
}
