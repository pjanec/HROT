# BATCH-07-REVISION-02: Enhanced Combat System & Visual Feedback

**Issued:** 2026-01-05  
**Priority:** HIGH  
**Estimated Time:** 1 hour

---

## 🎯 Problems to Fix

1. **Combat agents don't chase real enemies** - They chase random positions
2. **No visual feedback for targeting** - Can't see who is chasing whom
3. **No attack visualization** - Can't tell when attack happens
4. **Attack mode rarely entered** - 10% chance is too low

---

## 🔧 Solutions

### Fix 1: Store Target Agent ID in Blackboard

**File:** `demos/Fbt.Demo.Visual/Entities/Agent.cs`

**Find the `AgentBlackboard` struct (around line 40), replace with:**

```csharp
public struct AgentBlackboard
{
    public int PatrolPointIndex;
    public float LastPatrolTime;
    public int ResourceCount;
    public bool HasTarget;
    public int TargetAgentId;  // NEW - which agent we're chasing
}
```

---

### Fix 2: Make ScanForEnemy Actually Find Real Agents

**File:** `demos/Fbt.Demo.Visual/Systems/BehaviorSystem.cs`

**Find `ScanForEnemy` method, replace with:**

```csharp
private NodeStatus ScanForEnemy(ref AgentBlackboard bb, ref BehaviorTreeState state, ref DemoContext ctx, int payload)
{
    const float ScanRadius = 200f; // Detection range
    
    // Find nearest agent of ANY type within scan radius
    Agent? nearestEnemy = null;
    float nearestDist = float.MaxValue;
    
    foreach (var agent in ctx.AllAgents)
    {
        // Don't target ourselves
        if (agent.Id == ctx.Agent.Id)
            continue;
        
        float dist = Vector2.Distance(ctx.Agent.Position, agent.Position);
        
        if (dist < ScanRadius && dist < nearestDist)
        {
            nearestDist = dist;
            nearestEnemy = agent;
        }
    }
    
    if (nearestEnemy != null)
    {
        bb.HasTarget = true;
        bb.TargetAgentId = nearestEnemy.Id;
        ctx.Agent.TargetPosition = nearestEnemy.Position;
        return NodeStatus.Success;
    }
    
    bb.HasTarget = false;
    bb.TargetAgentId = -1;
    return NodeStatus.Failure;
}
```

---

### Fix 3: Update ChaseEnemy to Track Moving Target

**File:** `demos/Fbt.Demo.Visual/Systems/BehaviorSystem.cs`

**Find `ChaseEnemy`, replace with:**

```csharp
private NodeStatus ChaseEnemy(ref AgentBlackboard bb, ref BehaviorTreeState state, ref DemoContext ctx, int payload)
{
    if (!bb.HasTarget || bb.TargetAgentId < 0)
        return NodeStatus.Failure;
    
    // Find the actual target agent
    var targetAgent = ctx.AllAgents.FirstOrDefault(a => a.Id == bb.TargetAgentId);
    
    if (targetAgent == null)
    {
        // Target lost (maybe died/removed)
        bb.HasTarget = false;
        bb.TargetAgentId = -1;
        return NodeStatus.Failure;
    }
    
    // Update target position to track moving enemy
    ctx.Agent.TargetPosition = targetAgent.Position;
    
    // Check if we're close enough to attack
    float distance = Vector2.Distance(ctx.Agent.Position, targetAgent.Position);
    
    if (distance < 20f) // Attack range
        return NodeStatus.Success;
    
    return NodeStatus.Running;
}
```

---

### Fix 4: Add Attack Visual Feedback

**File:** `demos/Fbt.Demo.Visual/Entities/Agent.cs`

**Add new fields to Agent class:**

```csharp
public class Agent
{
    // ... existing fields ...
    
    // Visual feedback
    public TreeExecutionHighlight? CurrentNode { get; set; }
    public float AttackFlashTimer { get; set; }  // NEW
    public int LastAttackTargetId { get; set; }  // NEW
    
    // ... rest of class ...
}
```

**Update Attack action to set visual feedback:**

**File:** `demos/Fbt.Demo.Visual/Systems/BehaviorSystem.cs`

```csharp
private NodeStatus Attack(ref AgentBlackboard bb, ref BehaviorTreeState state, ref DemoContext ctx, int payload)
{
    // Visual feedback - flash effect
    ctx.Agent.AttackFlashTimer = 0.3f; // Flash for 300ms
    ctx.Agent.LastAttackTargetId = bb.TargetAgentId;
    
    // Simulate attack action - lose target after attack
    bb.HasTarget = false;
    bb.TargetAgentId = -1;
    
    return NodeStatus.Success;
}
```

---

### Fix 5: Add DemoContext AllAgents Field

**File:** `demos/Fbt.Demo.Visual/Systems/BehaviorSystem.cs`

**Find `DemoContext` struct, update:**

```csharp
public struct DemoContext : IAIContext
{
    public float Time { get; set; }
    public float DeltaTime { get; set; }
    public Agent Agent { get; set; }
    public int FrameCount { get; set; }
    public List<Agent> AllAgents { get; set; }  // NEW
    
    // ... IAIContext implementation ...
}
```

**Update BehaviorSystem.Update() to pass all agents:**

```csharp
public void Update(List<Agent> agents, float time, float dt)
{
    var context = new DemoContext 
    { 
        Time = time, 
        DeltaTime = dt,
        AllAgents = agents  // NEW
    };
    
    foreach (var agent in agents)
    {
        // Update attack flash timer
        if (agent.AttackFlashTimer > 0)
        {
            agent.AttackFlashTimer -= dt;
        }
        
        if (!_interpreters.TryGetValue(agent.TreeName, out var interpreter))
            continue;
            
        context.Agent = agent;
        
        // Execute behavior tree
        var status = interpreter.Tick(
            ref agent.Blackboard,
            ref agent.State,
            ref context);
        
        // ... rest of update ...
    }
}
```

---

### Fix 6: Add Visual Cues to Rendering

**File:** `demos/Fbt.Demo.Visual/Systems/RenderSystem.cs`

**Replace the entire `RenderAgents` method:**

```csharp
public void RenderAgents(List<Agent> agents, Agent? selectedAgent)
{
    foreach (var agent in agents)
    {
        // Draw scan radius for combat agents
        if (agent.Role == AgentRole.Combat)
        {
            Color scanColor = new Color(255, 0, 0, 30); // Red, very transparent
            Raylib.DrawCircleLines(
                (int)agent.Position.X,
                (int)agent.Position.Y,
                200, // Scan radius
                scanColor);
        }
        
        // Draw target line (shows where agent is going)
        Color lineColor = new Color(
            agent.Color.r, 
            agent.Color.g, 
            agent.Color.b, 
            80);
        
        Raylib.DrawLineEx(
            agent.Position,
            agent.TargetPosition,
            2f,
            lineColor);
        
        // Draw targeting line for combat agents with active target
        if (agent.Role == AgentRole.Combat && agent.Blackboard.HasTarget)
        {
            // Find target agent
            var targetAgent = agents.FirstOrDefault(a => a.Id == agent.Blackboard.TargetAgentId);
            if (targetAgent != null)
            {
                // Draw RED dashed line to target
                Raylib.DrawLineEx(
                    agent.Position,
                    targetAgent.Position,
                    3f,
                    Color.RED);
                
                // Draw circle around target
                Raylib.DrawCircleLines(
                    (int)targetAgent.Position.X,
                    (int)targetAgent.Position.Y,
                    15,
                    Color.RED);
            }
        }
        
        // Agent circle - flash white when attacking
        bool isSelected = agent == selectedAgent;
        float radius = isSelected ? 12f : 8f;
        
        Color agentColor = agent.Color;
        if (agent.AttackFlashTimer > 0)
        {
            // Flash WHITE when attacking
            agentColor = Color.WHITE;
        }
        
        Raylib.DrawCircleV(agent.Position, radius, agentColor);
        
        // Draw selection ring
        if (isSelected)
        {
            Raylib.DrawCircleLines(
                (int)agent.Position.X,
                (int)agent.Position.Y,
                16,
                Color.WHITE);
        }
        
        // Draw directional indicator
        var dirEnd = agent.Position + new Vector2(
            MathF.Cos(agent.Rotation) * 12f,
            MathF.Sin(agent.Rotation) * 12f);
        Raylib.DrawLineEx(agent.Position, dirEnd, 2f, Color.WHITE);
        
        // Draw attack effect
        if (agent.AttackFlashTimer > 0)
        {
            // Draw expanding circle for attack
            float attackRadius = (0.3f - agent.AttackFlashTimer) * 60f;
            Color attackColor = new Color(255, 255, 0, (byte)(agent.AttackFlashTimer / 0.3f * 200));
            Raylib.DrawCircleLines(
                (int)agent.Position.X,
                (int)agent.Position.Y,
                (int)attackRadius,
                attackColor);
        }
    }
}
```

---

### Fix 7: Enhanced Inspector - Show Target Agent

**File:** `demos/Fbt.Demo.Visual/UI/TreeVisualizer.cs`

**Find the Blackboard State section, add after HasTarget line:**

```csharp
// Full Blackboard State
ImGui.TextColored(new Vector4(1.0f, 0.8f, 0.3f, 1.0f), "Blackboard State");
ImGui.Indent();
ImGui.Text($"PatrolPointIndex: {agent.Blackboard.PatrolPointIndex}");
ImGui.Text($"ResourceCount: {agent.Blackboard.ResourceCount}");
ImGui.Text($"HasTarget: {agent.Blackboard.HasTarget}");

// NEW: Show which agent is being targeted
if (agent.Blackboard.HasTarget && agent.Blackboard.TargetAgentId >= 0)
{
    ImGui.TextColored(
        new Vector4(1f, 0.2f, 0.2f, 1f), 
        $"  → Targeting Agent #{agent.Blackboard.TargetAgentId}");
}

ImGui.Text($"LastPatrolTime: {agent.Blackboard.LastPatrolTime:F2}s");

// ... rest of blackboard ...
```

---

## 🎨 Visual Feedback Summary

After these fixes, you'll see:

### Combat Agent States

**1. Wandering/Scanning:**
- Red agent moving randomly
- Faint red circle = scan radius (200px)
- No target line

**2. Enemy Detected:**
- **THICK RED LINE** to target agent
- **RED CIRCLE** around target agent
- Inspector shows: "Targeting Agent #X"

**3. Chasing:**
- Combat agent follows target
- Red line updates in real-time
- Yellow highlight on "ChaseEnemy" node

**4. Attacking:**
- **WHITE FLASH** on combat agent
- **YELLOW EXPANDING CIRCLE** from agent
- Brief pause
- Target lost, back to wandering

---

## 🧪 Testing

After fixes:
1. Spawn 2-3 patrol agents and 1 combat agent
2. Watch combat agent wander with red scan circle
3. When patrol agent enters circle → RED LINE appears
4. Click combat agent → inspector shows "Targeting Agent #X"
5. Combat agent chases → follows target perfectly
6. When close → WHITE FLASH + expanding circle
7. After attack → back to wandering

---

## ⏱️ Time: 1 hour

This makes combat **completely visible and understandable**!

---

**Visual Cues Legend:**

🔴 **Red scan circle** - Detection radius  
🔴 **Thick red line** - Actively chasing this agent  
🔴 **Red ring on target** - This agent is being hunted  
⚪ **White flash** - ATTACK happening!  
🟡 **Yellow expanding ring** - Attack effect  
🟡 **Yellow highlight in tree** - Currently executing node  

**Now you can SEE the entire combat flow!** ⚔️
