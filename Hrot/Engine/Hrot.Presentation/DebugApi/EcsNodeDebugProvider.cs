using System;
using System.Linq;
using Fdp.Toolkit.Orchestration.Handlers;
using Hrot.Common.Infrastructure;
using Hrot.UI.Common.Facades;

namespace Hrot.Presentation.DebugApi;

/// <summary>
/// ⭐⭐⭐ <b>The ONE debug surface of an ECS node</b> (CGF, SimHost, IG, traffic): every capability that comes from the node's own
/// <see cref="HrotNodeContext"/> is derived HERE, so two nodes cannot answer differently by construction.
/// 🔒 User, `2026-10-26`: *"Best if the nodes cannot get different stuff by construction (unless really necessary)."*
/// </summary>
/// <remarks>
/// <para>⭐ From the context, the same on every node: the world and its entity map, the TKB catalog, the cluster master (the node's
/// orchestration bus), the cluster state and the exercise inventory (its <c>ClusterSlave</c>), the replay recording (the shared
/// <see cref="ReferenceReplayLoadHandler"/> on that slave), ownership transfer and the descriptor map (when the node runs NED
/// replication — the module that serves them), the architecture snapshot (its kernel).</para>
/// <para>⭐ Only what a node genuinely has or lacks is passed in (<see cref="EcsNodeDebugExtras"/>): a time-drive adapter, a gizmo
/// feed, an entity extraction service, a mission editor. 📌 Before, each host hand-wrote ~15 arguments and they had drifted: the
/// traffic node offered no save or dump, only CGF named its replay recording, and a new capability needed four edits.</para>
/// </remarks>
public static class EcsNodeDebugProvider
{
    /// <param name="node">The node's context, re-read on every access (built in <c>Initialize</c>, gone after <c>Shutdown</c>).</param>
    public static SubsystemDebugProvider For(
        string subsystemName, string perspective, Func<HrotNodeContext?> node, EcsNodeDebugExtras? extras = null)
    {
        if (node is null) throw new ArgumentNullException(nameof(node));
        var x = extras ?? new EcsNodeDebugExtras();
        return new SubsystemDebugProvider(
            subsystemName: subsystemName,
            perspective:   perspective,
            world:         () => node()?.World,
            entityMap:     () => node()?.EntityMap,
            extraction:    x.Extraction,
            drive:         x.Drive,
            gizmoBuffer:   x.GizmoBuffer,
            missionEditor: x.MissionEditor,
            tkbDb:         SubsystemDebugProvider.TkbFrom(() => node()?.World),
            master:        OrchestrationBusMasterControl.Via(() => node()?.EventBus),
            clusterState:  SubsystemDebugProvider.ClusterStateFrom(() => node()?.ClusterSlave),
            // ⭐⭐ CE-4096 (AQ108 §2.6) — what the master LAST ANNOUNCED to this node, on every ECS node alike (null = none yet):
            //    ⛔ not `ClusterState` above, which is the node's own committed state and stays Idle while the cluster loads.
            followedClusterState: () => node()?.ClusterSlave is { HasFollowedState: true } s ? s.FollowedClusterState : null,
            masterGone:    () => node()?.ClusterSlave.MasterGone ?? false,
            exercises:     SubsystemDebugProvider.ExercisesFrom(() => node()?.ClusterSlave),
            replayAxis:    () => node()?.ClusterSlave.ReplayAxis,   // ⭐ CE-3329 R-3058 — the master's broadcast, on every node
            runNotRecorded: () => node()?.ClusterSlave.RunNotRecorded,   // ⭐ CE-4090 D14 — the same broadcast
            recordLiveRunsOff: () => node()?.ClusterSlave.RecordLiveRunsOff,
            askToKeepRecordings: () => node()?.ClusterSlave.AskToKeepRecordings,   // ⭐ AQ108 D15 (CE-4100 ①)
            armedMode:         () => node()?.ClusterSlave.ArmedMode,   // ⭐ CE-3341 R-3068 — the same View
            runChoice:         x.RunChoice,   // ⭐ CE-1451 — the host's run choice, independent of the drive (the IG has none)
            clusterView:       () => node()?.ClusterSlave.View,   // ⭐ CE-3344 R-3069 — the node's ONE read model: CanOperate gates the HTTP cluster routes
            // ⭐⭐⭐ CE-1436 (R-3066, design V5) — the check round's answer from the node's ONE cluster read model (ClusterSlave.View), the same on CGF, SimHost, IG and
            //    traffic: ObservesUnsavedChecks is true by construction here, so ▶ asks the round on every host. ⛔ Not the node's own world check — the MASTER's answer.
            unsavedCheck:  id => node()?.ClusterSlave.View.UnsavedCheckOutcomeFor(id),
            scenarioDirty: () => node()?.ClusterSlave.View.LastUnsavedCheck?.Changed,
            replayRecording: () => node()?.ReplayRecordingPath,   // ⭐ CE-3330 E6 — the one lookup (HrotNodeContext), also what the shown timeline opens
            // ⭐ RB-2 — the replay's zero and now, from the same handler (frame-based seeks resolve against them)
            replayPosition: () => node()?.ClusterSlave.RegisteredHandlers.OfType<ReferenceReplayLoadHandler>().FirstOrDefault() is { } h
                                  && h.ReplayPosition is { } p ? new ReplayPosition(p.ZeroWallTicks, p.NowWallTicks, h.ReplaySimSeconds) : null,
            requestOwnershipTransfer: () => node() is { NedReplication: not null } n
                ? req => n.World.Bus.PublishManaged(req)
                : null,
            descriptorMap: () => node()?.NedReplication?.DescriptorOwnershipMap,
            // ⭐ CE-3330 — the node's live event timeline, on every node (stepped by HrotNodeContext.Tick)
            timeline:      () => node() is { } n ? TimelineOf(n) : null,
            architecture:  () => node()?.Kernel is { } kernel
                ? new Fdp.ModuleHost.Diagnostics.ArchitectureDiagnosticsService(() => kernel)
                : null);
    }

    // one access per context: the provider re-reads the node on every call and the access is a constant of it
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<HrotNodeContext, TimelineAccess> Accesses = new();

    /// <summary>⭐ CE-3330 — the one <see cref="TimelineAccess"/> of a node context (cached per context): the ECS nodes' provider and the editor's
    /// <c>/timeline</c> attach both serve THIS.</summary>
    public static TimelineAccess TimelineOf(HrotNodeContext n)
        // ⭐ CE-3330 E6 — it serves what the node SHOWS (live, or its own recording in a cluster replay); both are Funcs, so the cached access follows the mode
        => Accesses.GetValue(n, c => new TimelineAccess(() => c.ShownTimeline, c.TimelineLanes, () => c.ShownTimelineContext));
}

/// <summary>⭐ What an ECS node genuinely has or lacks beyond its <see cref="HrotNodeContext"/> — each a <see cref="Func{T}"/> because
/// hosts build them after the provider (the window, a later Initialize step).</summary>
public sealed record EcsNodeDebugExtras
{
    /// <summary>The node's time-drive adapter (a slave's cluster time adapter); <see langword="null"/> ⇒ it cannot step time.</summary>
    public Func<ITimeTransportFacade?>? Drive { get; init; }

    /// <summary>⭐ CE-1451 — the node's run choice (<c>IProvidesRunControls.RunChoice</c>), so <c>GET /status.runGate</c> works on a host with no drive (the IG).</summary>
    public Func<Hrot.UI.Common.RunMode.RunModeChoice?>? RunChoice { get; init; }

    /// <summary>The node's map/gizmo feed.</summary>
    public Func<Fdp.Toolkit.Diagnostics.Gizmos.DebugPrimitiveBuffer?>? GizmoBuffer { get; init; }

    /// <summary>The node's entity-state extraction service (else the dispatcher builds one from the world).</summary>
    public Func<Fdp.Toolkit.Diagnostics.IEntityStateExtractionService?>? Extraction { get; init; }

    /// <summary>The node's mission editor.</summary>
    public Func<Hrot.UI.Common.Facades.IMissionEditorService?>? MissionEditor { get; init; }
}
