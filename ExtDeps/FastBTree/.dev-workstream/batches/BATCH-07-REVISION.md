# BATCH-07-REVISION: Critical Fixes for Demo

**Project:** FastBTree - Visual Demo Application  
**Revision Number:** BATCH-07-REVISION-01  
**Issued:** 2026-01-05  
**Estimated Effort:** 2-3 hours  
**Priority:** HIGH

---

## 📋 Overview

Your demo is **80% complete** and working well! However, there are **6 critical issues** preventing it from showcasing FastBTree effectively.

**What works:**
- ✅ Raylib + ImGui infrastructure
- ✅ Patrol agents (blue)
- ✅ Gather agents (green)
- ✅ 60 FPS performance

**What needs fixing:**
- ❌ Combat behavior tree logic
- ❌ Agent inspector (can't click agents)
- ❌ Visual cues missing

---

## 🔥 Critical Fixes (MUST DO)

### Fix 1: Combat Behavior Tree Logic

**Problem:** Combat tree uses `ChaseEnemy` as a Condition when it's an Action.

**File:** `demos/Fbt.Demo.Visual/Trees/combat.json`

**Replace entire file with:**

```json
{
  "TreeName": "CombatAgent",
  "Root": {
    "Type": "Repeater",
    "RepeatCount": -1,
    "Children": [
      {
        "Type": "Selector",
        "Children": [
          {
            "Type": "Sequence",
            "Children": [
              {
                "Type": "Condition",
                "Action": "HasEnemy"
              },
              {
                "Type": "Action",
                "Action": "ChaseEnemy"
              },
              {
                "Type": "Action",
                "Action": "Attack"
              }
            ]
          },
          {
            "Type": "Sequence",
            "Children": [
              {
                "Type": "Action",
                "Action": "FindRandomPoint"
              },
              {
                "Type": "Action",
                "Action": "MoveToTarget"
              },
              {
                "Type": "Wait",
                "Duration": 1.0
              },
              {
                "Type": "Action",
                "Action": "ScanForEnemy"
              }
            ]
          }
        ]
      }
    ]
  }
}
```

**Why this works:**
- Selector tries combat branch first
- If no enemy (HasEnemy fails) → falls through to wander branch
- Wander: pick random point → move → wait → scan for enemies
- If ScanForEnemy succeeds (sets HasTarget = true) → next tick combat branch runs
- Combat: check if still has enemy → chase → attack
- After attack, HasTarget = false → back to wandering

---

### Fix 2: Add Missing Actions

**File:** `demos/Fbt.Demo.Visual/Systems/BehaviorSystem.cs`

**Step 2a: Register new actions**

Find the `RegisterActions()` method around line 33, add:

```csharp
private void RegisterActions()
{
    Console.WriteLine("Registering Actions...");
    
    // Patrol actions
    _registry.Register("FindPatrolPoint", FindPatrolPoint);
    _registry.Register("MoveToTarget", MoveToTarget);
    
    // Gather actions
    _registry.Register("FindResource", FindResource);
    _registry.Register("Gather", Gather);
    _registry.Register("ReturnToBase", ReturnToBase);
    
    // Combat actions
    _registry.Register("ScanForEnemy", ScanForEnemy);
    _registry.Register("HasEnemy", HasEnemy);          // NEW!
    _registry.Register("FindRandomPoint", FindRandomPoint); // NEW!
    _registry.Register("ChaseEnemy", ChaseEnemy);
    _registry.Register("Attack", Attack);
}
```

**Step 2b: Add new action implementations**

Add these methods at the end of the BehaviorSystem class (before the closing brace):

```csharp
// NEW - Check if we currently have a target
private NodeStatus HasEnemy(ref AgentBlackboard bb, ref BehaviorTreeState state, ref DemoContext ctx, int payload)
{
    return bb.HasTarget ? NodeStatus.Success : NodeStatus.Failure;
}

// NEW - Pick a random wander point
private NodeStatus FindRandomPoint(ref AgentBlackboard bb, ref BehaviorTreeState state, ref DemoContext ctx, int payload)
{
    ctx.Agent.TargetPosition = new Vector2(
        _random.Next(100, 1180), 
        _random.Next(100, 620));
    return NodeStatus.Success;
}
```

**Step 2c: Fix ScanForEnemy (optional but better)**

Replace the current `ScanForEnemy` (around line 157) with:

```csharp
private NodeStatus ScanForEnemy(ref AgentBlackboard bb, ref BehaviorTreeState state, ref DemoContext ctx, int payload)
{
    // Simple simulation - small chance to "detect" an enemy
    if (_random.NextDouble() < 0.1) // 10% chance each scan
    {
        bb.HasTarget = true;
        // Set a random enemy position
        ctx.Agent.TargetPosition = new Vector2(
            _random.Next(100, 1180), 
            _random.Next(100, 620));
        return NodeStatus.Success;
    }
    
    return NodeStatus.Failure;
}
```

---

### Fix 3: Implement Agent Selection

**File:** `demos/Fbt.Demo.Visual/DemoApp.cs`

**Step 3a: Add field for selected agent**

At the top of the `DemoApp` class (around line 14), add:

```csharp
private Agent? _selectedAgent = null;
```

**Step 3b: Add click detection in Update()**

Find the `Update()` method, add this at the beginning:

```csharp
private void Update(float dt)
{
    _time += dt;
    
    // NEW: Handle agent selection with mouse click
    if (Raylib.IsMouseButtonPressed(MouseButton.MOUSE_BUTTON_LEFT))
    {
        var mousePos = Raylib.GetMousePosition();
        
        // Find closest agent within 20 pixels
        _selectedAgent = _agents
            .Where(a => Vector2.Distance(a.Position, mousePos) < 20f)
            .OrderBy(a => Vector2.Distance(a.Position, mousePos))
            .FirstOrDefault();
    }
    
    // Update all agents
    _behaviorSystem.Update(_agents, _time, dt);
    
    // ... rest of Update
}
```

**Step 3b: Update RenderUI() to show inspector**

Find the `RenderUI()` method, replace the part with `_selectedAgent` comment:

```csharp
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
    
    if (ImGui.Button("Spawn Combat Agent"))
        SpawnCombatAgents(1);
    
    if (ImGui.Button("Clear All"))
    {
        _agents.Clear();
        _selectedAgent = null;
    }
    
    ImGui.Separator();
    ImGui.Text("Click any agent to inspect!");
    
    ImGui.End();
    
    // NEW: Selected agent inspector
    if (_selectedAgent != null)
    {
        RenderAgentInspector(_selectedAgent);
    }
}
```

---

### Fix 4: Implement Agent Inspector Panel

**File:** `demos/Fbt.Demo.Visual/DemoApp.cs`

**Add this new method at the end of the DemoApp class:**

```csharp
private void RenderAgentInspector(Agent agent)
{
    ImGui.Begin($"Agent Inspector - #{agent.Id}");
    
    // Basic info
    ImGui.Text($"Role: {agent.Role}");
    ImGui.Text($"Position: ({agent.Position.X:F0}, {agent.Position.Y:F0})");
    ImGui.Text($"Target: ({agent.TargetPosition.X:F0}, {agent.TargetPosition.Y:F0})");
    ImGui.Text($"Speed: {agent.Speed:F1}");
    
    ImGui.Separator();
    
    // Blackboard state
    ImGui.Text("Blackboard:");
    ImGui.Indent();
    ImGui.Text($"PatrolPointIndex: {agent.Blackboard.PatrolPointIndex}");
    ImGui.Text($"ResourceCount: {agent.Blackboard.ResourceCount}");
    ImGui.Text($"HasTarget: {agent.Blackboard.HasTarget}");
    ImGui.Text($"LastPatrolTime: {agent.Blackboard.LastPatrolTime:F2}");
    ImGui.Unindent();
    
    ImGui.Separator();
    
    // Behavior tree state
    ImGui.Text("Behavior Tree:");
    ImGui.Text($"Running Node: {agent.State.RunningNodeIndex}");
    
    ImGui.Separator();
    
    // Tree hierarchy with highlighting
    if (_trees.TryGetValue(agent.TreeName, out var blob))
    {
        ImGui.Text($"Tree: {blob.TreeName}");
        ImGui.Separator();
        RenderTreeHierarchy(blob, agent.State.RunningNodeIndex);
    }
    
    ImGui.End();
}

private void RenderTreeHierarchy(BehaviorTreeBlob blob, int runningIndex)
{
    RenderTreeNode(blob, 0, runningIndex, 0);
}

private void RenderTreeNode(BehaviorTreeBlob blob, int index, int runningIndex, int depth)
{
    if (index >= blob.Nodes.Length) return;
    
    var node = blob.Nodes[index];
    string indent = new string(' ', depth * 2);
    
    // Highlight running node in YELLOW
    Vector4 color = index == runningIndex
        ? new Vector4(1f, 1f, 0f, 1f) // Yellow for running
        : new Vector4(0.8f, 0.8f, 0.8f, 1f); // Gray for not running
    
    string nodeDesc = $"{indent}[{index}] {node.Type}";
    
    // Add method name for actions/conditions
    if (node.Type == NodeType.Action || node.Type == NodeType.Condition)
    {
        if (node.PayloadIndex >= 0 && node.PayloadIndex < (blob.MethodNames?.Length ?? 0))
        {
            nodeDesc += $" \"{blob.MethodNames[node.PayloadIndex]}\"";
        }
    }
    // Add duration for Wait nodes
    else if (node.Type == NodeType.Wait)
    {
        if (node.PayloadIndex >= 0 && node.PayloadIndex < (blob.FloatParams?.Length ?? 0))
        {
            float duration = blob.FloatParams[node.PayloadIndex];
            nodeDesc += $" ({duration:F1}s)";
        }
    }
    
    ImGui.TextColored(color, nodeDesc);
    
    // Recursively render children
    int childIndex = index + 1;
    for (int i = 0; i < node.ChildCount; i++)
    {
        RenderTreeNode(blob, childIndex, runningIndex, depth + 1);
        if (childIndex < blob.Nodes.Length)
        {
            childIndex += blob.Nodes[childIndex].SubtreeOffset;
        }
    }
}
```

---

### Fix 5: Add Visual Feedback

**File:** `demos/Fbt.Demo.Visual/Systems/RenderSystem.cs`

**Update the signature and implementation:**

```csharp
public void RenderAgents(List<Agent> agents, Agent? selectedAgent)
{
    foreach (var agent in agents)
    {
        // Draw target line (shows where agent is going)
        Color lineColor = new Color(
            agent.Color.r, 
            agent.Color.g, 
            agent.Color.b, 
            80); // Semi-transparent
        
        Raylib.DrawLineEx(
            agent.Position,
            agent.TargetPosition,
            2f,
            lineColor);
        
        // Draw agent circle
        bool isSelected = agent == selectedAgent;
        float radius = isSelected ? 12f : 8f;
        Raylib.DrawCircleV(agent.Position, radius, agent.Color);
        
        // Draw selection ring
        if (isSelected)
        {
            Raylib.DrawCircleLines(
                (int)agent.Position.X,
                (int)agent.Position.Y,
                16,
                Color.WHITE);
        }
        
        // Draw directional indicator (arrow)
        var dirEnd = agent.Position + new Vector2(
            MathF.Cos(agent.Rotation) * 12f,
            MathF.Sin(agent.Rotation) * 12f);
        Raylib.DrawLineEx(agent.Position, dirEnd, 2f, Color.WHITE);
    }
}
```

**Update the call in DemoApp.cs Render():**

Find the `Render()` method, update:

```csharp
private void Render()
{
    Raylib.BeginDrawing();
    Raylib.ClearBackground(Color.DARKGRAY);
    
    // Render world - pass selected agent!
    _renderSystem.RenderAgents(_agents, _selectedAgent);
    
    // ImGui UI
    rlImGui.Begin();
    RenderUI();
    rlImGui.End();
    
    Raylib.EndDrawing();
}
```

---

### Fix 6: Add SpawnCombatAgents Method

**File:** `demos/Fbt.Demo.Visual/DemoApp.cs`

**Add this method (similar to SpawnPatrolAgents):**

```csharp
private void SpawnCombatAgents(int count)
{
    for (int i = 0; i < count; i++)
    {
        var agent = new Agent(
            _agents.Count,
            new Vector2(_random.Next(100, 1180), _random.Next(100, 620)),
            "combat",
            AgentRole.Combat);
        
        _agents.Add(agent);
    }
}
```

**And add Random field at the top:**

```csharp
private Random _random = new Random();
```

---

## 🧪 Testing Checklist

After making these fixes, verify:

### Patrol Agents (Blue)
- [ ] Move between random points
- [ ] Wait at each point
- [ ] Continuous loop

### Gather Agents (Green)
- [ ] Find resources
- [ ] Return to base (upper left)
- [ ] Increment resource count

### Combat Agents (Red)
- [ ] Wander randomly when no enemy
- [ ] Stop and wait periodically
- [ ] When ScanForEnemy succeeds → HasTarget true
- [ ] Next tick: chase to that position
- [ ] When close: attack (clears HasTarget)
- [ ] Return to wandering

### Agent Inspector
- [ ] Click any agent → inspector opens
- [ ] Shows position, target, role
- [ ] Shows blackboard values
- [ ] Shows tree hierarchy
- [ ] **Running node highlighted in YELLOW**
- [ ] Can see which action is executing

### Visual Feedback
- [ ] Target lines visible (semi-transparent)
- [ ] Selected agent has white ring
- [ ] Directional arrows show facing
- [ ] Selected agent slightly larger

---

## 🎯 Expected Result

**After fixes, the demo should:**

1. **Window opens** - Shows 2D world with UI panels
2. **Agents spawn** - Blue/green/red circles moving
3. **Click any agent** - Inspector opens on right
4. **See tree execution** - Yellow highlight shows current node
5. **Visual feedback** - Lines, arrows, selection rings
6. **Combat agents** - Wander → scan → chase → attack → wander

**Key Value:** **SEE THE BEHAVIOR TREE WORKING IN REAL-TIME!**

---

## 📝 Reporting

When complete, update your report to include:
- Screenshot of agent inspector showing tree with yellow highlight
- GIF of clicking agents and seeing tree execution
- Confirmation that all 6 fixes are done

---

## ⏱️ Time Estimate

- Fix 1 (Combat tree): **15 min**
- Fix 2 (New actions): **15 min**
- Fix 3 (Selection): **20 min**
- Fix 4 (Inspector): **45 min**
- Fix 5 (Visual feedback): **20 min**
- Fix 6 (Spawn method): **5 min**

**Total: ~2 hours**

---

**This will make the demo SHINE! These fixes transform it from "working" to "impressive showcase"!** 🌟

*Revision Issued: 2026-01-05*  
*Priority: HIGH*  
*Estimated Completion: 2-3 hours*
