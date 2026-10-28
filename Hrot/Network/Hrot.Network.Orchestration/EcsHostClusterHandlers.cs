using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Core.Orchestration;
using Fdp.ModuleHost;
using Fdp.ModuleHost.Scheduling;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Orchestration.Handlers;
using Fdp.Toolkit.Orchestration.Preview;
using Fdp.Toolkit.Replication.Services;
using Hrot.Map.Common.ClusterLoad;
using Hrot.Map.Common.Services;

namespace Hrot.SimHost.Modules.Orchestration
{
    /// <summary>
    /// What one ECS host hands to <see cref="EcsHostClusterHandlers.Compose"/>. Every field is something the host
    /// HOLDS; ⛔ a host that holds one and leaves it unset is the silent-default defect (CLAUDE.md).
    /// </summary>
    public sealed class EcsHostClusterHandlerInputs
    {
        public required ClusterSlave     Slave        { get; init; }
        public required ModuleHostKernel Kernel       { get; init; }
        public required EntityRepository World        { get; init; }
        public required int              NodeId       { get; init; }
        public required NodeRole         Role         { get; init; }
        public required string           HostLabel    { get; init; }
        /// <summary>The node's staging root — recordings, staged scenarios, the prefetch store.</summary>
        public required string           StagingRoot  { get; init; }

        // ── the recording controller (P5a: EcsRecordReplayController.ForNode) ──
        /// <summary>The replication module's post-seek hook, or null on a host with no transport.</summary>
        public Action? TransportAfterSeek { get; init; }
        /// <summary>The node's lifecycle module — gated during replay and cleared at every world replacement (§2.1m).</summary>
        public Fdp.Toolkit.Lifecycle.EntityLifecycleModule? Elm { get; init; }

        // ── replay load: the groups a replay switches off ──
        public TogglableInputGroup?          InputGroup     { get; init; }
        public TogglableSimulationGroup?     SimGroup       { get; init; }
        public TogglablePostSimulationGroup? PostSimGroup   { get; init; }
        public NetworkLifecycleSystemGroup?  LifecycleGroup { get; init; }
        public Action<bool>?                 BypassLifecycle { get; init; }

        // ── the dry run's event history (D2) ──
        /// <summary>⭐ <c>CE-3314</c> — the step-event window a restore restarts (dry-run exit and checkpoint restore, D4). ⛔ REQUIRED:
        /// the editor and CGF held it (<c>HrotNodeContext.EventAccumulator</c>) and did not pass it, so a restore there kept the old
        /// window (the silent-default rule). A host with no step window says so with an explicit null.</summary>
        public required EventAccumulator? EventAccumulator { get; init; }

        // ── dry run: the non-ECS state it also puts back (DESIGN_Deterministic_Network_Ids.md §2b) ──
        public Fdp.Toolkit.NetworkSpawning.INetworkIdAllocator? IdAllocator { get; init; }
        /// <summary>The node's entity map. ⚠ Null on a host that sets the map singleton AFTER composing — the dry run
        /// then resolves it from the world when it runs (<see cref="PreviewParticipants.EntityMapFromRepository"/>).</summary>
        public NetworkEntityMap? EntityMap { get; init; }
        /// <summary>Copy Transient components in the dry run (the editor's did, HN-016) — until B2's keyframe.</summary>
        public bool DryRunIncludesTransient { get; init; }

        // ── the load phase ──
        public required List<ILoadPartProvider> LoadProviders { get; init; }
        /// <summary>The host's own scenario handlers (episode load), placed after the shared ones and before the chain.</summary>
        public IReadOnlyList<IClusterStateHandler> ScenarioHandlers { get; init; } = Array.Empty<IClusterStateHandler>();
        public IClusterStateHandler? ScenarioSaveHandler { get; init; }

        // ── terrain / zone ops ──
        public TerrainLoadService? TerrainService     { get; init; }
        /// <summary>Where the D5 identity check reads the staged header; null where nothing is staged (the editor).</summary>
        public string?             TerrainStagingRoot { get; init; }
    }

    /// <summary>The objects <see cref="EcsHostClusterHandlers.Compose"/> built that the host keeps using.</summary>
    public sealed record EcsHostClusterHandlers(
        EcsRecordReplayController RecordReplayController,
        ReferencePreviewHandler   DryRun,
        Fdp.Toolkit.Replay.WorldChangeCheck ChangeCheck)
    {
        /// <summary>
        /// ⭐⭐⭐ <c>D16</c> (<c>R-3046</c>) — <b>THE cluster handlers of an ECS host, composed in ONE place.</b> The recording
        /// controller, replay load, checkpoint, dry run, prefetch, terrain/zone ops, the host's scenario handlers, the load
        /// chain, the scenario-save/archive pair and live load — in one order for every host.
        /// <para>📐 Measured before this existed (restore design §1b U3): <c>NodeBootstrapper</c> (SimHost, IG, traffic,
        /// Stride), <c>CgfSubsystem</c> and <c>EditorSubsystem</c> each wrote this set by hand, and they had drifted — the
        /// checkpoint handler only on the first, the editor's recorder built without <c>ForNode</c> (no ELM gate on a
        /// seek), its replay handler without the world-replacement hook, and its dry run on no slave at all.</para>
        /// <para>⚠ <b>Order is load-bearing</b>: <see cref="ClusterSlave"/> gives an operation to the FIRST claimant and
        /// returns. Replay load is first (it must claim <c>PrepareLive</c> while a replay is active); live load is last
        /// (it claims what nothing before it did).</para>
        /// 📄 docs/DESIGN_Checkpoint_And_Dry_Run_Restore.md §1b, D14–D16.
        /// </summary>
        public static EcsHostClusterHandlers Compose(EcsHostClusterHandlerInputs h)
        {
            if (h is null) throw new ArgumentNullException(nameof(h));
            var slave = h.Slave;

            // P5a — the ONE recording controller: a seek clears the ELM's bookkeeping, and the ELM is gated in replay.
            var controller = EcsRecordReplayController.ForNode(h.Kernel, h.NodeId, h.World, h.TransportAfterSeek, h.Elm);
            Action<bool>? worldReplaced = h.Elm == null ? null : h.Elm.OnWorldReplaced;

            // 1. Replay load — FIRST, so it claims PrepareLive while a replay is active (live-from-replay, CGF1-S0305).
            var replayLoad = new ReferenceReplayLoadHandler(
                controller, h.InputGroup, h.SimGroup, h.PostSimGroup, h.LifecycleGroup, h.BypassLifecycle,
                h.StagingRoot,
                suspendGlobalTimePush: h.Kernel.SuspendGlobalTimePush,
                resumeGlobalTimePush:  h.Kernel.ResumeGlobalTimePush,
                // §2.1m step 3 — the ELM's bookkeeping is discarded at EVERY world replacement.
                worldReplaced:         worldReplaced,
                world:                 h.World);   // ⭐ CE-3337 — a branch's after-restore step runs on it
            slave.RegisterHandler(replayLoad);

            // 2. Checkpoint — ⭐ CE-3299 B3 (R-3044): a keyframe the node's LIVE recording writes on request, on EVERY host (the
            //    'FDPC' worker it replaced lived on SimHost only, so CGF, IG, Stride, traffic and the editor took none).
            //    ⭐ B4 (§1h): and the RESTORE — so it is handed everything a restore touches (the silent-default rule).
            slave.RegisterHandler(new ReferenceCheckpointHandler(
                () => controller.ActiveRecordingModule, controller, h.World, h.EventAccumulator, worldReplaced, h.StagingRoot,
                resumeSchedule: h.Kernel.ResumeAfterRestore));   // ⭐ CE-3316

            // 2b. ⭐ CE-3329 P3 — During Action Review: the SAME controller (the replay of the OPEN recording), the SAME gates (the replay
            //     load's), the checkpoint's return (PutBack) — every dependency a restore touches, handed over (the silent-default rule).
            slave.RegisterHandler(new ReferenceReviewHandler(
                controller, () => controller.ActiveRecordingModule, replayLoad, h.World, h.EventAccumulator, worldReplaced,
                resumeSchedule: h.Kernel.ResumeAfterRestore, storageDirectory: h.StagingRoot,
                episodeRecorders: () => controller.EpisodeRecorders));   // ⭐ CE-3329 row 44 — a review suspends them too

            // 3. Dry run (D14) — this node puts back its OWN world, id pool, entity map and ELM queues (HN-017/HN-018).
            var rewindables = new List<IPreviewRewindable>();
            if (h.IdAllocator != null)
                rewindables.Add(PreviewParticipants.IdAllocator(h.IdAllocator));
            rewindables.Add(h.EntityMap != null
                ? PreviewParticipants.EntityMap(h.EntityMap)
                : PreviewParticipants.EntityMapFromRepository(h.World));
            if (h.Elm != null)
                rewindables.Add(PreviewParticipants.LifecycleModule(h.Elm));
            var dryRun = new ReferencePreviewHandler(h.World, rewindables, includeTransient: h.DryRunIncludesTransient, accumulator: h.EventAccumulator);
            slave.RegisterHandler(dryRun);

            // 4. Prefetch — stage scenario files and ACK.
            slave.RegisterHandler(new ReferencePrefetchHandler(new LocalDiskStorageProvider(h.StagingRoot)));

            // 5. Terrain / zone ops (D3) — on every ECS host; a host with nothing to make resident still ACKs.
            TerrainAssetRegistrar.Register(slave, h.TerrainService, h.World, h.NodeId, h.TerrainStagingRoot);

            // 6. The host's own scenario handlers (episode load).
            foreach (var handler in h.ScenarioHandlers)
                slave.RegisterHandler(handler);

            // 6b. ⭐⭐ CE-4093 (AQ108 D20) — the unsaved-changes check, ONE per node: its state lives HERE, in the host composition, never in the ECS
            //     world (a baseline in the world would itself change it). The handler answers a master's CHECK ROUND; the load chain (7) takes the
            //     baseline when a load into editing resolves — a late joiner's catch-up is the same chain — and the save wrapper (8) after a
            //     completed save round: every host passes through all three by construction.
            var changeCheck = new Fdp.Toolkit.Replay.WorldChangeCheck();
            slave.RegisterHandler(new WorldChangeCheckHandler(changeCheck, h.World));

            // 7. The load-phase chain (L2) — once, after every provider is known; before live load, which keeps FinalizeLive.
            slave.RegisterHandler(LoadPhaseChain.FromRoles(
                h.Role, h.LoadProviders, h.World,
                recordingController: controller,
                storageDirectory:    h.StagingRoot,
                hostLabel:           h.HostLabel,
                entityMap:           h.EntityMap,
                changeCheck:         changeCheck));

            // 8. Scenario save + recording archive (CE-279 Layer A). ⭐ CE-4093 — a save handler is wrapped so the node's part of a completed save
            //    round re-baselines the check.
            SerializeLocalRegistrar.Register(slave,
                h.ScenarioSaveHandler is null ? null : new BaselineAfterSaveHandler(h.ScenarioSaveHandler, changeCheck, h.World),
                h.StagingRoot, h.NodeId);

            // 9. Live load — LAST: FinalizeLive, and a cold PrepareLive nothing above claimed.
            slave.RegisterHandler(new ReferenceLiveLoadHandler(controller, h.StagingRoot));

            return new EcsHostClusterHandlers(controller, dryRun, changeCheck);
        }
    }
}
