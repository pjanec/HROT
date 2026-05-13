using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Scheduling;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Systems;
using Fdp.Toolkit.Vis2D.Components;
using Hrot.Common;
using Hrot.Common.Diagnostics.Gizmos;
using Hrot.ClusterRunner.Systems;
using Hrot.ClusterRunner.Tests.Mocks;
using Xunit;

namespace Hrot.ClusterRunner.Tests;

/// <summary>
/// Tests for WM-S701 (<see cref="TogglePerspectiveEvent"/>),
/// WM-S702 (<see cref="ActivePerspective"/>), and
/// WM-S703 (<see cref="PerspectiveCoordinatorSystem"/>).
/// </summary>
public class PerspectiveCoordinatorSystemTests
{
    // ── Helpers ────────────────────────────────────────────────────────────────

    private static SubsystemOrchestrator CreateHeadlessOrchestrator(params ISubsystem[] subsystems)
        => new SubsystemOrchestrator(subsystems, new RunnerOptions { Headless = true });

    private static IReadOnlyDictionary<string, string> PerspMap(params (string persp, string sub)[] pairs)
    {
        var d = new Dictionary<string, string>();
        foreach (var (p, s) in pairs) d[p] = s;
        return d;
    }

    private static MapCamera MakeCamera(float zoom, float x, float y)
    {
        var cam = new MapCamera();
        var src = new MapCamera();
        src.InnerCamera.Zoom   = zoom;
        src.InnerCamera.Target = new Vector2(x, y);
        cam.SnapTo(src);
        return cam;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // WM-S701: TogglePerspectiveEvent (deeper coverage in TogglePerspectiveEventTests)
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void TogglePerspectiveEvent_StoresOldAndNewPerspective()
    {
        var evt = new TogglePerspectiveEvent("IG", "SimHost");

        Assert.Equal("IG",      evt.OldPerspective);
        Assert.Equal("SimHost", evt.NewPerspective);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // WM-S702: ActivePerspective managed ECS component
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void ActivePerspective_DefaultName_IsEmpty()
    {
        var comp = new ActivePerspective();

        Assert.Equal(string.Empty, comp.Name);
    }

    [Fact]
    public void ActivePerspective_NameCanBeSetAndRead()
    {
        var comp = new ActivePerspective { Name = "IG" };

        Assert.Equal("IG", comp.Name);
    }

    [Fact]
    public void ActivePerspective_IsSealed()
    {
        Assert.True(typeof(ActivePerspective).IsSealed);
    }

    [Fact]
    public void ActivePerspective_IsClass_NotValueType()
    {
        Assert.False(typeof(ActivePerspective).IsValueType);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // WM-S703: PerspectiveCoordinatorSystem
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Coordinator_InitialCurrentPerspective_IsEmpty()
    {
        var orch = CreateHeadlessOrchestrator();
        orch.Initialize();
        var coordinator = new PerspectiveCoordinatorSystem(orch, PerspMap());

        Assert.Equal(string.Empty, coordinator.CurrentPerspective);
    }

    [Fact]
    public void Coordinator_ProcessEvent_UpdatesCurrentPerspective()
    {
        var sub  = new MockSubsystem("IG");
        var orch = CreateHeadlessOrchestrator(sub);
        orch.Initialize();
        var coordinator = new PerspectiveCoordinatorSystem(orch, PerspMap(("IG", "IG")));

        coordinator.Enqueue(new TogglePerspectiveEvent("Default", "IG"));
        coordinator.ProcessPendingEvents();

        Assert.Equal("IG", coordinator.CurrentPerspective);
    }

    [Fact]
    public void Coordinator_UnknownPerspective_UpdatesCurrentPerspectiveStill()
    {
        var orch       = CreateHeadlessOrchestrator();
        orch.Initialize();
        var coordinator = new PerspectiveCoordinatorSystem(orch, PerspMap());

        coordinator.Enqueue(new TogglePerspectiveEvent("Default", "Unknown"));
        coordinator.ProcessPendingEvents(); // must not throw

        Assert.Equal("Unknown", coordinator.CurrentPerspective);
    }

    [Fact]
    public void Coordinator_MultipleEvents_ProcessedInOrder()
    {
        var orch       = CreateHeadlessOrchestrator();
        orch.Initialize();
        var coordinator = new PerspectiveCoordinatorSystem(orch, PerspMap());

        coordinator.Enqueue(new TogglePerspectiveEvent("Default", "IG"));
        coordinator.Enqueue(new TogglePerspectiveEvent("IG",      "SimHost"));
        coordinator.ProcessPendingEvents();

        Assert.Equal("SimHost", coordinator.CurrentPerspective);
    }

    [Fact]
    public void Coordinator_EmptyQueue_ProcessPendingEvents_IsNoOp()
    {
        var orch       = CreateHeadlessOrchestrator();
        orch.Initialize();
        var coordinator = new PerspectiveCoordinatorSystem(orch, PerspMap());

        coordinator.ProcessPendingEvents(); // must not throw

        Assert.Equal(string.Empty, coordinator.CurrentPerspective);
    }

    [Fact]
    public void Coordinator_KnownPerspective_CallsSwitchMapOwner_VerifiedViaCameraSync()
    {
        // Camera sync is only performed by SwitchMapOwner — if the SimHost camera
        // ends up with IG's zoom after the coordinator processes the event,
        // SwitchMapOwner was indeed called.
        var igCam  = MakeCamera(zoom: 3.0f, x: 100f, y: 200f);
        var shCam  = MakeCamera(zoom: 1.0f, x: 0f,   y: 0f);

        var ig      = new MapCameraSubsystemMock("IG",      igCam);
        var simHost = new MapCameraSubsystemMock("SimHost", shCam);

        var orch = CreateHeadlessOrchestrator(ig, simHost);
        orch.Initialize(); // first IMapCameraProvider (IG) is active owner

        var coordinator = new PerspectiveCoordinatorSystem(
            orch, PerspMap(("SimHost", "SimHost")));

        coordinator.Enqueue(new TogglePerspectiveEvent("IG", "SimHost"));
        coordinator.ProcessPendingEvents();

        // SwitchMapOwner snaps SimHost camera to IG camera state.
        Assert.Equal(igCam.Zoom,   shCam.Zoom,   precision: 4);
        Assert.Equal(igCam.Target, shCam.Target);
    }
}

// ==========================================================================
// GZH-014: Perspective-aware GizmoExecutionController listener transfer
// ==========================================================================

/// <summary>
/// Stub <see cref="IGizmoControllable"/> backed by a real
/// <see cref="GizmoExecutionController"/> so tests can assert
/// <see cref="GizmoExecutionController.ListenerCount"/> changes.
/// </summary>
internal sealed class StubGizmoControllable : IGizmoControllable
{
    public GizmoExecutionController? GizmoController { get; }

    public StubGizmoControllable(GizmoExecutionController ctrl)
    {
        GizmoController = ctrl;
    }
}

public class GZH014_Tests
{
    // ── Helper: build a minimal GizmoExecutionController without a live ECS loop. ──

    private static GizmoExecutionController MakeController()
    {
        var buf       = new DebugPrimitiveBuffer();
        var globalMgr = new GlobalGizmoManager(buf);
        var registry  = new GizmoRegistry();
        var ddSys     = new DataDrivenGizmoSystem(registry, buf);
        var group     = new TogglablePostSimulationGroup("GizmoExecution");
        group.Enabled = false;
        return new GizmoExecutionController(group, globalMgr, ddSys);
    }

    private static SubsystemOrchestrator CreateHeadlessOrchestrator(params ISubsystem[] subsystems)
        => new SubsystemOrchestrator(subsystems, new RunnerOptions { Headless = true });

    // GZH014_1: Perspective switch transfers listener count between subsystems.
    [Fact]
    public void GZH014_1_PerspectiveSwitch_TransfersGizmoListenerCount()
    {
        var ctrlA = MakeController();
        var ctrlB = MakeController();

        var subA = new MockSubsystem("SubA");
        var subB = new MockSubsystem("SubB");

        var orch = CreateHeadlessOrchestrator(subA, subB);
        orch.Initialize();

        var controllables = new Dictionary<string, IGizmoControllable>
        {
            ["SubA"] = new StubGizmoControllable(ctrlA),
            ["SubB"] = new StubGizmoControllable(ctrlB),
        };

        var coordinator = new PerspectiveCoordinatorSystem(
            orch,
            new Dictionary<string, string> { ["SubA"] = "SubA", ["SubB"] = "SubB" },
            controllables);

        // Simulate opening the local window on SubA (no outgoing perspective yet).
        coordinator.Enqueue(new TogglePerspectiveEvent("", "SubA"));
        coordinator.ProcessPendingEvents();
        Assert.Equal(1, ctrlA.ListenerCount);
        Assert.Equal(0, ctrlB.ListenerCount);

        // Switch to SubB: SubA loses its listener, SubB gains one.
        coordinator.Enqueue(new TogglePerspectiveEvent("SubA", "SubB"));
        coordinator.ProcessPendingEvents();
        Assert.Equal(0, ctrlA.ListenerCount);
        Assert.Equal(1, ctrlB.ListenerCount);
    }

    // GZH014_2: Unknown perspective in gizmoControllables is silently ignored.
    [Fact]
    public void GZH014_2_UnknownNewPerspective_IsIgnored_NoException()
    {
        var ctrlA = MakeController();
        var subA  = new MockSubsystem("SubA");

        var orch = CreateHeadlessOrchestrator(subA);
        orch.Initialize();

        var controllables = new Dictionary<string, IGizmoControllable>
        {
            ["SubA"] = new StubGizmoControllable(ctrlA),
        };

        var coordinator = new PerspectiveCoordinatorSystem(
            orch,
            new Dictionary<string, string> { ["SubA"] = "SubA" },
            controllables);

        // SubA is the current perspective; switch to an unknown name.
        coordinator.Enqueue(new TogglePerspectiveEvent("SubA", "UnknownPersp"));

        // Must not throw even though "UnknownPersp" is not in the perspective map.
        // The entire listener-transfer block is skipped (new perspective unknown),
        // so neither RemoveListener nor AddListener fires.
        var ex = Record.Exception(() => coordinator.ProcessPendingEvents());
        Assert.Null(ex);
        Assert.Equal(0, ctrlA.ListenerCount);
    }
}
