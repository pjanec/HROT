using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Core.Diagnostics;
using Fdp.Presentation.Abstractions;
using Fdp.Presentation.Adapters;
using Fdp.Presentation.Panels;
using Fdp.Presentation.Panels.ReplayBrowser;
using Fdp.Presentation.WindowManager;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.ReplayBrowser;
using Fdp.Toolkit.ReplayBrowser.Diff;
using Fdp.Toolkit.ReplayBrowser.Search;
using Fdp.Toolkit.Runner;
using Fdp.Toolkit.Scenario;
using Fdp.Toolkit.Vis2D;
using Fdp.Toolkit.ReplayBrowser.Federation;
using Hrot.CGF.Configuration;
using Hrot.Core.Network;
using StructEdit.Reflection;

namespace Hrot.ReplayBrowser;

/// <summary>
/// Standalone replay-browser subsystem. Launches via <c>-m replaybrowser</c>.
/// Hosts an isolated <see cref="ReplayBrowserContext"/> that never touches the
/// live simulation state. Does not implement <c>IMapCameraProvider</c> so the
/// spatial camera remains independent of other subsystems.
/// </summary>
public sealed class ReplayBrowserSubsystem : ISubsystem, IWindowRegistrar
{
    // ── ISubsystem ────────────────────────────────────────────────────────

    public string Name => "ReplayBrowser";
    public Vector4 TitleBarColor => new(0.2f, 0.6f, 0.8f, 1f);

    // ── State (always allocated on Initialize) ────────────────────────────


    private FederatedReplayManager? _manager;
    private EntityRepository? _activeRepo;
    private EntitySelectionHistory _entityHistory = null!;
    private PlaybackHistoryTracker _playbackHistory = null!;
    private bool _headless;

    // ── Federation / Merged View state ────────────────────────────────────

    private ViewMode _viewMode = ViewMode.SingleNode;
    private EntityRepository? _transientMaster;
    private TransientMasterBuilder? _transientBuilder;
    private FederationPanel? _federationPanel;

    /// <summary>⭐⭐⭐ U-obs-5 follow-up — kept so <c>OnLoadGroup</c> can RE-REGISTER
    /// <see cref="Fdp.Presentation.Windows.ReplayBrowser.FederationWindow"/> under the same id every
    /// time <see cref="_federationPanel"/> is replaced by a fresh group load; <c>RegisterWindows</c> is
    /// called only once, before any group is loaded, so this is the only place that can do it.</summary>
    private WindowManager? _windowManager;

    // ── Internal accessors for testing ────────────────────────────────────

    internal FederatedReplayManager? Manager => _manager;
    internal EntityRepository? ActiveRepo => _activeRepo;
    internal ViewMode ViewMode => _viewMode;

    /// <summary>
    /// Test seam: when set, replaces the <see cref="TransientMasterBuilder.Build"/> call
    /// inside <see cref="BuildAndBindTransientMaster"/> so tests can count builds or
    /// return a controlled repo without spinning up <see cref="TransientMasterBuilder"/>.
    /// </summary>
    internal Func<FederatedReplayManager, EntityRepository>? TransientBuildOverride;

    // ── State (non-headless only) ─────────────────────────────────────────

    private MapCanvas? _canvas;
    private InspectorState? _inspectorState;
    private RepositoryAdapter? _session;
    private ReplayTimelinePanel? _timelinePanel;
    private IFileDialogService? _fileDialogService;
    private IRecordingExportService? _exportService;
    private ComponentDiffPanel? _diffPanel;
    private ComponentDiffService _diffService = new ComponentDiffService();
    private EntityInspectorPanel? _inspectorPanel;
    private EventBrowserPanel? _eventPanel;
    private ReplaySearchPanel? _searchPanel;
    private ScenarioSerializer _scenarioSerializer = null!;
    private BehaviorRegistry _behaviorRegistry = new();
    // Required by PredicateCompiler for BlueprintVariablePredicateDto; without it, blueprint
    // conditional breakpoints compile to a constant-false delegate (BP-29). Mirrors CgfSubsystem.
    private Fdp.Toolkit.Blueprints.BlueprintRegistry _blueprintRegistry = new();
    // ── Continuous Diff Tracking ──────────────────────────────────────────
    private int _lastDiffFrame = -1;
    private Entity? _lastDiffEntity = null;
    private JsonNode? _lastDiffEntityJson = null;
    private float _playbackAccumulator = 0f;
    private Task? _seekToChangeTask;
    private volatile int _pendingChangeSeekFrame = -1;
    // â”€â”€ Gizmo debug overlay â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    private Fdp.Toolkit.Diagnostics.Gizmos.DebugPrimitiveBuffer? _gizmoBuffer;
    private Fdp.Toolkit.Diagnostics.Gizmos.Systems.GlobalGizmoManager? _globalGizmoManager;
    private Fdp.Toolkit.Diagnostics.Gizmos.Systems.DataDrivenGizmoSystem? _dataDrivenGizmoSystem;
    private Fdp.Toolkit.Diagnostics.Gizmos.Systems.StatelessGizmoSystem? _statelessGizmoSystem;
    private Fdp.Toolkit.Vis2D.Layers.DebugGizmoLayer? _gizmoLayer;
    private Fdp.Core.FdpEventBus? _interactionBus;
    private Hrot.Common.Systems.GlobalActionDispatchSystem? _actionDispatchSystem;
    private Hrot.ScenarioEditor.Systems.SelectionInteractionSystem? _selectionSystem;
    private readonly Fdp.Toolkit.Diagnostics.Gizmos.Hub.GizmoUiStateHub _gizmoUiHub = new();

    // ── Constructors ──────────────────────────────────────────────────────

    /// <summary>
    /// Constructor used by <c>ScanForSubsystems</c> / <c>TryCreateSubsystem</c>.
    /// The <paramref name="networkFactory"/> is accepted but intentionally unused;
    /// the replay browser is fully offline.
    /// </summary>
    public ReplayBrowserSubsystem(INetworkFactory networkFactory) { _ = networkFactory; }

    /// <summary>Parameterless constructor for unit tests.</summary>
    public ReplayBrowserSubsystem() { }

    // ── ISubsystem lifecycle ──────────────────────────────────────────────

    public void Initialize(SubsystemConfig config)
    {
        _headless = config.Headless;
        _manager = null;
        _activeRepo = null;
        _entityHistory = new EntitySelectionHistory();
        _playbackHistory = new PlaybackHistoryTracker();

        if (!_headless)
        {
            _activeRepo = new EntityRepository();
            Fdp.Toolkit.ReplayBrowser.Federation.RepositoryPriming.RegisterDiscoveredComponents(_activeRepo);
            _canvas = new MapCanvas();

            _inspectorState = new InspectorState();
            _session = new RepositoryAdapter(_activeRepo);

            var behaviorRegistry = new BehaviorRegistry();
            _behaviorRegistry = behaviorRegistry;
            CgfBehaviorSetup.LoadFromAiAssembly(behaviorRegistry, _blueprintRegistry);
            _scenarioSerializer = Hrot.SimHost.Serializers.HrotScenarioSerializerFactory.Build(behaviorRegistry);
            Hrot.Presentation.Renderers.BrainBlackboardRenderer.BehaviorRegistryAccessor = behaviorRegistry;
            Hrot.Presentation.Renderers.Blackboard1024Renderer.BehaviorRegistryAccessor = behaviorRegistry;
            Hrot.Presentation.Renderers.BTreeVisualizerRenderer.BehaviorRegistryAccessor = behaviorRegistry;
            Hrot.Presentation.Renderers.BehaviorStateRenderer.BehaviorRegistryAccessor = behaviorRegistry;
            Hrot.Presentation.Renderers.BTreeTraceWorkingMemoryRenderer.BehaviorRegistryAccessor = behaviorRegistry;
            Hrot.Presentation.Renderers.HsmTraceWorkingMemoryRenderer.BehaviorRegistryAccessor = behaviorRegistry;
            // â”€â”€ Gizmo Setup â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            // ── UXI-23 S2b: the shared pack constructs the map's machinery ──────────────────────
            // 🔒 The pack CONSTRUCTS; this host still SCHEDULES (it holds the two systems directly rather
            // than scheduling the togglable group, which remains its choice — the run-set is its role).
            //
            // ⚠⚠ The four projectors below go in through ContributeExtras, which the pack invokes AFTER
            // the reflection pass and BEFORE constructing StatelessGizmoSystem. That ordering is
            // load-bearing: the system sizes its visibility cache from registry.Rules.Count, so a rule
            // registered afterwards lands beyond the cache and silently ignores its visibility policy.
            //
            // EntityEditorPolylineGizmo and EntityEditorLabelGizmo are deliberately attribute-LESS —
            // their constructors need a BehaviorRegistry, which reflection cannot supply, so it correctly
            // skips them rather than guessing.
            var rubberBandState = new Hrot.ScenarioEditor.Gizmos.RubberBandState();

            var mapInteraction = Hrot.ScenarioEditor.Map.MapInteractionPack.Build(
                new Hrot.ScenarioEditor.Map.MapInteractionContext
                {
                    World = _activeRepo!,
                    IsSelectedPredicate = static (view, entity) =>
                        view.HasComponent<Hrot.IG.Components.SelectionState>(entity) &&
                        view.GetComponentRO<Hrot.IG.Components.SelectionState>(entity).IsSelected,
                    // The replay browser is an interactive window: it has a viewer from startup.
                    StartEnabled = true,
                    ContributeExtras = regs =>
                    {
                        regs.Stateless.Register(
                            new Hrot.ScenarioEditor.Gizmos.EntityEditorPolylineGizmo(),
                            new[] { typeof(Fdp.Core.SimTransform), typeof(Fdp.Toolkit.Replication.Components.NetworkIdentity) });
                        regs.Stateless.Register(
                            new Hrot.ScenarioEditor.Gizmos.EntityEditorLabelGizmo(behaviorRegistry),
                            new[] { typeof(Fdp.Core.SimTransform), typeof(Fdp.Toolkit.Replication.Components.NetworkIdentity) });
                        regs.Stateless.RegisterGlobal(new Hrot.ScenarioEditor.Gizmos.RubberBandGizmo(rubberBandState));
                        regs.Stateless.RegisterGlobal(new ReplaySpatialBoundsGizmo(() => _searchPanel?.ActiveSpatialBounds));
                    },
                });

            _gizmoBuffer           = mapInteraction.Buffer;
            _interactionBus        = mapInteraction.InteractionBus;
            _globalGizmoManager    = mapInteraction.GlobalManager;
            _dataDrivenGizmoSystem = mapInteraction.DataDrivenSystem;
            _statelessGizmoSystem  = mapInteraction.StatelessSystem;
            var gizmoRegistry      = mapInteraction.GizmoRegistry;
            var statelessRegistry  = mapInteraction.StatelessRegistry;
            var settingsRegistry   = mapInteraction.Settings;

            _selectionSystem = new Hrot.ScenarioEditor.Systems.SelectionInteractionSystem(_activeRepo!, _interactionBus, rubberBandState);
            _selectionSystem.OnSelectionChanged += (entity, worldPos) =>
            {
                if (entity == Fdp.Core.Entity.Null)
                    _inspectorState.SelectedEntity = null;
                else if (_activeRepo?.IsAlive(entity) ?? false)
                    _inspectorState.SelectedEntity = entity;
            };

            // â”€â”€ Layer Control & Actions â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            var actionRegistry = new Hrot.Common.Interactions.GlobalActionRegistry();
            long layerControlId = Fdp.Toolkit.Diagnostics.Gizmos.Systems.GlobalGizmoManager.NewId();
            var editService = new StructEdit.Reflection.ComponentEditServiceBuilder().Build();
            var layerControlGizmo = new Hrot.Common.Diagnostics.Gizmos.LayerControlGizmo(
                layerControlId, _interactionBus, editService, _gizmoUiHub);

            _globalGizmoManager.Register(layerControlId, layerControlGizmo);

            actionRegistry.Register(Hrot.Common.Constants.GlobalActionIds.OpenLayerControl, (_, _) =>
            {
                _interactionBus.Publish(new Hrot.Common.Diagnostics.Gizmos.OpenLayerEditorEvent());
            });

            actionRegistry.Register(Hrot.Common.Constants.GlobalActionIds.CenterOnEntity, (view, target) =>
            {
                if (target == Fdp.Core.Entity.Null) return;
                if (view.HasComponent<Fdp.Core.SimTransform>(target))
                {
                    ref readonly var tf = ref view.GetComponentRO<Fdp.Core.SimTransform>(target);
                    _canvas.Camera.FocusOn(new System.Numerics.Vector2(tf.Position.X, tf.Position.Y));
                }
            });

            _actionDispatchSystem = new Hrot.Common.Systems.GlobalActionDispatchSystem(actionRegistry, _interactionBus);

            var schemaRegistry = new GizmoMap.Presentation.GizmoSchemaRegistry();
            using var layerControlSchemaSession = editService.Open(
                new Hrot.Common.Diagnostics.Gizmos.LayerControlDto { Entities = true, Perception = true, AiHelpers = true },
                typeof(Hrot.Common.Diagnostics.Gizmos.LayerControlDto));
            schemaRegistry.Register(Hrot.Common.Diagnostics.Gizmos.LayerControlGizmo.SchemaHash, layerControlSchemaSession.Document);

            _gizmoLayer = new Fdp.Toolkit.Vis2D.Layers.DebugGizmoLayer(
                31, _gizmoBuffer, _interactionBus, _activeRepo!, _canvas.Camera,
                new GizmoMap.Presentation.Shapes.DefaultEntityShapeLibrary(), schemaRegistry);

            _canvas.AddLayer(_gizmoLayer);
            _canvas.DrawBuffer = _gizmoBuffer;

            _diffService = new ComponentDiffService();
            _exportService = new RecordingExportService(_scenarioSerializer, _diffService);
            _fileDialogService = FileDialogServiceFactory.Create();
            _timelinePanel = new ReplayTimelinePanel(
                null,
                () => _manager?.LocalEntitiesProviderNodeId ?? 0,
                _exportService,
                _fileDialogService,
                _playbackHistory,
                _inspectorState);

            // Wire up group-load delegate and merged-view query so the timeline panel
            // can coordinate with the federation manager without knowing about it.
            _timelinePanel.OnLoadGroup = paths =>
            {
                try
                {
                    _manager?.Dispose();
                    _transientMaster?.Dispose();
                    _transientMaster = null;

                    _manager = FederatedReplayManager.LoadGroup(paths);
                    _manager.OnTimeChanged += OnManagerTimeChanged;

                    CreateOrReplaceFederationPanel();

                    OnManagerTimeChanged();
                    _timelinePanel?.SetManager(_manager!);
                    return null;  // no rejection
                }
                catch (LoadGroupException ex)
                {
                    return ex.Message;
                }
                catch (System.IO.IOException ex)
                {
                    return $"Failed to read recording file: {ex.Message}";
                }
                catch (UnauthorizedAccessException ex)
                {
                    return $"Access denied reading recording file: {ex.Message}";
                }
                catch (System.Text.Json.JsonException ex)
                {
                    return $"Recording metadata is corrupt: {ex.Message}";
                }
            };
            _timelinePanel.IsMergedViewQuery = () => _viewMode == ViewMode.Merged;

            _transientBuilder = new TransientMasterBuilder(_scenarioSerializer);

            _inspectorPanel = new EntityInspectorPanel();
            _inspectorPanel.Serializer = _scenarioSerializer;
            _diffPanel = new ComponentDiffPanel();
            _eventPanel = new EventBrowserPanel()
            {
                SelectedProvider = "All",
                CurrentFrameProvider = () => (uint)Math.Max(0, PrimaryNodeCurrentFrame())
            };

            WireDelegates();

            // Search panel is created after WireDelegates so it receives the wired
            // seek/select intents.
        }
    }

    public void Update(float deltaTime)
    {
        int pendingSeek = _pendingChangeSeekFrame;
        if (pendingSeek >= 0)
        {
            _pendingChangeSeekFrame = -1;
            Entity currentEntity = _inspectorState?.SelectedEntity ?? Entity.Null;
            _playbackHistory.PushWaypoint(PrimaryNodeCurrentFrame(), currentEntity);
            _playbackHistory.PushWaypoint(pendingSeek, currentEntity);
            SeekFrameViaManager(pendingSeek);
        }

        if (_searchPanel != null)
        {
            if (_viewMode == ViewMode.Merged || _manager == null || _manager.Contexts.Count == 0)
                _searchPanel.CurrentFilePath = null;
            else
            {
                int searchNodeId = _manager.LocalEntitiesProviderNodeId;
                _searchPanel.CurrentFilePath = _manager.Contexts.TryGetValue(searchNodeId, out var searchCtx)
                    ? searchCtx.CurrentFdpPath : null;
            }
        }

        if (!_headless)
        {
            if (_timelinePanel != null && _timelinePanel.IsPlaying)
            {
                _playbackAccumulator += deltaTime * _timelinePanel.PlaybackRate;
                float frameTime = 1.0f / 60.0f;

                if (_playbackAccumulator > frameTime * 10f)
                    _playbackAccumulator = frameTime * 10f;

                while (_playbackAccumulator >= frameTime)
                {
                    _playbackAccumulator -= frameTime;
                    if (!TryStepForwardViaManager())
                    {
                        _timelinePanel.IsPlaying = false;
                        _playbackAccumulator = 0f;
                        break;
                    }
                }
            }

            int currentFrame = PrimaryNodeCurrentFrame();
            Entity? currentEntity = _inspectorState?.SelectedEntity;
            bool isPlaying = _timelinePanel != null && _timelinePanel.IsPlaying;

            // Reactive diff engine: re-evaluate whenever time or selection shifts.
            if ((_lastDiffFrame != currentFrame || _lastDiffEntity != currentEntity) && !isPlaying)
            {
                bool isNextFrame = (currentFrame == _lastDiffFrame + 1) && (_lastDiffEntity == currentEntity);

                if (_diffPanel != null)
                {
                    if (currentFrame > 0 && currentEntity.HasValue && !currentEntity.Value.IsNull)
                    {
                        _diffPanel.CurrentDiffs = ComputeDiffInternal(currentFrame, currentEntity.Value, isNextFrame);
                    }
                    else
                    {
                        _diffPanel.CurrentDiffs = Array.Empty<DiffNode>();
                        _lastDiffEntityJson = null;
                    }
                }

                _lastDiffFrame = currentFrame;
                _lastDiffEntity = currentEntity;
            }

            // Allow user to pan the replay viewport when ImGui isn't capturing the mouse
            if (!ImGuiNET.ImGui.GetIO().WantCaptureMouse && _canvas != null)
                _canvas.Camera.HandleInput(new Fdp.Toolkit.Vis2D.Defaults.RaylibInputProvider());

            _canvas?.Update(deltaTime);

            // Evict transient primitives before backend population
            _gizmoBuffer?.EndFrame(deltaTime);

            if (_activeRepo != null)
            {
                _selectionSystem?.Tick(deltaTime);
                _actionDispatchSystem?.Execute(_activeRepo, deltaTime);
                _dataDrivenGizmoSystem?.Execute(_activeRepo, deltaTime);
                _globalGizmoManager?.Execute(_activeRepo, deltaTime);
                _statelessGizmoSystem?.Execute(_activeRepo, deltaTime);
            }

            // Swap the interaction bus so intent events are visible on the next frame
            _interactionBus?.SwapBuffers();
        }
    }

    public void DrawWorld()
    {
        if (!_headless)
            _canvas?.Draw();
    }

    public void DrawUI()
    {
        if (_headless) return;

        _gizmoLayer?.DrawContextMenu();
        _gizmoLayer?.DrawStructInspector();

        // Render gizmo-contributed main menu items (e.g., "View > Tactical Map Layers...")
        var gizmoMenus = _gizmoLayer?.ConsumeMainMenu();
        if (gizmoMenus != null && gizmoMenus.Count > 0)
        {
            if (ImGuiNET.ImGui.BeginMainMenuBar())
            {
                GizmoMap.Presentation.ImGuiMenuRenderer.DrawMenus(gizmoMenus, actionId =>
                {
                    _interactionBus?.Publish(new Fdp.Toolkit.Diagnostics.Gizmos.Events.GizmoMenuActionEvent { AnchorId = 0, ActionId = actionId });
                });
                ImGuiNET.ImGui.EndMainMenuBar();
            }
        }
    }

    public void Shutdown()
    {
        _manager?.Dispose();
        _transientMaster?.Dispose();
    }

    // ── Federation wiring (internal for tests) ────────────────────────────

    /// <summary>
    /// Switches <see cref="ActiveRepo"/> to the sandbox repo of the local-entities
    /// provider node whenever the manager reports a time change.
    /// In Merged View, rebuilds the transient master and binds to that instead.
    /// </summary>
    private void OnManagerTimeChanged()
    {
        if (_manager == null || _manager.Contexts.Count == 0) return;

        if (_viewMode == ViewMode.Merged)
        {
            BuildAndBindTransientMaster();
        }
        else
        {
            int nodeId = _manager.LocalEntitiesProviderNodeId;
            if (_manager.Contexts.TryGetValue(nodeId, out var ctx))
                RebindActiveRepo(ctx.SandboxRepo);
        }

        // Update EventBrowserPanel's history service to the primary node's service.
        if (_eventPanel != null && _manager != null
            && _manager.Contexts.TryGetValue(_manager.LocalEntitiesProviderNodeId, out var primaryCtxForEvent))
            _eventPanel.HistoryService = primaryCtxForEvent.HistoryService;
    }

    /// <summary>
    /// Builds a fresh transient master from the current manager state and
    /// rebinds <see cref="ActiveRepo"/> to it.
    /// Disposes any previously allocated transient master.
    /// </summary>
    private void BuildAndBindTransientMaster()
    {
        if (_manager == null) return;

        EntityRepository newMaster;
        if (TransientBuildOverride != null)
        {
            newMaster = TransientBuildOverride(_manager);
        }
        else if (_transientBuilder != null)
        {
            newMaster = _transientBuilder.Build(_manager);
        }
        else
        {
            // Fallback: no builder available — bind to single-node as before.
            int nodeId = _manager.LocalEntitiesProviderNodeId;
            if (_manager.Contexts.TryGetValue(nodeId, out var ctx))
                RebindActiveRepo(ctx.SandboxRepo);
            return;
        }

        _transientMaster?.Dispose();
        _transientMaster = newMaster;
        RebindActiveRepo(_transientMaster);
    }

    /// <summary>
    /// Switches the active view mode. Notifies panels and triggers a rebind.
    /// </summary>
    internal void SetViewMode(ViewMode mode)
    {
        if (mode == ViewMode.Merged && _seekToChangeTask != null && !_seekToChangeTask.IsCompleted)
        {
            if (_diffPanel != null)
                _diffPanel.IsSearching = false;
        }

        _viewMode = mode;

        if (_searchPanel != null)
            _searchPanel.IsMergedViewActive = mode == ViewMode.Merged;

        if (_inspectorState != null)
            _inspectorState.IsMergedView = mode == ViewMode.Merged;

        // Trigger immediate rebind for the new mode.
        OnManagerTimeChanged();
    }

    /// <summary>
    /// Test seam: loads a federated group from <paramref name="paths"/> using a provided
    /// <paramref name="builder"/> (bypassing headless-guard so tests can call it directly).
    /// </summary>
    internal void LoadFdpGroupForTest(string[] paths, TransientMasterBuilder builder)
    {
        _manager?.Dispose();
        _transientMaster?.Dispose();
        _transientMaster = null;

        _transientBuilder = builder;
        _manager = FederatedReplayManager.LoadGroup(paths);
        _manager.OnTimeChanged += OnManagerTimeChanged;
        _timelinePanel?.SetManager(_manager);

        CreateOrReplaceFederationPanel();

        OnManagerTimeChanged();
    }

    /// <summary>⭐⭐⭐ U-obs-5 follow-up — creates (or replaces, on a subsequent group load) the
    /// <see cref="FederationPanel"/> for the CURRENT <see cref="_manager"/>, and — when
    /// <see cref="_windowManager"/> is already known (i.e. <see cref="RegisterWindows"/> already ran) —
    /// (re)registers <see cref="Fdp.Presentation.Windows.ReplayBrowser.FederationWindow"/> under the
    /// same id so <c>WindowManager</c>'s replace-by-id semantics swap in the new panel. Shared by the
    /// real <c>OnLoadGroup</c> delegate and the <see cref="LoadFdpGroupForTest"/> test seam so both
    /// paths exercise the identical wiring.</summary>
    private void CreateOrReplaceFederationPanel()
    {
        if (_federationPanel != null)
            _federationPanel.OnViewModeChanged -= SetViewMode;
        _federationPanel = new FederationPanel(_manager!);
        _federationPanel.OnViewModeChanged += SetViewMode;

        if (_windowManager != null)
            RegisterFederationWindow(_windowManager, _federationPanel);
    }

    private void RebindActiveRepo(EntityRepository repo)
    {
        _activeRepo = repo;
        _session = new RepositoryAdapter(repo);
    }

    /// <summary>
    /// Loads one or more .fdp recording files via a fresh <see cref="FederatedReplayManager"/>
    /// and binds <see cref="ActiveRepo"/> to the local-entities provider node's sandbox repo.
    /// Disposes any previously loaded manager first.
    /// </summary>
    internal void LoadFdpViaManager(string path)
    {
        _manager?.Dispose();
        _manager = FederatedReplayManager.LoadGroup(new[] { path });
        _manager.OnTimeChanged += OnManagerTimeChanged;
        _timelinePanel?.SetManager(_manager);
        OnManagerTimeChanged();
    }

    // ── IWindowRegistrar ──────────────────────────────────────────────────

    public void RegisterWindows(WindowManager windowManager)
    {
        if (_headless) return;
        _windowManager = windowManager;
        RegisterWindowsCore(
            windowManager,
            _timelinePanel!,
            _inspectorPanel!,
            _diffPanel!,
            _eventPanel!,
            _searchPanel!,
            _federationPanel);

        // Wire the ImGui file dialog fallback so it renders on non-Windows hosts.
        // Harmless no-op for the Win32 backend: WindowManager only draws the service
        // when it is an ImGuiFileDialogService.
        if (_fileDialogService != null)
            windowManager.SetFileDialogService(_fileDialogService);
    }

    /// <summary>
    /// Test seam: registers the replay-browser windows using caller-supplied
    /// panel instances. Skips the headless guard so tests can exercise window
    /// registration without initialising Raylib.
    /// </summary>
    /// <param name="federationPanel">⭐⭐⭐ U-obs-5 follow-up — <c>null</c> until a replay group has
    /// been loaded (the panel is created lazily); when non-null its window is registered too.</param>
    internal void RegisterWindowsCore(
        WindowManager windowManager,
        ReplayTimelinePanel timelinePanel,
        EntityInspectorPanel inspectorPanel,
        ComponentDiffPanel diffPanel,
        EventBrowserPanel eventPanel,
        ReplaySearchPanel searchPanel,
        FederationPanel? federationPanel = null)
    {
        string perspective = "ReplayBrowser";
        Vector4 color = TitleBarColor;

        // Capture safe references for the inspector window factories.
        InspectorState stateRef = _inspectorState ?? new InspectorState();

        windowManager.RegisterWindow(new Fdp.Presentation.Windows.ReplayBrowser.ReplayTimelineWindow(
            "rb_timeline", "Replay Timeline", perspective, timelinePanel, color));

        windowManager.RegisterWindow(new Fdp.Presentation.Windows.ReplayBrowser.FdpEntityInspectorWindow(
            "rb_inspector", "Replay Entity Inspector", perspective,
            inspectorPanel,
            () => _session,
            () => stateRef,
            color));

        windowManager.RegisterWindow(new Fdp.Presentation.Windows.ReplayBrowser.ComponentDiffWindow(
            "rb_diff", "Frame Diff Viewer", perspective, diffPanel, color));

        windowManager.RegisterWindow(new Fdp.Presentation.Windows.ReplayBrowser.FdpEventBrowserWindow(
            "rb_events", "Replay Event Browser", perspective, eventPanel, color));

        windowManager.RegisterWindow(new Fdp.Presentation.Windows.ReplayBrowser.ReplaySearchWindow(
            "rb_search", "Replay Search", perspective, searchPanel, color));

        if (federationPanel != null)
            RegisterFederationWindow(windowManager, federationPanel);
    }

    /// <summary>⭐⭐⭐ U-obs-5 follow-up — shared by <see cref="RegisterWindowsCore"/> (the initial
    /// registration, when a group happened to already be loaded) and <c>OnLoadGroup</c> (every
    /// subsequent group load, which replaces <see cref="_federationPanel"/>).</summary>
    private void RegisterFederationWindow(WindowManager windowManager, FederationPanel panel)
        => windowManager.RegisterWindow(new Fdp.Presentation.Windows.ReplayBrowser.FederationWindow(
            "rb_federation", "Federation", "ReplayBrowser", panel, TitleBarColor));

    // ── Delegate wiring ───────────────────────────────────────────────────

    private void WireDelegates()
    {
        var (seekIntent, selectIntent, matchIntent) = WireDelegatesForTest(
            _entityHistory, _playbackHistory, _inspectorState!, null!, _diffPanel!, _eventPanel!);

        _inspectorPanel!.OnEntitySelected = selectIntent;
        _inspectorPanel.ChainToMap = true;
        _diffPanel!.IsMergedViewQuery = () => _viewMode == ViewMode.Merged;
        _diffPanel!.OnSeekToChangeRequested = direction =>
        {
            if (_viewMode == ViewMode.Merged) return; // disabled in Merged View
            if (_inspectorState?.SelectedEntity != null)
                _seekToChangeTask = SeekToNextChangeAsync(_inspectorState.SelectedEntity.Value, direction);
        };

        // Build search services.
        var editSvc = new ComponentEditServiceBuilder()
            .RegisterFieldEditor<Type>(new Fdp.Presentation.Editing.TypeFieldEditor())
            .RegisterFieldEditor<BoundingBox2D>(new Fdp.Presentation.Editing.BoundingBoxFieldEditor())
            .RegisterFieldEditor<SearchPredicateDto>(new Fdp.Presentation.Editing.PredicateValueFieldEditor())
            .Build();
        // See BP-29: the registry is what makes BlueprintVariablePredicateDto evaluable.
        var predicateCompiler = new PredicateCompiler(editSvc, _behaviorRegistry, _blueprintRegistry);
        var eventScannerCompiler = new EventScannerCompiler(editSvc);
        var searchSvc = new RecordingSearchService(predicateCompiler, eventScannerCompiler);
        Func<Entity?> getSelectedEntity = () => _inspectorState?.SelectedEntity;
        Func<long?> getSelectedNetworkId = () =>
        {
            var e = _inspectorState?.SelectedEntity;
            if (e == null || e.Value.IsNull || !(_activeRepo?.IsAlive(e.Value) ?? false)) return null;
            if (_activeRepo?.HasComponent<Fdp.Toolkit.Replication.Components.NetworkIdentity>(e.Value) ?? false)
                return _activeRepo!.GetComponentRO<Fdp.Toolkit.Replication.Components.NetworkIdentity>(e.Value).Value;
            return null;
        };

        _searchPanel = new ReplaySearchPanel(
            editSvc, searchSvc, seekIntent, selectIntent, matchIntent,
            _behaviorRegistry, getSelectedEntity, getSelectedNetworkId);
        if (_globalGizmoManager != null)
        {
            _searchPanel.SpatialPickerCtx = new ReplaySpatialPickerContext(_globalGizmoManager);
        }
    }

    private async Task SeekToNextChangeAsync(Entity target, int direction)
    {
        if (PrimaryNodeCurrentFdpPath() == null || _diffPanel == null || _diffPanel.IsSearching)
            return;

        _diffPanel.IsSearching = true;
        string fdpPath = PrimaryNodeCurrentFdpPath()!;
        int startFrame = PrimaryNodeCurrentFrame();
        var excludedNames = new HashSet<string>(_diffPanel.ExcludedTypes.Select(t => t.Name));
        double epsilon = _diffPanel.IsEpsilonIgnored ? 0.001 : 0.0;
        var diffSvc = _diffService;
        var serializer = _scenarioSerializer;

        try
        {
            int? foundFrame = await Task.Run(() =>
            {
                using var tempContext = new ReplayBrowserContext();
                tempContext.LoadRecording(fdpPath);
                var playback = tempContext.Playback;
                if (playback == null)
                    return (int?)null;

                var repo = tempContext.SandboxRepo;
                var resolver = new Fdp.Toolkit.Diagnostics.DiagnosticGuidResolver();
                var mask512 = repo.GetSnapshotableMask();

                bool IsActualIncludedDiff(JsonNode? before, JsonNode? after)
                {
                    var diffs = diffSvc.ComputeTreeDiff(before, after, epsilon);
                    foreach (var node in diffs)
                    {
                        if (node.IsModified && !excludedNames.Contains(node.Name))
                            return true;
                    }

                    return false;
                }

                if (direction > 0)
                {
                    tempContext.SeekToFrame(startFrame, suppressHistory: true);
                    JsonNode? baseline = repo.IsAlive(target)
                        ? serializer.SerializeEntity(repo, target, resolver, mask512)
                        : null;
                    while (tempContext.StepForward(suppressHistory: true))
                    {
                        JsonNode? current = repo.IsAlive(target)
                            ? serializer.SerializeEntity(repo, target, resolver, mask512)
                            : null;

                        if (IsActualIncludedDiff(baseline, current))
                            return playback.CurrentFrame;

                        baseline = current;
                    }
                }
                else
                {
                    if (startFrame <= 0)
                        return null;

                    tempContext.SeekToFrame(0, suppressHistory: true);
                    int? lastChangeFrame = null;
                    JsonNode? baseline = repo.IsAlive(target)
                        ? serializer.SerializeEntity(repo, target, resolver, mask512)
                        : null;

                    if (IsActualIncludedDiff(null, baseline))
                        lastChangeFrame = 0;

                    while (playback.CurrentFrame < startFrame - 1)
                    {
                        if (!tempContext.StepForward(suppressHistory: true))
                            break;

                        JsonNode? current = repo.IsAlive(target)
                            ? serializer.SerializeEntity(repo, target, resolver, mask512)
                            : null;
                        if (IsActualIncludedDiff(baseline, current))
                            lastChangeFrame = playback.CurrentFrame;

                        baseline = current;
                    }
                    return lastChangeFrame;
                }

                return (int?)null;
            });

            if (foundFrame.HasValue)
            {
                _pendingChangeSeekFrame = foundFrame.Value;
            }
        }
        finally
        {
            _diffPanel.IsSearching = false;
        }
    }

    /// <summary>
    /// Test seam: wires delegates using caller-supplied dependencies.
    /// Returns the seek and select intents so tests can invoke them directly.
    /// Replaces _entityHistory, _playbackHistory, and _context with the injected
    /// objects so causality-jump and reactive diff logic operate on the same instances in tests.
    /// </summary>
    internal (Action<int> seekIntent, Action<Entity> selectIntent, Action<int, Entity> matchIntent) WireDelegatesForTest(
        EntitySelectionHistory entityHistory,
        PlaybackHistoryTracker playbackHistory,
        InspectorState inspectorState,
        ReplayBrowserContext context,
        ComponentDiffPanel diffPanel,
        EventBrowserPanel eventPanel)
    {
        _entityHistory   = entityHistory;
        _playbackHistory = playbackHistory;

        // History-driven selection: when the selection history changes, update inspector state.
        entityHistory.OnSelectionChanged += e => inspectorState.SelectedEntity = e;

        // Seek history: when the playback history fires, seek frame + restore selection.
        playbackHistory.OnWaypointRequested += wp =>
        {
            SeekFrameViaManager(wp.FrameIndex);
            inspectorState.SelectedEntity = wp.SelectedEntity.IsNull ? null : wp.SelectedEntity;
            if (!wp.SelectedEntity.IsNull)
                entityHistory.PushSelection(wp.SelectedEntity);
        };

        // Intents passed down to panels (panels stay unaware of history trackers).
        Action<int> seekIntent = f =>
        {
            Entity selected = inspectorState.SelectedEntity ?? Entity.Null;
            playbackHistory.PushWaypoint(f, selected);
            SeekFrameViaManager(f);
        };
        Action<Entity> selectIntent = e => entityHistory.PushSelection(e);
        Action<int, Entity> matchIntent = (f, e) =>
        {
            playbackHistory.PushWaypoint(PrimaryNodeCurrentFrame(), inspectorState.SelectedEntity ?? Entity.Null);
            playbackHistory.PushWaypoint(f, e);
            entityHistory.PushSelection(e);
            SeekFrameViaManager(f);
        };

        diffPanel.OnEntityLinkClicked  = selectIntent;
        eventPanel.OnEntityLinkClicked = selectIntent;
        eventPanel.OnCausalityJumpRequested = ExecuteCausalityJump;

        return (seekIntent, selectIntent, matchIntent);
    }

    /// <summary>
    /// Executes the causality jump by seeking to the frame immediately after the source event
    /// and selecting the target entity. Diff rendering is handled reactively in Update().
    /// </summary>
    internal void ExecuteCausalityJump(int eventFrame, Entity target)
    {
        _playbackHistory.PushWaypoint(PrimaryNodeCurrentFrame(), _inspectorState?.SelectedEntity ?? Entity.Null);
        // Do not push Entity.Null to entity selection history; only push the target.

        int targetFrame = eventFrame + 1;

        _entityHistory.PushSelection(target);
        _playbackHistory.PushWaypoint(targetFrame, target);
        SeekFrameViaManager(targetFrame);
    }

    /// <summary>
    /// Compatibility overload retained for existing tests. Uses the current frame as jump origin.
    /// </summary>
    internal void ExecuteCausalityJump(Entity target)
        => ExecuteCausalityJump(PrimaryNodeCurrentFrame(), target);

    // ── Manager-driven helpers ────────────────────────────────────────────

    internal int PrimaryNodeCurrentFrame()
    {
        if (_manager == null || _manager.Contexts.Count == 0) return -1;
        return _manager.Contexts.TryGetValue(_manager.LocalEntitiesProviderNodeId, out var ctx)
            ? ctx.CurrentFrame : -1;
    }

    private string? PrimaryNodeCurrentFdpPath()
    {
        if (_manager == null || _manager.Contexts.Count == 0) return null;
        return _manager.Contexts.TryGetValue(_manager.LocalEntitiesProviderNodeId, out var ctx)
            ? ctx.CurrentFdpPath : null;
    }

    private bool TryStepForwardViaManager()
    {
        if (_manager == null) return false;
        int nodeId = _manager.LocalEntitiesProviderNodeId;
        if (!_manager.Contexts.TryGetValue(nodeId, out var ctx) || ctx.Playback == null) return false;
        int nextFrame = ctx.CurrentFrame + 1;
        if (nextFrame >= ctx.Playback.TotalFrames) return false;
        _manager.StepForwardAll();
        return true;
    }

    private void SeekFrameViaManager(int frame)
    {
        if (_manager == null) return;
        int nodeId = _manager.LocalEntitiesProviderNodeId;
        if (!_manager.Contexts.TryGetValue(nodeId, out var ctx) || ctx.Playback == null) return;
        _manager.SetBaseWallTicks(ctx.Playback.GetFrameMetadata(frame).WallClockTicks);
    }

    private IReadOnlyList<DiffNode> ComputeDiffInternal(int frame, Entity entity, bool isNextFrame)
    {
        if (_manager == null || _scenarioSerializer == null || entity.IsNull) return Array.Empty<DiffNode>();
        int nodeId = _manager.LocalEntitiesProviderNodeId;
        if (!_manager.Contexts.TryGetValue(nodeId, out var primaryCtx) || primaryCtx.Playback == null)
            return Array.Empty<DiffNode>();
        if (frame <= 0 || frame >= primaryCtx.Playback.TotalFrames)
            return Array.Empty<DiffNode>();

        long? networkId = GetStableNetworkId(entity, _activeRepo);
        var resolver = new Fdp.Toolkit.Diagnostics.DiagnosticGuidResolver();
        JsonNode? before = null;

        if (isNextFrame && _lastDiffEntityJson != null)
        {
            before = _lastDiffEntityJson;
        }
        else
        {
            _manager.StepBackwardAll();
            Entity beforeEntity = networkId.HasValue ? FindEntityByNetworkId(networkId.Value) : entity;
            if (_activeRepo != null && _activeRepo.IsAlive(beforeEntity))
            {
                var mask = _activeRepo.GetSnapshotableMask();
                before = _scenarioSerializer.SerializeEntity(_activeRepo, beforeEntity, resolver, mask);
            }
            _manager.StepForwardAll();
        }

        Entity afterEntity = networkId.HasValue ? FindEntityByNetworkId(networkId.Value) : entity;
        JsonNode? after = null;
        if (_activeRepo != null && _activeRepo.IsAlive(afterEntity))
        {
            var mask = _activeRepo.GetSnapshotableMask();
            after = _scenarioSerializer.SerializeEntity(_activeRepo, afterEntity, resolver, mask);
        }

        _lastDiffEntityJson = after;
        return _diffService.ComputeTreeDiff(before, after, 0.0);
    }

    /// <summary>Test seam: exposes the two-rebuild diff cycle for headless tests.</summary>
    internal IReadOnlyList<DiffNode> ComputeDiffForTest(int frame, Entity entity)
        => ComputeDiffInternal(frame, entity, false);

    /// <summary>Test seam: injects a ScenarioSerializer for headless diff tests.</summary>
    internal void SetSerializerForTest(Fdp.Toolkit.Scenario.ScenarioSerializer serializer)
        => _scenarioSerializer = serializer;

    private static long? GetStableNetworkId(Entity entity, EntityRepository? repo)
    {
        if (repo == null || entity.IsNull || !repo.IsAlive(entity)) return null;
        if (!repo.HasComponent<Fdp.Toolkit.Replication.Components.NetworkIdentity>(entity)) return null;
        return repo.GetComponentRO<Fdp.Toolkit.Replication.Components.NetworkIdentity>(entity).Value;
    }

    /// <summary>
    /// ⭐ <c>BP-508</c> — routed through the ONE resolver *(<c>R-77</c>)*. ⛔ This copy scanned
    /// <b>every</b> entity and asked <c>HasComponent</c> per entity; the shared one filters the query.
    /// </summary>
    private Entity FindEntityByNetworkId(long networkId)
        => Fdp.Toolkit.Replication.Services.NetworkIdResolver.FindEntityByNetworkId(_activeRepo, networkId);

    // ── Null service stubs (used until real implementations are injected) ──

    private sealed class NullRecordingExportService : IRecordingExportService
    {
        public void ExportToJson(string inputFdpPath, string outputJsonPath, JsonExportOptions options) { }
    }

    private sealed class NullFileDialogService : IFileDialogService
    {
        public System.Threading.Tasks.Task<string?> ShowSaveAsDialogAsync(
            string callSiteId, string defaultFileName, string extensionFilter)
            => System.Threading.Tasks.Task.FromResult<string?>(null);

        public System.Threading.Tasks.Task<string?> ShowOpenFileDialogAsync(string callSiteId, string extensionFilter)
            => System.Threading.Tasks.Task.FromResult<string?>(null);

        public System.Threading.Tasks.Task<string[]?> ShowOpenMultipleFilesDialogAsync(string callSiteId, string extensionFilter)
            => System.Threading.Tasks.Task.FromResult<string[]?>(null);
    }

    private sealed class ReplaySpatialPickerContext : Fdp.Presentation.Editing.ISpatialPickerContext
    {
        private readonly Fdp.Toolkit.Diagnostics.Gizmos.Systems.GlobalGizmoManager _gizmoManager;
        private string? _pendingPath;
        private Fdp.Toolkit.ReplayBrowser.Search.BoundingBox2D? _resolvedBox;
        private long? _activeGizmoId;

        public ReplaySpatialPickerContext(Fdp.Toolkit.Diagnostics.Gizmos.Systems.GlobalGizmoManager gizmoManager)
        {
            _gizmoManager = gizmoManager;
        }

        public bool IsPickPendingFor(string jsonPath) => _activeGizmoId.HasValue && _pendingPath == jsonPath;

        public void RequestBoundingBoxPick(string jsonPath)
        {
            if (_activeGizmoId.HasValue)
            {
                _gizmoManager.Unregister(_activeGizmoId.Value);
                _activeGizmoId = null;
            }

            _pendingPath = jsonPath;
            _resolvedBox = null;

            long id = Fdp.Toolkit.Diagnostics.Gizmos.Systems.GlobalGizmoManager.NewId();
            var gizmo = new Fdp.Toolkit.ReplayBrowser.BoundingBoxPickerGizmo(
                box => _resolvedBox = box,
                () =>
                {
                    _gizmoManager.Unregister(id);
                    _activeGizmoId = null;
                });

            _activeGizmoId = id;
            _gizmoManager.Register(id, gizmo);
        }

        public bool TryConsumeBoundingBoxPick(string jsonPath, out Fdp.Toolkit.ReplayBrowser.Search.BoundingBox2D box)
        {
            if (_pendingPath == jsonPath && _resolvedBox.HasValue)
            {
                box = _resolvedBox.Value;
                _pendingPath = null;
                _resolvedBox = null;
                return true;
            }

            box = default;
            return false;
        }
    }

    private sealed class ReplaySpatialBoundsGizmo : Fdp.Toolkit.Diagnostics.Gizmos.IGlobalStatelessGizmo
    {
        private readonly Func<Fdp.Toolkit.ReplayBrowser.Search.BoundingBox2D?> _getBounds;

        public ReplaySpatialBoundsGizmo(Func<Fdp.Toolkit.ReplayBrowser.Search.BoundingBox2D?> getBounds)
        {
            _getBounds = getBounds;
        }

        public void Draw(Fdp.ModuleHost.Abstractions.ISimulationView view, Fdp.Toolkit.Diagnostics.Gizmos.IDebugDrawBuilder drawBuilder)
        {
            var bounds = _getBounds();
            if (!bounds.HasValue) return;

            var box = bounds.Value;
            if (box.Min == box.Max) return;

            Vector2 center = (box.Min + box.Max) * 0.5f;
            Vector2 extents = new Vector2(
                MathF.Abs(box.Max.X - box.Min.X) * 0.5f,
                MathF.Abs(box.Max.Y - box.Min.Y) * 0.5f);

            drawBuilder.DrawBox2D(
                center,
                extents,
                new Fdp.Toolkit.Diagnostics.Gizmos.Rgba32(0, 100, 0, 255),
                angleDeg: 0f,
                thickness: 1.5f,
                sizeMode: Fdp.Toolkit.Diagnostics.Gizmos.SizeMode.WorldMeters,
                target: Fdp.Toolkit.Diagnostics.Gizmos.PipelineTarget.All,
                layer: 0,
                fillColor: default,
                style: Fdp.Toolkit.Diagnostics.Gizmos.LineStyle.Dashed);
        }
    }
}
