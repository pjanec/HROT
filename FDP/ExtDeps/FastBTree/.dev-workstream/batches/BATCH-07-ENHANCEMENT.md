# BATCH-07-ENHANCEMENT: Multi-Line Colored Agent Labels & Enhanced Inspector

**Status:** Implementation complete, needs enhancement  
**Priority:** HIGH  
**Estimated Time:** 1-2 hours

---

## 🎯 Current Status

The enhanced visualization system is **implemented and building successfully**:

✅ Dynamic agent status labels above avatars  
✅ Node detail inspector panel  
✅ Clickable tree nodes for deep inspection  
✅ Real-time tree visualization  

**BUT** needs further enhancement based on user feedback.

---

## 📝 Enhancement Request

### Enhancement 1: Multi-Line Colored Agent Labels

**Current:** Single-line white text labels  
**Requested:** Multi-line, color-coded labels for complex states

**Example Visual:**
```
Current:
┌──────────────────┐
│ Chasing Agent #7 │  ← Single line, white
└──────────────────┘

Requested:
┌──────────────────┐
│ COMBAT           │  ← Role (Blue)
│ Chasing #7       │  ← Action (Yellow/Red)
│ Dist: 52.3px     │  ← Detail (Gray)
└──────────────────┘
```

**Requirements:**
- Support 2-3 lines of text
- Different colors per line (role, state, details)
- Compact but readable
- Still performant with 100+ agents

**Implementation Location:** `RenderSystem.RenderAgentLabel()`

**Color Suggestions:**
- **Role line:** Agent.Color (blue/green/red)
- **State line:** Yellow for running actions, Red for attacking, White for idle
- **Detail line:** Gray (subtle info)

**Sample Enhancement:**
```csharp
private void RenderAgentLabel(Vector2 position, Agent agent, BehaviorTreeBlob blob, bool isSelected)
{
    // Generate multi-line status
    var lines = GenerateMultiLineStatus(agent, blob);
    
    Vector2 labelPos = position + new Vector2(0, -35); // Higher for multi-line
    int fontSize = 10;
    int lineHeight = 12;
    
    // Calculate background size
    int maxWidth = lines.Max(l => Raylib.MeasureText(l.Text, fontSize));
    int bgHeight = lines.Count * lineHeight + 8;
    
    // Draw background
    Color bgColor = isSelected ? new Color(50, 50, 50, 220) : new Color(0, 0, 0, 180);
    Raylib.DrawRectangle(
        (int)(labelPos.X - maxWidth / 2 - 4),
        (int)(labelPos.Y - 4),
        maxWidth + 8,
        bgHeight,
        bgColor);
    
    // Draw each line with its color
    for (int i = 0; i < lines.Count; i++)
    {
        var line = lines[i];
        int textWidth = Raylib.MeasureText(line.Text, fontSize);
        Raylib.DrawText(
            line.Text,
            (int)(labelPos.X - textWidth / 2),
            (int)(labelPos.Y + i * lineHeight),
            fontSize,
            line.Color);
    }
}

private List<(string Text, Color Color)> GenerateMultiLineStatus(Agent agent, BehaviorTreeBlob blob)
{
    var lines = new List<(string, Color)>();
    
    // Line 1: Role
    lines.Add((agent.Role.ToString().ToUpper(), agent.Color));
    
    // Line 2: Current action/state
    if (blob.Nodes != null && agent.State.RunningNodeIndex >= 0 && agent.State.RunningNodeIndex < blob.Nodes.Length)
    {
        var node = blob.Nodes[agent.State.RunningNodeIndex];
        string action = GetNodeActionName(blob, node);
        Color actionColor = GetActionColor(action, agent);
        lines.Add((action, actionColor));
    }
    
    // Line 3: Contextual detail (role-specific)
    string detail = GetContextualDetail(agent);
    if (!string.IsNullOrEmpty(detail))
    {
        lines.Add((detail, new Color(180, 180, 180, 255))); // Gray
    }
    
    return lines;
}

private string GetContextualDetail(Agent agent)
{
    switch (agent.Role)
    {
        case AgentRole.Combat when agent.Blackboard.HasTarget:
            float dist = Vector2.Distance(agent.Position, agent.TargetPosition);
            return $"→#{agent.Blackboard.TargetAgentId} {dist:F0}px";
            
        case AgentRole.Gather:
            return $"Resources: {agent.Blackboard.ResourceCount}";
            
        case AgentRole.Patrol:
            return $"Point {agent.Blackboard.PatrolPointIndex + 1}/4";
            
        default:
            return "";
    }
}

private Color GetActionColor(string action, Agent agent)
{
    // Attack = bright red
    if (action.Contains("ATTACK") || agent.AttackFlashTimer > 0)
        return Color.RED;
    
    // Chasing = orange
    if (action.Contains("Chasing"))
        return Color.ORANGE;
    
    // Moving/Active = yellow
    if (action.Contains("Moving") || action.Contains("Chasing") || action.Contains("Gathering"))
        return Color.YELLOW;
    
    // Idle/Waiting = white
    return Color.WHITE;
}
```

---

### Enhancement 2: Time-Based Wait Countdown

**Current Issue:** Wait nodes show duration but not actual countdown

**Problem:** `BehaviorTreeState` doesn't expose `Time` or elapsed calculation directly

**Options:**

**Option A: Pass DemoContext time to status provider**
```csharp
public interface IAgentStatusProvider
{
    string GetAgentStatus(Agent agent, BehaviorTreeBlob blob, float currentTime);
}

// Then in Wait calculation:
float elapsed = currentTime - agent.State.AsyncData; // AsyncData stores start time
float remaining = duration - elapsed;
return $"Waiting ({remaining:F1}s)";
```

**Option B: Add helper property to BehaviorTreeState** (requires kernel change)
```csharp
// In BehaviorTreeState.cs
public float GetWaitElapsed(float currentTime)
{
    return currentTime - (float)AsyncData;
}
```

**Recommended:** Option A (no kernel changes needed)

---

### Enhancement 3: Node Inspector Improvements

**Add Missing Info:**

1. **For Wait nodes:** Show actual progress bar with real-time countdown
2. **For Parallel nodes:** Show which children are currently executing
3. **For all nodes:** Show node duration/execution time

**Implementation:**

```csharp
// Pass current time to inspector
_detailPanel.Render(agent, blob, currentTime);

// In RenderWaitDetails:
private void RenderWaitDetails(BehaviorTreeBlob blob, NodeDefinition node, BehaviorTreeState state, float currentTime)
{
    float duration = GetDuration(blob, node);
    float startTime = (float)state.AsyncData;
    float elapsed = currentTime - startTime;
    float remaining = MathF.Max(0, duration - elapsed);
    
    ImGui.Text($"Duration: {duration:F2}s");
    ImGui.Text($"Elapsed: {elapsed:F2}s");
    ImGui.Text($"Remaining: {remaining:F2}s");
    
    float progress = duration > 0 ? MathF.Min(1f, elapsed / duration) : 0;
    ImGui.ProgressBar(progress, new Vector2(-1, 0), $"{progress * 100:F0}%");
}
```

---

## 🎨 Expected Result

**After enhancements:**

```
Visual Demo Screen:

Agent Labels:
┌──────────┐  ┌──────────┐  ┌───────────┐
│ PATROL   │  │ GATHER   │  │ COMBAT    │
│ Moving   │  │ Gather   │  │ Chasing#3 │
│ Pt 2/4   │  │ Res: 5   │  │ →3 45px   │
└──────────┘  └──────────┘  └───────────┘
   (Blue)       (Green)         (Red)

Inspector when clicking Wait node:
┌─────────────────────────┐
│ Node [4] - Wait         │
│                         │
│ Duration: 2.00s         │
│ Elapsed: 1.23s          │
│ Remaining: 0.77s        │
│ [████████░░] 62%        │
│                         │
│ AsyncData: 123.45       │
│ LocalRegisters: ...     │
└─────────────────────────┘
```

---

## ✅ Tasks

1. **Multi-line labels:**
   - Modify `RenderAgentLabel()` to support multiple lines
   - Add color per line
   - Generate contextual detail line per agent role

2. **Time tracking:**
   - Pass `currentTime` through to status provider
   - Calculate elapsed time from `AsyncData`
   - Show real countdown for Wait nodes

3. **Enhanced inspector:**
   - Pass `currentTime` to `NodeDetailPanel.Render()`
   - Add progress bar to Wait node details
   - Show real-time values

---

## 🔧 Files to Modify

1. `RenderSystem.cs` - Multi-line label rendering
2. `AgentStatusProvider.cs` - Accept currentTime parameter
3. `NodeDetailPanel.cs` - Accept currentTime, show progress
4. `DemoApp.cs` - Pass `_time` to rendering/inspector
5. `TreeVisualizer.cs` - Pass currentTime to detail panel

---

## ⏱️ Time Estimate

- Multi-line labels: **45 minutes**
- Time-based countdown: **30 minutes**
- Inspector enhancements: **15 minutes**

**Total: 1.5 hours**

---

## 📊 Benefits

**Multi-line labels:**
- ✅ More information at a glance
- ✅ Color coding for quick state recognition
- ✅ Role-specific contextual data

**Time tracking:**
- ✅ See exact countdown on Wait nodes
- ✅ Understand timing better
- ✅ More educational/debugging value

**Enhanced inspector:**
- ✅ Complete picture of node state
- ✅ Real-time progress visualization
- ✅ Professional-grade debugging tool

---

**This makes the demo truly IMPRESSIVE and INFORMATIVE!** 🎨✨

*Enhancement Document Created: 2026-01-05*  
*Current Status: Base implementation complete, enhancements ready to implement*
