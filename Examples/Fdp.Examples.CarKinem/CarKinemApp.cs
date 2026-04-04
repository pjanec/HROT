using System;
using System.Numerics;
using Raylib_cs;
using ImGuiNET;
using Fdp.Kernel;
using FDP.Framework.Raylib;
using FDP.Toolkit.Vis2D;
using FDP.Toolkit.Vis2D.Layers;
using FDP.Toolkit.ImGui.Panels;
using Fdp.Examples.CarKinem.Visualization;
using Fdp.Examples.CarKinem.Core;
using Fdp.Examples.CarKinem.Components;
using Fdp.Examples.CarKinem.UI; // UI Panels
using CarKinem.Core;
using CarKinem.Road;
using CarKinem.Spatial;
using CarKinem.Formation;
using CarKinem.Commands;
using CarKinem.Systems;
using CarKinem.Trajectory;
using ModuleHost.Core;
using ModuleHost.Core.Abstractions;
using ModuleHost.Core.Time; // ITimeController
using FDP.Toolkit.Time.Controllers;
using Fdp.Kernel.FlightRecorder; // Recorder

using FDP.Toolkit.Vis2D.Tools; // Added Tools namespace

namespace Fdp.Examples.CarKinem;

public class CarKinemApp : FdpApplication
{
    // Simulation Core
    private EntityRepository _repository = null!;
    private ModuleHostKernel _kernel = null!;
    private EventAccumulator _eventAccumulator = null!;
    private EntityQuery _vehicleQuery = null!; // Promoted to field for Tools
    
    // Tools
    private StandardInteractionTool _interactionTool = null!;
    
    // Time & Recording (Restored)
    private ITimeController _activeTimeController = null!;
    private ITimeController _continuousTime = null!;
    private SteppingTimeController _steppingTime = null!;
    private AsyncRecorder? _recorder;
    private PlaybackController? _playback;
    
    // Systems
    private SpatialHashSystem _spatialSystem = null!;
    private FormationTargetSystem _formationSystem = null!;
    private VehicleCommandSystem _commandSystem = null!;
    private CarKinematicsSystem _kinematicsSystem = null!;

    // Managers / Resources
    private RoadNetworkBlob _roadNetwork = new();
    private TrajectoryPoolManager _trajectoryPool = null!;
    private FormationTemplateManager _formationTemplates = null!;
    private ScenarioManager _scenarioManager = null!;
    // private InputManager _inputManager = null!; // Removed

    // Visualization
    private MapCanvas _map = null!;
    private VehicleVisualizer _vehicleVisualizer = null!;
    private SelectionManager _selectionManager = null!;
    private CarKinemInspectorAdapter _inspectorAdapter = null!;
    
    // UI Panels (Restored)
    private MainUI _legacyUI = null!;
    
    // Systems List for Profiling
    private List<Fdp.Kernel.ComponentSystem> _systems = new();

    // App State (Removed local state, using UIState from MainUI)

    public CarKinemApp() : base(new ApplicationConfig 
    { 
        Width = 1280, 
        Height = 720, 
        WindowTitle = "FDP CarKinem (Refactored)",
        TargetFPS = 60,
        Flags = ConfigFlags.ResizableWindow | ConfigFlags.Msaa4xHint
    }) 
    { 
    }

    protected override void OnLoad()
    {
        // 1. Core Simulation Setup
        _repository = new EntityRepository();
        _eventAccumulator = new EventAccumulator();
        _kernel = new ModuleHostKernel(_repository, _eventAccumulator);
        
        // Register Components
        RegisterComponents();
        
        // Singleton Time
        _repository.RegisterComponent<GlobalTime>();
        _repository.SetSingletonUnmanaged(new GlobalTime());

        // Load Road Network
        _roadNetwork = new RoadNetworkBlob();
        try {
             _roadNetwork = RoadNetworkLoader.LoadFromJson("Assets/sample_road.json");
        } catch {
             // Ignore or log
        }
        
        // Create Managers
        _trajectoryPool = new TrajectoryPoolManager();
        _formationTemplates = new FormationTemplateManager();
        
        // Initialize Systems
        _spatialSystem = new SpatialHashSystem();
        _formationSystem = new FormationTargetSystem(_formationTemplates, _trajectoryPool);
        _commandSystem = new VehicleCommandSystem();
        _kinematicsSystem = new CarKinematicsSystem(_roadNetwork, _trajectoryPool);
        
        // Initialize Kernel Time
        var timeConfig = new TimeControllerConfig { Role = TimeRole.Standalone };
        
        // create continuous controller (via factory or directly)
        _continuousTime = TimeControllerFactory.Create(_repository.Bus, timeConfig);
        
        // create stepping controller (manual instantiation as it requires seed state later, 
        // but we can init with default and seed on switch)
        _steppingTime = new SteppingTimeController(new GlobalTime());

        // Use Switchable Proxy to avoid ModuleHostKernel exception on swapping
        _activeTimeController = _continuousTime;
        _kernel.SetTimeController(_activeTimeController); 
        _kernel.Initialize();
        
        // Create Systems in Repo
        _spatialSystem.Create(_repository);
        _formationSystem.Create(_repository);
        _commandSystem.Create(_repository);
        _kinematicsSystem.Create(_repository);
        
        // Add to list for profiler
        _systems.Add(_spatialSystem);
        _systems.Add(_formationSystem);
        _systems.Add(_commandSystem);
        _systems.Add(_kinematicsSystem);

        // Scenario Manager
        _scenarioManager = new ScenarioManager(_repository, _roadNetwork, _trajectoryPool, _formationTemplates);
        _scenarioManager.SpawnFastOne(); // Initial Spawn
        
        // 2. Visualization Setup
        _map = new MapCanvas();
        _map.AddResource(_trajectoryPool);
        var roadLayer = new RoadMapLayer(_roadNetwork);
        _map.AddLayer(roadLayer);
        
        _vehicleVisualizer = new VehicleVisualizer();
        _selectionManager = new SelectionManager(); 
        _inspectorAdapter = new CarKinemInspectorAdapter(_selectionManager, _repository);
        
        _vehicleQuery = _repository.Query()
            .With<VehicleState>()
            .With<VehicleParams>()
            .Build();
            
        var vehicleLayer = new EntityRenderLayer(
            "Vehicles",     
            0,              
            _repository, 
            _vehicleQuery, 
            _vehicleVisualizer, 
            _inspectorAdapter
        );
        _map.AddLayer(vehicleLayer);
        
        var trajectoryLayer = new TrajectoryMapLayer(_trajectoryPool, _repository, _inspectorAdapter);
        _map.AddLayer(trajectoryLayer);
        
        // 3. UI & Input
        _legacyUI = new MainUI();
        
        // --- Tool Setup ---
        _interactionTool = new StandardInteractionTool(_repository, _vehicleQuery, _vehicleVisualizer);
        
        // 1. Selection Interaction
        _interactionTool.OnEntitySelectRequest += (entity, augment) =>
        {
            if (_repository.IsAlive(entity))
            {
                if (augment)
                    _selectionManager.Add(entity);
                else
                    _selectionManager.Set(entity);
            }
            else if (!augment)
            {
                _selectionManager.Clear();
            }
        };

        // 2. Drag Interaction
        _interactionTool.OnEntityMoved += (entity, pos) =>
        {
            // Update simulation state
            if (_repository.HasComponent<SimTransform>(entity))
            {
                 ref var tf = ref _repository.GetComponentRW<SimTransform>(entity);
                 tf.Position = new Vector3(pos.X, pos.Y, 0);
                 
                 // Also reset velocity?
                 if (_repository.HasComponent<VehicleState>(entity))
                 {
                     ref var state = ref _repository.GetComponentRW<VehicleState>(entity);
                     state.Speed = 0.0f;
                 }
            }
        };

        // 3. Generic Interaction (Context Menu / Actions)
        _interactionTool.OnWorldClick += (pos, btn, shift, ctrl, hit) =>
        {
             if (btn == MouseButton.Right)
             {
                 var entities = _selectionManager.SelectedEntities;

                 if (entities.Count == 0) return;

                 if (shift)
                 {
                     // Add Waypoint (Shift+Right) -> Sequence
                     var mode = _legacyUI?.UIState?.InterpolationMode ?? global::CarKinem.Trajectory.TrajectoryInterpolation.CatmullRom;
                     foreach(var e in entities)
                     {
                         if (_repository.IsAlive(e))
                            _scenarioManager.AddWaypoint(e, pos, mode);
                     }
                 }
                 else
                 {
                     // Context (Navigate) -> Write NavState directly (Cmd bus removed)
                     foreach(var e in entities)
                     {
                        if (!_repository.IsAlive(e)) continue;
                        if (!_repository.HasComponent<NavState>(e)) continue;

                        var nav = _repository.GetComponent<NavState>(e);
                        nav.Mode             = KinematicsMode.None;
                        nav.FinalDestination = pos;
                        nav.ArrivalRadius    = 3.0f;
                        nav.TargetSpeed      = 10.0f;
                        nav.HasArrived       = 0;
                        _repository.SetComponent(e, nav);
                     }
                 }
             }
        };

        // 2. Region Select
        _interactionTool.OnRegionSelected += (entities) =>
        {
             // Assumes Replace logic for now
             _selectionManager.SetMultiple(entities);
        };
        
        // 3. Drag
        _interactionTool.OnEntityMoved += (entity, newPos) =>
        {
            if (_repository.HasComponent<SimTransform>(entity))
            {
                ref var tf = ref _repository.GetComponentRW<SimTransform>(entity);
                tf.Position = new Vector3(newPos.X, newPos.Y, 0);
            }
        };
        
        _map.SwitchTool(_interactionTool);
        
        // Input Manager removed
        // _inputManager = new InputManager();
        // _inputManager.EnableCameraControl = false; // MapCanvas handles it
    }


    private void RegisterComponents()
    {
        _repository.RegisterComponent<SimTransform>();
        _repository.RegisterComponent<SimVelocity>();
        _repository.RegisterComponent<VehicleState>();
        _repository.RegisterComponent<VehicleParams>();
        _repository.RegisterComponent<NavState>();
        _repository.RegisterComponent<FormationMember>();
        _repository.RegisterComponent<FormationRoster>();
        _repository.RegisterComponent<FormationTarget>();
        _repository.RegisterComponent<VehicleColor>();
        // _repository.RegisterComponent<NavigationPath>(); // Removed: Trajectories are stored in Pool
        
        // Commands
        _repository.RegisterEvent<CmdSpawnVehicle>();
        _repository.RegisterEvent<CmdCreateFormation>();
        _repository.RegisterEvent<CmdJoinFormation>();
        _repository.RegisterEvent<CmdLeaveFormation>();
    }

    protected override void OnUpdate(float dt)
    {
        // Global Input Handling (Delete)
        if (Raylib.IsKeyPressed(KeyboardKey.Delete)) 
        {
             var toDelete = _selectionManager.SelectedEntities.ToList();
             if (toDelete.Count > 0)
             {
                 foreach(var e in toDelete)
                 {
                     if (_repository.IsAlive(e))
                         _repository.DestroyEntity(e);
                 }
                 _selectionManager.Clear();
             }
        }

        // --- 1. Handle Input (Pause/Record/Replay) ---
        
        // Pause/Play Logic (Time Mode Switching)
        bool isPaused = _legacyUI.IsPaused;
        // Use proxy to get active
        var currentController = _activeTimeController;
        
        if (isPaused && currentController != _steppingTime)
        {
            // Switch to Deterministic (Stepping)
            _steppingTime.SeedState(currentController.GetCurrentState());
            _activeTimeController = _steppingTime;
            _kernel.SetTimeController(_steppingTime);
            Console.WriteLine("Switched to Deterministic Time (Paused)");
        }
        else if (!isPaused && currentController != _continuousTime)
        {
            // Switch to Continuous
            _continuousTime.SeedState(currentController.GetCurrentState());
            _activeTimeController = _continuousTime;
            _kernel.SetTimeController(_continuousTime);
            Console.WriteLine("Switched to Continuous Time (Playing)");
        }
        
        // Update Time Scale
        _kernel.GetTimeController().SetTimeScale(_legacyUI.TimeScale);
        
        // Handle Recording Toggle
        if (_legacyUI.ConsumeRecordingToggle())
        {
            if (_recorder == null)
            {
                // Start Recording
                string filename = $"recording_{DateTime.Now:yyyyMMdd_HHmmss}.fdprec";
                _recorder = new AsyncRecorder(filename);
                _legacyUI.IsRecording = true;
                Console.WriteLine($"Started recording to {filename}");
            }
            else
            {
                // Stop Recording
                _recorder.Dispose();
                _recorder = null;
                _legacyUI.IsRecording = false;
                Console.WriteLine("Stopped recording");
            }
        }
        
        // Handle Replay Toggle
        if (_legacyUI.ConsumeReplayToggle())
        {
             if (_playback == null)
             {
                 // Start Replay (Loads most recent or hardcoded for now)
                 // Find most recent .fdprec
                 var files = System.IO.Directory.GetFiles(".", "*.fdprec");
                 if (files.Length > 0)
                 {
                     // FIX: Stop recording if active before starting replay
                     if (_recorder != null)
                     {
                         _recorder.Dispose();
                         _recorder = null;
                         _legacyUI.IsRecording = false;
                         Console.WriteLine("Stopped recording strictly before playback");
                     }
                     
                     // Clear current entities to avoid conflicts/ghosts
                     _scenarioManager.ClearAll();
                     
                     var lastFile = files.OrderByDescending(f => f).First();
                     _playback = new PlaybackController(lastFile);
                     // Pause simulation for replay control? Or let playback drive?
                     // Usually Playback replaces Kernel updates.
                     _legacyUI.IsReplaying = true;
                     _legacyUI.IsPaused = true; // Pause sim so we don't conflict
                     Console.WriteLine($"Started replay of {lastFile}");
                 }
             }
             else
             {
                 _playback.Dispose();
                 _playback = null;
                 _legacyUI.IsReplaying = false;
                 Console.WriteLine("Stopped replay");
             }
        }

        // --- 2. Simulation Step ---

        bool shouldStep = false;

        // A. ALWAYS Update Kernel (Time & Event Buffer Swap)
        _kernel.Update();

        // B. ALWAYS Run Logic Systems (Command, Formation)
        // These systems consume input events from buffers (swapped above).
        // If we don't run them, next frame properties will be lost.
        // They only set intentions/state, not physics.
        // (Unless in Replay Mode, where we might want to disable them)
        if (_playback == null)
        {
            _commandSystem.Run();
            _formationSystem.Run();
        }

        // C. Conditional Physics/Stepping Logic
        // If Replaying, we drive from Playback
        if (_playback != null)
        {
            // If not paused, OR if step requested, advance frame
            if (!_legacyUI.IsPaused || _legacyUI.ConsumeStepRequest())
            {
                if (!_playback.StepForward(_repository))
                {
                    // End of recording
                    _legacyUI.IsPaused = true;
                    // Log
                }
            }
        }
        else
        {
            // Normal Simulation Logic
            if (_legacyUI.ConsumeStepRequest())
            {
                // Single Step: Force SteppingController to advance
                 _steppingTime.Step(1.0f / 60.0f);
                 shouldStep = true;
            }
            else if (!isPaused)
            {
                // Continuous Run already updated time in _kernel.Update()
                shouldStep = true;
            }
        }

        // --- 3. Physics & Integration Execution ---
        
        if (shouldStep)
        {
            // Run Systems that integrate/modify physics state
            _spatialSystem.Run();
            // _formationSystem.Run(); // Moved strictly to always run (Logic)
            // _commandSystem.Run();   // Moved strictly to always run (Logic)
            _kinematicsSystem.Run();
            
            _scenarioManager.Update();
            
            // Record Frame if Recording
            if (_recorder != null)
            {
                var time = _kernel.GetTimeController().GetCurrentState();
                
                // Use frame-locked wall clock: TotalWallTicks if populated, else sample at call-site.
                long wallTicks = time.TotalWallTicks != 0
                    ? time.TotalWallTicks
                    : DateTime.UtcNow.Ticks;

                // Capture Keyframe every 60 frames
                if (time.FrameNumber % 60 == 0)
                {
                    _recorder.CaptureKeyframe(_repository, wallTicks);
                }
                
                // prevTick is previous frame index
                uint prevTick = (uint)Math.Max(0, time.FrameNumber - 1);
                _recorder.CaptureFrame(_repository, prevTick, wallTicks);
            }
        }

        // 4. Input Manager (Right Click -> Navigate) REMOVED
        // _inputManager.HandleInput(_selectionManager, _scenarioManager, _map.Camera.InnerCamera, _legacyUI.UIState);
        
        // --- Tool Interactions (Draw Path) ---
        // Press P to draw path for selected entity
        if (Raylib.IsKeyPressed(KeyboardKey.P) && _selectionManager.SelectedEntity.HasValue)
        {
            var entity = _selectionManager.SelectedEntity.Value;
            var pathTool = new PointSequenceTool(points => 
            {
                if (points.Length > 0)
                {
                    // Call ScenarioManager SetDestination/AddWaypoint sequence
                    // Clear path by calling SetDestination with first point
                    _scenarioManager.SetDestination(entity, points[0], _legacyUI.UIState.InterpolationMode);
                    
                    // Add remaining points
                    for (int i = 1; i < points.Length; i++)
                    {
                        _scenarioManager.AddWaypoint(entity, points[i], _legacyUI.UIState.InterpolationMode);
                    }
                }
                
                // Switch back to default
                _map.PopTool();
            });
            _map.PushTool(pathTool);
        }
        else if (Raylib.IsKeyPressed(KeyboardKey.Escape))
        {
             // Reset tool
             _map.SwitchTool(_interactionTool);
        }

        // 5. Map Update (Camera/Zoom/Layers)
        _map.Update(dt);
    }

    protected override void OnDrawWorld()
    {
        _map.Draw();
    }

    protected override void OnDrawUI()
    {
        // Render the restored UI Panels
        _legacyUI.Render(_repository, _kernel, _scenarioManager, _inspectorAdapter, _systems, _playback); 
    }

    protected override void OnUnload()
    {
        // Cleanup
        _recorder?.Dispose();
        _playback?.Dispose();
        
        _spatialSystem?.Dispose();
        _kinematicsSystem?.Dispose();
        _roadNetwork.Dispose();
        _trajectoryPool?.Dispose();
        _formationTemplates?.Dispose();
        _kernel?.Dispose();
        _repository?.Dispose();
    }
}
