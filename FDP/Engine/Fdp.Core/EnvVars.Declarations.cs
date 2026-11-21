using System;

namespace Fdp.Core
{
    /// <summary>
    /// ⭐ CE-4586 — the declarations: one <c>EnvVar</c> per name, grouped by area. A name used anywhere in the sources (tests included) is declared here;
    /// <c>EveryEnvReadIsRegisteredTests</c> fails on one that is not. Add yours here, then read it with <c>EnvVars.Read(EnvVars.YourName)</c>.
    /// </summary>
    public static partial class EnvVars
    {

        // ── Threading and performance switches (the Performance menu / PerformanceSettings choose them at process start) ──
        public static readonly EnvVar HrotParallelSim = new("HROT_PARALLEL_SIM", EnvCategory.Switch, "0|1", "", "per-entity parallel phases on; the performance settings set it, an explicit value overrides them", "DESIGN_City_Battle_Showcase.md");
        public static readonly EnvVar HrotParallelSync = new("HROT_PARALLEL_SYNC", EnvCategory.Switch, "0|1", "0", "parallel world-to-world sync of the replica worlds", "DESIGN_Parallel_Simulation.md");
        public static readonly EnvVar HrotThreadedKernel = new("HROT_THREADED_KERNEL", EnvCategory.Switch, "0|1|2|3", "", "threaded kernel mode: 1 engaged, 2 two worlds, 3 composed worlds; an explicit value overrides the performance settings", "DESIGN_Threaded_Review_Source.md");
        public static readonly EnvVar HrotThreadedSingle = new("HROT_THREADED_SINGLE", EnvCategory.Switch, "0|1", "0", "composed worlds on a single thread (measures the composition cost alone)", "DESIGN_Threaded_Work_Inventory.md");
        public static readonly EnvVar HrotTwoWorldsLockTick = new("HROT_TWO_WORLDS_LOCK_TICK", EnvCategory.Switch, "0|1", "0", "two-worlds mode holds one lock across the whole tick (measuring)", "STATUS_Threaded_POC_2.md");
        public static readonly EnvVar HrotFairHandover = new("HROT_FAIR_HANDOVER", EnvCategory.Switch, "0|1", "0", "fair handover between the sim and the presentation in the threaded kernel", "DESIGN_Threaded_Work_Inventory.md");
        public static readonly EnvVar HrotSimThreads = new("HROT_SIM_THREADS", EnvCategory.Switch, "int", "", "worker thread count of the shared simulation worker pool; an explicit value overrides the performance settings", "DESIGN_Parallel_Simulation.md");
        public static readonly EnvVar HrotSerialBrains = new("HROT_SERIAL_BRAINS", EnvCategory.Switch, "0|1", "0", "run the brain phase serial while the rest stays parallel (N-15 measuring switch)", "STATUS_Mode_And_Serial_Systems.md");
        public static readonly EnvVar HrotSerialSquads = new("HROT_SERIAL_SQUADS", EnvCategory.Switch, "0|1", "0", "run the squad-coordination phase serial while the rest stays parallel (N-15 measuring switch)", "STATUS_Overnight_Threaded_Build.md");
        public static readonly EnvVar HrotManagedReadStamp = new("HROT_MANAGED_READ_STAMP", EnvCategory.Switch, "0|1", "0", "managed reads stamp the change version (1) instead of the pure read path", "DESIGN_Parallel_Simulation.md");
        public static readonly EnvVar HrotEcoqos = new("HROT_ECOQOS", EnvCategory.Switch, "0|1", "0", "keep the Windows default (EcoQoS) power throttling instead of opting the process out", "Blueprint_Issues_Tracker.md");
        public static readonly EnvVar HrotChangeGate = new("HROT_CHANGE_GATE", EnvCategory.Switch, "0|1", "1", "change-gated systems skip unchanged work; 0 turns the gate off (A/B)", "DESIGN_Change_Gated_Systems.md");
        public static readonly EnvVar HrotEgressGate = new("HROT_EGRESS_GATE", EnvCategory.Switch, "0|1", "1", "egress visit gate skips unchanged entities; 0 turns it off (A/B)", "DESIGN_Change_Gated_Systems.md");
        public static readonly EnvVar HrotBpStampOnChange = new("HROT_BP_STAMP_ON_CHANGE", EnvCategory.Switch, "0|1", "0", "the blueprint tick stamps a component only when its value changed", "DESIGN_System_State_Inventory.md");
        public static readonly EnvVar HrotRecordBlockDeltas = new("HROT_RECORD_BLOCK_DELTAS", EnvCategory.Switch, "0|1", "1", "the recorder writes block deltas; 0 writes the older form (measuring)", "DESIGN_Replay_Playback_Performance.md");
        public static readonly EnvVar HrotEqsConfigGate = new("HROT_EQS_CONFIG_GATE", EnvCategory.Switch, "0|1", "0", "gate the EQS sensor-config egress translator", "DESIGN_System_State_Inventory.md");
        public static readonly EnvVar HrotEqsBudget = new("HROT_EQS_BUDGET", EnvCategory.Switch, "int", "built-in budget", "EQS solver budget units per tick", "DESIGN_Scale_Rig.md");
        public static readonly EnvVar HrotEqsWorkers = new("HROT_EQS_WORKERS", EnvCategory.Switch, "int", "worker pool size", "EQS solver worker count", "DESIGN_Sensors_And_Doctrine.md");
        public static readonly EnvVar HrotSoldierCadence = new("HROT_SOLDIER_CADENCE", EnvCategory.Switch, "float", "built-in cadence", "override the soldier brain evaluation cadence", "DESIGN_Evaluation_Cost_And_Cadence.md");
        public static readonly EnvVar HrotStressPeriod = new("HROT_STRESS_PERIOD", EnvCategory.Switch, "float", "built-in period", "override the stress system step period", "DESIGN_System_State_Inventory.md");
        public static readonly EnvVar HrotReplayPrefetch = new("HROT_REPLAY_PREFETCH", EnvCategory.Switch, "int", "built-in depth", "replay playback prefetch depth in frames", "DESIGN_Replay_Playback_Performance.md");
        public static readonly EnvVar HrotReplayFullRepair = new("HROT_REPLAY_FULL_REPAIR", EnvCategory.Switch, "0|1", "0", "replay playback forces a full world repair on a seek (measuring)", "MEASURE_Ui_Replay_Frame_Cost.md");
        public static readonly EnvVar HrotHeadlessMaxHz = new("HROT_HEADLESS_MAX_HZ", EnvCategory.Switch, "int", "uncapped", "cap a headless node's tick rate in Hz", "DESIGN_Scale_Rig.md");
        public static readonly EnvVar FdpEgressOffframe = new("FDP_EGRESS_OFFFRAME", EnvCategory.Switch, "0|1", "0", "run the egress pipeline off the frame thread", "DESIGN_Egress_Change_Detection.md");
        public static readonly EnvVar FdpEgressWorkers = new("FDP_EGRESS_WORKERS", EnvCategory.Switch, "int", "built-in", "egress pipeline worker count", "DESIGN_Egress_Change_Detection.md");
        public static readonly EnvVar FdpTickmodeGate = new("FDP_TICKMODE_GATE", EnvCategory.Switch, "0|1", "1", "tick-mode gate on component access; 0 bisects a live run too", "");
        public static readonly EnvVar FdpFailFast = new("FDP_FAIL_FAST", EnvCategory.Switch, "0|1", "1", "fail fast on a module fault; 0 / false / off keeps going", "Fdp.ModuleHost.md");
        public static readonly EnvVar FdpSlowWallclock = new("FDP_SLOW_WALLCLOCK", EnvCategory.Switch, "0|1", "1", "slow-module detection by wall clock; 0 uses sim time", "DESIGN_Scale_Rig.md");

        // ── Product behaviour: ports, paths, hosts, features ──
        public static readonly EnvVar HrotDebugApiPort = new("HROT_DEBUG_API_PORT", EnvCategory.Switch, "port", "", "start the HTTP debug API on this port", "DESIGN_Stride_Node_Modes.md");
        public static readonly EnvVar HrotPanelCapture = new("HROT_PANEL_CAPTURE", EnvCategory.Switch, "0|1", "1", "panel snapshot capture for the debug API; 0 turns it off", "DESIGN_Replay_Playback_Performance.md");
        public static readonly EnvVar HrotViewFilter = new("HROT_VIEW_FILTER", EnvCategory.Switch, "0|1", "1", "the map view filter; 0 turns it off", "DESIGN_Map_View_Filter.md");
        public static readonly EnvVar HrotAssetAuthoring = new("HROT_ASSET_AUTHORING", EnvCategory.Switch, "string", "", "asset-authoring mode of the cluster load", "DESIGN_Asset_Management.md");
        public static readonly EnvVar HrotAudio = new("HROT_AUDIO", EnvCategory.Switch, "0|1", "1", "the audio layer; 0 disables audio", "DESIGN_FPS_Audio.md");
        public static readonly EnvVar HrotAudioPanFlip = new("HROT_AUDIO_PAN_FLIP", EnvCategory.Switch, "0|1", "0", "flip the stereo pan direction (1 = flipped)", "DESIGN_FPS_Audio.md");
        public static readonly EnvVar HrotHiddenWindow = new("HROT_HIDDEN_WINDOW", EnvCategory.Switch, "0|1", "0", "create the Raylib window hidden (frames drawn off screen)", "CAPABILITIES.md");
        public static readonly EnvVar HrotImguiRenderer = new("HROT_IMGUI_RENDERER", EnvCategory.Switch, "string", "", "ImGui draw-data renderer backend", "DESIGN_ImGui_Draw_Data_Renderer.md");
        public static readonly EnvVar HrotNavtileCache = new("HROT_NAVTILE_CACHE", EnvCategory.Switch, "path", "local app data/Hrot/navtiles", "folder of the navigation tile cache", "DESIGN_Terrain_Import_UTM.md");
        public static readonly EnvVar HrotKeepOverlays = new("HROT_KEEP_OVERLAYS", EnvCategory.Switch, "0|1", "0", "keep the staged terrain overlays after use", "DESIGN_Terrain_Authoring.md");
        public static readonly EnvVar HrotOsmTileUrl = new("HROT_OSM_TILE_URL", EnvCategory.Switch, "url", "built-in OSM source", "map tile source URL template", "DESIGN_Terrain_From_OSM.md");
        public static readonly EnvVar HrotTransferShare = new("HROT_TRANSFER_SHARE", EnvCategory.Switch, "path", "", "shared folder used by the file transfer between nodes", "Architect_Question_91_Player_Nodes.md");
        public static readonly EnvVar HrotSumoHome = new("HROT_SUMO_HOME", EnvCategory.Switch, "path", "", "SUMO install folder used for netconvert", "DESIGN_Terrain_Authoring.md");
        public static readonly EnvVar HrotSumoNoLocal = new("HROT_SUMO_NO_LOCAL", EnvCategory.Switch, "0|1", "0", "ignore the repo-local SUMO install", "DESIGN_Terrain_Import_UTM.md");
        public static readonly EnvVar FdpStagingRoot = new("FDP_STAGING_ROOT", EnvCategory.Switch, "path", "data root", "root of the exercise staging area", "RUNBOOK_Filming.md");
        public static readonly EnvVar FdpKeepExercises = new("FDP_KEEP_EXERCISES", EnvCategory.Switch, "int", "built-in", "how many exercise caches to keep", "DESIGN_Checkpoint_And_Dry_Run_Restore.md");
        public static readonly EnvVar FdpFakeInitFrames = new("FDP_FAKE_INIT_FRAMES", EnvCategory.Switch, "int", "0", "fake extra initialisation frames in the replication module", "DESIGN_Cross_Node_Construction_Barrier.md");
        public static readonly EnvVar HrotBuildSha = new("HROT_BUILD_SHA", EnvCategory.Switch, "string", "", "build commit stamped into the director's output", "");
        public static readonly EnvVar HrotVerify = new("HROT_VERIFY", EnvCategory.Switch, "string", "", "scenario list to verify from the command line, then exit", "DESIGN_Scenario_Regression.md");
        public static readonly EnvVar HrotVerifyBranchAt = new("HROT_VERIFY_BRANCH_AT", EnvCategory.Switch, "float", "0", "verify: branch the run live at this time", "");
        public static readonly EnvVar HrotVerifyCapture = new("HROT_VERIFY_CAPTURE", EnvCategory.Switch, "0|1", "0", "verify: capture the golden", "");
        public static readonly EnvVar HrotVerifyR1 = new("HROT_VERIFY_R1", EnvCategory.Switch, "json", "", "verify: the restore-check specification", "DESIGN_Checkpoint_And_Dry_Run_Restore.md");
        public static readonly EnvVar HrotVerifyReplay = new("HROT_VERIFY_REPLAY", EnvCategory.Switch, "0|1", "0", "verify: run live against its own recording", "");
        public static readonly EnvVar HrotVerifyReport = new("HROT_VERIFY_REPORT", EnvCategory.Switch, "path", "", "verify: where to write the report", "");
        public static readonly EnvVar HrotVerifyReview = new("HROT_VERIFY_REVIEW", EnvCategory.Switch, "json", "", "verify: the review-check specification", "DESIGN_Scenario_Regression.md");
        public static readonly EnvVar StrideVehicleNavmesh = new("STRIDE_VEHICLE_NAVMESH", EnvCategory.Switch, "0|1", "0", "Stride vehicle navigation intent uses the navmesh", "");
        public static readonly EnvVar StrideSelftest = new("STRIDE_SELFTEST", EnvCategory.Switch, "string", "", "Stride app self-test mode", "");
        public static readonly EnvVar StrideEditorWindow = new("STRIDE_EDITOR_WINDOW", EnvCategory.Switch, "string", "", "Stride inspector window placement", "");

        // ── Diagnostics: counters, traces, probes and checks (the product behaves the same) ──
        public static readonly EnvVar HrotFrameStats = new("HROT_FRAME_STATS", EnvCategory.Diagnostic, "0|1", "0", "print per-frame statistics", "DESIGN_Map_View_Filter.md");
        public static readonly EnvVar HrotQueryTimings = new("HROT_QUERY_TIMINGS", EnvCategory.Diagnostic, "0|1", "0", "time ECS queries (R-4213)", "STATUS_Pre_Build_Measurements.md");
        public static readonly EnvVar HrotQuerySample = new("HROT_QUERY_SAMPLE", EnvCategory.Diagnostic, "int", "16", "query timings sample one in N", "DESIGN_Parallel_Simulation.md");
        public static readonly EnvVar HrotWriteStats = new("HROT_WRITE_STATS", EnvCategory.Diagnostic, "0|1", "0", "count component-table writes", "Blueprint_Issues_Tracker.md");
        public static readonly EnvVar HrotWriteStatsNotes = new("HROT_WRITE_STATS_NOTES", EnvCategory.Diagnostic, "0|1", "0", "write stats also note who wrote (occurrence store)", "STATUS_Parallel_Sim_POC_3.md");
        public static readonly EnvVar HrotWriteStatsStacks = new("HROT_WRITE_STATS_STACKS", EnvCategory.Diagnostic, "type name", "", "write stats record stacks for this component type", "STATUS_Parallel_Sim_POC_2.md");
        public static readonly EnvVar HrotContentVersion = new("HROT_CONTENT_VERSION", EnvCategory.Diagnostic, "0|1", "0", "experiment leftover: count content-version bumps (parallel-sim POC)", "DESIGN_Parallel_Simulation.md");
        public static readonly EnvVar HrotAccessTrace = new("HROT_ACCESS_TRACE", EnvCategory.Diagnostic, "0|1", "0", "trace component access per system", "STATUS_Threaded_Analysis_Instruments.md");
        public static readonly EnvVar HrotAccessTraceOut = new("HROT_ACCESS_TRACE_OUT", EnvCategory.Diagnostic, "path", "temp dir", "access-trace output file", "STATUS_Overnight_Threaded_Build.md");
        public static readonly EnvVar HrotAccessTraceCtl = new("HROT_ACCESS_TRACE_CTL", EnvCategory.Diagnostic, "path", "<out>.ctl", "access-trace control file", "");
        public static readonly EnvVar HrotEntityCensus = new("HROT_ENTITY_CENSUS", EnvCategory.Diagnostic, "0|1", "0", "per-system entity access census", "DESIGN_Parallel_Simulation.md");
        public static readonly EnvVar HrotEntityCensusOut = new("HROT_ENTITY_CENSUS_OUT", EnvCategory.Diagnostic, "path", "temp dir", "entity-census output file", "");
        public static readonly EnvVar HrotMemoryCensus = new("HROT_MEMORY_CENSUS", EnvCategory.Diagnostic, "0|1", "0", "memory census of the component tables", "HANDOFF_Coordinator_To_Backend_POC_Combined.md");
        public static readonly EnvVar HrotMemoryCensusOut = new("HROT_MEMORY_CENSUS_OUT", EnvCategory.Diagnostic, "path", "temp dir", "memory-census output file", "");
        public static readonly EnvVar HrotWorldGuard = new("HROT_WORLD_GUARD", EnvCategory.Diagnostic, "0|1", "0", "world guard: a write off the owning thread is reported (1 strict = throws)", "DESIGN_City_Battle_Showcase.md");
        public static readonly EnvVar HrotWorldGuardOut = new("HROT_WORLD_GUARD_OUT", EnvCategory.Diagnostic, "path", "temp dir", "world-guard report file", "STATUS_Threaded_POC_2.md");
        public static readonly EnvVar HrotReadCheck = new("HROT_READ_CHECK", EnvCategory.Diagnostic, "0|1|2", "0", "parallel read check: an undeclared cross-entity read throws (2 lists them all)", "DESIGN_City_Battle_Showcase.md");
        public static readonly EnvVar HrotReadCheckOut = new("HROT_READ_CHECK_OUT", EnvCategory.Diagnostic, "path", "", "parallel read-check survey output file", "DESIGN_Parallel_Simulation.md");
        public static readonly EnvVar HrotManagedMutationCheck = new("HROT_MANAGED_MUTATION_CHECK", EnvCategory.Diagnostic, "0|1", "0", "detect in-place mutation of managed components", "STATUS_Managed_Reads_Audit.md");
        public static readonly EnvVar HrotManagedMutationOut = new("HROT_MANAGED_MUTATION_OUT", EnvCategory.Diagnostic, "path", "temp dir", "managed-mutation / stamp-trace report directory", "STATUS_Managed_Reads_Audit.md");
        public static readonly EnvVar HrotStampTrace = new("HROT_STAMP_TRACE", EnvCategory.Diagnostic, "type list", "", "trace change stamps of these component types", "DESIGN_Parallel_Simulation.md");
        public static readonly EnvVar HrotLasttickTypes = new("HROT_LASTTICK_TYPES", EnvCategory.Diagnostic, "type list", "", "probe last-tick values of these component types", "STATUS_Entity_Access_Census.md");
        public static readonly EnvVar HrotNanCheck = new("HROT_NAN_CHECK", EnvCategory.Diagnostic, "0|1", "0", "check transforms for non-finite values", "STATUS_Overnight_Threaded_Build.md");
        public static readonly EnvVar HrotPublishProbe = new("HROT_PUBLISH_PROBE", EnvCategory.Diagnostic, "0|1", "0", "experiment leftover: probe what the publish phase copies", "STATUS_Threaded_Analysis_Instruments.md");
        public static readonly EnvVar HrotPublishProbeEvery = new("HROT_PUBLISH_PROBE_EVERY", EnvCategory.Diagnostic, "int", "30", "experiment leftover: publish probe sampling period in ticks", "");
        public static readonly EnvVar HrotPublishProbeOut = new("HROT_PUBLISH_PROBE_OUT", EnvCategory.Diagnostic, "path", "temp dir", "experiment leftover: publish probe output file", "");
        public static readonly EnvVar HrotPublishProbeMasks = new("HROT_PUBLISH_PROBE_MASKS", EnvCategory.Diagnostic, "path", "", "experiment leftover: publish probe component masks file", "STATUS_Threaded_Analysis_Instruments.md");
        public static readonly EnvVar HrotReplicaProbe = new("HROT_REPLICA_PROBE", EnvCategory.Diagnostic, "0|1", "0", "experiment leftover: replica sync probe", "DESIGN_Egress_Change_Detection.md");
        public static readonly EnvVar HrotContactProbe = new("HROT_CONTACT_PROBE", EnvCategory.Diagnostic, "0|1", "0", "experiment leftover: contact / cover probe", "Blueprint_Issues_Tracker.md");
        public static readonly EnvVar HrotContactProbeDetail = new("HROT_CONTACT_PROBE_DETAIL", EnvCategory.Diagnostic, "0|1", "0", "experiment leftover: contact probe per-entity detail", "Blueprint_Issues_Tracker.md");
        public static readonly EnvVar HrotContactProbeOut = new("HROT_CONTACT_PROBE_OUT", EnvCategory.Diagnostic, "path", "temp dir", "experiment leftover: contact probe output file", "");
        public static readonly EnvVar HrotEgressGateStats = new("HROT_EGRESS_GATE_STATS", EnvCategory.Diagnostic, "0|1", "0", "print the egress visit-gate statistics", "DESIGN_Egress_Change_Detection.md");
        public static readonly EnvVar HrotReplayStats = new("HROT_REPLAY_STATS", EnvCategory.Diagnostic, "0|1", "0", "print replay recorder / playback statistics", "MEASURE_Ui_Replay_Frame_Cost.md");
        public static readonly EnvVar HrotInsyncLog = new("HROT_INSYNC_LOG", EnvCategory.Diagnostic, "0|1", "0", "log cluster in-sync and hot-reload timing", "STATUS_Pre_Build_Measurements.md");
        public static readonly EnvVar HrotPoc3Walk = new("HROT_POC3_WALK", EnvCategory.Diagnostic, "string", "", "experiment leftover: POC-3 world walk mode", "DESIGN_Threaded_Work_Inventory.md");
        public static readonly EnvVar HrotPoc3WalkOut = new("HROT_POC3_WALK_OUT", EnvCategory.Diagnostic, "path", "", "experiment leftover: POC-3 world walk output file", "");
        public static readonly EnvVar HrotTwoWorldsMasks = new("HROT_TWO_WORLDS_MASKS", EnvCategory.Diagnostic, "path", "", "experiment leftover: two-worlds component masks file", "STATUS_Threaded_POC_2.md");
        public static readonly EnvVar HrotTwoWorldsMask = new("HROT_TWO_WORLDS_MASK", EnvCategory.Diagnostic, "string", "ui-no-inspector", "experiment leftover: two-worlds mask name", "STATUS_Threaded_POC_2.md");
        public static readonly EnvVar HrotImguiAb = new("HROT_IMGUI_AB", EnvCategory.Diagnostic, "path", "", "write ImGui frames to this folder for an A/B compare", "DESIGN_ImGui_Draw_Data_Renderer.md");
        public static readonly EnvVar FdpTrackRepoLeaks = new("FDP_TRACK_REPO_LEAKS", EnvCategory.Diagnostic, "0|1", "0", "track undisposed entity repositories", "Blueprint_Issues_Tracker.md");
        public static readonly EnvVar FdpGizmoIdentityStrict = new("FDP_GIZMO_IDENTITY_STRICT", EnvCategory.Diagnostic, "0|1", "0", "a gizmo anchor identity violation throws instead of logging", "DESIGN_Gizmo_Anchor_Identity.md");

        // ── Tests and rails (read only by tests) ──
        public static readonly EnvVar HrotRailShots = new("HROT_RAIL_SHOTS", EnvCategory.Test, "path", "", "a frame rail writes its rendered frames here", "DESIGN_Terrain_Authoring.md");
        public static readonly EnvVar HrotRulesShots = new("HROT_RULES_SHOTS", EnvCategory.Test, "path", "temp dir", "the rules-window rails write their frames here", "");
        public static readonly EnvVar HrotFrameScreenshots = new("HROT_FRAME_SCREENSHOTS", EnvCategory.Test, "path", "temp dir", "the editor frame rails write their frames here", "");
        public static readonly EnvVar HrotTimelineShots = new("HROT_TIMELINE_SHOTS", EnvCategory.Test, "path", "temp dir", "the timeline panel rail writes its frames here", "DESIGN_Director_Timeline_Editor.md");
        public static readonly EnvVar HrotTimelineAssemblyDir = new("HROT_TIMELINE_ASSEMBLY_DIR", EnvCategory.Test, "path", "", "timeline index test: folder of the assemblies", "");
        public static readonly EnvVar HrotTimelineRecording = new("HROT_TIMELINE_RECORDING", EnvCategory.Test, "path", "", "timeline index test: recording to index", "");
        public static readonly EnvVar HrotFilmProof = new("HROT_FILM_PROOF", EnvCategory.Test, "0|1", "0", "run the film proof test", "DESIGN_Director_Timeline_Editor.md");
        public static readonly EnvVar HrotFilmProofAt = new("HROT_FILM_PROOF_AT", EnvCategory.Test, "float", "9.1", "film proof: time", "DESIGN_Director_Timeline_Editor.md");
        public static readonly EnvVar HrotFilmProofFrom = new("HROT_FILM_PROOF_FROM", EnvCategory.Test, "string", "Tank", "film proof: the observing unit", "");
        public static readonly EnvVar HrotFilmProofKey = new("HROT_FILM_PROOF_KEY", EnvCategory.Test, "int", "0", "film proof: key index", "");
        public static readonly EnvVar HrotFilmProofOut = new("HROT_FILM_PROOF_OUT", EnvCategory.Test, "path", "", "film proof: output folder", "DESIGN_Director_Timeline_Editor.md");
        public static readonly EnvVar HrotFilmProofRecording = new("HROT_FILM_PROOF_RECORDING", EnvCategory.Test, "path", "", "film proof: recording", "DESIGN_Director_Timeline_Editor.md");
        public static readonly EnvVar HrotFilmProofScenario = new("HROT_FILM_PROOF_SCENARIO", EnvCategory.Test, "string", "", "film proof: scenario", "");
        public static readonly EnvVar HrotFilmProofTake = new("HROT_FILM_PROOF_TAKE", EnvCategory.Test, "string", "", "film proof: take", "");
        public static readonly EnvVar HrotFilmProofUnit = new("HROT_FILM_PROOF_UNIT", EnvCategory.Test, "string", "Enemy Tank", "film proof: the filmed unit", "");
        public static readonly EnvVar HrotFilmWindow = new("HROT_FILM_WINDOW", EnvCategory.Test, "0|1", "0", "film tests open a real window", "DESIGN_Director_Timeline_Editor.md");
        public static readonly EnvVar HrotModeRailWindow = new("HROT_MODE_RAIL_WINDOW", EnvCategory.Test, "int", "10", "mode startup rails: how long to watch a node", "DESIGN_Stride_Port.md");
        public static readonly EnvVar HrotGoldenUpdate = new("HROT_GOLDEN_UPDATE", EnvCategory.Test, "0|1", "0", "regenerate a golden file instead of comparing", "");
        public static readonly EnvVar HrotCompositionCapture = new("HROT_COMPOSITION_CAPTURE", EnvCategory.Test, "0|1", "0", "capture the host composition snapshots", "DESIGN_Subsystem_Composition_Unification.md");
        public static readonly EnvVar HrotBehaviourTable = new("HROT_BEHAVIOUR_TABLE", EnvCategory.Test, "path", "", "behaviour hosting snapshot: table file", "");
        public static readonly EnvVar HrotRecordHosting = new("HROT_RECORD_HOSTING", EnvCategory.Test, "0|1", "0", "rewrite the behaviour hosting golden", "");
        public static readonly EnvVar HrotA0Dump = new("HROT_A0_DUMP", EnvCategory.Test, "path", "", "world change check: dump the two saves here", "");
        public static readonly EnvVar HrotDirectorIdentityReport = new("HROT_DIRECTOR_IDENTITY_REPORT", EnvCategory.Test, "0|1", "0", "director identity rail prints its report", "");
        public static readonly EnvVar HrotReplayEquivOut = new("HROT_REPLAY_EQUIV_OUT", EnvCategory.Test, "path", "", "managed-reads replay equivalence: output folder", "");
        public static readonly EnvVar HrotDisableClickLatch = new("HROT_DISABLE_CLICK_LATCH", EnvCategory.Test, "0|1", "0", "disable the Win32 click latch (also read by the product on Windows)", "");
        public static readonly EnvVar HrotEditorDll = new("HROT_EDITOR_DLL", EnvCategory.Test, "path", "", "editor assembly the system tests launch", "");
        public static readonly EnvVar HrotSystemTests = new("HROT_SYSTEM_TESTS", EnvCategory.Test, "0|1", "0", "enable the system (E2E) tests", "");
        public static readonly EnvVar HrotSystemTestsBootTimeout = new("HROT_SYSTEM_TESTS_BOOT_TIMEOUT", EnvCategory.Test, "int", "built-in", "system tests: editor boot timeout in seconds", "");
        public static readonly EnvVar HrotRunFlaky = new("HROT_RUN_FLAKY", EnvCategory.Test, "0|1", "0", "run the tests marked Flaky", "Blueprint_Issues_Tracker.md");
        public static readonly EnvVar HrotRunExternalConformance = new("HROT_RUN_EXTERNAL_CONFORMANCE", EnvCategory.Test, "0|1", "0", "run the external-host conformance tests", "DESIGN_Cross_Node_Construction_Barrier.md");
        public static readonly EnvVar HrotRunMeasure = new("HROT_RUN_MEASURE", EnvCategory.Test, "0|1", "0", "run the measuring tests", "DESIGN_Terrain_Import_UTM.md");
        public static readonly EnvVar HrotRunTownBudget = new("HROT_RUN_TOWN_BUDGET", EnvCategory.Test, "0|1", "0", "run the town budget test", "DESIGN_Terrain_Authoring.md");
        public static readonly EnvVar HrotUpdateShippedTrees = new("HROT_UPDATE_SHIPPED_TREES", EnvCategory.Test, "0|1", "0", "regenerate the shipped trees", "Blueprint_Issues_Tracker.md");
        public static readonly EnvVar HrotAudioSmoke = new("HROT_AUDIO_SMOKE", EnvCategory.Test, "0|1", "0", "audio smoke test", "DESIGN_FPS_Audio.md");
        public static readonly EnvVar HrotExportSounds = new("HROT_EXPORT_SOUNDS", EnvCategory.Test, "path", "", "export the generated sounds here", "DESIGN_FPS_Audio.md");
        public static readonly EnvVar HrotGenCityBattle = new("HROT_GEN_CITY_BATTLE", EnvCategory.Test, "0|1", "0", "regenerate the city battle terrain", "DESIGN_Terrain_Authoring.md");
        public static readonly EnvVar HrotGenPersonBody = new("HROT_GEN_PERSON_BODY", EnvCategory.Test, "0|1", "0", "regenerate the person body golden", "DESIGN_Body_Parts_And_Hit_Location.md");
        public static readonly EnvVar HrotGenVehicleBody = new("HROT_GEN_VEHICLE_BODY", EnvCategory.Test, "0|1", "0", "regenerate the vehicle body golden", "DESIGN_Terrain_Authoring.md");
        public static readonly EnvVar HrotLiveDem = new("HROT_LIVE_DEM", EnvCategory.Test, "0|1", "0", "live DEM download test", "DESIGN_Terrain_From_OSM.md");
        public static readonly EnvVar HrotOsmLive = new("HROT_OSM_LIVE", EnvCategory.Test, "0|1", "0", "live OSM wizard test", "DESIGN_Terrain_From_OSM.md");
        public static readonly EnvVar HrotOsmLiveName = new("HROT_OSM_LIVE_NAME", EnvCategory.Test, "string", "", "live OSM test: terrain name", "");
        public static readonly EnvVar HrotOsmLiveNas = new("HROT_OSM_LIVE_NAS", EnvCategory.Test, "path", "", "live OSM test: folder to keep the result in", "");
        public static readonly EnvVar HrotOsmLivePlace = new("HROT_OSM_LIVE_PLACE", EnvCategory.Test, "string", "", "live OSM test: place to fetch", "");
        public static readonly EnvVar HrotOsmLiveSideM = new("HROT_OSM_LIVE_SIDE_M", EnvCategory.Test, "float", "", "live OSM test: side length in metres", "");
        public static readonly EnvVar HrotOsmRunDir = new("HROT_OSM_RUN_DIR", EnvCategory.Test, "path", "", "OSM test: input run folder", "");
        public static readonly EnvVar HrotOsmRunOut = new("HROT_OSM_RUN_OUT", EnvCategory.Test, "path", "", "OSM test: output folder", "");
        public static readonly EnvVar HrotMeasure = new("HROT_MEASURE", EnvCategory.Test, "0|1", "0", "opt-in measuring tests", "DESIGN_Map_3D_Mode.md");
        public static readonly EnvVar HrotMeasureCompile = new("HROT_MEASURE_COMPILE", EnvCategory.Test, "0|1", "0", "measure the overlay compile", "");
        public static readonly EnvVar HrotMeasureOut = new("HROT_MEASURE_OUT", EnvCategory.Test, "path", "temp dir", "measuring tests write here", "");
        public static readonly EnvVar HrotMeasureOverlay = new("HROT_MEASURE_OVERLAY", EnvCategory.Test, "0|1", "0", "measure the overlay merge", "DESIGN_Terrain_Authoring.md");
        public static readonly EnvVar HrotMeasureSignature = new("HROT_MEASURE_SIGNATURE", EnvCategory.Test, "0|1", "0", "measure the live overlay signature", "");
        public static readonly EnvVar HrotMeasureSoup = new("HROT_MEASURE_SOUP", EnvCategory.Test, "0|1", "0", "measure the large terrain triangle soup", "STATUS_Ui_Large_Terrain_Drawing.md");
        public static readonly EnvVar HrotRepo = new("HROT_REPO", EnvCategory.Test, "path", "", "repository root for a measuring test", "");
        public static readonly EnvVar HrotScaleRig = new("HROT_SCALE_RIG", EnvCategory.Test, "0|1", "0", "run the scale rig rail", "DESIGN_Scale_Rig.md");
        public static readonly EnvVar HrotScaleRigArgs = new("HROT_SCALE_RIG_ARGS", EnvCategory.Test, "string", "--terrain scale-1 --men 1000 --vehicles 50 --aircraft 10", "scale rig rail: node arguments", "");
        public static readonly EnvVar FdpRunPerfTests = new("FDP_RUN_PERF_TESTS", EnvCategory.Test, "0|1", "0", "run the perf tests", "");

        // ── Tools and non-variables ──
        public static readonly EnvVar HrotReachPerSystem = new("HROT_REACH_PER_SYSTEM", EnvCategory.Tool, "string", "", "reach analysis tool: per-system report specification", "N3_Shared_State_Per_System.md");
        public static readonly EnvVar HrotEditorGenerated = new("HROT_EDITOR_GENERATED", EnvCategory.Tool, "marker", "", "NOT an environment variable: marker comment in generated editor code", "Hrot.Utility.Editor.md");
        public static readonly EnvVar Fdp001 = new("FDP_001", EnvCategory.Tool, "diagnostic id", "", "NOT an environment variable: the behaviour parameter size analyzer's diagnostic id", "AI_DEV_GUIDE.md");
    }
}
