# BATCH-15: Visual Demo Refactor (BTree → HSM)

**Batch Number:** BATCH-15  
**Tasks:** Demo Showcase  
**Phase:** Phase E - Examples & Polish  
**Estimated Effort:** 2-3 days  
**Priority:** HIGH  
**Dependencies:** BATCH-12.1 (Core HSM complete)

---

## 📋 Onboarding & Workflow

### Developer Instructions

Welcome to **BATCH-15**! This batch refactors the Raylib visual demo from FastBTree to FastHSM.

You'll create an interactive visual demo showcasing:
- Hierarchical state machines in action
- Real-time state visualization
- Event-driven transitions
- Manual event injection
- Performance metrics

### Required Reading (IN ORDER)

1. **Design Document:** `.dev-workstream/batches/DEMO-REFACTOR-DESIGN.md` - Complete design
2. **Quick Start:** `.dev-workstream/batches/DEMO-REFACTOR-QUICKSTART.md` - Implementation guide
3. **Existing Demo:** `demos/Fhsm.Demo.Visual/` - Current BTree demo
4. **Console Example:** `examples/Fhsm.Examples.Console/TrafficLightExample.cs` - HSM reference

### Source Code Location

- **Demo Project:** `demos/Fhsm.Demo.Visual/`
- **Agent:** `Entities/Agent.cs` (UPDATE)
- **Machines:** `MachineDefinitions.cs` (NEW)
- **Actions:** `Actions.cs` (NEW)
- **System:** `Systems/BehaviorSystem.cs` (UPDATE)
- **Visualizer:** `UI/StateMachineVisualizer.cs` (NEW)
- **Main:** `DemoApp.cs` (UPDATE)

---

## Context

**Current state:** Demo uses FastBTree with JSON-defined behavior trees.  
**Goal:** Convert to FastHSM with C#-defined state machines.

**Why HSM is better for this demo:**
- Clearer state-based logic (Patrolling, Gathering, Engaging)
- History states (combat agents return to patrol)
- Interrupt transitions (enemy detection)
- Better visualization (state hierarchy)

**Related Documents:**
- DEMO-REFACTOR-DESIGN.md - Full design
- DEMO-REFACTOR-QUICKSTART.md - Quick reference

---

## 🎯 Batch Objectives

Transform the demo to showcase FastHSM:
- 3 working state machines (Patrol, Gather, Combat)
- Real-time state visualization
- Interactive event injection
- Transition history display
- Performance metrics
- Smooth 60 FPS with 20+ agents

---

## ✅ Tasks

---

### Task 1: Update Agent Structure

**File:** `demos/Fhsm.Demo.Visual/Entities/Agent.cs`

**Changes:**

1. **Replace BTree references with HSM:**

```csharp
// OLD (remove):
using Fbt;
using Fbt.Runtime;

public string TreeName;
public AgentBlackboard Blackboard = new AgentBlackboard(); 
public BehaviorTreeState State;
public TreeExecutionHighlight? CurrentNode;

// NEW (add):
using Fhsm.Kernel;
using Fhsm.Kernel.Data;

public string MachineName;
public unsafe HsmInstance64 Instance;
public AgentContext Context;
public ushort[] ActiveStates = Array.Empty<ushort>();
public List<TransitionRecord> RecentTransitions = new();
```

2. **Update constructor:**

```csharp
// OLD:
public Agent(int id, Vector2 position, string treeName, AgentRole role)
{
    Id = id;
    Position = position;
    TreeName = treeName;
    Role = role;
    State = new BehaviorTreeState();
    // ...
}

// NEW:
public Agent(int id, Vector2 position, string machineName, AgentRole role)
{
    Id = id;
    Position = position;
    MachineName = machineName;
    Role = role;
    Context = new AgentContext { Agent = this };
    // Instance will be initialized by BehaviorSystem
    
    Color = role switch
    {
        AgentRole.Patrol => Color.Blue,
        AgentRole.Gather => Color.Green,
        AgentRole.Combat => Color.Red,
        _ => Color.White
    };
}
```

3. **Replace AgentBlackboard with AgentContext:**

```csharp
// OLD: Remove AgentBlackboard struct entirely

// NEW:
public struct AgentContext
{
    // Patrol
    public int PatrolPointIndex;
    public float LastPatrolTime;
    
    // Gather
    public int ResourceCount;
    public Vector2 ResourcePosition;
    public Vector2 BasePosition;
    
    // Combat
    public bool HasTarget;
    public int TargetAgentId;
    public Vector2 TargetPosition;
    
    // Shared
    public float DistanceToTarget;
    public float DistanceToBase;
    public float Time;
    public float DeltaTime;
    
    // Reference to agent (for actions to update agent state)
    public Agent Agent;
}
```

4. **Add transition record:**

```csharp
public struct TransitionRecord
{
    public ushort FromState;
    public ushort ToState;
    public ushort EventId;
    public float Timestamp;
}
```

5. **Remove TreeExecutionHighlight** (no longer needed)

---

### Task 2: Create State Machine Definitions

**File:** `demos/Fhsm.Demo.Visual/MachineDefinitions.cs` (NEW)

**Create class with event IDs and machine builders:**

```csharp
using System;
using Fhsm.Kernel;
using Fhsm.Kernel.Data;
using Fhsm.Compiler;

namespace Fhsm.Demo.Visual
{
    public static class MachineDefinitions
    {
        // Event IDs
        public const ushort PointSelected = 1;
        public const ushort Arrived = 2;
        public const ushort TimerExpired = 3;
        public const ushort ResourceFound = 4;
        public const ushort ResourceCollected = 5;
        public const ushort ResourcesDeposited = 6;
        public const ushort EnemyDetected = 7;
        public const ushort EnemyLost = 8;
        public const ushort UpdateEvent = 9;
        
        public static HsmDefinitionBlob CreatePatrolMachine()
        {
            var builder = new HsmBuilder("Patrol");
            
            // Root state
            var root = builder.Root();
            
            // Patrolling composite
            var patrolling = builder.State("Patrolling");
            root.AddChild(patrolling);
            
            // Sub-states
            var selecting = builder.State("SelectingPoint")
                .OnEntry("FindPatrolPoint");
                
            var moving = builder.State("Moving")
                .Activity("MoveToTarget");
                
            var waiting = builder.State("Waiting");
            
            patrolling.AddChild(selecting);
            patrolling.AddChild(moving);
            patrolling.AddChild(waiting);
            
            // Transitions
            selecting.On(PointSelected).GoTo(moving);
            moving.On(Arrived).GoTo(waiting);
            waiting.On(TimerExpired).GoTo(selecting);
            
            // Initial state
            selecting.Initial();
            
            // Compile
            return CompileAndEmit(builder);
        }
        
        public static HsmDefinitionBlob CreateGatherMachine()
        {
            var builder = new HsmBuilder("Gather");
            
            var root = builder.Root();
            var gathering = builder.State("Gathering");
            root.AddChild(gathering);
            
            // States
            var searching = builder.State("Searching")
                .OnEntry("FindResource");
                
            var movingToResource = builder.State("MovingToResource")
                .Activity("MoveToResource");
                
            var harvesting = builder.State("Harvesting")
                .OnEntry("Gather");
                
            var movingToBase = builder.State("MovingToBase")
                .Activity("MoveToBase");
                
            var depositing = builder.State("Depositing")
                .OnEntry("DepositResources");
            
            gathering.AddChild(searching);
            gathering.AddChild(movingToResource);
            gathering.AddChild(harvesting);
            gathering.AddChild(movingToBase);
            gathering.AddChild(depositing);
            
            // Transitions
            searching.On(ResourceFound).GoTo(movingToResource);
            movingToResource.On(Arrived).GoTo(harvesting);
            harvesting.On(ResourceCollected).GoTo(movingToBase);
            movingToBase.On(Arrived).GoTo(depositing);
            depositing.On(ResourcesDeposited).GoTo(searching);
            
            searching.Initial();
            
            return CompileAndEmit(builder);
        }
        
        public static HsmDefinitionBlob CreateCombatMachine()
        {
            var builder = new HsmBuilder("Combat");
            
            var root = builder.Root();
            var combat = builder.State("Combat");
            root.AddChild(combat);
            
            // Patrolling (with deep history)
            var patrolling = builder.State("Patrolling");
            // Note: History state support requires StateFlags.HasHistory
            // For v1, we'll simulate it or keep it simple
            
            var wandering = builder.State("Wandering")
                .OnEntry("FindRandomPoint")
                .Activity("MoveToTarget");
                
            var scanning = builder.State("Scanning")
                .Activity("ScanForEnemy");
            
            patrolling.AddChild(wandering);
            patrolling.AddChild(scanning);
            
            // Engaging
            var engaging = builder.State("Engaging");
            
            var chasing = builder.State("Chasing")
                .Activity("ChaseEnemy");
                
            var attacking = builder.State("Attacking")
                .OnEntry("Attack");
            
            engaging.AddChild(chasing);
            engaging.AddChild(attacking);
            
            combat.AddChild(patrolling);
            combat.AddChild(engaging);
            
            // Transitions within Patrolling
            wandering.On(Arrived).GoTo(scanning);
            scanning.On(UpdateEvent).GoTo(wandering);
            
            // Transitions within Engaging
            chasing.On(Arrived).GoTo(attacking);
            attacking.On(UpdateEvent).GoTo(chasing);
            
            // Cross-composite transitions
            // EnemyDetected: Patrolling → Engaging
            patrolling.On(EnemyDetected)
                .If("HasTarget")
                .GoTo(chasing);
            
            // EnemyLost: Engaging → Patrolling
            engaging.On(EnemyLost).GoTo(wandering);
            
            // Initial states
            wandering.Initial();
            chasing.Initial();
            
            return CompileAndEmit(builder);
        }
        
        private static HsmDefinitionBlob CompileAndEmit(HsmBuilder builder)
        {
            var graph = builder.Build();
            HsmNormalizer.Normalize(graph);
            
            if (!HsmGraphValidator.Validate(graph, out var errors))
            {
                throw new Exception($"Machine validation failed: {string.Join(", ", errors)}");
            }
            
            var flattened = HsmFlattener.Flatten(graph);
            return HsmEmitter.Emit(flattened);
        }
    }
}
```

---

### Task 3: Implement Actions and Guards

**File:** `demos/Fhsm.Demo.Visual/Actions.cs` (NEW)

**Implement all actions/guards:**

```csharp
using System;
using System.Numerics;
using Fhsm.Kernel.Attributes;
using Fhsm.Kernel.Data;

namespace Fhsm.Demo.Visual
{
    public static unsafe class Actions
    {
        private static Random _random = new Random();
        
        // ==================== PATROL ACTIONS ====================
        
        [HsmAction(Name = "FindPatrolPoint")]
        public static void FindPatrolPoint(void* instance, void* context, ushort eventId)
        {
            var ctx = (AgentContext*)context;
            var target = new Vector2(_random.Next(100, 1180), _random.Next(100, 620));
            
            ctx->TargetPosition = target;
            ctx->Agent.TargetPosition = target;
            ctx->PatrolPointIndex = (ctx->PatrolPointIndex + 1) % 4;
            
            // Immediately fire PointSelected event
            var evt = new HsmEvent { EventId = MachineDefinitions.PointSelected };
            var inst = (HsmInstance64*)instance;
            HsmEventQueue.TryEnqueue(inst, 64, evt);
        }
        
        [HsmAction(Name = "MoveToTarget")]
        public static void MoveToTarget(void* instance, void* context, ushort eventId)
        {
            var ctx = (AgentContext*)context;
            var agent = ctx->Agent;
            
            // Calculate distance
            var distance = Vector2.Distance(agent.Position, agent.TargetPosition);
            ctx->DistanceToTarget = distance;
            
            // If arrived, fire event
            if (distance < 10f)
            {
                var evt = new HsmEvent { EventId = MachineDefinitions.Arrived };
                var inst = (HsmInstance64*)instance;
                HsmEventQueue.TryEnqueue(inst, 64, evt);
            }
        }
        
        // ==================== GATHER ACTIONS ====================
        
        [HsmAction(Name = "FindResource")]
        public static void FindResource(void* instance, void* context, ushort eventId)
        {
            var ctx = (AgentContext*)context;
            var resourcePos = new Vector2(_random.Next(100, 1180), _random.Next(100, 620));
            
            ctx->ResourcePosition = resourcePos;
            ctx->TargetPosition = resourcePos;
            ctx->Agent.TargetPosition = resourcePos;
            
            var evt = new HsmEvent { EventId = MachineDefinitions.ResourceFound };
            var inst = (HsmInstance64*)instance;
            HsmEventQueue.TryEnqueue(inst, 64, evt);
        }
        
        [HsmAction(Name = "MoveToResource")]
        public static void MoveToResource(void* instance, void* context, ushort eventId)
        {
            var ctx = (AgentContext*)context;
            var distance = Vector2.Distance(ctx->Agent.Position, ctx->ResourcePosition);
            ctx->DistanceToTarget = distance;
            
            if (distance < 10f)
            {
                var evt = new HsmEvent { EventId = MachineDefinitions.Arrived };
                var inst = (HsmInstance64*)instance;
                HsmEventQueue.TryEnqueue(inst, 64, evt);
            }
        }
        
        [HsmAction(Name = "Gather")]
        public static void Gather(void* instance, void* context, ushort eventId)
        {
            var ctx = (AgentContext*)context;
            ctx->ResourceCount++;
            
            var evt = new HsmEvent { EventId = MachineDefinitions.ResourceCollected };
            var inst = (HsmInstance64*)instance;
            HsmEventQueue.TryEnqueue(inst, 64, evt);
        }
        
        [HsmAction(Name = "MoveToBase")]
        public static void MoveToBase(void* instance, void* context, ushort eventId)
        {
            var ctx = (AgentContext*)context;
            var basePos = new Vector2(50, 50);
            ctx->BasePosition = basePos;
            ctx->Agent.TargetPosition = basePos;
            
            var distance = Vector2.Distance(ctx->Agent.Position, basePos);
            ctx->DistanceToBase = distance;
            
            if (distance < 20f)
            {
                var evt = new HsmEvent { EventId = MachineDefinitions.Arrived };
                var inst = (HsmInstance64*)instance;
                HsmEventQueue.TryEnqueue(inst, 64, evt);
            }
        }
        
        [HsmAction(Name = "DepositResources")]
        public static void DepositResources(void* instance, void* context, ushort eventId)
        {
            var ctx = (AgentContext*)context;
            ctx->ResourceCount = 0;
            
            var evt = new HsmEvent { EventId = MachineDefinitions.ResourcesDeposited };
            var inst = (HsmInstance64*)instance;
            HsmEventQueue.TryEnqueue(inst, 64, evt);
        }
        
        // ==================== COMBAT ACTIONS ====================
        
        [HsmAction(Name = "FindRandomPoint")]
        public static void FindRandomPoint(void* instance, void* context, ushort eventId)
        {
            var ctx = (AgentContext*)context;
            var target = new Vector2(_random.Next(100, 1180), _random.Next(100, 620));
            
            ctx->TargetPosition = target;
            ctx->Agent.TargetPosition = target;
            
            var evt = new HsmEvent { EventId = MachineDefinitions.PointSelected };
            var inst = (HsmInstance64*)instance;
            HsmEventQueue.TryEnqueue(inst, 64, evt);
        }
        
        [HsmAction(Name = "ScanForEnemy")]
        public static void ScanForEnemy(void* instance, void* context, ushort eventId)
        {
            // This is handled by BehaviorSystem.UpdateCombatScanning()
            // which injects EnemyDetected events
            
            // For now, just periodically return to wandering
            // (UpdateEvent is fired by BehaviorSystem)
        }
        
        [HsmAction(Name = "ChaseEnemy")]
        public static void ChaseEnemy(void* instance, void* context, ushort eventId)
        {
            var ctx = (AgentContext*)context;
            
            if (!ctx->HasTarget)
            {
                // Lost target, fire EnemyLost
                var lostEvt = new HsmEvent { EventId = MachineDefinitions.EnemyLost };
                var inst = (HsmInstance64*)instance;
                HsmEventQueue.TryEnqueue(inst, 64, lostEvt);
                return;
            }
            
            // Update agent target position (BehaviorSystem updates this with enemy position)
            ctx->Agent.TargetPosition = ctx->TargetPosition;
            
            // Check if in attack range
            var distance = Vector2.Distance(ctx->Agent.Position, ctx->TargetPosition);
            if (distance < 30f)
            {
                var evt = new HsmEvent { EventId = MachineDefinitions.Arrived };
                var inst = (HsmInstance64*)instance;
                HsmEventQueue.TryEnqueue(inst, 64, evt);
            }
        }
        
        [HsmAction(Name = "Attack")]
        public static void Attack(void* instance, void* context, ushort eventId)
        {
            var ctx = (AgentContext*)context;
            ctx->Agent.AttackFlashTimer = 0.3f;
            
            // Lose target after attack
            ctx->HasTarget = false;
            ctx->TargetAgentId = -1;
            
            var evt = new HsmEvent { EventId = MachineDefinitions.EnemyLost };
            var inst = (HsmInstance64*)instance;
            HsmEventQueue.TryEnqueue(inst, 64, evt);
        }
        
        // ==================== GUARDS ====================
        
        [HsmGuard(Name = "HasTarget")]
        public static bool HasTarget(void* instance, void* context, ushort eventId)
        {
            var ctx = (AgentContext*)context;
            return ctx->HasTarget;
        }
        
        [HsmGuard(Name = "IsAtTarget")]
        public static bool IsAtTarget(void* instance, void* context, ushort eventId)
        {
            var ctx = (AgentContext*)context;
            return ctx->DistanceToTarget < 10f;
        }
        
        [HsmGuard(Name = "IsAtBase")]
        public static bool IsAtBase(void* instance, void* context, ushort eventId)
        {
            var ctx = (AgentContext*)context;
            return ctx->DistanceToBase < 20f;
        }
    }
}
```

---

### Task 4: Refactor BehaviorSystem

**File:** `demos/Fhsm.Demo.Visual/Systems/BehaviorSystem.cs`

**Complete rewrite:**

```csharp
using System;
using System.Numerics;
using System.Collections.Generic;
using Fhsm.Kernel;
using Fhsm.Kernel.Data;

namespace Fhsm.Demo.Visual
{
    public unsafe class BehaviorSystem
    {
        private Dictionary<string, HsmDefinitionBlob> _machines;
        private List<Agent>? _currentAgents;
        private Random _random = new Random();
        
        public BehaviorSystem()
        {
            // Create state machines
            _machines = new Dictionary<string, HsmDefinitionBlob>
            {
                ["patrol"] = MachineDefinitions.CreatePatrolMachine(),
                ["gather"] = MachineDefinitions.CreateGatherMachine(),
                ["combat"] = MachineDefinitions.CreateCombatMachine()
            };
        }
        
        public void InitializeAgent(Agent agent)
        {
            if (!_machines.TryGetValue(agent.MachineName, out var blob))
            {
                throw new Exception($"Machine '{agent.MachineName}' not found");
            }
            
            // Initialize instance
            fixed (HsmInstance64* inst = &agent.Instance)
            {
                HsmInstanceManager.Initialize(inst, blob);
            }
            
            // Trigger initial entry
            HsmKernel.Trigger(ref agent.Instance);
        }
        
        public void Update(List<Agent> agents, float time, float dt)
        {
            _currentAgents = agents;
            
            // Update combat agent scanning
            UpdateCombatScanning(agents);
            
            // Update all agents
            foreach (var agent in agents)
            {
                if (!_machines.TryGetValue(agent.MachineName, out var blob))
                    continue;
                
                // Update attack flash timer
                if (agent.AttackFlashTimer > 0)
                    agent.AttackFlashTimer -= dt;
                
                // Prepare context
                agent.Context.Time = time;
                agent.Context.DeltaTime = dt;
                agent.Context.Agent = agent;
                
                // Tick HSM
                fixed (AgentContext* ctx = &agent.Context)
                {
                    HsmKernel.Update(blob, ref agent.Instance, agent.Context, dt);
                }
                
                // Update active states for visualization
                UpdateActiveStates(agent, blob);
                
                // Fire periodic UpdateEvent for combat agents
                if (agent.Role == AgentRole.Combat && time - agent.Context.LastPatrolTime > 3.0f)
                {
                    agent.Context.LastPatrolTime = time;
                    var evt = new HsmEvent { EventId = MachineDefinitions.UpdateEvent };
                    fixed (HsmInstance64* inst = &agent.Instance)
                    {
                        HsmEventQueue.TryEnqueue(inst, 64, evt);
                    }
                }
                
                // Fire TimerExpired for patrol/gather agents
                if (agent.Role == AgentRole.Patrol && time - agent.Context.LastPatrolTime > 2.0f)
                {
                    agent.Context.LastPatrolTime = time;
                    var evt = new HsmEvent { EventId = MachineDefinitions.TimerExpired };
                    fixed (HsmInstance64* inst = &agent.Instance)
                    {
                        HsmEventQueue.TryEnqueue(inst, 64, evt);
                    }
                }
            }
        }
        
        private void UpdateCombatScanning(List<Agent> agents)
        {
            foreach (var agent in agents)
            {
                if (agent.Role != AgentRole.Combat) continue;
                if (agent.Context.HasTarget) continue;
                
                // Find closest non-combat agent
                float closestDistSq = 300f * 300f;
                Agent? target = null;
                
                foreach (var other in agents)
                {
                    if (other == agent) continue;
                    if (other.Role == AgentRole.Combat) continue;
                    
                    float dSq = Vector2.DistanceSquared(agent.Position, other.Position);
                    if (dSq < closestDistSq)
                    {
                        closestDistSq = dSq;
                        target = other;
                    }
                }
                
                if (target != null)
                {
                    agent.Context.HasTarget = true;
                    agent.Context.TargetAgentId = target.Id;
                    agent.Context.TargetPosition = target.Position;
                    agent.TargetPosition = target.Position;
                    
                    // Fire EnemyDetected event
                    var evt = new HsmEvent 
                    { 
                        EventId = MachineDefinitions.EnemyDetected,
                        Priority = EventPriority.Interrupt 
                    };
                    
                    fixed (HsmInstance64* inst = &agent.Instance)
                    {
                        HsmEventQueue.TryEnqueue(inst, 64, evt);
                    }
                }
                else
                {
                    // Random chance to investigate
                    if (_random.NextDouble() < 0.01) // 1% per frame
                    {
                        agent.Context.HasTarget = true;
                        agent.Context.TargetAgentId = -1;
                        agent.Context.TargetPosition = new Vector2(_random.Next(100, 1180), _random.Next(100, 620));
                        
                        var evt = new HsmEvent { EventId = MachineDefinitions.EnemyDetected };
                        fixed (HsmInstance64* inst = &agent.Instance)
                        {
                            HsmEventQueue.TryEnqueue(inst, 64, evt);
                        }
                    }
                }
            }
        }
        
        private void UpdateActiveStates(Agent agent, HsmDefinitionBlob blob)
        {
            // Read active leaf states from instance
            fixed (HsmInstance64* inst = &agent.Instance)
            {
                byte* ptr = (byte*)inst;
                
                // Active leaf IDs are stored after the header (16 bytes)
                // Offset: 16 (header) + potential padding
                // For HsmInstance64: header (16B) + activeLeafs (regionCount * 2B)
                int regionCount = blob.Header.OrthogonalRegionCount;
                if (regionCount == 0) regionCount = 1; // At least one region
                
                ushort* activeLeafs = (ushort*)(ptr + 16);
                
                agent.ActiveStates = new ushort[regionCount];
                for (int i = 0; i < regionCount; i++)
                {
                    agent.ActiveStates[i] = activeLeafs[i];
                }
            }
        }
        
        public Dictionary<string, HsmDefinitionBlob> GetMachines() => _machines;
    }
}
```

---

### Task 5: Create State Machine Visualizer

**File:** `demos/Fhsm.Demo.Visual/UI/StateMachineVisualizer.cs` (NEW)

**Complete implementation:**

```csharp
using System;
using System.Linq;
using System.Numerics;
using ImGuiNET;
using Fhsm.Kernel;
using Fhsm.Kernel.Data;

namespace Fhsm.Demo.Visual.UI
{
    public unsafe class StateMachineVisualizer
    {
        public void Render(Agent agent, HsmDefinitionBlob blob, float time)
        {
            ImGui.SetNextWindowSize(new Vector2(400, 600), ImGuiCond.FirstUseEver);
            ImGui.Begin($"State Machine: Agent {agent.Id}");
            
            // Header
            ImGui.TextColored(new Vector4(1, 1, 0, 1), $"Agent {agent.Id}");
            ImGui.SameLine();
            ImGui.Text($"({agent.Role})");
            
            ImGui.Separator();
            
            // Basic info
            ImGui.Text($"Machine: {agent.MachineName}");
            ImGui.Text($"Position: ({agent.Position.X:F0}, {agent.Position.Y:F0})");
            ImGui.Text($"Target: ({agent.TargetPosition.X:F0}, {agent.TargetPosition.Y:F0})");
            
            ImGui.Separator();
            
            // Active states
            RenderActiveStates(agent, blob);
            
            ImGui.Separator();
            
            // State hierarchy
            RenderStateHierarchy(agent, blob);
            
            ImGui.Separator();
            
            // Context data
            RenderContext(agent);
            
            ImGui.Separator();
            
            // Transition history
            RenderTransitionHistory(agent);
            
            ImGui.Separator();
            
            // Manual event controls
            RenderEventControls(agent);
            
            ImGui.End();
        }
        
        private void RenderActiveStates(Agent agent, HsmDefinitionBlob blob)
        {
            ImGui.TextColored(new Vector4(0, 1, 0, 1), "Active States:");
            
            if (agent.ActiveStates != null && agent.ActiveStates.Length > 0)
            {
                foreach (var stateId in agent.ActiveStates)
                {
                    if (stateId == 0xFFFF) continue;
                    
                    ImGui.BulletText($"State {stateId}");
                }
            }
            else
            {
                ImGui.TextDisabled("(none)");
            }
        }
        
        private void RenderStateHierarchy(Agent agent, HsmDefinitionBlob blob)
        {
            if (ImGui.CollapsingHeader("State Hierarchy", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.Indent();
                
                // Find root state(s)
                for (ushort i = 0; i < blob.Header.StateCount; i++)
                {
                    var state = blob.States[i];
                    if (state.ParentIndex == 0xFFFF)
                    {
                        RenderStateNode(blob, i, agent.ActiveStates, 0);
                    }
                }
                
                ImGui.Unindent();
            }
        }
        
        private void RenderStateNode(HsmDefinitionBlob blob, ushort stateId, ushort[] activeStates, int depth)
        {
            var state = blob.States[stateId];
            bool isActive = activeStates != null && Array.Exists(activeStates, s => s == stateId);
            
            var color = isActive ? new Vector4(0, 1, 0, 1) : new Vector4(0.7f, 0.7f, 0.7f, 1);
            var prefix = new string(' ', depth * 2);
            
            bool hasChildren = state.FirstChildIndex != 0xFFFF;
            
            if (hasChildren)
            {
                bool nodeOpen = ImGui.TreeNodeEx($"{prefix}State {stateId}##node{stateId}", 
                    isActive ? ImGuiTreeNodeFlags.DefaultOpen : ImGuiTreeNodeFlags.None);
                
                if (isActive)
                {
                    ImGui.SameLine();
                    ImGui.TextColored(new Vector4(0, 1, 0, 1), " ◀ ACTIVE");
                }
                
                if (nodeOpen)
                {
                    // Render children
                    ushort childId = state.FirstChildIndex;
                    while (childId != 0xFFFF)
                    {
                        RenderStateNode(blob, childId, activeStates, depth + 1);
                        childId = blob.States[childId].NextSiblingIndex;
                    }
                    
                    ImGui.TreePop();
                }
            }
            else
            {
                ImGui.TextColored(color, $"{prefix}└ State {stateId}");
                
                if (isActive)
                {
                    ImGui.SameLine();
                    ImGui.TextColored(new Vector4(0, 1, 0, 1), " ◀ ACTIVE");
                }
            }
        }
        
        private void RenderContext(Agent agent)
        {
            if (ImGui.CollapsingHeader("Context Data"))
            {
                ImGui.Indent();
                
                ImGui.Text($"PatrolPointIndex: {agent.Context.PatrolPointIndex}");
                ImGui.Text($"ResourceCount: {agent.Context.ResourceCount}");
                ImGui.Text($"HasTarget: {agent.Context.HasTarget}");
                ImGui.Text($"TargetAgentId: {agent.Context.TargetAgentId}");
                ImGui.Text($"DistanceToTarget: {agent.Context.DistanceToTarget:F2}");
                
                ImGui.Unindent();
            }
        }
        
        private void RenderTransitionHistory(Agent agent)
        {
            if (ImGui.CollapsingHeader("Recent Transitions"))
            {
                ImGui.Indent();
                
                if (agent.RecentTransitions.Count == 0)
                {
                    ImGui.TextDisabled("(no transitions yet)");
                }
                else
                {
                    foreach (var trans in agent.RecentTransitions.TakeLast(10).Reverse())
                    {
                        ImGui.Text($"{trans.Timestamp:F2}s: {trans.FromState} → {trans.ToState} (Event: {trans.EventId})");
                    }
                }
                
                ImGui.Unindent();
            }
        }
        
        private void RenderEventControls(Agent agent)
        {
            if (ImGui.CollapsingHeader("Manual Events"))
            {
                ImGui.Indent();
                
                if (ImGui.Button("Trigger TimerExpired"))
                    InjectEvent(agent, MachineDefinitions.TimerExpired);
                
                if (ImGui.Button("Trigger EnemyDetected"))
                    InjectEvent(agent, MachineDefinitions.EnemyDetected);
                
                if (ImGui.Button("Trigger EnemyLost"))
                    InjectEvent(agent, MachineDefinitions.EnemyLost);
                
                if (ImGui.Button("Trigger Arrived"))
                    InjectEvent(agent, MachineDefinitions.Arrived);
                
                ImGui.Unindent();
            }
        }
        
        private void InjectEvent(Agent agent, ushort eventId)
        {
            var evt = new HsmEvent 
            { 
                EventId = eventId,
                Priority = EventPriority.Normal 
            };
            
            fixed (HsmInstance64* inst = &agent.Instance)
            {
                HsmEventQueue.TryEnqueue(inst, 64, evt);
            }
        }
    }
}
```

---

### Task 6: Update DemoApp

**File:** `demos/Fhsm.Demo.Visual/DemoApp.cs`

**Changes:**

1. **Update imports:**
```csharp
// Remove: using Fbt references
// Add:
using Fhsm.Kernel;
using Fhsm.Kernel.Data;
```

2. **Update fields:**
```csharp
// OLD:
private Dictionary<string, BehaviorTreeBlob> _trees = new();
private UI.TreeVisualPanel _treeVisualPanel = new UI.TreeVisualPanel();

// NEW:
private Dictionary<string, HsmDefinitionBlob> _machines = new();
private UI.StateMachineVisualizer _smVisualizer = new UI.StateMachineVisualizer();
```

3. **Update Initialize():**
```csharp
private void Initialize()
{
    // Create systems
    _behaviorSystem = new BehaviorSystem();
    _renderSystem = new RenderSystem();
    
    // Get machines
    _machines = _behaviorSystem.GetMachines();
    
    // Spawn initial agents
    SpawnPatrolAgents(5);
    SpawnGatherAgents(3);
    SpawnCombatAgents(2);
}
```

4. **Remove LoadTree() method entirely**

5. **Update spawn methods:**
```csharp
private void SpawnPatrolAgents(int count)
{
    for (int i = 0; i < count; i++)
    {
        var agent = new Agent(
            _agents.Count,
            GetRandomPosition(),
            "patrol",
            AgentRole.Patrol);
        
        _behaviorSystem.InitializeAgent(agent);
        _agents.Add(agent);
    }
}

private void SpawnGatherAgents(int count)
{
    for (int i = 0; i < count; i++)
    {
        var agent = new Agent(
            _agents.Count,
            GetRandomPosition(),
            "gather",
            AgentRole.Gather);
        
        _behaviorSystem.InitializeAgent(agent);
        _agents.Add(agent);
    }
}

private void SpawnCombatAgents(int count)
{
    for (int i = 0; i < count; i++)
    {
        var agent = new Agent(
            _agents.Count,
            GetRandomPosition(),
            "combat",
            AgentRole.Combat);
        
        _behaviorSystem.InitializeAgent(agent);
        _agents.Add(agent);
    }
}

private Vector2 GetRandomPosition()
{
    var random = new Random();
    return new Vector2(random.Next(100, 1180), random.Next(100, 620));
}
```

6. **Update RenderUI():**
```csharp
private void RenderUI()
{
    ImGui.Begin("FastHSM Demo");  // Changed from FastBTree
    
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
        
    if (ImGui.Button("Spawn Combat Agent"))
        SpawnCombatAgents(1);
    
    if (ImGui.Button("Clear All"))
    {
        _agents.Clear();
        _selectedAgent = null;
    }

    ImGui.Separator();
    
    if (ImGui.CollapsingHeader("Agents List"))
    {
        foreach(var agent in _agents)
        {
            bool isSelected = _selectedAgent == agent;
            if (ImGui.Selectable($"Agent {agent.Id} ({agent.Role})", isSelected))
            {
                _selectedAgent = agent;
            }
        }
    }
    
    ImGui.End();
    
    // Selected agent details
    if (_selectedAgent != null)
    {
        if (_machines.TryGetValue(_selectedAgent.MachineName, out var blob))
        {
            _smVisualizer.Render(_selectedAgent, blob, _time);
        }
    }
}
```

7. **Update window title:**
```csharp
public void Run()
{
    Raylib.InitWindow(ScreenWidth, ScreenHeight, "FastHSM Visual Demo");
    // ... rest unchanged
}
```

---

### Task 7: Update Project References

**File:** `demos/Fhsm.Demo.Visual/Fhsm.Demo.Visual.csproj`

**Update:**
```xml
<ItemGroup>
  <!-- Remove BTree references -->
  <!-- <ProjectReference Include="..\..\path\to\FastBTree.csproj" /> -->
  
  <!-- Add HSM references -->
  <ProjectReference Include="..\..\src\Fhsm.Kernel\Fhsm.Kernel.csproj" />
  <ProjectReference Include="..\..\src\Fhsm.Compiler\Fhsm.Compiler.csproj" />
  <ProjectReference Include="..\..\src\Fhsm.SourceGen\Fhsm.SourceGen.csproj" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
</ItemGroup>
```

---

### Task 8: Update README

**File:** `demos/Fhsm.Demo.Visual/README.md`

**Replace content:**
```markdown
# Fhsm.Demo.Visual

Raylib-based visual demonstration of the FastHSM library.

## Features

- **2D Agent Simulation**: Watch agents perform different behaviors (Patrol, Gather, Combat).
- **Real-time State Machine Visualization**: Inspect the active HSM of any agent to see exactly what is going on.
- **Interactive Controls**: Pause, spawn agents, adjust time scale, inject events.
- **Performance Metrics**: Monitor FPS and tick efficiency.

## Controls

- **Mouse Left-Click**: Select an agent to inspect
- **Mouse Wheel**: Zoom camera
- **Pause**: Toggle simulation pause
- **Time Scale**: Slider to speed up or slow down time
- **Spawn Buttons**: Add more agents dynamically
- **Agent List**: Click to inspect state machine

## State Machines

The demo uses three state machines:

### Patrol
Simple patrol loop with waypoints.
- **States**: SelectingPoint → Moving → Waiting
- **Events**: PointSelected, Arrived, TimerExpired

### Gather
Resource gathering cycle.
- **States**: Searching → MovingToResource → Harvesting → MovingToBase → Depositing
- **Events**: ResourceFound, Arrived, ResourceCollected, ResourcesDeposited

### Combat
Enemy detection and engagement with history.
- **States**: Patrolling (Wandering ↔ Scanning) ↔ Engaging (Chasing ↔ Attacking)
- **Events**: EnemyDetected, EnemyLost, UpdateEvent
- **Features**: Interrupt transitions, history states

## Running the Demo

```bash
dotnet run --project demos/Fhsm.Demo.Visual
```

## State Machine Viewer

When you select an agent, you'll see:
- **Active States**: Currently executing states (green)
- **State Hierarchy**: Full state tree with parent/child relationships
- **Context Data**: Agent's internal state (resource count, targets, etc.)
- **Transition History**: Recent state changes
- **Manual Events**: Buttons to inject events for testing

## Performance

The demo maintains 60 FPS with 20+ agents. Each agent:
- Runs a hierarchical state machine (3-7 states)
- Processes events from a priority queue
- Executes activities and transitions
- Uses zero-allocation runtime (fixed 64B instances)

## Architecture

```
Agent (HsmInstance64 + Context)
    ↓
BehaviorSystem (HSM Update Loop)
    ↓
State Machine (compiled blob)
    ↓
Actions/Guards (source-generated dispatch)
```
```

---

## 🧪 Testing Requirements

### Manual Testing

**Test 1: Patrol Agents**
1. Spawn 5 patrol agents
2. Select one agent
3. Verify:
   - [ ] Agent moves between random points
   - [ ] States cycle: SelectingPoint → Moving → Waiting
   - [ ] Wait state lasts ~2 seconds
   - [ ] Active state shown in green

**Test 2: Gather Agents**
1. Spawn 3 gather agents
2. Select one agent
3. Verify:
   - [ ] Agent moves to resource
   - [ ] Harvests (ResourceCount increases)
   - [ ] Returns to base (top-left corner)
   - [ ] Deposits (ResourceCount resets to 0)
   - [ ] Repeats cycle

**Test 3: Combat Agents**
1. Spawn 2 combat agents + 3 patrol agents
2. Select combat agent
3. Verify:
   - [ ] Combat agent wanders when no enemies
   - [ ] Detects nearby patrol agents
   - [ ] Chases and attacks
   - [ ] Returns to patrolling after attack
   - [ ] Attack flash effect visible

**Test 4: State Visualization**
1. Select any agent
2. Verify UI shows:
   - [ ] Active states highlighted
   - [ ] State hierarchy tree
   - [ ] Context data (resource count, target, etc.)
   - [ ] Transition history

**Test 5: Manual Events**
1. Select patrol agent
2. Click "Trigger TimerExpired" button
3. Verify:
   - [ ] Agent immediately transitions to next state
   - [ ] Transition appears in history

**Test 6: Performance**
1. Spawn 20+ agents (mix of all types)
2. Verify:
   - [ ] FPS stays at 60
   - [ ] No stuttering
   - [ ] All agents move smoothly

---

## 📊 Success Criteria

- [ ] All 3 state machines working
- [ ] Agents spawn correctly
- [ ] State hierarchy displays correctly
- [ ] Active states highlighted in green
- [ ] Transitions trigger on events
- [ ] Guards evaluated (HasTarget, IsAtTarget, etc.)
- [ ] Activities run continuously (MoveToTarget)
- [ ] Entry actions fire (FindPatrolPoint, Gather, Attack)
- [ ] Manual event injection works
- [ ] Transition history tracks changes
- [ ] Context data updates
- [ ] Combat agents detect enemies
- [ ] Attack flash effect works
- [ ] 60 FPS with 20+ agents
- [ ] No crashes or exceptions
- [ ] Code compiles without warnings
- [ ] Project references correct

---

## ⚠️ Common Pitfalls

1. **Namespace Mismatch:** Ensure all files use `Fhsm.Demo.Visual`
2. **Unsafe Code:** Enable in .csproj: `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>`
3. **Source Generator:** Reference `Fhsm.SourceGen` as Analyzer
4. **Event Queue:** 64B instance has small queue, don't spam events
5. **Active States:** Update after each `HsmKernel.Update()`
6. **Pointer Safety:** Always use `fixed` blocks
7. **Context Lifetime:** Context is struct, passed by value to Update()

---

## 📚 Reference

- **Design:** `.dev-workstream/batches/DEMO-REFACTOR-DESIGN.md`
- **Quick Start:** `.dev-workstream/batches/DEMO-REFACTOR-QUICKSTART.md`
- **HSM Example:** `examples/Fhsm.Examples.Console/TrafficLightExample.cs`
- **Kernel API:** `src/Fhsm.Kernel/HsmKernel.cs`

---

**This is a complete showcase of FastHSM capabilities!** 🎮

**Estimated Time:** 2-3 days

**Questions?** See design documents or ask in questions file.
