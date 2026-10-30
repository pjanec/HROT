using System;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Runner;
using Hrot.Editor;
using Xunit;

namespace Hrot.Editor.Tests;

/// <summary>
/// ⭐⭐ Q86 (R-215) — the editor runs the ONE orchestrator core the cluster runs, so a scenario load resets ITS
/// clock to 0 through the same load handler. ⛔ Before, the editor ran a hand-built copy of the orchestrator with no
/// load handler at all, and a load left the clock at whatever time the last world reached (CE-122).
/// 📄 docs/blueprints/Architect_Question_86_Editor_Runs_The_Orchestrator_Core.md §5 S5.
/// </summary>
public sealed class TheEditorRunsTheOrchestratorCoreTests
{
    [Fact(Timeout = 60_000)]
    public void Q86S5_AnEditorScenarioLoad_ResetsTheEditorsClockToZero()
    {
        var editor = new EditorSubsystem();
        editor.Initialize(new SubsystemConfig { Headless = true });
        try
        {
            var clock = editor.TimeControllerForTest!;
            var bus   = editor.OrchestrationBusForTest!;

            clock.SwitchToContinuous();
            editor.Update(0.016f);
            System.Threading.Thread.Sleep(200);
            editor.Update(0.016f);
            Assert.True(clock.GetCurrentState().TotalTime > 0.05, $"the clock must be running first ({clock.GetCurrentState().TotalTime})");

            bus.PublishManaged(new TransitionStateIntent
            {
                TransactionId = Guid.NewGuid(),
                TargetState   = ClusterState.OperatingLive,
                ScenarioId    = "q86s5_" + Guid.NewGuid().ToString("N"),   // no saved context ⇒ the fresh-scenario path: t = 0
                TimeMode      = "Deterministic",
            });
            // The load handler asks for the jump in frame 0; the editor updates its clock BEFORE it swaps the
            // orchestration bus (EditorSubsystem Update), so the request reaches the clock in frame 2 — the same
            // path, and the same latency, as the toolbar's own pause request. Measured: t = 0.000 from frame 2.
            var trace = new System.Text.StringBuilder();
            for (int frame = 0; frame < 3; frame++)
            {
                editor.Update(0.016f);
                trace.Append($" f{frame}:t={clock.GetCurrentState().TotalTime:F3}");
            }

            Assert.True(clock.GetCurrentState().TotalTime < 0.05,
                $"an editor load must reset the clock to 0 (was {clock.GetCurrentState().TotalTime}); trace:{trace}");
        }
        finally
        {
            editor.Shutdown();
        }
    }

    /// <summary>
    /// ⭐⭐ <c>CE-4087</c> (S3) — the editor plays every simulation role in-process, so its OWN master's load gate (a scenario load needs a
    /// Brain and a MuscleGround node) is satisfied at once: the editor neither waits for nor refuses itself. Asserted on the CONSTRUCTED
    /// editor (the silent-default rule): its master carries <see cref="EditorCapabilities.DefaultRole"/> as <c>LocalRoles</c>, and a
    /// scenario load requested in its very first frames is not held.
    /// </summary>
    [Fact(Timeout = 60_000)]
    public void CE4087_TheEditorsOwnMasterCountsItsInProcessRoles_SoItsLoadIsNeverHeld()
    {
        var editor = new EditorSubsystem();
        editor.Initialize(new SubsystemConfig { Headless = true });
        try
        {
            var master = editor.ClusterMasterForTest!;
            Assert.Equal(EditorCapabilities.DefaultRole, master.LocalRoles);

            editor.OrchestrationBusForTest!.PublishManaged(new TransitionStateIntent
            {
                TransactionId = Guid.NewGuid(),
                TargetState   = ClusterState.OperatingLive,
                ScenarioId    = "ce4087_" + Guid.NewGuid().ToString("N"),
            });
            editor.Update(0.016f);
            editor.Update(0.016f);

            Assert.Null(master.HeldLoadRequestId);   // not waiting …
            Assert.Null(master.LoadNotice);          // … and nothing to read about a refusal
        }
        finally
        {
            editor.Shutdown();
        }
    }

    /// <summary>
    /// ⭐ <c>CE-3330</c> — the editor has NO timeline of its own: on a real headless editor its live index IS its node context's
    /// (<c>Timeline</c>, E-1); frames of the editor (<c>Update</c>, the clock running) step it, so a unit that gains a network id in its world
    /// lands in that index (E-2, red-proof: the editor's <c>_node?.StepTimeline()</c> behind a runtime-false guard); and the access its
    /// <c>/timeline</c> attach serves (<c>TimelineOf(node)</c>) reads that same source (E-3: the attach's access, not the HTTP service — that exists only with HROT_DEBUG_API_PORT).
    /// </summary>
    [Fact(Timeout = 60_000)]
    public void CE3330_Node_TheEditorsLiveTimelineIsItsNodeContexts_SteppedByItsFrames_AndServedOnItsDebugSurface()
    {
        var editor = new EditorSubsystem();
        editor.Initialize(new SubsystemConfig { Headless = true });
        try
        {
            var node = editor.NodeForTest!;
            Assert.NotNull(editor.LiveTimeline);                                              // E-1
            Assert.Same(node.Timeline, editor.LiveTimeline);

            var world = node.World;
            if (!world.IsComponentTypeRegistered<Fdp.Toolkit.Replication.Components.NetworkIdentity>())
                world.RegisterComponent<Fdp.Toolkit.Replication.Components.NetworkIdentity>();
            editor.TimeControllerForTest!.SwitchToContinuous();
            editor.Update(0.016f);
            System.Threading.Thread.Sleep(50);
            editor.Update(0.016f);
            Assert.True(editor.LiveTimeline!.Frames > 0, "the editor's frames must be indexing before the event happens");

            var unit = world.CreateEntity();                                                  // E-2: an event between two editor frames
            world.AddComponent(unit, new Fdp.Toolkit.Replication.Components.NetworkIdentity(31337));
            var spawned = System.Array.Empty<Fdp.Toolkit.ReplayBrowser.Timeline.TimelineEvent>();
            for (int i = 0; i < 40 && spawned.Length == 0; i++)
            {
                System.Threading.Thread.Sleep(80);                                            // the snapshot publishes at most every quarter second
                editor.Update(0.016f);
                spawned = System.Linq.Enumerable.ToArray(editor.LiveTimeline.Index.Lane(Fdp.Toolkit.Replication.ReplicationTimelineLanes.Spawned.Id));
            }
            var hit = System.Linq.Enumerable.SingleOrDefault(spawned, e => e.Subject == 31337);
            Assert.True(hit.Subject == 31337, "the unit that gained a network id must reach the editor's live timeline index");

            // E-3: the editor's debug service exists only when HROT_DEBUG_API_PORT starts its HTTP host, so it is not constructed here; its
            //   /timeline attach is `_node is {} n ? EcsNodeDebugProvider.TimelineOf(n) : null` — the access asserted here is that very object.
            Assert.Same(editor.LiveTimeline, Hrot.Presentation.DebugApi.EcsNodeDebugProvider.TimelineOf(node).Source());
        }
        finally
        {
            editor.Shutdown();
        }
    }

    /// <summary>
    /// ⭐ <c>CE-4054</c> — <c>/status</c> reports <c>inPreview</c> from the editor's preview controller, and that controller says "in preview" for
    /// ONE cluster state only: the dry run's operating state. Not Live, not Replay, not Review (nor the loading/unloading states around them).
    /// 🔴 Red-proof: the comparison widened to also hold in <c>OperatingLive</c> (behind a runtime-true guard) ⇒ the Live row fails.
    /// </summary>
    [Fact]
    public void CE4054_InPreview_IsTrueOnlyInADryRun_NotInLiveReplayOrReview()
    {
        foreach (var state in Enum.GetValues<ClusterState>())
        {
            var bus = new Fdp.Core.FdpEventBus();
            var controller = new EditorSubsystem.EditorPreviewController(
                new Fdp.Toolkit.Orchestration.Handlers.ReferencePreviewHandler(null), () => bus, () => state,
                new Fdp.Toolkit.Time.IntentTimeCommands(bus));
            Assert.Equal(state == ClusterState.OperatingPreview, controller.IsInPreviewMode);
        }
    }

    /// <summary>⭐ <c>CE-4054</c> — on a real headless editor (Idle / Edit, no dry run) the controller <c>/status</c> reads says "not in preview".</summary>
    [Fact(Timeout = 60_000)]
    public void CE4054_AHeadlessEditorOutsideADryRun_IsNotInPreview()
    {
        var editor = new EditorSubsystem();
        editor.Initialize(new SubsystemConfig { Headless = true });
        try { Assert.False(editor.PreviewControllerForTest!.IsInPreviewMode); }
        finally { editor.Shutdown(); }
    }

    /// <summary>The editor kernel's systems in EXECUTION order, as type names.</summary>
    private static System.Collections.Generic.List<string> SystemSequence(EditorSubsystem editor)
    {
        var seq = new System.Collections.Generic.List<string>();
        foreach (var sys in editor.NodeForTest!.Kernel.SystemScheduler.GetAllSystems()) seq.Add(sys.GetType().FullName ?? sys.GetType().Name);
        return seq;
    }

    /// <summary>
    /// ⭐ <c>CE-1446</c> B2 (R-3074 decisions B, C) — <b>the editor is an OFFLINE node on the shared bootstrapper</b>: no DDS participant (a Brain with none — decision B, nothing faked), its own
    /// clock (Standalone ⇒ a master-type controller), and ONE slave (the builder's, ticked by the shared frame — not a kernel module).
    /// 🔴 Red-proof: the <c>SwitchToDeterministic</c> line removed from <c>EditorNodeBootstrapper.BuildContext</c> ⇒ the authoring-starts-paused row.
    /// </summary>
    [Fact(Timeout = 60_000)]
    public void CE1446B2_TheEditorIsAnOfflineStandaloneNode_WithOneSlave()
    {
        var editor = new EditorSubsystem();
        editor.Initialize(new SubsystemConfig { Headless = true });
        try
        {
            var node = editor.NodeForTest!;
            Assert.Null(node.Participant);
            Assert.Equal(Fdp.Toolkit.Time.Controllers.TimeRole.Standalone, node.TimeRole);
            Assert.IsType<Fdp.Toolkit.Time.Controllers.MasterSyncController>(node.TimeController);
            Assert.Null(node.SlaveTranslator);
            // authoring starts PAUSED (Deterministic: dt == 0 every frame): frames over real time do not advance the clock
            var clock = editor.TimeControllerForTest!;
            for (int i = 0; i < 3; i++) { editor.Update(0.016f); System.Threading.Thread.Sleep(80); }
            Assert.True(clock.GetCurrentState().TotalTime < 0.001, $"a fresh editor must not advance its clock ({clock.GetCurrentState().TotalTime})");
        }
        finally { editor.Shutdown(); }
    }

    /// <summary>
    /// ⭐ <c>CE-1446</c> B2 (decision C) — <b>the editor's frame order, pinned.</b> The editor boots through <c>SharedApplicationBootstrapper</c> (the base's phase order) and ticks through the
    /// shared frame, so the kernel's systems run in the base's order. 📐 Measured against the inline build: the editor gains 5 systems — the Geographic module's two (CoordinateTransform,
    /// GeodeticSmoothing; the base registers <c>BaseModules</c>, which this host never did) and the time-sync translators' Cyclone ingress / egress / cleanup (no-ops with no participant);
    /// the rest is the same set, ordered by the base (the input group and the request / genesis / blueprint systems now lead; the simulation group, which holds the Brain and muscle systems,
    /// now precedes the editor's perception / zone-authoring and animation dispatchers). A change here is a change to what an editor frame does first — review it, do not just re-pin it.
    /// </summary>
    [Fact(Timeout = 60_000)]
    public void CE1446B2_TheEditorsKernelRunsItsSystemsInTheSharedBootstrappersOrder()
    {
        var editor = new EditorSubsystem();
        editor.Initialize(new SubsystemConfig { Headless = true });
        try { Assert.Equal(ExpectedSystemSequence, SystemSequence(editor)); }
        finally { editor.Shutdown(); }
    }

    private static readonly string[] ExpectedSystemSequence =
    {
        "Fdp.ModuleHost.Scheduling.TogglableInputGroup",
        "Hrot.Common.Systems.CreateEntityRequestSystem",
        "Hrot.Common.EntityCreation.SeatPresetSystem",
        "Hrot.SimHost.Systems.GenesisMaterializationSystem",
        "Hrot.SimHost.Systems.InitialBrainMaterializationSystem",
        "Hrot.SimHost.Systems.BlueprintMaterializationSystem",
        "Fdp.Toolkit.Blueprints.Systems.BlueprintEventIngressSystem",
        "Fdp.Network.Cyclone.Modules.CycloneNetworkIngressSystem",
        "Fdp.Toolkit.Behavior.Diagnostics.DebugStatePatchSystem",
        "Fdp.Modules.Geographic.Systems.GeodeticSmoothingSystem",
        "Fdp.Toolkit.Navigation.EngineBacked.EngineBackedPathResponseSystem",
        "Fdp.Toolkit.Navigation.Systems.PathfindingResultMaterializationSystem",
        "Hrot.MuscleCharacter.Animation.Systems.AnimationCapabilityChangeReactorSystem",
        "Fdp.ModuleHost.Scheduling.TogglablePostSimulationGroup",
        "Hrot.Common.Systems.EntityRequestFinalizationSystem",
        "Fdp.Toolkit.NetworkSpawning.Systems.SpawnLevelSystem",
        "Fdp.ModuleHost.Diagnostics.EventHistoryCaptureSystem",
        "Fdp.ModuleHost.Diagnostics.EventHistoryCaptureSystem",
        "Hrot.Presentation.Map.MapLayerAssignmentSystem",
        "Hrot.Diagnostics.Breakpoints.DataBreakpointSystem",
        "Hrot.Editor.Debugging.NodeBreakpointEvaluator",
        "Fdp.ModuleHost.Diagnostics.EventHistoryCaptureSystem",
        "Hrot.Presentation.Systems.CanvasMenuUpdateSystem",
        "Fdp.Modules.Geographic.Systems.CoordinateTransformSystem",
        "Hrot.MuscleCharacter.Animation.Systems.NotifyEventEmitterSystem",
        "Hrot.MuscleCharacter.Animation.Systems.AnimationStateReporterSystem",
        "Hrot.MuscleCharacter.Animation.Systems.AnimationBackendCleanupSystem",
        "Hrot.ScenarioEditor.Systems.SelectionRequestSystem",
        "Hrot.ScenarioEditor.Systems.SelectionNotificationSystem",
        "Hrot.ScenarioEditor.Systems.ToolActivationDrainSystem",
        "Hrot.ScenarioEditor.Systems.CenterOnEntitySystem",
        "Hrot.IG.Systems.MapCullingSystem",
        "Hrot.IG.Systems.StyleResolutionSystem",
        "Fdp.Toolkit.Replication.Systems.GhostPromotionSystem",
        "Hrot.Map.Common.Services.StaticObstacleBakeSystem",
        "Hrot.Diagnostics.Breakpoints.DebugSnapshotProvider",
        "Fdp.Toolkit.Behavior.Diagnostics.TraceBufferLifecycleSystem",
        "Fdp.Toolkit.Lifecycle.Systems.BlueprintApplicationSystem",
        "Fdp.Toolkit.Lifecycle.Systems.LifecycleSystem",
        "Fdp.Toolkit.NetworkSpawning.Systems.NetworkSpawningSystem",
        "Fdp.Network.Cyclone.Systems.CycloneEgressSystem",
        "Fdp.Network.Cyclone.Systems.CycloneNetworkCleanupSystem",
        "Hrot.Diagnostics.Breakpoints.ChangeBaselineSystem",
        "Fdp.ModuleHost.Time.ResumeAndDrainSystem",
        "Fdp.ModuleHost.Scheduling.TogglableSimulationGroup",
        "Hrot.MuscleCharacter.Animation.Systems.AnimationDispatcherSystem",
        "Hrot.MuscleCharacter.Animation.Systems.LookAtDispatcherSystem",
        "Hrot.MuscleCharacter.Animation.Systems.MontageQueueAdvanceSystem",
        "Hrot.MuscleCharacter.Animation.Systems.AnimationRuntimeBridgeSystem",
        "Hrot.MuscleCharacter.Animation.Systems.StanceTransitionSystem",
        "Hrot.Editor.Systems.EditorPerceptionSetupSystem",
        "Hrot.Editor.Systems.EditorZoneAuthoringSystem",
        "Fdp.Toolkit.Perception.Systems.LocalGridBuilderSystem",
        "Hrot.SimHost.Systems.EqsSolverSystem",
        "Fdp.Toolkit.Navigation.Systems.PathfindingSolverSystem",
        "Hrot.Common.Systems.ContextActionIngressSystem",
        "Fdp.Toolkit.Behavior.Diagnostics.DebugStatePatchSystem",
        "Fdp.ModuleHost.Scheduling.TogglablePostSimulationGroup",
        "Hrot.UI.Common.Effects.EffectSpawnSystem",
        "Hrot.UI.Common.Effects.WreckSpawnSystem",
        "Fdp.Toolkit.Time.LifespanSystem",
        "Hrot.ScenarioEditor.Map.MenuOrders",
        "Hrot.ScenarioEditor.Map.MenuShortcuts",
        "Hrot.Common.Systems.GlobalActionDispatchSystem"
    };
}
