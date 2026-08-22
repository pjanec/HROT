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
using Hrot.BTree.Editor.Model;
using Hrot.BTree.Editor.Persistence;
using Hrot.AiEditor.Persistence.BTree;
using Hrot.AiEditor.Persistence.Emit;
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
using NodeEditor.Core.Action;
using NodeEditor.Primitives;
using Hrot.Hsm.Editor.Catalog;
using Hrot.Hsm.Editor.Host;
using Hrot.Hsm.Editor.Model;
using Hrot.Hsm.Editor.Persistence;
using Hrot.AiEditor.Persistence.Hsm;
// AIE-030/031/032: BTree/HSM debug session infrastructure
using Hrot.BTree.Editor.Inspector;
using Hrot.Hsm.Editor.Inspector;
// AIE-050/051/052: cross-asset services
using Hrot.BTree.Editor.Blackboard;
using Hrot.BTree.Editor.Comparison;
using Hrot.Hsm.Editor.Blackboard;
using Hrot.Hsm.Editor.Comparison;
using Hrot.Blueprints.Editor.Comparison;

using Hrot.Blueprints.Core.Debug;

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
    public sealed class EditorSubsystem : ISubsystem, IMapCameraProvider, IWindowRegistrar, Hrot.Common.Diagnostics.Gizmos.IGizmoControllable, Fdp.Toolkit.Runner.IAppExitGuard
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
        private Fdp.Toolkit.Time.ITimeCommands? _timeCommands;
        /// <summary>⭐ BATCH 84 / R-66 — the frozen-time signal for the variable surfaces (ruling 15).</summary>
        private MasterSyncTimeControllerAdapter? _bpTimeAdapter;
        /// <summary>⭐ BATCH 84 — the AI debug-session registry built in RegisterWindows; see the test accessor.</summary>
        private Hrot.Editor.AiShared.Debug.DebugSessionRegistry? _aiDebugRegistry;
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
        // ⭐⭐⭐ Batch 95 (95b) — THE SELECTED ENTITY, ONCE, for every store this subsystem holds.
        // 🔴🔴 Measured: this editor builds FOUR EditorSelectionStores and calls
        //    CallbackSelectionBridge.Connect exactly ONCE, on _aiEditorSelectionStore below. ⇒
        //    SelectedEntity was null on all three PERSPECTIVE stores, always ⇒ every live-value
        //    provider returned null on its second line ⇒ every Details/Watch row on every host read
        //    "(pending)" for ever. ⚠ The comment beside the two AI providers further down asserted the
        //    opposite ("Both selection stores share the same entity selection (global)") — it was
        //    false, and that is why the gap survived four batches of fixes on the same two paths.
        // 📄 AI_Editor_Shared_Infrastructure.md:450 — "SelectedEntity stays global because entities
        //    exist independently of which asset is being edited"; :45 — the store is "the single
        //    selection bus all three editors subscribe to". ⇒ this restores the design, it does not
        //    invent a policy.
        // ⛔ NOT three more Connect() calls: that is the shape PerspectiveWorkspaceServices exists to
        //    abolish. ⭐ One fact, read by every store; the bridge still connects exactly one.
        private readonly Hrot.Editor.AiShared.Selection.SharedEntitySelection _sharedEntitySelection = new();
        private readonly Hrot.Editor.AiShared.Selection.EditorSelectionStore _aiEditorSelectionStore;
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
        // BUG-A6: scan roots and JSON contributors captured from Initialize so RegisterWindows
        // can target new-asset writes at the source dir and refresh the right contributor.
        private string?                             _bpRootDir;
        private string?                             _btreeJsonRootDir;
        private string?                             _hsmJsonRootDir;
        private BTreeJsonAssetContributor?          _btreeJsonContrib;
        private HsmJsonAssetContributor?            _hsmJsonContrib;
        // MTB-P5-T2: Scenario catalog contributor (non-file-backed; refreshed on scenario list change).
        private Hrot.Editor.Catalog.ScenarioCatalogContributor? _scenarioContributor;
        // AIE-026: save → emit → reload scheduler (ticked in Update)
        private Hrot.Editor.AiShared.Emit.RegenerationScheduler? _regenerationScheduler;
        // AIE-026 (Blueprint): Quick Reload trigger — null until Phase 4 wires QuickReloadService.
        // Receives IEditableAsset (a BlueprintFileAsset in Phase 2; a loaded BlueprintAsset in Phase 4).
        private Action<Hrot.Editor.AiShared.IEditableAsset>? _blueprintQuickReloadTrigger;
        // QR-03: BTree quick-reload trigger — wired in Phase 4 alongside _blueprintQuickReloadTrigger.
        // Invokes ToDto → EmitTopologyCore + EmitBridge → TriggerFromSourcesAsync (no IEditableAsset param).
        private Action? _btreeQuickReloadTrigger;
        // QR-04: HSM quick-reload trigger — symmetric to QR-03 via HsmEmitCore / HsmBridgeEmitCore.
        private Action? _hsmQuickReloadTrigger;
        // CF-7-rev: QuickReloadService and asset catalog stored for auto-instrumentation callback.
        private QuickReloadService? _blueprintQuickReloadService;
        private Hrot.Blueprints.Editor.BlueprintPeerSource? _blueprintAssetCatalog;
        // BF-UX1 FIX A: gate auto-reload on edit; defaults false so node moves/edits do NOT trigger
        // a Roslyn compile. The user compiles via the toolbar Quick Reload / Full Rebuild buttons.
        // TODO: wire from BlueprintEditorPreferences.AutoReloadOnSave when the prefs instance is
        //       reachable here (the prefs window lives in a different composition scope).
        private bool _blueprintAutoReloadOnEdit = false;
        // ⭐⭐ Batch 95 (95b) — all three join _sharedEntitySelection, assigned in the constructor.
        //    ⛔ Not a field initializer: C# forbids one instance field initializer reading another,
        //    and the whole point is that these four stores read ONE cell.
        private readonly EditorSelectionStore  _btreeSelectionStore;
        private readonly EditorSelectionStore  _hsmSelectionStore;
        private readonly EditorSelectionStore  _blueprintSelectionStore;
        private PerspectiveWorkspaceRegistrar? _btreeRegistrar;
        private PerspectiveWorkspaceRegistrar? _hsmRegistrar;
        private PerspectiveWorkspaceRegistrar? _blueprintRegistrar;
        private AssetBrowserDockedWindow?       _aiAssetBrowser;
        // AIE-047: My Blueprint window (hosts NodeEdit MyBlueprintPanel).
        private Hrot.Blueprints.Editor.Windows.BlueprintMyBlueprintWindow? _blueprintMyBlueprintWindow;
        // AIE-048: Blueprint Details + Variables windows.
        private Hrot.Blueprints.Editor.Windows.BlueprintDetailsWindow? _blueprintDetailsWindow;
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

        // App-exit "unsaved changes" prompt: state machine (headless-testable) + one-shot popup latch.
        // When the window [X] is clicked with dirty documents open, IAppExitGuard.OnExitRequested opens
        // the modal instead of exiting; the loop keeps running until the user resolves it.
        private Hrot.Editor.AiShared.Documents.AppExitPromptController? _exitPrompt;
        private bool _exitPopupOpened;

        // BATCH-06: perspective-level shell hotkey dispatcher (Ctrl+S/Ctrl+Shift+S fix, §20).
        private ImGuiInputSource? _shellInputSource;
        private Hrot.Editor.AiShared.Windows.EditorHotkeyDispatcher? _shellHotkeyDispatcher;

        // BATCH-20 (DEC-9): per-kind INewAssetService registry for SaveAsDialog.
        // Initialized before ShellSaveCommands.Register so the requestSaveAs seam
        // can create a fully-seeded dialog.
        private Dictionary<Hrot.Editor.AiShared.AssetKind, Hrot.Editor.AiShared.Recipes.INewAssetService>? _newAssetServices;

        // BATCH-29 (MTB-P8-T3): Dedicated shell picker registry for global Open-Asset picker.
        // Separate from adapterBundle.PickerRegistry (which is DrawFrame()-ed by canvas windows)
        // to avoid double-DrawFrame on the same registry instance.
        private NodeEditor.UI.Picker.PickerRegistry? _shellPickers;

        // BATCH-42 (MTB2-T8b): Save-As browser dialog host for the New-asset flow.
        private NodeEditor.UI.Dialogs.SaveAsBrowserDialog? _saveAsBrowser;

        // BATCH-42 (MTB2-T8b): icon provider captured from adapter bundle for per-frame
        // SaveAsBrowserDialog draw.
        private NodeEditor.Core.Interfaces.IIconProvider? _iconProvider;

        // BATCH-26: Asset-pick action router — routes file kinds → AiDocumentManager.Open,
        // Scenario → IEditorLogic.LoadScenarioByName.
        private Hrot.Editor.AssetPickActionRouter? _assetPickRouter;

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

        /// <summary>
        /// ⭐⭐ Internal test hook: the AI debug-session registry, so a rail can reproduce <c>R-66</c>'s
        /// exact state — <b>a document open while the sim is DOWN</b> — and assert what the variable
        /// surfaces read then. ⛔ Null until <see cref="RegisterWindows"/> has run.
        /// </summary>
        internal Hrot.Editor.AiShared.Debug.DebugSessionRegistry? AiDebugRegistry => _aiDebugRegistry;

        /// <summary>
        /// ⭐⭐⭐ Batch 95 (<c>95b</c>) — internal test hook: <b>the ONE store the selection bridge
        /// writes to.</b>
        ///
        /// <para>⭐ <c>CallbackSelectionBridge.Connect</c>'s entire action is
        /// <c>store.SelectedEntity = entity</c> on this store, so writing here IS how production
        /// selects an entity. ⛔ A rail that instead wrote to a PERSPECTIVE store would assert the
        /// defect away rather than expose it — the whole finding is that the perspective stores are
        /// not the ones production writes.</para>
        /// </summary>
        internal Hrot.Editor.AiShared.Selection.EditorSelectionStore AiEditorSelectionStore
            => _aiEditorSelectionStore;

        /// <summary>
        /// ⭐⭐⭐ Batch 95 (<c>95a</c>) — internal test hook: the registrar a perspective actually got.
        ///
        /// <para>📌 <c>R-67</c>, verbatim: <i>"a rail that builds its own composition root cannot see a
        /// composition-root defect."</i> ⛔ The edit-gesture binder is reachable only through its
        /// registrar, so a rail that must assert <b>a session opened</b> has to be given the REAL one.
        /// ⭐ Same shape as <see cref="AiDebugRegistry"/> above, and null until
        /// <c>RegisterWindows</c> has run.</para>
        /// </summary>
        internal Hrot.Editor.AiShared.Windows.PerspectiveWorkspaceRegistrar? RegistrarFor(string perspective)
            => perspective?.ToLowerInvariant() switch
            {
                "btree"     => _btreeRegistrar,
                "hsm"       => _hsmRegistrar,
                "blueprint" => _blueprintRegistrar,
                _           => null,
            };

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

        /// <summary>
        /// ⭐⭐⭐ <b>Is the simulation clock HALTED this frame?</b> — <c>DeltaTime == 0</c> on the
        /// <c>GlobalTime</c> singleton the kernel pushes into the live world every frame.
        ///
        /// <para>⛔⛔ <b>This is the ONE reading of the clock that is true.</b> 📐 <c>M-42</c>, measured
        /// <c>2026-08-21</c>: <c>GlobalTime.IsPaused</c> is <c>TimeScale == 0</c> and a pause never sets
        /// <c>TimeScale</c> to <c>0</c> — it switches the master to <c>MasterMode.Stepping</c>, whose
        /// <c>UpdateStepping</c> returns <c>BuildGlobalTime(dt: _pendingStepDelta, …)</c> with
        /// <c>TimeScale</c> untouched. ⇒ <b>the convenience flag is FALSE while paused</b>, and it has
        /// zero production readers, which is the only reason that has never bitten.</para>
        ///
        /// <para>⚠ <b>And it must be read from the WORLD, not the controller.</b>
        /// <c>MasterSyncController.GetCurrentState()</c> is <c>BuildGlobalTime(0.0f, 0.0f)</c> — it
        /// hard-codes the delta to zero, so a delta-based predicate read through it answers
        /// <i>"halted"</i> forever.</para>
        ///
        /// <para>⭐ <c>true</c> when there is no world or no singleton yet: nothing is advancing before
        /// the first tick, and a surface with no way to observe the clock must not claim the sim is
        /// running.</para>
        /// </summary>
        /// <summary>
        /// T5, first site. This was a hand-rolled copy of the guarded singleton read that
        /// <c>SimClock</c> now owns — null world, missing singleton and the DeltaTime predicate, all
        /// three identical. Routed rather than kept: the point of `T1` is that "is the simulation
        /// running" has ONE named answer, and a second copy of the predicate is how the codebase
        /// arrived at a dozen of them.
        /// </summary>
        private bool ClockIsHalted() => Fdp.Toolkit.Time.SimClock.Of(_world).IsHalted;

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
            // ⭐⭐⭐ Batch 95 (95b) — EVERY selection store this editor holds reads ONE entity cell.
            // 🔴🔴 The bridge connects exactly one store (_aiEditorSelectionStore), and the three
            //    PERSPECTIVE stores were connected to nothing ⇒ their SelectedEntity was null for
            //    ever ⇒ every live-value provider bailed on its second line and every Details/Watch
            //    row on every host rendered "(pending)".
            // ⛔ NOT three more Connect() calls — that is the shape PerspectiveWorkspaceServices
            //    exists to abolish. ⭐ The selected entity is ONE FACT ABOUT THE WORLD, held once.
            // 📄 AI_Editor_Shared_Infrastructure.md:450 already specified it global; this restores it.
            // ⚠ In the constructor and not a helper: the fields are readonly, which is what stops a
            //   later edit from quietly giving one store a cell of its own.
            _aiEditorSelectionStore  = new Hrot.Editor.AiShared.Selection.EditorSelectionStore(_sharedEntitySelection);
            _btreeSelectionStore     = new EditorSelectionStore(_sharedEntitySelection);
            _hsmSelectionStore       = new EditorSelectionStore(_sharedEntitySelection);
            _blueprintSelectionStore = new EditorSelectionStore(_sharedEntitySelection);
        }


        // ctor for ClusterRunner
        public EditorSubsystem( INetworkFactory _ ) : this()
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
            // T3: the controller lives on THE BUS THE INTENTS LIVE ON — _orchestrationBus, which
            // carries OrchestrationEventRegistry (registered at the top of this method). That is the
            // rule every other node already follows: the Orchestrator builds its master on the same
            // _bus it registers (OrchestratorSubsystem:118/:146), and CGF/SimHost/IG/ExCon put their
            // controller and their egress translator on one bus each.
            //
            // The editor was the only place those were two different objects: the registry on
            // _orchestrationBus, the master on _world.Bus. Intents published by the toolbar, the
            // debugger or a BTree/HSM path therefore landed on a bus the master never read, and
            // ReadManaged on the other bus returns empty — no error, nothing happens. Putting them
            // on one bus is what unblocks paths B/C/D publishing intents like everyone else, and it
            // is the same code the CGF node will need for cluster-side debugging.
            var timeConfig = new TimeControllerConfig { Role = TimeRole.Standalone };
            _timeController = (MasterSyncController)TimeControllerFactory.Create(_orchestrationBus, timeConfig);
            _kernel.SetTimeController(_timeController);
            // Start in Deterministic mode so authoring starts paused (dt == 0 every frame).
            _timeController.SwitchToDeterministic(new System.Collections.Generic.HashSet<int>());

            // ?? 3. Shared services ????????????????????????????????????????????
            var geoTransform     = HrotEnvironment.CreateGeoTransform();
            _geoTransform = geoTransform;
            var entityMap        = new NetworkEntityMap();
            _entityMap = entityMap;
            _world.SetSingletonManaged<NetworkEntityMap>(entityMap);
            // Behavior resolvers (Phase 2b) read the geographic transform from this world singleton;
            // publish it so the editor's behavior preview/activation resolves geo-aware params correctly.
            _world.SetSingletonManaged<IGeographicTransform>(geoTransform);
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

            // Feature A (BATCH-10): expose the behavior registry to the shared stateful
            // working-state projection helper so BlueprintBlackboard* renderers can decode
            // and display typed WorkingState structs in the Entity Inspector.
            Hrot.Presentation.Renderers.StatefulWorkingStateProjection.BehaviorRegistryAccessor = behaviorRegistry;

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
            // T4d: the SUBSYSTEM-SPECIFIC coordinator, not the base class. The base's
            // RequestPause/RequestContinue/RequestStepOneTick are virtual no-ops, so constructing it
            // here meant a BTree or HSM tracer asking the simulation to stop did nothing at all --
            // silently. It publishes intents on _orchestrationBus, the bus the master drains (T3),
            // so path D now has the same shape as the cluster path.
            _timeCommands        = new Fdp.Toolkit.Time.IntentTimeCommands(_orchestrationBus!);
            _aiTracerCoordinator = new Hrot.Editor.Debug.EditorAiTracerCoordinator(_timeCommands);
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

            var aiRootDir  = ResolveAiBehaviorsDir(AiBehaviorsProjectPath);
            // BUG-A6: store scan roots and JSON contributors as fields so RegisterWindows
            // can target new-asset writes at the source dir and refresh the right contributor.
            _bpRootDir       = aiRootDir != null
                ? System.IO.Path.Combine(aiRootDir, AssetRoots.AssetsRelative(AssetKind.Blueprint))
                : System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Blueprints");
            _btreeJsonRootDir = aiRootDir != null
                ? System.IO.Path.Combine(aiRootDir, AssetRoots.AssetsRelative(AssetKind.BTree))
                : null;
            _hsmJsonRootDir  = aiRootDir != null
                ? System.IO.Path.Combine(aiRootDir, AssetRoots.AssetsRelative(AssetKind.Hsm))
                : null;
            var bpRootDir        = _bpRootDir;
            var bpContrib        = new BlueprintAssetContributor(bpRootDir);
            _btreeJsonContrib    = new BTreeJsonAssetContributor(_btreeDebugSession);
            _hsmJsonContrib      = new HsmJsonAssetContributor();
            var btreeJsonContrib = _btreeJsonContrib;
            var hsmJsonContrib   = _hsmJsonContrib;
            var btreeJsonRootDir = _btreeJsonRootDir;
            var hsmJsonRootDir   = _hsmJsonRootDir;
            if (aiRootDir == null)
            {
                Console.WriteLine("[EditorSubsystem] WARNING: Hrot.AI.Behaviors project dir not found " +
                    $"(searched up from CWD + BaseDirectory for {System.IO.Path.Combine(AiBehaviorsProjectPath)}); " +
                    "editor-owned BTree/HSM JSON assets will not load with layout.");
            }
            else
            {
                if (System.IO.Directory.Exists(btreeJsonRootDir!))
                    btreeJsonContrib.Refresh(rootDirectory: btreeJsonRootDir);
                else
                    Console.WriteLine($"[EditorSubsystem] WARNING: BTree JSON root not found: {btreeJsonRootDir}");

                if (System.IO.Directory.Exists(hsmJsonRootDir!))
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
            if (!_world.HasSingletonManaged<ITkbDatabase>()) _world.SetSingletonManaged<ITkbDatabase>(tkbDb);
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

            // FC-1·G2: splice bpTick BEFORE the action dispatchers (its [UpdateBefore] targets)
            // instead of appending it -- module-group order is array position, so an appended tick
            // ran AFTER the dispatchers and intent writes were only dispatched next tick, silently
            // violating the Q#16-B same-tick contract. See BlueprintRuntimeWiring.SpliceIntoSimulation.
            var toggleSim = new TogglableSimulationGroup(
                "EditorSim",
                Hrot.Blueprints.Editor.Runtime.BlueprintRuntimeWiring.SpliceIntoSimulation(
                    cgfLogicPackInst.SimulationSystems.Concat(simHostCorePack.SimulationSystems), bpTick).ToArray());

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

            // ⭐ BATCH 84 / R-66: kept as a FIELD so RunStateSource's "is time frozen?" signal reads
            //   through this adapter -- the same one the breakpoint manager drives time with. ⛔ A
            //   second reading of _timeController.GetMode() here would be a duplicate rule.
            var bpTimeAdapter           = _bpTimeAdapter = new MasterSyncTimeControllerAdapter(_timeController!);
            var bpEditSvc               = new ComponentEditServiceBuilder().Build();
            // _blueprintRegistry is required for BlueprintVariablePredicateDto -- the predicate that
            // "Add Conditional Data Breakpoint..." synthesizes. Omitting it makes
            // CompileBlueprintVariablePredicate return a constant-false delegate, so blueprint
            // conditional breakpoints silently never fire (BP-29).
            var bpPredicateCompiler     = new PredicateCompiler(bpEditSvc, _behaviorRegistry, _blueprintRegistry);
            var bpEventScannerCompiler  = new EventScannerCompiler(bpEditSvc);
            _bpSnapshotProvider         = new DebugSnapshotProvider(_bpPreTickSnapshot);
            _bpManager                  = new DataBreakpointManager(
                _world!, _bpPreTickSnapshot, _bpSnapshotProvider,
                bpTimeAdapter, bpPredicateCompiler, bpEventScannerCompiler);
            _bpSystem                   = new DataBreakpointSystem(_bpManager, _world!.Bus);

            _kernel.RegisterGlobalSystem(_bpSnapshotProvider);
            _kernel.RegisterGlobalSystem(_bpSystem);

            // ⭐⭐⭐ THE STAGED-WRITE DRAIN WIRE. 📄 DESIGN_Staged_Live_Write.md §8.
            //   The PreFrame drain (time lane's W1/W2) is fed the breakpoint manager AS IStagedWrites
            //   (its W4 role) and the kernel's publishing signal (closes AS-10's replay-prep residual).
            //   ⇒ a staged live edit is PULLED into the repo at the next advancing tick — R-126.
            _kernel.RegisterGlobalSystem(new Fdp.ModuleHost.Time.ResumeAndDrainSystem(
                _bpManager, () => _kernel.IsPublishingGlobalTime));

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
            // BP-08 / BP-66: the CallPeerBlueprint drawer's peer list, scanned from the SAME root
            // (_bpRootDir, resolved at §715 via AssetRoots.AssetsRelative) that every other blueprint
            // consumer uses. NOT "{BaseDirectory}/blueprints" — that directory does not exist; see
            // BP-66, where the long-standing pin-projection catalog had the same wrong path and so
            // never resolved a peer either. Leaving the provider null would ship a picker that always
            // reports "no peer Blueprints discovered" — the inert-default failure this programme
            // keeps finding (BP-29, BP-61).
            var blueprintPeerProvider = new Hrot.Blueprints.Editor.NodeDrawers.BlueprintPeerSourceProvider(
                new Hrot.Blueprints.Editor.BlueprintPeerSource(
                    _bpRootDir ?? Hrot.Editor.AiShared.AssetRoots.AssetsFor(
                        Hrot.Editor.AiShared.AssetKind.Blueprint)));

            _blueprintNodeDrawers = Hrot.Blueprints.Editor.BlueprintEditorBootstrap.CreateNodeDrawerRegistry(
                channelCatalog, engineEventCatalog, blueprintEditService, bpPredicateCompiler, eqsTemplates,
                peerProvider: blueprintPeerProvider);
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
            Hrot.SimHost.Gizmos.GizmoRegistrar.RegisterAll(
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
                OrchestrationConstants.ResolveStagingRoot(),
                EditorNodeId);
            _assetPrefetchProcessManager = new AssetPrefetchProcessManager(
                _orchestrationBus!,
                _storageGateway,
                ClusterConfiguration.Default.NasBasePath,
                OrchestrationConstants.ResolveStagingRoot());
            _uiCache = new ClusterUiCache(_orchestrationBus!, _timeController);
            _clusterPanel = new ClusterScenarioPanel(_orchestrationBus!, _uiCache);
            _fileDialogService = FileDialogServiceFactory.Create();
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
            // Curated test scenarios: copy the git-committed set into the working NAS folder on start,
            // overwriting ONLY those names (non-curated scenarios are never touched, nothing is deleted).
            // No-op in a deployed build — there is no source tree to copy from. See
            // Hrot.ScenarioEditor.Services.CuratedScenarios.
            Hrot.ScenarioEditor.Services.CuratedScenarios.SeedIntoWorking(EditorBootstrap.ScenariosRoot);
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
        // ── IAppExitGuard: app-exit "unsaved changes" prompt ──────────────────────────────────

        /// <inheritdoc/>
        public Fdp.Toolkit.Runner.ExitDisposition OnExitRequested()
        {
            if (_exitPrompt == null) return Fdp.Toolkit.Runner.ExitDisposition.CanExit;
            return _exitPrompt.RequestExit()
                ? Fdp.Toolkit.Runner.ExitDisposition.CanExit
                : Fdp.Toolkit.Runner.ExitDisposition.Deferred;
        }

        /// <inheritdoc/>
        public bool ExitApproved => _exitPrompt?.ExitApproved ?? false;

        /// <summary>
        /// Renders the app-exit unsaved-changes modal while a close is pending. ImGui-only; the button
        /// actions delegate to the headless-testable <see cref="AiShared.Documents.AppExitPromptController"/>.
        /// </summary>
        private void DrawExitPromptModal()
        {
            if (_exitPrompt == null || !_exitPrompt.IsPrompting) return;

            const string popupId = "Unsaved Changes###ai_app_exit_confirm";
            if (!_exitPopupOpened)
            {
                ImGuiNET.ImGui.OpenPopup(popupId);
                _exitPopupOpened = true;
            }

            var center = ImGuiNET.ImGui.GetMainViewport().GetCenter();
            ImGuiNET.ImGui.SetNextWindowPos(center, ImGuiNET.ImGuiCond.Appearing, new System.Numerics.Vector2(0.5f, 0.5f));

            bool stayOpen = true;
            if (ImGuiNET.ImGui.BeginPopupModal(popupId, ref stayOpen,
                    ImGuiNET.ImGuiWindowFlags.AlwaysAutoResize | ImGuiNET.ImGuiWindowFlags.NoSavedSettings))
            {
                var dirty = _exitPrompt.DirtyDocuments;
                ImGuiNET.ImGui.TextUnformatted(
                    $"You have {dirty.Count} document{(dirty.Count == 1 ? "" : "s")} with unsaved changes:");
                ImGuiNET.ImGui.Spacing();
                foreach (var d in dirty)
                    ImGuiNET.ImGui.BulletText($"{d.Asset.Name}  ({d.Kind})");
                ImGuiNET.ImGui.Spacing();
                ImGuiNET.ImGui.TextUnformatted("Save them before exiting?");
                ImGuiNET.ImGui.Spacing();

                if (ImGuiNET.ImGui.Button("Save All & Exit"))
                { ImGuiNET.ImGui.CloseCurrentPopup(); _exitPopupOpened = false; _exitPrompt.ResolveSaveAndExit(); }
                ImGuiNET.ImGui.SameLine();
                if (ImGuiNET.ImGui.Button("Discard & Exit"))
                { ImGuiNET.ImGui.CloseCurrentPopup(); _exitPopupOpened = false; _exitPrompt.ResolveDiscardAndExit(); }
                ImGuiNET.ImGui.SameLine();
                if (ImGuiNET.ImGui.Button("Cancel"))
                { ImGuiNET.ImGui.CloseCurrentPopup(); _exitPopupOpened = false; _exitPrompt.ResolveCancel(); }

                ImGuiNET.ImGui.EndPopup();
            }
            else if (!stayOpen)
            {
                // Dismissed via the popup's [X] / Esc — treat as Cancel (stay open, nothing saved).
                _exitPopupOpened = false;
                _exitPrompt.ResolveCancel();
            }
        }

        public void DrawUI()
        {
            if (_headless) return;

            // App-exit unsaved-changes modal — rendered on top when a window-close was deferred.
            if (ImGuiNET.ImGui.GetCurrentContext() != System.IntPtr.Zero)
                DrawExitPromptModal();

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

            // BATCH-29 (MTB-P8-T3): Draw the shell-global picker frame (Open Asset via Tree layout).
            _shellPickers?.DrawFrame();

            // BATCH-42 (MTB2-T8b): Draw the Save-As browser dialog (New flow).
            if (_saveAsBrowser != null && _iconProvider != null)
                _saveAsBrowser.DrawFrame(_iconProvider);
        }

        /// <inheritdoc/>
        public void RegisterWindows(Fdp.Presentation.WindowManager.WindowManager windowManager)
        {
            _wm = windowManager;

            // Colored menu icons: resolve semantic keys (e.g. "shell/save", "asset/btree") to
            // silk-atlas sprites, drawn in an aligned gutter by the shared menu renderers. Bound
            // to the WindowManager's own atlas so the texture matches what it renders with.
            windowManager.MenuIcons = Hrot.Editor.AiShared.Adapters.SilkMenuIconResolver.Create(windowManager.Atlas);
            if (_gizmoLayer != null)
                _gizmoLayer.ContextMenuIconResolver = windowManager.MenuIcons; // gizmo right-click menus

            // Wire the ImGui file dialog fallback so it renders on non-Windows hosts.
            // Harmless no-op for the Win32 backend: WindowManager only draws the service
            // when it is an ImGuiFileDialogService.
            if (_fileDialogService != null)
                windowManager.SetFileDialogService(_fileDialogService);

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
                new Hrot.BTree.Editor.Catalog.BTreeComposedBlueprintReferenceContributor(),
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

            // ⭐ BATCH 84 — kept on the instance so a rail can put the editor into the exact state
            //   R-66 describes (a document open, the sim DOWN) and check what the variable surfaces
            //   then read. ⛔ Without this the anti-vacuity probe for R-66 is not expressible, and a
            //   rail that cannot be reddened is not a rail.
            var debugRegistry   = _aiDebugRegistry = new DebugSessionRegistry();
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

            // BATCH-11: Build the live-value provider for the Blackboard Authoring window's Value column.
            // Captures _fdpRepoAdapter (set in Initialize, null until simulation runs) and _behaviorRegistry
            // via lambdas so both perspectives share the same live-world source.
            // ⭐⭐⭐ Batch 95 (95b) — this sentence is now TRUE BY CONSTRUCTION, and it was FALSE when
            //    it was written: the four stores each held a private entity field and only
            //    _aiEditorSelectionStore was ever written, so "both read the same entity via their
            //    respective store" described an intention, not the code. ⇒ every provider below
            //    returned null on its entity gate and every row read "(pending)" for ever.
            // ⭐ All four stores now share one SharedEntitySelection (see the constructor), so one
            //    provider instance per perspective is again the right shape.
            var btreeLiveValueProvider = new LiveBlackboardValueProvider(
                sessionFactory:  () => _fdpRepoAdapter,
                registryFactory: () => _behaviorRegistry,
                store:           _btreeSelectionStore);
            var hsmLiveValueProvider = new LiveBlackboardValueProvider(
                sessionFactory:  () => _fdpRepoAdapter,
                registryFactory: () => _behaviorRegistry,
                store:           _hsmSelectionStore);

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

            // ⭐⭐⭐ Batch 97 (97c) — THE WRITE SIDE, and the reason a paused edit never landed.
            //    🔴🔴 Measured by Batch 96: TryWriteWorkingStateField (Batch 84) and the WriteLiveValue
            //    delegate both shipped with ZERO production call sites, so VariableEditCommit.Commit
            //    answered LiveWriteUnavailable for every paused edit on every host. ⛔ R-67's seventh
            //    instance -- and it hid for six batches because a refusal is a LEGITIMATE outcome, so a
            //    refusing editor is indistinguishable from a correctly-gated one.
            //    ⭐ Same store as the READ (blueprintLiveValueProvider, below), deliberately: the write
            //      must target whatever the read displayed. See BlueprintLiveValueWriter's remarks
            //      (R-78's chameleon sentinel).
            // ⭐⭐⭐ W4 (2026-08-21) — MOVED UP from beside the Blueprint registrar, because the shared
            //    yellow needs it BEFORE perspectiveServices is built: StagedWriteView resolves a row's
            //    address through THIS object, so that the yellow and the write cannot disagree about
            //    where a variable lives (R-13). ⛔ Nothing else about it changed.
            var blueprintLiveValueWriter = new BlueprintLiveValueWriter(
                sessionFactory: () => debugRegistry.ActiveSession as IBlueprintDebugSession,
                store:          _blueprintSelectionStore);

            // ⭐⭐⭐ BATCH 84 / R-67 — ONE shared-service bundle instead of three hand-written argument
            //    lists. 🔴🔴 The lists had diverged: facetEditService went to BTree and HSM and NOT to
            //    Blueprint, so "Edit value…" and "Properties…" did nothing on the Blueprint
            //    perspective; expressionTargetFieldAccessor, aggregatorService and liveValueProvider
            //    were dropped there too. ⛔ That is the FOURTH instance of "a production caller that
            //    HAS a dependency must PASS it", and passing one more argument does not compose --
            //    the next shared service is one more thing three call sites must remember.
            // ⭐ PerspectiveWorkspaceServices REQUIRES facetEditService and both clock signals, so the
            //   omission is no longer expressible. What stays below is what genuinely differs per
            //   perspective.
            var perspectiveServices = new Hrot.Editor.AiShared.Windows.PerspectiveWorkspaceServices(
                catalog, refactorService, debugRegistry, facetEditService,
                // ⭐⭐ R-66 — the run state comes from the CLOCK, not from "is a document open".
                //    IPreviewController.IsInPreviewMode is what "the sim is up" means in this editor:
                //    EnterPreviewMode switches the clock to continuous, ExitPreviewMode switches it
                //    back to deterministic.
                isSimUp:  () => _previewController?.IsInPreviewMode ?? false,
                // ⭐ Ruling 15's two arms: a breakpoint pause OR deterministic stepping. ⛔ Read
                //   through the SAME adapter the Blueprint debugger uses -- not a second rule.
                // ⭐⭐⭐ THIRD ARM (2026-08-21, M-40) -- THE SIMULATION CLOCK ITSELF.
                // 🔴🔴 User: "it is fail in the value changing point - value does not change although
                //    I do it when sim is paused." 📐 Measured: the two arms below see only the DEBUGGER.
                //    The pause a designer actually presses is ITimeTransportFacade.TogglePlayPause
                //    (MainToolbarTimeControlSection:42, ClusterTimeControlStatusBarSection:47), which
                //    sets the clock's TimeScale to 0 -- and NOTHING here asked the clock. ⇒ the panel
                //    answered Running, TargetFor(Running) is Nowhere, and the dialog refused with
                //    "only when the simulation is paused" WHILE IT WAS PAUSED.
                // ⛔⛔⛔ 2026-08-21, CORRECTED THE SAME DAY -- the first version of this third arm was
                //    `_timeController.GetCurrentState().IsPaused`, and it CAN NEVER BE TRUE. Two
                //    independent reasons, both measured (M-42):
                //      (a) GlobalTime.IsPaused is `TimeScale == 0`, and a pause NEVER sets TimeScale
                //          to 0 -- PauseTimeIntent switches the master to MasterMode.Stepping, which
                //          returns BuildGlobalTime(dt: 0, ...) with TimeScale UNCHANGED.
                //      (b) GetCurrentState() is `BuildGlobalTime(0.0f, 0.0f)` -- it hard-codes dt to
                //          zero, so no delta-based predicate can be read through it either.
                //    ⚠ The comment it replaced asserted "the toolbar sets the clock's TimeScale to 0".
                //    That was inferred, not measured, and it was false.
                // ⭐⭐⭐ The clock's real answer is DeltaTime on the ECS singleton the kernel pushes
                //    every frame (ModuleHostKernel.UpdateInternal). `_world` IS the kernel's live world
                //    (:661), so this reads the same struct every system sees this tick.
                isFrozen: () => (_bpManager?.IsPaused ?? false)
                             || (_bpTimeAdapter?.IsPausedByDebugger ?? false)
                             || ClockIsHalted())
            {
                BreakpointManager             = _bpManager,
                SanitizerRegistry             = sanitizerRegistry,
                ExportBuilder                 = comparisonExportBuilder,
                SessionRegistry               = comparisonSessionRegistry,
                AggregatorService             = aggregatorService,
                SchemaExporter                = sharedSchemaExporter,
                ExpressionTargetFieldAccessor = ResolveExpressionTargetField,
                // ⭐⭐⭐ L0.4 (R-122) — "entity selection is on the entity". The Details context reads
                //    SelectionState from the WORLD, not from an editor-side copy.
                // ⭐ `_world` IS the kernel's live world (:661) — the same one SelectionInteractionSystem
                //   writes and the ring gizmos read. ⛔ A production caller that HAS it must PASS it.
                EntitySelection               = new Hrot.Editor.AiShared.Shell.WorldEntitySelectionSource(() => _world),

                // ⭐⭐⭐ W4 — THE ONE SHARED STAGED SET, built here and nowhere else.
                //    📄 DESIGN_Staged_Live_Write.md §4 fork A / §7; 📌 R-120 (shared state lives at the
                //    composition root, not in a view).
                // 🔒 User, 2026-08-21: "both yellow, both showing the same staged value, immediately
                //    after user edit." ⇒ ONE instance, forwarded to every IVariableTableHost by the
                //    registrar — ⛔ one per perspective would let two surfaces disagree.
                // ⭐⭐ All three arms are RESOLVED AT CALL TIME, not captured: _bpManager is assigned at
                //    :1127, AFTER this bag is built. ⛔ Capturing it here would bind null for the
                //    editor's whole lifetime and nothing would ever go yellow — 📌 the same
                //    construction-order shape as L0.4's world and L3.3's first wiring.
                StagedWrites                  = new Hrot.Editor.AiShared.Variables.StagedWriteView(
                    writes:         () => _bpManager,
                    resolve:        blueprintLiveValueWriter.ResolveStagedField,
                    selectedEntity: () => blueprintLiveValueWriter.SelectedEntity),
            };

            _btreeRegistrar    = perspectiveServices.CreateRegistrar(
                "BTree", _btreeSelectionStore,
                validators: new Hrot.Editor.AiShared.Validation.IAssetValidator[]
                {
                    new Hrot.BTree.Editor.Validation.BTreeAssetValidator(
                        new Hrot.BTree.Editor.Validation.BTreeValidator()),
                },
                liveValueProvider: btreeLiveValueProvider);
            _hsmRegistrar      = perspectiveServices.CreateRegistrar(
                "HSM", _hsmSelectionStore,
                validators: new Hrot.Editor.AiShared.Validation.IAssetValidator[]
                {
                    new Hrot.Hsm.Editor.Validation.HsmAssetValidator(
                        sharedSchemaExporter,
                        isStatefulSubtree: IsStatefulSubtreeAsset,
                        sharedScopeKeys:   SharedScopeKeysOfAsset),
                },
                liveValueProvider: hsmLiveValueProvider);
            // ⭐⭐⭐ Batch 88a — Blueprint's live-value provider, row 58's unbuilt half.
            //    🔴 This call used to say "no live-value provider yet" and pass none, so the Details
            //    Value column rendered (pending) forever — the DESIGNED output for a source with no
            //    reader, which is exactly why nothing looked broken and the row was merged half-built.
            //    ⭐ Same INTERFACE as BTree/HSM, different SOURCE: theirs reads BrainBlackboard through
            //    BehaviorRegistry; this one reads the blueprint debug session's state snapshot.
            //    📌 R-67 — the Blueprint registrar is the one that has forgotten a service four times,
            //    so the argument is passed here rather than defaulted somewhere downstream.
            //    ⭐⭐ The composition root resolves the blueprint session and hands the READ — so the
            //    provider never type-tests the shared registry, and BlueprintRuntimeInspectorPane keeps
            //    sole ownership of the paused-pointer-vs-live rule.
            var blueprintLiveValueProvider = new BlueprintLiveValueProvider(
                readerFactory: () => debugRegistry.ActiveSession is IBlueprintDebugSession bp
                    ? (self, assetId) =>
                          Hrot.Blueprints.Editor.Inspector.BlueprintRuntimeInspectorPane
                              .ResolveInspectorSnapshot(bp, self, assetId)
                    : null,
                store: _blueprintSelectionStore);

            // ⭐ `blueprintLiveValueWriter` is built ABOVE, beside `facetEditService` — W4 moved it so
            //   the shared StagedWriteView could resolve addresses through the same object the write
            //   uses. See its comment there.

            // ⭐ Blueprint still has no host-specific validator -- and it SAYS so, rather than
            //   expressing that by omitting a whole argument list's worth of shared services.
            _blueprintRegistrar = perspectiveServices.CreateRegistrar(
                "Blueprint", _blueprintSelectionStore,
                validators: Array.Empty<Hrot.Editor.AiShared.Validation.IAssetValidator>(),
                liveValueProvider: blueprintLiveValueProvider,
                // ⛔⛔ BTree and HSM pass NONE, above, and that is not an omission: neither host has a
                //    staged surgical write, so their paused edits keep answering LiveWriteUnavailable.
                //    ⭐ Faking one would be "the unsafe route wearing the safe one's name"
                //    (VariableEditCommit's own remark). ⚠ When they grow one, it is passed HERE.
                //    ⭐⭐ Batch 102 (102b) — WriteLive, not Write: it carries the REASON a refusal
                //      happened, so the dialog names the cause instead of the "no writer installed OR it
                //      refused" sentence that made a missing capability look like a correct gate (M-36).
                writeLive: blueprintLiveValueWriter.WriteLive);

            // Document manager — activated doc drives perspective switch.
            _aiDocumentManager = new AiDocumentManager(_perspectiveSwitcher);
            _perspectiveSwitcher.SetDocumentManager(_aiDocumentManager);

            // Toolbar debug icons (AiDebugCommands) gate IsEnabled on debugRegistry.ActiveSession. Mirror the active
            // document's debug session into the registry so those icons enable/disable live. Side-effect-free setter
            // (NOT TryAcquire/Release) — the blueprint session is eagerly attached + is DebugProbe.Sink and must NOT be
            // detached. Blueprint only: BTree/HSM debug sessions are not yet attached/working → mapped to null for now.
            void SyncActiveDebugSession()
            {
                Hrot.Editor.AiShared.Debug.IAiDebugSession? session = _aiDocumentManager?.Active?.Kind switch
                {
                    Hrot.Editor.AiShared.AssetKind.Blueprint => _blueprintDebugSession,
                    // BTree/HSM debug sessions are not yet attached/working — intentionally null until wired.
                    _ => null,
                };
                debugRegistry.SetActiveSession(session);
            }
            _aiDocumentManager.ActiveChanged += SyncActiveDebugSession;
            SyncActiveDebugSession(); // initialise for whatever doc (if any) is already active

            // BATCH-26: Asset-pick action router — file kinds → AiDocumentManager.Open,
            // Scenario → IEditorLogic.LoadScenarioByName. Null-safe delegates guard
            // against bare-ctor scenarios.
            _assetPickRouter = new Hrot.Editor.AssetPickActionRouter(
                openDocument: a => _aiDocumentManager?.Open(a),
                loadScenario: name => _editorLogic?.LoadScenarioByName(name));

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
                    // BB1D: share ONE BTreeFacetFqnContext between the dispatcher (writer)
                    // and the drawer (reader) so the blackboard-field picker filters by the
                    // current action's DtoType in the same frame.
                    var btreeCtx     = new BTreeFacetFqnContext();
                    var btreeDrawers = BTreePickerDrawerFactory.BuildDrawers(
                        btreeAsset, _behaviorRegistry, sharedSchemaExporter, btreeCtx);
                    _btreeRegistrar?.Inspector.SetFacetEditService(facetEditService, btreeDrawers);
                    // FIX-A + BB1D: wire the per-asset facet dispatcher with the shared context
                    // so InspectorWindow.GetCurrentFacet() returns a non-null facet and
                    // the picker reads the updated FQN on the same frame.
                    _btreeRegistrar?.Inspector.SetFacetDispatcher(
                        BTreeSelectionBridgeHelper.BuildFacetDispatcher(btreeAsset, btreeCtx));
                }
                else if (active?.Kind == Hrot.Editor.AiShared.AssetKind.Hsm
                    && active.Asset is Hrot.Hsm.Editor.Model.HsmAsset hsmAsset)
                {
                    // BB1D: share ONE HsmFacetFqnContext between the dispatcher (writer)
                    // and the drawer (reader) so the blackboard-field picker filters by the
                    // current transition action's DtoType in the same frame.
                    var hsmCtx     = new HsmFacetFqnContext();
                    var hsmDrawers = HsmPickerDrawerFactory.BuildDrawers(
                        hsmAsset, sharedSchemaExporter, hsmCtx);
                    _hsmRegistrar?.Inspector.SetFacetEditService(facetEditService, hsmDrawers);
                    // FIX-A + BB1D: wire the per-asset facet dispatcher with the shared context
                    // so InspectorWindow.GetCurrentFacet() returns a non-null facet and
                    // the picker reads the updated FQN on the same frame.
                    _hsmRegistrar?.Inspector.SetFacetDispatcher(
                        HsmSelectionBridgeHelper.BuildFacetDispatcher(hsmAsset, hsmCtx));
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
                        commands:       ctx?.Commands ?? new NodeEditor.Core.Action.EditorCommandsImpl(),
                        // BP-12b: item rename/delete/duplicate record onto this document's undo stack.
                        view:           ctx?.View,
                        // BP-57/BP-72: the Local Variables section is GRAPH-scoped — it follows the
                        // canvas through this provider, the same one the signature window below
                        // takes. The other five sections are asset-scoped and ignore it.
                        currentGraphId: ctx?.CurrentGraphId,
                        // BP-223: where the locals "+" refusal on a macro graph is drawn.
                        indicators:     ctx?.Indicators);

                    // Retarget Details window (just needs the BlueprintAsset).
                    _blueprintDetailsWindow?.Retarget(bpAsset);

                    // Retarget Variables window via legacy bridge store.
                    _blueprintLegacySelectionStore.SelectAsset(bpAsset);

                    // BATCH-03D2: Retarget Graph Signature window.
                    // BP-72: also hand it the canvas's current-graph provider. Unlike the three
                    // windows above (all asset-scoped) this one is GRAPH-scoped, so after a BP-24
                    // graph switch it would otherwise sit on functionGraphs[0] and edit the
                    // signature of a graph the designer is not looking at.
                    _blueprintSignatureWindow?.Retarget(bpAsset, ctx?.CurrentGraphId);
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

            // (MTB2-T7: legacy RecipeCreateModal removed.)

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

            // App-exit unsaved-changes prompt: reuses the same Save-All action for its
            // "Save All & Exit" choice. Its dirty-doc list comes from the document manager.
            _exitPrompt = new Hrot.Editor.AiShared.Documents.AppExitPromptController(
                _aiDocumentManager, () => _saveAllCallback?.Invoke());

            // ── BATCH-20 (DEC-9): per-kind service registry for Save-As ──────────────────────────
            // Create the INewAssetService dictionary so ShellSaveCommands.requestSaveAs
            // can seed a SaveAsDialog from the current document's asset.
            // BUG-A6: pass the SOURCE-project JSON scan roots so CreateNew writes the
            // new file where _btreeJsonContrib / _hsmJsonContrib will find it on Refresh.
            // Fallback to null lets the service default to AssetRoots.AssetsFor (bin dir)
            // when the source dir is unavailable (e.g. CI build without source tree).
            _newAssetServices = new Dictionary<Hrot.Editor.AiShared.AssetKind, Hrot.Editor.AiShared.Recipes.INewAssetService>
            {
                [Hrot.Editor.AiShared.AssetKind.Blueprint] = new Hrot.Blueprints.Editor.BlueprintNewAssetService(),
                [Hrot.Editor.AiShared.AssetKind.BTree]     = new Hrot.BTree.Editor.BTreeNewAssetService(_btreeJsonRootDir),
                [Hrot.Editor.AiShared.AssetKind.Hsm]       = new Hrot.Hsm.Editor.HsmNewAssetService(_hsmJsonRootDir),
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

            // Base-folder resolver for KnownSubfolders (mirrors AssetBrowserPanel.BaseFolderFor
            // which is internal — lambda wraps the same try/catch over AssetRoots.AssetsFor).
            Func<Hrot.Editor.AiShared.AssetKind, string?> baseFolderFor = kind =>
            {
                try { return Hrot.Editor.AiShared.AssetRoots.AssetsFor(kind); }
                catch (ArgumentOutOfRangeException) { return null; }
            };

            // ── BATCH-06: register shell save commands (Ctrl+S/Ctrl+Shift+S fix, §20) ──────────
            // Wire the three save commands into the global shell command set with production
            // save delegates and a requestSaveAs seam (DEC-9: connected to SaveAsDialog model).
            _shellInputSource = new ImGuiInputSource();
            _shellHotkeyDispatcher = new Hrot.Editor.AiShared.Windows.EditorHotkeyDispatcher(
                _shellInputSource);

            // BATCH-43 (MTB2-T8b): scenario Save-As → SaveAsBrowserDialog (same browser as New + doc Save-As).
            Action openScenarioSaveAs = () =>
            {
                if (_editorLogic == null || _newAssetServices == null) return;

                var currentName = _editorLogic.LoadedScenarioName ?? "";
                // Extract leaf name (e.g. "folder/my_scenario" → "my_scenario").
                int lastSlash = currentName.LastIndexOf('/');
                string initialName = lastSlash >= 0 ? currentName.Substring(lastSlash + 1) : currentName;

                var fp = new Hrot.Editor.AiShared.Browser.FolderPickerState(
                    Hrot.Editor.AiShared.Browser.AssetFolderDerivation.KnownSubfolders(
                        catalog.All, Hrot.Editor.AiShared.AssetKind.Scenario, baseFolderFor));

                var req = BuildSaveAsRequest(
                    Hrot.Editor.AiShared.AssetKind.Scenario, "Save Scenario As",
                    initialName, "", "Save", fp);

                _saveAsBrowser?.Open(req, result =>
                {
                    if (!result.Confirmed) return;

                    // Compute full scenario name (trim leading "/").
                    string dest = result.DestinationPath.TrimStart('/');
                    string fullName = string.IsNullOrEmpty(dest)
                        ? result.Name
                        : dest + "/" + result.Name;

                    _editorLogic?.SaveScenarioAs(fullName);
                    _saveAllStatus = $"[OK] Saved scenario as '{fullName}'.";
                });
            };

            Hrot.Editor.AiShared.Documents.ShellSaveCommands.Register(
                register:          windowManager.ShellCommands.Register,
                docManager:        _aiDocumentManager,
                saveBlueprint:     saveBlueprintDelegate,
                saveBTree:         saveBTreeDelegate,
                saveHsm:           saveHsmDelegate,
                saveScenario:      null, // Scenario saved via IEditorLogic, not file delegate
                requestSaveAs:     doc =>
                {
                    // BATCH-43 (MTB2-T8b): open the SaveAsBrowserDialog;
                    // on confirm, seed a SaveAsDialog to perform the fresh-id duplicate write.
                    if (_newAssetServices == null) return;

                    var fp = new Hrot.Editor.AiShared.Browser.FolderPickerState(
                        Hrot.Editor.AiShared.Browser.AssetFolderDerivation.KnownSubfolders(
                            catalog.All, doc.Asset.Kind, baseFolderFor));

                    var req = BuildSaveAsRequest(
                        doc.Asset.Kind, $"Save {doc.Asset.Kind} As",
                        doc.Asset.Name, FolderOf(doc.Asset, doc.Asset.Kind, baseFolderFor),
                        "Save", fp);

                    _saveAsBrowser?.Open(req, result =>
                    {
                        if (!result.Confirmed) return;
                        if (_newAssetServices == null) return;

                        var dialog = new Hrot.Editor.AiShared.Recipes.SaveAsDialog(
                            doc.Asset, _newAssetServices,
                            saveMintOnlyAsset: saveAsBlueprintToFile,
                            saveScenarioAs:    saveAsScenario);

                        dialog.Name = result.Name;
                        dialog.FolderPicker.SelectedRelPath = result.DestinationPath;
                        var r = dialog.Confirm();
                        _saveAllStatus = r.IsSuccess
                            ? $"[OK] Saved as '{result.Name}'."
                            : $"[INFO] Save As: {r.Error}";
                    });
                },
                report:               msg => _saveAllStatus = msg,
                isScenarioContext:    () => windowManager.CurrentPerspective == "Editor",
                hasLoadedScenario:    () => !string.IsNullOrEmpty(_editorLogic?.LoadedScenarioName),
                saveScenarioAction:   () => { _editorLogic?.SaveCurrentScenario(); _saveAllStatus = $"[OK] Saved scenario '{_editorLogic?.LoadedScenarioName}'."; },
                requestScenarioSaveAs: openScenarioSaveAs,
                describeActiveTarget: () =>
                {
                    if (windowManager.CurrentPerspective == "Editor")
                    {
                        var n = _editorLogic?.LoadedScenarioName;
                        return string.IsNullOrEmpty(n) ? "Save Scenario" : $"Save [scenario: {n}]";
                    }
                    // BUG-A12: resolve from the current perspective, not docManager.Active.
                    var act = ResolveDocumentForCurrentPerspective(windowManager, _aiDocumentManager);
                    return act != null
                        ? $"Save [{act.Kind.ToString().ToLowerInvariant()}: {act.Asset.Name}]"
                        : "Save";
                },
                resolveActiveDocument: () =>
                    ResolveDocumentForCurrentPerspective(windowManager, _aiDocumentManager));
            // ───────────────────────────────────────────────────────────────────────────────────

            // SAVE-ON-CLOSE FIX: saving is DECOUPLED from closing. The former
            // BeforeDocumentClosed "flush-on-close" (PU-603) silently wrote ANY dirty doc to
            // disk on EVERY close path (tab X, whole-editor close, app-exit) with no
            // confirmation — and for blueprints the projection-only save corrupted
            // hand-authored explicit-GUID assets. It is removed. This delegate is now invoked
            // ONLY by the unsaved-changes prompt's "Save" button (AiGraphCanvasWindow.
            // ResolveCloseSave); every other close path discards. Kind dispatch reuses the
            // per-kind save delegates wired above for Save-All (§PU-602).
            // FOLLOW-UP: app-exit currently discards dirty docs silently; a real app-exit
            // "you have unsaved changes" prompt is tracked separately (user-requested).
            Action<Hrot.Editor.AiShared.Documents.AiDocument> saveDocumentOnClose = doc =>
            {
                var asset = doc.Asset;
                var path  = asset.SourceFilePath;
                if (string.IsNullOrEmpty(path)) return; // no path → nothing to save

                try
                {
                    switch (doc.Kind)
                    {
                        case Hrot.Editor.AiShared.AssetKind.Blueprint:
                            // saveBlueprintDelegate resolves BlueprintAsset via ViewState.
                            saveBlueprintDelegate(asset, path);
                            break;
                        case Hrot.Editor.AiShared.AssetKind.BTree:
                            saveBTreeDelegate(asset, path);
                            break;
                        case Hrot.Editor.AiShared.AssetKind.Hsm:
                            saveHsmDelegate(asset, path);
                            break;
                        default:
                            // Other kinds (Scenario, Blackboard, Utility) are not saved via
                            // the document-save path — skip silently.
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[SAVE-ON-CLOSE] Failed to save '{asset.Name}': {ex.Message}");
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

            // ── BATCH-29 (MTB-P8-T3): Dedicated shell picker registry + AssetPickerLauncher ──
            // Replaces the AssetPickerModal production path with the Tree-layout entry-driven
            // picker (PickerRegistry.OpenPicker). Separate from adapterBundle.PickerRegistry
            // (which canvas windows already DrawFrame) to avoid double-DrawFrame.
            _shellPickers = new NodeEditor.UI.Picker.PickerRegistry();
            _shellPickers.SetServices(adapterBundle.IconProvider, adapterBundle.EditorTheme);

            // BATCH-42 (MTB2-T8b): capture icon provider + init Save-As browser dialog.
            _iconProvider = adapterBundle.IconProvider;
            _saveAsBrowser = new NodeEditor.UI.Dialogs.SaveAsBrowserDialog();

            // Null-safe guard: _assetPickRouter may be null in bare-ctor tests.
            var assetPickerLauncher = _assetPickRouter != null
                ? new Hrot.Editor.AssetPickerLauncher(
                    openPicker: _shellPickers.OpenPicker,
                    catalog:    catalog,
                    router:     _assetPickRouter)
                : null;

            // Local helper: directory part of an asset's relative path for the given kind.
            // Promoted from ShowNewAssetDialog so BuildSaveAsRequest can also use it.
            static string FolderOf(
                Hrot.Editor.AiShared.IEditableAsset a,
                Hrot.Editor.AiShared.AssetKind k,
                Func<Hrot.Editor.AiShared.AssetKind, string?> bf)
            {
                var rel = Hrot.Editor.AiShared.Browser.AssetRelPath.RelPath(a, bf(k));
                int lastSlash = rel.LastIndexOf('/');
                return lastSlash >= 0 ? rel.Substring(0, lastSlash) : "";
            }

            // ── BATCH-43 (MTB2-T8b): shared SaveAsRequest builder for New + Save-As flows. ──
            NodeEditor.UI.Dialogs.SaveAsRequest BuildSaveAsRequest(
                Hrot.Editor.AiShared.AssetKind kind, string title, string initialName,
                string initialDestination, string confirmLabel,
                Hrot.Editor.AiShared.Browser.FolderPickerState folderPicker)
            {
                return new NodeEditor.UI.Dialogs.SaveAsRequest
                {
                    Title              = title,
                    InitialName        = initialName,
                    InitialDestination = initialDestination,
                    ConfirmLabel       = confirmLabel,
                    GetFolderTree = () => Hrot.Editor.AiShared.Browser.AssetFolderDerivation.ToCategoryNode(
                        folderPicker.FolderPaths.ToList()),
                    GetFolderContents = folder => catalog.All
                        .Where(a => a.Kind == kind &&
                            FolderOf(a, kind, baseFolderFor) == folder)
                        .Select(a => new NodeEditor.UI.Dialogs.SaveAsContentItem(
                            a.Name,
                            Hrot.Editor.AiShared.AssetKindIcons.GetIconKey(kind)))
                        .ToList(),
                    OnCreateFolder = (parent, newName) => folderPicker.AddFolder(parent, newName),
                    NameExists = (name, dest) => catalog.All.Any(a =>
                        a.Kind == kind &&
                        FolderOf(a, kind, baseFolderFor) == dest &&
                        a.Name == name),
                    ValidateName = name => string.IsNullOrWhiteSpace(name)
                        ? "Name must not be empty."
                        : null,
                };
            }

            // ── BATCH-36 (MTB2-T7): NewAssetLauncher — opens the recipe Tree picker; ──
            // on pick → ShowNewAssetDialog opens the Save-As browser (BATCH-42: MTB2-T8b).
            void ShowNewAssetDialog(Hrot.Editor.AiShared.AssetKind kind, Hrot.Editor.AiShared.IEditableAsset recipe)
            {
                if (_newAssetServices == null) return;

                var folderPicker = new Hrot.Editor.AiShared.Browser.FolderPickerState(
                    Hrot.Editor.AiShared.Browser.AssetFolderDerivation.KnownSubfolders(
                        catalog.All, kind, baseFolderFor));

                string initialName = _newAssetServices[kind].IsBlankTemplate(recipe)
                    ? $"New{kind}"
                    : recipe.Name;

                var request = BuildSaveAsRequest(kind, $"New {kind}", initialName, "", "Create", folderPicker);

                _saveAsBrowser?.Open(request, result =>
                {
                    if (!result.Confirmed) return;
                    var minted = _newAssetServices![kind].CreateNew(recipe, result.Name, result.DestinationPath);
                    // Blueprint is mint-only — write its file at the chosen folder;
                    // BTree/HSM/Scenario persist in CreateNew.
                    if (kind == Hrot.Editor.AiShared.AssetKind.Blueprint)
                    {
                        // BUG-A6: pass _bpRootDir as the asset-root override so the file
                        // lands in the SOURCE project dir that BlueprintAssetContributor
                        // scans (_bpRootDir), not the bin/output dir (AssetRoots.AssetsFor).
                        var bpPath = Hrot.Editor.AiShared.AssetSavePath.Compose(
                            Hrot.Editor.AiShared.AssetKind.Blueprint,
                            result.DestinationPath, result.Name,
                            assetRootOverride: _bpRootDir);
                        saveAsBlueprintToFile(minted, bpPath);
                    }
                    // Refresh the catalog then open the catalogued (concrete) asset (document kinds).
                    if (kind is Hrot.Editor.AiShared.AssetKind.Blueprint
                        or Hrot.Editor.AiShared.AssetKind.BTree
                        or Hrot.Editor.AiShared.AssetKind.Hsm)
                    {
                        var aiAsm = AppDomain.CurrentDomain.GetAssemblies()
                            .FirstOrDefault(a => a.GetName().Name == "Hrot.AI.Behaviors");
                        if (aiAsm != null) _aiCatalogBuilder?.RefreshFromAssembly(aiAsm);
                        // BUG-A6: RefreshFromAssembly only refreshes assembly-based contributors;
                        // JSON contributors must be refreshed separately so the newly-written
                        // .btree.json / .hsm.json file is discovered and FindByAssetId succeeds.
                        if (kind == Hrot.Editor.AiShared.AssetKind.BTree && _btreeJsonRootDir != null)
                            _btreeJsonContrib?.Refresh(rootDirectory: _btreeJsonRootDir);
                        if (kind == Hrot.Editor.AiShared.AssetKind.Hsm && _hsmJsonRootDir != null)
                            _hsmJsonContrib?.Refresh(rootDirectory: _hsmJsonRootDir);
                        var catalogued = _aiCatalogBuilder?.Catalog?.FindByAssetId(minted.AssetId);
                        if (catalogued != null)
                            _aiDocumentManager?.Open(catalogued);
                        else
                            _saveAllStatus = $"[INFO] Created '{minted.Name}'.";
                    }
                    else
                        _saveAllStatus = $"[OK] Created {kind}: '{minted.Name}'.";
                });
            }

            var newAssetLauncher = _newAssetServices != null
                ? new Hrot.Editor.NewAssetLauncher(
                    openPicker:         _shellPickers.OpenPicker,
                    services:           _newAssetServices,
                    showNewAssetDialog: ShowNewAssetDialog)
                : null;

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
                    // BATCH-29 (MTB-P8-T3): scenario.load opens via AssetPickerLauncher.
                    // The callback (Action<IEditableAsset?>) is passed as onPicked so the
                    // existing scenario-load contract (ScenarioMenuCommands) is preserved.
                    assetPickerLauncher?.Open(kinds, callback);
                },
                openSaveAsDialog:     cb => openScenarioSaveAs(),
                showMigrationHistory:  sidecars =>
                {
                    // Log migration sidecars to the save status line for visibility.
                    _saveAllStatus = sidecars.Count == 0
                        ? "[Migration] No sidecars found for current scenario."
                        : $"[Migration] {sidecars.Count} sidecar(s): "
                          + string.Join(", ", sidecars.Select(s => $"{s.Kind} v{s.Version}"));
                },
                // Curated test scenarios: enabled only from a source checkout; copies the working copies of
                // the git-committed set back into git. No-op/disabled in a deployed build.
                isCuratedSaveEnabled: () => Hrot.ScenarioEditor.Services.CuratedScenarios.CanSaveToGit(),
                saveCuratedToGit:     () =>
                {
                    var written = Hrot.ScenarioEditor.Services.CuratedScenarios.SaveWorkingToGit(EditorBootstrap.ScenariosRoot);
                    _saveAllStatus = written.Count == 0
                        ? "[Curated] No curated scenarios saved (not a source checkout, or none present)."
                        : $"[Curated] Saved {written.Count} scenario(s) to git: " + string.Join(", ", written);
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
                pickers:      adapterBundle.PickerRegistry,
                input:        adapterBundle.InputSource,
                saveDocument: saveDocumentOnClose);

            var hsmCanvasWindow = new Hrot.Editor.AiShared.Windows.AiGraphCanvasWindow(
                assetKind:  "HSM",
                docManager: _aiDocumentManager,
                renderer:   new Hrot.Editor.AiShared.Windows.DelegatingCanvasRenderSeam(
                    renderDelegate:    view => hsmCanvasRenderer.Render(view, null),
                    renderWithFindBar: (view, fb, cmds) => hsmCanvasRenderer.Render(view, fb, cmds)),
                pickers:      adapterBundle.PickerRegistry,
                input:        adapterBundle.InputSource,
                saveDocument: saveDocumentOnClose);

            // AIE-046: Blueprint canvas window.
            var blueprintCanvasWindow = new Hrot.Editor.AiShared.Windows.AiGraphCanvasWindow(
                assetKind:  "Blueprint",
                docManager: _aiDocumentManager,
                renderer:   new Hrot.Editor.AiShared.Windows.DelegatingCanvasRenderSeam(
                    renderDelegate:    view => blueprintCanvasRenderer.Render(view, null),
                    renderWithFindBar: (view, fb, cmds) => blueprintCanvasRenderer.Render(view, fb, cmds)),
                pickers:      adapterBundle.PickerRegistry,
                input:        adapterBundle.InputSource,
                saveDocument: saveDocumentOnClose);

            // Register the canvas windows into their respective perspectives via the extension seam.
            _btreeRegistrar!.RegisterExtraWindow(windowManager, btreeCanvasWindow);
            _hsmRegistrar!.RegisterExtraWindow(windowManager, hsmCanvasWindow);
            // AIE-046: Register Blueprint canvas window into the Blueprint perspective.
            _blueprintRegistrar!.RegisterExtraWindow(windowManager, blueprintCanvasWindow);

            // UX: when a document becomes active (opened from the browser, or re-activated), make
            // sure its canvas window is visible. The user may have closed the canvas, and switching
            // perspective alone does NOT reopen a closed window (WindowManager.SwitchPerspective only
            // flips CurrentPerspective). AiDocumentManager.Activate switches perspective BEFORE firing
            // ActiveChanged, so here OwningPerspective == CurrentPerspective — ShowWindow just sets
            // IsOpen=true (idempotent, no cross-perspective pinning, no focus-stealing).
            _aiDocumentManager.ActiveChanged += () =>
            {
                var activeDoc = _aiDocumentManager.Active;
                var canvasId = activeDoc?.Kind switch
                {
                    Hrot.Editor.AiShared.AssetKind.Blueprint => blueprintCanvasWindow.Id,
                    Hrot.Editor.AiShared.AssetKind.BTree     => btreeCanvasWindow.Id,
                    Hrot.Editor.AiShared.AssetKind.Hsm       => hsmCanvasWindow.Id,
                    _ => null,
                };
                if (canvasId != null)
                    windowManager.ShowWindow(canvasId);
            };

            // BF-UX1 FIX C: wire the per-frame selection→Details bridge.
            // Bookmarks: also draw the off-screen edge-marker overlay (yellow arrows toward
            // bookmarked slots 1-9 that are scrolled out of view) every frame the canvas is
            // drawn — mirrors NodeEditor.Demo.DemoShell's overlay convention exactly (see
            // BlueprintEditorBootstrap.DrawBookmarkEdgeMarkers for why this isn't wired as an
            // ICustomCanvasRenderer).
            var blueprintSelectionAfterDraw =
                Hrot.Blueprints.Editor.Host.BlueprintSelectionBridgeHelper.BuildAfterDrawAction(
                    _blueprintSelectionStore);
            // BP-223: the missing consumer. IEditorIndicators.Notify has been enqueueing into a
            // ToastQueue nothing drained (the only TryDequeue in the repo was NodeEditor.Demo's own
            // shell), so bookmark notifications were discarded and BP-74's collapse refusal would
            // have been too. Drawn on the same per-frame hook as the bookmark overlay.
            var blueprintToasts = new Hrot.Blueprints.Editor.NotificationOverlay();
            blueprintCanvasWindow.AfterDraw = ctx =>
            {
                blueprintSelectionAfterDraw(ctx);
                if (ctx.Bookmarks != null)
                    Hrot.Blueprints.Editor.BlueprintEditorBootstrap.DrawBookmarkEdgeMarkers(
                        ctx.View, ctx.Bookmarks, adapterBundle.EditorTheme);
                blueprintToasts.Draw(ctx.Indicators, ImGuiNET.ImGui.GetIO().DeltaTime);
            };
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

            // ── Bookmarks panel window (set/jump via Ctrl+1..9 / Ctrl+Shift+1..9; this
            // window is just a read-only list of the active document's bookmarks) ───────────
            var blueprintBookmarksWindow =
                new Hrot.Blueprints.Editor.Windows.BlueprintBookmarksWindow(_aiDocumentManager!);
            _blueprintRegistrar!.RegisterExtraWindow(windowManager, blueprintBookmarksWindow);

            // ── AIE-048: Blueprint Details + Variables windows ────────────────────────────────
            _blueprintDetailsWindow = new Hrot.Blueprints.Editor.Windows.BlueprintDetailsWindow(
                selectionStore:  _blueprintSelectionStore,
                drawerRegistry:  _blueprintNodeDrawers ?? new Hrot.Blueprints.Editor.NodeDrawers.BlueprintNodeDrawerRegistry(),
                // ⭐⭐ Batch 99 (99a) — the Properties form's RENAME runs this. 📌 The silent-default
                //    ruling: "a production caller that HAS a dependency must PASS it" — this method
                //    hands the SAME service to BlueprintVariablesManagedWindow seven lines below, and
                //    the first draft of 99a left this one defaulted to null.
                refactorService: refactorService);
            _blueprintRegistrar!.RegisterExtraWindow(windowManager, _blueprintDetailsWindow);

            // ⛔⛔ L5 — BlueprintVariablesManagedWindow / BlueprintVariablesWindow are RETIRED
            //    (Q38's retire list). ⭐ Their replacement is LIVE and that is the precondition §6 L5
            //    sets: BlueprintDetailsWindow hosts the SHARED VariableDetailsSection (U-6, Batch 82),
            //    and the per-perspective AiVariablesWindow is the standalone table.
            //    ⚠ The legacy store bridge (_blueprintLegacySelectionStore) STAYS — GraphSignatureWindow
            //      below still uses it.

            // BATCH-03D2: Graph Signature window — edits Function graph Inputs/Outputs.
            // Uses the same legacy selection store bridge (SelectAsset is called in ActiveChanged).
            _blueprintSignatureWindow = new Hrot.Blueprints.Editor.Windows.GraphSignatureWindow(
                selectionStore: _blueprintLegacySelectionStore,
                dirtyTracker:   _blueprintSaveDirtyTracker,
                // BP-125: without this the window only marked the asset dirty — a declared output never
                // became a pin on the Return node, and the edit was not undoable (BP-102). Passed as an
                // accessor because the edit service is created after this window.
                editServiceAccessor: () => _blueprintEditService);
            _blueprintRegistrar!.RegisterExtraWindow(windowManager, _blueprintSignatureWindow);

            // BATCH-03C2: blueprint asset catalog used by BlueprintDocumentFactory to build the
            // peer-signature lookup so CallPeerBlueprintNodes project typed argument pins from the
            // peer blueprint's exported function signature (read on demand from disk).
            //
            // BP-66: this scanned "{BaseDirectory}/blueprints" — a directory that does not exist.
            // Every other blueprint consumer uses Assets/Blueprints (AssetRoots.AssetsRelative), so
            // EnumerateAll() returned nothing and the lookup silently fell back to the untyped
            // exec+Return pin shape for every CallPeerBlueprint node. It matched the other path in
            // this same file at §715 and §3099.
            var blueprintPeerCatalog = new Hrot.Blueprints.Editor.BlueprintPeerSource(
                _bpRootDir ?? Hrot.Editor.AiShared.AssetRoots.AssetsFor(
                    Hrot.Editor.AiShared.AssetKind.Blueprint));

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
                            breakpointManager:   _bpManager,
                            actionSchema:        sharedSchemaExporter,
                            // Phase D (AIE-053): "Open Blueprint" context-menu item on composed
                            // AiPrimitive nodes — resolve via the shared asset catalog, open via
                            // the shared AiDocumentManager (which also switches perspective).
                            assetCatalog:        _aiCatalogBuilder?.Catalog,
                            openBlueprint:       a => _aiDocumentManager?.Open(a));
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

                // QR-03: BTree quick-reload trigger — active BehaviorTreeAsset → ToDto →
                // EmitTopologyCore + EmitBridge → TriggerFromSourcesAsync (self-registering bridge).
                _btreeQuickReloadTrigger = () =>
                {
                    var ctx     = _aiDocumentManager?.Active?.ViewState
                        as Hrot.Editor.AiShared.Windows.AiCanvasContext;
                    var btAsset = ctx?.AssetRef as Hrot.BTree.Editor.Model.BehaviorTreeAsset;
                    if (btAsset == null) { _blueprintCompileStatus = "No active BTree document."; return; }

                    var dto      = Hrot.BTree.Editor.Persistence.BehaviorTreeAssetMapper.ToDto(btAsset);
                    var topology  = Hrot.AiEditor.Persistence.Emit.BTreeEmitCore.EmitTopologyCore(dto);
                    var bridge    = Hrot.AiEditor.Persistence.Emit.BTreeBridgeEmitCore.EmitBridge(dto);

                    var asmName = $"BTreePatch_{dto.AssetId:N}_{Guid.NewGuid():N}";
                    var result = quickReloadService.TriggerFromSourcesAsync(
                        new[] { (topology, dto.Name + ".g.cs"), (bridge, dto.Name + ".Registrar.g.cs") },
                        asmName).GetAwaiter().GetResult();

                    _blueprintCompileStatus = result.Succeeded
                        ? $"Compiled BTree '{dto.Name}' in {result.DurationMs}ms"
                        : $"BTree compile failed: {result.ErrorMessage}";
                };

                // QR-04: HSM quick-reload trigger — active HsmAsset → ToDto →
                // EmitTopologyCore + EmitBridge → TriggerFromSourcesAsync (self-registering bridge).
                _hsmQuickReloadTrigger = () =>
                {
                    var ctx      = _aiDocumentManager?.Active?.ViewState
                        as Hrot.Editor.AiShared.Windows.AiCanvasContext;
                    var hsmAsset = ctx?.AssetRef as Hrot.Hsm.Editor.Model.HsmAsset;
                    if (hsmAsset == null) { _blueprintCompileStatus = "No active HSM document."; return; }

                    var dto      = Hrot.Hsm.Editor.Persistence.HsmAssetMapper.ToDto(hsmAsset);
                    var topology = Hrot.AiEditor.Persistence.Emit.HsmEmitCore.EmitTopologyCore(dto);
                    var bridge   = Hrot.AiEditor.Persistence.Emit.HsmBridgeEmitCore.EmitBridge(dto);

                    var asmName = $"HsmPatch_{dto.AssetId:N}_{Guid.NewGuid():N}";
                    var result = quickReloadService.TriggerFromSourcesAsync(
                        new[] { (topology, dto.Name + ".g.cs"), (bridge, dto.Name + ".Registrar.g.cs") },
                        asmName).GetAwaiter().GetResult();

                    _blueprintCompileStatus = result.Succeeded
                        ? $"Compiled HSM '{dto.Name}' in {result.DurationMs}ms"
                        : $"HSM compile failed: {result.ErrorMessage}";
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

            // ── BATCH-29 (MTB-P8-T3): "Open Asset" command (shell.openAsset) — leftmost toolbar
            // button, File→Open Asset… menu item, Ctrl+O hotkey. Opens the Tree-layout
            // picker via AssetPickerLauncher with Kinds=All. ────────────────────────────
            string openAssetId = "shell.openAsset";
            windowManager.ShellCommands.Register(
                new EditorCommandDescriptor(
                    Id:          openAssetId,
                    DisplayName: "Open Asset…",
                    Category:    "File",
                    Description: "Open an AI asset (blueprint, behavior tree, HSM, scenario, etc.)",
                    IconKey:     "browser/open",
                    DefaultKey:  new KeyBinding(EditorKey.O, KeyModifiers.Ctrl),
                    IsEnabled:   () => true),
                _ =>
                {
                    // BATCH-29 (MTB-P8-T3): Open Asset via the Tree-layout picker launcher.
                    // router.Route is the default pick action (no onPicked callback).
                    assetPickerLauncher?.Open(AssetKindFilter.All);
                });

            // ── BATCH-36 (MTB2-T7): "New Asset" command (shell.newAsset) — opens the recipe
            // Tree picker via NewAssetLauncher. Ctrl+N hotkey. ───────────────────────
            string newAssetId = "shell.newAsset";
            windowManager.ShellCommands.Register(
                new EditorCommandDescriptor(
                    Id:          newAssetId,
                    DisplayName: "New Asset…",
                    Category:    "File",
                    Description: "Create a new AI asset from a recipe",
                    IconKey:     "asset/new",
                    DefaultKey:  new KeyBinding(EditorKey.N, KeyModifiers.Ctrl),
                    IsEnabled:   () => true),
                _ =>
                {
                    newAssetLauncher?.Open();
                });

            // ── BATCH-24: Main toolbar groups (Perspective §8 + AI-debug §9) ──────────────────
            // All wiring is null-safe so RegisterWindows does not throw on a bare EditorSubsystem.
            if (windowManager.MainToolbar != null)
            {
                var toolbarIconProvider = new SilkIconProvider(windowManager.Atlas);

                // ── BATCH-36: "New Asset" button (sortOrder -11, left of Open Asset) ──
                ToolbarCommandAdapter.Register(windowManager.MainToolbar, windowManager.ShellCommands,
                    newAssetId, toolbarIconProvider, sortOrder: -11);

                // ── BATCH-26: "Open Asset" button (leftmost, sortOrder -10) ─────────
                ToolbarCommandAdapter.Register(windowManager.MainToolbar, windowManager.ShellCommands,
                    openAssetId, toolbarIconProvider, sortOrder: -10);

                // ── BATCH-31: "Save" button (sortOrder -9, right of Open Asset) ──
                ToolbarCommandAdapter.Register(windowManager.MainToolbar, windowManager.ShellCommands,
                    Hrot.Editor.AiShared.Documents.ShellSaveCommands.SaveId,
                    toolbarIconProvider, sortOrder: -9);

                // Separator after Open Asset + Save (between toolbar group and Perspective).
                windowManager.MainToolbar.RegisterSeparator("ToolbarSep_OpenAsset", sortOrder: 0);

                // ── A. Perspective icon keys — register before creating the section so
                //    PerspectiveToolbarSection.BuildRadioModel() resolves icons on first frame.
                windowManager.RegisterPerspectiveIconKey("BTree",      "asset/btree");
                windowManager.RegisterPerspectiveIconKey("HSM",        "asset/hsm");
                windowManager.RegisterPerspectiveIconKey("Blueprint",  "asset/blueprint");
                windowManager.RegisterPerspectiveIconKey("Blueprints", "asset/blueprint");
                windowManager.RegisterPerspectiveIconKey("Editor",     "perspective/editor");

                // MTB2-T5: Show "Editor" perspective as "Scenario" in the Perspective menu.
                windowManager.RegisterPerspectiveLabel("Editor", "Scenario");

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

                // ── C. Build / reload (§9, sortOrder range 50–51) ──────────────────────
                windowManager.ShellCommands.Register(
                    new EditorCommandDescriptor(
                        Id:          "blueprint.compileReload",
                        DisplayName: "Compile / Reload",
                        Category:    "Blueprint",
                        Description: "Compile & hot-reload the active blueprint / BTree / HSM",
                        IconKey:     "build/compile",
                        DefaultKey:  null,
                        IsEnabled:   () => _aiDocumentManager?.Active?.Kind
                            is Hrot.Editor.AiShared.AssetKind.Blueprint
                            or Hrot.Editor.AiShared.AssetKind.BTree
                            or Hrot.Editor.AiShared.AssetKind.Hsm),
                    _ =>
                    {
                        switch (_aiDocumentManager?.Active?.Kind)
                        {
                            case Hrot.Editor.AiShared.AssetKind.Blueprint: _blueprintCompileCallback?.Invoke(); break;
                            case Hrot.Editor.AiShared.AssetKind.BTree:     _btreeQuickReloadTrigger?.Invoke();  break;
                            case Hrot.Editor.AiShared.AssetKind.Hsm:       _hsmQuickReloadTrigger?.Invoke();    break;
                        }
                    });

                windowManager.ShellCommands.Register(
                    new EditorCommandDescriptor(
                        Id:          "blueprint.fullRebuild",
                        DisplayName: "Full Rebuild",
                        Category:    "Build",
                        Description: "Rebuild all AI behavior assets",
                        IconKey:     "build/rebuild",
                        DefaultKey:  null,
                        IsEnabled:   () => true),
                    _ => _blueprintFullRebuildCallback?.Invoke());

                windowManager.MainToolbar.RegisterSeparator("ToolbarSep_AiDebugToBuild", sortOrder: 49);
                ToolbarCommandAdapter.Register(windowManager.MainToolbar, windowManager.ShellCommands,
                    "blueprint.compileReload", toolbarIconProvider, sortOrder: 50);
                ToolbarCommandAdapter.Register(windowManager.MainToolbar, windowManager.ShellCommands,
                    "blueprint.fullRebuild", toolbarIconProvider, sortOrder: 51);
            }
            // ───────────────────────────────────────────────────────────────────────────────────

            // BATCH-26: File → Open Asset… menu item (Ctrl+O shortcut attached via descriptor).
            MenuCommandAdapter.Register(windowManager.GlobalMenu, windowManager.ShellCommands,
                openAssetId, "File/Open Asset…");

            // BATCH-36: File → New Asset… menu item (Ctrl+N shortcut attached via descriptor).
            MenuCommandAdapter.Register(windowManager.GlobalMenu, windowManager.ShellCommands,
                newAssetId, "File/New Asset…");

            // ── MTB2-T5 (BATCH-34): File menu save entries ──────────────────────────
            // Guard each with Get(id) != null so the bare-ctor RegisterWindows path is null-safe.
            if (windowManager.ShellCommands.Get(Hrot.Editor.AiShared.Documents.ShellSaveCommands.SaveId) != null)
                MenuCommandAdapter.Register(windowManager.GlobalMenu, windowManager.ShellCommands,
                    Hrot.Editor.AiShared.Documents.ShellSaveCommands.SaveId, "File/Save");

            if (windowManager.ShellCommands.Get(Hrot.Editor.AiShared.Documents.ShellSaveCommands.SaveAsId) != null)
                MenuCommandAdapter.Register(windowManager.GlobalMenu, windowManager.ShellCommands,
                    Hrot.Editor.AiShared.Documents.ShellSaveCommands.SaveAsId, "File/Save As…");

            if (windowManager.ShellCommands.Get(Hrot.Editor.AiShared.Documents.ShellSaveCommands.SaveAllId) != null)
                MenuCommandAdapter.Register(windowManager.GlobalMenu, windowManager.ShellCommands,
                    Hrot.Editor.AiShared.Documents.ShellSaveCommands.SaveAllId, "File/Save All");

            // ─────────────────────────────────────────────────────────────────────────

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
                var timeControls = new TimeControlStatusBarSection(
                    _previewController, _timeController, _world, _timeCommands);
                windowManager.StatusBar.RegisterSection(
                    id:             "editor_time_controls",
                    sortOrder:      100,
                    renderDelegate: timeControls.Render,
                    perspective:    "Editor");

                // ── BATCH-24: Main toolbar time-control group (§7, sortOrder range 0–9) ──
                var timeTransportFacade = new Hrot.Editor.UI.EditorTimeTransportFacade(
                    _previewController, _timeController, _world, _timeCommands);
                var toolbarTimeSection = new Hrot.UI.Common.Panels.MainToolbarTimeControlSection(
                    timeTransportFacade);
                windowManager.MainToolbar.RegisterEntry(
                    "TimeControlGroup", sortOrder: 0,
                    declaredHeight: Fdp.Presentation.WindowManager.MainToolbarManager.DefaultEntryHeight,
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
            // ── SAVE-ON-CLOSE FIX: app-exit does NOT save open documents ───────────────────────────
            // Saving is decoupled from closing (see AiGraphCanvasWindow.ResolveCloseSave): only an
            // explicit prompt-"Save" persists a doc; every other close path — including app-exit —
            // DISCARDS unsaved edits. The former `_saveAllCallback?.Invoke()` here silently
            // force-saved every dirty doc on exit, which (for blueprints) wrote a projection-only,
            // pin-stripped file over the source — persisting exploratory/invalid edits the user never
            // chose to keep. Removed.
            // FlushNow() is kept: it only drains the debounced BTree/HSM regen (blueprints are
            // in-memory-only there and never written), consistent with their auto-save-on-edit design.
            // FOLLOW-UP (user-requested): a real app-exit "you have unsaved changes" prompt — until
            // then, app-exit silently discards.
            _regenerationScheduler?.FlushNow();
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

        // ── BB1D: Shared accessor for ExpressionTargetField ──────────────────────────────
        // Returns the ExpressionTargetField value for facet types that carry it
        // (BTree Action/Condition facets; HSM Transition/GlobalTransition facets).
        // Returns null for all other facet types (e.g. BTreeWaitFacet, StateFacet).
        // Shared between both BTree and HSM perspective registrars so the
        // "Static Parameters" panel in InspectorWindow knows which blackboard variable
        // is currently bound.
        /// <summary>
        /// ⭐⭐⭐ <b><c>E4</c> — THE resolver <c>DEBT-AIB-028</c>'s activation recipe asks for:</b>
        /// <c>id =&gt; catalog.TryFind(id, out a) &amp;&amp; a.HasAnyStatefulNode()</c>.
        ///
        /// <para>
        /// ⭐ <b>It lives HERE and nowhere else</b>, because this is the only place that can see both
        /// <c>BehaviorTreeAsset</c> and <c>HsmAsset</c>. ⛔ Two copies — one per validator entry point —
        /// would let the node badges and the Diagnostics window disagree about which sub-trees are
        /// stateful, which is the same class of split the slot-key discipline exists to prevent.
        /// </para>
        ///
        /// <para>
        /// ⚠ <b>Rules 8/8b may still not fire on real assets</b>: <c>StateNode.SubtreeAssetId</c> is not
        /// persisted (<c>DEBT-AIB-028</c>(a)), so nothing sets the field yet — that is <c>E5</c>'s
        /// prerequisite. ⭐ This makes the WIRING honest; <c>E5</c> makes the rule reachable.
        /// </para>
        /// </summary>
        private bool IsStatefulSubtreeAsset(Guid assetId)
            => _aiCatalogBuilder?.Catalog?.FindByAssetId(assetId) switch
            {
                Hrot.BTree.Editor.Model.BehaviorTreeAsset bt => bt.HasAnyStatefulNode(),
                Hrot.Hsm.Editor.Model.HsmAsset h             => h.HasAnyStatefulNode(),
                _                                            => false,
            };

        /// <summary>
        /// ⭐⭐ <b><c>E4</c>'s SECOND resolver, supplied in Batch 69.</b> Rule 8b compares the shared
        /// (<c>Behavior</c>/<c>Entity</c>) scope keys of sub-trees running in different parallel
        /// regions; ⛔ left at its <c>_ =&gt; Array.Empty&lt;int&gt;()</c> default it could never fire.
        ///
        /// <para>
        /// ⭐ <b>Same shape, same place, same reason as <see cref="IsStatefulSubtreeAsset"/></b> — one
        /// definition, at the only layer that sees both asset types. ⚠ Batch 68 threaded the parameter
        /// and flagged that it was still defaulted; this fills it.
        /// </para>
        /// </summary>
        private IReadOnlyCollection<int> SharedScopeKeysOfAsset(Guid assetId)
            => _aiCatalogBuilder?.Catalog?.FindByAssetId(assetId) switch
            {
                Hrot.BTree.Editor.Model.BehaviorTreeAsset bt => bt.GetSharedScopeKeys(),
                Hrot.Hsm.Editor.Model.HsmAsset h             => h.GetSharedScopeKeys(),
                _                                            => System.Array.Empty<int>(),
            };

        private static string? ResolveExpressionTargetField(object? facet) => facet switch
        {
            BTreeActionFacet af          => af.ExpressionTargetField,
            BTreeConditionFacet cf       => cf.ExpressionTargetField,
            TransitionFacet tf           => tf.ExpressionTargetField,
            GlobalTransitionFacet gtf    => gtf.ExpressionTargetField,
            _                            => null,
        };

        /// <summary>
        /// BUG-A12: Resolves the open document that belongs to the CURRENT canvas perspective.
        /// Returns null when the current perspective is the Scenario/"Editor" perspective (the
        /// scenario branch handles it) or when no document of the matching kind is open.
        /// <para>
        /// Path: <c>windowManager.CurrentPerspective</c> (string) → canonical
        /// <see cref="AssetKind"/> via reverse of <see cref="AssetKindExtensions.ToPerspectiveName"/>
        /// → last open document in <paramref name="docManager"/> whose
        /// <see cref="AiDocument.Kind"/> matches → returned as the save target.
        /// </para>
        /// </summary>
        private static Hrot.Editor.AiShared.Documents.AiDocument? ResolveDocumentForCurrentPerspective(
            Fdp.Presentation.WindowManager.WindowManager windowManager,
            Hrot.Editor.AiShared.Documents.AiDocumentManager? docManager)
        {
            if (docManager == null) return null;

            // Map the current perspective name back to an AssetKind.
            // "Editor" (Scenario perspective) is handled by the scenario branch — return null here.
            var perspectiveName = windowManager.CurrentPerspective;
            Hrot.Editor.AiShared.AssetKind? targetKind = perspectiveName switch
            {
                "Blueprint" => Hrot.Editor.AiShared.AssetKind.Blueprint,
                "BTree"     => Hrot.Editor.AiShared.AssetKind.BTree,
                "HSM"       => Hrot.Editor.AiShared.AssetKind.Hsm,
                _           => (Hrot.Editor.AiShared.AssetKind?)null,
            };

            if (targetKind == null) return null;

            // Return the last open document whose kind matches the current perspective
            // (mirrors the logic in WindowManagerPerspectiveSwitcher.OnPerspectiveChanged).
            Hrot.Editor.AiShared.Documents.AiDocument? match = null;
            foreach (var doc in docManager.OpenDocuments)
            {
                if (doc.Kind == targetKind.Value)
                    match = doc; // take the last match
            }
            return match;
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






