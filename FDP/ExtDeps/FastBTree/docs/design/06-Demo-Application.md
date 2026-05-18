# FastBTree Demo Application Design

**Version:** 1.0.0  
**Date:** 2026-01-04

**Stack:** ImGui.NET + Raylib + C# (.NET 8)

---

## 1. Overview

The demo application serves multiple purposes:

1. **Showcase** behavior tree capabilities
2. **Debug tool** for visual inspection of tree execution
3. **Performance profiler** for bottleneck identification
4. **Recording/replay** system for golden run capture
5. **Testing ground** for integration with real ECS

**Platform:** Windows Desktop (Primary), Cross-platform capable

---

## 2. Architecture

### 2.1 Application Structure

```
FastBTreeDemo/
├── Core/
│   ├── DemoApp.cs                 // Main application
│   ├── SceneManager.cs            // Scene switching
│   └── InputManager.cs            // Keyboard/mouse
├── Scenes/
│   ├── IScene.cs                  // Scene interface
│   ├── PatrolScene.cs             // Simple patrol demo
│   ├── CombatScene.cs             // Combat AI demo
│   ├── CrowdScene.cs              // Stress test (many agents)
│   └── PlaybackScene.cs           // Golden run playback
├── ECS/
│   ├── Entity.cs                  // Simple ECS structure
│   ├── Components/
│   │   ├── Transform.cs
│   │   ├── Sprite.cs
│   │   ├── BehaviorComponent.cs
│   │   └── HealthComponent.cs
│   └── Systems/
│       ├── BehaviorSystem.cs
│       ├── MovementSystem.cs
│       └── RenderSystem.cs
├── UI/
│   ├── TreeVisualizer.cs          // ImGui tree display
│   ├── PerformancePanel.cs        // Profiling UI
│   ├── EntityInspector.cs         // Entity details
│   ├── RecorderPanel.cs           // Recording controls
│   └── SceneControls.cs           // Scene-specific UI
└── Assets/
    ├── Trees/
    │   ├── patrol.json
    │   ├── combat.json
    │   └── guard.json
    └── Sprites/
        ├── orc.png
        ├── player.png
        └── environment/
```

### 2.2 Main Loop

```csharp
using Raylib_cs;
using ImGuiNET;

namespace FastBTreeDemo
{
    public class DemoApp
    {
        private const int ScreenWidth = 1920;
        private const int ScreenHeight = 1080;
        
        private SceneManager _sceneManager;
        private ImGuiController _imgui;
        private PerformanceMonitor _perfMonitor;
        
        public void Run()
        {
            // Initialize
            Raylib.InitWindow(ScreenWidth, ScreenHeight, "FastBTree Demo");
            Raylib.SetTargetFPS(60);
            
            _imgui = new ImGuiController();
            _sceneManager = new SceneManager();
            _perfMonitor = new PerformanceMonitor();
            
            LoadScenes();
            
            // Main loop
            while (!Raylib.WindowShouldClose())
            {
                float deltaTime = Raylib.GetFrameTime();
                
                // Update
                Update(deltaTime);
                
                // Render
                Render();
            }
            
            // Cleanup
            Raylib.CloseWindow();
        }
        
        private void Update(float deltaTime)
        {
            _perfMonitor.BeginFrame();
            
            // Update active scene
            _sceneManager.CurrentScene?.Update(deltaTime);
            
            _perfMonitor.EndFrame();
        }
        
        private void Render()
        {
            Raylib.BeginDrawing();
            Raylib.ClearBackground(Color.DARKGRAY);
            
            // Render 2D scene
            _sceneManager.CurrentScene?.Render();
            
            // Begin ImGui frame
            _imgui.BeginFrame();
            
            // Render UI
            RenderUI();
            
            // End ImGui frame
            _imgui.EndFrame();
            
            Raylib.EndDrawing();
        }
        
        private void RenderUI()
        {
            RenderMainMenu();
            RenderPerformancePanel();
            RenderTreeVisualizer();
            RenderEntityInspector();
            RenderRecorderPanel();
            
            _sceneManager.CurrentScene?.RenderUI();
        }
    }
}
```

---

## 3. Demo Scenes

### 3.1 Patrol Scene

**Goal:** Demonstrate basic waypoint navigation and waiting.

**Tree:** `patrol.json`
```json
{
  "treeName": "SimplePatrol",
  "root": {
    "type": "Sequence",
    "children": [
      { "type": "Action", "method": "GetNextPatrolPoint" },
      { "type": "Action", "method": "MoveToLocation" },
      { "type": "Wait", "params": { "duration": 2.0 } }
    ]
  }
}
```

**Visuals:**
- 3-5 patrol points shown as circles
- Agent sprite moving along path
- Green line showing computed path
- Wait timer visualization

```csharp
namespace FastBTreeDemo.Scenes
{
    public class PatrolScene : IScene
    {
        private List<Entity> _agents;
        private Vector2[] _patrolPoints;
        private BehaviorSystem _behaviorSystem;
        private MovementSystem _movementSystem;
        
        public void Initialize()
        {
            // Setup patrol points
            _patrolPoints = new[]
            {
                new Vector2(200, 200),
                new Vector2(600, 200),
                new Vector2(600, 500),
                new Vector2(200, 500)
            };
            
            // Create agent
            var agent = new Entity();
            agent.AddComponent(new Transform { Position = _patrolPoints[0] });
            agent.AddComponent(new Sprite { Texture = LoadTexture("orc.png") });
            agent.AddComponent(new BehaviorComponent 
            { 
                TreeBlob = TreeManager.Load("patrol.json"),
                State = new BehaviorTreeState(),
                Blackboard = new PatrolBlackboard 
                { 
                    PatrolPoints = _patrolPoints,
                    CurrentPointIndex = 0
                }
            });
            
            _agents = new List<Entity> { agent };
            
            // Systems
            _behaviorSystem = new BehaviorSystem();
            _movementSystem = new MovementSystem();
        }
        
        public void Update(float deltaTime)
        {
            foreach (var agent in _agents)
            {
                _behaviorSystem.Update(agent, deltaTime);
                _movementSystem.Update(agent, deltaTime);
            }
        }
        
        public void Render()
        {
            // Draw patrol points
            foreach (var pt in _patrolPoints)
            {
                Raylib.DrawCircleV(pt, 10, Color.YELLOW);
            }
            
            // Draw agents
            foreach (var agent in _agents)
            {
                var transform = agent.GetComponent<Transform>();
                var sprite = agent.GetComponent<Sprite>();
                
                Raylib.DrawTextureV(sprite.Texture, transform.Position, Color.WHITE);
                
                // Draw vision cone
                DrawVisionCone(transform.Position, transform.Forward, 100f);
            }
        }
        
        public void RenderUI()
        {
            ImGui.Begin("Patrol Scene");
            ImGui.Text($"Agents: {_agents.Count}");
            ImGui.SliderInt("Agent Count", ref _agentCount, 1, 50);
            if (ImGui.Button("Randomize Patrol Points"))
            {
                RandomizePatrolPoints();
            }
            ImGui.End();
        }
    }
}
```

### 3.2 Combat Scene

**Goal:** Showcase reactive behavior, observer aborts, target acquisition.

**Tree:** `combat.json`
```json
{
  "treeName": "OrcCombat",
  "root": {
    "type": "Selector",
    "children": [
      {
        "type": "Observer",
        "children": [
          { "type": "Condition", "method": "HasEnemyInRange" },
          {
            "type": "Selector",
            "children": [
              {
                "type": "Sequence",
                "children": [
                  { "type": "Condition", "method": "IsInMeleeRange" },
                  { "type": "Action", "method": "AttackMelee" }
                ]
              },
              {
                "type": "Action", "method": "ChaseTarget"
              }
            ]
          }
        ]
      },
      {
        "type": "Subtree",
        "params": { "subtreePath": "patrol.json" }
      }
    ]
  }
}
```

**Visuals:**
- Player-controlled entity
- 5-10 Orc agents
- Health bars
- Attack animations
- Damage numbers

```csharp
namespace FastBTreeDemo.Scenes
{
    public class CombatScene : IScene
    {
        private Entity _player;
        private List<Entity> _orcs;
        private CombatSystem _combatSystem;
        
        public void Initialize()
        {
            // Create player
            _player = new Entity();
            _player.AddComponent(new Transform { Position = new Vector2(400, 300) });
            _player.AddComponent(new Health { Current = 100, Max = 100 });
            _player.AddComponent(new PlayerInput());
            
            // Create orcs
            _orcs = new List<Entity>();
            for (int i = 0; i < 5; i++)
            {
                var orc = CreateOrcAgent(RandomPosition());
                _orcs.Add(orc);
            }
            
            _combatSystem = new CombatSystem(_player);
        }
        
        public void Update(float deltaTime)
        {
            // Player input
            UpdatePlayerInput(deltaTime);
            
            // Orc AI
            foreach (var orc in _orcs)
            {
                _behaviorSystem.Update(orc, deltaTime);
                _combatSystem.Update(orc, deltaTime);
            }
        }
        
        public void Render()
        {
            // Draw player
            DrawEntity(_player, Color.BLUE);
            
            // Draw orcs
            foreach (var orc in _orcs)
            {
                DrawEntity(orc, Color.RED);
                DrawHealthBar(orc);
                
                // Draw aggro radius
                var bb = orc.GetComponent<BehaviorComponent>().Blackboard as OrcBlackboard;
                if (bb.TargetEntityId != 0)
                {
                    // Draw line to target
                    var transform = orc.GetComponent<Transform>();
                    var targetPos = _player.GetComponent<Transform>().Position;
                    Raylib.DrawLineEx(transform.Position, targetPos, 2, Color.YELLOW);
                }
            }
        }
        
        public void RenderUI()
        {
            ImGui.Begin("Combat Scene");
            ImGui.Text($"Player Health: {_player.GetComponent<Health>().Current:F0}");
            ImGui.Text($"Orcs Alive: {_orcs.Count(o => o.GetComponent<Health>().Current > 0)}");
            
            if (ImGui.Button("Spawn Orc"))
            {
                _orcs.Add(CreateOrcAgent(RandomPosition()));
            }
            
            ImGui.Checkbox("Show AI Debug", ref _showAIDebug);
            ImGui.End();
        }
    }
}
```

### 3.3 Crowd Scene (Stress Test)

**Goal:** Performance testing with dynamic agent count.

**Features:**
- Slider: 10 to 10,000 agents
- Real-time FPS/frame time display
- LOD system visualization
- Batched query count

```csharp
namespace FastBTreeDemo.Scenes
{
    public class CrowdScene : IScene
    {
        private List<Entity> _crowd;
        private int _targetAgentCount = 100;
        private PerformanceLODSystem _lodSystem;
        
        public void Initialize()
        {
            _crowd = new List<Entity>();
            _lodSystem = new PerformanceLODSystem();
            
            SpawnAgents(_targetAgentCount);
        }
        
        public void Update(float deltaTime)
        {
            // Adjust agent count
            if (_crowd.Count < _targetAgentCount)
            {
                SpawnAgents(_targetAgentCount - _crowd.Count);
            }
            else if (_crowd.Count > _targetAgentCount)
            {
                RemoveAgents(_crowd.Count - _targetAgentCount);
            }
            
            // Update with LOD
            _lodSystem.Update(_crowd, deltaTime);
        }
        
        public void Render()
        {
            // Simple sprite batching
            foreach (var entity in _crowd)
            {
                var transform = entity.GetComponent<Transform>();
                Raylib.DrawCircleV(transform.Position, 5, Color.WHITE);
            }
            
            // Draw LOD zones
            if (_showLODZones)
            {
                DrawLODVisualization();
            }
        }
        
        public void RenderUI()
        {
            ImGui.Begin("Crowd Stress Test");
            
            ImGui.SliderInt("Agent Count", ref _targetAgentCount, 10, 10000);
            ImGui.Text($"Current: {_crowd.Count}");
            ImGui.Separator();
            
            ImGui.Text($"FPS: {Raylib.GetFPS()}");
            ImGui.Text($"Frame Time: {Raylib.GetFrameTime() * 1000:F2}ms");
            ImGui.Separator();
            
            ImGui.Text("LOD Distribution:");
            ImGui.Text($"  High: {_lodSystem.HighLODCount}");
            ImGui.Text($"  Medium: {_lodSystem.MediumLODCount}");
            ImGui.Text($"  Low: {_lodSystem.LowLODCount}");
            
            ImGui.Checkbox("Show LOD Zones", ref _showLODZones);
            
            ImGui.End();
        }
    }
}
```

### 3.4 Playback Scene (Golden Run Replay)

**Goal:** Visualize recorded test scenarios.

```csharp
namespace FastBTreeDemo.Scenes
{
    public class PlaybackScene : IScene
    {
        private GoldenRunRecording _recording;
        private int _currentFrame;
        private bool _isPaused = true;
        private float _playbackSpeed = 1.0f;
        
        public void LoadRecording(string path)
        {
            var json = File.ReadAllText(path);
            _recording = JsonSerializer.Deserialize<GoldenRunRecording>(json);
            _currentFrame = 0;
        }
        
        public void Update(float deltaTime)
        {
            if (_isPaused) return;
            
            // Step through frames
            if (_currentFrame < _recording.Frames.Count)
            {
                var frame = _recording.Frames[_currentFrame];
                
                // Apply frame state
                ApplyFrameState(frame);
                
                _currentFrame++;
            }
        }
        
        public void Render()
        {
            // Render scene state at current frame
            RenderRecordedState();
        }
        
        public void RenderUI()
        {
            ImGui.Begin("Playback Controls");
            
            if (ImGui.Button(_isPaused ? "Play" : "Pause"))
            {
                _isPaused = !_isPaused;
            }
            
            ImGui.SameLine();
            if (ImGui.Button("Step"))
            {
                _currentFrame++;
            }
            
            ImGui.SliderInt("Frame", ref _currentFrame, 0, _recording.Frames.Count - 1);
            ImGui.SliderFloat("Speed", ref _playbackSpeed, 0.1f, 2.0f);
            
            ImGui.Text($"Frame: {_currentFrame} / {_recording.Frames.Count}");
            ImGui.Text($"Time: {GetCurrentTime():F2}s");
            
            ImGui.Separator();
            
            // Frame details
            if (_currentFrame < _recording.Frames.Count)
            {
                var frame = _recording.Frames[_currentFrame];
                ImGui.Text("Frame Details:");
                ImGui.Text($"  Running Node: {frame.ExpectedRunningNode}");
                ImGui.Text($"  Result: {frame.ExpectedResult}");
                ImGui.Text($"  Raycasts: {frame.RaycastResults.Count}");
            }
            
            ImGui.End();
        }
    }
}
```

---

## 4. ImGui UI Components

### 4.1 Tree Visualizer

```csharp
namespace FastBTreeDemo.UI
{
    public class TreeVisualizer
    {
        public void Draw(BehaviorTreeBlob blob, BehaviorTreeState state, Entity selectedEntity)
        {
            ImGui.Begin("Behavior Tree");
            
            if (blob == null)
            {
                ImGui.Text("No tree loaded");
                ImGui.End();
                return;
            }
            
            ImGui.Text($"Tree: {blob.TreeName}");
            ImGui.Separator();
            
            // Recursive tree display
            DrawNode(blob, state, 0, 0);
            
            ImGui.End();
        }
        
        private void DrawNode(BehaviorTreeBlob blob, BehaviorTreeState state, int nodeIndex, int depth)
        {
            ref var node = ref blob.Nodes[nodeIndex];
            
            // Determine color
            bool isRunning = state.RunningNodeIndex == nodeIndex;
            var color = isRunning 
                ? new Vector4(1, 1, 0, 1)  // Yellow
                : new Vector4(0.7f, 0.7f, 0.7f, 1); // Gray
            
            ImGui.PushStyleColor(ImGuiCol.Text, color);
            
            // Format label
            string label = FormatNodeLabel(blob, node, nodeIndex);
            
            // Tree node
            bool expanded = ImGui.TreeNodeEx(
                $"##{nodeIndex}",
                 ImGuiTreeNodeFlags.DefaultOpen,
                label);
            
            ImGui.PopStyleColor();
            
            // Context menu
            if (ImGui.BeginPopupContextItem())
            {
                if (ImGui.MenuItem("Force Success"))
                {
                    // Debug option
                }
                if (ImGui.MenuItem("Force Failure"))
                {
                    // Debug option
                }
                ImGui.EndPopup();
            }
            
            // Children
            if (expanded)
            {
                int currentChild = nodeIndex + 1;
                for (int i = 0; i < node.ChildCount; i++)
                {
                    DrawNode(blob, state, currentChild, depth + 1);
                    currentChild += blob.Nodes[currentChild].SubtreeOffset;
                }
                ImGui.TreePop();
            }
        }
        
        private string FormatNodeLabel(BehaviorTreeBlob blob, NodeDefinition node, int index)
        {
            string name = node.Type.ToString();
            
            if (node.Type == NodeType.Action || node.Type == NodeType.Condition)
            {
                name += $" ({blob.MethodNames[node.PayloadIndex]})";
            }
            
            return $"[{index}] {name}";
        }
    }
}
```

### 4.2 Performance Panel

```csharp
namespace FastBTreeDemo.UI
{
    public class PerformancePanel
    {
        private CircularBuffer<float> _frameTimeHistory = new(120); // 2 seconds @ 60fps
        private CircularBuffer<float> _btTickTimeHistory = new(120);
        
        public void Draw(PerformanceMonitor monitor)
        {
            ImGui.Begin("Performance");
            
            // Current stats
            ImGui.Text($"FPS: {monitor.FPS:F1}");
            ImGui.Text($"Frame Time: {monitor.FrameTime * 1000:F2}ms");
            ImGui.Text($"BT Tick Time: {monitor.BTTickTime * 1000:F3}ms");
            ImGui.Separator();
            
            // Graphs
            _frameTimeHistory.Add(monitor.FrameTime * 1000);
            _btTickTimeHistory.Add(monitor.BTTickTime * 1000);
            
            ImGui.PlotLines(
                "Frame Time (ms)",
                ref _frameTimeHistory.Data[0],
                _frameTimeHistory.Count,
                0,
                null,
                0,
                33.3f, // 30fps line
                new Vector2(0, 80));
            
            ImGui.PlotLines(
                "BT Tick Time (ms)",
                ref _btTickTimeHistory.Data[0],
                _btTickTimeHistory.Count,
                0,
                null,
                0,
                10f,
                new Vector2(0, 80));
            
            // Detailed breakdown
            if (ImGui.CollapsingHeader("Detailed Stats"))
            {
                ImGui.Text($"Entity Count: {monitor.EntityCount}");
                ImGui.Text($"Active Trees: {monitor.ActiveTreeCount}");
                ImGui.Text($"Batched Raycasts: {monitor.BatchedRaycastCount}");
                ImGui.Text($"Batched Paths: {monitor.BatchedPathCount}");
            }
            
            ImGui.End();
        }
    }
}
```

### 4.3 Entity Inspector

```csharp
namespace FastBTreeDemo.UI
{
    public class EntityInspector
    {
        public void Draw(Entity selectedEntity)
        {
            ImGui.Begin("Entity Inspector");
            
            if (selectedEntity == null)
            {
                ImGui.Text("No entity selected");
                ImGui.Text("Click on an entity in the scene");
                ImGui.End();
                return;
            }
            
            ImGui.Text($"Entity ID: {selectedEntity.Id}");
            ImGui.Separator();
            
            // Components
            if (ImGui.CollapsingHeader("Transform"))
            {
                var transform = selectedEntity.GetComponent<Transform>();
                var pos = transform.Position;
                ImGui.Text($"Position: ({pos.X:F1}, {pos.Y:F1})");
                ImGui.Text($"Rotation: {transform.Rotation:F1}°");
            }
            
            if (selectedEntity.HasComponent<BehaviorComponent>() && 
                ImGui.CollapsingHeader("Behavior", ImGuiTreeNodeFlags.DefaultOpen))
            {
                var beh = selectedEntity.GetComponent<BehaviorComponent>();
                
                ImGui.Text($"Tree: {beh.TreeBlob.TreeName}");
                ImGui.Text($"Running Node: {beh.State.RunningNodeIndex}");
                ImGui.Text($"Tree Version: {beh.State.TreeVersion}");
                ImGui.Separator();
                
                // Blackboard
                if (buh.Blackboard is OrcBlackboard orcBB)
                {
                    ImGui.Text("Blackboard:");
                    ImGui.Text($"  Target: {orcBB.TargetEntityId}");
                    ImGui.Text($"  Health: {orcBB.Health:F1}");
                    ImGui.Text($"  Alerted: {orcBB.IsAlerted}");
                }
                
                // Registers
                if (ImGui.TreeNode("Registers"))
                {
                    unsafe
                    {
                        for (int i = 0; i < 4; i++)
                        {
                            ImGui.Text($"  [{i}]: {beh.State.LocalRegisters[i]}");
                        }
                    }
                    ImGui.TreePop();
                }
                
                // Async Handles
                if (ImGui.TreeNode("Async Handles"))
                {
                    unsafe
                    {
                        for (int i = 0; i < 3; i++)
                        {
                            var token = AsyncToken.Unpack(beh.State.AsyncHandles[i]);
                            ImGui.Text($"  [{i}]: ReqID={token.RequestID}, Ver={token.Version}");
                        }
                    }
                    ImGui.TreePop();
                }
            }
            
            ImGui.End();
        }
    }
}
```

### 4.4 Recorder Panel

```csharp
namespace FastBTreeDemo.UI
{
    public class RecorderPanel
    {
        private GoldenRunRecorder _recorder = new();
        private bool _isRecording;
        private string _recordingName = "NewRecording";
        
        public void Draw()
        {
            ImGui.Begin("Recorder");
            
            if (!_isRecording)
            {
                ImGui.InputText("Name", ref _recordingName, 128);
                
                if (ImGui.Button("Start Recording"))
                {
                    StartRecording();
                }
            }
            else
            {
                ImGui.Text("Recording...");
                ImGui.Text($"Frames: {_recorder.FrameCount}");
                ImGui.Text($"Duration: {_recorder.Duration:F2}s");
                
                if (ImGui.Button("Stop Recording"))
                {
                    StopRecording();
                }
            }
            
            ImGui.Separator();
            
            ImGui.Text("Saved Recordings:");
            foreach (var file in Directory.GetFiles("Recordings", "*.json"))
            {
                if (ImGui.Selectable(Path.GetFileName(file)))
                {
                    LoadRecording(file);
                }
            }
            
            ImGui.End();
        }
        
        private void StartRecording()
        {
            // Implementation
            _isRecording = true;
        }
        
        private void StopRecording()
        {
            _recorder.Save($"Recordings/{_recordingName}.json");
            _isRecording = false;
        }
    }
}
```

---

## 5. Simple ECS Implementation

```csharp
namespace FastBTreeDemo.ECS
{
    public class Entity
    {
        public int Id { get; }
        private Dictionary<Type, object> _components = new();
        
        public T AddComponent<T>(T component)
        {
            _components[typeof(T)] = component;
            return component;
        }
        
        public T GetComponent<T>()
        {
            return (T)_components[typeof(T)];
        }
        
        public bool HasComponent<T>()
        {
            return _components.ContainsKey(typeof(T));
        }
    }
    
    public struct Transform
    {
        public Vector2 Position;
        public float Rotation;
        public Vector2 Forward => new Vector2(
            MathF.Cos(Rotation * DEG2RAD),
            MathF.Sin(Rotation * DEG2RAD));
    }
    
    public struct Sprite
    {
        public Texture2D Texture;
        public Color Tint;
    }
    
    public struct Health
    {
        public float Current;
        public float Max;
    }
    
    public class BehaviorComponent
    {
        public BehaviorTreeBlob TreeBlob;
        public BehaviorTreeState State;
        public object Blackboard; // Typed as object, cast in systems
    }
}
```

---

## 6. Build & Run

### 6.1 Project Structure

```xml
<!-- FastBTreeDemo.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Raylib-cs" Version="5.0.0" />
    <PackageReference Include="ImGui.NET" Version="1.90.0" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\src\Fbt.Kernel\Fbt.Kernel.csproj" />
  </ItemGroup>
  
  <ItemGroup>
    <None Update="Assets\**\*">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </None>
  </ItemGroup>
</Project>
```

### 6.2 Launch

```bash
# Build
dotnet build

# Run
dotnet run --project demos/FastBTreeDemo

# Release mode (for profiling)
dotnet run --project demos/FastBTreeDemo -c Release
```

---

**Summary:** The demo application provides a complete showcase of FastBTree capabilities with visual debugging, performance profiling, and recording/replay features, all while using ECS-compatible data structures for future integration.
