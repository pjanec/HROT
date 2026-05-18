# Enhanced Visualization System Design

**Feature:** Dynamic Agent Labels + Detailed Node Inspector  
**Goal:** Show what agents are doing at a glance + drill down into node details  
**Design Date:** 2026-01-05

---

## 🎯 Two-Part System

### Part 1: Dynamic Agent Labels (Above Avatar)
Show contextual status like:
- "Patrolling (2/4)"
- "Gathering resources"
- "Chasing Agent #7"
- "Attacking!"

### Part 2: Detailed Node Inspector (In UI)
Click a node in tree view → Show:
- Node parameters (duration, count, etc.)
- Current execution state
- Local registers used
- Custom node-specific data

---

## 🏗️ Architecture

### Component 1: IAgentStatusProvider

**Purpose:** Generate human-readable status strings for agents

```csharp
public interface IAgentStatusProvider
{
    string GetAgentStatus(Agent agent, BehaviorTreeBlob blob);
}
```

**Implementation:**

```csharp
public class DefaultStatusProvider : IAgentStatusProvider
{
    public string GetAgentStatus(Agent agent, BehaviorTreeBlob blob)
    {
        // If tree not running, show idle
        if (agent.State.RunningNodeIndex < 0 || 
            agent.State.RunningNodeIndex >= blob.Nodes.Length)
        {
            return $"{agent.Role}";
        }
        
        var runningNode = blob.Nodes[agent.State.RunningNodeIndex];
        
        // Build status based on node type + action name
        return BuildStatusString(agent, blob, runningNode, agent.State.RunningNodeIndex);
    }
    
    private string BuildStatusString(
        Agent agent, 
        BehaviorTreeBlob blob, 
        NodeDefinition node, 
        int nodeIndex)
    {
        // For Action/Condition nodes - show the method name
        if (node.Type == NodeType.Action || node.Type == NodeType.Condition)
        {
            string actionName = GetActionName(blob, node);
            
            // Custom formatting based on action name
            return actionName switch
            {
                "FindPatrolPoint" => "Picking patrol point",
                "MoveToTarget" => "Moving...",
                "FindResource" => "Looking for resources",
                "Gather" => "Gathering",
                "ReturnToBase" => "Returning to base",
                "ScanForEnemy" => "Scanning for enemies",
                "ChaseEnemy" => $"Chasing Agent #{agent.Blackboard.TargetAgentId}",
                "Attack" => "⚔️ ATTACKING!",
                _ => actionName
            };
        }
        
        // For Wait nodes - show countdown
        if (node.Type == NodeType.Wait)
        {
            float duration = GetWaitDuration(blob, node);
            float elapsed = GetWaitElapsed(agent.State);
            float remaining = duration - elapsed;
            return $"Waiting ({remaining:F1}s)";
        }
        
        // For Repeater - show iteration
        if (node.Type == NodeType.Repeater)
        {
            int currentCount = GetRepeaterCount(agent.State);
            int maxCount = GetRepeaterMax(blob, node);
            if (maxCount < 0)
                return $"Loop #{currentCount}";
            return $"Loop {currentCount}/{maxCount}";
        }
        
        // Default: show node type
        return node.Type.ToString();
    }
    
    private string GetActionName(BehaviorTreeBlob blob, NodeDefinition node)
    {
        if (node.PayloadIndex >= 0 && node.PayloadIndex < (blob.MethodNames?.Length ?? 0))
            return blob.MethodNames[node.PayloadIndex];
        return "Unknown";
    }
    
    private float GetWaitDuration(BehaviorTreeBlob blob, NodeDefinition node)
    {
        if (node.PayloadIndex >= 0 && node.PayloadIndex < (blob.FloatParams?.Length ?? 0))
            return blob.FloatParams[node.PayloadIndex];
        return 0f;
    }
    
    private float GetWaitElapsed(BehaviorTreeState state)
    {
        // AsyncToken stores start time for Wait nodes
        return state.Time - state.AsyncToken;
    }
    
    private int GetRepeaterCount(BehaviorTreeState state)
    {
        // Repeater stores count in LocalRegisters[0]
        return state.LocalRegisters[0];
    }
    
    private int GetRepeaterMax(BehaviorTreeBlob blob, NodeDefinition node)
    {
        if (node.PayloadIndex >= 0 && node.PayloadIndex < (blob.IntParams?.Length ?? 0))
            return blob.IntParams[node.PayloadIndex];
        return -1;
    }
}
```

---

### Component 2: Node Detail Inspector

**Purpose:** Show detailed state of a specific node when clicked

```csharp
public class NodeDetailPanel
{
    private int? _selectedNodeIndex = null;
    
    public void Render(Agent agent, BehaviorTreeBlob blob)
    {
        if (_selectedNodeIndex == null) return;
        
        ImGui.Begin("Node Details");
        
        int nodeIndex = _selectedNodeIndex.Value;
        if (nodeIndex >= 0 && nodeIndex < blob.Nodes.Length)
        {
            RenderNodeDetails(agent, blob, nodeIndex);
        }
        
        ImGui.End();
    }
    
    private void RenderNodeDetails(Agent agent, BehaviorTreeBlob blob, int nodeIndex)
    {
        var node = blob.Nodes[nodeIndex];
        
        ImGui.TextColored(new Vector4(0.5f, 0.8f, 1.0f, 1.0f), 
            $"Node [{nodeIndex}] - {node.Type}");
        
        ImGui.Separator();
        
        // Basic properties
        ImGui.Text($"Type: {node.Type}");
        ImGui.Text($"ChildCount: {node.ChildCount}");
        ImGui.Text($"SubtreeOffset: {node.SubtreeOffset}");
        ImGui.Text($"PayloadIndex: {node.PayloadIndex}");
        
        ImGui.Separator();
        
        // Type-specific details
        switch (node.Type)
        {
            case NodeType.Action:
            case NodeType.Condition:
                RenderActionDetails(blob, node, agent);
                break;
                
            case NodeType.Wait:
            case NodeType.Cooldown:
                RenderWaitDetails(blob, node, agent.State);
                break;
                
            case NodeType.Repeater:
                RenderRepeaterDetails(blob, node, agent.State);
                break;
                
            case NodeType.Parallel:
                RenderParallelDetails(blob, node, agent.State);
                break;
        }
        
        // Execution state
        ImGui.Separator();
        ImGui.TextColored(new Vector4(1f, 1f, 0f, 1f), "Execution State");
        
        bool isRunning = agent.State.RunningNodeIndex == nodeIndex;
        ImGui.Text($"Is Running: {isRunning}");
        
        if (isRunning)
        {
            ImGui.Indent();
            ImGui.Text($"AsyncToken: {agent.State.AsyncToken}");
            ImGui.Text($"AsyncData: {agent.State.AsyncData}");
            ImGui.Text("LocalRegisters:");
            for (int i = 0; i < 4; i++)
            {
                ImGui.Text($"  [{i}] = {agent.State.LocalRegisters[i]}");
            }
            ImGui.Unindent();
        }
    }
    
    private void RenderActionDetails(BehaviorTreeBlob blob, NodeDefinition node, Agent agent)
    {
        ImGui.TextColored(new Vector4(1f, 0.8f, 0.3f, 1f), "Action Details");
        
        string methodName = "Unknown";
        if (node.PayloadIndex >= 0 && node.PayloadIndex < (blob.MethodNames?.Length ?? 0))
        {
            methodName = blob.MethodNames[node.PayloadIndex];
        }
        
        ImGui.Text($"Method: {methodName}");
        
        // Show relevant blackboard fields based on action
        ImGui.Text("Relevant State:");
        ImGui.Indent();
        
        switch (methodName)
        {
            case "ChaseEnemy":
            case "Attack":
                ImGui.Text($"HasTarget: {agent.Blackboard.HasTarget}");
                ImGui.Text($"TargetAgentId: {agent.Blackboard.TargetAgentId}");
                ImGui.Text($"TargetPosition: {agent.TargetPosition}");
                break;
                
            case "FindPatrolPoint":
            case "MoveToTarget":
                ImGui.Text($"PatrolPointIndex: {agent.Blackboard.PatrolPointIndex}");
                ImGui.Text($"TargetPosition: {agent.TargetPosition}");
                ImGui.Text($"CurrentPosition: {agent.Position}");
                float dist = Vector2.Distance(agent.Position, agent.TargetPosition);
                ImGui.Text($"Distance to Target: {dist:F1}");
                break;
                
            case "Gather":
                ImGui.Text($"ResourceCount: {agent.Blackboard.ResourceCount}");
                break;
        }
        
        ImGui.Unindent();
    }
    
    private void RenderWaitDetails(BehaviorTreeBlob blob, NodeDefinition node, BehaviorTreeState state)
    {
        ImGui.TextColored(new Vector4(1f, 0.8f, 0.3f, 1f), "Wait Details");
        
        float duration = 0f;
        if (node.PayloadIndex >= 0 && node.PayloadIndex < (blob.FloatParams?.Length ?? 0))
        {
            duration = blob.FloatParams[node.PayloadIndex];
        }
        
        ImGui.Text($"Duration: {duration:F2}s");
        
        float elapsed = state.Time - state.AsyncToken;
        float remaining = duration - elapsed;
        
        ImGui.Text($"Elapsed: {elapsed:F2}s");
        ImGui.Text($"Remaining: {remaining:F2}s");
        
        float progress = duration > 0 ? (elapsed / duration) : 0;
        ImGui.ProgressBar(progress, new Vector2(-1, 0), $"{progress * 100:F0}%");
    }
    
    private void RenderRepeaterDetails(BehaviorTreeBlob blob, NodeDefinition node, BehaviorTreeState state)
    {
        ImGui.TextColored(new Vector4(1f, 0.8f, 0.3f, 1f), "Repeater Details");
        
        int maxCount = -1;
        if (node.PayloadIndex >= 0 && node.PayloadIndex < (blob.IntParams?.Length ?? 0))
        {
            maxCount = blob.IntParams[node.PayloadIndex];
        }
        
        int currentCount = state.LocalRegisters[0];
        
        ImGui.Text($"Max Count: {(maxCount < 0 ? "Infinite" : maxCount.ToString())}");
        ImGui.Text($"Current Iteration: {currentCount}");
        ImGui.Text($"Uses LocalRegisters[0]");
        
        if (maxCount > 0)
        {
            float progress = (float)currentCount / maxCount;
            ImGui.ProgressBar(progress, new Vector2(-1, 0), $"{currentCount}/{maxCount}");
        }
    }
    
    private void RenderParallelDetails(BehaviorTreeBlob blob, NodeDefinition node, BehaviorTreeState state)
    {
        ImGui.TextColored(new Vector4(1f, 0.8f, 0.3f, 1f), "Parallel Details");
        
        int policy = 0;
        if (node.PayloadIndex >= 0 && node.PayloadIndex < (blob.IntParams?.Length ?? 0))
        {
            policy = blob.IntParams[node.PayloadIndex];
        }
        
        ImGui.Text($"Policy: {(policy == 0 ? "RequireAll" : "RequireOne")}");
        ImGui.Text($"Child Count: {node.ChildCount}");
        ImGui.Text($"Uses LocalRegisters[3] (bitfield)");
        
        // Decode bitfield
        int bitfield = state.LocalRegisters[3];
        ImGui.Text("Child States:");
        ImGui.Indent();
        
        for (int i = 0; i < node.ChildCount && i < 16; i++)
        {
            bool finished = (bitfield & (1 << i)) != 0;
            bool success = (bitfield & (1 << (i + 16))) != 0;
            
            string status = finished 
                ? (success ? "✓ Success" : "✗ Failure")
                : "⋯ Running";
            
            ImGui.Text($"  Child {i}: {status}");
        }
        
        ImGui.Unindent();
    }
    
    public void SetSelectedNode(int? nodeIndex)
    {
        _selectedNodeIndex = nodeIndex;
    }
}
```

---

### Component 3: Rendering Agent Labels

**In RenderSystem.cs:**

```csharp
public class RenderSystem
{
    private IAgentStatusProvider _statusProvider = new DefaultStatusProvider();
    
    public void RenderAgents(List<Agent> agents, Agent? selectedAgent, Dictionary<string, BehaviorTreeBlob> trees)
    {
        foreach (var agent in agents)
        {
            // ... existing rendering ...
            
            // NEW: Render status label above agent
            if (trees.TryGetValue(agent.TreeName, out var blob))
            {
                string status = _statusProvider.GetAgentStatus(agent, blob);
                RenderAgentLabel(agent.Position, status, agent == selectedAgent);
            }
        }
    }
    
    private void RenderAgentLabel(Vector2 position, string text, bool isSelected)
    {
        // Position above agent
        Vector2 labelPos = position + new Vector2(0, -25);
        
        // Measure text
        int fontSize = 12;
        Vector2 textSize = Raylib.MeasureTextEx(
            Raylib.GetFontDefault(), 
            text, 
            fontSize, 
            1);
        
        // Background box
        Color bgColor = isSelected 
            ? new Color(50, 50, 50, 200)   // Darker for selected
            : new Color(0, 0, 0, 150);      // Semi-transparent black
        
        Raylib.DrawRectangle(
            (int)(labelPos.X - textSize.X / 2 - 4),
            (int)(labelPos.Y - 4),
            (int)(textSize.X + 8),
            (int)(textSize.Y + 8),
            bgColor);
        
        // Text
        Color textColor = isSelected ? Color.Yellow : Color.White;
        Raylib.DrawText(
            text,
            (int)(labelPos.X - textSize.X / 2),
            (int)labelPos.Y,
            fontSize,
            textColor);
    }
}
```

---

### Component 4: Interactive Tree View

**In TreeVisualizer.cs:**

```csharp
public class TreeVisualPanel
{
    private NodeDetailPanel _detailPanel = new NodeDetailPanel();
    private int? _hoveredNodeIndex = null;
    
    public void Render(Agent agent, BehaviorTreeBlob blob)
    {
        ImGui.Begin($"Agent Inspector - ID: {agent.Id}");
        
        // ... existing agent details ...
        
        ImGui.Separator();
        
        ImGui.TextColored(new Vector4(0.5f, 0.8f, 1.0f, 1.0f), 
            $"Behavior Tree: {blob.TreeName}");
        ImGui.Text($"Running Node Index: {agent.State.RunningNodeIndex}");
        ImGui.Text("💡 Click a node for details");
        
        ImGui.Separator();
        
        ImGui.BeginChild("TreeScroll", new Vector2(0, 300), ImGuiChildFlags.Borders);
        RenderNode(blob, 0, agent.State.RunningNodeIndex, 0);
        ImGui.EndChild();
        
        ImGui.End();
        
        // Render detail panel if node selected
        _detailPanel.Render(agent, blob);
    }
    
    private void RenderNode(BehaviorTreeBlob blob, int index, int runningIndex, int depth)
    {
        if (index >= blob.Nodes.Length) return;
        
        var node = blob.Nodes[index];
        string indent = new string(' ', depth * 4);
        
        // Determine color
        Vector4 color = (index == runningIndex)
            ? new Vector4(1f, 1f, 0f, 1f) // Yellow for running
            : new Vector4(1f, 1f, 1f, 1f); // White otherwise
        
        // Build node text
        string extra = "";
        if (node.Type == NodeType.Action || node.Type == NodeType.Condition)
        {
             if (node.PayloadIndex >= 0 && node.PayloadIndex < blob.MethodNames.Length)
                extra = $" \"{blob.MethodNames[node.PayloadIndex]}\"";
        }
        else if (node.Type == NodeType.Wait || node.Type == NodeType.Cooldown)
        {
            if (node.PayloadIndex >= 0 && node.PayloadIndex < blob.FloatParams.Length)
                extra = $" ({blob.FloatParams[node.PayloadIndex]}s)";
        }
        
        string nodeText = $"{indent}[{index}] {node.Type}{extra}";
        
        // Make it selectable for detail view
        ImGui.PushID(index);
        bool isSelected = _detailPanel.IsNodeSelected(index);
        
        if (ImGui.Selectable(nodeText, isSelected, ImGuiSelectableFlags.AllowDoubleClick))
        {
            _detailPanel.SetSelectedNode(index);
        }
        
        // Apply color
        if (index == runningIndex)
        {
            // Re-render with color for running node
            ImGui.SameLine(0, -ImGui.CalcTextSize(nodeText).X);
            ImGui.TextColored(color, nodeText);
        }
        
        ImGui.PopID();
        
        // Render children
        int childIndex = index + 1;
        for (int i = 0; i < node.ChildCount; i++)
        {
            RenderNode(blob, childIndex, runningIndex, depth + 1);
            childIndex += blob.Nodes[childIndex].SubtreeOffset;
        }
    }
}
```

---

## 🎨 Visual Design

### Agent Labels (Above Avatar)

```
┌─────────────────────┐
│ Chasing Agent #7    │  ← Semi-transparent black box
└─────────────────────┘
        ↓
       ● ← Red combat agent
```

**States Examples:**
- Patrol: `"Patrolling (3/4)"`
- Gather: `"Gathering"` or `"Returning to base"`
- Combat: `"Chasing Agent #7"` or `"⚔️ ATTACKING!"`
- Wait: `"Waiting (1.5s)"`

### Node Detail Panel (Inspector)

```
┌───────────────────────────────┐
│ Node Details                  │
├───────────────────────────────┤
│ Node [5] - Action             │
│                               │
│ Type: Action                  │
│ ChildCount: 0                 │
│ SubtreeOffset: 1              │
│ PayloadIndex: 3               │
│ ───────────────────────────   │
│ Action Details                │
│ Method: ChaseEnemy            │
│                               │
│ Relevant State:               │
│   HasTarget: true             │
│   TargetAgentId: 7            │
│   TargetPosition: (342, 567)  │
│   CurrentPosition: (298, 532) │
│   Distance to Target: 52.3    │
│ ───────────────────────────   │
│ Execution State               │
│ Is Running: true              │
│   AsyncToken: 0               │
│   AsyncData: 0                │
│   LocalRegisters:             │
│     [0] = 0                   │
│     [1] = 0                   │
│     [2] = 0                   │
│     [3] = 0                   │
└───────────────────────────────┘
```

---

## 📊 Benefits

**Agent Labels:**
- ✅ Instant understanding of what each agent is doing
- ✅ No need to click to see basic state
- ✅ Scalable: works with many agents on screen
- ✅ Context-aware: different text per action/state

**Node Details:**
- ✅ Deep dive into any node's state
- ✅ See exact register values
- ✅ Understand timing (Wait countdown)
- ✅ Debug Parallel bitfields
- ✅ Educational: learn how nodes work internally

---

## 🔧 Implementation Steps

1. **Add `DefaultStatusProvider`** - Generate status strings
2. **Update `RenderSystem`** - Draw labels above agents
3. **Add `NodeDetailPanel`** - Detailed inspector UI
4. **Update `TreeVisualPanel`** - Make nodes selectable
5. **Pass trees to RenderAgents** - Need blob for status generation

**Estimated Time:** 2-3 hours

---

## 🎯 Extensibility

**Custom Status for New Actions:**
```csharp
// Just add to the switch in BuildStatusString:
"MyCustomAction" => $"Doing my thing ({someParam})",
```

**Custom Node Details:**
```csharp
// Add case in RenderNodeDetails:
case NodeType.MyCustomNode:
    RenderMyCustomDetails(blob, node, agent.State);
    break;
```

---

**This creates a COMPLETE visual debugging system!** 🎨🔍

