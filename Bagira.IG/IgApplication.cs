using System;
using System.Collections.Generic;
using System.Numerics;
using Bagira.BDC.SSTD;
using Bagira.BDC.SSTM;
using Bagira.DDS.DM;
using Bagira.IG.Adapters;
using Bagira.IG.Components;
using Bagira.IG.Modules;
using Bagira.IG.Systems;
using Bagira.IG.Tools;
using Bagira.IG.Translators;
using Bagira.IG.UI;
using Bagira.Map.Common;
using Bagira.Map.Common.Commands;
using Bagira.Map.Common.Events;
using Bagira.Map.Definitions.Tkb;
using CycloneDDS.Runtime;
using Fdp.Kernel;
using Fdp.Modules.Geographic.Components;
using Fdp.Modules.Geographic.Transforms;
using FDP.Toolkit.Lifecycle;
using FDP.Kernel.Logging;
using FDP.Toolkit.Combat.Components;
using FDP.Toolkit.Lifecycle.Events;
using FDP.Toolkit.Perception.Components;
using FDP.Toolkit.Physics.Components;
using FDP.Toolkit.NetworkSpawning.Systems;
using FDP.Toolkit.Replication;
using FDP.Toolkit.Replication.Components;
using FDP.Toolkit.Replication.Services;
using FDP.Toolkit.Time.Controllers;
using FDP.Toolkit.Vis2D;
using FDP.Toolkit.Vis2D.Abstractions;
using FDP.Toolkit.Vis2D.Components;
using FDP.Toolkit.Vis2D.Defaults;
using FDP.Toolkit.Vis2D.Layers;
using ImGuiNET;
using ModuleHost.Core;
using ModuleHost.Core.Abstractions;
using ModuleHost.Core.Network;
using ModuleHost.Core.Network.Interfaces;
using ModuleHost.Network.Cyclone.Modules;
using DdsIdAllocator = ModuleHost.Network.Cyclone.Services.DdsIdAllocator;
using NodeIdMapper    = ModuleHost.Network.Cyclone.Services.NodeIdMapper;
using Raylib_cs;
using rlImGui_cs;

namespace Bagira.IG;

/// <summary>
/// Main application shell for the IG Mock. Owns the Raylib window, MapCanvas, and camera.
/// </summary>
public class IgApplication
{
    // --- Window constants ---
    public const int    WindowWidth  = 1600;
    public const int    WindowHeight = 900;
    public const int    TargetFps    = 60;
    public const string WindowTitle  = "IG Mock";

    // --- Debug overlay layout ---
    private const int DebugFontSize   = 18;
    private const int DebugLineHeight = 22;
    private const int DebugMarginX    = 10;
    private const int DebugMarginY    = 10;

    // --- Runtime state (rendering) ---
    private MapCanvas _canvas = null!;
    private MapCamera _camera = null!;

    /// <summary>
    /// Tracks the camera target set by arrow-key panning.
    /// Maintained separately from MapCamera._targetTarget so that mouse-drag pan
    /// and keyboard pan do not fight each other.
    /// </summary>
    private Vector2 _keyboardPanTarget;

    // --- Runtime state (ECS / network) ---
    private EntityRepository _world   = null!;
    private ModuleHostKernel _kernel  = null!;
    private NetworkEntityMap _entityMap = null!;

    // ── Network enabled flag — false when DDS libraries are unavailable (e.g. unit-test host)
    private bool _networkEnabled;

    // ── Headless flag — set by InitializeEmbedded(); skips all Raylib/ImGui calls in Update/Draw
    private bool _headless;

    // ── Optional domain override (tests) ─────────────────────────────────────
    private int? _domainOverride;

    // ── Task 5: IG-to-IOS event translator state ──────────────────────────────────────────────
    private WGS84Transform?                  _geoTransform;
    private BdcCommandGateway?               _commandGateway;
    private DdsWriter<MapClickEvent>?        _clickWriter;
    private DdsReader<MapInteractionConfig>? _configReader;
    private Guid                             _activeContextId;

    // ── Style and culling objects — updated and injected into modules
    private MapUserConfig     _userConfig     = null!;
    private MapCameraViewport _cameraViewport = null!;

    // ── ImGui UI panels (TASK-IF008) ──────────────────────────────────────────
    private DebugPanelState       _debugPanelState   = null!;
    private IgDebugPanel          _debugPanel        = null!;
    private EntityInspectorState  _inspectorState    = null!;
    private EntityInspectorPanel  _inspectorPanel    = null!;
    private MiniIosPanelState     _miniIosState      = null!;
    private MiniIosPanel          _miniIosPanel      = null!;
    private PerformanceMetrics    _performanceMetrics = null!;
    private PerformanceOverlay    _performanceOverlay = null!;
    private ContextMenuPanel      _contextMenuPanel   = null!;
    private ContextMenuSystem     _contextMenuSystem  = null!;

    // ── Context menu state ───────────────────────────────────────────────────
    private Entity _mapContextEntity = Entity.Null;

    // -------------------------------------------------------------------------

    public void Initialize()
    {
        Raylib.InitWindow(WindowWidth, WindowHeight, WindowTitle);
        Raylib.SetTargetFPS(TargetFps);
        rlImGui.Setup(darkTheme: true);
        InitializeEmbedded();
    }

    /// <summary>
    /// Initialises ECS, network, camera, and canvas without creating a Raylib window.
    /// Used when the orchestrator owns the window (embedded mode).
    /// The caller must create a Raylib window before invoking any rendering methods.
    /// Pass <paramref name="headless"/> = <c>true</c> to skip all Raylib/ImGui calls
    /// even during <see cref="Update"/>.
    /// </summary>
    public void InitializeEmbedded(bool headless = false, int? domainIdOverride = null)
    {
        _headless = headless;
        _domainOverride = domainIdOverride;
        _camera = new MapCamera
        {
            MinZoom   = IgCameraConstants.MinZoom,
            MaxZoom   = IgCameraConstants.MaxZoom,
            ZoomSpeed = IgCameraConstants.ZoomSpeedPerTick
        };

        // Centre the camera over the initial world position.
        _camera.Target = new Vector2(IgCameraConstants.InitialPositionX, IgCameraConstants.InitialPositionY);
        _camera.Zoom   = IgCameraConstants.InitialZoom;
        // Offset keeps the world origin centred in the window.
        _camera.Offset = new Vector2(WindowWidth / 2f, WindowHeight / 2f);

        _keyboardPanTarget = new Vector2(
            IgCameraConstants.InitialPositionX,
            IgCameraConstants.InitialPositionY);

        _canvas        = new MapCanvas(new RaylibInputProvider());
        _canvas.Camera = _camera;

        InitializeEcs();
        InitializeNetwork(enableNetwork: true, domainIdOverride: _domainOverride);
    }

    public EntityRepository World => _world;

    // -------------------------------------------------------------------------

    /// <summary>
    /// Initialises the ECS world and kernel (no DDS — safe to call in tests).
    /// </summary>
    private void InitializeEcs()
    {
        _world     = new EntityRepository();
        _entityMap = new NetworkEntityMap();

        var accumulator = new EventAccumulator();
        _kernel         = new ModuleHostKernel(_world, accumulator);

        // Pre-register components produced by style and culling systems.
        _world.RegisterComponent<ResolvedStyle>();
        _world.RegisterComponent<CullingState>();
        _world.RegisterComponent<SelectionState>();
        _world.RegisterComponent<NetworkIdentity>();
        _world.RegisterComponent<NetworkOwnership>();
        _world.RegisterComponent<NetworkAuthority>();
        _world.RegisterComponent<NetworkSpawnRequest>();
        _world.RegisterComponent<PendingNetworkAck>();
        _world.RegisterComponent<NetworkPosition>();
        _world.RegisterComponent<NetworkVelocity>();
        _world.RegisterComponent<SimTransform>();
        _world.RegisterComponent<SimVelocity>();
        _world.RegisterComponent<GeoTransform>();
        _world.RegisterComponent<GeoVelocity>();
        _world.RegisterComponent<IgHealthState>();
        _world.RegisterComponent<Faction>();
        _world.RegisterComponent<PerceptionReceptor>();
        _world.RegisterComponent<TargetMemory>();
        _world.RegisterComponent<WeaponState>();
        _world.RegisterComponent<Health>();
        _world.RegisterComponent<HealthData>();
        _world.RegisterComponent<PhysicsCollider>();

        // IG4 — Advanced Features components
        _world.RegisterComponent<HistoryTrail>();
        _world.RegisterComponent<VisualEffectState>();
        _world.RegisterComponent<TracerTarget>();
        _world.RegisterManagedComponent<ContextMenuState>();
        _world.RegisterManagedComponent<EditablePolyline>();
        _world.RegisterManagedComponent<IgVisualDef>();
        _world.RegisterManagedComponent<IgEntityData>();
        _world.RegisterManagedComponent<SimVehicleDef>();
        _world.RegisterManagedComponent<SimCombatDef>();
        _world.RegisterManagedComponent<TkbCompositionDef>();

        _world.RegisterEvent<ConstructionOrder>();
        _world.RegisterEvent<ConstructionAck>();
        _world.RegisterEvent<DestructionOrder>();
        _world.RegisterEvent<DestructionAck>();

        // IG4 — Events (skip in headless mode to avoid aggregated-ID collisions).
        if (!_headless)
        {
            try
            {
                _world.RegisterEvent<Bagira.Map.Common.Events.FireInteractionEvent>();
            }
            catch (TypeInitializationException ex) when (ex.InnerException is InvalidOperationException)
            {
                // Aggregated Runner mode can load multiple event types with the same ID.
                FdpLog<IgApplication>.Warn(
                    "[IG] FireInteractionEvent registration skipped: {0}",
                    ex.InnerException!.Message);
            }
            catch (InvalidOperationException ex)
            {
                // Aggregated Runner mode can load multiple event types with the same ID.
                FdpLog<IgApplication>.Warn("[IG] FireInteractionEvent registration skipped: {0}", ex.Message);
            }
        }

        _userConfig     = new MapUserConfig();
        _cameraViewport = new MapCameraViewport();

        // ── ImGui UI panels (TASK-IF008) ─────────────────────────────────────
        _debugPanelState    = new DebugPanelState(_userConfig);
        _debugPanel         = new IgDebugPanel(_debugPanelState);
        _inspectorState     = new EntityInspectorState();
        _inspectorPanel     = new EntityInspectorPanel(_inspectorState);
        _miniIosState       = new MiniIosPanelState();
        _miniIosPanel       = new MiniIosPanel(_miniIosState, _world.Bus);
        _performanceMetrics = new PerformanceMetrics();
        _performanceOverlay = new PerformanceOverlay(_performanceMetrics);
        _contextMenuSystem  = new ContextMenuSystem();
        _contextMenuPanel   = new ContextMenuPanel(_world, _contextMenuSystem, HandleContextMenuAction);

        _mapContextEntity = _world.CreateEntity();
        _world.AddComponent(_mapContextEntity, new NetworkIdentity(0));
    }

    /// <summary>
    /// Registers all modules and sets up the DDS participant (unless <paramref name="enableNetwork"/>
    /// is <c>false</c>).  Call after <see cref="InitializeEcs"/>.
    /// </summary>
    private void InitializeNetwork(bool enableNetwork, int? domainIdOverride)
    {
        _networkEnabled = enableNetwork;

        var domainId = domainIdOverride ?? IgNetworkConstants.DdsDomain;

        var tkb = BagiraEnvironment.CreateTkb();
        _world.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(tkb);

        var nodeMapper = new NodeIdMapper(
            localDomain:   domainId,
            localInstance: IgNetworkConstants.InstanceId);

        var topology = new StaticNetworkTopology(
            localNodeId: IgNetworkConstants.LocalNodeId,
            allNodes:    new[] { IgNetworkConstants.LocalNodeId });

        // A. EntityLifecycleModule — IG is a ghost node; no peers need to ACK
        var elm = new EntityLifecycleModule(tkb, Array.Empty<int>());
        _kernel.RegisterModule(elm);

        _kernel.RegisterModule(new ReplicationLogicModule());

        // B. SpawningModule — processes SpawnEntityCommand / DestroyEntityCommand
        INetworkIdAllocator idAllocator = new IgSequentialIdAllocator();
        var spawningSystem = new NetworkSpawningSystem(
            tkb, elm, _entityMap, idAllocator,
            IgNetworkConstants.LocalNodeId);
        _kernel.RegisterModule(new SpawningModule(spawningSystem));

        // E. StyleResolutionModule — writes ResolvedStyle each Simulation tick
        _kernel.RegisterModule(new StyleResolutionModule(_userConfig));

        // F. MapCullingModule — writes CullingState each PostSimulation tick
        _kernel.RegisterModule(new MapCullingModule(_cameraViewport));

        // G. HistoryTrailModule — records entity position trails (IG.4.1)
        _kernel.RegisterModule(new HistoryTrailModule());

        // H. EventEffectModule — spawns and cleans up visual effects (IG.4.2)
        if (!_headless)
            _kernel.RegisterModule(new EventEffectModule());

        // C. CycloneNetworkModule — DDS ingress/egress (optional)
        if (enableNetwork)
        {
            try
            {
                var participant = BagiraEnvironment.CreateParticipant(domainId);

                // Task 5: Create command gateway, click writer and config reader.
                _commandGateway = new BdcCommandGateway(participant);
                _clickWriter    = new DdsWriter<MapClickEvent>(participant, "MapClickEvent");
                _configReader   = new DdsReader<MapInteractionConfig>(participant);

                _geoTransform = BagiraEnvironment.CreateGeoTransform();
                _miniIosState.SetGeoTransform(_geoTransform);

                var customTranslators = new List<Fdp.Interfaces.IDescriptorTranslator>
                {
                    new EntityMasterTranslator(participant, _entityMap, _world.Bus),
                    new GeoSpatialTranslator(participant, _entityMap, _geoTransform),
                    new GeoSpatialDRTranslator(participant, _entityMap, _geoTransform),
                    new EntityInfoTranslator(participant, _entityMap, _world.Bus),
                    new EntityDamageTranslator(participant, _entityMap),
                    new MapEntitySymbolTranslator(participant, _entityMap, IgNetworkConstants.MapGroupId),
                    new ContextActionsUpdateTranslator(participant, _entityMap, _world.Bus),
                    new TimePulseTranslator(participant, _world.Bus),
                };

                if (!_headless)
                    customTranslators.Add(new FireInteractionEventTranslator(participant, _entityMap));

                var ddsAllocator = new DdsIdAllocator(participant, $"IG_{IgNetworkConstants.InstanceId}");

                var networkModule = new CycloneNetworkModule(
                    participant, nodeMapper, ddsAllocator,
                    topology, elm,
                    customTranslators: customTranslators,
                    sharedEntityMap:   _entityMap);
                _kernel.RegisterModule(networkModule);

                _networkEnabled = true;
            }
            catch (Exception ex)
            {
                FdpLog<IgApplication>.Warn("[IG] Network init failed ({0}). Running offline.", ex.Message);
                _networkEnabled = false;
            }
        }

        // D. EntityRenderLayer wired to the StubVisualizerAdapter
        var query = _world.Query()
            .With<NetworkIdentity>()
            .With<SimTransform>()
            .Build();

        var adapter   = new SstVisualizerAdapter();
        var selection = new DefaultSelectionState();
        var layer     = new EntityRenderLayer(
            "Entities", layerBitIndex: 0,
            _world, query, adapter, selection);
        _canvas.AddLayer(layer);

        // SelectionRenderSystem — PostRender overlay drawing selection rings.
        var selectionQuery  = _world.Query().With<SelectionState>().With<SimTransform>().Build();
        var selectionLayer  = new SelectionRenderSystem(_world, selectionQuery);
        _canvas.AddLayer(selectionLayer);

        // StandardInteractionTool — default canvas tool wiring selection to ECS.
        var interactionTool = new StandardInteractionTool(_world, query, adapter, selection);
        _canvas.SwitchTool(interactionTool);

        interactionTool.OnWorldClick += OnCanvasWorldClick;

        // Task 5: Wire IG-to-IOS event translators when DDS participant is ready.
        if (_networkEnabled)
        {
            interactionTool.OnWorldClick += OnCanvasClicked;
            _miniIosPanel.SetGateway(_commandGateway);
        }

        // E. SlaveTimeController — driven by TimePulse events on the event bus
        var timeController = new SlaveTimeController(_world.Bus);
        _kernel.SetTimeController(timeController);

        _kernel.RegisterGlobalSystem(new DeadReckoningSyncSystem());
        _kernel.RegisterGlobalSystem(_contextMenuSystem);

        _kernel.Initialize();
    }

    // -------------------------------------------------------------------------

    /// <summary>
    /// Advances one frame of IG logic (input, ECS tick, viewport update).  
    /// Must be called before <see cref="DrawWorld"/> and <see cref="DrawUI"/> each frame.
    /// Called by both the standalone <see cref="Run"/> loop and the embedded orchestrator.
    /// </summary>
    public void Update(float dt)
    {
        if (!_headless)
        {
            // Gate map input when ImGui is consuming the mouse (TASK-IF008).
            if (!ImGui.GetIO().WantCaptureMouse)
            {
                HandleCameraInput(dt);
                _canvas.Update(dt);
            }

            // Project screen corners to world space and feed MapCullingSystem.
            var topLeft     = _camera.ScreenToWorld(Vector2.Zero);
            var bottomRight = _camera.ScreenToWorld(new Vector2(WindowWidth, WindowHeight));
            _cameraViewport.WorldMinX = MathF.Min(topLeft.X, bottomRight.X);
            _cameraViewport.WorldMaxX = MathF.Max(topLeft.X, bottomRight.X);
            _cameraViewport.WorldMinY = MathF.Min(topLeft.Y, bottomRight.Y);
            _cameraViewport.WorldMaxY = MathF.Max(topLeft.Y, bottomRight.Y);
            _cameraViewport.Zoom      = _camera.Zoom;
        }

        // Always tick ECS/network — even in headless mode DDS messages must be processed.
        _kernel.Update();

        // Task 5: Poll IOS → IG interaction-config updates (active context ID).
        if (_networkEnabled && _configReader != null)
        {
            using var loan = _configReader.Take(1);
            foreach (var sample in loan)
            {
                if (!sample.IsValid) continue;
                _activeContextId = sample.Data.ActiveContextId;
                FdpLog<IgApplication>.Debug(
                    "[TRACE-IG] MapInteractionConfig: ActiveContextId={0}", _activeContextId);
            }
        }

        if (!_headless)
        {
            // Update UI panel states (TASK-IF008).
            _performanceMetrics.Snapshot(_world, Raylib.GetFPS(), Raylib.GetFrameTime() * 1000f);
            _inspectorState.Refresh(_world, GetSelectedEntity());
        }
    }

    /// <summary>
    /// Renders the 2-D map canvas and debug overlay.  
    /// Must be called inside <c>Raylib.BeginDrawing()</c>, before ImGui.
    /// No-op in headless mode.
    /// </summary>
    public void DrawWorld()
    {
        _canvas.Draw();
        DrawDebugOverlay();
    }

    /// <summary>
    /// Renders ImGui panels.  
    /// Must be called inside <c>rlImGui.Begin()</c>.
    /// No-op in headless mode.
    /// </summary>
    public void DrawUI()
    {
        _debugPanel.Draw();
        _inspectorPanel.Draw();
        _miniIosPanel.Draw();
        _performanceOverlay.Draw();
        _contextMenuPanel.Draw();
    }

    /// <summary>
    /// Runs the standalone main loop (owns window lifecycle).
    /// Uses <see cref="Update"/>, <see cref="DrawWorld"/>, and <see cref="DrawUI"/> internally.
    /// </summary>
    public void Run()
    {
        while (!Raylib.WindowShouldClose())
        {
            float dt = Raylib.GetFrameTime();
            Update(dt);

            Raylib.BeginDrawing();
            Raylib.ClearBackground(Color.DarkGray);
            DrawWorld();

            rlImGui.Begin();
            DrawUI();
            rlImGui.End();

            Raylib.EndDrawing();
        }
    }

    // -------------------------------------------------------------------------

    /// <summary>
    /// Processes keyboard camera controls (arrow-key pan, +/- zoom).
    /// Middle-mouse drag pan is handled automatically by MapCanvas/MapCamera.
    /// Mouse-wheel zoom is also handled by MapCanvas/MapCamera via RaylibInputProvider.
    /// </summary>
    private void HandleCameraInput(float dt)
    {
        // --- Arrow-key panning ---
        // panDir is in screen space: Up arrow = -Y (screen Y goes down),
        // which scrolls the view upward as the user expects.
        Vector2 panDir = Vector2.Zero;
        if (Raylib.IsKeyDown(KeyboardKey.Right)) panDir.X += 1f;
        if (Raylib.IsKeyDown(KeyboardKey.Left))  panDir.X -= 1f;
        if (Raylib.IsKeyDown(KeyboardKey.Up))    panDir.Y -= 1f; // screen-up = -Y
        if (Raylib.IsKeyDown(KeyboardKey.Down))  panDir.Y += 1f; // screen-down = +Y

        if (panDir != Vector2.Zero)
        {
            // Accumulate displacement into our tracked target so that multiple
            // consecutive key frames add up correctly, even while the camera is
            // still interpolating toward a prior target.
            _keyboardPanTarget +=
                panDir * IgCameraConstants.ArrowKeyPanSpeedMetersPerSecond * dt;
            _camera.FocusOn(_keyboardPanTarget);
        }
        else
        {
            // Re-sync anchor to current interpolated camera position whenever
            // no arrow key is held, so the next key-press continues from wherever
            // the user has navigated (including via mouse drag).
            _keyboardPanTarget = _camera.Target;
        }

        // --- Keyboard zoom (+/=  and  -  keys) ---
        // Simulate a single wheel tick so the same 1.2× factor is applied.
        bool zoomIn  = Raylib.IsKeyPressed(KeyboardKey.Equal)
                    || Raylib.IsKeyPressed(KeyboardKey.KpAdd);
        bool zoomOut = Raylib.IsKeyPressed(KeyboardKey.Minus)
                    || Raylib.IsKeyPressed(KeyboardKey.KpSubtract);

        Vector2 mousePos = Raylib.GetMousePosition();
        if (zoomIn)  _camera.ProcessInput(1.0f,  mousePos, false, false);
        if (zoomOut) _camera.ProcessInput(-1.0f, mousePos, false, false);
    }

    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns the first entity in the ECS that has a <see cref="SelectionState"/>
    /// with <see cref="SelectionState.IsSelected"/> or
    /// <see cref="SelectionState.IsPrimarySelection"/> set to <c>true</c>.
    /// Returns <see cref="Entity.Null"/> when nothing is selected.
    /// </summary>
    private Entity GetSelectedEntity()
    {
        var q = _world.Query().With<SelectionState>().Build();
        foreach (var entity in q)
        {
            var state = _world.GetComponent<SelectionState>(entity);
            if (state.IsSelected || state.IsPrimarySelection)
                return entity;
        }
        return Entity.Null;
    }

    // -------------------------------------------------------------------------

    /// <summary>Draws camera state and cursor coordinates in screen space (outside Camera.BeginMode).</summary>
    private void DrawDebugOverlay()
    {
        Vector2 worldMousePos = _camera.ScreenToWorld(Raylib.GetMousePosition());

        int y = DebugMarginY;

        Raylib.DrawText(
            $"Camera: ({_camera.Target.X:F1}, {_camera.Target.Y:F1}) m",
            DebugMarginX, y, DebugFontSize, Color.White);
        y += DebugLineHeight;

        Raylib.DrawText(
            $"Zoom: {_camera.Zoom:F4} px/m  ({1f / _camera.Zoom:F2} m/px)",
            DebugMarginX, y, DebugFontSize, Color.White);
        y += DebugLineHeight;

        Raylib.DrawText(
            $"Mouse World: ({worldMousePos.X:F1}, {worldMousePos.Y:F1}) m",
            DebugMarginX, y, DebugFontSize, Color.White);
    }

    // -------------------------------------------------------------------------

    /// <summary>
    /// Releases all IG resources.  
    /// Pass <c>ownsWindow = false</c> when the orchestrator owns the Raylib window.
    /// </summary>
    public void Shutdown(bool ownsWindow = true)
    {
        _commandGateway?.Dispose();
        _clickWriter?.Dispose();
        _configReader?.Dispose();
        _kernel?.Dispose();
        if (ownsWindow)
        {
            rlImGui.Shutdown();
            Raylib.CloseWindow();
        }
    }

    // ── Task 5: IG-to-IOS event translators ──────────────────────────────────

    /// <summary>
    /// Internal test hook to simulate a map click without Raylib input.
    /// </summary>
    internal void TestHook_SimulateMapClick(Vector2 worldPos)
        => OnCanvasClicked(worldPos, MouseButton.Left, false, false, Entity.Null);

    /// <summary>
    /// Internal test hook to expose the latest interaction context ID.
    /// </summary>
    internal Guid TestHook_ActiveContextId => _activeContextId;

    /// <summary>
    /// Converts a canvas world-click to a <see cref="MapClickEvent"/> and writes it
    /// to the DDS "MapClickEvent" topic so IOS can route the interaction.
    /// No-op when network is disabled.
    /// </summary>
    private void OnCanvasClicked(Vector2 worldPos, MouseButton button, bool shift, bool ctrl, Entity hit)
    {
        if (!_networkEnabled || _clickWriter == null || _geoTransform == null)
            return;

        var (lat, lon, alt) = _geoTransform.ToGeodetic(new Vector3(worldPos.X, worldPos.Y, 0f));

        var evt = new MapClickEvent
        {
            MapId                = IgNetworkConstants.InstanceId,
            Position             = new GeoPosition { Latitude = lat, Longitude = lon, Altitude = alt },
            InteractionContextId = _activeContextId,
            HitStack             = new List<MapObjectRef>(),
        };

        _clickWriter.Write(evt);
        FdpLog<IgApplication>.Info("[IG] MapClickEvent published. ContextId={0}", _activeContextId);
    }

    private void OnCanvasWorldClick(Vector2 worldPos, MouseButton button, bool shift, bool ctrl, Entity hit)
    {
        if (button != MouseButton.Right)
            return;

        var targetEntity = hit != Entity.Null ? hit : _mapContextEntity;
        var mousePos = Raylib.GetMousePosition();

        _contextMenuSystem.RequestOpen(targetEntity, mousePos.X, mousePos.Y);
    }

    private void HandleContextMenuAction(Entity entity, ContextAction action)
    {
        if (action.ActionName.StartsWith("IG_", StringComparison.Ordinal))
        {
            ExecuteLocalContextAction(entity, action.ActionName);
            return;
        }

        int networkId = 0;
        var view = (ISimulationView)_world;
        if (view.HasComponent<NetworkIdentity>(entity))
        {
            ref readonly var id = ref view.GetComponentRO<NetworkIdentity>(entity);
            networkId = (int)id.Value;
        }

        _world.Bus.PublishManaged(new ContextActionTriggered
        {
            EntityNetworkId = networkId,
            ActionName = action.ActionName
        });
    }

    private void ExecuteLocalContextAction(Entity entity, string actionName)
    {
        switch (actionName)
        {
            case "IG_Center":
            case "IG_CenterOnEntity":
                CenterCameraOn(entity);
                break;
            default:
                FdpLog<IgApplication>.Warn("[IG] Unhandled local context action: {0}", actionName);
                break;
        }
    }

    private void CenterCameraOn(Entity entity)
    {
        var view = (ISimulationView)_world;
        if (!view.HasComponent<SimTransform>(entity))
            return;

        ref readonly var transform = ref view.GetComponentRO<SimTransform>(entity);
        var target = new Vector2(transform.Position.X, transform.Position.Y);
        _keyboardPanTarget = target;
        _camera.FocusOn(target);
    }
}
