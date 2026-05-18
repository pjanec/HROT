# Visual Demo Refactor: BTree → HSM

**Date:** 2026-01-11  
**Scope:** Convert `Fhsm.Demo.Visual` from behavior trees to hierarchical state machines  
**Status:** Design Phase

---

## 📋 Overview

Refactor the Raylib-based visual demo to showcase FastHSM instead of FastBTree. The demo should:
- Show agents using HSMs (Patrol, Gather, Combat)
- Visualize active states in real-time
- Allow interactive inspection of state machines
- Demonstrate transitions, events, guards, activities
- Show performance metrics

---

## 🎯 Design Goals

1. **Visual State Machine Viewer** - See hierarchy, active states, transitions
2. **Real-Time Event Injection** - Trigger events manually (stun, damage, etc.)
3. **Trace Visualization** - Show recent transitions
4. **Performance Monitoring** - Measure tick time, transition count
5. **Interactive Controls** - Spawn agents, pause, adjust time scale
6. **State Persistence** - Show history states working

---

## 🗺️ Architecture

### Current (BTree)
```
Agent
├── BehaviorTreeState (64 bytes)
├── AgentBlackboard (custom data)
└── TreeName (reference to blob)

BehaviorSystem
├── Interpreters (per tree)
└── ActionRegistry (actions/conditions)

TreeVisualizer
└── Renders tree structure + active node
```

### Target (HSM)
```
Agent
├── HsmInstance64 (64 bytes)
├── AgentContext (custom data)
└── MachineName (reference to blob)

BehaviorSystem
├── HsmDefinitionBlobs (per machine)
└── HsmActionDispatcher (actions/guards)

StateMachineVisualizer
├── Renders state hierarchy
├── Shows active states
├── Displays transitions
└── Trace buffer visualization
```

---

## 📐 State Machine Designs

### 1. Patrol Agent

**Hierarchy:**
```
Root
└── Patrolling
    ├── SelectingPoint (entry: FindPatrolPoint)
    ├── Moving (activity: MoveToTarget)
    └── Waiting (timer: 2.0s)
```

**Transitions:**
- SelectingPoint → Moving (event: PointSelected)
- Moving → Waiting (event: Arrived)
- Waiting → SelectingPoint (event: TimerExpired)

**Events:**
- PointSelected (internal)
- Arrived (internal)
- TimerExpired (from timer)

**States:**
```csharp
State: SelectingPoint
  OnEntry: FindPatrolPoint
  Transition: Auto → Moving (no guard)

State: Moving
  Activity: MoveToTarget (checks distance)
  Transition: Auto → Waiting (guard: IsAtTarget)

State: Waiting
  Timer: 2.0s
  Transition: TimerExpired → SelectingPoint
```

---

### 2. Gather Agent

**Hierarchy:**
```
Root
└── Gathering
    ├── SearchingResource
    │   └── Searching (entry: FindResource)
    ├── Collecting
    │   ├── MovingToResource (activity: MoveToResource)
    │   └── Harvesting (entry: Gather)
    └── Returning
        ├── MovingToBase (activity: MoveToBase)
        └── Depositing (entry: DepositResources)
```

**Transitions:**
- SearchingResource → Collecting (event: ResourceFound)
- Collecting → Returning (event: ResourceCollected)
- Returning → SearchingResource (event: ResourcesDeposited)

**Guards:**
- IsAtResource (distance check)
- IsAtBase (distance check)
- HasResources (resource count > 0)

---

### 3. Combat Agent

**Hierarchy:**
```
Root
└── Combat
    ├── Patrolling (history: shallow)
    │   ├── Wandering (entry: FindRandomPoint)
    │   └── Scanning (activity: ScanForEnemy)
    └── Engaging
        ├── Chasing (activity: ChaseEnemy)
        └── Attacking (entry: Attack)
```

**Global Transitions:**
- ANY → Engaging (event: EnemyDetected, guard: HasTarget)
- Engaging → Patrolling (event: EnemyLost)

**Key Features:**
- History state: Return to last patrol sub-state
- Interrupt events: Enemy detection
- Guards: Range checks, target validation

---

## 🔧 Implementation Plan

### Phase 1: Core Data Structures

**1.1 Update Agent.cs**
```csharp
public class Agent
{
    public int Id { get; set; }
    public Vector2 Position { get; set; }
    public Vector2 Velocity { get; set; }
    public float Rotation { get; set; }
    public Color Color { get; set; }
    
    // HSM (instead of BTree)
    public string MachineName;
    public unsafe HsmInstance64 Instance;  // Fixed-size state
    public AgentContext Context;           // Custom data (was Blackboard)
    
    // AI state
    public Vector2 TargetPosition;
    public float Speed = 50f;
    public AgentRole Role;
    
    // Visual state
    public ushort[] ActiveStates;         // For visualization
    public float AttackFlashTimer;
    
    // Trace visualization
    public List<TransitionRecord> RecentTransitions = new();
}
```

**1.2 Update AgentContext (was AgentBlackboard)**
```csharp
public struct AgentContext
{
    public int PatrolPointIndex;
    public float LastPatrolTime;
    public int ResourceCount;
    public bool HasTarget;
    public int TargetAgentId;
    public Vector2 TargetPosition;  // Moved from Agent
    
    // Distances (for guards)
    public float DistanceToTarget;
    public float DistanceToBase;
    
    // Timing
    public float Time;
    public float DeltaTime;
    
    // Reference to agent (for actions)
    public Agent Agent;  // Unsafe pointer alternative?
}
```

---

### Phase 2: State Machine Definitions

**2.1 Create MachineDefinitions.cs**

```csharp
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
        public const ushort UpdateEvent = 9; // Tick event
        
        public static HsmDefinitionBlob CreatePatrolMachine()
        {
            var builder = new HsmBuilder("Patrol");
            
            // Root
            var root = builder.Root();
            
            // Patrolling composite state
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
            
            // Set initial
            selecting.Initial();
            
            // Compile
            var graph = builder.Build();
            HsmNormalizer.Normalize(graph);
            
            if (!HsmGraphValidator.Validate(graph, out var errors))
            {
                throw new Exception($"Patrol machine invalid: {string.Join(", ", errors)}");
            }
            
            var flattened = HsmFlattener.Flatten(graph);
            return HsmEmitter.Emit(flattened);
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
            
            // Compile
            var graph = builder.Build();
            HsmNormalizer.Normalize(graph);
            
            if (!HsmGraphValidator.Validate(graph, out var errors))
            {
                throw new Exception($"Gather machine invalid: {string.Join(", ", errors)}");
            }
            
            var flattened = HsmFlattener.Flatten(graph);
            return HsmEmitter.Emit(flattened);
        }
        
        public static HsmDefinitionBlob CreateCombatMachine()
        {
            var builder = new HsmBuilder("Combat");
            
            var root = builder.Root();
            var combat = builder.State("Combat");
            root.AddChild(combat);
            
            // Patrolling (with history)
            var patrolling = builder.State("Patrolling");
            patrolling.Flags |= StateFlags.HasHistory | StateFlags.HistoryDeep;
            
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
            
            // Transitions
            wandering.On(Arrived).GoTo(scanning);
            scanning.On(UpdateEvent).GoTo(wandering);  // Periodic
            
            chasing.On(Arrived).GoTo(attacking);
            attacking.On(UpdateEvent).GoTo(chasing);   // Re-chase
            
            // Global transitions
            // EnemyDetected (anywhere in patrolling) → Engaging.Chasing
            builder.GlobalTransition()
                .From(patrolling)
                .On(EnemyDetected)
                .If("HasTarget")
                .GoTo(chasing);
            
            // EnemyLost (anywhere in engaging) → Patrolling (history)
            builder.GlobalTransition()
                .From(engaging)
                .On(EnemyLost)
                .GoTo(patrolling);  // Will restore history
            
            wandering.Initial();
            chasing.Initial();  // Initial for Engaging
            
            // Compile
            var graph = builder.Build();
            HsmNormalizer.Normalize(graph);
            
            if (!HsmGraphValidator.Validate(graph, out var errors))
            {
                throw new Exception($"Combat machine invalid: {string.Join(", ", errors)}");
            }
            
            var flattened = HsmFlattener.Flatten(graph);
            return HsmEmitter.Emit(flattened);
        }
    }
}
```

---

### Phase 3: Action/Guard Implementation

**3.1 Create Actions.cs**

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
        
        // --- Patrol Actions ---
        
        [HsmAction(Name = "FindPatrolPoint")]
        public static void FindPatrolPoint(void* instance, void* context, ushort eventId)
        {
            var ctx = (AgentContext*)context;
            var target = new Vector2(_random.Next(100, 1180), _random.Next(100, 620));
            ctx->TargetPosition = target;
            ctx->Agent.TargetPosition = target;
            
            // Fire event to advance state
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
        
        // --- Gather Actions ---
        
        [HsmAction(Name = "FindResource")]
        public static void FindResource(void* instance, void* context, ushort eventId)
        {
            var ctx = (AgentContext*)context;
            ctx->TargetPosition = new Vector2(_random.Next(100, 1180), _random.Next(100, 620));
            ctx->Agent.TargetPosition = ctx->TargetPosition;
            
            var evt = new HsmEvent { EventId = MachineDefinitions.ResourceFound };
            var inst = (HsmInstance64*)instance;
            HsmEventQueue.TryEnqueue(inst, 64, evt);
        }
        
        [HsmAction(Name = "MoveToResource")]
        public static void MoveToResource(void* instance, void* context, ushort eventId)
        {
            var ctx = (AgentContext*)context;
            var distance = Vector2.Distance(ctx->Agent.Position, ctx->TargetPosition);
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
        
        // --- Combat Actions ---
        
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
            var ctx = (AgentContext*)context;
            
            // This is called from BehaviorSystem which has access to all agents
            // For now, set flag in context that BehaviorSystem will check
            // (Better: pass agent list in context or use separate scanning system)
        }
        
        [HsmAction(Name = "ChaseEnemy")]
        public static void ChaseEnemy(void* instance, void* context, ushort eventId)
        {
            var ctx = (AgentContext*)context;
            
            // Update target position based on enemy position
            // (BehaviorSystem updates this)
            
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
        
        // --- Guards ---
        
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

### Phase 4: Update BehaviorSystem

**4.1 Refactor BehaviorSystem.cs**

```csharp
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
            // Register actions/guards
            // (Done via source generation at compile time)
            
            // Create machines
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
            
            fixed (HsmInstance64* inst = &agent.Instance)
            {
                HsmInstanceManager.Initialize(inst, blob);
            }
            
            // Trigger initial entry
            fixed (HsmInstance64* inst = &agent.Instance)
            {
                HsmKernel.Trigger(ref agent.Instance);
            }
        }
        
        public void Update(List<Agent> agents, float time, float dt)
        {
            _currentAgents = agents;
            
            // Update combat agent scanning (detect enemies)
            UpdateCombatScanning(agents);
            
            // Tick all agents
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
                fixed (HsmInstance64* inst = &agent.Instance)
                fixed (AgentContext* ctx = &agent.Context)
                {
                    HsmKernel.Update(blob, ref agent.Instance, agent.Context, dt);
                }
                
                // Update visualization (read active states)
                UpdateActiveStates(agent, blob);
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
            }
        }
        
        private void UpdateActiveStates(Agent agent, HsmDefinitionBlob blob)
        {
            // Read active leaf states from instance
            fixed (HsmInstance64* inst = &agent.Instance)
            {
                byte* ptr = (byte*)inst;
                int regionCount = blob.Header.OrthogonalRegionCount;
                ushort* activeLeafs = (ushort*)(ptr + 32); // Offset to active states
                
                agent.ActiveStates = new ushort[regionCount];
                for (int i = 0; i < regionCount; i++)
                {
                    agent.ActiveStates[i] = activeLeafs[i];
                }
            }
        }
    }
}
```

---

### Phase 5: State Machine Visualizer

**5.1 Create StateMachineVisualizer.cs**

```csharp
using ImGuiNET;
using System.Numerics;
using Fhsm.Kernel.Data;

namespace Fhsm.Demo.Visual.UI
{
    public class StateMachineVisualizer
    {
        public void Render(Agent agent, HsmDefinitionBlob blob, float time)
        {
            ImGui.Begin($"State Machine: Agent {agent.Id}");
            
            ImGui.Text($"Role: {agent.Role}");
            ImGui.Text($"Machine: {agent.MachineName}");
            ImGui.Text($"Position: ({agent.Position.X:F0}, {agent.Position.Y:F0})");
            
            ImGui.Separator();
            
            // Active states
            ImGui.TextColored(new Vector4(0, 1, 0, 1), "Active States:");
            if (agent.ActiveStates != null)
            {
                foreach (var stateId in agent.ActiveStates)
                {
                    if (stateId == 0xFFFF) continue;
                    
                    var state = blob.States[stateId];
                    ImGui.BulletText($"State {stateId}"); // TODO: Name lookup
                }
            }
            
            ImGui.Separator();
            
            // State hierarchy (tree view)
            RenderStateHierarchy(blob, agent.ActiveStates);
            
            ImGui.Separator();
            
            // Context data
            ImGui.CollapsingHeader("Context");
            ImGui.Indent();
            ImGui.Text($"ResourceCount: {agent.Context.ResourceCount}");
            ImGui.Text($"HasTarget: {agent.Context.HasTarget}");
            ImGui.Text($"TargetPosition: ({agent.Context.TargetPosition.X:F0}, {agent.Context.TargetPosition.Y:F0})");
            ImGui.Unindent();
            
            ImGui.Separator();
            
            // Recent transitions
            RenderTransitionHistory(agent);
            
            ImGui.Separator();
            
            // Manual event injection
            RenderEventControls(agent);
            
            ImGui.End();
        }
        
        private void RenderStateHierarchy(HsmDefinitionBlob blob, ushort[] activeStates)
        {
            ImGui.CollapsingHeader("State Hierarchy");
            ImGui.Indent();
            
            // Render tree starting from root
            for (ushort i = 0; i < blob.Header.StateCount; i++)
            {
                var state = blob.States[i];
                if (state.ParentIndex == 0xFFFF) // Root
                {
                    RenderStateNode(blob, i, activeStates, 0);
                }
            }
            
            ImGui.Unindent();
        }
        
        private void RenderStateNode(HsmDefinitionBlob blob, ushort stateId, ushort[] activeStates, int depth)
        {
            var state = blob.States[stateId];
            bool isActive = activeStates != null && Array.Exists(activeStates, s => s == stateId);
            
            var color = isActive ? new Vector4(0, 1, 0, 1) : new Vector4(1, 1, 1, 1);
            var prefix = new string(' ', depth * 2);
            
            ImGui.TextColored(color, $"{prefix}└ State {stateId}");
            
            // Render children
            ushort childId = state.FirstChildIndex;
            while (childId != 0xFFFF)
            {
                RenderStateNode(blob, childId, activeStates, depth + 1);
                childId = blob.States[childId].NextSiblingIndex;
            }
        }
        
        private void RenderTransitionHistory(Agent agent)
        {
            if (ImGui.CollapsingHeader("Recent Transitions"))
            {
                ImGui.Indent();
                
                foreach (var trans in agent.RecentTransitions.TakeLast(10).Reverse())
                {
                    ImGui.Text($"{trans.FromState} → {trans.ToState} (Event: {trans.EventId})");
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
                {
                    InjectEvent(agent, MachineDefinitions.TimerExpired);
                }
                
                if (ImGui.Button("Trigger EnemyDetected"))
                {
                    InjectEvent(agent, MachineDefinitions.EnemyDetected);
                }
                
                if (ImGui.Button("Trigger EnemyLost"))
                {
                    InjectEvent(agent, MachineDefinitions.EnemyLost);
                }
                
                ImGui.Unindent();
            }
        }
        
        private unsafe void InjectEvent(Agent agent, ushort eventId)
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
    
    public struct TransitionRecord
    {
        public ushort FromState;
        public ushort ToState;
        public ushort EventId;
        public float Timestamp;
    }
}
```

---

## 📊 Implementation Checklist

### Phase 1: Core Structures ✅
- [ ] Update `Agent.cs` (HsmInstance64, AgentContext)
- [ ] Update `AgentRole` enum
- [ ] Add `TransitionRecord` struct

### Phase 2: State Machines ✅
- [ ] Create `MachineDefinitions.cs`
- [ ] Implement `CreatePatrolMachine()`
- [ ] Implement `CreateGatherMachine()`
- [ ] Implement `CreateCombatMachine()`
- [ ] Test compilation

### Phase 3: Actions/Guards ✅
- [ ] Create `Actions.cs`
- [ ] Implement patrol actions
- [ ] Implement gather actions
- [ ] Implement combat actions
- [ ] Implement guards
- [ ] Verify source generation

### Phase 4: System Updates ✅
- [ ] Refactor `BehaviorSystem.cs`
- [ ] Remove BTree dependencies
- [ ] Add HSM initialization
- [ ] Add HSM update loop
- [ ] Add combat scanning logic

### Phase 5: Visualization ✅
- [ ] Create `StateMachineVisualizer.cs`
- [ ] Render state hierarchy
- [ ] Show active states
- [ ] Display transition history
- [ ] Add manual event controls
- [ ] Update `RenderSystem.cs` (if needed)

### Phase 6: UI Updates ✅
- [ ] Update `DemoApp.cs`
- [ ] Replace tree references with machines
- [ ] Update spawn methods
- [ ] Update UI text
- [ ] Test all controls

### Phase 7: Polish ✅
- [ ] Add state name lookup (debug data)
- [ ] Add transition animation
- [ ] Add event visualization
- [ ] Performance metrics
- [ ] Update README.md

---

## 🎨 Visual Enhancements

### State Visualization
- **Active states:** Green highlight
- **Inactive states:** Gray
- **Transitioning:** Yellow animation
- **History states:** Blue marker

### Transition Animation
- Show arrows between states during transitions
- Fade effect for recent transitions
- Event labels on arrows

### Trace Integration
- Real-time trace buffer display
- Filterable by tier (Tier 1/2/3)
- Exportable to file

---

## 🚀 Running the Demo

```bash
cd demos/Fhsm.Demo.Visual
dotnet run
```

**Controls:**
- Left-click: Select agent
- Mouse wheel: Zoom
- Middle-click drag: Pan camera
- UI buttons: Spawn agents, pause, adjust time

---

## 📈 Success Criteria

- [ ] All three agent types working (Patrol, Gather, Combat)
- [ ] State hierarchy visible and accurate
- [ ] Transitions trigger correctly
- [ ] Events processed in order
- [ ] Guards evaluated correctly
- [ ] Activities run continuously
- [ ] History states restore properly
- [ ] Visual feedback smooth (60 FPS with 20+ agents)
- [ ] No crashes or errors
- [ ] Code clean and documented

---

**This design provides a complete visual showcase of FastHSM capabilities!** 🎮
