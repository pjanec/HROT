using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using System.Text.Json;
using Fbt;
using Fbt.HotReload;
using Fbt.Runtime;
using Fdp.Core;
using Fdp.Core.Logging;
using Fdp.Examples.Scenarios.Integrated;
using Fdp.Modules.Geographic;
using Fdp.Toolkit.Runner;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Modules;
using Fdp.Toolkit.Behavior.TacticalOrderMapper;
using Fdp.Presentation.Abstractions;
using Fdp.Presentation.Adapters;
using Fdp.Presentation.Panels;
using Fdp.Presentation.Utils;
using Fdp.Presentation.WindowManager;
using Fdp.Toolkit.Lifecycle;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.NetworkSpawning.Systems;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Physics;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Blueprints;
using Fdp.Toolkit.Scenario;
using Fdp.Toolkit.Time.Controllers;
using Fdp.Toolkit.Vis2D;
using Fdp.Toolkit.Vis2D.Components;
using Fdp.Toolkit.Vis2D.Defaults;
using Fdp.Toolkit.Vis2D.Layers;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Perception.Events;
using Fdp.Toolkit.Perception.Translators;
using Fdp.Toolkit.Vis2D.Abstractions;
using Fdp.Toolkit.Diagnostics;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Events;
using Fdp.Toolkit.Diagnostics.Gizmos.Settings;
using Fdp.Toolkit.Diagnostics.Gizmos.Systems;
using Hrot.CGF;
using Hrot.CGF.Orchestration;
using Hrot.CGF.Systems;
using Hrot.Editor.Windows;
using Hrot.Orchestrator.Panels;
using Hrot.Presentation.Windows;
using Hrot.Common.Orchestration.Handlers;
using Hrot.Common.Diagnostics;
using Hrot.Common.Constants;
using Hrot.Common.Interactions;
using Hrot.Common.Systems;
using Hrot.Common.Scenario;
using Hrot.Editor;
using Hrot.Editor.Adapters;
using Hrot.Editor.AiShared.Adapters;
using Hrot.Editor.Events;
using Hrot.Editor.Modules;
using Hrot.Editor.Rendering;
using Hrot.Editor.UI;
using Hrot.IG.Components;
using Hrot.IG.Systems;
using Hrot.IG.Modules;
using Hrot.Map.Common;
using Hrot.Map.Common.Components;
using Hrot.Map.Common.Config;
using Hrot.Map.Common.Services;
using Hrot.Orchestrator;
using Hrot.ScenarioEditor;
using Hrot.ScenarioEditor.Rendering;
using Hrot.ScenarioEditor.Services;
using Hrot.SimHost;
using Hrot.SimHost.Modules;
using Hrot.Presentation.Facades;
using Hrot.UI.Common.Facades;
using Hrot.UI.Common.Panels;
using Hrot.Core.Network;
using Fdp.ModuleHost;
using Fdp.ModuleHost.Abstractions;
using Fdp.Core.Diagnostics;
using Fdp.ModuleHost.Diagnostics;
using Fdp.ModuleHost.Scheduling;
// Disambiguate IMapCameraProvider: Hrot.SimHost.Modules also defines this interface.
using IMapCameraProvider = Fdp.Toolkit.Runner.IMapCameraProvider;
using FdpEntityInspectorPanel = Fdp.Presentation.Panels.EntityInspectorPanel;
using FdpEventBrowserPanel = Fdp.Presentation.Panels.EventBrowserPanel;
using FdpRepositoryAdapter = Fdp.Presentation.Adapters.RepositoryAdapter;
using FdpInspectorState = Fdp.Presentation.Abstractions.InspectorState;
// (Phase 5: EditorInteractionTool alias removed with StandardInteractionTool)
using Fdp.Toolkit.NetworkSpawning;
using Fdp.Interfaces;
using Fdp.Toolkit.Spatial;
using CarKinem.Tkb;
using Fdp.Toolkit.Behavior.Translators;
using Fdp.Toolkit.Combat.Translators;
using Hrot.Editor.Commands;
using Hrot.Common.Events;
using Hrot.Diagnostics.Breakpoints;
using Hrot.Blueprints.Core;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Editor.Debug;
using Hrot.Blueprints.Editor.Reload;
using StructEdit.Reflection;
using Fdp.Toolkit.ReplayBrowser.Search;
// AIE-015: shared AI editor infrastructure
using Hrot.BTree.Editor.Catalog;
using Hrot.BTree.Editor.Host;
using Hrot.Blueprints.Editor.Catalog;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Catalog;
using Hrot.Editor.AiShared.Comparison;
using Hrot.Editor.AiShared.Debug;
using Hrot.Editor.AiShared.Documents;
using Hrot.Editor.AiShared.Refactor;
using Hrot.Editor.AiShared.References;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Browser;
using Hrot.Editor.AiShared.Selection;
using Hrot.Editor.AiShared.Windows;
using Hrot.Hsm.Editor.Catalog;
using Hrot.Hsm.Editor.Host;
// AIE-030/031/032: BTree/HSM debug session infrastructure
using Hrot.BTree.Editor.Inspector;
using Hrot.Hsm.Editor.Inspector;
// AIE-050/051/052: cross-asset services
using Hrot.BTree.Editor.Blackboard;
using Hrot.BTree.Editor.Comparison;
using Hrot.Hsm.Editor.Blackboard;
using Hrot.Hsm.Editor.Comparison;
using Hrot.Blueprints.Editor.Comparison;

namespace Hrot.Editor
{
    /// <summary>
    /// <see cref="ISubsystem"/> implementation that embeds the standalone HROT Editor.
    ///
    /// <para>Lifecycle:
    /// <list type="number">
    ///   <item><see cref="Initialize"/> ? builds the offline ECS composition root
    ///   (entities, kernel, logic packs, adapters, UI panels) without DDS.</item>
    ///   <item><see cref="Update"/> ? steps the time controller and ticks the kernel.</item>
    ///   <item><see cref="DrawWorld"/> ? renders the 2-D map canvas (skipped in headless).</item>
    ///   <item><see cref="DrawUI"/> ? renders ImGui panels not registered as managed windows
    ///   (skipped in headless).</item>
    ///   <item><see cref="RegisterWindows"/> ? registers editor panels with the Window Manager
    ///   so they participate in the shared docking layout.</item>
    ///   <item><see cref="Shutdown"/> ? disposes the kernel and ECS world.</item>
    /// </list>
    /// </para>
    /// </summary>
    public sealed class EditorSubsystem : ISubsystem, IMapCameraProvider, IWindowRegistrar, Hrot.Common.Diagnostics.Gizmos.IGizmoControllable
    {
        private const int EditorNodeId = 0;

        // ?? Subsystem identity ????????????????????????????????????????????????

        /// <inheritdoc/>
        public string Name => "Editor";

        /// <inheritdoc/>
        /// <remarks>Slate blue ? distinct from IG (green), SimHost (red) and ExCon (violet).</remarks>
        public Vector4 TitleBarColor => new(0.15f, 0.22f, 0.48f, 1f);

        // ?? Network factory (no-op stubs for offline editor) ?????????????????

        private readonly INetworkFactory _networkFactory = new OfflineNetworkFactory();

        // ?? Core state ????????????????????????????????????????????????????????

        private EntityRepository?       _world;
        private ModuleHostKernel?       _kernel;
        private MasterSyncController?   _timeController;
        private PhysicsToolkitModule?   _physicsModule;
        private IEditorLogic?           _editorLogic;
        private EditorApplication?      _editorApp;
        private MapCanvas?              _canvas;
        private MapCamera?              _camera;
        private bool                    _headless;
        // GZH-016: gate — false when another subsystem owns the map view.
        private Func<bool>              _isActiveMapOwner = () => true;

        // ── Universal breakpoints (UBP-P10T1) ────────────────────────────────────
        private EntityRepository?       _bpPreTickSnapshot;
        private DebugSnapshotProvider?  _bpSnapshotProvider;
        private DataBreakpointManager?  _bpManager;
        private DataBreakpointSystem?   _bpSystem;
        private Hrot.Blueprints.Core.Debug.BlueprintDebugSession? _blueprintDebugSession;

        // ── CF-8: Debug session persistence ──────────────────────────────────────
        private CancellationTokenSource? _debugSessionSaveCts;

        // ── Adapters (canvas-dependent; null in headless) ─────────────────────

        private EditorSpawnAdapter?             _spawnAdapter;
        private EditorMissionService?           _missionService;
        private EditorOrbatAdapter?             _orbatAdapter;
        private EditorMapConfigAdapter?         _mapConfigAdapter;
        private EditorMapPickAdapter?           _mapPickAdapter;
        private EditorZoneAdapter?              _zoneAdapter;
        private JsonEntityContextMenuHandler? _contextMenuHandler;
        private EditorPreviewController?        _previewController;
        private MapViewConfig?                  _mapViewConfig;

        // ?? UI panels (legacy, always created) ????????????????????????????????

        private EditorToolbarPanel?   _toolbarPanel;
        private EditorOrbatPanel?     _orbatPanel;

        // ?? Shared UI panels (skipped in headless) ????????????????????????????

        private SpawnerPanel?    _spawnerPanel;
        private MissionPanel?    _missionPanel;
        private ConfigPanel?     _configPanel;
        private SharedOrbatPanel? _sharedOrbatPanel;
        private PreviewPanel?    _previewPanel;
        private ZoneEditorPanel? _zoneEditorPanel;

        // ?? FDP framework panels ??????????????????????????????????????????????

        private FdpEntityInspectorPanel _fdpEntityInspector = new();
        private FdpEventBrowserPanel                 _fdpEventBrowser    = null!;
        private DiagnosticEventHistoryService        _fdpEventHistory    = new();
        private FdpRepositoryAdapter?   _fdpRepoAdapter;
        private FdpInspectorState       _fdpInspectorState  = new();
        private uint                    _fdpFrameCount;
        private Hrot.SimHost.Modules.CognitiveSpatialModule? _perceptionMod;

        // ?? Offline orchestrator (single-node scenario listing) ???????????????????

        private FdpEventBus?           _orchestrationBus;
        private ClusterMaster?                _clusterMaster;
        private ReplaySeekProcessManager?     _seekProcessManager;
        private ReplayProcessManager?         _replayProcessManager;
        private AssetInventoryProcessManager?  _assetInventoryProcessManager;
        private AssetPrefetchProcessManager?   _assetPrefetchProcessManager;
        private StorageGatewayModule?          _storageGateway;
        private ClusterUiCache?                _uiCache;
        private ClusterScenarioPanel?          _clusterPanel;
        private ClusterDiagnosticsPanel?       _clusterDiagnosticsPanel;
        private IFileDialogService?            _fileDialogService;
        private DiagnosticsDumpProcessManager? _diagnosticsDumpProcessManager;
        private DiagnosticLogMergeWorker?      _logMergeWorker;

        // ?? Selection state ???????????????????????????????????????????????????????

        private DefaultSelectionState? _selectionState;
        private Hrot.ScenarioEditor.Gizmos.RubberBandState? _rubberBandState;
        private Hrot.ScenarioEditor.Systems.SelectionInteractionSystem? _selectionSystem;
        private Hrot.Editor.AiShared.Selection.EditorSelectionStore _aiEditorSelectionStore = new();
        private Hrot.Editor.AiShared.Selection.CallbackSelectionBridge? _selectionBridge;
        // ?? Behavior registry (promoted for tooltip rendering) ?????????????????

        private BehaviorRegistry? _behaviorRegistry;

        // ?? AI behavior hot-reload coordinator ?????????????????????????????????

        private AiHotReloadCoordinator?    _aiCoordinator;
        private HotReloadMessageLogSource? _hotReloadSource;
        private BlueprintRegistry          _blueprintRegistry = new();
        private Hrot.Blueprints.Editor.NodeDrawers.BlueprintNodeDrawerRegistry? _blueprintNodeDrawers;
        private Hrot.Blueprints.Editor.NodeDrawers.NodeKindRegistry? _blueprintPaletteEntries;
        // AN7: unified behavior-action catalog (channel commands + [SharedAiAction]/AiPrimitive
        // schema entries). Constructed once after the shared ActionSchemaExporter and reused by the
        // palette + BlueprintDocumentFactory so non-channel "Action:{FQN}" nodes project pins live.
        private Hrot.Blueprints.Editor.ActionCatalog.BehaviorActionCatalog? _behaviorActionCatalog;
        // AIE-049: real EditService — context swapped per-document by BlueprintDocumentFactory.
        private Hrot.Blueprints.Editor.NodeDrawers.EditService? _blueprintEditService;

        // ── AIE-015: Shared AI editor catalog + document/perspective infrastructure ──────────
        private AiAssetCatalogBuilder?         _aiCatalogBuilder;
        private AiDocumentManager?             _aiDocumentManager;
        private WindowManagerPerspectiveSwitcher? _perspectiveSwitcher;
        // MTB-P5-T2: Scenario catalog contributor (non-file-backed; refreshed on scenario list change).
        private Hrot.Editor.Catalog.ScenarioCatalogContributor? _scenarioContributor;
        // AIE-026: save → emit → reload scheduler (ticked in Update)
        private Hrot.Editor.AiShared.Emit.RegenerationScheduler? _regenerationScheduler;
        // AIE-026 (Blueprint): Quick Reload trigger — null until Phase 4 wires QuickReloadService.
        // Receives IEditableAsset (a BlueprintFileAsset in Phase 2; a loaded BlueprintAsset in Phase 4).
        private Action<Hrot.Editor.AiShared.IEditableAsset>? _blueprintQuickReloadTrigger;
        // CF-7-rev: QuickReloadService and asset catalog stored for auto-instrumentation callback.
        private QuickReloadService? _blueprintQuickReloadService;
        private Hrot.Blueprints.Editor.BlueprintPeerSource? _blueprintAssetCatalog;
        // BF-UX1 FIX A: gate auto-reload on edit; defaults false so node moves/edits do NOT trigger
        // a Roslyn compile. The user compiles via the toolbar Quick Reload / Full Rebuild buttons.
        // TODO: wire from BlueprintEditorPreferences.AutoReloadOnSave when the prefs instance is
        //       reachable here (the prefs window lives in a different composition scope).
        private bool _blueprintAutoReloadOnEdit = false;
        private EditorSelectionStore           _btreeSelectionStore  = new();
        private EditorSelectionStore           _hsmSelectionStore    = new();
        private EditorSelectionStore           _blueprintSelectionStore = new();
        private PerspectiveWorkspaceRegistrar? _btreeRegistrar;
        private PerspectiveWorkspaceRegistrar? _hsmRegistrar;
        private PerspectiveWorkspaceRegistrar? _blueprintRegistrar;
        private AssetBrowserDockedWindow?       _aiAssetBrowser;
        // AIE-047: My Blueprint window (hosts NodeEdit MyBlueprintPanel).
        private Hrot.Blueprints.Editor.Windows.BlueprintMyBlueprintWindow? _blueprintMyBlueprintWindow;
        // AIE-048: Blueprint Details + Variables windows.
        private Hrot.Blueprints.Editor.Windows.BlueprintDetailsWindow? _blueprintDetailsWindow;
        private Hrot.Blueprints.Editor.Windows.BlueprintVariablesManagedWindow? _blueprintVariablesWindow;
        // BATCH-03D2: Graph Signature window (edits Function graph Inputs/Outputs).
        private Hrot.Blueprints.Editor.Windows.GraphSignatureWindow? _blueprintSignatureWindow;
        // AIE-048: legacy selection store bridging AiShared → BlueprintVariablesWindow.
        private readonly Hrot.Blueprints.Editor.EditorSelectionStore _blueprintLegacySelectionStore = new();
        // AIE-030: shared debug session infrastructure (created in Initialize, wired in RegisterWindows)
        private AiTracerCoordinator?                    _aiTracerCoordinator;
        private Hrot.BTree.Editor.Debug.BTreeDebugSession? _btreeDebugSession;
        private Hrot.Hsm.Editor.Debug.HsmDebugSession?     _hsmDebugSession;
        // ─────────────────────────────────────────────────────────────────────────────────────

        // BATCH-24: Main toolbar perspective group (self-registers in ctor; field pins it against GC).
        private PerspectiveToolbarSection? _perspectiveToolbarSection;

        // MVE-BATCH-03: "Run Blueprint on Selected Entity" toolbar button.
        // Callback is ImGui-free (testable headlessly); DrawUI renders the ImGui button.
        private Action? _blueprintRunButtonCallback;
        private string _blueprintRunStatus = string.Empty;

        // MVE-BATCH-04: "Save" toolbar button + Ctrl+S shortcut.
        // Callback is ImGui-free (testable headlessly); DrawUI renders the ImGui button.
        private Action? _blueprintSaveCallback;
        private string _blueprintSaveStatus = string.Empty;
        // DirtyTracker shared between Save wiring and the blueprint document pipeline.
        // Allocated in RegisterWindows (together with the rest of the blueprint composition).
        private Hrot.Blueprints.Editor.DirtyTracker _blueprintSaveDirtyTracker = new();

        // MVE-BATCH-05: "Compile / Reload Blueprint" toolbar button.
        // Callback is ImGui-free (testable headlessly); DrawUI renders the ImGui button.
        // Compile works from the in-memory asset (no disk-save required); the result is
        // committed into _blueprintRegistry via _aiCoordinator.ApplyQuickReload so the
        // SAME registry instance the kernel ticks immediately sees the new definition.
        private Action? _blueprintCompileCallback;
        private Action? _blueprintFullRebuildCallback;
        private Fdp.Presentation.WindowManager.WindowManager? _wm;
        private string _blueprintCompileStatus = string.Empty;

        // PU-603: "Save All" toolbar button + Ctrl+Shift+S shortcut.
        // Callback is ImGui-free (testable headlessly); DrawUI renders the ImGui button.
        // FlushNow()s the regeneration scheduler then calls SaveAllAiDocumentsCommand.Execute.
        private Action? _saveAllCallback;
        private string _saveAllStatus = string.Empty;

        // BATCH-06: perspective-level shell hotkey dispatcher (Ctrl+S/Ctrl+Shift+S fix, §20).
        private ImGuiInputSource? _shellInputSource;
        private Hrot.Editor.AiShared.Windows.EditorHotkeyDispatcher? _shellHotkeyDispatcher;

        // BATCH-20 (DEC-9): per-kind INewAssetService registry for SaveAsDialog.
        // Initialized before ShellSaveCommands.Register so the requestSaveAs seam
        // can create a fully-seeded dialog.
        private Dictionary<Hrot.Editor.AiShared.AssetKind, Hrot.Editor.AiShared.Recipes.INewAssetService>? _newAssetServices;

        // BATCH-21: Scenario picker modal (for Load command).
        private Hrot.Editor.AiShared.Browser.AssetPickerModal? _scenarioPickerModal;

        // Captured at Initialize() so the coordinator can pass them to the behavior factory.
        private IGeographicTransform? _geoTransform;
        private NetworkEntityMap?     _entityMap;
        // ?? Production visualizer dependencies ???????????????????????????????????

        private readonly MapUserConfig     _userConfig     = new();
        private readonly MapCameraViewport _cameraViewport = new();
        private DebugPrimitiveBuffer? _gizmoBuffer;
        private DataDrivenGizmoSystem? _editorDataDrivenGizmoSystem;
        private GlobalGizmoManager?  _globalGizmoManager;
        private FdpEventBus?         _interactionBus;
        private GizmoExecutionController? _gizmoController;
        // DEBT-002: hub broadcasts DTO state to all connected terminals.
        private readonly Fdp.Toolkit.Diagnostics.Gizmos.Hub.GizmoUiStateHub _gizmoUiHub = new Fdp.Toolkit.Diagnostics.Gizmos.Hub.GizmoUiStateHub();
        // GZH-003: provides Phase-5 perspective switching with ref-counted gate.
        // GZH-014: public to satisfy IGizmoControllable.
        public GizmoExecutionController GizmoController => _gizmoController!;
        // DEBT-002: exposed for future module installation (BATCH-04).
        internal Fdp.Toolkit.Diagnostics.Gizmos.Hub.GizmoUiStateHub GizmoUiHub => _gizmoUiHub;

        // ?? Tool handling ?????????????????????????????????????????????????????????

        // (Phase 5: _interactionTool removed; entity interaction via ECS gizmos)

        // ?? Context menu (ImGui popup trigger) ????????????????????????????????????

        private DebugGizmoLayer? _gizmoLayer;

        // ?? Rename dialog state ???????????????????????????????????????????????????

        private long   _renameTargetNetworkId;
        private bool   _openRenameModalThisFrame;
        private string _renameBuffer = string.Empty;

        // ?? Private helpers ???????????????????????????????????????????????????

        /// <summary>
        /// Lightweight IPreviewController that wraps <see cref="PreviewClusterOpHandler"/>
        /// and tracks preview state internally without requiring <c>IScenarioStateProvider</c>.
        /// </summary>
        private sealed class EditorPreviewController : IPreviewController
        {
            private readonly PreviewClusterOpHandler _handler;
            private readonly MasterSyncController    _timeController;
            private bool _inPreview;

            internal EditorPreviewController(EntityRepository world, MasterSyncController timeController)
            {
                _handler        = new PreviewClusterOpHandler(world);
                _timeController = timeController;
            }

            public bool IsInPreviewMode => _inPreview;

            public void EnterPreviewMode(bool startPaused = false)
            {
                _handler.TriggerLoadingPreview();
                if (!startPaused)
                    _timeController.SwitchToContinuous();
                _inPreview = true;
            }

            public void ExitPreviewMode()
            {
                _handler.TriggerUnloadingPreview();
                _timeController.SwitchToDeterministic(new System.Collections.Generic.HashSet<int>());
                _inPreview = false;
            }
        }

        // ?? Nested helper: offline sequential ID allocator ????????????????????

        private sealed class SequentialIdAllocator : INetworkIdAllocator
        {
            private long _next = 1000;
            public long AllocateId()            => _next++;
            public void Reset(long startId = 0) => _next = startId;
            public void Dispose() { }
        }

        // ?? Internal test accessors ???????????????????????????????????????????

        /// <summary>Internal test hook: direct access to the ECS world.</summary>
        internal EntityRepository World =>
            _world ?? throw new InvalidOperationException("EditorSubsystem is not initialized.");

        /// <summary>Internal test hook: direct access to the kernel.</summary>
        internal ModuleHostKernel Kernel =>
            _kernel ?? throw new InvalidOperationException("EditorSubsystem is not initialized.");

        /// <summary>Internal test hook: direct access to the editor logic facade.</summary>
        internal IEditorLogic EditorLogic =>
            _editorLogic ?? throw new InvalidOperationException("EditorSubsystem is not initialized.");

        /// <summary>Internal test hook: direct access to the time controller.</summary>
        internal MasterSyncController TimeController =>
            _timeController ?? throw new InvalidOperationException("EditorSubsystem is not initialized.");

        /// <summary>Internal test hook: direct access to the preview controller.</summary>
        internal IPreviewController PreviewController =>
            _previewController ?? throw new InvalidOperationException("EditorSubsystem is not initialized.");

        /// <summary>Internal test hook: exposes the data breakpoint manager (UBP-P10T1).</summary>
        internal IDataBreakpointManager? DataBreakpointManager => _bpManager;

        /// <summary>Internal test hook: exposes the debug snapshot provider (UBP-P10T1).</summary>
        internal DebugSnapshotProvider? BpSnapshotProvider => _bpSnapshotProvider;

        /// <summary>Internal test hook: exposes the mutation interceptor wired to the entity inspector (UBP-P10T5).</summary>
        internal Fdp.Toolkit.Diagnostics.Gizmos.IMutationInterceptor? BpMutationInterceptor
            => _fdpEntityInspector.Reflector.MutationInterceptor;

        /// <summary>Internal test hook: exposes the AI hot-reload coordinator (UBP-P10T10).</summary>
        internal AiHotReloadCoordinator? AiCoordinator => _aiCoordinator;

        /// <summary>
        /// Internal accessor: exposes the shared <see cref="Hrot.Blueprints.Editor.NodeDrawers.EditService"/>
        /// so that <see cref="Hrot.Blueprints.Editor.Host.BlueprintDocumentFactory"/> can inject a
        /// per-document <see cref="Hrot.Blueprints.Editor.NodeDrawers.EditServiceContext"/> when a
        /// Blueprint document is opened (AIE-049).
        /// </summary>
        internal Hrot.Blueprints.Editor.NodeDrawers.EditService? BlueprintEditService => _blueprintEditService;

        // AIE-015: BlueprintWindowRegistrar retired — replaced by PerspectiveWorkspaceRegistrar infrastructure.
        // Kept as a null-returning stub so any external reference during migration compiles.
        [System.Obsolete("Retired by AIE-015. Use the perspective registrar infrastructure instead.")]
        internal Fdp.Toolkit.Runner.IWindowRegistrar? BlueprintWindowRegistrar => null;

        /// <summary>Internal test hook: exposes the "Compile / Reload Blueprint" toolbar callback (MVE-BATCH-05).</summary>
        internal Action? BlueprintCompileCallback => _blueprintCompileCallback;

        /// <summary>Internal test hook: exposes the compile status string (MVE-BATCH-05).</summary>
        internal string BlueprintCompileStatus => _blueprintCompileStatus;

        /// <summary>Internal test hook: exposes the "Save All" callback (PU-603).</summary>
        internal Action? SaveAllCallback => _saveAllCallback;

        /// <summary>Internal test hook: exposes the BlueprintRegistry instance (MVE-BATCH-05).</summary>
        internal Fdp.Toolkit.Blueprints.BlueprintRegistry BlueprintRegistry => _blueprintRegistry;

        /// <inheritdoc/>
        public MapCameraView? GetCameraView() => _camera?.GetCameraView();

        /// <inheritdoc/>
        public void ApplyCameraView(MapCameraView view) => _camera?.ApplyCameraView(view);

        // Non-interface helper kept for backward-compat with tests.
        public MapCamera? GetMapCamera() => _camera;

        // ?? ISubsystem lifecycle ??????????????????????????????????????????????

        // ctor for unit tests
        public EditorSubsystem()
        {
        }


        // ctor for ClusterRunner
        public EditorSubsystem( INetworkFactory _ )
        {
            // we do not use the injected network factory in the offline editor,
            // but we accept it in the constructor to satisfy the dependency graph and allow for future online features.
        }

        /// <summary>
        /// Relative path segments to the AI Behaviors .csproj used by <see cref="IEditorLogic.RebuildAndReloadAI"/>.
        /// Set by the composition root (e.g. Program.cs) before <see cref="Initialize"/> is called.
        /// Defaults to the standard workspace layout.
        /// </summary>
        public string[] AiBehaviorsProjectPath { get; set; } =
            new[] { "Subsystems", "Hrot.AI.Behaviors", "Hrot.AI.Behaviors.csproj" };


        /// <inheritdoc/>
        public void Initialize(SubsystemConfig config)
        {
            _headless = config.Headless;
            // GZH-016: store active-map-owner predicate injected by SubsystemOrchestrator.
            _isActiveMapOwner = config.IsActiveMapOwner;

            // ?? 1. ECS world ?????????????????????????????????????????????????
            _world = new EntityRepository();
            _orchestrationBus = new FdpEventBus(); // Control Plane bus (cluster management)
            Fdp.Toolkit.Orchestration.OrchestrationEventRegistry.RegisterAll(_orchestrationBus);
            Hrot.Orchestrator.OrchestratorEventRegistry.RegisterInternalEvents(_orchestrationBus);
            var accumulator = new EventAccumulator();
            _kernel = new ModuleHostKernel(_world, accumulator);
            _physicsModule = new PhysicsToolkitModule();
            _physicsModule.Initialize(_world);

            // ?? 1b. Register all components BEFORE building serializers ???????
            // FdpAutoSerializer compiles property-extraction delegates at Build() time
            // against the current ComponentTypeRegistry, so all types must be registered
            // first ? otherwise the serializer schema is empty and Save/Load is a no-op.
            SimHostComponentRegistry.RegisterAll(_world);
            CgfComponentRegistry.RegisterAll(_world);
            _world.RegisterManagedComponent<Hrot.Map.Common.Components.ZoneMembership>();
            // MapDisplayComponent is used by MapLayerAssignmentSystem to tag entities
            // with the layer bitmask used by the DebugGizmoLayer for visibility culling.
            _world.RegisterComponent<MapDisplayComponent>();
            // IG presentation components required by MapCullingModule / StyleResolutionModule.
            _world.RegisterComponent<Hrot.IG.Components.CullingState>();
            _world.RegisterComponent<Hrot.IG.Components.ResolvedStyle>();
            _world.RegisterManagedComponent<Hrot.IG.Components.IgSymbolOverride>();
            // Visual effect components required by EventEffectModule (EventToEffectSystem).
            _world.RegisterComponent<VisualEffectState>();
            _world.RegisterComponent<TracerTarget>();
            _world.RegisterEvent<ActivateEditorToolEvent>();
            _world.RegisterEvent<CenterOnEntityCommand>();

            // ?? 2. Time controller (MasterSyncController in Deterministic/frozen mode) ??
            var timeConfig = new TimeControllerConfig { Role = TimeRole.Standalone };
            _timeController = (MasterSyncController)TimeControllerFactory.Create(_world.Bus, timeConfig);
            _kernel.SetTimeController(_timeController);
            // Start in Deterministic mode so authoring starts paused (dt == 0 every frame).
            _timeController.SwitchToDeterministic(new System.Collections.Generic.HashSet<int>());

            // ?? 3. Shared services ????????????????????????????????????????????
            var geoTransform     = HrotEnvironment.CreateGeoTransform();
            _geoTransform = geoTransform;
            var entityMap        = new NetworkEntityMap();
            _entityMap = entityMap;
            _world.SetSingletonManaged<NetworkEntityMap>(entityMap);
            var behaviorRegistry = new BehaviorRegistry();
            _behaviorRegistry = behaviorRegistry;
            // Register Urban Combat behaviors so MissionAdapterSystem can resolve Ambush
            // and InfantryCombat behavior trees when loading UrbanCombatNew scenario files.
            // CGF behaviors (MoveToLocation, FollowRoute, ...) are loaded asynchronously
            // from Hrot.AI.Behaviors.dll via TriggerInitialLoad() below.
            UrbanCombatNewScenario.RegisterUrbanCombatBehaviors(behaviorRegistry);

            // Expose the registry to the diagnostic renderers so the entity inspector
            // can project BrainBlackboard memory and visualize the BTree execution state.
            Hrot.Presentation.Renderers.BrainBlackboardRenderer.BehaviorRegistryAccessor = behaviorRegistry;
            Hrot.Presentation.Renderers.Blackboard1024Renderer.BehaviorRegistryAccessor = behaviorRegistry;
            Hrot.Presentation.Renderers.BTreeVisualizerRenderer.BehaviorRegistryAccessor = behaviorRegistry;
            Hrot.Presentation.Renderers.BehaviorStateRenderer.BehaviorRegistryAccessor = behaviorRegistry;
            Hrot.Presentation.Renderers.BTreeTraceWorkingMemoryRenderer.BehaviorRegistryAccessor = behaviorRegistry;
            Hrot.Presentation.Renderers.HsmTraceWorkingMemoryRenderer.BehaviorRegistryAccessor = behaviorRegistry;

            // Expose the blueprint registry to the Entity Inspector renderers so
            // BlueprintBlackboard* components can show per-tier slot summaries.
            Hrot.Presentation.Renderers.BlueprintBlackboard1024Renderer.BlueprintRegistryAccessor  = _blueprintRegistry;
            Hrot.Presentation.Renderers.BlueprintBlackboard4096Renderer.BlueprintRegistryAccessor  = _blueprintRegistry;
            Hrot.Presentation.Renderers.BlueprintBlackboard16384Renderer.BlueprintRegistryAccessor = _blueprintRegistry;

            // ?? Hot reload: watch the deployment directory for Hrot.AI.Behaviors.dll changes ??
            // When the user clicks "Reload BTrees" and MSBuild overwrites the DLL, the watcher
            // detects the change, loads the new assembly into a fresh collectible ALC on a
            // background thread, and enqueues an interpreter swap for the main thread to apply.
            // Watch specifically for Hrot.AI.Behaviors.dll so the watcher does not fire
            // on unrelated DLL writes during compilation.
            string aiAssemblyDir = AppDomain.CurrentDomain.BaseDirectory;
            _aiCoordinator = new AiHotReloadCoordinator(
                aiAssemblyDir, "Hrot.AI.Behaviors.dll",
                _world!, _behaviorRegistry!,
                _blueprintRegistry,
                new AiHotReloadCoordinatorOptions(LoadPdbOnDeveloperMode: true),
                _geoTransform, _entityMap);

            _aiCoordinator.OnReloadCompleted += _ =>
                Console.WriteLine("[HotReload] AI Behaviors hot-swapped.");

            // Load the current DLL immediately so behaviors are ready before the first frame.
            _aiCoordinator.TriggerInitialLoad();

            // ── AIE-030: Shared debug session infrastructure (created before contributor, wired in RegisterWindows) ──
            _aiTracerCoordinator = new AiTracerCoordinator();
            _btreeDebugSession   = new Hrot.BTree.Editor.Debug.BTreeDebugSession(_aiTracerCoordinator);
            _hsmDebugSession     = new Hrot.Hsm.Editor.Debug.HsmDebugSession(_aiTracerCoordinator);
            // ────────────────────────────────────────────────────────────────────────────────────

            // ── AIE-015: Build the shared AI asset catalog ───────────────────────────────────────
            // Contributors are created and registered in one step via AiAssetCatalogBuilder.
            // The blueprints directory mirrors the path used by the retired CreateBlueprintWindowRegistrar.
            // AIE-030: pass _btreeDebugSession so LoadFrom wires NodeDebugMetadata for symbolication.
            var btreeContrib  = new BTreeAssetContributor(_btreeDebugSession);
            var hsmContrib    = new HsmAssetContributor();

            // PU-301/PU-402: JSON file-based contributors for the dual-load strategy (§3 D4).
            // Editor-owned *.btree.json / *.hsm.json live in the SOURCE tree (Trees/ Machines/ under
            // the Hrot.AI.Behaviors project) — committed + regenerated to C# on build. The editor's
            // BaseDirectory is the deploy/bin dir, NOT the source tree, so we resolve the project
            // directory the same robust way RebuildAndReloadAI does: walk up from CWD and BaseDirectory
            // looking for the .csproj (AiBehaviorsProjectPath). A hard-coded "../../../" is fragile and
            // breaks when the editor runs from a different bin depth (BATCH-11 fix).
            static string? ResolveAiBehaviorsDir(string[] csprojSegments)
            {
                var relative = System.IO.Path.Combine(csprojSegments);
                foreach (var start in new[] { Environment.CurrentDirectory, AppDomain.CurrentDomain.BaseDirectory })
                {
                    var dir = start;
                    while (!string.IsNullOrEmpty(dir))
                    {
                        var candidate = System.IO.Path.Combine(dir, relative);
                        if (System.IO.File.Exists(candidate))
                            return System.IO.Path.GetDirectoryName(candidate);
                        dir = System.IO.Path.GetDirectoryName(dir);
                    }
                }
                return null;
            }

            var aiRootDir          = ResolveAiBehaviorsDir(AiBehaviorsProjectPath);
            var bpRootDir          = aiRootDir != null
                ? System.IO.Path.Combine(aiRootDir, AssetRoots.AssetsRelative(AssetKind.Blueprint))
                : System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Blueprints");
            var bpContrib          = new BlueprintAssetContributor(bpRootDir);
            var btreeJsonContrib   = new BTreeJsonAssetContributor(_btreeDebugSession);
            var hsmJsonContrib     = new HsmJsonAssetContributor();
            if (aiRootDir == null)
            {
                Console.WriteLine("[EditorSubsystem] WARNING: Hrot.AI.Behaviors project dir not found " +
                    $"(searched up from CWD + BaseDirectory for {System.IO.Path.Combine(AiBehaviorsProjectPath)}); " +
                    "editor-owned BTree/HSM JSON assets will not load with layout.");
            }
            else
            {
                var btreeJsonRootDir = System.IO.Path.Combine(aiRootDir, AssetRoots.AssetsRelative(AssetKind.BTree));
                var hsmJsonRootDir   = System.IO.Path.Combine(aiRootDir, AssetRoots.AssetsRelative(AssetKind.Hsm));
                if (System.IO.Directory.Exists(btreeJsonRootDir))
                    btreeJsonContrib.Refresh(rootDirectory: btreeJsonRootDir);
                else
                    Console.WriteLine($"[EditorSubsystem] WARNING: BTree JSON root not found: {btreeJsonRootDir}");

                if (System.IO.Directory.Exists(hsmJsonRootDir))
                    hsmJsonContrib.Refresh(rootDirectory: hsmJsonRootDir);
                else
                    Console.WriteLine($"[EditorSubsystem] WARNING: HSM JSON root not found: {hsmJsonRootDir}");
            }

            _aiCatalogBuilder = new AiAssetCatalogBuilder(
                btreeContrib,
                hsmContrib,
                bpContrib,
                asm => btreeContrib.LoadFrom(asm),
                asm => hsmContrib.LoadFrom(asm),
                ()  => bpContrib.Refresh(),
                bTreeJsonContributor: btreeJsonContrib,
                hsmJsonContributor:   hsmJsonContrib);

            // MTB-P5-T2: Add scenario contributor (non-file-backed; projects AvailableScenarios).
            _scenarioContributor = new Hrot.Editor.Catalog.ScenarioCatalogContributor(
                () => _editorLogic?.AvailableScenarios ?? Array.Empty<string>());
            _aiCatalogBuilder.Catalog.AddContributor(_scenarioContributor);

            // Wire hot-reload: refresh the catalog whenever AI behaviors are reloaded.
            // On initial load the OnReloadCompleted fires via DrainPendingCallbacks; each
            // subsequent file-watcher reload triggers the same callback.
            _aiCoordinator.OnReloadCompleted += info =>
            {
                // Obtain the Hrot.AI.Behaviors assembly from the new ALC (or AppDomain for initial load).
                var aiAsm = info.NewAlc?.Assemblies
                    .FirstOrDefault(a => a.GetName().Name == "Hrot.AI.Behaviors")
                    ?? AppDomain.CurrentDomain.GetAssemblies()
                        .FirstOrDefault(a => a.GetName().Name == "Hrot.AI.Behaviors");

                if (aiAsm != null)
                    _aiCatalogBuilder?.RefreshFromAssembly(aiAsm);
            };
            // ─────────────────────────────────────────────────────────────────────────────────────

            // ?? Hot-reload message log source ?????????????????????????????????
            // Wire up after the coordinator is configured so that both the
            // behavior-swap callbacks and the log-source callbacks are registered.
            _hotReloadSource = new HotReloadMessageLogSource();
            _aiCoordinator.OnReloadCompleted += info => _hotReloadSource.OnReloadCompleted(info.DllPath ?? "__ai_behaviors__");
            _aiCoordinator.OnReloadFailed    += _hotReloadSource.OnReloadFailed;

            var clusterSlave     = new ClusterSlave(EditorNodeId, "Editor", _orchestrationBus);
            var zoneService      = new ZoneManagerService();

            // Build the serializer with custom translators AFTER component registration
            // so FdpAutoSerializer compiles extraction delegates for all registered types.
            var scenarioSerializer = Hrot.SimHost.Serializers.HrotScenarioSerializerFactory.Build(behaviorRegistry, _blueprintRegistry);

            // Wire the unified serialization path so the entity inspector Copy JSON
            // buttons produce readable DTO output for BrainBlackboard and Blackboard1024.
            _fdpEntityInspector.Serializer = scenarioSerializer;
            _fdpEntityInspector.ExtractionService = new Fdp.Toolkit.Diagnostics.EntityStateExtractionService(_world, _entityMap, scenarioSerializer);

            // Inject bus and zoneService so file ops trigger WorldResetEvent and persist zone data.
            var fileService = new ScenarioFileService(scenarioSerializer, _world.Bus, zoneService);

            // ?? 3b. TKB + ELM + offline spawning ?????????????????????????????
            var tkbDb       = HrotEnvironment.CreateTkb();
            // Register Urban Combat entity blueprints (TKB types 1001?2003) so the
            // ScenarioSerializer can resolve MilitaryApc, InfantrySoldier, and Insurgent.
            UrbanCombatNewScenario.RegisterUrbanCombatTkbTemplates(tkbDb);
            var translators = new List<ITkbEntityTranslator>
            {
                new SpatialCoreTkbTranslator(),
                new VehicleKinematicsTkbTranslator(),
                new BehaviorTkbTranslator(),
                new CombatTkbTranslator(),
                new PerceptionTkbTranslator()
            }.AsReadOnly();
            var elm               = new EntityLifecycleModule(tkbDb, Array.Empty<int>());
            elm.SetTranslators(translators);
            var idAllocator       = new SequentialIdAllocator();
            var spawnSys          = new NetworkSpawningSystem(tkbDb, elm, entityMap, idAllocator, localNodeId: EditorNodeId, translators: translators);
            var scenarioLoadSource = new ScenarioEntityCreationRequestSource();
            var extractor          = new StagingEntityExtractor();
            string isolatedTempRoot = Fdp.Toolkit.Orchestration.OrchestrationConstants.GetNodeStagingRoot(EditorNodeId);

            // ?? 3c. Offline scenario load handler ?????????????????????????????
            var storageProvider = new LocalDiskStorageProvider(isolatedTempRoot);
            var scenarioLoader  = new HrotScenarioLoader(storageProvider, "Hrot.Scenario");
            clusterSlave.RegisterHandler(new Fdp.Toolkit.Orchestration.Handlers.ReferencePrefetchHandler(storageProvider));

            // FIX: EcsRecordReplayController now handles NetworkEntityMap resync internally for all subsystems.
            // Pass null (no downstream callbacks for offline Editor); the controller will rebuild the map.
            var rrController    = new Hrot.SimHost.Modules.Orchestration.EcsRecordReplayController(
                _kernel, EditorNodeId, _world!);
            clusterSlave.RegisterHandler(new Hrot.ScenarioEditor.Handlers.HrotEditLoadHandler(
                scenarioSerializer, scenarioLoader, zoneService, extractor, scenarioLoadSource, idAllocator, _world));
            clusterSlave.RegisterHandler(new Hrot.SimHost.Orchestration.Handlers.HrotScenarioLoadHandler(
                scenarioSerializer, scenarioLoader, zoneService, extractor, scenarioLoadSource, idAllocator, _world,
                controller: rrController,
                storageDirectory: isolatedTempRoot));
            clusterSlave.RegisterHandler(new DiagnosticsDumpClusterOpHandler(
                _fdpEventHistory,
                new ArchitectureDiagnosticsService(() => _kernel),
                new Fdp.Toolkit.Diagnostics.EntityStateExtractionService(_world, _entityMap, scenarioSerializer),
                new Hrot.Core.Diagnostics.LogArchiveExtractionService(
                    System.IO.Path.Combine(System.AppContext.BaseDirectory, "logs"),
                    "Editor",
                    0),
                new Hrot.Common.Infrastructure.HrotNodeConfig
                {
                    NodeId = EditorNodeId,
                    SubsystemName = "Editor",
                    LocalTempRoot = isolatedTempRoot,
                    LogDirectory = System.IO.Path.Combine(System.AppContext.BaseDirectory, "logs"),
                }));

            // ?? 4. Module registration (offline ? no translator packs) ????????
            var simHostCorePack  = new SimHostCoreLogicPack(entityMap);
            var perceptionMod    = new CognitiveSpatialModule(
                _world,
                colliderRadiusReader: (view, e) => view.HasComponent<Fdp.Toolkit.Physics.Components.PhysicsCollider>(e)
                    ? view.GetComponentRO<Fdp.Toolkit.Physics.Components.PhysicsCollider>(e).Radius
                    : 0f);
            _perceptionMod = perceptionMod;
            var mapperRegistry = new TacticalIntentMapperRegistry();
            mapperRegistry.Register(new Hrot.AI.Behaviors.Mappers.DefendAreaMapper());
            mapperRegistry.Register(new Hrot.AI.Behaviors.Mappers.HullDownAttackMapper());
            var cgfLogicPackInst = new CgfLogicPack(behaviorRegistry, entityMap,
                scenarioLoadSource,
                mapperRegistry);

            var toggleInput = new TogglableInputGroup(
                "EditorInput",
                cgfLogicPackInst.InputSystems.Concat(simHostCorePack.InputSystems).ToArray());

            // ── Blueprint runtime (MVE-BATCH-02) ──────────────────────────────────────
            // Wire the Instance-Blueprint runtime into THIS kernel (the real composition the
            // running editor uses — no sandbox world). The shared helper registers the three
            // blackboard tier components on _world and registers BlueprintMaintenanceSystem
            // (BeforeSync) as a global system; it returns the Simulation-phase tick system,
            // which must be scheduled inside a module's sim list. We tick against the SAME
            // _blueprintRegistry the editor's AiHotReloadCoordinator compiles blueprints into
            // (see field declaration + _aiCoordinator construction above), so editor-registered
            // blueprints run live. Both this composition and the integration-test EditorHarness
            // call WireBlueprintRuntime so the wiring stays a single source of truth.
            var bpTick = Hrot.Blueprints.Editor.Runtime.BlueprintRuntimeWiring.WireBlueprintRuntime(
                _kernel, _world!, _blueprintRegistry);

            var toggleSim = new TogglableSimulationGroup(
                "EditorSim",
                cgfLogicPackInst.SimulationSystems.Concat(simHostCorePack.SimulationSystems).Append(bpTick).ToArray());

            var togglePostSim = new TogglablePostSimulationGroup(
                "EditorPostSim",
                simHostCorePack.PostSimulationSystems.ToArray());
            var orchPack         = new OrchestrationLogicPack(clusterSlave);
            var scenarioMod      = new ScenarioEditorModule(fileService);

            _kernel.RegisterModule(new BehaviorDiagnosticsModule());
            _kernel.RegisterModule(perceptionMod);
            _kernel.RegisterGlobalSystem(new Hrot.SimHost.Systems.AreaQueryResultMaterializationSystem());
            _kernel.RegisterModule(orchPack);
            _kernel.RegisterModule(scenarioMod);

            // Register the event history service and its capture system.
            _fdpEventBrowser = new FdpEventBrowserPanel(_fdpEventHistory);
            _kernel.RegisterGlobalSystem(new EventHistoryCaptureSystem("World", _fdpEventHistory, _world.Bus));
            if (_orchestrationBus != null)
                _kernel.RegisterGlobalSystem(new EventHistoryCaptureSystem("Orchestration", _fdpEventHistory, _orchestrationBus));

            // ?? 4a. Multi-phase system registration for SimHostCorePack and CgfLogicPack ??
            _kernel.RegisterGlobalSystem(toggleInput);
            _kernel.RegisterGlobalSystem(togglePostSim);
            // Simulation-phase systems must go through a module (kernel forbids global registration).
            _kernel.RegisterModule(new EditorSimulationModule(toggleSim));

            // Register replay handler before live handler so the replay branch can claim
            // PrepareLive while replay is active.
            clusterSlave.RegisterHandler(new Fdp.Toolkit.Orchestration.Handlers.ReferenceReplayLoadHandler(
                rrController,
                inputGroup:            toggleInput,
                simGroup:              toggleSim,
                postSimGroup:          togglePostSim,
                lifecycleGroup:        null,
                bypassLifecycleToggle: null,
                storageDirectory:      isolatedTempRoot,
                suspendGlobalTimePush: _kernel.SuspendGlobalTimePush,
                resumeGlobalTimePush:  _kernel.ResumeGlobalTimePush));
            clusterSlave.RegisterHandler(new Fdp.Toolkit.Orchestration.Handlers.ReferenceLiveLoadHandler(
                checkpointWorker: null,
                controller: rrController,
                storageDirectory: isolatedTempRoot));

            // NOTE: SimHostComponentRegistry.RegisterAll was moved to step 1b above.
            _kernel.RegisterModule(new EditorSystemsModule());

            // ?? 4c. ELM + offline spawning module + scenario genesis pipeline ??????????????????
            // CreateEntityRequestSystem drains scenarioLoadSource each Input tick and emits
            // SpawnEntityCommand events for NetworkSpawningSystem (BeforeSync tick), which
            // sets AuthorityMask = ComponentMask for locally owned entities.
            var requestSystem = new CreateEntityRequestSystem(
                requestSource:      scenarioLoadSource,
                ackSink:            new NullEntityAckSink(),
                tkbDb:              tkbDb,
                idAllocator:        idAllocator,
                localNodeId:        EditorNodeId,
                isDefaultProcessor: true);
            _kernel.RegisterModule(elm);
            _kernel.RegisterModule(new SimHostModule(spawnSys));
            _kernel.RegisterGlobalSystem(requestSystem);
            _kernel.RegisterGlobalSystem(new Hrot.SimHost.Systems.GenesisMaterializationSystem(entityMap));
            // BSA-WIRE: register the blueprint genesis + event-ingress systems so that
            // InitialBlueprintsIntent (written by BlueprintStateTranslator on scenario load)
            // is consumed in the offline editor just as it is on a CGF node.
            Hrot.SimHost.Systems.BlueprintGenesisRuntimeRegistration.RegisterBlueprintGenesisSystems(
                _kernel, _blueprintRegistry);

            // ?? 4b. Logic-pack list used by EditorApplication.SwitchToExternalAsync ??
            var logicPacks = new List<IEcsModule> { simHostCorePack, perceptionMod, cgfLogicPackInst };

            // ?? 4d. MapLayerAssignmentSystem ? must be registered BEFORE Initialize() ??
            // Stamps MapDisplayComponent.LayerMask on each entity so the DebugGizmoLayer
            // can cull entities whose layer is toggled off in the editor's config panel.
            _kernel.RegisterGlobalSystem(new MapLayerAssignmentSystem());

            // ?? 4e. IG presentation modules ? compute CullingState and ResolvedStyle ??
            // Must be registered BEFORE Initialize() so their component queries are built.
            _kernel.RegisterModule(new MapCullingModule(_cameraViewport));
            _kernel.RegisterModule(new StyleResolutionModule(_userConfig, localNodeId: EditorNodeId));

            // ?? 4f. Visual effects module ? spawns and cleans up tracers / explosions ??
            _kernel.RegisterModule(new EventEffectModule());

            // ── Universal breakpoints (UBP-P10T1) ────────────────────────────────────
            // Allocate the pre-tick snapshot repo and mirror all component registrations.
            // Placed here (before gizmo systems) so _bpManager can be passed as
            // breakpointManager: to the gizmo system constructors (UBP-P10T4).
            _bpPreTickSnapshot = new EntityRepository();
            SimHostComponentRegistry.RegisterAll(_bpPreTickSnapshot);
            CgfComponentRegistry.RegisterAll(_bpPreTickSnapshot);
            _bpPreTickSnapshot.RegisterManagedComponent<Hrot.Map.Common.Components.ZoneMembership>();
            _bpPreTickSnapshot.RegisterComponent<MapDisplayComponent>();
            _bpPreTickSnapshot.RegisterComponent<Hrot.IG.Components.CullingState>();
            _bpPreTickSnapshot.RegisterComponent<Hrot.IG.Components.ResolvedStyle>();
            _bpPreTickSnapshot.RegisterManagedComponent<Hrot.IG.Components.IgSymbolOverride>();
            _bpPreTickSnapshot.RegisterComponent<VisualEffectState>();
            _bpPreTickSnapshot.RegisterComponent<TracerTarget>();

            var bpTimeAdapter           = new MasterSyncTimeControllerAdapter(_timeController!);
            var bpEditSvc               = new ComponentEditServiceBuilder().Build();
            var bpPredicateCompiler     = new PredicateCompiler(bpEditSvc, _behaviorRegistry);
            var bpEventScannerCompiler  = new EventScannerCompiler(bpEditSvc);
            _bpSnapshotProvider         = new DebugSnapshotProvider(_bpPreTickSnapshot);
            _bpManager                  = new DataBreakpointManager(
                _world!, _bpPreTickSnapshot, _bpSnapshotProvider,
                bpTimeAdapter, bpPredicateCompiler, bpEventScannerCompiler);
            _bpSystem                   = new DataBreakpointSystem(_bpManager, _world!.Bus);

            _kernel.RegisterGlobalSystem(_bpSnapshotProvider);
            _kernel.RegisterGlobalSystem(_bpSystem);

            // ── Blueprint debug session bridge (UBP-P10T6) ───────────────────────────────────
            var bpBlueprintSession = new Hrot.Blueprints.Core.Debug.BlueprintDebugSession(
                _blueprintRegistry, _world!, bpTimeAdapter);
            bpBlueprintSession.SetDataBreakpointManager(_bpManager);
            bpBlueprintSession.SetLiveRepository(_world);  // NGS-2.0: wire live repo for sub-tick recording
            Hrot.Blueprints.Core.Debug.DebugProbe.Sink = bpBlueprintSession;
            bpBlueprintSession.Attach();
            _blueprintDebugSession = bpBlueprintSession;

            // ── CF-8: Debounced save on breakpoint/session changes ────────────────────────
            bpBlueprintSession.OnBreakpointListChanged += _ => ScheduleDebugSessionSave();
            bpBlueprintSession.OnSessionStateChanged    += ScheduleDebugSessionSave;
            // ──────────────────────────────────────────────────────────────────────────────

            // AIE-015: CreateBlueprintWindowRegistrar retired - perspective infra is wired in RegisterWindows.

            // ─────────────────────────────────────────────────────────────────────────────────

            // ── WHEN-M11: Wire Blueprint Editor Bootstrap (Corrective) ──────────────────────
            // Initialize node drawers, palette entries, and visual attachments for When-Node.
            // Dependencies: use existing breakpoint infrastructure components.
            var channelCatalog = Hrot.Blueprints.Core.Compiler.Catalogs.BuiltInChannelCommandCatalog.Instance;
            var engineEventCatalog = Hrot.Blueprints.Core.Compiler.Catalogs.BuiltInEngineEventCatalog.Instance;
            var eqsTemplates = new Hrot.Blueprints.Editor.NodeDrawers.EqsTemplateRegistry();

            // IEditService stub - no-op for now since the interface is marked as stub.
            // AIE-049: real IEditService — context (CommandHistory + markDirty) is injected
            // per-document by BlueprintDocumentFactory when a document is opened.
            _blueprintEditService = new Hrot.Blueprints.Editor.NodeDrawers.EditService();
            var blueprintEditService = _blueprintEditService;

            // Note: These registries are created but not yet wired to UI components.
            // Final wiring happens in the canvas/UI initialization below (section 10+).
            _blueprintNodeDrawers = Hrot.Blueprints.Editor.BlueprintEditorBootstrap.CreateNodeDrawerRegistry(
                channelCatalog, engineEventCatalog, blueprintEditService, bpPredicateCompiler, eqsTemplates);
            // Blueprint palette is built below (after the BehaviorActionCatalog is constructed) with BOTH
            // the channel-command catalog (AN4: per-channel-action entries) AND the unified behavior-action
            // catalog (AN7: non-channel "Action:{FQN}" entries). _blueprintPaletteEntries is only consumed
            // later at doc-open, so the single build below suffices.
            var blueprintAttachmentProviders = Hrot.Blueprints.Editor.BlueprintEditorBootstrap.CreateAttachmentProviders(
                eqsTemplates, peerNameResolver: _ => null);
            var blueprintCanvasRenderers = Hrot.Blueprints.Editor.BlueprintEditorBootstrap.CreateCanvasRenderers();

            // Store registries for later use by blueprint editor windows (opened on-demand).
            // The actual UI panels that consume these will be initialized in headless gate below.
            // ─────────────────────────────────────────────────────────────────────────────────

            // ── UBP-P10T10: forward reload events to breakpoint manager ─────────────────────
            _aiCoordinator.OnReloadBegin     += () => _bpManager?.OnHotReloadBegin();
            _aiCoordinator.OnReloadCompleted += _  => _bpManager?.OnHotReloadCompleted();
            // ─────────────────────────────────────────────────────────────────────────────────

            // ── UBP-P10T11: restore watches from previous session ───────────────────────────
            var watchesFilePath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "watches.json");
            if (_bpManager != null && System.IO.File.Exists(watchesFilePath))
            {
                try { _bpManager.LoadWatches(watchesFilePath); }
                catch (Exception ex)
                {
                    Console.WriteLine($"[UBP] Failed to load watches.json: {ex.Message}");
                }
            }
            // ─────────────────────────────────────────────────────────────────────────────────

            // ?? 4g. Gizmo subsystem ? local stateless gizmo rendering ?????????????????
            // The Editor has no DDS transport; primitives are produced locally and consumed
            // by a DebugGizmoLayer on the canvas.
            _gizmoBuffer = new DebugPrimitiveBuffer();
            var editorGizmoRegistry = new GizmoRegistry();
            var editorStatelessGizmoRegistry = new StatelessGizmoRegistry();
            var editorGizmoSettings = new GizmoSettingsRegistry();
            // Auto-register all [GizmoProjector]-decorated gizmos in Hrot.ScenarioEditor.Gizmos
            // (IgEntityPresentationGizmo, RouteGizmo, MapOverlayGizmo, EffectPresentationGizmo, ...).
            Hrot.ScenarioEditor.Gizmos.GizmoRegistrar.RegisterAll(
                editorGizmoRegistry, editorStatelessGizmoRegistry, editorGizmoSettings);
            // Register gizmos from Hrot.Common.Diagnostics (SelectionHighlightGizmo, HealthBarGizmo, ...).
            Hrot.Common.Diagnostics.Gizmos.GizmoRegistrar.RegisterAll(
                editorGizmoRegistry, editorStatelessGizmoRegistry, editorGizmoSettings);
            // Register gizmos from Hrot.IG.Gizmos (EffectPresentationGizmo, ...).
            Hrot.IG.Gizmos.GizmoRegistrar.RegisterAll(
                editorGizmoRegistry, editorStatelessGizmoRegistry, editorGizmoSettings);
            // Register CanvasContextMenuGizmo so empty-space right-click resolves through the binding pipeline.
            Hrot.Presentation.Gizmos.GizmoRegistrar.RegisterAll(
                editorGizmoRegistry, editorStatelessGizmoRegistry, editorGizmoSettings);
            // behavior gizmos
            Hrot.AI.Behaviors.Gizmos.GizmoRegistrar.RegisterAll(editorGizmoRegistry, editorStatelessGizmoRegistry, editorGizmoSettings);

            // MissionPresentationGizmo requires IGeographicTransform ? register manually.
            editorStatelessGizmoRegistry.Register(
                new Hrot.ScenarioEditor.Gizmos.MissionPresentationGizmo(geoTransform),
                new[] { typeof(SimTransform), typeof(SelectionState) });
            // EntityEditorLabelGizmo requires BehaviorRegistry ? register manually.
            editorStatelessGizmoRegistry.Register(
                new Hrot.ScenarioEditor.Gizmos.EntityEditorLabelGizmo(_behaviorRegistry!),
                new[] { typeof(SimTransform), typeof(Fdp.Toolkit.Replication.Components.NetworkIdentity) });
            // EntityDragGizmoDefinition has an optional callback constructor ? register manually.
            editorGizmoRegistry.Register(new Hrot.ScenarioEditor.Gizmos.EntityDragGizmoDefinition());
            // Editor has no DDS transport so no network ingress/egress translators.
            var interactionBus = new FdpEventBus();
            Hrot.Common.Interactions.InteractionEventRegistry.RegisterAll(interactionBus);
            _interactionBus = interactionBus;
            _editorDataDrivenGizmoSystem = new DataDrivenGizmoSystem(
                editorGizmoRegistry,
                _gizmoBuffer,
                isSelectedPredicate: static (view, entity) =>
                    view.HasComponent<SelectionState>(entity) &&
                    view.GetComponentRO<SelectionState>(entity).IsSelected,
                interactionBus: interactionBus,
                breakpointManager: _bpManager);
            _globalGizmoManager = new GlobalGizmoManager(_gizmoBuffer, interactionBus,
                breakpointManager: _bpManager);
            var actionRegistry = new GlobalActionRegistry();
            long layerControlId = GlobalGizmoManager.NewId();
            var layerControlGizmo = new Hrot.Common.Diagnostics.Gizmos.LayerControlGizmo(layerControlId, interactionBus, new StructEdit.Reflection.ComponentEditServiceBuilder().Build(), _gizmoUiHub);
            _globalGizmoManager.Register(layerControlId, layerControlGizmo);
            actionRegistry.Register(GlobalActionIds.OpenLayerControl, (_, _) =>
            {
                interactionBus.Publish(new Hrot.Common.Diagnostics.Gizmos.OpenLayerEditorEvent());
            });
            actionRegistry.Register(GlobalActionIds.Rotate, (view, target) =>
            {
                if (target == Entity.Null) return;
                if (!view.HasComponent<SimTransform>(target)) return;
                _editorDataDrivenGizmoSystem!.DeactivateGizmo(target);
                var gizmo = new Hrot.SimHost.Gizmos.EntityRotatorGizmo(
                    view, target, onRemove: () => _editorDataDrivenGizmoSystem!.DeactivateGizmo(target));
                _editorDataDrivenGizmoSystem!.ActivateGizmo(target, gizmo);
            });
            actionRegistry.Register(GlobalActionIds.Measure, (_, _) =>
            {
                _world.Bus.Publish(new ActivateEditorToolEvent(EditorTool.Measure));
            });
            actionRegistry.Register(GlobalActionIds.PlaceEntity, (_, _) =>
            {
                _world.Bus.Publish(new ActivateEditorToolEvent(EditorTool.Spawn));
            });
            actionRegistry.Register(GlobalActionIds.EditOverlay, (view, target) =>
            {
                if (target == Entity.Null || !view.HasManagedComponent<EditablePolyline>(target)) return;

                if (_editorDataDrivenGizmoSystem!.HasInjectedGizmo(target))
                {
                    _editorDataDrivenGizmoSystem!.DeactivateGizmo(target);
                }
                else
                {
                    long netId = view.HasComponent<NetworkIdentity>(target)
                        ? view.GetComponentRO<NetworkIdentity>(target).Value
                        : 0L;
                    var gizmo = new Hrot.ScenarioEditor.Gizmos.VertexEditGizmo(
                        _world!, target, netId,
                        onRemove: () => _editorDataDrivenGizmoSystem!.DeactivateGizmo(target));
                    _editorDataDrivenGizmoSystem!.ActivateGizmo(target, gizmo);
                }
            });
            actionRegistry.Register(GlobalActionIds.EditRoute, (view, target) =>
            {
                if (target == Entity.Null || !view.HasManagedComponent<RoutePlan>(target)) return;

                if (_editorDataDrivenGizmoSystem!.HasInjectedGizmo(target))
                {
                    _editorDataDrivenGizmoSystem!.DeactivateGizmo(target);
                }
                else
                {
                    long netId = view.HasComponent<NetworkIdentity>(target)
                        ? view.GetComponentRO<NetworkIdentity>(target).Value
                        : 0L;
                    var gizmo = new Hrot.ScenarioEditor.Gizmos.RouteWaypointGizmo(
                        _world!, target, netId,
                        onRemove: () => _editorDataDrivenGizmoSystem!.DeactivateGizmo(target));
                    _editorDataDrivenGizmoSystem!.ActivateGizmo(target, gizmo);
                }
            });
            actionRegistry.Register(GlobalActionIds.CenterOnEntity, (view, target) =>
            {
                if (target == Entity.Null) return;
                long netId = view.HasComponent<NetworkIdentity>(target)
                    ? view.GetComponentRO<NetworkIdentity>(target).Value
                    : 0L;
                if (netId != 0)
                    _world!.Bus.Publish(new Hrot.Editor.Commands.CenterOnEntityCommand { NetworkId = netId });
            });
            actionRegistry.Register(GlobalActionIds.Delete, (view, target) =>
            {
                if (target == Entity.Null) return;
                long netId = view.HasComponent<NetworkIdentity>(target)
                    ? view.GetComponentRO<NetworkIdentity>(target).Value
                    : 0L;
                if (netId != 0)
                    _world!.Bus.PublishManaged(new DestroyEntityCommand { NetworkId = netId, Reason = "ContextMenu" });
                else
                    _world!.DestroyEntity(target);
            });
            actionRegistry.Register(GlobalActionIds.Select, (_, target) =>
            {
                if (target == Entity.Null) return;

                var q = _world!.Query().With<SelectionState>().WithLifecycle(EntityLifecycle.All).Build();
                foreach (var e in q)
                {
                    if (_world.IsAlive(e))
                        _world.SetComponent(e, new SelectionState { IsSelected = false, IsPrimarySelection = false });
                }

                _world.SetComponent(target, new SelectionState { IsSelected = true, IsPrimarySelection = true });
                if (_selectionState != null) _selectionState.PrimarySelected = target;
                _fdpInspectorState.SelectedEntity = target;
            });
            actionRegistry.Register(GlobalActionIds.ToggleAiTrace, (view, target) =>
            {
                if (target == Entity.Null) return;
                if (view is not EntityRepository repo) return;
                if (!repo.HasComponent<Fdp.Toolkit.Behavior.Components.BehaviorState>(target)) return;

                const Fdp.Toolkit.Behavior.Diagnostics.BehaviorDebugFlags flag = Fdp.Toolkit.Behavior.Diagnostics.BehaviorDebugFlags.EnableTraceBuffer;
                bool current = repo.HasComponent<Fdp.Toolkit.Behavior.Diagnostics.DebugState>(target)
                    && (repo.GetComponentRO<Fdp.Toolkit.Behavior.Diagnostics.DebugState>(target).Behavior & flag) != 0;
                bool next = !current;
                string nextStr = next ? "true" : "false";
                string patchJson = $$"""
                {
                    "{{nameof(Fdp.Toolkit.Behavior.Diagnostics.DebugState.Behavior)}}": {
                        "{{flag}}": {{nextStr}}
                    }
                }
                """;

                repo.Bus.PublishManaged(new Fdp.Toolkit.Behavior.Diagnostics.PatchDebugStateCommand
                {
                    Target = target,
                    PatchJson = patchJson,
                });
            });
            actionRegistry.Register(GlobalActionIds.ToggleAiTraceLog, (view, target) =>
            {
                if (target == Entity.Null) return;
                if (view is not EntityRepository repo) return;
                if (!repo.HasComponent<Fdp.Toolkit.Behavior.Components.BehaviorState>(target)) return;

                const Fdp.Toolkit.Behavior.Diagnostics.BehaviorDebugFlags flag = Fdp.Toolkit.Behavior.Diagnostics.BehaviorDebugFlags.EmitToLog;
                bool current = repo.HasComponent<Fdp.Toolkit.Behavior.Diagnostics.DebugState>(target)
                    && (repo.GetComponentRO<Fdp.Toolkit.Behavior.Diagnostics.DebugState>(target).Behavior & flag) != 0;
                bool next = !current;
                string nextStr = next ? "true" : "false";
                string patchJson = $$"""
                {
                    "{{nameof(Fdp.Toolkit.Behavior.Diagnostics.DebugState.Behavior)}}": {
                        "{{flag}}": {{nextStr}}
                    }
                }
                """;

                repo.Bus.PublishManaged(new Fdp.Toolkit.Behavior.Diagnostics.PatchDebugStateCommand
                {
                    Target = target,
                    PatchJson = patchJson,
                });
            });

            var contextIngress = new ContextActionIngressSystem(entityMap, interactionBus);
            _rubberBandState = new Hrot.ScenarioEditor.Gizmos.RubberBandState();
            editorStatelessGizmoRegistry.RegisterGlobal(new Hrot.ScenarioEditor.Gizmos.RubberBandGizmo(_rubberBandState));
            _selectionSystem = new Hrot.ScenarioEditor.Systems.SelectionInteractionSystem(_world, interactionBus, _rubberBandState);
            _selectionSystem.OnSelectionChanged += (entity, _) =>
            {
                if (entity == Entity.Null)
                {
                    if (_selectionState != null) _selectionState.PrimarySelected = null;
                    _fdpInspectorState.SelectedEntity = null;
                }
                else if (_world.IsAlive(entity))
                {
                    if (_selectionState != null) _selectionState.PrimarySelected = entity;
                    _fdpInspectorState.SelectedEntity = entity;
                }
            };
            // Wire the AI editor selection store so AI editor windows track the selected entity.
            _selectionBridge = new Hrot.Editor.AiShared.Selection.CallbackSelectionBridge(onEntitySelected =>
            {
                Action<Entity, System.Numerics.Vector3> handler = (entity, _) =>
                {
                    onEntitySelected(_world != null && entity != Entity.Null && _world.IsAlive(entity)
                        ? entity
                        : (Entity?)null);
                };
                _selectionSystem!.OnSelectionChanged += handler;
                return new DelegateDisposable(() =>
                {
                    if (_selectionSystem != null)
                        _selectionSystem.OnSelectionChanged -= handler;
                });
            });
            _selectionBridge.Connect(_aiEditorSelectionStore);
            var gizmoGroup = new TogglablePostSimulationGroup("GizmoExecution",
                _editorDataDrivenGizmoSystem,
                _globalGizmoManager,
                new StatelessGizmoSystem(editorStatelessGizmoRegistry, _gizmoBuffer));
            // GZH-003: Editor is interactive, always has a window at startup.
            gizmoGroup.Enabled = true;
            _gizmoController = new GizmoExecutionController(gizmoGroup, _globalGizmoManager, _editorDataDrivenGizmoSystem);
            _kernel.RegisterModule(new GizmoInteractionModule(
                interactionBus,
                contextIngress: contextIngress,
                interactionSystems: new IEcsModuleSystem[]
                {
                    new GlobalActionDispatchSystem(actionRegistry, interactionBus),
                    gizmoGroup,
                },
                gizmoIngress: null,
                gizmoEgress:  null));
            _kernel.RegisterGlobalSystem(new EventHistoryCaptureSystem("Interaction", _fdpEventHistory, interactionBus));
            // Register canvas menu update so CanvasContextMenuGizmo has state to project.
            _kernel.RegisterGlobalSystem(new Hrot.Presentation.Systems.CanvasMenuUpdateSystem());

            // ── 5. Kernel initialization ─────────────────────────────────────────────
            _kernel.Initialize();

            // ?? 6. Editor application (IEditorLogic facade) ??????????????????
            var app = new EditorApplication(
                fileService, _world.Bus, _orchestrationBus!, _world, _kernel, logicPacks,
                hotReloadSource: _hotReloadSource,
                aiProjectPathSegments: AiBehaviorsProjectPath);
            _editorLogic = app;
            _editorApp   = app;

            // ?? 6b. Offline orchestrator ? scenario listing via ClusterMaster + UICache ??
            var offlineConfig = new ClusterConfiguration { Mandatory = Array.Empty<string>() };
            _clusterMaster  = new ClusterMaster(_orchestrationBus!, offlineConfig);

            // Register the seek aggregator and process manager so the clock snaps on seek
            _seekProcessManager = new ReplaySeekProcessManager(_orchestrationBus!, _timeController);
            _clusterMaster.RegisterAggregator(new ReplaySeekAggregator());

            // Register replay manager and aggregator so duration payload flows through 2PC
            _replayProcessManager = new ReplayProcessManager(_orchestrationBus!, _timeController);
            _clusterMaster.RegisterAggregator(_replayProcessManager.CreateAggregator());

            _storageGateway = new StorageGatewayModule();
            _assetInventoryProcessManager = new AssetInventoryProcessManager(
                _orchestrationBus!,
                _storageGateway,
                ClusterConfiguration.Default.NasBasePath,
                OrchestrationConstants.DefaultStagingDirectory,
                EditorNodeId);
            _assetPrefetchProcessManager = new AssetPrefetchProcessManager(
                _orchestrationBus!,
                _storageGateway,
                ClusterConfiguration.Default.NasBasePath,
                OrchestrationConstants.DefaultStagingDirectory);
            _uiCache = new ClusterUiCache(_orchestrationBus!, _timeController);
            _clusterPanel = new ClusterScenarioPanel(_orchestrationBus!, _uiCache);
            _fileDialogService = new WinFormsFileDialogService();
            _clusterDiagnosticsPanel = new ClusterDiagnosticsPanel(
                _uiCache,
                _orchestrationBus!,
                _fileDialogService,
                EditorBootstrap.ScenariosRoot);
            var diagnosticsAggregator = new DiagnosticsConsensusAggregator();
            _clusterMaster.RegisterAggregator(diagnosticsAggregator);
            _diagnosticsDumpProcessManager = new DiagnosticsDumpProcessManager(
                _orchestrationBus!,
                _storageGateway,
                EditorBootstrap.ScenariosRoot,
                diagnosticsAggregator);
            _logMergeWorker = new DiagnosticLogMergeWorker(_orchestrationBus!);
            app.SetAvailableScenariosSource(() => ScenarioEnumeration.EnumerateRelPaths(EditorBootstrap.ScenariosRoot));

            // ?? 7. Map canvas + camera (skipped in headless) ??????????????????
            if (!_headless)
            {
                _camera = new MapCamera();
                _canvas = new MapCanvas(new RaylibInputProvider());
                _canvas.Camera = _camera;
            }

            // ?? 8. Preview controller (works headless too ? no canvas dep) ????
            _previewController = new EditorPreviewController(_world, _timeController!);

            // ?? 9. Mission service (no canvas dependency) ?????????????????????
            _missionService = new EditorMissionService(_world.Bus, _world, behaviorRegistry);

            // ?? 10. Canvas-dependent adapters, layers, and interaction tool ???
            if (!_headless)
            {
                _mapViewConfig    = new MapViewConfig();
                _mapPickAdapter   = new EditorMapPickAdapter(_canvas!, geoTransform, _world, _globalGizmoManager!);

                // Build the JSON?ECS attribute compiler with the geo-transform so that
                // geodetic spawn coordinates are projected correctly on entity placement.
                var jsonCompiler  = Hrot.SimHost.AttributeCompilerFactory.Build(geoTransform);
                _spawnAdapter     = new EditorSpawnAdapter(_world.Bus, jsonCompiler, tkbDb, scenarioLoadSource, _globalGizmoManager!);
                _zoneAdapter      = new EditorZoneAdapter(_canvas!, _world.Bus, _globalGizmoManager!);
                _mapConfigAdapter = new EditorMapConfigAdapter(_mapViewConfig, _canvas!);
                _selectionState   = new DefaultSelectionState();
                _orbatAdapter     = new EditorOrbatAdapter(_world, _world.Bus, _editorLogic, _spawnAdapter);
                _contextMenuHandler = new JsonEntityContextMenuHandler(_world, interactionBus);
                _fdpRepoAdapter = new FdpRepositoryAdapter(_world);

                // Register context menu handlers with the FDP entity inspector.
                // 1) JSON-driven domain actions (populated by ExCon via ContextMenuState.MenuJson).
                _fdpEntityInspector.RegisterContextMenuHandler(_contextMenuHandler);
                // 2) Local editor authoring actions (centre, rename, edit, delete).
                _fdpEntityInspector.RegisterContextMenuHandler(new LambdaEntityContextMenuHandler((entity, builder) =>
                {
                    if (!_world.IsAlive(entity)) return;
                    if (entity == FdpRepositoryAdapter.SingletonEntity) return;

                    long networkId = _world.HasComponent<NetworkIdentity>(entity)
                        ? _world.GetComponentRO<NetworkIdentity>(entity).Value
                        : 0L;

                    bool hasPolyline = _world.HasManagedComponent<EditablePolyline>(entity);
                    bool hasRoute    = _world.HasManagedComponent<RoutePlan>(entity);

                    builder.AddItem("Center on Entity", () => _editorLogic?.CenterOnEntity(networkId));
                    if (networkId != 0)
                        builder.AddItem("Rename...", () => _editorLogic?.OpenRenameDialog(networkId));
                    if (hasPolyline)
                        builder.AddItem("Edit Shape", () => { _editorLogic?.SelectEntity(networkId); _editorLogic?.ActivateTool(EditorTool.Edit); });
                    if (hasRoute)
                        builder.AddItem("Edit Route", () => { _editorLogic?.SelectEntity(networkId); _editorLogic?.ActivateTool(EditorTool.Route); });
                    builder.AddItem("Rotate", () => { _editorLogic?.SelectEntity(networkId); _editorLogic?.ActivateTool(EditorTool.Rotate); });
                    builder.AddSeparator();
                    builder.AddItem("Delete", () => _world?.Bus.PublishManaged(
                        new DestroyEntityCommand { NetworkId = networkId, Reason = "EditorContextMenu" }));
                }));
                // 3) Perception seeding actions (mark target memory entries for selected perceivers).
                _fdpEntityInspector.RegisterContextMenuHandler(new LambdaEntityContextMenuHandler((entity, builder) =>
                {
                    if (!_world.HasComponent<TargetMemory>(entity)) return;

                    int perceiverCount = _selectionState?.SelectedEntities.Count ?? 0;

                    builder.AddSeparator();

                    builder.AddItem(
                        $"Mark Target for {perceiverCount} Units...",
                        async void () =>
                        {
                            int targetNetId = await _mapPickAdapter!.PickEntityAsync();
                            Entity target   = FindEntityByNetworkId(targetNetId);
                            if (!_world.IsAlive(target)) return;

                            foreach (var perceiver in _selectionState?.SelectedEntities ?? System.Array.Empty<Entity>())
                                _world.Bus.Publish(new SeedTargetCommand
                                {
                                    Perceiver  = perceiver,
                                    Target     = target,
                                    ScoreBoost = 1.0f,
                                });
                        });

                    builder.AddItem(
                        $"Mark Area Targets for {perceiverCount} Units...",
                        async void () =>
                        {
                            IReadOnlyList<int> targetNetIds = await _mapPickAdapter!.PickAreaEntitiesAsync();
                            foreach (var perceiver in _selectionState?.SelectedEntities ?? System.Array.Empty<Entity>())
                                foreach (int netId in targetNetIds)
                                {
                                    Entity target = FindEntityByNetworkId(netId);
                                    if (!_world.IsAlive(target)) continue;
                                    _world.Bus.Publish(new SeedTargetCommand
                                    {
                                        Perceiver  = perceiver,
                                        Target     = target,
                                        ScoreBoost = 1.0f,
                                    });
                                }
                        });
                }));

                // 4) AI tracing toggles (behav-diag-1). Only shown on entities with a brain.
                _fdpEntityInspector.RegisterContextMenuHandler(new LambdaEntityContextMenuHandler((entity, builder) =>
                {
                    if (!_world.IsAlive(entity)) return;
                    if (!_world.HasComponent<Fdp.Toolkit.Behavior.Components.BehaviorState>(entity)) return;

                    builder.AddSeparator();
                    builder.AddItem("Toggle AI Trace Buffer", () =>
                        interactionBus.Publish(new Hrot.Common.Events.GlobalActionRequestedEvent
                        {
                            ActionId = Hrot.Common.Constants.GlobalActionIds.ToggleAiTrace,
                            Target   = entity,
                        }));
                    builder.AddItem("Toggle AI Trace Log", () =>
                        interactionBus.Publish(new Hrot.Common.Events.GlobalActionRequestedEvent
                        {
                            ActionId = Hrot.Common.Constants.GlobalActionIds.ToggleAiTraceLog,
                            Target   = entity,
                        }));
                }));

                // Entity query ? all networked simulation entities with a location.
                var entityQuery = _world.Query()
                    .With<NetworkIdentity>()
                    .With<SimTransform>()
                    .WithLifecycle(EntityLifecycle.All)
                    .Build();

                // Gizmo layer ? renders entity presentation primitives produced locally by StatelessGizmoSystem.
                var schemaRegistry = new GizmoMap.Presentation.GizmoSchemaRegistry();
                var layerControlEditService = new StructEdit.Reflection.ComponentEditServiceBuilder().Build();
                using var layerControlSchemaSession = layerControlEditService.Open(
                    new Hrot.Common.Diagnostics.Gizmos.LayerControlDto
                    {
                        Entities = true,
                        Perception = true,
                        AiHelpers = true
                    },
                    typeof(Hrot.Common.Diagnostics.Gizmos.LayerControlDto));
                schemaRegistry.Register(
                    Hrot.Common.Diagnostics.Gizmos.LayerControlGizmo.SchemaHash,
                    layerControlSchemaSession.Document);
                _gizmoLayer = new DebugGizmoLayer(
                    31,
                    _gizmoBuffer!,
                    interactionBus,
                    _world,
                    _canvas!.Camera,
                    new GizmoMap.Presentation.Shapes.DefaultEntityShapeLibrary(),
                    schemaRegistry);
                _canvas!.AddLayer(_gizmoLayer);
                if (_canvas != null) _canvas.DrawBuffer = _gizmoBuffer;

                // Grid map layer ? reads MapViewConfig.ShowGrid each frame.
                var gridLayer = new GridMapLayer(() => _mapViewConfig!.ShowGrid);
                _canvas!.AddLayer(gridLayer);

                // (Phase 5: StandardInteractionTool removed; entity interaction via ECS gizmos)
            }

            // ?? 11. UI panels ?????????????????????????????????????????????????
            _toolbarPanel = new EditorToolbarPanel();
            _orbatPanel   = new EditorOrbatPanel();

            if (!_headless)
            {
                var tkbCatalog = new TkbCatalogEntry[]
                {
                    new(TkbEntityTypes.Tank_M1Abrams,      "M1 Abrams"),
                    new(TkbEntityTypes.IFV_Bradley,        "M2 Bradley IFV"),
                    new(TkbEntityTypes.Truck_HMMWV,        "HMMWV"),
                    new(TkbEntityTypes.Tank_T72,           "T-72"),
                    new(TkbEntityTypes.Infantry_Rifleman,  "Infantry Rifleman"),
                    new(TkbEntityTypes.Infantry_Officer,   "Infantry Officer"),
                    new(TkbEntityTypes.CivilianPedestrian, "Civilian Pedestrian"),
                    new(TkbEntityTypes.CivilianCar,        "Civilian Car"),
                    new(TkbEntityTypes.MilitaryApc,        "Military APC"),
                    new(TkbEntityTypes.InfantrySoldier,    "Infantry Soldier"),
                    new(TkbEntityTypes.Insurgent,          "Insurgent"),
                    new(TkbEntityTypes.Unit_TankPlatoon,   "Tank Platoon"),
                    new(TkbEntityTypes.Unit_InfantrySquad, "Infantry Squad"),
                    new(TkbEntityTypes.Unit_TankPlatoon_Auto, "Tank Platoon (Auto-Spawn)"),
                };

                _spawnerPanel     = new SpawnerPanel(tkbCatalog);
                _missionPanel     = new MissionPanel(0, Hrot.Presentation.Behavior.BehaviorUiSetup.CreateRegistry());
                _configPanel      = new ConfigPanel();
                _sharedOrbatPanel = new SharedOrbatPanel();
                _previewPanel     = new PreviewPanel();
                _zoneEditorPanel  = new ZoneEditorPanel();
            }
        }

        /// <inheritdoc/>
        public void Update(float deltaTime)
        {
            // Process input pipeline BEFORE kernel update so authored tools
            // (CreationTool, ObstaclePlacementTool, etc.) receive mouse events this frame.
            _canvas?.Update(deltaTime);
            _selectionSystem?.Tick(deltaTime);
            // Update camera viewport so MapCullingModule knows what area is on-screen.
            if (_camera != null)
            {
                var topLeft     = _camera.ScreenToWorld(System.Numerics.Vector2.Zero);
                var bottomRight = _camera.ScreenToWorld(
                    new System.Numerics.Vector2(Raylib_cs.Raylib.GetScreenWidth(), Raylib_cs.Raylib.GetScreenHeight()));

                _cameraViewport.WorldMinX = System.MathF.Min(topLeft.X, bottomRight.X);
                _cameraViewport.WorldMaxX = System.MathF.Max(topLeft.X, bottomRight.X);
                _cameraViewport.WorldMinY = System.MathF.Min(topLeft.Y, bottomRight.Y);
                _cameraViewport.WorldMaxY = System.MathF.Max(topLeft.Y, bottomRight.Y);
                _cameraViewport.Zoom      = _camera.Zoom;
            }

            // Clear the primitive buffer before backend ECS systems populate it.
            // This must happen before kernel.Update() and after canvas.Update() so that
            // tool-emitted primitives (written during canvas.Update ? ActiveTool.Draw) are
            // already in the buffer when StatelessGizmoSystem runs.
            _gizmoBuffer?.EndFrame(deltaTime);

            // Kernel.Update() internally calls bus.SwapBuffers() then ticks registered modules.
            _kernel?.Update();

            // Drain AI hot-reload callbacks safely on the main thread.
            // Any BTreeInterpreter pointer swaps queued by the background ALC worker
            // are applied here, between kernel ticks, so no active simulation tick
            // can observe a half-swapped pointer.
            _aiCoordinator?.DrainPendingCallbacks();

            // AIE-026: tick the regeneration scheduler — flushes debounced dirty saves
            // (BTree/HSM emit-on-dirty).  Safe in headless mode; no-op when nothing is pending.
            _regenerationScheduler?.Tick();

            // Swap the Control Plane bus so intents published by the UI this frame
            // are readable by ClusterMaster/ClusterUiCache on the orchestration bus.
            _orchestrationBus?.SwapBuffers();
            _clusterMaster?.Tick();
            _seekProcessManager?.Tick(); // Pump the seek Saga
            _replayProcessManager?.Tick(); // Pump the replay manager for duration extraction
            _assetInventoryProcessManager?.Tick();
            _assetPrefetchProcessManager?.Tick();
            _diagnosticsDumpProcessManager?.Tick();
            _logMergeWorker?.Tick();
            _uiCache?.Update();
            _editorLogic?.Update();
            _clusterPanel?.Update(deltaTime);

            // Drain ActivateEditorToolEvent ? published by toolbar / context menu.
            if (!_headless)
                DrainToolActivationEvents();

            // Poll mission ACKs so async CommitMissionAsync tasks can resolve.
            _missionService?.PollAcks();

            // Synchronise map selection to the MissionPanel using Network ID.
            if (_missionPanel != null && _selectionState != null && _world != null)
            {
                var selected = _selectionState.PrimarySelected;
                int selectedNetId = 0;

                if (selected.HasValue && selected.Value != Entity.Null && _world.IsAlive(selected.Value))
                {
                    if (_world.HasComponent<Fdp.Toolkit.Replication.Components.NetworkIdentity>(selected.Value))
                    {
                        selectedNetId = (int)_world.GetComponentRO<Fdp.Toolkit.Replication.Components.NetworkIdentity>(selected.Value).Value;
                    }
                }

                _missionPanel.SelectedEntityId = selectedNetId;
            }

            // Feed the FDP event browser each frame.
            if (!_headless && _world != null)
            {
                _fdpFrameCount++;
            }
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Renders the 2-D map canvas.
        /// Called inside <c>Raylib.BeginDrawing()</c> by the orchestrator.
        /// No-op in headless mode.
        /// </remarks>
        public void DrawWorld()
        {
            if (_headless) return;
            _canvas?.Draw();
        }

        /// <inheritdoc/>
        /// <remarks>
        /// After <see cref="RegisterWindows"/>, the main editor panels are rendered by
        /// the Window Manager.  This method renders the map right-click context menu popup
        /// and the entity rename modal.
        /// </remarks>
        public void DrawUI()
        {
            if (_headless) return;

            // ── BATCH-06: perspective-level shell hotkey dispatch (Ctrl+S fix, §20) ───────────
            // Pump the shell command hotkeys once per frame so Ctrl+S/Ctrl+Shift+S fire
            // regardless of which sub-window is focused. Skip while the user is typing in
            // a text field so keystrokes are not stolen from text inputs.
            if (_shellHotkeyDispatcher != null && _wm?.ShellCommands != null)
            {
                var io = ImGuiNET.ImGui.GetIO();
                if (!io.WantTextInput)
                    _shellHotkeyDispatcher.ProcessThisFrame(_wm.ShellCommands);
            }
            // ───────────────────────────────────────────────────────────────────────────────────

            // ── Unified Blueprint Tools Panel ──────────────────────────────────────────────────
            // Merges MVE-BATCH-03/04/05 + PU-603 into a single "Blueprint Tools" window.
            // Gate on ImGui context availability (skipped in non-ImGui headless test paths).
            if (ImGuiNET.ImGui.GetCurrentContext() != System.IntPtr.Zero)
            {
                bool showBlueprintTools = _blueprintRunButtonCallback != null
                    || _blueprintSaveCallback != null
                    || _blueprintCompileCallback != null
                    || _saveAllCallback != null
                    || _blueprintDebugSession != null;

                if (showBlueprintTools && ImGuiNET.ImGui.Begin("Blueprint Tools"))
                {
                    // -- 1. Run Blueprint --
                    if (_blueprintRunButtonCallback != null)
                    {
                        if (ImGuiNET.ImGui.Button(
                                Hrot.Blueprints.Editor.Runtime.RunBlueprintOnEntityCommand.ToolbarLabel))
                            _blueprintRunButtonCallback.Invoke();

                        if (!string.IsNullOrEmpty(_blueprintRunStatus))
                        {
                            ImGuiNET.ImGui.SameLine();
                            ImGuiNET.ImGui.TextUnformatted(_blueprintRunStatus);
                        }
                    }

                    // -- 2. Save Blueprint (hotkey via shell.save command, §20) --
                    if (_blueprintSaveCallback != null)
                    {
                        ImGuiNET.ImGui.SameLine();
                        if (ImGuiNET.ImGui.Button("Save Blueprint"))
                            _blueprintSaveCallback.Invoke();

                        if (!string.IsNullOrEmpty(_blueprintSaveStatus))
                        {
                            ImGuiNET.ImGui.SameLine();
                            ImGuiNET.ImGui.TextUnformatted(_blueprintSaveStatus);
                        }
                    }

                    // -- 3. Compile / Reload Blueprint --
                    if (_blueprintCompileCallback != null)
                    {
                        ImGuiNET.ImGui.SameLine();
                        if (ImGuiNET.ImGui.Button("Compile / Reload"))
                            _blueprintCompileCallback.Invoke();

                        if (_blueprintFullRebuildCallback != null)
                        {
                            ImGuiNET.ImGui.SameLine();
                            if (ImGuiNET.ImGui.Button("Full Rebuild"))
                                _blueprintFullRebuildCallback.Invoke();
                        }

                        if (!string.IsNullOrEmpty(_blueprintCompileStatus))
                        {
                            ImGuiNET.ImGui.SameLine();
                            ImGuiNET.ImGui.TextUnformatted(_blueprintCompileStatus);
                        }
                    }

                    // -- 4. Save All (hotkey via shell.saveAll command, §20) --
                    if (_saveAllCallback != null)
                    {
                        ImGuiNET.ImGui.SameLine();
                        if (ImGuiNET.ImGui.Button("Save All"))
                            _saveAllCallback.Invoke();

                        if (!string.IsNullOrEmpty(_saveAllStatus))
                        {
                            ImGuiNET.ImGui.SameLine();
                            ImGuiNET.ImGui.TextUnformatted(_saveAllStatus);
                        }
                    }

                    // -- 5. Debug step controls (when session is available) --
                    if (_blueprintDebugSession != null)
                    {
                        ImGuiNET.ImGui.Separator();
                        Hrot.Blueprints.Editor.Debug.DebugStepControls.Draw(_blueprintDebugSession);
                    }
                }

                if (showBlueprintTools)
                    ImGuiNET.ImGui.End();
            }
            // ─────────────────────────────────────────────────────────────────────────────────────

            // Render hover tooltip with entity info (label, health, cognitive state).
            if (_isActiveMapOwner() && !ImGuiNET.ImGui.GetIO().WantCaptureMouse && _canvas != null && _world != null)
            {
                var mouseWorld = _canvas.Camera.ScreenToWorld(Raylib_cs.Raylib.GetMousePosition());
                var hovered = _canvas.PickTopmostEntity(mouseWorld);

                if (hovered.HasValue && hovered.Value != Entity.Null)
                {
                    var sb = new System.Text.StringBuilder();

                    if (_world.HasComponent<Hrot.IG.Components.ResolvedStyle>(hovered.Value))
                    {
                        ref readonly var style = ref _world.GetComponentRO<Hrot.IG.Components.ResolvedStyle>(hovered.Value);
                        string label = style.GetLabelText();
                        if (!string.IsNullOrEmpty(label)) sb.AppendLine(label);
                    }

                    if (_world.HasComponent<Fdp.Toolkit.Combat.Components.Health>(hovered.Value))
                    {
                        ref readonly var hp = ref _world.GetComponentRO<Fdp.Toolkit.Combat.Components.Health>(hovered.Value);
                        sb.AppendLine($"Health: {hp.Current:F0} / {hp.Max:F0}");
                    }

                    if (_world.HasComponent<Fdp.Toolkit.Behavior.Components.BehaviorState>(hovered.Value))
                    {
                        ref readonly var ds = ref _world.GetComponentRO<Fdp.Toolkit.Behavior.Components.BehaviorState>(hovered.Value);
                        string docName = ds.ActiveBehaviorHash == 0 ? "Idle" : ds.ActiveBehaviorHash.ToString();

                        if (_behaviorRegistry != null && _behaviorRegistry.TryGetName(ds.ActiveBehaviorHash, out string? name))
                            docName = name;

                        sb.AppendLine($"Behavior: {docName} (Tier {ds.BrainTier})");
                    }

                    if (sb.Length > 0)
                    {
                        ImGuiNET.ImGui.BeginTooltip();
                        ImGuiNET.ImGui.TextUnformatted(sb.ToString().TrimEnd());
                        ImGuiNET.ImGui.EndTooltip();
                    }
                }
            }

            // Trigger ImGui popup when a right-click was recorded this frame.
            _gizmoLayer?.DrawContextMenu();
            _gizmoLayer?.DrawStructInspector();

            // Render gizmo-contributed main menu items (e.g. "View > Tactical Map Layers...").
            var gizmoMenus = _gizmoLayer?.ConsumeMainMenu();
            if (gizmoMenus != null && gizmoMenus.Count > 0)
            {
                if (ImGuiNET.ImGui.BeginMainMenuBar())
                {
                    GizmoMap.Presentation.ImGuiMenuRenderer.DrawMenus(gizmoMenus, actionId =>
                    {
                        _interactionBus?.Publish(new GizmoMenuActionEvent { AnchorId = 0, ActionId = actionId });
                    });
                    ImGuiNET.ImGui.EndMainMenuBar();
                }
            }

            // Trigger rename modal when requested by DrainToolActivationEvents.
            if (_openRenameModalThisFrame)
            {
                ImGuiNET.ImGui.OpenPopup("Rename Entity");
                _openRenameModalThisFrame = false;
            }

            // Render the rename modal.
            bool isRenameOpen = true;
            if (ImGuiNET.ImGui.BeginPopupModal("Rename Entity", ref isRenameOpen, ImGuiNET.ImGuiWindowFlags.AlwaysAutoResize))
            {
                if (ImGuiNET.ImGui.IsKeyPressed(ImGuiNET.ImGuiKey.Escape))
                    ImGuiNET.ImGui.CloseCurrentPopup();

                ImGuiNET.ImGui.InputText("New Name", ref _renameBuffer, 64);
                ImGuiNET.ImGui.Separator();

                bool canSave = !string.IsNullOrWhiteSpace(_renameBuffer);
                if (!canSave) ImGuiNET.ImGui.BeginDisabled();
                if (ImGuiNET.ImGui.Button("Save") && canSave)
                {
                    // Find entity by network id, read existing EntityInfo and update name.
                    if (_world != null)
                    {
                        var q = _world.Query()
                            .With<Fdp.Toolkit.Replication.Components.NetworkIdentity>()
                            .With<EntityInfo>()
                            .Build();
						EntityInfo updatedInfo = default;
                        foreach (var e in q)
                        {
                            if (_world.GetComponent<Fdp.Toolkit.Replication.Components.NetworkIdentity>(e).Value == _renameTargetNetworkId)
                            {
                                updatedInfo = _world.GetComponent<EntityInfo>(e);
                                break;
                            }
                        }
                        updatedInfo.Name = new Fdp.Core.FixedString64(_renameBuffer.Trim());
                        _editorLogic?.CommitPropertyEdit(_renameTargetNetworkId, new List<object> { updatedInfo });
                    }
                    ImGuiNET.ImGui.CloseCurrentPopup();
                }
                if (!canSave) ImGuiNET.ImGui.EndDisabled();

                ImGuiNET.ImGui.SameLine();
                if (ImGuiNET.ImGui.Button("Cancel"))
                    ImGuiNET.ImGui.CloseCurrentPopup();

                ImGuiNET.ImGui.EndPopup();
            }

            // BATCH-21: Draw the scenario picker modal when open.
            _scenarioPickerModal?.DrawModal("Load Scenario");
        }

        /// <inheritdoc/>
        public void RegisterWindows(Fdp.Presentation.WindowManager.WindowManager windowManager)
        {
            _wm = windowManager;

            // ── AIE-015: Shared AI editor — document manager + perspective switcher ───────────
            // Wire the perspective switcher to the window manager so manual toolbar
            // switches can activate the most-recently-opened doc of that kind.
            _perspectiveSwitcher = new WindowManagerPerspectiveSwitcher(windowManager);

            // Build shared services needed by registrars.
            var catalog = _aiCatalogBuilder?.Catalog ?? new AssetCatalog();

            // ── AIE-051: Reference catalog contributors ───────────────────────────────────────────
            var referenceContributors = new IReferenceCatalogContributor[]
            {
                new BTreeBlackboardVariableContributor(),
                new HsmReferenceContributor(),
                new BlueprintReferenceContributor(),
            };
            var referenceCatalog = new ReferenceCatalog(catalog, referenceContributors);
            // ─────────────────────────────────────────────────────────────────────────────────────

            var refactorService = new RefactorService(
                referenceCatalog,
                catalog,
                new AtomicMultiFileWriter());

            // ── AIE-050: Comparison sanitizers + ComparisonExportBuilder ─────────────────────────
            var sanitizerRegistry = new SanitizerRegistry();
            sanitizerRegistry.Register(new BTreeComparisonSanitizer(catalog));
            sanitizerRegistry.Register(new HsmComparisonSanitizer(catalog));
            sanitizerRegistry.Register(new BlueprintComparisonSanitizer(
                new NoOpComparisonMigrationAdapter(),
                new NoOpMetaEnvelopeSanitizer(),
                catalog));
            var comparisonExportBuilder = new ComparisonExportBuilder();
            var comparisonSessionRegistry = new ComparisonSessionRegistry();
            // ─────────────────────────────────────────────────────────────────────────────────────

            // ── AIE-052: Blackboard aggregator service + strategies ───────────────────────────────
            // Construct service with empty strategy list, then register strategies after to break
            // the circular dependency (service ↔ strategies take each other in their ctors).
            // AIE-053: Shared ActionSchemaExporter instance — also forwarded to the Inspector
            // for sub-element collision diagnostics.
            var sharedSchemaExporter = new ActionSchemaExporter();
            // AN7 live-discovery fix: the exporter is constructed empty and is otherwise only
            // Rebuilt by ActionSchemaExporterCatalogWatcher (not wired here). Populate it NOW by
            // reflecting the already-loaded game assemblies, so the behavior-action catalog +
            // palette below actually contain the non-channel [SharedAiAction]/AiPrimitive entries.
            // (Post-reload refresh of newly-generated AiPrimitive actions is a separate follow-up.)
            sharedSchemaExporter.Rebuild();

            // ── AN7: unified behavior-action catalog ────────────────────────────────────────────
            // Compose the channel-command catalog (same source as the palette built ~line 917) +
            // the shared ActionSchemaExporter into one IBehaviorActionCatalog. It subscribes to
            // sharedSchemaExporter.Changed internally, so it stays fresh across hot-reloads —
            // construct ONCE here and reuse for both the palette and BlueprintDocumentFactory
            // (live non-channel "Action:{FQN}" pin projection).
            var bpChannelCatalog = Hrot.Blueprints.Core.Compiler.Catalogs.BuiltInChannelCommandCatalog.Instance;
            _behaviorActionCatalog = new Hrot.Blueprints.Editor.ActionCatalog.BehaviorActionCatalog(
                bpChannelCatalog, sharedSchemaExporter);
            // Rebuild the palette now that the behavior-action catalog exists so non-channel actions
            // appear alongside the channel-command entries built earlier (line ~917).
            _blueprintPaletteEntries = Hrot.Blueprints.Editor.BlueprintEditorBootstrap.CreatePaletteRegistry(
                bpChannelCatalog, behaviorActionCatalog: _behaviorActionCatalog);
            // ────────────────────────────────────────────────────────────────────────────────────

            var aggregatorService = new BlackboardAggregatorService(
                Array.Empty<IBlackboardAggregatorStrategy>(),
                sharedSchemaExporter,
                catalog);
            aggregatorService.Register(new BTreeBlackboardAggregatorStrategy(aggregatorService));
            aggregatorService.Register(new HsmBlackboardAggregatorStrategy(aggregatorService));
            // ─────────────────────────────────────────────────────────────────────────────────────

            var debugRegistry   = new DebugSessionRegistry();
            var liveProvider    = new LiveSessionRegistry();

            // ── AIE-030: Register BTree/HSM debug session factories ─────────────────────────────
            // Factories capture the pre-built sessions (created in Initialize with the
            // AiTracerCoordinator).  NodeDebugMetadata symbolication is already wired because
            // BTreeAssetContributor was constructed with _btreeDebugSession and calls
            // SetDebugMetadata on every LoadFrom / RegisterBlob invocation.
            if (_btreeDebugSession != null)
                debugRegistry.RegisterSessionFactory<Hrot.BTree.Editor.Debug.BTreeDebugSession>(
                    () => _btreeDebugSession);
            if (_hsmDebugSession != null)
                debugRegistry.RegisterSessionFactory<Hrot.Hsm.Editor.Debug.HsmDebugSession>(
                    () => _hsmDebugSession);
            // ────────────────────────────────────────────────────────────────────────────────────

            // Build per-perspective selection stores and registrars.
            // AIE-034: pass _bpManager so each perspective gets Watch + Breakpoints windows.
            // AIE-050: pass comparison services so BlackboardAuthoringWindow shows comparison toolbar.
            // AIE-052: pass aggregatorService so BlackboardAuthoringWindow runs bin-packing with sub-tree requirements.
            // SE1: build a StructEdit IComponentEditService so the Inspector renders facet
            // structs as live, editable field rows (enum→combo, bool→checkbox, number/text inputs)
            // instead of the "[FacetTypeName] + Apply" stub. The attribute picker drawers
            // (BehaviorHash/BlackboardField/HSM action/guard/state/event) require the *active*
            // asset, which changes per opened document; the existing drawers capture a fixed
            // asset in their ctor and a single registered drawer cannot follow the selection
            // store dynamically. Per SE1 scope, picker drawers are deferred — facet picker
            // fields fall through to plain text inputs (acceptable). The edit service alone is
            // the core win.
            var facetEditService = new ComponentEditServiceBuilder().Build();
            _btreeRegistrar    = new PerspectiveWorkspaceRegistrar(
                "BTree", _btreeSelectionStore, catalog, refactorService, debugRegistry,
                breakpointManager:    _bpManager,
                sanitizerRegistry:    sanitizerRegistry,
                exportBuilder:        comparisonExportBuilder,
                sessionRegistry:      comparisonSessionRegistry,
                aggregatorService:    aggregatorService,
                schemaExporter:       sharedSchemaExporter,
                facetEditService:     facetEditService);
            _hsmRegistrar      = new PerspectiveWorkspaceRegistrar(
                "HSM", _hsmSelectionStore, catalog, refactorService, debugRegistry,
                breakpointManager:    _bpManager,
                sanitizerRegistry:    sanitizerRegistry,
                exportBuilder:        comparisonExportBuilder,
                sessionRegistry:      comparisonSessionRegistry,
                aggregatorService:    aggregatorService,
                schemaExporter:       sharedSchemaExporter,
                facetEditService:     facetEditService);
            _blueprintRegistrar = new PerspectiveWorkspaceRegistrar(
                "Blueprint", _blueprintSelectionStore, catalog, refactorService, debugRegistry,
                breakpointManager:    _bpManager,
                sanitizerRegistry:    sanitizerRegistry,
                exportBuilder:        comparisonExportBuilder,
                sessionRegistry:      comparisonSessionRegistry,
                schemaExporter:       sharedSchemaExporter);

            // Document manager — activated doc drives perspective switch.
            _aiDocumentManager = new AiDocumentManager(_perspectiveSwitcher);
            _perspectiveSwitcher.SetDocumentManager(_aiDocumentManager);

            // AIE-025: Retarget per-perspective selection stores when the active document changes.
            // Each BlackboardAuthoringWindow reads its store's ActiveAsset every frame (pull model),
            // so updating ActiveAsset here is all that is needed for the window to show the right schema.
            // AIE-047/048: Also retarget My Blueprint + Details + Variables windows for Blueprint.
            _aiDocumentManager.ActiveChanged += () =>
            {
                var active = _aiDocumentManager.Active;
                _btreeSelectionStore.ActiveAsset       = (active?.Kind == Hrot.Editor.AiShared.AssetKind.BTree)      ? active.Asset : null;
                _hsmSelectionStore.ActiveAsset         = (active?.Kind == Hrot.Editor.AiShared.AssetKind.Hsm)        ? active.Asset : null;
                _blueprintSelectionStore.ActiveAsset   = (active?.Kind == Hrot.Editor.AiShared.AssetKind.Blueprint)  ? active.Asset : null;

                // SE2: Rebuild picker-drawer maps for the newly active BTree / HSM asset so that
                // attribute-dispatched dropdowns (BehaviorHash, BlackboardField, HSM action/guard/
                // state/event) reflect the fields and methods of the live document rather than a
                // stale, fixed-at-ctor asset.  The maps are small (1–2 entries) and built cheaply
                // from the asset already in memory — no I/O.  Calling SetFacetEditService also
                // drops the cached StructEdit session so the next render opens a fresh one against
                // the correct facet type (harmless when the asset type did not change).
                if (active?.Kind == Hrot.Editor.AiShared.AssetKind.BTree
                    && active.Asset is Hrot.BTree.Editor.Model.BehaviorTreeAsset btreeAsset
                    && _behaviorRegistry is not null)
                {
                    var btreeDrawers = BTreePickerDrawerFactory.BuildDrawers(btreeAsset, _behaviorRegistry);
                    _btreeRegistrar?.Inspector.SetFacetEditService(facetEditService, btreeDrawers);
                    // FIX-A: wire the per-asset facet dispatcher so InspectorWindow.GetCurrentFacet()
                    // returns a non-null facet when a BTree node is selected.
                    _btreeRegistrar?.Inspector.SetFacetDispatcher(
                        BTreeSelectionBridgeHelper.BuildFacetDispatcher(btreeAsset));
                }
                else if (active?.Kind == Hrot.Editor.AiShared.AssetKind.Hsm
                    && active.Asset is Hrot.Hsm.Editor.Model.HsmAsset hsmAsset)
                {
                    var hsmDrawers = HsmPickerDrawerFactory.BuildDrawers(hsmAsset);
                    _hsmRegistrar?.Inspector.SetFacetEditService(facetEditService, hsmDrawers);
                    // FIX-A: wire the per-asset facet dispatcher so InspectorWindow.GetCurrentFacet()
                    // returns a non-null facet when an HSM state is selected.
                    _hsmRegistrar?.Inspector.SetFacetDispatcher(
                        HsmSelectionBridgeHelper.BuildFacetDispatcher(hsmAsset));
                }
                else
                {
                    // Switching to Blueprint or clearing: reset pickers to null (plain-text fallback).
                    // The edit service itself remains so the inspector still renders struct fields.
                    _btreeRegistrar?.Inspector.SetFacetEditService(facetEditService, null);
                    _hsmRegistrar?.Inspector.SetFacetEditService(facetEditService, null);
                    // FIX-A: clear facet dispatchers when no BTree/HSM is active.
                    _btreeRegistrar?.Inspector.SetFacetDispatcher(null);
                    _hsmRegistrar?.Inspector.SetFacetDispatcher(null);
                }

                // AIE-047/048: Retarget Blueprint-specific windows.
                if (active?.Kind == Hrot.Editor.AiShared.AssetKind.Blueprint)
                {
                    // Extract the BlueprintAsset from the canvas context (set by BlueprintDocumentFactory).
                    var ctx = active.ViewState as Hrot.Editor.AiShared.Windows.AiCanvasContext;
                    var bpAsset = ctx?.AssetRef as Hrot.Blueprints.Core.Assets.BlueprintAsset;

                    // Retarget My Blueprint window.
                    // BCP-BATCH-02-FIX Task 3: pass the document's real command set (ctx.Commands)
                    // so the panel's "+ Variable" hits the registered editor.create-variable handler
                    // (which appends a VariableDecl) instead of a fresh, empty command instance.
                    _blueprintMyBlueprintWindow?.Retarget(
                        editableAsset:  active.Asset,
                        blueprintAsset: bpAsset,
                        hostServices:   ctx?.View.Host,
                        commands:       ctx?.Commands ?? new NodeEditor.Core.Action.EditorCommandsImpl());

                    // Retarget Details window (just needs the BlueprintAsset).
                    _blueprintDetailsWindow?.Retarget(bpAsset);

                    // Retarget Variables window via legacy bridge store.
                    _blueprintLegacySelectionStore.SelectAsset(bpAsset);

                    // BATCH-03D2: Retarget Graph Signature window.
                    _blueprintSignatureWindow?.Retarget(bpAsset);
                }
                else
                {
                    // Clear Blueprint windows when switching away from Blueprint perspective.
                    _blueprintMyBlueprintWindow?.Retarget(null, null, null, null);
                    _blueprintDetailsWindow?.Retarget(null);
                    _blueprintLegacySelectionStore.SelectAsset(null);
                    _blueprintSignatureWindow?.Retarget(null);
                }
            };

            // Global Asset Browser — single instance, Global scope, shows Open-docs section.
            var assetBrowserFindResults = new FindResultsWindow(
                idOverride:        "ai_asset_browser_find_results",
                owningPerspective: "Global");
            var assetBrowserIconProvider = new SilkIconProvider(windowManager.Atlas);
            _aiAssetBrowser = new AssetBrowserDockedWindow(
                catalog:          catalog,
                icons:            assetBrowserIconProvider,
                options:          new AssetBrowserPanelOptions { Kinds = AssetKindFilter.All, ShowAllTab = false },
                onAssetActivated: asset => _aiDocumentManager?.Open(asset),
                id:               "ai_asset_browser"); // prior global Asset Browser id (MTB-P7-T4: register docked host with the prior id/scope)

            var recipeModal = new Hrot.Blueprints.Editor.Windows.RecipeCreateModal((recipe, newName) =>
            {
                var service = new Hrot.Blueprints.Editor.NewFromRecipeService();
                var newAsset = service.CreateFromRecipe(recipe, newName);

                string? recipeProjectDir = null;
                var relativeProjectPath = System.IO.Path.Combine(AiBehaviorsProjectPath);
                foreach (var start in new[] { Environment.CurrentDirectory, AppDomain.CurrentDomain.BaseDirectory })
                {
                    var dir = start;
                    while (!string.IsNullOrEmpty(dir))
                    {
                        var candidate = System.IO.Path.Combine(dir, relativeProjectPath);
                        if (System.IO.File.Exists(candidate))
                        {
                            recipeProjectDir = System.IO.Path.GetDirectoryName(candidate);
                            break;
                        }
                        dir = System.IO.Path.GetDirectoryName(dir);
                    }

                    if (recipeProjectDir != null)
                        break;
                }

                string saveDir = recipeProjectDir != null
                    ? System.IO.Path.Combine(recipeProjectDir, AssetRoots.RecipesRelative(AssetKind.Blueprint))
                    : System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Recipes", "Blueprints");
                System.IO.Directory.CreateDirectory(saveDir);
                string path = System.IO.Path.Combine(saveDir, $"{newName}.bp.json");

                Hrot.Blueprints.Editor.SaveActiveBlueprintCommand.Save(newAsset, path);

                var aiAsm = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == "Hrot.AI.Behaviors");

                if (aiAsm != null)
                {
                    _aiCatalogBuilder?.RefreshFromAssembly(aiAsm);

                    var editableAsset = _aiCatalogBuilder?.Catalog.FindByAssetId(newAsset.AssetId);
                    if (editableAsset != null)
                        _aiDocumentManager?.Open(editableAsset);
                }
            });

            _aiAssetBrowser.CustomToolbarDraw = () =>
            {
                if (ImGuiNET.ImGui.Button("+ New from Recipe..."))
                    recipeModal.Open();

                ImGuiNET.ImGui.Separator();
                recipeModal.Draw();
            };

            // Register all three perspective side-panel sets.
            _btreeRegistrar.RegisterWindows(windowManager);
            _hsmRegistrar.RegisterWindows(windowManager);
            _blueprintRegistrar.RegisterWindows(windowManager);

            // ── MVE-BATCH-03: "Run Blueprint on Selected Entity" toolbar button ────────────────
            // Register via IWindowRegistrar.RegisterToolbarEntry so the button appears in the
            // Blueprint toolbar. The callback is ImGui-free and headlessly testable; DrawUI renders
            // the ImGui button gated on ImGui.GetCurrentContext() != Zero.
            var bpWindowRegistrar = new Hrot.Blueprints.Editor.Internal.CaptureWindowRegistrar();
            bpWindowRegistrar.RegisterToolbarEntry(
                Hrot.Blueprints.Editor.Runtime.RunBlueprintOnEntityCommand.ToolbarLabel,
                () =>
                {
                    // Resolve active asset: document manager → active doc → ViewState → AssetRef.
                    var activeCtx = _aiDocumentManager?.Active?.ViewState
                        as Hrot.Editor.AiShared.Windows.AiCanvasContext;
                    var activeAssetRef = activeCtx?.AssetRef;

                    Hrot.Blueprints.Editor.Runtime.RunBlueprintOnEntityCommand.Execute(
                        world:           _world,
                        registry:        _blueprintRegistry,
                        selectedEntity:  _aiEditorSelectionStore.SelectedEntity,
                        activeAssetRef:  activeAssetRef,
                        report:          msg => _blueprintRunStatus = msg);
                });
            _blueprintRunButtonCallback = bpWindowRegistrar.GetToolbarCallback(
                Hrot.Blueprints.Editor.Runtime.RunBlueprintOnEntityCommand.ToolbarLabel);

            // ── MVE-BATCH-04: "Save Blueprint" toolbar entry + Ctrl+S ────────────────────────────
            // Resolves active asset via AiDocumentManager (same path as run-button).
            // _blueprintSaveDirtyTracker is initialised at field declaration; reused here.
            var saveRegistrar = new Hrot.Blueprints.Editor.Internal.CaptureWindowRegistrar();
            saveRegistrar.RegisterToolbarEntry(
                "Save Blueprint",
                () =>
                {
                    Hrot.Blueprints.Editor.SaveActiveBlueprintCommand.SaveFromActiveDocument(
                        _aiDocumentManager,
                        _blueprintSaveDirtyTracker,
                        msg => _blueprintSaveStatus = msg);
                });
            _blueprintSaveCallback = saveRegistrar.GetToolbarCallback("Save Blueprint");
            // ─────────────────────────────────────────────────────────────────────────────────────

            // ── MVE-BATCH-05: "Compile / Reload Blueprint" toolbar entry ─────────────────────────
            // Save-then-compile decision: QuickReloadService compiles from the in-memory asset
            // (via _editorState.GetInMemoryAsset / the asset reference in the active document),
            // NOT from the .bp.json on disk.  Therefore no pre-save is required; the callback
            // triggers the _blueprintQuickReloadTrigger which calls QuickReloadService.TriggerAsync
            // with the live in-memory BlueprintAsset.  If the user WANTS the compiled output
            // persisted they should Save first (MVE-04) — but compilation itself works from RAM.
            var compileRegistrar = new Hrot.Blueprints.Editor.Internal.CaptureWindowRegistrar();
            compileRegistrar.RegisterToolbarEntry(
                "Compile / Reload Blueprint",
                () =>
                {
                    var activeCtx = _aiDocumentManager?.Active?.ViewState
                        as Hrot.Editor.AiShared.Windows.AiCanvasContext;
                    var activeAssetRef = activeCtx?.AssetRef
                        as Hrot.Blueprints.Core.Assets.BlueprintAsset;

                    if (activeAssetRef == null)
                    {
                        _blueprintCompileStatus = "No active blueprint document.";
                        return;
                    }

                    // Re-use the trigger wired up in RegisterWindows (RegenerationScheduler path).
                    // The trigger was also wired directly, so invoke it here for the button.
                    _blueprintQuickReloadTrigger?.Invoke(_aiDocumentManager!.Active!.Asset);
                });
            _blueprintCompileCallback = compileRegistrar.GetToolbarCallback("Compile / Reload Blueprint");
            // ─────────────────────────────────────────────────────────────────────────────────────

            // ── BSA-205: "Entity Blueprints" perspective window ───────────────────────────────
            // Registered via RegisterExtraWindow so it appears in the Window → Blueprint menu.
            var entityBpWindow = new Hrot.Blueprints.Editor.EntityBlueprints.EntityBlueprintsManagedWindow(
                () =>
                {
                    var model = new Hrot.Blueprints.Editor.EntityBlueprints.EntityBlueprintsEditModel(
                        _world!, _blueprintRegistry!, Entity.Null);
                    var panel = new Hrot.Blueprints.Editor.EntityBlueprints.EntityBlueprintsPanel(
                        model, _world!, _blueprintRegistry!,
                        entityResolver: () => _aiEditorSelectionStore?.SelectedEntity);
                    return panel;
                });
            _blueprintRegistrar!.RegisterExtraWindow(windowManager, entityBpWindow);
            // ─────────────────────────────────────────────────────────────────────────────────────

            // ── PU-603/PU-D11: "Save All" callback — FlushNow + SaveAllAiDocumentsCommand ─────────
            // Build per-kind save delegates (injected to avoid circular assembly refs, design §PU-602).
            // Blueprint: reuse SaveActiveBlueprintCommand.Save (unchanged write path).
            // BTree/HSM: mapper → JSON serializer → AtomicFileWriter.
            // PU-D11 (PU-402): these delegates are also reused by the debounced RegenerationScheduler
            // flushAction so BTree/HSM flush writes JSON (not C#) — see the scheduler wiring below.
            Hrot.Editor.AiShared.SaveAllAiDocumentsCommand.SaveDelegate saveBlueprintDelegate =
                (asset, path) =>
                {
                    // doc.Asset is BlueprintFileAsset (IEditableAsset wrapper); the real
                    // BlueprintAsset is stored in the AiCanvasContext.AssetRef of the document.
                    // Find the matching document by AssetId to get the canvas context.
                    var doc = _aiDocumentManager?.OpenDocuments
                        .FirstOrDefault(d => d.Asset.AssetId == asset.AssetId);
                    var ctx     = doc?.ViewState as Hrot.Editor.AiShared.Windows.AiCanvasContext;
                    var bpAsset = ctx?.AssetRef as Hrot.Blueprints.Core.Assets.BlueprintAsset;
                    if (bpAsset == null) return;
                    Hrot.Blueprints.Editor.SaveActiveBlueprintCommand.Save(bpAsset, path);
                    _blueprintSaveDirtyTracker.MarkClean(bpAsset.AssetId);
                };

            Hrot.Editor.AiShared.SaveAllAiDocumentsCommand.SaveDelegate saveBTreeDelegate =
                (asset, path) =>
                {
                    var btreeAsset = asset as Hrot.BTree.Editor.Model.BehaviorTreeAsset;
                    if (btreeAsset == null) return;
                    var dto        = Hrot.BTree.Editor.Persistence.BehaviorTreeAssetMapper.ToDto(btreeAsset);
                    var json       = Hrot.AiEditor.Persistence.BTree.BTreeJsonServices.Serialize(dto);
                    var prettyJson = Fdp.Toolkit.Serialization.JsonAestheticFormatter.FlattenNumericArrays(json);
                    Hrot.AiEditor.Persistence.AtomicFileWriter.Write(path, prettyJson);
                };

            Hrot.Editor.AiShared.SaveAllAiDocumentsCommand.SaveDelegate saveHsmDelegate =
                (asset, path) =>
                {
                    var hsmAsset   = asset as Hrot.Hsm.Editor.Model.HsmAsset;
                    if (hsmAsset == null) return;
                    var dto        = Hrot.Hsm.Editor.Persistence.HsmAssetMapper.ToDto(hsmAsset);
                    var json       = Hrot.AiEditor.Persistence.Hsm.HsmJsonServices.Serialize(dto);
                    var prettyJson = Fdp.Toolkit.Serialization.JsonAestheticFormatter.FlattenNumericArrays(json);
                    Hrot.AiEditor.Persistence.AtomicFileWriter.Write(path, prettyJson);
                };

            _saveAllCallback = () =>
            {
                // FlushNow() drains any debounced .cs regeneration before saving JSON.
                _regenerationScheduler?.FlushNow();

                Hrot.Editor.AiShared.SaveAllAiDocumentsCommand.Execute(
                    _aiDocumentManager,
                    saveBlueprintDelegate,
                    saveBTreeDelegate,
                    saveHsmDelegate,
                    msg => _saveAllStatus = msg);
            };

            // ── BATCH-20 (DEC-9): per-kind service registry for Save-As ──────────────────────────
            // Create the INewAssetService dictionary so ShellSaveCommands.requestSaveAs
            // can seed a SaveAsDialog from the current document's asset.
            _newAssetServices = new Dictionary<Hrot.Editor.AiShared.AssetKind, Hrot.Editor.AiShared.Recipes.INewAssetService>
            {
                [Hrot.Editor.AiShared.AssetKind.Blueprint] = new Hrot.Blueprints.Editor.BlueprintNewAssetService(),
                [Hrot.Editor.AiShared.AssetKind.BTree]     = new Hrot.BTree.Editor.BTreeNewAssetService(),
                [Hrot.Editor.AiShared.AssetKind.Hsm]       = new Hrot.Hsm.Editor.HsmNewAssetService(),
            };

            // Scenario: create a thin session adapter for IEditorLogic → IScenarioCreationSession.
            // The editor app (_editorLogic) is guaranteed non-null at this point.
            if (_editorApp != null)
            {
                _newAssetServices[Hrot.Editor.AiShared.AssetKind.Scenario] =
                    new ScenarioNewAssetService(new EditorLogicSessionAdapter(_editorApp));
            }

            // Save-As blueprint file-save delegate (mint-only, so the dialog performs the save).
            Action<Hrot.Editor.AiShared.IEditableAsset, string> saveAsBlueprintToFile = (asset, path) =>
            {
                // For Save-As, the asset is a freshly minted BlueprintEditableAssetAdapter
                // wrapping a BlueprintAsset. Extract the inner asset for serialization.
                try
                {
                    if (asset is Hrot.Blueprints.Editor.Variables.BlueprintEditableAssetAdapter adapter)
                    {
                        Hrot.Blueprints.Editor.SaveActiveBlueprintCommand.Save(adapter.Asset, path);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[SaveAs] Failed to save Blueprint '{asset.Name}': {ex.Message}");
                }
            };

            // Save-As scenario delegate: routes to IEditorLogic.SaveScenarioAs.
            Action<string> saveAsScenario = fullName =>
            {
                _editorLogic?.SaveScenarioAs(fullName);
            };

            // ── BATCH-06: register shell save commands (Ctrl+S/Ctrl+Shift+S fix, §20) ──────────
            // Wire the three save commands into the global shell command set with production
            // save delegates and a requestSaveAs seam (DEC-9: connected to SaveAsDialog model).
            _shellInputSource = new ImGuiInputSource();
            _shellHotkeyDispatcher = new Hrot.Editor.AiShared.Windows.EditorHotkeyDispatcher(
                _shellInputSource);

            Hrot.Editor.AiShared.Documents.ShellSaveCommands.Register(
                register:          windowManager.ShellCommands.Register,
                docManager:        _aiDocumentManager,
                saveBlueprint:     saveBlueprintDelegate,
                saveBTree:         saveBTreeDelegate,
                saveHsm:           saveHsmDelegate,
                saveScenario:      null, // Scenario saved via IEditorLogic, not file delegate
                requestSaveAs:     doc =>
                {
                    // DEC-9 RESOLVED: the requestSaveAs seam creates a SaveAsDialog
                    // seeded from the document's asset and attempts to confirm.
                    // UI surfacing (ImGui name/folder picker popup) deferred to
                    // Phase 7 / DBT-2; until then Confirm() uses the source asset's
                    // name and root path, which succeeds for empty-SourceFilePath
                    // "promote to file" paths (§18.5) and fails gracefully with a
                    // collision error when the target file already exists.
                    if (_newAssetServices == null) return;

                    var dialog = new Hrot.Editor.AiShared.Recipes.SaveAsDialog(
                        doc.Asset,
                        _newAssetServices,
                        knownFolderPaths: Array.Empty<string>(),
                        saveMintOnlyAsset: saveAsBlueprintToFile,
                        saveScenarioAs:    saveAsScenario);

                    var result = dialog.Confirm();
                    if (result.IsSuccess)
                    {
                        _saveAllStatus = $"[OK] Saved '{result.Asset?.Name}' as new asset.";
                    }
                    else
                    {
                        _saveAllStatus = $"[INFO] Save As '{doc.Asset.Name}': {result.Error}";
                    }
                },
                report:            msg => _saveAllStatus = msg);
            // ───────────────────────────────────────────────────────────────────────────────────

            // PU-603: flush-on-close — save dirty path'd docs before close.
            // Manager fires BeforeDocumentClosed before removing the doc from its list.
            // _aiDocumentManager is guaranteed non-null here (assigned 3 lines above).
            _aiDocumentManager.BeforeDocumentClosed += doc =>
            {
                    if (!doc.IsDirty) return;
                    var asset = doc.Asset;
                    var path  = asset.SourceFilePath;
                    if (string.IsNullOrEmpty(path)) return; // no path → skip silently

                    try
                    {
                        switch (doc.Kind)
                        {
                            case Hrot.Editor.AiShared.AssetKind.Blueprint:
                                // saveBlueprintDelegate resolves BlueprintAsset via ViewState.
                                saveBlueprintDelegate(asset, path);
                                doc.MarkClean();
                                break;
                            case Hrot.Editor.AiShared.AssetKind.BTree:
                                saveBTreeDelegate(asset, path);
                                doc.MarkClean();
                                break;
                            case Hrot.Editor.AiShared.AssetKind.Hsm:
                                saveHsmDelegate(asset, path);
                                doc.MarkClean();
                                break;
                            default:
                                // Other kinds (Scenario, Blackboard, Utility) are not saved via
                                // the document-save path — skip silently.
                                break;
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[PU-603] Failed to save '{asset.Name}' on close: {ex.Message}");
                    }
                };
            // ─────────────────────────────────────────────────────────────────────────────────────

            // ── AIE-031: Register BTree/HSM runtime inspector panes ─────────────────────────────
            // Each pane holds a reference to its session; the window selects the matching pane
            // at draw time based on the active asset kind.
            if (_btreeDebugSession != null)
            {
                var btreePane = new BTreeRuntimeInspectorPane();
                btreePane.SetSession(_btreeDebugSession);
                _btreeRegistrar.RuntimeInspector.RegisterPane(btreePane);
            }
            if (_hsmDebugSession != null)
            {
                var hsmPane = new HsmRuntimeInspectorPane();
                hsmPane.SetSession(_hsmDebugSession);
                _hsmRegistrar.RuntimeInspector.RegisterPane(hsmPane);
            }
            if (_blueprintDebugSession != null)
            {
                var blueprintPane = new Hrot.Blueprints.Editor.Inspector.BlueprintRuntimeInspectorPane();
                blueprintPane.SetSession(_blueprintDebugSession);
                blueprintPane.SetResolvers(
                    selectedEntityResolver: () => _aiEditorSelectionStore?.SelectedEntity,
                    activeAssetIdResolver:  () =>
                    {
                        var ctx = _aiDocumentManager?.Active?.ViewState
                            as Hrot.Editor.AiShared.Windows.AiCanvasContext;
                        return (ctx?.AssetRef as Hrot.Blueprints.Core.Assets.BlueprintAsset)?.AssetId;
                    });
                _blueprintRegistrar.RuntimeInspector.RegisterPane(blueprintPane);
            }
            // ────────────────────────────────────────────────────────────────────────────────────

            // ── AIE-032: Register BTree/HSM trace lane providers ────────────────────────────────
            _btreeRegistrar.TraceTimeline.RegisterProvider(new BTreeTraceLaneProvider());
            _hsmRegistrar.TraceTimeline.RegisterProvider(new HsmTraceLaneProvider());
            // ────────────────────────────────────────────────────────────────────────────────────

            // Register the global Asset Browser.
            windowManager.RegisterWindow(_aiAssetBrowser);

            // ── AIE-020/021/022: AiGraphCanvasWindow + document factories ────────────────────────
            // Build the adapter bundle from the engine icon atlas (no GPU calls at construction time).
            var adapterBundle = new Hrot.Editor.AiShared.Adapters.AiEditorAdapterBundle(windowManager.Atlas);

            // ── BATCH-21: Scenario menu commands (New/Save/SaveAs/Load/Migration History) ──────
            // Wire the five scenario shell commands and surface them as "Scenario" menu items.
            // The Load picker opens a scenario-filtered AssetPickerModal; Save-As routes through
            // the existing saveAsScenario delegate.
            _scenarioPickerModal = new Hrot.Editor.AiShared.Browser.AssetPickerModal(
                catalog, adapterBundle.IconProvider);

            var saveAsScenarioDelegate = new Action<string>(fullName =>
            {
                _editorLogic?.SaveScenarioAs(fullName);
            });

            // Guard: a minimally-constructed EditorSubsystem (e.g. window-registration unit tests)
            // has no IEditorLogic. Skip the scenario-menu wiring in that case so RegisterWindows
            // still registers the perspective windows. Production always has _editorLogic set.
            if (_editorLogic != null)
            ScenarioMenuCommands.Register(
                registerCommand:      windowManager.ShellCommands.Register,
                menu:                 windowManager.GlobalMenu,
                commands:             windowManager.ShellCommands,
                editorLogic:          _editorLogic,
                openPicker:           (kinds, callback) =>
                {
                    _scenarioPickerModal?.Open(
                        new Hrot.Editor.AiShared.Browser.AssetBrowserPanelOptions { Kinds = kinds },
                        callback);
                },
                openSaveAsDialog:     cb =>
                {
                    // For scenario Save-As, open the dialog UI (inline asset for the model).
                    // The SaveAsDialog.Confirm() routes to saveScenarioAs → IEditorLogic.SaveScenarioAs.
                    if (_editorLogic != null && _newAssetServices != null)
                    {
                        var scenarioAsset = new ScenarioSaveAsAsset(
                            _editorLogic.LoadedScenarioName ?? "Unnamed");

                        var dialog = new Hrot.Editor.AiShared.Recipes.SaveAsDialog(
                            scenarioAsset,
                            _newAssetServices,
                            saveScenarioAs:    saveAsScenarioDelegate);

                        var result = dialog.Confirm();
                        if (result.IsSuccess)
                        {
                            // Pass the confirmed name back through the callback.
                            cb(scenarioAsset.Name);
                        }
                    }
                },
                showMigrationHistory:  sidecars =>
                {
                    // Log migration sidecars to the save status line for visibility.
                    _saveAllStatus = sidecars.Count == 0
                        ? "[Migration] No sidecars found for current scenario."
                        : $"[Migration] {sidecars.Count} sidecar(s): "
                          + string.Join(", ", sidecars.Select(s => $"{s.Kind} v{s.Version}"));
                });

            // Build per-perspective canvas renderers (CanvasRenderer is stateless — one per canvas is fine).
            var btreeCanvasRenderer     = new NodeEditor.UI.Canvas.CanvasRenderer();
            var hsmCanvasRenderer       = new NodeEditor.UI.Canvas.CanvasRenderer();
            var blueprintCanvasRenderer = new NodeEditor.UI.Canvas.CanvasRenderer();

            // Canvas windows — one per perspective.
            // BCP-F: thread FindBar + IEditorCommands from AiCanvasContext into the render call.
            // BCP-BATCH-02-FIX Task 1: pass the shared picker registry + host input so the
            // canvas draws the picker overlay every frame and pumps command hotkeys (Ctrl+F).
            var btreeCanvasWindow = new Hrot.Editor.AiShared.Windows.AiGraphCanvasWindow(
                assetKind:  "BTree",
                docManager: _aiDocumentManager,
                renderer:   new Hrot.Editor.AiShared.Windows.DelegatingCanvasRenderSeam(
                    renderDelegate:    view => btreeCanvasRenderer.Render(view, null),
                    renderWithFindBar: (view, fb, cmds) => btreeCanvasRenderer.Render(view, fb, cmds)),
                pickers:    adapterBundle.PickerRegistry,
                input:      adapterBundle.InputSource);

            var hsmCanvasWindow = new Hrot.Editor.AiShared.Windows.AiGraphCanvasWindow(
                assetKind:  "HSM",
                docManager: _aiDocumentManager,
                renderer:   new Hrot.Editor.AiShared.Windows.DelegatingCanvasRenderSeam(
                    renderDelegate:    view => hsmCanvasRenderer.Render(view, null),
                    renderWithFindBar: (view, fb, cmds) => hsmCanvasRenderer.Render(view, fb, cmds)),
                pickers:    adapterBundle.PickerRegistry,
                input:      adapterBundle.InputSource);

            // AIE-046: Blueprint canvas window.
            var blueprintCanvasWindow = new Hrot.Editor.AiShared.Windows.AiGraphCanvasWindow(
                assetKind:  "Blueprint",
                docManager: _aiDocumentManager,
                renderer:   new Hrot.Editor.AiShared.Windows.DelegatingCanvasRenderSeam(
                    renderDelegate:    view => blueprintCanvasRenderer.Render(view, null),
                    renderWithFindBar: (view, fb, cmds) => blueprintCanvasRenderer.Render(view, fb, cmds)),
                pickers:    adapterBundle.PickerRegistry,
                input:      adapterBundle.InputSource);

            // Register the canvas windows into their respective perspectives via the extension seam.
            _btreeRegistrar!.RegisterExtraWindow(windowManager, btreeCanvasWindow);
            _hsmRegistrar!.RegisterExtraWindow(windowManager, hsmCanvasWindow);
            // AIE-046: Register Blueprint canvas window into the Blueprint perspective.
            _blueprintRegistrar!.RegisterExtraWindow(windowManager, blueprintCanvasWindow);
            // BF-UX1 FIX C: wire the per-frame selection→Details bridge.
            blueprintCanvasWindow.AfterDraw =
                Hrot.Blueprints.Editor.Host.BlueprintSelectionBridgeHelper.BuildAfterDrawAction(
                    _blueprintSelectionStore);
            // FIX-A: wire per-frame canvas selection→Inspector bridges for BTree and HSM.
            // Each AfterDraw reads ctx.AssetRef (set by the document factory) and maps
            // the single selected node to a BTreeNodeSelection / HsmStateSelection published
            // to the perspective's EditorSelectionStore so GetCurrentFacet() returns non-null.
            btreeCanvasWindow.AfterDraw =
                BTreeSelectionBridgeHelper.BuildAfterDrawAction(_btreeSelectionStore);
            hsmCanvasWindow.AfterDraw =
                HsmSelectionBridgeHelper.BuildAfterDrawAction(_hsmSelectionStore);

            // ── AIE-047: Blueprint "My Blueprint" panel window ────────────────────────────────
            _blueprintMyBlueprintWindow = new Hrot.Blueprints.Editor.Windows.BlueprintMyBlueprintWindow();
            _blueprintRegistrar!.RegisterExtraWindow(windowManager, _blueprintMyBlueprintWindow);

            // ── AIE-048: Blueprint Details + Variables windows ────────────────────────────────
            _blueprintDetailsWindow = new Hrot.Blueprints.Editor.Windows.BlueprintDetailsWindow(
                selectionStore:  _blueprintSelectionStore,
                drawerRegistry:  _blueprintNodeDrawers ?? new Hrot.Blueprints.Editor.NodeDrawers.BlueprintNodeDrawerRegistry());
            _blueprintRegistrar!.RegisterExtraWindow(windowManager, _blueprintDetailsWindow);

            // BlueprintVariablesWindow (wrapped in a ManagedWindow adapter) uses the legacy
            // Blueprints.Editor.EditorSelectionStore (which holds a BlueprintAsset? directly);
            // we bridge it from the AiShared store via _blueprintLegacySelectionStore in ActiveChanged.
            _blueprintVariablesWindow = new Hrot.Blueprints.Editor.Windows.BlueprintVariablesManagedWindow(
                legacySelectionStore: _blueprintLegacySelectionStore,
                refactorService:      refactorService);
            _blueprintRegistrar!.RegisterExtraWindow(windowManager, _blueprintVariablesWindow);

            // BATCH-03D2: Graph Signature window — edits Function graph Inputs/Outputs.
            // Uses the same legacy selection store bridge (SelectAsset is called in ActiveChanged).
            _blueprintSignatureWindow = new Hrot.Blueprints.Editor.Windows.GraphSignatureWindow(
                selectionStore: _blueprintLegacySelectionStore,
                dirtyTracker:   _blueprintSaveDirtyTracker);
            _blueprintRegistrar!.RegisterExtraWindow(windowManager, _blueprintSignatureWindow);

            // BATCH-03C2: blueprint asset catalog used by BlueprintDocumentFactory to build the
            // peer-signature lookup so CallPeerBlueprintNodes project typed argument pins from the
            // peer blueprint's exported function signature (read on demand from disk).
            var blueprintPeerCatalog = new Hrot.Blueprints.Editor.BlueprintPeerSource(
                System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "blueprints"));

            // Wire AiDocumentManager.Open so that opening a BTree/HSM/Blueprint asset populates
            // ViewState via the matching document factory.
            _aiDocumentManager.DocumentOpened += doc =>
            {
                if (doc.ViewState != null) return; // already populated (re-open of existing doc)
                switch (doc.Kind)
                {
                    case Hrot.Editor.AiShared.AssetKind.BTree:
                        // AIE-033: inject BTree debug session + breakpoint manager so runtime
                        // overlay and breakpoint-gutter renderers bind to the active session.
                        doc.ViewState = Hrot.BTree.Editor.Host.BTreeDocumentFactory.Build(
                            doc.Asset, adapterBundle, _btreeSelectionStore,
                            btreeDebugSession:   _btreeDebugSession,
                            breakpointManager:   _bpManager);
                        break;
                    case Hrot.Editor.AiShared.AssetKind.Hsm:
                        // AIE-033: inject HSM debug session + breakpoint manager.
                        doc.ViewState = Hrot.Hsm.Editor.Host.HsmDocumentFactory.Build(
                            doc.Asset, adapterBundle,
                            hsmDebugSession:   _hsmDebugSession,
                            breakpointManager: _bpManager);
                        break;
                    case Hrot.Editor.AiShared.AssetKind.Blueprint:
                        // AIE-046: Blueprint canvas binding via BlueprintDocumentFactory.
                        // Injects per-document EditServiceContext into the shared EditService
                        // so node drawers route property edits through this document's CommandHistory.
                        // BCP-BATCH-03 Task 1: forward the channel-command catalog so
                        // ChannelCommandNodes project their parameter data-IN pins (projection-only).
                        doc.ViewState = Hrot.Blueprints.Editor.Host.BlueprintDocumentFactory.Build(
                            doc.Asset, adapterBundle, _blueprintEditService,
                            _blueprintPaletteEntries,
                            channelCommands: Hrot.Blueprints.Core.Compiler.Catalogs.BuiltInChannelCommandCatalog.Instance,
                            peerAssetCatalog: blueprintPeerCatalog,
                            // AN7: forward the behavior-action catalog so non-channel ChannelCommandNodes
                            // (ActionFqn set) project their parameter data-IN pins from the matching entry.
                            behaviorActions: _behaviorActionCatalog,
                            debugSession: _blueprintDebugSession);
                        break;
                    default:
                        // Other kinds (Scenario, Blackboard, Utility) have no ViewState factory —
                        // they are not document-backed kinds.
                        break;
                }

                // AIE-026: subscribe to this asset's Changed event so dirty edits
                // get queued into the regeneration scheduler.
                // PU-BATCH-10: also mark the document dirty so SaveAllAiDocumentsCommand
                // includes it (it skips docs where doc.IsDirty == false).
                if (_regenerationScheduler != null)
                {
                    var schedulerRef = _regenerationScheduler;
                    doc.Asset.Changed += () =>
                    {
                        doc.MarkDirty();
                        if (doc.Asset.IsDirty)
                            schedulerRef.Schedule(doc.Asset);
                    };
                }
            };

            // ── AIE-026: Build the BTree/HSM emit service + RegenerationScheduler ───────────────
            var btreeEmitter = new Hrot.BTree.Editor.Emit.BTreeFluentEmitter();
            var hsmEmitter   = new Hrot.Hsm.Editor.Emit.HsmFluentEmitter();

            // PU-D11 (PU-402): emitService is no longer used by the RegenerationScheduler
            // flushAction (which now writes JSON via saveBTreeDelegate/saveHsmDelegate instead
            // of C#). The emitters + emitService remain available for any future direct C# emit
            // path (e.g. hand-authored assets). AiAssetEmitService is NOT removed per spec.
            var emitService = new Hrot.Editor.AiShared.Emit.AiAssetEmitService(
                emitDelegate: asset => asset switch
                {
                    Hrot.BTree.Editor.Model.BehaviorTreeAsset bt => btreeEmitter.Emit(bt),
                    Hrot.Hsm.Editor.Model.HsmAsset             hs => hsmEmitter.Emit(hs),
                    _                                             => null,
                },
                postEmit: (asset, _) =>
                {
                    // Clear in-memory dirty flag after a successful emit (written or no-op).
                    if (asset is Hrot.BTree.Editor.Model.BehaviorTreeAsset btAsset)
                        btAsset.ClearDirty();
                    else if (asset is Hrot.Hsm.Editor.Model.HsmAsset hsmAsset)
                        hsmAsset.ClearDirty();
                });
            _ = emitService; // suppress unused-variable lint; see comment above

            // MVE-BATCH-05 (Blueprint): Wire the QuickReloadService so that every dirty Blueprint
            // asset that the RegenerationScheduler flushes is automatically compiled and committed
            // into the SAME _blueprintRegistry instance that the kernel ticks.
            //
            // QuickReloadService (Hrot.Blueprints.Editor.Reload) requires a
            // Fdp.Toolkit.Behavior.AiHotReloadCoordinator (the lightweight FDP variant, not
            // the file-watching Hrot.Editor variant).  A new instance is constructed with the
            // SAME _behaviorRegistry and _blueprintRegistry references, so ApplyQuickReload
            // commits into the exact registry BlueprintTickSystem reads — instance sharing proven.
            //
            // TriggerAsync is synchronous internally (returns Task.FromResult), so
            // .GetAwaiter().GetResult() is safe here — it never yields to the thread pool.
            // Result/diagnostics are surfaced to _blueprintCompileStatus.
            {
                string? quickReloadProjectDir = null;
                var quickReloadRelativeProjectPath = System.IO.Path.Combine(AiBehaviorsProjectPath);
                foreach (var start in new[] { Environment.CurrentDirectory, AppDomain.CurrentDomain.BaseDirectory })
                {
                    var dir = start;
                    while (!string.IsNullOrEmpty(dir))
                    {
                        var candidate = System.IO.Path.Combine(dir, quickReloadRelativeProjectPath);
                        if (System.IO.File.Exists(candidate))
                        {
                            quickReloadProjectDir = System.IO.Path.GetDirectoryName(candidate);
                            break;
                        }
                        dir = System.IO.Path.GetDirectoryName(dir);
                    }

                    if (quickReloadProjectDir != null)
                        break;
                }
                var bpDir      = quickReloadProjectDir != null
                    ? System.IO.Path.Combine(quickReloadProjectDir, AssetRoots.AssetsRelative(AssetKind.Blueprint))
                    : System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Blueprints");
                var qrsCatalog = new Hrot.Blueprints.Editor.BlueprintPeerSource(bpDir);
                _blueprintAssetCatalog = qrsCatalog;
                var qrsState   = new Hrot.Blueprints.Editor.EditorState();
                var qrsConsole = _hotReloadSource != null
                    ? (Hrot.Blueprints.Editor.IOutputConsole)new MessageLogOutputConsole(_hotReloadSource)
                    : new Hrot.Blueprints.Editor.SystemConsoleOutputConsole();
                var qrsCompiler = new Hrot.Blueprints.Core.Compiler.BlueprintCompiler();

                // Lightweight FDP coordinator — shares _blueprintRegistry with the kernel.
                var qrsCoordinator = new Fdp.Toolkit.Behavior.AiHotReloadCoordinator(
                    _behaviorRegistry!,
                    _blueprintRegistry,
                    new Fdp.Toolkit.Behavior.AiHotReloadCoordinatorOptions());

                var quickReloadService = new Hrot.Blueprints.Editor.Reload.QuickReloadService(
                    qrsCatalog,
                    qrsState,
                    qrsConsole,
                    qrsCompiler,
                    qrsCoordinator,
                    session: _blueprintDebugSession);
                _blueprintQuickReloadService = quickReloadService;

                // ── CF-7-rev: Auto-instrumentation callback ───────────────────────────────────
                // When a breakpoint or watch is set on an asset that has no DebugMap yet,
                // the session fires this callback to trigger a Debug/Trace QuickReload in-memory.
                // The user does not need to manually click "Compile" — instrumentation is on-demand.
                if (_blueprintDebugSession != null)
                {
                _blueprintDebugSession.SetInstrumentationCallback(async (Guid assetId, Hrot.Blueprints.Core.Compiler.CompilerMode mode) =>
                {
                    var bpLog = _hotReloadSource != null
                        ? (Hrot.Blueprints.Editor.IOutputConsole)new MessageLogOutputConsole(_hotReloadSource)
                        : new Hrot.Blueprints.Editor.SystemConsoleOutputConsole();

                    try
                    {
                        // Find the asset file by ID in the catalog.
                        string? filePath = null;
                        foreach (var entry in _blueprintAssetCatalog.EnumerateAll())
                        {
                            if (entry.AssetId == assetId)
                            {
                                filePath = entry.Path;
                                break;
                            }
                        }

                        if (filePath == null)
                        {
                            bpLog.LogWarning($"Auto-instrumentation: asset {assetId} not found in catalog.");
                            return;
                        }

                        // Load the asset from disk using BlueprintJsonServices.
                        // CRITICAL: BlueprintJsonServices.Deserialize includes JsonStringEnumConverter,
                        // AllowTrailingCommas, and ReadCommentHandling.Skip — plain JsonSerializer
                        // options cannot correctly deserialize .bp.json files.
                        var json = System.IO.File.ReadAllText(filePath);
                        var asset = BlueprintJsonServices.Deserialize(json);
                        if (asset == null)
                        {
                            bpLog.LogWarning($"Auto-instrumentation: BlueprintJsonServices returned null for '{filePath}'.");
                            return;
                        }

                        // Set the compiler mode and trigger QuickReload in-memory.
                        asset.EditorMetadata.CompilerMode = mode;
                        await _blueprintQuickReloadService.TriggerAsync(asset);
                        bpLog.LogInfo($"Auto-instrumentation: {asset.Name} compiled in {mode} mode.");
                    }
                    catch (Exception ex)
                    {
                        bpLog.LogError($"Auto-instrumentation failed for asset {assetId}: {ex.Message}");
                    }
                });
                } // if (_blueprintDebugSession != null)
                // ───────────────────────────────────────────────────────────────────────────

                // ── CF-8: Restore debug session from previous run ─────────────────────────
                // Must happen AFTER the CF-7-rev callback is wired.
                // Instrumentation is deferred to after editor init — QuickReload
                // infrastructure isn't ready during startup (ApplyQuickReload is no-op).
                // Breakpoints/watches are restored as tentative; a timer fires after the
                // editor is fully initialized to trigger CF-7-rev instrumentation.
                RestoreDebugSession();
                Task.Delay(2000).ContinueWith(_ =>
                {
                    _blueprintDebugSession?.RequestInstrumentationForPendingAssets();
                }, TaskScheduler.Default);
                // ───────────────────────────────────────────────────────────────────────────

                string? fullRebuildProjectDir = null;
                var relativeProjectPath = System.IO.Path.Combine(AiBehaviorsProjectPath);
                foreach (var start in new[] { Environment.CurrentDirectory, AppDomain.CurrentDomain.BaseDirectory })
                {
                    var dir = start;
                    while (!string.IsNullOrEmpty(dir))
                    {
                        var candidate = System.IO.Path.Combine(dir, relativeProjectPath);
                        if (System.IO.File.Exists(candidate))
                        {
                            fullRebuildProjectDir = System.IO.Path.GetDirectoryName(candidate);
                            break;
                        }
                        dir = System.IO.Path.GetDirectoryName(dir);
                    }

                    if (fullRebuildProjectDir != null)
                        break;
                }

                string buildTarget = fullRebuildProjectDir != null
                    ? $"\"{System.IO.Path.Combine(fullRebuildProjectDir, "Hrot.AI.Behaviors.csproj")}\""
                    : string.Empty;
                var fullRebuildService = new Hrot.Blueprints.Editor.Reload.FullRebuildService(qrsConsole, buildTarget);

                _blueprintQuickReloadTrigger = editableAsset =>
                {
                    // Resolve the BlueprintAsset from the active document's canvas context.
                    var ctx     = _aiDocumentManager?.Active?.ViewState
                        as Hrot.Editor.AiShared.Windows.AiCanvasContext;
                    var bpAsset = ctx?.AssetRef as Hrot.Blueprints.Core.Assets.BlueprintAsset;
                    if (bpAsset == null) return;

                    // TriggerAsync is synchronous (Task.FromResult) — .GetResult() is safe.
                    var result = quickReloadService.TriggerAsync(bpAsset).GetAwaiter().GetResult();
                    _blueprintCompileStatus = result.Succeeded
                        ? $"Compiled in {result.DurationMs}ms"
                        : $"Compile failed: {result.ErrorMessage}";
                };

                var rebuildRegistrar = new Hrot.Blueprints.Editor.Internal.CaptureWindowRegistrar();
                rebuildRegistrar.RegisterToolbarEntry(
                    "Full Rebuild",
                    () =>
                    {
                        _saveAllCallback?.Invoke();
                        if (_wm != null &&
                            _wm.TryGetWindow("fdp_message_log", out var msgLogWindow) &&
                            msgLogWindow is Fdp.Presentation.Windows.MessageLogWindow typedMsgLogWindow)
                        {
                            _wm.FocusWindow("fdp_message_log");
                            typedMsgLogWindow.SelectTab("fbt_hotreload");
                        }
                        _ = fullRebuildService.TriggerAsync();
                    });
                _blueprintFullRebuildCallback = rebuildRegistrar.GetToolbarCallback("Full Rebuild");
            }

            _regenerationScheduler = new Hrot.Editor.AiShared.Emit.RegenerationScheduler(
                flushAction: asset =>
                {
                    if (asset.Kind == Hrot.Editor.AiShared.AssetKind.Blueprint)
                    {
                        // BF-UX1 FIX A: only auto-recompile when the opt-in flag is set (default false).
                        // The user triggers compilation via the Quick Reload / Full Rebuild toolbar buttons.
                        if (_blueprintAutoReloadOnEdit)
                            _blueprintQuickReloadTrigger?.Invoke(asset);
                        return;
                    }

                    // PU-D11 (PU-402): BTree/HSM assets are now JSON-owned (SampleScout.btree.json /
                    // SampleGuard.hsm.json are the only two editor-owned assets and are now committed).
                    // Writing C# here would clobber the .json with C# (wrong source-of-truth).
                    // Reuse the same saveBTreeDelegate / saveHsmDelegate wired for Save-All (§PU-602):
                    //   mapper.ToDto → JsonServices.Serialize → AtomicFileWriter.Write.
                    // Guards:
                    //   - No-path (empty SourceFilePath) → skip silently; never throw.
                    //   - AssetBaseNameCollisionGuard: checked before write (D5; won't fire post-migration).
                    //   - Blueprint path: UNCHANGED (handled above).
                    // NOTE: end-to-end edit→MSBuild-regen→hot-reload latency is Phase 9 / manual smoke.
                    // This change ensures the flush PERSISTS correctly (valid JSON, not C#).
                    try
                    {
                        var path = asset.SourceFilePath;
                        if (string.IsNullOrEmpty(path))
                            return; // no path → skip silently (awaiting path-at-creation)

                        if (asset.Kind == Hrot.Editor.AiShared.AssetKind.BTree)
                        {
                            var collision = Hrot.AiEditor.Persistence.AssetBaseNameCollisionGuard
                                .CheckCollisionOnDisk(path, System.IO.Directory.EnumerateFiles);
                            if (collision != null)
                                return; // D5 collision: block the write, leave dirty, never throw
                            saveBTreeDelegate(asset, path);
                        }
                        else if (asset.Kind == Hrot.Editor.AiShared.AssetKind.Hsm)
                        {
                            var collision = Hrot.AiEditor.Persistence.AssetBaseNameCollisionGuard
                                .CheckCollisionOnDisk(path, System.IO.Directory.EnumerateFiles);
                            if (collision != null)
                                return; // D5 collision: block the write, leave dirty, never throw
                            saveHsmDelegate(asset, path);
                        }
                        // else: unknown kind — skip silently
                    }
                    catch
                    {
                        // Never throw out of the flush — debounced callbacks must not crash the frame loop.
                    }
                },
                debounceTicks: 500);

            // AIE-026: On reload completed → reconcile open documents from the refreshed catalog
            // so BTree/HSM assets reflect the new assembly's projected layout (positions by VisualId/StableId).
            if (_aiCoordinator != null)
            {
                _aiCoordinator.OnReloadCompleted += _ =>
                {
                    if (_aiCatalogBuilder == null || _aiDocumentManager == null) return;
                    // _aiCatalogBuilder.Catalog.All already contains the freshly projected assets
                    // because the contributor's ContributorChanged fires synchronously inside
                    // AiAssetCatalogBuilder.RefreshFromAssembly (called earlier in OnReloadCompleted).
                    _aiDocumentManager.ReconcileFromCatalog(_aiCatalogBuilder.Catalog.All);
                };
            }
            // ─────────────────────────────────────────────────────────────────────────────────────

            // ── BATCH-24: Main toolbar groups (Perspective §8 + AI-debug §9) ──────────────────
            // All wiring is null-safe so RegisterWindows does not throw on a bare EditorSubsystem.
            if (windowManager.MainToolbar != null)
            {
                var toolbarIconProvider = new SilkIconProvider(windowManager.Atlas);

                // ── A. Perspective group (§8, sortOrder range 20–29) ──────────────────────
                _perspectiveToolbarSection = new PerspectiveToolbarSection(
                    windowManager, toolbarIconProvider, windowManager.MainToolbar, sortOrder: 20);

                // Separator between Perspective and AI-debug.
                windowManager.MainToolbar.RegisterSeparator("ToolbarSep_PerspToAiDebug", sortOrder: 30);

                // ── B. AI-debug group (§9, sortOrder range 40–49) ────────────────────────
                AiDebugCommands.Register(windowManager.ShellCommands.Register, debugRegistry);

                int aiSort = 40;
                ToolbarCommandAdapter.Register(windowManager.MainToolbar, windowManager.ShellCommands,
                    AiDebugCommands.ContinueId, toolbarIconProvider, aiSort++);
                ToolbarCommandAdapter.Register(windowManager.MainToolbar, windowManager.ShellCommands,
                    AiDebugCommands.StepOverId, toolbarIconProvider, aiSort++);
                ToolbarCommandAdapter.Register(windowManager.MainToolbar, windowManager.ShellCommands,
                    AiDebugCommands.StepIntoId, toolbarIconProvider, aiSort++);
                ToolbarCommandAdapter.Register(windowManager.MainToolbar, windowManager.ShellCommands,
                    AiDebugCommands.StepOutId, toolbarIconProvider, aiSort++);
                ToolbarCommandAdapter.Register(windowManager.MainToolbar, windowManager.ShellCommands,
                    AiDebugCommands.PauseId, toolbarIconProvider, aiSort++);
                // Blueprint-only StepBack — registered too; toolbar adapter resolves enabled state live.
                ToolbarCommandAdapter.Register(windowManager.MainToolbar, windowManager.ShellCommands,
                    AiDebugCommands.StepBackId, toolbarIconProvider, aiSort++);
            }
            // ───────────────────────────────────────────────────────────────────────────────────

            if (_editorLogic == null) return;

            // ?? Legacy editor-specific windows ????????????????????????????????
            windowManager.RegisterWindow(new EditorToolbarWindow(_toolbarPanel!, _editorLogic));
            if (_clusterPanel != null && _uiCache != null)
                windowManager.RegisterWindow(new Hrot.Orchestrator.Windows.ClusterControlWindow(_clusterPanel, _uiCache));
            if (_clusterDiagnosticsPanel != null)
                windowManager.RegisterWindow(new Hrot.Orchestrator.Windows.DiagnosticsWindow(_clusterDiagnosticsPanel));

            // ?? Data Breakpoint Manager window (UBP-P10T3) ??? registered unconditionally ???????????
            // Registered before the headless guard so the window is available in headless mode (tests).
            if (_bpManager != null)
            {
                var bpBannerState = new Hrot.Diagnostics.Breakpoints.TemporalStatusBannerState();
                var bpPanel       = new Hrot.Presentation.Panels.Breakpoints.DataBreakpointManagerPanel(
                    _bpManager, bpBannerState);
                var bpWin         = new Hrot.Presentation.Windows.DataBreakpointManagerWindow(
                    "editor_bp_manager", "Editor", bpPanel, EditorWindowColor.TitleBar);
                windowManager.RegisterWindow(bpWin);
            }

            // ?? UBP-P10T5: wire MutationInterceptor early so it is set in headless mode too ??????????
            if (_bpManager != null)
                _fdpEntityInspector.Reflector.MutationInterceptor = _bpManager;

            if (_headless) return;

            // ?? Shared UI panels ??????????????????????????????????????????????
            if (_spawnerPanel     != null && _spawnAdapter     != null)
                windowManager.RegisterWindow(new EditorSpawnerWindow(_spawnerPanel, _spawnAdapter));

            if (_missionPanel     != null && _missionService   != null && _mapPickAdapter != null)
                windowManager.RegisterWindow(new EditorMissionWindow(_missionPanel, _missionService, _mapPickAdapter));

            if (_configPanel      != null && _mapConfigAdapter  != null)
                windowManager.RegisterWindow(new EditorConfigWindow(_configPanel, _mapConfigAdapter));

            if (_sharedOrbatPanel != null && _orbatAdapter     != null)
                windowManager.RegisterWindow(new EditorSharedOrbatWindow(_sharedOrbatPanel, _orbatAdapter, _orbatAdapter));

            if (_previewPanel     != null && _previewController != null)
                windowManager.RegisterWindow(new EditorPreviewWindow(_previewPanel, _previewController));

            if (_zoneEditorPanel  != null && _zoneAdapter       != null)
                windowManager.RegisterWindow(new EditorZoneEditorWindow(_zoneEditorPanel, _zoneAdapter));

            // ?? FDP framework panels (entity inspector + event browser) ???????
            windowManager.RegisterWindow(new FdpEntityInspectorWindow(
                "editor_fdp_inspector", "Editor Entity Inspector", "Editor",
                _fdpEntityInspector,
                () => _fdpRepoAdapter,
                () => _fdpInspectorState,
                EditorWindowColor.TitleBar));

            // Wire component-editor reflector and "Inspect..." context menu.
            MapPickServiceBridge? editorPickBridge = _mapPickAdapter != null && _world != null
                ? new MapPickServiceBridge(_mapPickAdapter, _world)
                : null;
            FdpEntityInspectorHelper.WireInspectorWithInspectContextMenu(
                _fdpEntityInspector,
                windowManager,
                "Editor",
                () => _fdpRepoAdapter,
                editorPickBridge,
                EditorWindowColor.TitleBar);

            // Register the blackboard view provider so the editor projects typed DTO params.
            _fdpEntityInspector.Reflector.AddBufferViewProvider(new Hrot.Presentation.Renderers.BrainBlackboardViewProvider());
            // Register the heavy blackboard view provider for Blackboard1024.
            _fdpEntityInspector.Reflector.AddBufferViewProvider(new Hrot.Presentation.Renderers.Blackboard1024ViewProvider());

            // Inject EditContextFactory so TryOpenEditWindow passes ParamsDtoType/HeavyDtoType to StructEdit.
            var capturedEditorRegistry = _behaviorRegistry;
            _fdpEntityInspector.Reflector.EditContextFactory = (session, e, type) =>
            {
                if (type != typeof(Fdp.Toolkit.Behavior.Components.BrainBlackboard)
                 && type != typeof(Fdp.Toolkit.Behavior.Components.Blackboard1024)) return null;
                if (!session.HasComponent(e, typeof(Fdp.Toolkit.Behavior.Components.BehaviorState))) return null;
                var ds = session.GetComponent(e, typeof(Fdp.Toolkit.Behavior.Components.BehaviorState))
                    as Fdp.Toolkit.Behavior.Components.BehaviorState?;
                if (ds == null) return null;
                if (capturedEditorRegistry?.TryGetDefinition(ds.Value.ActiveBehaviorHash, out var def) != true) return null;
                if (def == null) return null;
                if (type == typeof(Fdp.Toolkit.Behavior.Components.BrainBlackboard))
                {
                    if (def.ParamsDtoType == null) return null;
                    return new StructEdit.Core.EditContext().With("ParamsDtoType", def.ParamsDtoType);
                }
                // Blackboard1024
                if (def.HeavyDtoType == null) return null;
                return new StructEdit.Core.EditContext().With("HeavyDtoType", def.HeavyDtoType);
            };

            windowManager.RegisterWindow(new FdpEventBrowserWindow(
                "editor_fdp_events", "Editor Event Browser", "Editor",
                _fdpEventBrowser,
                EditorWindowColor.TitleBar));

            // ?? Message Log: register hot-reload source ???????????????????????
            // The NLog source and the global window are created by Program.cs.
            // Here we attach the Editor-specific Hot Reload source so its messages
            // appear as a second tab in the shared Message Log window.
            if (_hotReloadSource != null)
                windowManager.MessageLogRegistry?.RegisterSource(_hotReloadSource);
            // Register the AI Behaviors log tab (dedicated tab for structured AI diagnostics).
            windowManager.MessageLogRegistry?.RegisterSource(AiBehaviorLogTarget.SharedInstance);

            if (_kernel != null)
            {
                windowManager.RegisterWindow(new ArchitectureDiagnosticsWindow(
                    "editor_architecture_diagnostics", "Editor Architecture Diagnostics", "Editor",
                    new Fdp.Presentation.Panels.ArchitectureDiagnosticsPanel(
                        new Fdp.ModuleHost.Diagnostics.ArchitectureDiagnosticsService(_kernel)),
                    EditorWindowColor.TitleBar));
            }

            // ?? Time transport controls in status bar ?????????????????????????
            if (_previewController != null && _timeController != null && _world != null
                && windowManager.MainToolbar != null)
            {
                var timeControls = new TimeControlStatusBarSection(_previewController, _timeController, _world);
                windowManager.StatusBar.RegisterSection(
                    id:             "editor_time_controls",
                    sortOrder:      100,
                    renderDelegate: timeControls.Render,
                    perspective:    "Editor");

                // ── BATCH-24: Main toolbar time-control group (§7, sortOrder range 0–9) ──
                var timeTransportFacade = new Hrot.Editor.UI.EditorTimeTransportFacade(
                    _previewController, _timeController, _world);
                var toolbarTimeSection = new Hrot.UI.Common.Panels.MainToolbarTimeControlSection(
                    timeTransportFacade);
                windowManager.MainToolbar.RegisterEntry(
                    "TimeControlGroup", sortOrder: 0, declaredHeight: 64f,
                    toolbarTimeSection.Render);

                // Separator between Time-control and Perspective groups.
                windowManager.MainToolbar.RegisterSeparator(
                    "ToolbarSep_TimeToPersp", sortOrder: 10);
            }

            // ?? Message Log notification icon in status bar ???????????????????
            // The MessageLogWindow is registered globally by Program.cs; we look it
            // up here so the Editor also shows the notification badge.
            if (windowManager.TryGetWindow("fdp_message_log", out var msgLogWin) &&
                msgLogWin is Fdp.Presentation.Windows.MessageLogWindow typedMsgLogWin)
            {
                var msgLogSection = new Fdp.Presentation.WindowManager.MessageLogStatusBarSection(
                    typedMsgLogWin, windowManager);
                windowManager.StatusBar.RegisterSection(
                    "msg_log_notify", sortOrder: 90, msgLogSection.Render);
            }
        }

        /// <inheritdoc/>
        public void Shutdown()
        {
            // ── PU-603: flush pending regeneration + save all dirty open docs ──────────────────────
            // FlushNow() drains any debounced .cs regeneration before we tear down.
            // SaveAllAiDocumentsCommand skips no-path docs (warns via Console); never throws.
            _regenerationScheduler?.FlushNow();
            _saveAllCallback?.Invoke();
            // ─────────────────────────────────────────────────────────────────────────────────────

            // ── CF-8: persist debug session before clearing ─────────────────────────────────────
            // Cancel any pending debounced save and flush immediately.
            _debugSessionSaveCts?.Cancel();
            SaveDebugSession();
            // ─────────────────────────────────────────────────────────────────────────────────────

            // ── UBP-P10T6: clear blueprint debug session ──────────────────────────────────────────
            Hrot.Blueprints.Core.Debug.DebugProbe.Sink = null;
            _blueprintDebugSession = null;
            // ── UBP-P10T11: persist watches for next session (legacy — kept for backward compat) ──
            if (_bpManager != null)
            {
                try
                {
                    var watchesFilePath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "watches.json");
#pragma warning disable CS0618 // Type or member is obsolete — legacy compat
                    _bpManager.SaveWatches(watchesFilePath);
#pragma warning restore CS0618
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[UBP] Failed to save watches.json: {ex.Message}");
                }
            }
            // ─────────────────────────────────────────────────────────────────────────────────────
            _aiCoordinator?.Dispose();
            _aiCoordinator = null;
            _kernel?.Dispose();
            _kernel = null;
            _physicsModule?.Dispose();
            _physicsModule = null;
            _world?.Dispose();
            _world = null;
            _editorLogic = null;
            _editorApp   = null;
            _timeController = null;
            _canvas = null;
            _camera = null;
            _spawnAdapter     = null;
            _missionService   = null;
            _orbatAdapter     = null;
            _mapConfigAdapter = null;
            _mapPickAdapter   = null;
            _zoneAdapter      = null;
            _contextMenuHandler = null;
            _previewController  = null;
            _mapViewConfig      = null;
            _spawnerPanel     = null;
            _missionPanel     = null;
            _configPanel      = null;
            _sharedOrbatPanel = null;
            _previewPanel     = null;
            _zoneEditorPanel  = null;
            _fdpRepoAdapter   = null;
            _selectionState   = null;
            _selectionBridge?.Dispose();
            _selectionBridge  = null;
            // (Phase 5: _interactionTool was here; removed)
            _clusterMaster?.Dispose();
            _clusterMaster  = null;
            _assetInventoryProcessManager = null;
            _assetPrefetchProcessManager = null;
            _diagnosticsDumpProcessManager = null;
            _logMergeWorker?.Dispose();
            _logMergeWorker = null;
            _uiCache?.Dispose();
            _uiCache        = null;
            _clusterPanel = null;
            _clusterDiagnosticsPanel = null;
            _fileDialogService = null;
            _storageGateway = null;
        }

        // ?? Private helpers ???????????????????????????????????????????????????

        // AIE-015: CreateBlueprintWindowRegistrar removed - retired in favor of PerspectiveWorkspaceRegistrar.

        /// <summary>
        /// Drains <see cref="ActivateEditorToolEvent"/> from the bus and routes each
        /// request to the appropriate canvas tool or adapter.
        /// Called once per frame from <see cref="Update"/> (non-headless only).
        /// </summary>
        private Entity FindEntityByNetworkId(long networkId)
        {
            if (_world == null) return default;
            var query = _world.Query().With<NetworkIdentity>().Build();
            foreach (var e in query)
                if (_world.GetComponent<NetworkIdentity>(e).Value == networkId)
                    return e;
            return default;
        }

        private void DrainToolActivationEvents()
        {
            if (_world == null || _canvas == null || _selectionState == null) return;

            foreach (ref readonly var evt in _world.Bus.Read<Hrot.Editor.Events.ActivateEditorToolEvent>())
            {
                switch (evt.Tool)
                {
                    case Hrot.Editor.EditorTool.Select:
                        // (Phase 5: _interactionTool removed; selection via ECS gizmos)
                        break;

                    case Hrot.Editor.EditorTool.Spawn:
                        // Start placement with the last selected type (tracked by the adapter).
                        _spawnAdapter?.StartPlacementModeWithLastType();
                        break;

                    case Hrot.Editor.EditorTool.Edit:
                    {
                        // Inject VertexEditGizmo directly via the gizmo system (toggle if already active).
                        var entity = _selectionState.PrimarySelected;
                        if (entity is { } e && e != Entity.Null && _world.HasManagedComponent<Hrot.IG.Components.EditablePolyline>(e))
                        {
                            if (_editorDataDrivenGizmoSystem!.HasInjectedGizmo(e))
                            {
                                _editorDataDrivenGizmoSystem!.DeactivateGizmo(e);
                            }
                            else
                            {
                                long netId = _world.HasComponent<Fdp.Toolkit.Replication.Components.NetworkIdentity>(e)
                                    ? _world.GetComponentRO<Fdp.Toolkit.Replication.Components.NetworkIdentity>(e).Value
                                    : 0L;
                                var gizmo = new Hrot.ScenarioEditor.Gizmos.VertexEditGizmo(
                                    _world!, e, netId,
                                    onRemove: () => _editorDataDrivenGizmoSystem!.DeactivateGizmo(e));
                                _editorDataDrivenGizmoSystem!.ActivateGizmo(e, gizmo);
                            }
                        }
                        break;
                    }

                    case Hrot.Editor.EditorTool.Route:
                    {
                        // Inject RouteWaypointGizmo directly via the gizmo system (toggle if already active).
                        var entity = _selectionState.PrimarySelected;
                        if (entity is { } e && e != Entity.Null && _world.HasManagedComponent<Hrot.Map.Common.Components.RoutePlan>(e))
                        {
                            if (_editorDataDrivenGizmoSystem!.HasInjectedGizmo(e))
                            {
                                _editorDataDrivenGizmoSystem!.DeactivateGizmo(e);
                            }
                            else
                            {
                                long netId = _world.HasComponent<Fdp.Toolkit.Replication.Components.NetworkIdentity>(e)
                                    ? _world.GetComponentRO<Fdp.Toolkit.Replication.Components.NetworkIdentity>(e).Value
                                    : 0L;
                                var gizmo = new Hrot.ScenarioEditor.Gizmos.RouteWaypointGizmo(
                                    _world!, e, netId,
                                    onRemove: () => _editorDataDrivenGizmoSystem!.DeactivateGizmo(e));
                                _editorDataDrivenGizmoSystem!.ActivateGizmo(e, gizmo);
                            }
                        }
                        break;
                    }

                    case Hrot.Editor.EditorTool.Measure:
                        if (_globalGizmoManager != null)
                        {
                            var id = GlobalGizmoManager.NewId();
                            var gizmo = new Hrot.ScenarioEditor.Gizmos.MeasureGizmo(onRemove: () => _globalGizmoManager?.Unregister(id));
                            _globalGizmoManager.Register(id, gizmo);
                        }
                        break;

                    case Hrot.Editor.EditorTool.Rotate:
                    {
                        // Inject EntityRotatorGizmo directly via the gizmo system.
                        var entity = _selectionState.PrimarySelected;
                        if (entity is { } e && e != Entity.Null && _world.HasComponent<Fdp.Core.SimTransform>(e))
                        {
                            _editorDataDrivenGizmoSystem!.DeactivateGizmo(e);
                            var gizmo = new Hrot.SimHost.Gizmos.EntityRotatorGizmo(
                                _world!, e,
                                onRemove: () => _editorDataDrivenGizmoSystem!.DeactivateGizmo(e));
                            _editorDataDrivenGizmoSystem!.ActivateGizmo(e, gizmo);
                        }
                        break;
                    }
                }
            }

            // ?? Drain camera-center requests ??????????????????????????????????
            foreach (ref readonly var cmd in _world.Bus.Read<Hrot.Editor.Commands.CenterOnEntityCommand>())
            {
                if (_camera == null) continue;
                var q = _world.Query()
                    .With<Fdp.Toolkit.Replication.Components.NetworkIdentity>()
                    .With<Fdp.Core.SimTransform>()
                    .Build();
                foreach (var e in q)
                {
                    if (_world.GetComponent<Fdp.Toolkit.Replication.Components.NetworkIdentity>(e).Value == cmd.NetworkId)
                    {
                        ref readonly var tf = ref _world.GetComponentRO<Fdp.Core.SimTransform>(e);
                        _camera.FocusOn(new System.Numerics.Vector2(tf.Position.X, tf.Position.Y));
                        break;
                    }
                }
            }

            // ?? Drain rename-dialog requests ??????????????????????????????????
            foreach (ref readonly var cmd in _world.Bus.Read<Hrot.Common.Events.OpenRenameDialogCommand>())
            {
                _renameTargetNetworkId    = cmd.NetworkId;
                _openRenameModalThisFrame = true;
                _renameBuffer             = string.Empty;

                // Pre-fill buffer with the entity's current name.
                var q = _world.Query()
                    .With<Fdp.Toolkit.Replication.Components.NetworkIdentity>()
                    .With<EntityInfo>()
                    .Build();
                foreach (var e in q)
                {
                    if (_world.GetComponent<Fdp.Toolkit.Replication.Components.NetworkIdentity>(e).Value == cmd.NetworkId)
                    {
                        _renameBuffer = _world.GetComponent<EntityInfo>(e).Name.ToString();
                        break;
                    }
                }
            }
        }

        // ── CF-8: Debug session persistence helpers ──────────────────────────────

        /// <summary>
        /// Resolves the repo root directory by walking up from <see cref="AppDomain.CurrentDomain.BaseDirectory"/>
        /// looking for IOS-IG-SimHost.sln.
        /// </summary>
        private static string? ResolveRepoRoot()
        {
            var dir = AppDomain.CurrentDomain.BaseDirectory;
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir, "IOS-IG-SimHost.sln")))
                    return dir;
                dir = Path.GetDirectoryName(dir);
            }
            return null;
        }

        private string? GetDebugSessionPath()
        {
            var root = ResolveRepoRoot();
            return root != null ? Path.Combine(root, ".debug", "bpsession.json") : null;
        }

        /// <summary>
        /// Saves the full debug session (node BPs, data BPs, watches) to the session file.
        /// </summary>
        private void SaveDebugSession()
        {
            var path = GetDebugSessionPath();
            if (path == null) return;

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);

                var nodeBps = _blueprintDebugSession?.GetBreakpoints();
                var watches = _blueprintDebugSession?.GetWatches();
                var dbmBps  = _bpManager?.AllBreakpoints;

                DebugSessionPersistence.Save(
                    nodeBps ?? Array.Empty<Hrot.Blueprints.Core.Debug.Breakpoint>(),
                    watches ?? Array.Empty<Hrot.Blueprints.Core.Debug.Watch>(),
                    dbmBps  ?? Array.Empty<Hrot.Diagnostics.Breakpoints.Breakpoint>(),
                    path);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CF8] Failed to save debug session: {ex.Message}");
            }
        }

        /// <summary>
        /// Debounced save trigger (500 ms). Call on every breakpoint/watch change;
        /// repeated calls reset the timer.
        /// </summary>
        private void ScheduleDebugSessionSave()
        {
            _debugSessionSaveCts?.Cancel();
            _debugSessionSaveCts = new CancellationTokenSource();
            var token = _debugSessionSaveCts.Token;
            Task.Delay(500, token).ContinueWith(_ =>
            {
                if (!token.IsCancellationRequested)
                    SaveDebugSession();
            }, TaskScheduler.Default);
        }

        /// <summary>
        /// Restores the debug session from the previous run.
        /// Must be called AFTER the CF-7-rev instrumentation callback is wired.
        /// </summary>
        private void RestoreDebugSession()
        {
            var path = GetDebugSessionPath();
            if (path == null) return;

            Hrot.Diagnostics.Breakpoints.DebugSessionFile? file;
            try
            {
                file = DebugSessionPersistence.TryLoad(path);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CF8] Failed to load debug session: {ex.Message}");
                return;
            }

            if (file == null)
                return;

            // Restore data breakpoints into the DBM (before node breakpoints,
            // since node breakpoints may trigger CF-7-rev instrumentation).
            if (file.DataBreakpoints.Count > 0 && _bpManager != null)
            {
                foreach (var entry in file.DataBreakpoints)
                {
                    if (entry.Condition == null) continue;

                    try
                    {
                        var mgrBp = new Hrot.Diagnostics.Breakpoints.Breakpoint
                        {
                            Id              = Hrot.Diagnostics.Breakpoints.BreakpointId.Invalid,
                            Condition       = entry.Condition,
                            DisplayName     = entry.DisplayName,
                            SourceElementId = entry.SourceElementId,
                            Enabled         = entry.Enabled,
                            IsWatch         = entry.IsWatch,
                        };
                        _bpManager.Add(mgrBp);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[CF8] Failed to restore data breakpoint '{entry.DisplayName}': {ex.Message}");
                    }
                }
            }

            // Restore node breakpoints (triggers CF-7-rev instrumentation).
            if (file.NodeBreakpoints.Count > 0 && _blueprintDebugSession != null)
            {
                _blueprintDebugSession.RestoreNodeBreakpoints(file.NodeBreakpoints);
            }

            // Restore watches (triggers CF-7-rev Trace instrumentation).
            if (file.Watches.Count > 0 && _blueprintDebugSession != null)
            {
                _blueprintDebugSession.RestoreWatches(file.Watches);
            }
        }

        // IEcsModule wrapper for Simulation-phase systems in the offline Editor.
        // The kernel forbids registering SystemPhase.Simulation systems as global systems;
        // they must be routed through a module.

        private sealed class DelegateDisposable : IDisposable
        {
            private Action? _action;
            public DelegateDisposable(Action action) => _action = action;
            public void Dispose() { _action?.Invoke(); _action = null; }
        }

        private sealed class EditorSimulationModule : IEcsModule        {
            private readonly TogglableSimulationGroup _simulationGroup;

            public string Name => "EditorSimulation";
            public ExecutionPolicy Policy => ExecutionPolicy.Synchronous();

            public EditorSimulationModule(TogglableSimulationGroup simulationGroup)
                => _simulationGroup = simulationGroup;

            public void RegisterSystems(ISystemRegistry registry)
                => registry.RegisterSystem(_simulationGroup);

            public void Tick(ISimulationView view, float deltaTime) { }
        }

        private sealed class MessageLogOutputConsole : Hrot.Blueprints.Editor.IOutputConsole
        {
            private readonly HotReloadMessageLogSource _source;

            public MessageLogOutputConsole(HotReloadMessageLogSource source)
            {
                _source = source;
            }

            public void LogInfo(string message)
            {
                Console.WriteLine($"[BP] INFO: {message}");
                _source.PushLine(message);
            }

            public void LogWarning(string message)
            {
                Console.WriteLine($"[BP] WARN: {message}");
                _source.PushLine($"warning: {message}");
            }

            public void LogError(string message)
            {
                Console.WriteLine($"[BP] ERR:  {message}");
                _source.PushLine($"error: {message}");
            }

            public void LogDebug(string message)
            {
                Console.WriteLine($"[BP] DBG:  {message}");
                _source.PushLine(message);
            }

            public void LogDiagnostic(Microsoft.CodeAnalysis.Diagnostic diagnostic)
            {
                Console.WriteLine($"[BP] {diagnostic.Severity}: {diagnostic.GetMessage()}");
                _source.PushLine($"{diagnostic.Severity}: {diagnostic.GetMessage()}");
            }
        }
        // BATCH-21: Lightweight IEditableAsset for scenario Save-As dialog seeding.
        private sealed class ScenarioSaveAsAsset : Hrot.Editor.AiShared.IEditableAsset
        {
            public ScenarioSaveAsAsset(string name) { Name = name; }
            public Guid AssetId { get; } = Guid.NewGuid();
            public string Name { get; }
            public Hrot.Editor.AiShared.AssetKind Kind => Hrot.Editor.AiShared.AssetKind.Scenario;
            public string SourceFilePath => "";
            public bool IsDirty => false;
            public bool IsEditorOwned => true;
#pragma warning disable CS0067
            public event Action? Changed;
#pragma warning restore CS0067
        }
    }
}






