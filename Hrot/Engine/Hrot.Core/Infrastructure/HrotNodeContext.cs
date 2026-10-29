using System.Collections.Generic;
using System.Linq;
using Fdp.Toolkit.Orchestration.Handlers;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Core;
using Fdp.Modules.Geographic;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Replication.Systems;
using Hrot.Common.Abstractions;
using Fdp.ModuleHost;
using Fdp.ModuleHost.Abstractions;
using Fdp.ModuleHost.Time;
using NetworkEntityMap = Fdp.Toolkit.Replication.Services.NetworkEntityMap;
using Fdp.Toolkit.NetworkSpawning;
// IOrchestrationTranslator lives in same namespace (Hrot.Common.Infrastructure)

namespace Hrot.Common.Infrastructure;

/// <summary>
/// Immutable snapshot of all infrastructure objects produced by <see cref="HrotNodeBuilder"/>.
/// Passed to subsystems so they can register modules, wire handlers, and add ECS systems
/// without re-running the bootstrap sequence.
///
/// <para>⭐⭐⭐ <b>OWNERSHIP — <c>QA-001</c>.</b> The builder CREATES <see cref="World"/> and
/// <see cref="Kernel"/>, so the context OWNS them and <see cref="Dispose"/> tears them down.
/// ⛔ Every consumer must call <c>context.Dispose()</c> on shutdown — ⛔ NOT
/// <c>context.Kernel.Dispose()</c>, which is what all four consumers used to do and is exactly how
/// the world came to be leaked on every node teardown.</para>
///
/// <para>📐 <b>Measured, 2026-08-26:</b> the four consumers that receive a world through this record
/// (<c>SimHostApp</c>, <c>IgApplication</c>, <c>CgfSubsystem</c>, <c>EditorSubsystem</c>) all
/// disposed the kernel and none disposed the world; the three that build their own world directly
/// (<c>CgfApplication</c>, <c>EditorSubsystem</c>, <c>ScenarioSubsystem</c>) all dispose it. ⇒ the
/// defect was the MISSING OWNERSHIP CONTRACT on this record, not four independent oversights.
/// One leaked <see cref="EntityRepository"/> holds an <c>int[1_000_000]</c> free list plus one
/// <c>NativeChunkTable</c> per registered component; accumulated across an integration run it
/// exhausted a 16 GB box and aborted the test host.</para>
///
/// <para>⚠ <see cref="Participant"/> is deliberately NOT disposed here — its ownership is genuinely
/// conditional (a test harness or an outer composition root may own it), so it stays with whoever
/// created it.</para>
/// </summary>
public sealed record HrotNodeContext : IDisposable
{
    private bool _disposed;

    /// <summary>
    /// ⭐ CE-4013 D7 — THE node frame, one place: the cluster translator and slave tick (so the frame sees this frame's cluster
    /// commands and time), then the host's own pre-kernel work, the kernel, the host's own post-kernel work, then the bus swap (this
    /// frame's events become next frame's reads). 📐 It was written four times (Stride, SimHost, IG, CGF) and a fifth (Traffic), with
    /// the host's own work interleaved differently; the spine itself was the same everywhere. 📄 docs/DESIGN_Subsystem_Composition_Unification.md §4.1ae.
    /// </summary>
    /// <param name="beforeKernel">The host's work after the cluster has ticked and before the kernel (input, a gizmo frame close, a canvas).</param>
    /// <param name="afterKernel">The host's work after the kernel and before the swap, while this frame's events are still readable.</param>
    public void Tick(System.Action? beforeKernel = null, System.Action? afterKernel = null)
    {
        SlaveTranslator?.Tick();
        ClusterSlave.Tick();
        beforeKernel?.Invoke();
        Kernel.Update();
        afterKernel?.Invoke();
        // ⭐ CE-3330 — the node's live event timeline: the shipped lanes evaluated on this world each sim frame, while this frame's
        //   events are still readable (before the swap). Here, so EVERY host gets it by construction; nothing while the world does not advance.
        StepTimeline();
        EventBus.SwapBuffers();
    }

    /// <summary>
    /// ⭐ Disposes what the builder created: the <see cref="Kernel"/> first (so module providers and
    /// in-flight tasks are torn down while their storage is still valid), then the
    /// <see cref="World"/>. Idempotent — safe to call from a shutdown path that may run twice.
    /// ⛔ Does not touch <see cref="Participant"/>; see the ownership note on the type.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _recordingTimeline?.Dispose();   // ⭐ CE-3330 E6 — cancels a running index build over the replay recording
        Kernel.Dispose();
        World.Dispose();
    }

    /// <summary>The ECS world (entity repository).</summary>
    public required EntityRepository World { get; init; }

    /// <summary>The module-host kernel.</summary>
    public required ModuleHostKernel Kernel { get; init; }

    /// <summary>The live DDS participant. <c>null</c> in headless/test contexts where DDS is disabled.</summary>
    public DdsParticipant? Participant { get; init; }

    /// <summary>The application event bus.</summary>
    public required FdpEventBus EventBus { get; init; }

    /// <summary>Shared network entity map for ghost/egress lookups.</summary>
    public required NetworkEntityMap EntityMap { get; init; }

    /// <summary>The cluster slave wired with the four generic reference handlers.</summary>
    public required ClusterSlave ClusterSlave { get; init; }

    /// <summary>
    /// DDS <-> bus bridge for <c>NodeOpCommand</c> / <c>NodeOpStatus</c> / <c>NodeHeartbeat</c>.
    /// <c>null</c> when no DDS participant was provided.
    /// </summary>
    public Hrot.Core.Network.ISlaveOrchestrationTranslator? SlaveTranslator { get; init; }

    /// <summary>
    /// Infrastructure <see cref="IEcsModule"/> instances (e.g. <c>EntityLifecycleModule</c>,
    /// <c>GeographicModule</c>) that must be registered on the kernel before subsystem modules.
    /// </summary>
    public required IReadOnlyList<IEcsModule> BaseModules { get; init; }

    /// <summary>
    /// Ghost-creation system shared between the network ingress translators and the replay handler.
    /// Exposed here so Phase 4 can wire it into <c>ReplayLoadClusterOpHandler</c>.
    /// <c>null</c> in headless/test contexts that skip NED replication.
    /// </summary>
    public GhostCreationSystem? GhostCreationSystem { get; init; }

    /// <summary>The DDS ID allocator for entity ID allocation. Null in headless contexts.</summary>
    public INetworkIdAllocator? IdAllocator { get; init; }

    /// <summary>The local node ID used for DDS identity and ID allocation.</summary>
    public int NodeId { get; init; }

    /// <summary>
    /// The TKB (toolkit database) shared by <c>EntityLifecycleModule</c> and spawning systems.
    /// Use this when constructing systems that require the same tkbDb as the lifecycle module.
    /// </summary>
    public ITkbDatabase? TkbDb { get; init; }

    /// <summary>
    /// The geodetic coordinate transform created by the builder.
    /// Use this instead of calling <c>HrotEnvironment.CreateGeoTransform()</c> again.
    /// </summary>
    public IGeographicTransform? GeoTransform { get; init; }

    /// <summary>
    /// The replication module bundling translator packs and their lifecycle systems.
    /// Set by <c>HrotNodeBuilderReplicationExtensions.Build()</c>.
    /// <c>null</c> only in legacy call sites that have not yet migrated to the extension Build().
    /// </summary>
    public INedReplicationModule? NedReplication { get; init; }

    /// <summary>
    /// Protocol-neutral replication interface. Aliases <see cref="NedReplication"/> for
    /// code that does not need NED-specific members.
    /// </summary>
    public IReplicationModule? Replication => NedReplication;

    /// <summary>Event accumulator for checkpoint event preservation (CGF-SCN-2 S503).</summary>
    public required EventAccumulator EventAccumulator { get; init; }

    /// <summary>
    /// The time controller the builder created and already handed to <see cref="Kernel"/>, exposed so a
    /// host that must drive the clock can reach it without building a second one.
    /// </summary>
    /// <remarks>
    /// <para>⭐⭐⭐ <b><c>N₀</c> item ② (<c>CE-203</c>).</b> <c>CE-201</c> made the time ROLE an input but
    /// never exposed the RESULT, so a host wanting the controller still had to call
    /// <c>TimeControllerFactory.Create</c> itself — which is the duplicate the editor has today.</para>
    ///
    /// <para>⛔ <b>Typed as the INTERFACE, deliberately.</b> A <c>MasterSyncController</c>-typed property
    /// would push the time role back into the builder's shape, which is exactly what <c>N₀</c> removed.
    /// ⭐ A host that needs the concrete master casts at its own site, as the editor already does.</para>
    /// </remarks>
    public ITimeController? TimeController { get; init; }

    /// <summary>⭐ CE-1446 B0 — the time role the node was built with (<c>HrotNodeBuilder.WithTimeRole</c>, default Slave). Recorded so the shared bootstrapper can verify a subclass honoured its declared <c>TimeRole</c>.</summary>
    public Fdp.Toolkit.Time.Controllers.TimeRole TimeRole { get; init; } = Fdp.Toolkit.Time.Controllers.TimeRole.Slave;

    /// <summary>
    /// ⭐ CE-4013 (<c>DESIGN_Subsystem_Composition_Unification.md</c> §4.1ae T1) — the extra terrain content THIS node loads with every
    /// terrain; a capability adds to it (the traffic pack's SUMO net), the node's residency reads it. One per node, never per process.
    /// </summary>
    public Hrot.Map.Common.Services.TerrainContentRegistry TerrainContent { get; init; } = new();

    /// <summary>
    /// ⭐ <c>CE-3330</c> — one live-timeline step: the lanes evaluated on this world (nothing while its sim frame does not advance). <see cref="Tick"/>
    /// calls it after the kernel; a host with its OWN frame (the editor) calls it at the same point.
    /// </summary>
    /// <remarks>
    /// ⭐ <c>CE-3330</c> E1 (docs/DESIGN_Director_Timeline_Editor.md §2e) — the index is FROZEN while this node's <see cref="ClusterSlave.LocalClusterState"/>
    /// is a review or replay (a review rewinds the world: the frame counter going back would otherwise reset it; its history IS the run up to the
    /// live point), and starts AFRESH on entering <c>OperatingLive</c> from anything but a review (edit, preview, idle, loading…) so frame 0 is the
    /// run's first frame. Returning to live FROM a review does not reset — the index continues from the live point.
    /// ⭐ CE-1448 — "a review" includes the frames around it where this node's state still says live: from the review handler's prepare to its finalize (entry), and, after the return, until the world is past the index's last frame.
    /// </remarks>
    public void StepTimeline()
    {
        var state = ClusterSlave.LocalClusterState;
        // ⭐ CE-3330 E6 — in a cluster replay the shown index is the node's OWN recording's: keep it open (cheap no-op per frame)
        if (IsRecordingShown(state)) RecordingTimeline.Sync(ReplayRecordingPath);
        else _recordingTimeline?.Sync(null);
        // ⭐⭐ CE-1448 — a review is IN FLIGHT from the moment its prepare commits (the handler holds the live point) to the moment its finalize commits — which is BEFORE the cluster state says so on entry
        //   (the world is put to the review's moment while this node still reads OperatingLive: MEASURED on the real editor, 314 → 299 → the index reset to one frame) and AFTER it on return. The index is frozen throughout.
        bool reviewInFlight = (_reviewHandler ??= ClusterSlave.RegisteredHandlers.OfType<Fdp.Toolkit.Orchestration.Handlers.ReferenceReviewHandler>().FirstOrDefault())?.InReview == true;
        if (reviewInFlight) _timelineAfterReview = true;
        if (Fdp.Toolkit.Orchestration.ClusterStates.IsReview(state)) _timelineAfterReview = true;
        else if (state == Fdp.Toolkit.Orchestration.ClusterState.OperatingLive)
        {
            if (reviewInFlight) { _timelineState = state; return; }   // ⭐ CE-1448 — entering a DAR: the world is rewound under a state that still says live — no step, no reset
            if (_timelineState != Fdp.Toolkit.Orchestration.ClusterState.OperatingLive && !_timelineAfterReview) Timeline.Reset(World);
            // ⭐ CE-1448 — the state flips to OperatingLive when the review's world is put back, but the master returns the clock to the live point only a few frames later. MEASURED (the DAR rails): the first live
            //   steps still read this node's own last frame (== the index's last), then the clock snaps back to L (a few frames BEHIND it), then climbs — and the snap-back looked like a rewind and reset the index
            //   (the DAR's history lost). So HOLD — no step, no reset — until the world is PAST the index's last frame (<=: the stale frame and the snap-back are both held), then continue.
            //   ⛔ Only after a review: a genuine rewind outside one (edit, a new run) still resets (LiveTimelineSource.Step, and the fresh index on entering live from anything else).
            if (_timelineAfterReview && World.HasSingleton<Fdp.Core.GlobalTime>()
                && World.GetSingleton<Fdp.Core.GlobalTime>().FrameNumber <= Timeline.LastFrame)
            {
                _timelineState = state;
                return;
            }
            _timelineAfterReview = false;
        }
        else if (state is not (Fdp.Toolkit.Orchestration.ClusterState.LoadingLive or Fdp.Toolkit.Orchestration.ClusterState.UnloadingLive))
            _timelineAfterReview = false;   // another activity (edit, preview, replay, idle) intervened: the next live is a new run
        _timelineState = state;
        if (Fdp.Toolkit.Orchestration.ClusterStates.IsReplaying(state) || Fdp.Toolkit.Orchestration.ClusterStates.IsReview(state)) return;
        Timeline.Step(World, System.DateTime.UtcNow.Ticks);
    }

    private Fdp.Toolkit.Orchestration.ClusterState _timelineState;
    private bool _timelineAfterReview;
    private Fdp.Toolkit.Orchestration.Handlers.ReferenceReviewHandler? _reviewHandler;

    /// <summary>The context the node's timeline lanes apply in: the live world.</summary>
    public static Fdp.Toolkit.ReplayBrowser.Timeline.TimelineLaneContext TimelineContext { get; } = new(Live: true);

    private Fdp.Toolkit.ReplayBrowser.Timeline.TimelineLaneCatalog? _timelineLanes;
    private Fdp.Toolkit.ReplayBrowser.Timeline.LiveTimelineSource? _timeline;

    /// <summary>
    /// ⭐ <c>CE-3330</c> — the lane catalogue of this node's live timeline (the shipped lanes; a user's lane is registered into it).
    /// Built on first use, one per node.
    /// </summary>
    public Fdp.Toolkit.ReplayBrowser.Timeline.TimelineLaneCatalog TimelineLanes
        => _timelineLanes ??= Fdp.Toolkit.ReplayBrowser.Timeline.ShippedTimelineLanes.Catalog();

    /// <summary>
    /// ⭐ <c>CE-3330</c> — this node's LIVE event-timeline index, stepped by <see cref="Tick"/> after the kernel (the editor's
    /// <c>LiveTimelineSource</c>, not a second one). Built on first use, one per node.
    /// </summary>
    public Fdp.Toolkit.ReplayBrowser.Timeline.LiveTimelineSource Timeline
        => _timeline ??= new Fdp.Toolkit.ReplayBrowser.Timeline.LiveTimelineSource(TimelineLanes, TimelineContext, TimelineCompiler);

    private Fdp.Toolkit.ReplayBrowser.Timeline.TimelineLaneCompiler? _timelineCompiler;

    /// <summary>One lane compiler per node, shared by the live source and the recording slot.</summary>
    private Fdp.Toolkit.ReplayBrowser.Timeline.TimelineLaneCompiler TimelineCompiler
        => _timelineCompiler ??= new Fdp.Toolkit.ReplayBrowser.Timeline.TimelineLaneCompiler(
            new Fdp.Toolkit.ReplayBrowser.Search.EventScannerCompiler(new StructEdit.Reflection.ComponentEditServiceBuilder().Build()));

    // ───────────────────────── ⭐ CE-3330 E6 — what the node's timeline panel SHOWS ─────────────────────────

    /// <summary>The context the lanes apply in over a node's own recording (a cluster replay).</summary>
    public static Fdp.Toolkit.ReplayBrowser.Timeline.TimelineLaneContext RecordingTimelineContext { get; } = new(Recording: true);

    private Fdp.Toolkit.ReplayBrowser.Timeline.RecordingTimelineSlot? _recordingTimeline;
    private Fdp.Toolkit.ReplayBrowser.Timeline.RecordingTimelineSlot RecordingTimeline
        => _recordingTimeline ??= new Fdp.Toolkit.ReplayBrowser.Timeline.RecordingTimelineSlot(TimelineLanes, RecordingTimelineContext, () => TimelineCompiler);

    /// <summary>The recording this node's replay handler plays, else null.</summary>
    public string? ReplayRecordingPath
        => ClusterSlave.RegisteredHandlers.OfType<ReferenceReplayLoadHandler>().FirstOrDefault()?.ReplayFilePath;

    // A replay shows its own recording; a REVIEW shows the frozen live source (DAR §9e), so it is not in this set.
    private static bool IsRecordingShown(Fdp.Toolkit.Orchestration.ClusterState s)
        => s is Fdp.Toolkit.Orchestration.ClusterState.LoadingReplay or Fdp.Toolkit.Orchestration.ClusterState.OperatingReplay
            or Fdp.Toolkit.Orchestration.ClusterState.UnloadingReplay;

    /// <summary>
    /// ⭐ <c>CE-3330</c> E6 — the index the node's timeline panel and <c>GET /timeline/*</c> read: in a cluster replay the node's OWN recording's
    /// (<see cref="Fdp.Toolkit.ReplayBrowser.Timeline.RecordingTimelineSlot"/>, null while no recording is loaded yet); otherwise — live, edit, review (frozen) — the live
    /// <see cref="Timeline"/>. The slot is synced by <see cref="StepTimeline"/>.
    /// </summary>
    public Fdp.Toolkit.ReplayBrowser.Timeline.ITimelineSource? ShownTimeline
        => IsRecordingShown(ClusterSlave.LocalClusterState) ? _recordingTimeline?.Source : Timeline;

    /// <summary>The lane context matching <see cref="ShownTimeline"/>.</summary>
    public Fdp.Toolkit.ReplayBrowser.Timeline.TimelineLaneContext ShownTimelineContext
        => IsRecordingShown(ClusterSlave.LocalClusterState) ? RecordingTimelineContext : TimelineContext;
}
