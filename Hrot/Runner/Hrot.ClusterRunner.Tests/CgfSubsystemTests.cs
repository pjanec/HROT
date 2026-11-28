using System;
using System.Collections.Generic;
using System.Reflection;
using Hrot.CGF;
using Hrot.Common;
using Hrot.Common.Infrastructure;
using Hrot.Map.Common;
using Hrot.Network.NED.Factory;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Orchestration.Handlers;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Runner;
using Fdp.Core;
using Xunit;

namespace Hrot.ClusterRunner.Tests;

/// <summary>
/// Unit tests for <see cref="CgfSubsystem"/> brain-role pack composition (PACK2-R002).
/// </summary>
[Collection("CgfSubsystemTests")]
public class CgfSubsystemTests : IDisposable
{
    private readonly CgfSubsystem _sut = new(new NedNetworkFactory(
        participant:  null,
        entityMap:    new NetworkEntityMap(),
        geoTransform: HrotEnvironment.CreateGeoTransform(52.52, 13.405, 0.0),
        eventBus:     new FdpEventBus(),
        localNodeId:  0,
        role:         NodeRole.None));  // Role gets overridden by ConfigureForNode(_context, Brain)

    public void Dispose() => _sut.Shutdown();

    [Fact]
    public void Initialize_InstallsThreePacks()
    {
        // Headless=true avoids DDS participant creation and DdsIdAllocatorServer wait,
        // making this a fast unit test that only verifies ECS kernel and module composition.
        // (In production/integration tests, Headless=false is used and an OrchestratorSubsystem
        // provides the DdsIdAllocatorServer before CgfSubsystem initializes.)
        var config = new SubsystemConfig { DomainId = 0, Headless = true };

        _sut.Initialize(config);

        // World and EntityMap are created by HrotNodeBuilder (via _context).
        Assert.NotNull(_sut.World);
        Assert.NotNull(_sut.GhostEntityMap);

        // NedReplicationModule (Brain) is wired into _context.NedReplication by
        // HrotNodeBuilderReplicationExtensions.Build() (S202/S401 migration).
        var contextField = typeof(CgfSubsystem)
            .GetField("_context", BindingFlags.NonPublic | BindingFlags.Instance);
        var context = contextField!.GetValue(_sut) as HrotNodeContext;
        Assert.NotNull(context?.NedReplication);
    }

    /// <summary>The kernel's systems in EXECUTION order, as type names — what a frame runs and in what sequence.</summary>
    private List<string> SystemSequence()
    {
        var context = (HrotNodeContext)typeof(CgfSubsystem).GetField("_context", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(_sut)!;
        var seq = new List<string>();
        foreach (var sys in context.Kernel.SystemScheduler.GetAllSystems()) seq.Add(sys.GetType().FullName ?? sys.GetType().Name);
        return seq;
    }

    /// <summary>
    /// ⭐ <c>CE-1446</c> B1 — <b>CGF's frame order, pinned.</b> CGF now boots through <c>SharedApplicationBootstrapper</c> (the base's phase order, as SimHost and IG), so
    /// the kernel's systems run in the base's order, not in the order the old inline build registered them. 📐 Measured against the inline build (same 72 systems):
    /// the SET is identical; the order differs only inside a phase — <c>TogglableSimulationGroup</c> (the capabilities' Brain systems) now precedes the map-effect /
    /// lifespan / menu-action systems; <c>NetworkSpawningSystem</c>, <c>GhostPromotionSystem</c>, <c>TraceBufferLifecycleSystem</c>, <c>DebugStatePatchSystem</c> and one
    /// <c>CycloneNetworkCleanupSystem</c> move a few places. A change to this list is a change to what a CGF frame does first — review it, do not just re-pin it.
    /// </summary>
    [Fact]
    public void CE1446_TheKernelRunsItsSystemsInTheSharedBootstrappersOrder()
    {
        _sut.Initialize(new SubsystemConfig { DomainId = 0, Headless = true });

        Assert.Equal(ExpectedSystemSequence, SystemSequence());
    }

    private static readonly string[] ExpectedSystemSequence =
    {
        "Fdp.ModuleHost.Scheduling.TogglableInputGroup",
        "Hrot.Common.Systems.NetworkPollingSystem",
        "Hrot.Common.Systems.DeleteEntityRequestSystem",
        "Hrot.Common.Systems.CreateEntityRequestSystem",
        "Hrot.Common.EntityCreation.SeatPresetSystem",
        "Hrot.Common.Macros.MacroExpansionSystem",          // ⭐ CE-3451 — macros S3a ④: the shared EntityCreation registers it after the seat presets
        "Hrot.SimHost.Systems.GenesisMaterializationSystem",
        "Hrot.SimHost.Systems.BlueprintMaterializationSystem",
        "Fdp.Toolkit.Blueprints.Systems.BlueprintEventIngressSystem",
        "Fdp.Network.Cyclone.Modules.CycloneNetworkIngressSystem",          // ⭐ CE-3451 — added since the pin: CE-1139 — CGF's stance pair (AnimationReplicationModule.RegisterStanceOn)
        "Fdp.Network.Cyclone.Modules.CycloneNetworkIngressSystem",
        "Fdp.Network.Cyclone.Modules.CycloneNetworkIngressSystem",
        "Fdp.Network.Cyclone.Modules.CycloneNetworkIngressSystem",
        "Fdp.Network.Cyclone.Modules.CycloneNetworkIngressSystem",
        "Fdp.Toolkit.Behavior.Diagnostics.DebugStatePatchSystem",
        "Fdp.Modules.Geographic.Systems.GeodeticSmoothingSystem",
        "Hrot.Network.Routing.ClusterCapabilityIngestSystem",
        "Fdp.Network.Cyclone.Modules.CycloneNetworkIngressSystem",
        "Fdp.Toolkit.Replication.Systems.OwnershipIngressSystem",
        "Fdp.Toolkit.Behavior.Systems.BrainHandOverSystem",
        "Hrot.Network.Systems.OwnershipTransferInitiationSystem",
        "Fdp.Toolkit.Replication.Systems.OwnershipRecomputeSystem",
        "Fdp.Toolkit.Replication.Systems.PartialOwnerReclaimSystem",
        "Fdp.Toolkit.Replication.Systems.LightweightRoundIdentitySystem",
        "Hrot.Common.Systems.EntityRequestFinalizationSystem",
        "Fdp.Toolkit.NetworkSpawning.Systems.SpawnLevelSystem",
        "Fdp.ModuleHost.Diagnostics.EventHistoryCaptureSystem",
        "Fdp.ModuleHost.Diagnostics.EventHistoryCaptureSystem",
        "Hrot.ScenarioEditor.Systems.SelectionInteractionSystemAdapter",
        "Fdp.ModuleHost.Diagnostics.EventHistoryCaptureSystem",
        "Hrot.Presentation.Systems.CanvasMenuUpdateSystem",
        "Hrot.Presentation.Map.MapLayerAssignmentSystem",
        "Hrot.Diagnostics.Breakpoints.DataBreakpointSystem",
        "Fdp.Modules.Geographic.Systems.CoordinateTransformSystem",
        "Fdp.Toolkit.Replication.Systems.SubEntityCleanupSystem",
        "Hrot.Common.Systems.DeadReckoningSyncSystem",
        "Fdp.Toolkit.Replication.Systems.DisposalMonitoringSystem",
        "Hrot.ScenarioEditor.Systems.SelectionRequestSystem",
        "Hrot.ScenarioEditor.Systems.SelectionNotificationSystem",
        "Hrot.ScenarioEditor.Systems.ToolActivationDrainSystem",
        "Hrot.ScenarioEditor.Systems.CenterOnEntitySystem",
        "Hrot.Map.Common.Services.StaticObstacleBakeSystem",
        "Fdp.Toolkit.Aggregate.HeldChildParticipant",          // ⭐ CE-3451 — added since the pin: CE-8013 ② — the shared EntityCreationPack
        "Hrot.Diagnostics.Breakpoints.DebugSnapshotProvider",
        "Fdp.Toolkit.Behavior.Diagnostics.TraceBufferLifecycleSystem",
        "Fdp.Toolkit.Lifecycle.Systems.BlueprintApplicationSystem",
        "Fdp.Toolkit.Lifecycle.Systems.LifecycleSystem",
        "Fdp.Toolkit.NetworkSpawning.Systems.NetworkSpawningSystem",
        "Fdp.Toolkit.Replication.Systems.GhostCreationSystem",
        "Fdp.Toolkit.Replication.Systems.NetworkGatewaySystem",
        "Hrot.Network.Replication.NedReplicationModule+LocalAuthorityYieldSystem",
        "Fdp.Toolkit.Replication.Systems.GhostPromotionSystem",
        "Fdp.Network.Cyclone.Systems.CycloneEgressSystem",
        "Fdp.Network.Cyclone.Systems.CycloneNetworkCleanupSystem",
        "Fdp.Network.Cyclone.Systems.CycloneEgressSystem",
        "Fdp.Network.Cyclone.Systems.CycloneNetworkCleanupSystem",
        "Fdp.Network.Cyclone.Systems.CycloneEgressSystem",
        "Fdp.Network.Cyclone.Systems.CycloneNetworkCleanupSystem",          // ⭐ CE-3451 — added since the pin: one more translator group's cleanup on CGF (not the stance pair, which adds none) — owner unmeasured
        "Fdp.Network.Cyclone.Systems.CycloneEgressSystem",          // ⭐ CE-3451 — added since the pin: CE-1139 — CGF's stance pair (AnimationReplicationModule.RegisterStanceOn)
        "Fdp.Network.Cyclone.Systems.CycloneEgressSystem",
        "Fdp.Network.Cyclone.Systems.CycloneNetworkCleanupSystem",
        "Fdp.Network.Cyclone.Systems.CycloneEgressSystem",
        "Fdp.Toolkit.Replication.Systems.SmartEgressSystem",
        "Fdp.Network.Cyclone.Systems.CycloneNetworkCleanupSystem",
        "Fdp.Network.Cyclone.Egress.EgressPipelineLaunchSystem",          // ⭐ CE-3451 — added since the pin: CE-3427 N-1 — every networked node (NedReplicationModule)
        "Fdp.ModuleHost.Time.ResumeAndDrainSystem",
        "Fdp.ModuleHost.Scheduling.TogglableSimulationGroup",
        "Hrot.Network.Systems.DeferredTakeoverSystem",
        "Hrot.Common.Systems.ContextActionIngressSystem",
        "Fdp.Toolkit.Behavior.Diagnostics.DebugStatePatchSystem",
        "Fdp.ModuleHost.Scheduling.TogglablePostSimulationGroup",
        "Hrot.UI.Common.Effects.HudFeedSystem",          // ⭐ CE-4671 LK-S1 — the HUD feeds, moved out of the spawner, right before it
        "Hrot.UI.Common.Effects.EffectSpawnSystem",
        "Hrot.UI.Common.Effects.WreckSpawnSystem",
        "Fdp.Toolkit.Time.LifespanSystem",
        "Hrot.ScenarioEditor.Map.MenuOrders",
        "Hrot.ScenarioEditor.Map.MenuShortcuts",
        "Hrot.ScenarioEditor.Map.HatArbiter",          // ⭐ CE-3451 — added since the pin: B-16 H3 — the map's hat arbiter (MapInteractionPack)
        "Hrot.Common.Systems.GlobalActionDispatchSystem"
    };

    // ── CE-3348: the claims `CgfHandlerRegistrationTests` made on the retired `CgfApplication` (CGF1-S0104 / BATCH-23 A.1), now on the LIVE CGF's slave ──

    private ClusterSlave LiveSlave()
    {
        _sut.Initialize(new SubsystemConfig { DomainId = 0, Headless = true });
        return _sut.NodeForTest!.ClusterSlave;
    }

    /// <summary>⭐ <c>CE-3348</c> — the live CGF's slave (built by <c>CgfSubsystem</c> through the shared bootstrapper) holds the replay-load handler.
    /// 🔴 Mutation: <c>ReferenceReplayLoadHandler</c> dropped from <c>EcsHostClusterHandlers.Compose</c> ⇒ red.</summary>
    [Fact]
    public void CE3348_TheLiveCgfSlave_RegistersReferenceReplayLoadHandler()
        => Assert.True(LiveSlave().IsHandlerRegistered<ReferenceReplayLoadHandler>(), "CGF must register ReferenceReplayLoadHandler for replay cluster participation.");

    /// <summary>⭐ <c>CE-3348</c> — … the live-load handler.</summary>
    [Fact]
    public void CE3348_TheLiveCgfSlave_RegistersReferenceLiveLoadHandler()
        => Assert.True(LiveSlave().IsHandlerRegistered<ReferenceLiveLoadHandler>(), "CGF must register ReferenceLiveLoadHandler for live cluster participation.");

    /// <summary>⭐ <c>CE-3348</c> — … the prefetch handler (scenario file staging).</summary>
    [Fact]
    public void CE3348_TheLiveCgfSlave_RegistersReferencePrefetchHandler()
        => Assert.True(LiveSlave().IsHandlerRegistered<ReferencePrefetchHandler>(), "CGF must register ReferencePrefetchHandler for scenario prefetch.");

    /// <summary>⭐ <c>CE-3348</c> — … the preview handler (dry run).</summary>
    [Fact]
    public void CE3348_TheLiveCgfSlave_RegistersReferencePreviewHandler()
        => Assert.True(LiveSlave().IsHandlerRegistered<ReferencePreviewHandler>(), "CGF must register ReferencePreviewHandler.");
}
