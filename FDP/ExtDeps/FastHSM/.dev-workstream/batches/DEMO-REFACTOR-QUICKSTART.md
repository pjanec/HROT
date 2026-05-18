# Visual Demo Refactor - Quick Start

**Goal:** Convert the BTree visual demo to use HSM in 7 phases

---

## 🎯 Quick Overview

**What Changes:**
- `BehaviorTreeState` → `HsmInstance64`
- `AgentBlackboard` → `AgentContext`  
- `Interpreter.Tick()` → `HsmKernel.Update()`
- JSON trees → C# `HsmBuilder` definitions
- Tree visualization → State hierarchy visualization

---

## 📝 Phase-by-Phase Implementation

### Phase 1: Update Agent (30 min)

**File:** `Entities/Agent.cs`

**Replace:**
```csharp
public BehaviorTreeState State;
public AgentBlackboard Blackboard;
```

**With:**
```csharp
public unsafe HsmInstance64 Instance;
public AgentContext Context;
```

**Add:**
```csharp
public ushort[] ActiveStates;
public List<TransitionRecord> RecentTransitions = new();
```

**Update Context struct:**
```csharp
public struct AgentContext
{
    public int PatrolPointIndex;
    public int ResourceCount;
    public bool HasTarget;
    public int TargetAgentId;
    public Vector2 TargetPosition;
    public float DistanceToTarget;
    public float DistanceToBase;
    public float Time;
    public float DeltaTime;
    public Agent Agent;
}
```

---

### Phase 2: Create Machine Definitions (60 min)

**Create:** `MachineDefinitions.cs`

**Add event IDs:**
```csharp
public const ushort PointSelected = 1;
public const ushort Arrived = 2;
public const ushort TimerExpired = 3;
// ... etc
```

**Create patrol machine:**
```csharp
public static HsmDefinitionBlob CreatePatrolMachine()
{
    var builder = new HsmBuilder("Patrol");
    
    var selecting = builder.State("SelectingPoint")
        .OnEntry("FindPatrolPoint");
    var moving = builder.State("Moving")
        .Activity("MoveToTarget");
    var waiting = builder.State("Waiting");
    
    selecting.On(PointSelected).GoTo(moving);
    moving.On(Arrived).GoTo(waiting);
    waiting.On(TimerExpired).GoTo(selecting);
    
    selecting.Initial();
    
    return CompileAndEmit(builder);
}
```

**Repeat for Gather and Combat machines.**

---

### Phase 3: Implement Actions (60 min)

**Create:** `Actions.cs`

**Template:**
```csharp
[HsmAction(Name = "ActionName")]
public static unsafe void ActionName(void* instance, void* context, ushort eventId)
{
    var ctx = (AgentContext*)context;
    
    // Your logic here
    
    // Fire event to advance
    var evt = new HsmEvent { EventId = MachineDefinitions.SomeEvent };
    var inst = (HsmInstance64*)instance;
    HsmEventQueue.TryEnqueue(inst, 64, evt);
}
```

**Key actions to implement:**
- FindPatrolPoint
- MoveToTarget
- FindResource
- Gather
- Attack
- ScanForEnemy

**Guards:**
```csharp
[HsmGuard(Name = "HasTarget")]
public static unsafe bool HasTarget(void* instance, void* context, ushort eventId)
{
    var ctx = (AgentContext*)context;
    return ctx->HasTarget;
}
```

---

### Phase 4: Update BehaviorSystem (45 min)

**File:** `Systems/BehaviorSystem.cs`

**Constructor:**
```csharp
public BehaviorSystem()
{
    _machines = new Dictionary<string, HsmDefinitionBlob>
    {
        ["patrol"] = MachineDefinitions.CreatePatrolMachine(),
        ["gather"] = MachineDefinitions.CreateGatherMachine(),
        ["combat"] = MachineDefinitions.CreateCombatMachine()
    };
}
```

**Initialize agent:**
```csharp
public unsafe void InitializeAgent(Agent agent)
{
    var blob = _machines[agent.MachineName];
    fixed (HsmInstance64* inst = &agent.Instance)
    {
        HsmInstanceManager.Initialize(inst, blob);
        HsmKernel.Trigger(ref agent.Instance);
    }
}
```

**Update loop:**
```csharp
public unsafe void Update(List<Agent> agents, float time, float dt)
{
    foreach (var agent in agents)
    {
        var blob = _machines[agent.MachineName];
        agent.Context.Time = time;
        agent.Context.DeltaTime = dt;
        agent.Context.Agent = agent;
        
        HsmKernel.Update(blob, ref agent.Instance, agent.Context, dt);
        
        UpdateActiveStates(agent, blob);
    }
}
```

---

### Phase 5: Create Visualizer (60 min)

**Create:** `UI/StateMachineVisualizer.cs`

**Main render method:**
```csharp
public void Render(Agent agent, HsmDefinitionBlob blob, float time)
{
    ImGui.Begin($"State Machine: Agent {agent.Id}");
    
    // Active states (green)
    RenderActiveStates(agent);
    
    // State hierarchy (tree view)
    RenderStateHierarchy(blob, agent.ActiveStates);
    
    // Transition history
    RenderTransitionHistory(agent);
    
    // Manual event controls
    RenderEventControls(agent);
    
    ImGui.End();
}
```

**Recursive tree rendering:**
```csharp
private void RenderStateNode(HsmDefinitionBlob blob, ushort stateId, ushort[] active, int depth)
{
    var state = blob.States[stateId];
    bool isActive = Array.Exists(active, s => s == stateId);
    
    var color = isActive ? GREEN : WHITE;
    ImGui.TextColored(color, $"State {stateId}");
    
    // Render children recursively
    ushort childId = state.FirstChildIndex;
    while (childId != 0xFFFF)
    {
        RenderStateNode(blob, childId, active, depth + 1);
        childId = blob.States[childId].NextSiblingIndex;
    }
}
```

---

### Phase 6: Update DemoApp (30 min)

**File:** `DemoApp.cs`

**Update initialization:**
```csharp
private void Initialize()
{
    _behaviorSystem = new BehaviorSystem();
    _renderSystem = new RenderSystem();
    
    SpawnPatrolAgents(5);
    SpawnGatherAgents(3);
}
```

**Update spawn methods:**
```csharp
private void SpawnPatrolAgents(int count)
{
    for (int i = 0; i < count; i++)
    {
        var agent = new Agent(
            _nextId++,
            GetRandomPosition(),
            "patrol",  // machine name
            AgentRole.Patrol);
        
        _behaviorSystem.InitializeAgent(agent);
        _agents.Add(agent);
    }
}
```

**Update UI:**
```csharp
private void RenderUI()
{
    ImGui.Begin("FastHSM Demo");  // Changed from FastBTree
    
    // ... existing controls ...
    
    if (_selectedAgent != null && _machines.TryGetValue(_selectedAgent.MachineName, out var blob))
    {
        _stateMachineVisualizer.Render(_selectedAgent, blob, _time);
    }
    
    ImGui.End();
}
```

---

### Phase 7: Final Polish (30 min)

**Update README.md:**
```markdown
# Fhsm.Demo.Visual

Visual demonstration of FastHSM library.

## Features
- Real-time state machine visualization
- Interactive event injection
- Transition history
- Performance metrics

## State Machines
- **Patrol:** Simple patrol loop
- **Gather:** Resource gathering cycle
- **Combat:** Enemy detection and engagement
```

**Test checklist:**
- [ ] Patrol agents move between points
- [ ] Gather agents collect resources
- [ ] Combat agents chase and attack
- [ ] State hierarchy displays correctly
- [ ] Transitions trigger on events
- [ ] Manual event injection works
- [ ] No crashes with 20+ agents
- [ ] 60 FPS maintained

---

## 🔧 Common Issues & Fixes

### Issue: "HsmAction not found"
**Fix:** Ensure Actions.cs is compiled with source generator enabled. Check project references.

### Issue: "Active states not updating"
**Fix:** Call `UpdateActiveStates()` after each `HsmKernel.Update()`.

### Issue: "Events not firing"
**Fix:** Check event IDs match between definitions and actions. Use `HsmEventQueue.TryEnqueue()`.

### Issue: "Transitions don't work"
**Fix:** Verify guards return correct values. Check transition ordering (priority).

---

## 🎨 Visual Tips

**State colors:**
- Active: Bright green
- Inactive: Gray
- Transitioning: Yellow flash

**Agent colors (unchanged):**
- Patrol: Blue
- Gather: Green
- Combat: Red

**UI Layout:**
- Left panel: Controls & agent list
- Right panel: Selected agent state machine
- Bottom panel: Performance metrics

---

## 🚀 Quick Build & Run

```bash
cd demos/Fhsm.Demo.Visual
dotnet build
dotnet run
```

**Expected result:** Window opens, agents move around, click agent to inspect state machine.

---

**Total Implementation Time: ~5-6 hours**

Phases 1-4 are core (must work for demo to run).  
Phases 5-7 are polish (make demo look good).

**Recommended order:**
1. Phase 1 (Agent structure)
2. Phase 2 (Machine definitions)
3. Phase 4 (BehaviorSystem) - test basic movement
4. Phase 3 (Actions) - implement gradually
5. Phase 5-7 (UI polish)
