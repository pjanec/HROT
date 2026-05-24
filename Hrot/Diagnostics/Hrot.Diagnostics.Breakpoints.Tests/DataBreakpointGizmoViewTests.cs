using System;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Interaction;
using Fdp.Toolkit.Diagnostics.Gizmos.Systems;
using Fdp.Toolkit.ReplayBrowser.Search;
using Hrot.Blueprints.Core.Debug;
using Hrot.Diagnostics.Breakpoints;

namespace Hrot.Diagnostics.Breakpoints.Tests;

// ---- Test-only ECS component (ID 213) for P3T1 ---------------------------

/// <summary>Test component for gizmo view routing tests (ID 213).</summary>
[ComponentId(213)]
internal struct TestHealthP3 { public float Value; }

// ---------------------------------------------------------------------------
// DataBreakpointGizmoViewTests  (UBP-P3T1)
// ---------------------------------------------------------------------------

/// <summary>
/// Integration tests verifying that the gizmo systems route the correct view to
/// <see cref="IEntityStatefulGizmo.UpdateAndDraw"/> based on the pause state of
/// <see cref="DataBreakpointManager"/>.
/// </summary>
[Collection("ComponentRegistry")]
public sealed class DataBreakpointGizmoViewTests
{
    // A minimal gizmo that captures the view passed to UpdateAndDraw.
    private sealed class ViewCapturingGizmo : IEntityStatefulGizmo
    {
        public ISimulationView? LastView;
        public bool RequiresExclusiveFocus => false;
        public bool IsFocused { get; private set; }
        public void SetFocus(bool v) => IsFocused = v;
        public void UpdateAndDraw(ISimulationView view, float dt, IDebugDrawBuilder draw)
            => LastView = view;
        public void Dispose() { }
        public void OnInteractionStarted(GizmoPickToken token, Vector3 worldPos) { }
        public void OnDragUpdate(Vector3 worldPos) { }
        public void OnCommit(Vector3 worldPos) { }
        public void OnCancel() { }
        public void OnMenuAction(int actionId) { }
        public void OnMouseEvent(MapMouseButton button, bool isPressed, Vector3 worldPos) { }
        public void OnKeyEvent(MapKeyboardKey key, bool isPressed) { }
    }

    // ------------------------------------------------------------------ //
    // UBP-P3T1: Gizmo receives correct view depending on pause state      //
    // ------------------------------------------------------------------ //

    /// <summary>
    /// Before pause the gizmo receives the live repo; while paused it receives the
    /// pre-tick snapshot; after resume it returns to the live repo.
    /// </summary>
    [Fact]
    public void Gizmo_RendersAgainstActiveView_ReflectsPauseState()
    {
        ComponentTypeRegistry.Clear();

        var liveRepo = new EntityRepository();
        var preTick  = new EntityRepository();
        var tc       = new MockDebugTimeController();
        var provider = new DebugSnapshotProvider(preTick);
        var manager  = new DataBreakpointManager(liveRepo, preTick, provider, tc);

        // Before pause: ActiveView must be the live repo.
        Assert.Equal((ISimulationView)liveRepo, manager.ActiveView);

        // Wire a ViewCapturingGizmo into the gizmo system with the breakpoint manager.
        var drawBuffer  = new DebugPrimitiveBuffer();
        var registry    = new GizmoRegistry();
        var gizmoSystem = new DataDrivenGizmoSystem(registry, drawBuffer,
            breakpointManager: manager);

        var entity = liveRepo.CreateEntity();
        // Simulate the pre-tick snapshot having been taken before this tick: both
        // repos now hold the entity, so the SyncFrom rewind in OnHit leaves it alive.
        preTick.SyncFrom(liveRepo);
        var capturingGizmo = new ViewCapturingGizmo();
        gizmoSystem.ActivateGizmo(entity, capturingGizmo);

        // Tick the gizmo system while NOT paused -> gizmo sees live repo.
        gizmoSystem.Execute(liveRepo, 0f);
        Assert.Equal((ISimulationView)liveRepo, capturingGizmo.LastView);

        // Trigger a pause via lifecycle predicate on the same entity.
        var bpSystem = new DataBreakpointSystem(manager);
        manager.Add(new Breakpoint
        {
            Id                  = BreakpointId.Invalid,
            Enabled             = true,
            OccurrenceThreshold = 1,
            DisplayName         = "LifecycleP3",
            Condition           = new LifecyclePredicateDto
            {
                IdentifierType = EntityIdentifierType.EcsHandle,
                TargetValue    = entity.Index.ToString()
            }
        });

        // Execute the breakpoint system; lifecycle birth hit -> manager pauses.
        bpSystem.Execute(liveRepo, 0f);
        Assert.True(manager.IsPaused);

        // Tick the gizmo system while PAUSED -> gizmo sees pre-tick snapshot.
        gizmoSystem.Execute(liveRepo, 0f);
        Assert.Equal((ISimulationView)manager.PreTickSnapshot, capturingGizmo.LastView);
        Assert.NotEqual((ISimulationView)liveRepo, capturingGizmo.LastView);

        // Resume and tick again -> gizmo returns to live repo.
        manager.RequestContinue();
        gizmoSystem.Execute(liveRepo, 0f);
        Assert.Equal((ISimulationView)liveRepo, capturingGizmo.LastView);
    }
}
