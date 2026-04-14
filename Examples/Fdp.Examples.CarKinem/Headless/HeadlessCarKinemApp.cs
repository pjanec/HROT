using System;
using System.Collections.Generic;
using Fdp.Kernel;
using Fdp.ModuleHost;
using Fdp.ModuleHost.Abstractions;
using Fdp.ModuleHost.Time;
using Fdp.Toolkit.Time.Controllers;
using Fdp.Kernel.FlightRecorder;
using CarKinem.Core;
using CarKinem.Road;
using CarKinem.Formation;
using Fdp.Examples.CarKinem.Core; // For ScenarioManager
using CarKinem.Systems;
using CarKinem.Commands;
using Fdp.Examples.CarKinem.Components; // VehicleState etc if local?
using CarKinem.Trajectory;
using Fdp.Kernel.Collections;
using System.Numerics;

namespace Fdp.Examples.CarKinem.Headless
{
    public class HeadlessCarKinemApp : IDisposable
    {
        // Exposed Kernel / Repos
        public EntityRepository Repository { get; private set; } = null!;
        public ModuleHostKernel Kernel { get; private set; } = null!;
        
        // Time Control
        public ITimeController TimeController { get; private set; } = null!;
        public SteppingTimeController SteppingTime { get; private set; } = null!;
        public ITimeController ContinuousTime { get; private set; } = null!;
        
        // Recorder / Playback
        public AsyncRecorder? Recorder { get; private set; }
        public PlaybackController? Playback { get; private set; }
        
        // Systems
        private SpatialHashSystem _spatialSystem = null!;
        private FormationTargetSystem _formationSystem = null!;
        private VehicleCommandSystem _commandSystem = null!;
        private CarKinematicsSystem _kinematicsSystem = null!;
        
        private ScenarioManager _scenarioManager = null!;
        private TrajectoryPoolManager _trajectoryPool = null!;
        private FormationTemplateManager _formationTemplates = null!;
        private RoadNetworkBlob _roadNetwork = new();

        public void Initialize()
        {
            // 1. Core Setup
            Repository = new EntityRepository();
            var eventAccumulator = new EventAccumulator();
            Kernel = new ModuleHostKernel(Repository, eventAccumulator);
            
            RegisterComponents();
            
            Repository.RegisterComponent<GlobalTime>();
            Repository.SetSingletonUnmanaged(new GlobalTime());
            
            SetupDummyRoad();

            // Managers
            _trajectoryPool = new TrajectoryPoolManager();
            _formationTemplates = new FormationTemplateManager();
            _scenarioManager = new ScenarioManager(Repository, _roadNetwork, _trajectoryPool, _formationTemplates);
            
            // Systems
            _spatialSystem = new SpatialHashSystem();
            _formationSystem = new FormationTargetSystem(_formationTemplates, _trajectoryPool);
            _commandSystem = new VehicleCommandSystem();
            _kinematicsSystem = new CarKinematicsSystem(_trajectoryPool);
            
            // Initialize Systems
            _spatialSystem.Create(Repository);
            _formationSystem.Create(Repository);
            _commandSystem.Create(Repository);
            _kinematicsSystem.Create(Repository);
            
            // Time Setup
            var timeConfig = new TimeControllerConfig { Role = TimeRole.Standalone };
            ContinuousTime = TimeControllerFactory.Create(Repository.Bus, timeConfig);
            SteppingTime = new SteppingTimeController(new GlobalTime());
            
            // Use ContinuousTime directly (SwitchableTimeController removed).
            TimeController = ContinuousTime; 
            
            Kernel.SetTimeController(TimeController);
            Kernel.Initialize();
        }

        private void SetupDummyRoad()
        {
            // Simple 2-node road
            _roadNetwork.Nodes = new NativeArray<RoadNode>(2, Allocator.Persistent);
            _roadNetwork.Nodes[0] = new RoadNode { Position = new Vector2(0, 0), SegmentCount = 1, FirstSegmentIndex = 0 };
            _roadNetwork.Nodes[1] = new RoadNode { Position = new Vector2(1000, 0), SegmentCount = 0, FirstSegmentIndex = -1 };
            
            _roadNetwork.Segments = new NativeArray<RoadSegment>(1, Allocator.Persistent);
            _roadNetwork.Segments[0] = new RoadSegment 
            { 
                P0 = new Vector2(0,0),
                P1 = new Vector2(1000,0),
                T0 = new Vector2(1000,0),
                T1 = new Vector2(1000,0),
                Length = 1000.0f,
                StartNodeIndex = 0, 
                EndNodeIndex = 1, 
                LaneWidth = 3.5f, 
                SpeedLimit = 30.0f 
            };
            
            // Dummy grid to avoid crashes if referenced
            _roadNetwork.GridHead = new NativeArray<int>(0, Allocator.Persistent);
            _roadNetwork.GridNext = new NativeArray<int>(0, Allocator.Persistent);
            _roadNetwork.GridValues = new NativeArray<int>(0, Allocator.Persistent);
            _roadNetwork.Width = 0;
            _roadNetwork.Height = 0;
        }

        private void RegisterComponents()
        {
            Repository.RegisterComponent<SimTransform>();
            Repository.RegisterComponent<SimVelocity>();
            Repository.RegisterComponent<VehicleState>();
            Repository.RegisterComponent<VehicleParams>();
            Repository.RegisterComponent<NavState>();
            Repository.RegisterComponent<FormationMember>();
            Repository.RegisterComponent<FormationRoster>();
            Repository.RegisterComponent<FormationTarget>();
            Repository.RegisterComponent<VehicleColor>();
            
            Repository.RegisterEvent<CmdSpawnVehicle>();
            Repository.RegisterEvent<CmdCreateFormation>();
            Repository.RegisterEvent<CmdJoinFormation>();
            Repository.RegisterEvent<CmdLeaveFormation>();
        }
        
        public void Update()
        {
             // 1. Handle Playback if active
             if (Playback != null)
             {
                 // Step Playback
                 if (!Playback.StepForward(Repository))
                 {
                     // End of playback
                 }
             }
             else
             {
                 // Normal Logic
                 Kernel.Update();
                 
                 _spatialSystem.Run();
                 _formationSystem.Run();
                 _commandSystem.Run();
                 _kinematicsSystem.Run();
                 
                 _scenarioManager.Update();
                 
                 // Recorder
                 if (Recorder != null)
                 {
                    var time = Kernel.CurrentTime; // Use accessor
                    // Use frame-locked wall clock: TotalWallTicks if populated, else sample at call-site.
                    long wallTicks = time.TotalWallTicks != 0
                        ? time.TotalWallTicks
                        : DateTime.UtcNow.Ticks;

                    if (time.FrameNumber % 60 == 0)
                        Recorder.CaptureKeyframe(Repository, wallTicks);
                        
                    uint prevTick = (uint)Math.Max(0, time.FrameNumber - 1);
                    // Use blocking=true for headless tests to ensure no dropped frames
                    Recorder.CaptureFrame(Repository, prevTick, wallTicks, blocking: true);
                 }
             }
        }
        
        public void SpawnFastOne()
        {
            _scenarioManager.SpawnFastOne();
        }
        
        public void StartRecording(string path)
        {
            if (Recorder != null) return;
            Recorder = new AsyncRecorder(path);
        }
        
        public void StopRecording()
        {
            Recorder?.Dispose();
            Recorder = null;
        }

        public void StartPlayback(string path)
        {
            // FIX: Stop recording if running
            if (Recorder != null)
            {
                StopRecording();
            }
            
            if (Playback != null) StopPlayback();
            
            Playback = new PlaybackController(path);
            
            // When replaying, we usually pause Sim, but here we drive the sim loop via 'Playback.StepForward' inside Update()
            // We should ensure TimeController is synced or irrelevant.
            // Actually, Playback replaces the state, so TimeController updates are overwritten.
        }

        public void StopPlayback()
        {
            Playback?.Dispose();
            Playback = null;
        }
        
        public void Dispose()
        {
            StopRecording();
            StopPlayback();
            Kernel?.Dispose();
            Repository?.Dispose();
            _spatialSystem?.Dispose();
            _kinematicsSystem?.Dispose();
            
            // Dispose road
            if (_roadNetwork.Nodes.IsCreated) _roadNetwork.Dispose();
        }
    }
}
