using System;
using System.Collections.Generic;
using System.Diagnostics;
using Raylib_cs;
using rlImGui_cs;
using Fdp.Kernel;
using Fdp.Kernel.FlightRecorder;
using Fdp.Examples.Showcase.Components;
using Fdp.Examples.Showcase.Modules;
using Fdp.Examples.Showcase.Systems;

namespace Fdp.Examples.Showcase.Core
{
    public class ShowcaseGame
    {
        public EntityRepository Repo { get; private set; } = null!;
        private TimeSystem _timeSystem = null!;
        private PlaybackSystem _playback = null!;

        // Performance Metrics
        public Dictionary<string, double> PhaseTimings { get; private set; } = new();
        public double TotalFrameTime { get; private set; }
        
        // Systems
        private MovementSystem _movement = null!;
        private SpatialSystem _spatial = null!;
        private PatrolSystem _combat = null!;
        private CollisionSystem _collision = null!;
        private CombatSystem _combatSystem = null!;
        private ProjectileSystem _projectileSystem = null!;
        private HitFlashSystem _hitFlashSystem = null!;
        private ParticleSystem _particleSystem = null!;
        private LifecycleSystem _lifecycle = null!;
        
        // Event Bus
        private FdpEventBus _eventBus = null!;
        public FdpEventBus EventBus => _eventBus;
        
        // Flight Recorder
        public AsyncRecorder? DiskRecorder { get; set; } = null;
        private string _recordingFilePath = "showcase_recording.fdp";
        public string RecordingFilePath => _recordingFilePath;
        
        // Playback Controller
        public PlaybackController? PlaybackController { get; set; } = null;

        // State
        public bool IsRunning { get; set; } = true;
        public bool IsRecording { get; set; } = true;
        public bool IsReplaying { get; set; } = false;
        public bool IsPaused { get; set; } = false;
        public bool ShowInspector { get; set; } = false;
        public bool SingleStep { get; set; } = false;
        
        // Entity Inspector
        public EntityInspector Inspector { get; private set; } = null!;
        
        // Event Inspector
        public EventInspector EventInspector { get; private set; } = null!;
        
        // Frame tracking
        private int _totalRecordedFrames = 0;
        private uint _previousTick = 0;

        private ShowcaseRenderer _renderer = null!;
        private ShowcaseInput _input = null!;

        public ShowcaseGame()
        {
            _renderer = new ShowcaseRenderer(this);
            _input = new ShowcaseInput(this);
        }

        public void Initialize()
        {
            Repo = new EntityRepository();
            _eventBus = new FdpEventBus();

            // Load Modules
            new PhysicsModule().Load(Repo);
            new CombatModule().Load(Repo);
            new RenderModule().Load(Repo);

            // Register global services
            _timeSystem = new TimeSystem(Repo);
            
            // Create the Lifecycle System (Barrier) FIRST
            _lifecycle = new LifecycleSystem(Repo);
            
            // Create Systems
            _movement = new MovementSystem(Repo);
            _combat = new PatrolSystem(Repo);
            _spatial = new SpatialSystem(Repo);
            _collision = new CollisionSystem(Repo, _eventBus, _spatial);
            _combatSystem = new CombatSystem(Repo, _eventBus, _spatial);
            _projectileSystem = new ProjectileSystem(Repo, _eventBus, _lifecycle, _spatial);
            _hitFlashSystem = new HitFlashSystem(Repo);
            _particleSystem = new ParticleSystem(Repo, _eventBus, _lifecycle);
            
            // Flight Recorder
            DiskRecorder = new AsyncRecorder(_recordingFilePath);
            _playback = new PlaybackSystem();
            
            // Entity Inspector
            Inspector = new EntityInspector(Repo);
            
            // Event Inspector
            EventInspector = new EventInspector(_eventBus);

            // Spawn Initial Entities
            SpawnUnit(UnitType.Tank, 10, 10, 5, 0);
            SpawnUnit(UnitType.Aircraft, 5, 5, 10, 2);
            SpawnUnit(UnitType.Infantry, 20, 12, 1, 1);
        }

        public void RunRaylibLoop()
        {
            // Main Raylib game loop
            while (!Raylib.WindowShouldClose() && IsRunning)
            {
                float dt = Raylib.GetFrameTime();
                
                // Update
                _renderer.UpdateCamera(dt);
                UpdateFrame(dt);
                
                // Render
                Raylib.BeginDrawing();
                Raylib.ClearBackground(new Color(20, 20, 30, 255));
                
                _renderer.RenderBattlefield();
                
                // Draw ImGui UI
                rlImGui.Begin();
                var ui = new ShowcaseUI(this);
                ui.DrawAllPanels();
                rlImGui.End();
                
                Raylib.EndDrawing();
            }
        }

        private void UpdateFrame(float dt)
        {
            long frameStart = Stopwatch.GetTimestamp();
            
            // Track if we need to process events
            bool stateUpdated = false;
            int replayFrameBefore = (IsReplaying && PlaybackController != null) ? PlaybackController.CurrentFrame : -1;

            // 1. Input - handled by Raylib keyboard checking
            long phaseStart = Stopwatch.GetTimestamp();
            _input.HandleRaylibInput(); // This might cause seeking in playback!
            RecordPhaseTime("1. Input", phaseStart);
            
            // 2. Logic Step
            bool shouldExecuteLogic = !IsReplaying && (!IsPaused || SingleStep);
            
            if (shouldExecuteLogic)
            {
                if (SingleStep)
                    SingleStep = false;
                
                phaseStart = Stopwatch.GetTimestamp();
                Repo.Tick();
                RecordPhaseTime("2. Repo.Tick", phaseStart);
                
                phaseStart = Stopwatch.GetTimestamp();
                _timeSystem.Update();
                RecordPhaseTime("3. TimeSystem", phaseStart);
                
                phaseStart = Stopwatch.GetTimestamp();
                _combat.Run();
                RecordPhaseTime("4. PatrolSystem", phaseStart);
                
                phaseStart = Stopwatch.GetTimestamp();
                _movement.Run();
                RecordPhaseTime("5. MovementSystem", phaseStart);
                
                phaseStart = Stopwatch.GetTimestamp();
                _spatial.Run();
                RecordPhaseTime("6. SpatialSystem", phaseStart);
                
                phaseStart = Stopwatch.GetTimestamp();
                _collision.Run();
                RecordPhaseTime("7. CollisionSystem", phaseStart);
                
                phaseStart = Stopwatch.GetTimestamp();
                _combatSystem.Run();
                RecordPhaseTime("8. CombatSystem", phaseStart);
                
                phaseStart = Stopwatch.GetTimestamp();
                _projectileSystem.Run();
                RecordPhaseTime("9. ProjectileSystem", phaseStart);
                
                phaseStart = Stopwatch.GetTimestamp();
                _hitFlashSystem.Run();
                RecordPhaseTime("10. HitFlashSystem", phaseStart);
                
                phaseStart = Stopwatch.GetTimestamp();
                _particleSystem.Run();
                RecordPhaseTime("11. ParticleSystem", phaseStart);
                
                phaseStart = Stopwatch.GetTimestamp();
                _lifecycle.Run();
                RecordPhaseTime("12. LifecycleSystem", phaseStart);
                
                if (IsRecording && !IsPaused && DiskRecorder != null)
                {
                    phaseStart = Stopwatch.GetTimestamp();
                    bool isKeyframe = (_totalRecordedFrames % 60 == 0);
                    
                    if (isKeyframe)
                    {
                        DiskRecorder.CaptureKeyframe(Repo, DateTime.UtcNow.Ticks, blocking: false, eventBus: _eventBus);
                    }
                    else
                    {
                        DiskRecorder.CaptureFrame(Repo, _previousTick, DateTime.UtcNow.Ticks, blocking: false, eventBus: _eventBus);
                    }
                    RecordPhaseTime("14. Recorder", phaseStart);
                    
                    _totalRecordedFrames++;
                    _previousTick = Repo.GlobalVersion;
                }
                
                stateUpdated = true;
            }
            else if (IsReplaying && PlaybackController != null)
            {
                if (!IsPaused)
                {
                    phaseStart = Stopwatch.GetTimestamp();
                    if (!PlaybackController.StepForward(Repo))
                    {
                        IsPaused = true;
                    }
                    RecordPhaseTime("Playback.Step", phaseStart);
                }
            }
            
            // Check if replay frame changed (due to stepping OR seeking in Input)
            if (IsReplaying && PlaybackController != null && PlaybackController.CurrentFrame != replayFrameBefore)
            {
                stateUpdated = true;
            }
            
            // Handle Events if state updated
            if (stateUpdated)
            {
                // Only swap buffers if we executed logic (Live/Recording) where events were Published to Write buffer.
                // If we Replayed, events were Injected into Read buffer directly, so we must NOT swap.
                if (shouldExecuteLogic)
                {
                    phaseStart = Stopwatch.GetTimestamp();
                    _eventBus.SwapBuffers();
                    RecordPhaseTime("13. EventBus.Swap", phaseStart);
                }
                
                // Capture events for Event Inspector (after swap or injection, so we see current frame's events)
                EventInspector.CaptureFrameEvents();
            }
            
            // Record total frame time
            TotalFrameTime = (Stopwatch.GetTimestamp() - frameStart) * 1000.0 / Stopwatch.Frequency;
        }
        
        private void RecordPhaseTime(string phaseName, long startTicks)
        {
            long elapsed = Stopwatch.GetTimestamp() - startTicks;
            double ms = elapsed * 1000.0 / Stopwatch.Frequency;
            PhaseTimings[phaseName] = ms;
        }

        public void Cleanup()
        {
            DiskRecorder?.Dispose();
            
            if (DiskRecorder != null)
            {
                Console.WriteLine($"\nRecording Statistics:");
                Console.WriteLine($"  Total Frames Recorded: {DiskRecorder.RecordedFrames}");
                Console.WriteLine($"  Dropped Frames: {DiskRecorder.DroppedFrames}");
                Console.WriteLine($"  Recording saved to: {_recordingFilePath}");
            }
            
            Repo.Dispose();
        }

        public void SpawnUnit(UnitType type, float x, float y, float vx, float vy)
        {
            var e = Repo.CreateEntity();
            Repo.AddComponent(e, new Position { X = x, Y = y });
            Repo.AddComponent(e, new Velocity { X = vx, Y = vy });
            
            var (shape, r, g, b, size) = type switch {
                UnitType.Tank => (EntityShape.Square, (byte)255, (byte)255, (byte)100, 1.5f),
                UnitType.Aircraft => (EntityShape.Triangle, (byte)100, (byte)200, (byte)255, 1.2f),
                _ => (EntityShape.Circle, (byte)150, (byte)150, (byte)150, 0.8f)
            };
            
            Repo.AddComponent(e, new RenderSymbol { 
                Shape = shape, 
                R = r, 
                G = g, 
                B = b,
                Size = size
            });
            
            Repo.AddComponent(e, new UnitStats { Type = type, Health = 100, MaxHealth = 100 });
            
            // Add managed CombatHistory component for testing managed component recording/playback
            Repo.AddComponent(e, new CombatHistory());
        }
        
        public void SpawnRandomUnit(UnitType type)
        {
            if (IsReplaying) return;
            
            var rand = new Random();
            float x = rand.Next(5, 55);
            float y = rand.Next(2, 18);
            float vx = rand.Next(-5, 6);
            float vy = rand.Next(-5, 6);
            
            SpawnUnit(type, x, y, vx, vy);
        }
        
        public void RemoveRandomUnit()
        {
            if (IsReplaying) return;
            
            var query = Repo.Query().Build();
            var entities = new List<Entity>();
            foreach (var e in query) entities.Add(e);
            
            if (entities.Count > 0)
            {
                var rand = new Random();
                var toRemove = entities[rand.Next(entities.Count)];
                Repo.DestroyEntity(toRemove);
            }
        }
    }
}
