# BATCH-07: Interactive Visual Demo Application

**Project:** FastBTree - High-Performance Behavior Tree Library  
**Batch Number:** 07  
**Phase:** Phase 3 - Optional Enhancements  
**Assigned:** 2026-01-05  
**Estimated Effort:** 4-5 days  
**Prerequisites:** BATCH-01-06 ✅ (v1.0 Released!)

---

## 📋 Batch Overview

**v1.0 is complete! This batch creates a visual showcase for FastBTree.**

This batch adds:
1. **Raylib-based visual demo** - 2D graphics, interactive UI
2. **Real-time tree visualization** - See trees execute in real-time
3. **AI agent simulation** - Multiple agents with different behaviors
4. **Performance monitoring** - FPS, tick times, entity counts
5. **Interactive controls** - Pause, slow-mo, step-through execution

**Purpose:**
- Marketing/showcase (not core functionality)
- Demonstrate library capabilities
- Educational tool for learning behavior trees

**Critical Success Factors:**
- ✅ Visual appeal (impress viewers!)
- ✅ Clear tree execution visualization
- ✅ Smooth performance (60 FPS with 100+ agents)
- ✅ Interactive and engaging

---

## 📚 Required Reading

**BEFORE starting, review:**

1. **FastBTree v1.0** - Understand the library
2. **Raylib-cs** - .NET bindings for Raylib
3. **ImGui.NET** - Immediate mode GUI

**Key Concepts:**
- Raylib for 2D rendering
- ImGui for dev UI
- Real-time tree visualization
- Agent-based simulation

---

## 🎯 Tasks

### Task 1: Project Setup

**Objective:** Create Raylib-based demo project.

**Commands:**
```bash
dotnet new console -n Fbt.Demo.Visual -o demos/Fbt.Demo.Visual
cd demos/Fbt.Demo.Visual
dotnet add package Raylib-cs
dotnet add package ImGui.NET
dotnet add package rlImGui-cs
dotnet add reference ../../src/Fbt.Kernel
```

**Project Structure:**
```
demos/
  Fbt.Demo.Visual/
    Program.cs
    DemoApp.cs
    Entities/
      Agent.cs
      Blackboard.cs
    Systems/
      RenderSystem.cs
      BehaviorSystem.cs
    Trees/
      patrol.json
      gather.json
      combat.json
    Assets/
      (sprites if needed)
```

---

### Task 2: Core Application Structure

**Objective:** Create main loop and entity management.

**File:** `demos/Fbt.Demo.Visual/DemoApp.cs`

```csharp
using Raylib_cs;
using ImGuiNET;
using rlImGui_cs;
using Fbt;
using Fbt.Runtime;
using Fbt.Serialization;

namespace Fbt.Demo.Visual
{
    public class DemoApp
    {
        private const int ScreenWidth = 1280;
        private const int ScreenHeight = 720;
        
        private List<Agent> _agents = new();
        private Dictionary<string, BehaviorTreeBlob> _trees = new();
        private BehaviorSystem _behaviorSystem;
        private RenderSystem _renderSystem;
        
        private float _time = 0;
        private bool _paused = false;
        private float _timeScale = 1.0f;
        
        public void Run()
        {
            Raylib.InitWindow(ScreenWidth, ScreenHeight, "FastBTree Visual Demo");
            Raylib.SetTargetFPS(60);
            rlImGui.Setup(true);
            
            Initialize();
            
            while (!Raylib.WindowShouldClose())
            {
                float dt = Raylib.GetFrameTime() * _timeScale;
                
                if (!_paused)
                {
                    Update(dt);
                }
                
                Render();
            }
            
            rlImGui.Shutdown();
            Raylib.CloseWindow();
        }
        
        private void Initialize()
        {
            // Load behavior trees
            _trees["patrol"] = LoadTree("Trees/patrol.json");
            _trees["gather"] = LoadTree("Trees/gather.json");
            _trees["combat"] = LoadTree("Trees/combat.json");
            
            // Create systems
            _behaviorSystem = new BehaviorSystem(_trees);
            _renderSystem = new RenderSystem();
            
            // Spawn initial agents
            SpawnPatrolAgents(10);
            SpawnGatherAgents(5);
        }
        
        private void Update(float dt)
        {
            _time += dt;
            
            // Update all agents
            _behaviorSystem.Update(_agents, _time, dt);
            
            // Update movement
            foreach (var agent in _agents)
            {
                agent.UpdateMovement(dt);
            }
        }
        
        private void Render()
        {
            Raylib.BeginDrawing();
            Raylib.ClearBackground(Color.DARKGRAY);
            
            // Render world
            _renderSystem.RenderAgents(_agents);
            
            // ImGui UI
            rlImGui.Begin();
            RenderUI();
            rlImGui.End();
            
            Raylib.EndDrawing();
        }
        
        private void RenderUI()
        {
            // Control panel
            ImGui.Begin("FastBTree Demo");
            
            ImGui.Text($"FPS: {Raylib.GetFPS()}");
            ImGui.Text($"Agents: {_agents.Count}");
            ImGui.Text($"Time: {_time:F2}s");
            
            ImGui.Separator();
            
            ImGui.Checkbox("Paused", ref _paused);
            ImGui.SliderFloat("Time Scale", ref _timeScale, 0.1f, 5.0f);
            
            ImGui.Separator();
            
            if (ImGui.Button("Spawn Patrol Agent"))
                SpawnPatrolAgents(1);
            
            if (ImGui.Button("Spawn Gather Agent"))
                SpawnGatherAgents(1);
            
            if (ImGui.Button("Clear All"))
                _agents.Clear();
            
            ImGui.End();
            
            // Selected agent details
            if (_selectedAgent != null)
            {
                RenderAgentInspector(_selectedAgent);
            }
        }
    }
}
```

---

### Task 3: Agent Entity System

**Objective:** Create agent entities with behavior trees.

**File:** `demos/Fbt.Demo.Visual/Entities/Agent.cs`

```csharp
using System.Numerics;
using Raylib_cs;
using Fbt;
using Fbt.Runtime;

namespace Fbt.Demo.Visual
{
    public class Agent
    {
        public int Id { get; set; }
        public Vector2 Position { get; set; }
        public Vector2 Velocity { get; set; }
        public float Rotation { get; set; }
        public Color Color { get; set; }
        
        // Behavior tree
        public string TreeName { get; set; }
        public AgentBlackboard Blackboard { get; set; }
        public BehaviorTreeState State { get; set; }
        
        // AI state
        public Vector2 TargetPosition { get; set; }
        public float Speed { get; set; } = 50f;
        public AgentRole Role { get; set; }
        
        // Visual state
        public TreeExecutionHighlight? CurrentNode { get; set; }
        
        public Agent(int id, Vector2 position, string treeName, AgentRole role)
        {
            Id = id;
            Position = position;
            TreeName = treeName;
            Role = role;
            Blackboard = new AgentBlackboard();
            State = new BehaviorTreeState();
            
            Color = role switch
            {
                AgentRole.Patrol => Color.BLUE,
                AgentRole.Gather => Color.GREEN,
                AgentRole.Combat => Color.RED,
                _ => Color.WHITE
            };
        }
        
        public void UpdateMovement(float dt)
        {
            // Move towards target
            var direction = Vector2.Normalize(TargetPosition - Position);
            if (!float.IsNaN(direction.X))
            {
                Velocity = direction * Speed;
                Position += Velocity * dt;
                Rotation = MathF.Atan2(direction.Y, direction.X);
            }
        }
    }
    
    public enum AgentRole
    {
        Patrol,
        Gather,
        Combat
    }
    
    public struct AgentBlackboard
    {
        public int PatrolPointIndex;
        public float LastPatrolTime;
        public int ResourceCount;
        public bool HasTarget;
    }
    
    public struct TreeExecutionHighlight
    {
        public int NodeIndex;
        public NodeStatus Status;
        public float Timestamp;
    }
}
```

---

### Task 4: Behavior System

**Objective:** Execute behavior trees for all agents.

**File:** `demos/Fbt.Demo.Visual/Systems/BehaviorSystem.cs`

```csharp
using Fbt;
using Fbt.Runtime;
using System.Numerics;

namespace Fbt.Demo.Visual
{
    public class BehaviorSystem
    {
        private Dictionary<string, Interpreter<AgentBlackboard, DemoContext>> _interpreters;
        private ActionRegistry<AgentBlackboard, DemoContext> _registry;
        
        public BehaviorSystem(Dictionary<string, BehaviorTreeBlob> trees)
        {
            _registry = new ActionRegistry<AgentBlackboard, DemoContext>();
            RegisterActions();
            
            _interpreters = new Dictionary<string, Interpreter<AgentBlackboard, DemoContext>>();
            foreach (var (name, blob) in trees)
            {
                _interpreters[name] = new Interpreter<AgentBlackboard, DemoContext>(blob, _registry);
            }
        }
        
        private void RegisterActions()
        {
            // Patrol actions
            _registry.Register("FindPatrolPoint", FindPatrolPoint);
            _registry.Register("MoveToTarget", MoveToTarget);
            _registry.Register("Wait", Wait);
            
            // Gather actions
            _registry.Register("FindResource", FindResource);
            _registry.Register("Gather", Gather);
            _registry.Register("ReturnToBase", ReturnToBase);
            
            // Combat actions
            _registry.Register("ScanForEnemy", ScanForEnemy);
            _registry.Register("ChaseEnemy", ChaseEnemy);
            _registry.Register("Attack", Attack);
        }
        
        public void Update(List<Agent> agents, float time, float dt)
        {
            var context = new DemoContext { Time = time, DeltaTime = dt };
            
            foreach (var agent in agents)
            {
                if (!_interpreters.TryGetValue(agent.TreeName, out var interpreter))
                    continue;
                
                // Execute behavior tree
                var status = interpreter.Tick(
                    ref agent.Blackboard,
                    ref agent.State,
                    ref context);
                
                // Highlight current node for visualization
                if (agent.State.RunningNodeIndex > 0)
                {
                    agent.CurrentNode = new TreeExecutionHighlight
                    {
                        NodeIndex = agent.State.RunningNodeIndex,
                        Status = NodeStatus.Running,
                        Timestamp = time
                    };
                }
            }
        }
        
        // Action implementations
        private NodeStatus FindPatrolPoint(
            ref AgentBlackboard bb,
            ref BehaviorTreeState state,
            ref DemoContext ctx,
            int payload)
        {
            // Simple patrol point selection
            ctx.Agent.TargetPosition = GetRandomPatrolPoint();
            bb.PatrolPointIndex = (bb.PatrolPointIndex + 1) % 4;
            return NodeStatus.Success;
        }
        
        private NodeStatus MoveToTarget(
            ref AgentBlackboard bb,
            ref BehaviorTreeState state,
            ref DemoContext ctx,
            int payload)
        {
            var distance = Vector2.Distance(ctx.Agent.Position, ctx.Agent.TargetPosition);
            
            if (distance < 10f)
                return NodeStatus.Success;
            
            return NodeStatus.Running;
        }
        
        // ... other action implementations
    }
    
    public struct DemoContext : IAIContext
    {
        public float Time { get; set; }
        public float DeltaTime { get; set; }
        public Agent Agent { get; set; }
        
        // IAIContext implementation
        public int RequestRaycast(Vector3 origin, Vector3 direction, float maxDistance) => 0;
        public RaycastResult GetRaycastResult(int requestId) => new() { IsReady = true };
        public int RequestPath(Vector3 from, Vector3 to) => 0;
        public PathResult GetPathResult(int requestId) => new() { IsReady = true, Success = true };
        public float GetFloatParam(int index) => 1f;
        public int GetIntParam(int index) => 1;
    }
}
```

---

### Task 5: Real-Time Tree Visualization

**Objective:** Visualize behavior tree execution in real-time.

**File:** `demos/Fbt.Demo.Visual/UI/TreeVisualizer.cs`

```csharp
using ImGuiNET;
using Fbt;
using Fbt.Serialization;

namespace Fbt.Demo.Visual.UI
{
    public class TreeVisualPanel
    {
        public void Render(Agent agent, BehaviorTreeBlob blob)
        {
            ImGui.Begin($"Behavior Tree - Agent {agent.Id}");
            
            ImGui.Text($"Tree: {blob.TreeName}");
            ImGui.Text($"Running Node: {agent.State.RunningNodeIndex}");
            
            ImGui.Separator();
            
            // Render tree hierarchy
            RenderNode(blob, 0, agent.State.RunningNodeIndex, 0);
            
            ImGui.End();
        }
        
        private void RenderNode(BehaviorTreeBlob blob, int index, int runningIndex, int depth)
        {
            if (index >= blob.Nodes.Length) return;
            
            var node = blob.Nodes[index];
            string indent = new string(' ', depth * 2);
            
            // Highlight running node
            Vector4 color = index == runningIndex 
                ? new Vector4(1f, 1f, 0f, 1f) // Yellow for running
                : new Vector4(1f, 1f, 1f, 1f); // White otherwise
            
            ImGui.TextColored(color, $"{indent}[{index}] {node.Type}");
            
            // Render children
            int childIndex = index + 1;
            for (int i = 0; i < node.ChildCount; i++)
            {
                RenderNode(blob, childIndex, runningIndex, depth + 1);
                childIndex += blob.Nodes[childIndex].SubtreeOffset;
            }
        }
    }
}
```

---

### Task 6: Performance Monitoring

**Objective:** Display performance metrics in real-time.

**File:** Add to `DemoApp.cs`:

```csharp
private void RenderPerformancePanel()
{
    ImGui.Begin("Performance");
    
    ImGui.Text($"FPS: {Raylib.GetFPS()}");
    ImGui.Text($"Frame Time: {Raylib.GetFrameTime() * 1000:F2}ms");
    ImGui.Text($"Agents: {_agents.Count}");
    ImGui.Text($"Trees Ticked/Frame: {_agents.Count}");
    
    ImGui.Separator();
    
    // Estimate tick time
    float avgTickTime = EstimateAvgTickTime();
    ImGui.Text($"Avg Tick Time: {avgTickTime * 1000000:F0}ns");
    
    // Performance budget
    float frameBudget = 16.67f; // 60 FPS
    float usedBudget = avgTickTime * _agents.Count * 1000;
    float percentUsed = (usedBudget / frameBudget) * 100;
    
    ImGui.ProgressBar(percentUsed / 100, new Vector2(-1, 0), $"{percentUsed:F1}% of frame");
    
    ImGui.End();
}
```

---

###Task 7: Example Trees

**Objective:** Create demo behavior trees.

**File:** `demos/Fbt.Demo.Visual/Trees/patrol.json`

```json
{
  "TreeName": "SimplePatrol",
  "Root": {
    "Type": "Repeater",
    "RepeatCount": -1,
    "Children": [
      {
        "Type": "Sequence",
        "Children": [
          { "Type": "Action", "Action": "FindPatrolPoint" },
          { "Type": "Action", "Action": "MoveToTarget" },
          { "Type": "Wait", "Duration": 2.0 }
        ]
      }
    ]
  }
}
```

**File:** `demos/Fbt.Demo.Visual/Trees/gather.json`

```json
{
  "TreeName": "ResourceGatherer",
  "Root": {
    "Type": "Repeater",
    "RepeatCount": -1,
    "Children": [
      {
        "Type": "Sequence",
        "Children": [
          { "Type": "Action", "Action": "FindResource" },
          { "Type": "Action", "Action": "MoveToTarget" },
          { "Type": "Action", "Action": "Gather" },
          { "Type": "Action", "Action": "ReturnToBase" }
        ]
      }
    ]
  }
}
```

---

## 📊 Deliverables

**Application:**
- [ ] `demos/Fbt.Demo.Visual/DemoApp.cs` (main application)
- [ ] `demos/Fbt.Demo.Visual/Program.cs` (entry point)
- [ ] `demos/Fbt.Demo.Visual/Entities/Agent.cs`
- [ ] `demos/Fbt.Demo.Visual/Systems/BehaviorSystem.cs`
- [ ] `demos/Fbt.Demo.Visual/Systems/RenderSystem.cs`
- [ ] `demos/Fbt.Demo.Visual/UI/TreeVisualizer.cs`

**Trees:**
- [ ] `demos/Fbt.Demo.Visual/Trees/patrol.json`
- [ ] `demos/Fbt.Demo.Visual/Trees/gather.json`
- [ ] `demos/Fbt.Demo.Visual/Trees/combat.json`

**Documentation:**
- [ ] `demos/Fbt.Demo.Visual/README.md` (how to run)

---

## ✅ Definition of Done

**Batch is DONE when:**

1. **Visual Demo Works**
   - [x] Window opens, renders at 60 FPS
   - [x] Agents spawn and move
   - [x] Behavior trees execute visually
   - [x] ImGui UI responsive

2. **Features Complete**
   - [x] Real-time tree visualization
   - [x] Agent inspector shows tree state
   - [x] Performance metrics displayed
   - [x] Interactive controls (pause, spawn, etc.)

3. **Performance**
   - [x] 60 FPS with 100+ agents
   - [x] Smooth rendering
   - [x] No noticeable lag

4. **Polish**
   - [x] Visually appealing
   - [x] Clear UI layout
   - [x] README with screenshots

---

## 🎯 Success Criteria

**You succeed when:**
- ✅ Demo looks impressive (marketing-ready!)
- ✅ Clearly shows FastBTree capabilities
- ✅ Runs smoothly with many agents
- ✅ Tree visualization is clear and informative
- ✅ Ready to share/showcase

**Estimated Time:** 4-5 days

---

**This batch creates a visual showcase for FastBTree v1.0! 🎨**

*Batch Issued: 2026-01-05*  
*Development Leader: FastBTree Team Lead*  
*Prerequisites: BATCH-01-06 Complete ✅*  
*Milestone: Visual Demo Application*
