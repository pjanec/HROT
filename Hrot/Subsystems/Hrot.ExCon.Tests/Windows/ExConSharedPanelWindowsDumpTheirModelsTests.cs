using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Diagnostics.Contracts.Panels;
using Fdp.Toolkit.DER;
using Hrot.Core.Mission;
using Hrot.ExCon.Panels;
using Hrot.ExCon.Services;
using Hrot.ExCon.Windows;
using Hrot.UI.Common.Facades;
using Hrot.UI.Common.Models;
using Hrot.UI.Common.Panels;
using Moq;
using Xunit;

namespace Hrot.ExCon.Tests.Windows;

/// <summary>
/// ⭐⭐⭐ <b>U-obs-5 — the ExCon-perspective hosts of the group-5 twin panels (plus
/// <c>DiagnosticsPanel</c>), converted to the <c>PanelSnapshot</c> contract.</b>
/// 📄 <c>docs/DESIGN_UI_Observability_Snapshot.md</c> §Example ·
/// <c>docs/blueprints/batches/QUEUE_Panel_Observability_Sweep.md</c> group 5/6. ⚠⚠ This closes the
/// "not yet converted" gap the Editor-side commits reported, and also corrects a false negative: an
/// earlier commit claimed <c>MissionPanel</c> has no ExCon host — it does
/// (<see cref="ExConMissionWindow"/>), and both hosts now cite <c>PanelIds.Mission</c>.
/// </summary>
[Collection(PanelSnapshotTestCollection.Name)]
public sealed class ExConSharedPanelWindowsDumpTheirModelsTests : IDisposable
{
    public ExConSharedPanelWindowsDumpTheirModelsTests()
    {
        PanelSnapshot.Clear();
        PanelSnapshot.CaptureEnabled = false;
    }

    public void Dispose()
    {
        PanelSnapshot.Clear();
        PanelSnapshot.CaptureEnabled = false;
    }

    private sealed class FakeMapConfigController : IMapConfigController
    {
        public MapLayerState GetCurrentConfig() => new(true, true, true, true, true, true, false);
        public void ApplyConfig(MapLayerState config) { }
    }

    private sealed class FakeSpawnController : ISpawnController
    {
        public void StartPlacementMode(long tkbType, string? initialPropertiesJson = null) { }
        public void StartAreaAuthoringMode(string styleOverrideJson = "") { }
        public void StartRouteAuthoringMode() { }
    }

    // ⚠ Two different interfaces of this name exist: Hrot.ExCon.Services (used by IExConLogic) and
    // Hrot.UI.Common.Facades (used by MissionPanel/ExConMissionWindow's ctor). Qualify explicitly.
    private sealed class FakeMissionEditorService : Hrot.UI.Common.Facades.IMissionEditorService
    {
        public IReadOnlyList<string> GetAvailableBehaviors(long entityId) => Array.Empty<string>();
        public (MissionPlan? Plan, long Version) GetMissionSnapshot(long entityId) => (null, 0);
        public Task<MissionCommitResult> CommitMissionAsync(long entityId, MissionPlan plan, long baseVersion)
            => Task.FromResult(new MissionCommitResult(true, 1, null));
        public Task<MissionCommitResult> SendControlCommandAsync(long entityId, eMissionCommandType type, Guid taskId)
            => Task.FromResult(new MissionCommitResult(true, 1, null));
    }

    private sealed class FakeMapPickService : Hrot.UI.Common.Facades.IMapPickService
    {
        public Task<GeoPoint> PickLocationAsync(CancellationToken ct = default) => new TaskCompletionSource<GeoPoint>().Task;
        public Task<int> PickEntityAsync(string[]? filterPresets = null, CancellationToken ct = default) => new TaskCompletionSource<int>().Task;
        public Task<IReadOnlyList<int>> PickAreaEntitiesAsync(string[]? filterPresets = null, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<int>>(Array.Empty<int>());
    }

    private static IExConLogic MakeLogic()
    {
        var mock = new Mock<IExConLogic>();
        mock.Setup(l => l.Repo).Returns(new DerRepo());
        mock.Setup(l => l.TransactionManager).Returns(Mock.Of<IRequestTransactionManager>(
            m => m.GetPendingRequests() == Array.Empty<PendingRequest>()));
        return mock.Object;
    }

    // ── ConfigPanel ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ConfigWindow_RegistersUnderTheSharedConfigKind()
    {
        PanelSnapshot.CaptureEnabled = true;
        var window = new ExConConfigWindow(new ConfigPanel(), new FakeMapConfigController());

        window.SimulateDrawClientArea();

        var vm = PanelSnapshot.TryGet("excon_config");
        Assert.NotNull(vm);
        Assert.Equal(PanelIds.Config, vm!.PanelKind);
    }

    // ── MissionPanel — the corrected finding ─────────────────────────────────────────────────

    [Fact]
    public void MissionWindow_RegistersUnderTheSharedMissionKind_AndDumpsTheSelectedEntity()
    {
        PanelSnapshot.CaptureEnabled = true;
        var panel = new MissionPanel { SelectedEntityId = 7 };
        var window = new ExConMissionWindow(panel, new FakeMissionEditorService(), new FakeMapPickService());

        window.SimulateDrawClientArea();

        var vm = PanelSnapshot.TryGet("excon_mission");
        Assert.NotNull(vm);
        Assert.Equal(PanelIds.Mission, vm!.PanelKind);
        Assert.Equal(7, vm.Dump()["selectedEntityId"]!.GetValue<int>());
    }

    // ── SpawnerPanel ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SpawnerWindow_RegistersUnderTheSharedSpawnerKind()
    {
        PanelSnapshot.CaptureEnabled = true;
        var window = new ExConSpawnerWindow(new SpawnerPanel(), new FakeSpawnController());

        window.SimulateDrawClientArea();

        var vm = PanelSnapshot.TryGet("excon_spawner");
        Assert.NotNull(vm);
        Assert.Equal(PanelIds.Spawner, vm!.PanelKind);
    }

    // ── DiagnosticsPanel ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void DiagnosticsWindow_DeclaresItInstrumented_AndDumpsTheEntityCount()
    {
        Assert.DoesNotContain("excon_diagnostics", PanelSnapshot.RegisteredPanels);   // ⛔ anti-vacuity

        PanelSnapshot.CaptureEnabled = true;
        var window = new ExConDiagnosticsWindow(new DiagnosticsPanel(), MakeLogic());

        Assert.Contains("excon_diagnostics", PanelSnapshot.RegisteredPanels);
        window.SimulateDrawClientArea();

        var vm = PanelSnapshot.TryGet("excon_diagnostics");
        Assert.NotNull(vm);
        Assert.Equal(ExConDiagnosticsWindow.Kind, vm!.PanelKind);
        Assert.Equal(0, vm.Dump()["entityCount"]!.GetValue<int>());
    }

    // ── capture-off publishes nothing, for all four ──────────────────────────────────────────

    [Fact]
    public void WithCaptureOff_NoneOfTheFourPublish()
    {
        new ExConConfigWindow(new ConfigPanel(), new FakeMapConfigController()).SimulateDrawClientArea();
        new ExConMissionWindow(new MissionPanel(), new FakeMissionEditorService(), new FakeMapPickService()).SimulateDrawClientArea();
        new ExConSpawnerWindow(new SpawnerPanel(), new FakeSpawnController()).SimulateDrawClientArea();
        new ExConDiagnosticsWindow(new DiagnosticsPanel(), MakeLogic()).SimulateDrawClientArea();

        Assert.Empty(PanelSnapshot.CapturedPanels);
        Assert.Contains("excon_config", PanelSnapshot.RegisteredPanels);
        Assert.Contains("excon_mission", PanelSnapshot.RegisteredPanels);
        Assert.Contains("excon_spawner", PanelSnapshot.RegisteredPanels);
        Assert.Contains("excon_diagnostics", PanelSnapshot.RegisteredPanels);
    }
}
