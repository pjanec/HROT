using System;
using System.Collections.Generic;
using System.Numerics;
using Bagira.BDC.SSTD;
using Bagira.IG.Adapters;
using Bagira.IG.Modules;
using Bagira.IG.Translators;
using CycloneDDS.Runtime;
using Fdp.Kernel;
using Fdp.Modules.Geographic.Transforms;
using FDP.Toolkit.Lifecycle;
using FDP.Toolkit.NetworkSpawning.Systems;
using FDP.Toolkit.Replication;
using FDP.Toolkit.Replication.Services;
using FDP.Toolkit.Time.Controllers;
using Fdp.Toolkit.Tkb;
using FDP.Toolkit.Vis2D;
using FDP.Toolkit.Vis2D.Abstractions;
using FDP.Toolkit.Vis2D.Components;
using FDP.Toolkit.Vis2D.Defaults;
using FDP.Toolkit.Vis2D.Layers;
using ModuleHost.Core;
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
    private FdpEventBus      _eventBus = null!;
    private NetworkEntityMap _entityMap = null!;

    // ── Network enabled flag — false when DDS libraries are unavailable (e.g. unit-test host)
    private bool _networkEnabled;

    // -------------------------------------------------------------------------

    public void Initialize()
    {
        Raylib.InitWindow(WindowWidth, WindowHeight, WindowTitle);
        Raylib.SetTargetFPS(TargetFps);

        rlImGui.Setup(darkTheme: true);

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
        InitializeNetwork(enableNetwork: true);
    }

    // -------------------------------------------------------------------------

    /// <summary>
    /// Initialises the ECS world and kernel (no DDS — safe to call in tests).
    /// </summary>
    private void InitializeEcs()
    {
        _world     = new EntityRepository();
        _eventBus  = new FdpEventBus();
        _entityMap = new NetworkEntityMap();

        var accumulator = new EventAccumulator();
        _kernel         = new ModuleHostKernel(_world, accumulator);
    }

    /// <summary>
    /// Registers all modules and sets up the DDS participant (unless <paramref name="enableNetwork"/>
    /// is <c>false</c>).  Call after <see cref="InitializeEcs"/>.
    /// </summary>
    private void InitializeNetwork(bool enableNetwork)
    {
        _networkEnabled = enableNetwork;

        var tkb      = new TkbDatabase();
        _world.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(tkb);

        var nodeMapper = new NodeIdMapper(
            localDomain:   IgNetworkConstants.DdsDomain,
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
        DisTypeExtractor disExtractor = (object c, out ulong dis) =>
        {
            if (c is EntityMaster m) { dis = m.DisType; return true; }
            dis = 0; return false;
        };

        var spawningSystem = new NetworkSpawningSystem(
            tkb, elm, _entityMap, idAllocator,
            IgNetworkConstants.LocalNodeId, disExtractor);
        _kernel.RegisterModule(new SpawningModule(spawningSystem));

        // C. CycloneNetworkModule — DDS ingress/egress (optional)
        if (enableNetwork)
        {
            try
            {
                var participant = new DdsParticipant(domainId: IgNetworkConstants.DdsDomain);

                var geoTransform = new WGS84Transform();
                geoTransform.SetOrigin(
                    IgNetworkConstants.GeoOriginLatDeg,
                    IgNetworkConstants.GeoOriginLonDeg,
                    IgNetworkConstants.GeoOriginAltMeters);

                var customTranslators = new List<Fdp.Interfaces.IDescriptorTranslator>
                {
                    new EntityMasterTranslator(participant, _entityMap, _eventBus),
                    new GeoSpatialTranslator(participant, _entityMap, geoTransform),
                    new EntityInfoTranslator(participant, _entityMap, _eventBus),
                    new TimePulseTranslator(participant, _eventBus),
                };

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
                Console.Error.WriteLine($"[IG] Network init failed ({ex.Message}). Running offline.");
                _networkEnabled = false;
            }
        }

        // D. EntityRenderLayer wired to the StubVisualizerAdapter
        var query = _world.Query()
            .With<EntityMaster>()
            .With<SimTransform>()
            .Build();

        var adapter   = new StubVisualizerAdapter();
        var selection = new DefaultSelectionState();
        var layer     = new EntityRenderLayer(
            "Entities", layerBitIndex: 0,
            _world, query, adapter, selection);
        _canvas.AddLayer(layer);

        // E. SlaveTimeController — driven by TimePulse events on the event bus
        var timeController = new SlaveTimeController(_eventBus);
        _kernel.SetTimeController(timeController);

        _kernel.Initialize();
    }

    // -------------------------------------------------------------------------

    public void Run()
    {
        while (!Raylib.WindowShouldClose())
        {
            float dt = Raylib.GetFrameTime();

            HandleCameraInput(dt);
            _canvas.Update(dt);

            // Tick ECS/network each render frame
            _kernel.Update();
            _eventBus.SwapBuffers();

            Raylib.BeginDrawing();
            Raylib.ClearBackground(Color.DarkGray);
            _canvas.Draw();
            DrawDebugOverlay();
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

    public void Shutdown()
    {
        _kernel?.Dispose();
        _eventBus?.Dispose();
        rlImGui.Shutdown();
        Raylib.CloseWindow();
    }
}
