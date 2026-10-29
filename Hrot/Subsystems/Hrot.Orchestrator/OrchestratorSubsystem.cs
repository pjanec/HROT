using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Hrot.Orchestrator;
using Hrot.Common;
using Fdp.Core;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Runner;
using Fdp.Core.Logging;
using Fdp.Toolkit.Time.Controllers;
using Fdp.Toolkit.Time.Messages;
using ImGuiNET;
using Fdp.ModuleHost;
using Fdp.ModuleHost.Time;
using Fdp.ModuleHost.Diagnostics;
using Fdp.Toolkit.Diagnostics;
using Fdp.Core.Diagnostics;
using Hrot.Orchestrator.Windows;
using Hrot.Orchestrator.Panels;
using Hrot.Core.Network;
using Hrot.Core.Diagnostics;
using Hrot.Common.Diagnostics;
using Fdp.Core.Serialization;

namespace Hrot.Orchestrator;

/// <summary>
/// Hosts <see cref="ClusterMaster"/> (DDS control plane + ID allocator server) under the Runner process.
/// Bypasses <c>WaitingRoomCoordinator</c> — boots instantly; UI renders immediately with a banner while
/// mandatory nodes are not yet ready (CGF1-S0105).
/// </summary>
public sealed class OrchestratorSubsystem : ISubsystem, IWindowRegistrar, Hrot.Presentation.DebugApi.IProvidesDebugSurface,
    Hrot.UI.Common.RunMode.IProvidesRunControls
{
    /// <summary>
    /// ⭐⭐ <c>CE-4002</c> — the orchestrator's debug surface: the MASTER's roster (only the node hosting the
    /// <c>ClusterMaster</c> can answer it), and cluster transitions and cancels published onto the bus the master reads
    /// directly. 📄 <c>docs/DESIGN_Cluster_Membership_Use_Cases.md</c> §3.1. ⛔ No world, no clock drive, no
    /// architecture — this subsystem has no ECS kernel, and absence is reported, never fabricated.
    /// </summary>
    public Hrot.Presentation.DebugApi.ISubsystemDebugProvider? CreateDebugProvider()
        => new Hrot.Presentation.DebugApi.SubsystemDebugProvider(
            subsystemName:     Name,
            perspective:       "Orchestrator",
            master:            Hrot.Presentation.DebugApi.OrchestrationBusMasterControl.Via(() => _bus),   // ⭐ R-3052…R-3055 — the master reads this bus directly
            // ⚠ The master holds the WIRE enum; the provider speaks the toolkit's — kept numerically identical on purpose.
            clusterState:      () => _core is null ? null : (Fdp.Toolkit.Orchestration.ClusterState)(int)_core.Master.CurrentClusterState,
            clusterRoster:     () => _core?.Master.DescribeRoster(),
            clusterLoadNotice: () => _core?.Master.LoadNotice,   // ⭐⭐ CE-4087 — the load gate's text (GET /cluster/roster → load.notice)
            exercises:         () => _core?.UiCache.AvailableExercises,
            replayDuration:    () => _core?.UiCache.ReplayDuration,
            replayAxis:        () => _core?.UiCache.ReplayAxis,   // ⭐ CE-3329 R-3058
            runNotRecorded:    () => _core?.UiCache.RunNotRecorded,   // ⭐ CE-4090 D14
            recordLiveRunsOff: () => _core?.UiCache.RecordLiveRunsOff,
            armedMode:         () => _core?.UiCache.ArmedMode,   // ⭐ CE-3341 R-3068
            unsavedCheck:      id => _core?.UiCache.UnsavedCheckOutcomeFor(id),   // ⭐ CE-4093 D20
            scenarioDirty:     () => _core?.UiCache.LastUnsavedCheck?.Changed);

    // ── ⭐⭐⭐ CE-1439 (AQ108 §2.12) — the Orchestrator is an OPERATOR host: it composes the SAME run surfaces as the editor and CGF ───────────────────────────
    //   the transport + run choice (CE-1437's IProvidesRunControls offer), the scenario session (EditorScenarioSession with NO world — this host holds none, the entities live on the nodes)
    //   and, when the composer picks it as the owner of ▶, the scenario menu (⇒ the Start surface offers Open / New / Recording) with the shared pickers.
    private Hrot.UI.Common.Adapters.ClusterTimeTransportAdapter? _timeTransport;
    private Hrot.Editor.AiShared.Scenarios.EditorScenarioSession? _scenarioSession;
    private Hrot.Presentation.DebugApi.ISubsystemDebugProvider? _unsavedReadsProvider;
    private NodeEditor.UI.Picker.PickerRegistry? _pickers;
    private Hrot.Editor.AiShared.Catalog.ScenarioCatalogContributor? _scenarioCatalog;   // projects the master's scenario inventory into the picker's catalog
    private readonly Hrot.Editor.AiShared.Scenarios.ConfirmPromptController _newExerciseConfirm = new("orchestrator_new_exercise_confirm");

    /// <summary>⭐ CE-1439 — the run choice of this host (a rail's and the composer's read).</summary>
    internal Hrot.UI.Common.RunMode.RunModeChoice? RunChoiceForTest => _timeTransport?.RunChoice;

    /// <summary>⭐ CE-1439 — a rail replaces the picker seam (the real one is the shell <c>PickerRegistry.OpenPicker</c>, which needs an ImGui frame to show anything) to read WHAT the Start surface's buttons open.</summary>
    internal Action<NodeEditor.UI.Picker.PickerRequest, Action<NodeEditor.UI.Picker.PickerResult>>? PickerOpenerOverride { get; set; }

    private void OpenPicker(NodeEditor.UI.Picker.PickerRequest request, Action<NodeEditor.UI.Picker.PickerResult> onChosen)
        => (PickerOpenerOverride ?? _pickers!.OpenPicker)(request, onChosen);

    /// <summary>⭐ CE-1439 — the cluster-control panel this host's core built (a rail asserts it was handed THIS host's run surfaces).</summary>
    internal Hrot.Orchestrator.Panels.ClusterScenarioPanel? ScenarioPanelForTest => _core?.ScenarioPanel;

    /// <summary>⭐ CE-1439 — the scenario session of this host (a rail's read).</summary>
    internal Hrot.Editor.AiShared.Scenarios.EditorScenarioSession? ScenarioSessionForTest => _scenarioSession;

    Hrot.UI.Common.RunMode.RunHostKind Hrot.UI.Common.RunMode.IProvidesRunControls.HostKind => Hrot.UI.Common.RunMode.RunHostKind.Orchestrator;
    Hrot.UI.Common.Facades.ITimeTransportFacade? Hrot.UI.Common.RunMode.IProvidesRunControls.Transport => _timeTransport;
    Hrot.UI.Common.RunMode.RunModeChoice? Hrot.UI.Common.RunMode.IProvidesRunControls.RunChoice => _timeTransport?.RunChoice;

    // ⭐ Q86 (R-215) — the master and every process manager live in the ONE core the editor builds too.
    private OrchestratorCore? _core;
    private Fdp.Presentation.Abstractions.IFileDialogService? _fileDialogService;
    private ClusterConfiguration _config = ClusterConfiguration.Default;
    private ClusterSlave? _clusterSlave;

    // ── Unified event bus (HEXAG2-S001) ─────────────────────────────────────
    private FdpEventBus?                   _bus;
    // ── Factory-managed infrastructure handles (HEXAG2-S008) ─────────────
    private INetworkFactory?               _networkFactory;
    private IOrchestrationTranslator?      _translator;
    private IDisposable?                   _idAllocatorServerHandle;
    private IMasterTimeTranslators?        _timeTranslators;
    // ── Time controller (CGF1-A.1, BATCH-09) ─────────────────────────────
    // MasterSyncController unifies wall-clock advancement, barrier protocol, and stepping.
    private MasterSyncController?          _masterSync;
    private Fdp.Toolkit.Time.ITimeCommands? _timeCommands;   // Q86 §4-C: the core commands the clock through this

    /// <summary>Internal event bus exposed for test assertions on SwitchTimeModeEvent.</summary>
    internal FdpEventBus? TimeBusForTest => _bus;

    /// <summary>Internal test hook: exposes the <see cref="ClusterUiCache"/> for bus-unification assertions.</summary>
    internal ClusterUiCache? UiCacheForTest => _core?.UiCache;

    /// <summary>
    /// Internal test hook: exposes the <see cref="ClusterMaster"/> hosted by this subsystem so
    /// E2E test fixtures can inject <see cref="ClusterOpRequest"/> values via
    /// <see cref="ClusterMaster.HandleClusterOpRequest"/> and read cluster state.
    /// </summary>
    internal ClusterMaster? TestHook_ClusterMaster => _core?.Master;

    /// <summary>Internal test hook: current master sim time in seconds.</summary>
    internal double TestHook_CurrentSimTime => _masterSync?.GetCurrentState().TotalTime ?? 0.0;

    /// <summary>
    /// TestHook: the master controller's current time scale. Exposed so an integration test can
    /// assert what the SetTimeScale cluster op actually delivered without inferring it from an
    /// observed sim-time slope.
    /// </summary>
    internal float TestHook_TimeScale => _masterSync?.GetTimeScale() ?? 0.0f;

    /// <summary>
    /// ⭐⭐ <b>The ONE fact the debug API's ack-gate needs: is the master still awaiting step ACKs?</b>
    /// <para><see langword="null"/> ⇒ <b>this node hosts no master</b> (parameterless/headless construction, or
    /// after <see cref="Shutdown"/> disposes it) ⇒ a step cannot be confirmed cluster-wide here.
    /// <see langword="true"/>/<see langword="false"/> ⇒ the master's own answer.</para>
    /// <para>⭐ Deliberately the NARROWEST surface rather than the controller itself:
    /// <see cref="MasterSyncController"/> also exposes <c>Step</c>/<c>SetTimeScale</c>, and handing those to the
    /// debug host would invite it to drive time directly — bypassing the perspective-scoped drive facade that
    /// <c>Architect_Question_54</c> Q54-2 established ("issue where the user is, confirm where the truth is").</para>
    /// <para>⚠ Read LIVE, never latched: <c>_masterSync</c> is created in <see cref="Initialize"/> and set back to
    /// <see langword="null"/> in <see cref="Shutdown"/>, so a captured reference would outlive the master and lie.</para>
    /// </summary>
    public bool? IsAwaitingStepAcks => _masterSync?.IsAwaitingStepAcks;

    public string Name => "Orchestrator";

    public System.Numerics.Vector4 TitleBarColor => new(0.72f, 0.64f, 0.47f, 1f);  // S0501: beige

    // used by tests
    public OrchestratorSubsystem()
    {
    }


    // used by ClusterMaster (HEXAG2-S008: factory-based constructor)
    public OrchestratorSubsystem(INetworkFactory networkFactory)
    {
        _networkFactory = networkFactory;
    }

    public void Initialize(SubsystemConfig config)
    {
        _config = ClusterConfiguration.LoadFromWorkingDirectory();

        // HEXAG2-S008: Use INetworkFactory to create the participant.
        // Parameterless constructor (headless/test mode) leaves _networkFactory null;
        // in that case factory calls return Null-object implementations via ?. / ?? operator.
        if (_networkFactory != null)
        {
            _networkFactory = _networkFactory.ConfigureForNode(_networkFactory.Participant, config.NodeId, NodeRole.None);
        }

        // ── Single unified event bus (HEXAG2-S001) ────────────────────────────────
        _bus          = new FdpEventBus();
        Fdp.Toolkit.Orchestration.OrchestrationEventRegistry.RegisterAll(_bus);
        OrchestratorEventRegistry.RegisterInternalEvents(_bus);
        int orchestratorNodeId = config.NodeId != 0 ? config.NodeId : 300;

        _translator    = _networkFactory?.CreateOrchestratorTranslators(_bus, config.NodeId)
                         ?? new NullOrchestrationTranslator();
        _idAllocatorServerHandle = _networkFactory?.CreateIdAllocatorServer()
                                   ?? new NullDisposable();

        // ── Time controller setup (CGF1-A.1, BATCH-09) ─────────────────────
        // Must be created before _timeTranslators so the initial SwitchTimeModeEvent is published to _bus
        // PENDING. Swap it immediately so the first ScanAndPublish can forward it to DDS before slaves start.
        // ⭐⭐⭐ CE-101 — BOOT PAUSED. 🔒 User, `2026-08-28`: *"simulation time is running from the beginning.
        //    Undesired, should start paused."* 📄 §5c.16.
        // ⭐ Q86: THIS host owns the clock — it creates, advances (Update) and disposes it. The core only asks.
        _masterSync       = new MasterSyncController(
            _bus, new HashSet<int>(), TimeConfig.Default, startPaused: true);
        _timeCommands     = new Fdp.Toolkit.Time.IntentTimeCommands(_bus);   // Q86 §4-C

        // ⭐⭐ Q86 — the ONE orchestrator core (the editor builds the same one).
        _fileDialogService = Fdp.Presentation.Panels.FileDialogServiceFactory.Create();
        _core = new OrchestratorCore(new OrchestratorCoreOptions
        {
            Bus               = _bus,
            Config            = _config,
            TimeCommands      = _timeCommands,
            TimeReads         = _masterSync,
            StagingNodeId     = orchestratorNodeId,
            // ⭐⭐⭐ HN-037 — type-tested: only the NED factory hosts a real authority (docs/DESIGN_Deterministic_Network_Ids.md §11).
            IdAuthority       = _idAllocatorServerHandle as Fdp.Toolkit.NetworkSpawning.IWorldIdAuthority,
            FileDialogService = _fileDialogService,
            DdsTransfer       = _networkFactory?.CreateFileTransferProvider(),   // ⭐ P6 part 2 — DDS to nodes with no share
        });

        // The orchestrator is also a cluster NODE (diagnostics dump) — host-only, not part of the core.
        _clusterSlave = new ClusterSlave(orchestratorNodeId, "Orchestrator", _bus);
        string isolatedTempRoot = OrchestrationConstants.GetNodeStagingRoot(orchestratorNodeId);
        string resolvedLogDir = System.IO.Path.Combine(System.AppContext.BaseDirectory, "logs");
        var orchestratorLogService = new LogArchiveExtractionService(
            resolvedLogDir,
            "Orchestrator",
            orchestratorNodeId);
        _clusterSlave.RegisterHandler(new DiagnosticsDumpClusterOpHandler(
            new OrchestratorNullDiagnosticEventHistoryService(),
            new ArchitectureDiagnosticsService(() => null),
            new OrchestratorNullEntityStateExtractionService(),
            orchestratorLogService,
            new Hrot.Common.Infrastructure.HrotNodeConfig
            {
                NodeId = orchestratorNodeId,
                SubsystemName = "Orchestrator",
                LocalTempRoot = isolatedTempRoot,
                LogDirectory = resolvedLogDir,
            }));

        _bus.SwapBuffers();
        _timeTranslators  = _networkFactory?.CreateMasterTimeTranslators(_bus, config.NodeId)
                            ?? new NullMasterTimeTranslators();

        // Drain the read buffer locally so the cache captures the bootstrapped state
        // before the first frame's Phase 2 SwapBuffers wipes it out.
        _core.UiCache.Update();

        // ⭐⭐⭐ CE-1439 — the run surfaces, over THIS host's bus and the core's ONE cluster view (CE-1436: the cache composes it; ⛔ no second fold here).
        //   ⭐ The session is the editor's / CGF's class with no world (this host has none); the choice arms the table's default (Live) and reads the unsaved round off this host's own debug
        //   provider, exactly as CGF does. ⛔ A host that holds the dependency passes it: the panel gets these, so it builds none of its own.
        var core = _core;
        _timeTransport = new Hrot.UI.Common.Adapters.ClusterTimeTransportAdapter(
            _bus, () => core.UiCache.View, () => _masterSync?.GetCurrentState().TotalTime ?? 0.0);
        _scenarioSession = new Hrot.Editor.AiShared.Scenarios.EditorScenarioSession(
            _bus, () => core.UiCache.View, () => OrchestrationConstants.GetSharedScenariosRoot());
        var choice = _timeTransport.AttachRunChoice(
            Hrot.UI.Common.RunMode.RunArmedDefaults.For(Hrot.UI.Common.RunMode.RunHostKind.Orchestrator), _scenarioSession.RunControl,   // ⭐ CE-4101: the RAW verbs go to the choice only
            unsavedReads: Hrot.UI.Common.RunMode.UnsavedCheckReads.From(() => _unsavedReadsProvider ??= CreateDebugProvider()));
        core.ScenarioPanel.RunControls = new Hrot.Orchestrator.Panels.ClusterPanelRunControls(_timeTransport, choice, _scenarioSession);
    }

    public void Update(float deltaTime)
    {
        // Phase 1: Network boundary — DDS ingress/egress (HEXAG2-S008).
        // ScanAndPublish reads from _bus CURRENT and sends to DDS (time-mode + lockstep).
        // PollIngress reads from DDS and writes to _bus WRITE buffer.
        // _translator.Tick() bridges DDS heartbeats, ClusterOpRequests, and NodeOpStatuses.
        _timeTranslators?.ScanAndPublish();
        _timeTranslators?.PollIngress();
        _translator?.Tick();

        // Phase 2: Single frame boundary swap — exactly one SwapBuffers per frame.
        _bus?.SwapBuffers();

        // Phase 3: Core logic — MasterSyncController drains bus intents (HEXAG2-S011),
        // then ClusterMaster ticks to process fan-out ops and 2PC tracking.
        // Then ReplayProcessManager auto-pauses the clock when replay ends.
        // Then StorageProcessManager handles NAS pulls for completed SerializeLocal ops.
        // Then EpisodeProcessManager updates active episode state and publishes EpisodeStateChangedEvent.
        _masterSync?.Update();          // ⭐ Q86: the host advances its own clock — the core never does
        _core?.Tick();
        _clusterSlave?.Tick();

        // Phase 4: Local observation — the UI cache after the master tick, then the seek debounce.
        _core?.TickUi(deltaTime);
        _timeTransport?.Update();     // ⭐ CE-1439 — after the cache drained the frame's events: the choice hears the cluster state, a check-round answer is delivered
        _scenarioSession?.Update();   // ⭐ CE-1439 — a load waiting for the unload goes out once the cluster is Idle
        _scenarioCatalog?.Refresh();  // ⭐ CE-1439 — the picker's catalog follows the master's inventory (it starts empty and fills when the first inventory arrives)

        // Phase 5: Time-sync NTP ingress (NTP responses from slaves).
        _timeTranslators?.PollNtpIngress();
    }

    public void DrawWorld() { }

    public void DrawUI()
    {
        /* panels registered as ManagedWindows via IWindowRegistrar */
        // ⭐ CE-1439 — the shared pickers (Open Scenario / Open Recording) and the New Exercise confirmation are drawn once a frame, as CGF and the editor do
        _pickers?.DrawFrame();
        Hrot.UI.Common.RunMode.ConfirmationModal.Draw(_newExerciseConfirm.Pending);
    }

    /// <summary>
    /// ⭐⭐⭐ <c>CE-1439</c> (AQ108 §2.12; R-3066 audit #9) — <b>the scenario menu of an operator host with no world</b>, registered by the composer when this host owns ▶
    /// (<c>LocalWindowController.ComposeRunControls</c>): the SAME <c>ScenarioMenuCommands.Register</c> the editor and CGF call, over this host's session, so the menu — and the Start surface,
    /// which draws the items marked allowed-in-Idle — is the same on every operator host by construction.
    /// <para>⚠ <b>What a host with no world cannot do</b>, stated and greyed with its reason (ruling 49), never faked: <i>Save Scenario As</i> has no dialog here (nothing local to author, so
    /// nothing local to name); <i>New Scenario</i> is the shared local wipe (<c>ClearWorld</c>) — on this host it only forgets the loaded name, until CE-4089 makes New a recipe the cluster opens.</para>
    /// </summary>
    public void ComposeScenarioActions(Fdp.Presentation.WindowManager.WindowManager windowManager)
    {
        if (_core is null || _scenarioSession is null || _timeTransport is null) return;

        // the ONE picker factory (entity icons + the silk atlas), as every host
        _pickers = Hrot.UI.Common.AddEntity.EntityAuthoring.CreatePickers(new Hrot.Editor.AiShared.Adapters.SilkIconProvider(windowManager.Atlas));

        // Open Scenario lists what the master's inventory offers — the same list the panel's combo and GET /scenarios show — through the shared scenario contributor and picker launcher
        var core = _core;
        var catalog = new Hrot.Editor.AiShared.Catalog.AssetCatalog();
        _scenarioCatalog = new Hrot.Editor.AiShared.Catalog.ScenarioCatalogContributor(
            () => core.UiCache.AvailableScenarios,
            () => OrchestrationConstants.GetSharedScenariosRoot());
        catalog.AddContributor(_scenarioCatalog);
        var scenarioPicker = new Hrot.Editor.AiShared.Browser.AssetPickerLauncher(
            openPicker: OpenPicker,
            catalog:    catalog,
            router:     new Hrot.Editor.AiShared.Browser.AssetPickActionRouter(_ => { }, name => _scenarioSession.OpenForEdit(name)));   // Open() below always passes its own onPicked

        Hrot.Editor.AiShared.Scenarios.ScenarioMenuCommands.Register(
            registerCommand:    windowManager.ShellCommands.Register,
            menu:               windowManager.GlobalMenu,
            commands:           windowManager.ShellCommands,
            session:            _scenarioSession,
            openPicker:         (kinds, callback) => scenarioPicker.Open(kinds, callback),
            openSaveAsDialog:   null,
            // ruling 53 — the confirm belongs where the operator sits, and this window has one: it PROMPTS (the ONE wording, shared with the editor)
            confirmNewExercise: run => _newExerciseConfirm.RequestNewExercise(run),
            // ⭐ CE-4101 — the session's own open / open-recording ARE the guarded door (Stop & Load over a run, Save / Discard / Cancel over unsaved edits): no host seam, no unguarded fallback
            openRecordingPicker: new Hrot.Editor.AiShared.Scenarios.RecordingPickerLauncher(
                openPicker:    OpenPicker,
                listExercises: () => core.UiCache.AvailableExercises,
                openRecording: id => _scenarioSession.OpenRecording(id)).Open);
    }

    /// <inheritdoc/>
    public void RegisterWindows(Fdp.Presentation.WindowManager.WindowManager windowManager)
    {
        if (_core == null) return;
        windowManager.RegisterWindow(new OrchestratorWindow(_core.ScenarioPanel));

        // Register diagnostics window.
        windowManager.RegisterWindow(new DiagnosticsWindow(_core.DiagnosticsPanel));

        // Wire the ImGui file dialog fallback so it renders on non-Windows hosts.
        // Harmless no-op for the Win32 backend: WindowManager only draws the service
        // when it is an ImGuiFileDialogService.
        if (_fileDialogService != null)
            windowManager.SetFileDialogService(_fileDialogService);
    }

    public void Shutdown()
    {
        // Dispose ID allocator server first — joins its polling thread before any DDS teardown.
        _idAllocatorServerHandle?.Dispose();
        _idAllocatorServerHandle = null;
        // Dispose the orchestration translator — tears down DDS readers/writers.
        _translator?.Dispose();
        _translator = null;
        _timeTranslators?.Dispose();
        _timeTranslators = null;
        _core?.Dispose();
        _core = null;
        _bus = null;
        _clusterSlave?.Dispose();
        _clusterSlave = null;
        _fileDialogService = null;
        _timeTransport = null;
        _scenarioSession = null;
        _unsavedReadsProvider = null;
        _pickers = null;
        _scenarioCatalog = null;
        _masterSync?.Dispose();
        _masterSync = null;
        _timeCommands = null;
        _networkFactory = null;
    }

    /// <summary>
    /// Formats a JSON string with indentation for tooltip display.
    /// Returns the original string if parsing fails (CGF1-S0501).
    /// </summary>
    internal static string FormatPrettyJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return string.Empty;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            return System.Text.Json.JsonSerializer.Serialize(doc, FdpJsonOptionsRegistry.Indented);
        }
        catch { return json; }
    }

    /// <summary>
    /// Parses a FixedDelta seconds value from a <c>StepTime</c> payload JSON.
    /// Returns <paramref name="fallback"/> when the payload is absent, malformed,
    /// or contains a non-positive value.
    /// </summary>
    internal static float ParseStepDelta(string payload, float fallback)
    {
        if (string.IsNullOrWhiteSpace(payload)) return fallback;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(payload);
            if (doc.RootElement.TryGetProperty("FixedDelta", out var el))
            {
                float v = el.GetSingle();
                return v > 0f ? v : fallback;
            }
        }
        catch { }
        return fallback;
    }
}

internal sealed class OrchestratorNullEntityStateExtractionService : IEntityStateExtractionService
{
    public IReadOnlyList<EntityStateDumpDto> ExtractEntities(IReadOnlyList<long>? networkIds = null)
        => Array.Empty<EntityStateDumpDto>();
}

internal sealed class OrchestratorNullDiagnosticEventHistoryService : IDiagnosticEventHistoryService
{
    public void Capture(string providerName, FdpEventBus eventBus, uint currentFrame) { }

    public CapturedEventDto[] GetHistory(IReadOnlyList<string>? providerFilter = null)
        => Array.Empty<CapturedEventDto>();

    public void ClearHistory() { }

    public void RewindHistory(uint toFrame) { }
}
