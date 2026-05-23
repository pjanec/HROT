# Fbt.Demo.Visual

**Project Path**: `FDP/ExtDeps/FastBTree/demos/Fbt.Demo.Visual/Fbt.Demo.Visual.csproj`
**Date**: 2026-05-23
**Framework**: net8.0
**Output Type**: Executable

---

## README Validation

**Status: Up-to-date.**

A `README.md` exists in the project folder (`demos/Fbt.Demo.Visual/README.md`). It accurately describes the demo features (Patrol, Gather, Combat agents, real-time tree visualization, controls) and the three JSON tree files. The source code matches the README. No divergence detected.

---

## Executive Overview

`Fbt.Demo.Visual` is an interactive 2D simulation that visualizes FastBTree behavior trees executing in real time. It is the primary showcase application for the FastBTree library, designed to teach three things at once:

1. **How behavior trees drive multi-agent AI** - multiple independent agents of three roles (Patrol, Gather, Combat) each tick their own behavior tree every frame.
2. **How to inspect tree execution state** - an ImGui-based inspector panel shows exactly which node is currently running (highlighted in yellow), the full node hierarchy, and the blackboard values for a selected agent.
3. **How to integrate FastBTree with a game loop** - the demo shows the complete lifecycle: load JSON, compile to blob, create an interpreter with an action registry, tick per frame, and render visual feedback.

This project is the reference implementation for anyone embedding FastBTree in a real-time Raylib application.

---

## Architecture

The demo follows a simple ECS-inspired pattern with a flat agent list, two systems (behavior and rendering), and a UI layer backed by ImGui. There is no formal ECS framework; it is intentionally kept minimal so the behavior tree integration remains visible and unobscured.

```
+-------------------------------------------------------------+
|                        DemoApp                              |
|                                                             |
|  _agents: List<Agent>     _trees: Dict<string, BlobTree>    |
|  _paused, _timeScale                                        |
|  _selectedAgent                                             |
|                                                             |
|  +-------------------+      +----------------------------+  |
|  |  BehaviorSystem   |      |       RenderSystem         |  |
|  |                   |      |                            |  |
|  | Interpreters[]    |      | RenderAgents()             |  |
|  | ActionRegistry    |      | RenderAgentLabel()         |  |
|  | Update(agents,dt) |      | IAgentStatusProvider       |  |
|  +-------------------+      +----------------------------+  |
|                                                             |
|  +-------------------------------------------------------+  |
|  |                  UI Layer (ImGui)                      |  |
|  |                                                        |  |
|  |  TreeVisualPanel    NodeDetailPanel                    |  |
|  |  AgentStatusProvider (DefaultStatusProvider)           |  |
|  +-------------------------------------------------------+  |
+-------------------------------------------------------------+
```

### Execution Flow

Each frame of the main loop proceeds as follows:

```
+--[ Main Loop ]-------------------------------------------+
|                                                          |
|  Raylib.GetFrameTime() -> dt                             |
|                                                          |
|  if !_paused:                                            |
|    BehaviorSystem.Update(agents, time, dt)               |
|      for each agent:                                     |
|        Interpreter.Tick(bb, state, ctx)    <-- FastBTree  |
|        agent.CurrentNode = highlight                     |
|        agent.UpdateMovement(dt)                          |
|                                                          |
|  RenderSystem.RenderAgents(agents, selected, trees, t)   |
|    for each agent: draw circle + label + attack ring     |
|                                                          |
|  ImGui panels:                                           |
|    ControlPanel (pause, spawn, time scale)               |
|    TreeVisualPanel (if agent selected)                   |
|    NodeDetailPanel (if tree node selected)               |
+----------------------------------------------------------+
```

---

## Source Structure

```
Fbt.Demo.Visual/
+-- Program.cs                  Entry point; creates DemoApp and calls Run()
+-- DemoApp.cs                  Main application class; owns game loop, camera,
|                               agents list, systems, UI panels
+-- Entities/
|   +-- Agent.cs                Agent entity: position, velocity, role, blackboard,
|                               BehaviorTreeState, visual state
+-- Systems/
|   +-- BehaviorSystem.cs       Owns interpreters and action registry; ticks all agents
|   +-- RenderSystem.cs         Draws agents, target lines, status labels, attack FX
+-- UI/
|   +-- AgentStatusProvider.cs  Interface + DefaultStatusProvider: maps running node
|                               to readable status strings above each agent
|   +-- TreeVisualizer.cs       ImGui panel: tree hierarchy view with clickable nodes
|   +-- NodeDetailPanel.cs      ImGui panel: shows raw node properties and exec state
+-- Trees/
    +-- patrol.json             Infinite Repeater -> Sequence(FindPatrolPoint, MoveToTarget, Wait 2s)
    +-- gather.json             Infinite Repeater -> Sequence(FindResource, Gather, ReturnToBase, Wait)
    +-- combat.json             Infinite Repeater -> Selector(combat branch, wander branch)
```

---

## Key Types

### Agent

```csharp
public class Agent
{
    public int Id { get; set; }
    public Vector2 Position { get; set; }
    public Vector2 Velocity { get; set; }
    public float Rotation { get; set; }
    public Color Color { get; set; }

    // Behavior tree binding
    public string TreeName;
    public AgentBlackboard Blackboard;    // struct - patrol/gather/combat data
    public BehaviorTreeState State;       // struct - interpreter execution state

    // Movement target
    public Vector2 TargetPosition;
    public float Speed = 50f;
    public AgentRole Role;

    // Visual feedback
    public TreeExecutionHighlight? CurrentNode;
    public float AttackFlashTimer;

    public void UpdateMovement(float dt);
}

public enum AgentRole { Patrol, Gather, Combat }

public struct AgentBlackboard
{
    public int PatrolPointIndex;
    public int ResourceCount;
    public bool HasTarget;
    public float LastPatrolTime;
    public int TargetAgentId;
}
```

### BehaviorSystem

```csharp
public class BehaviorSystem
{
    // One interpreter per named tree, keyed by tree name
    private Dictionary<string, Interpreter<AgentBlackboard, DemoContext>> _interpreters;
    private ActionRegistry<AgentBlackboard, DemoContext> _registry;

    public BehaviorSystem(Dictionary<string, BehaviorTreeBlob> trees);
    public void Update(List<Agent> agents, float time, float dt);

    // Registered actions:
    //   Patrol:  FindPatrolPoint, MoveToTarget, Wait
    //   Gather:  FindResource, Gather, ReturnToBase
    //   Combat:  ScanForEnemy, HasEnemy, ChaseEnemy, Attack, FindRandomPoint
}
```

### RenderSystem

```csharp
public class RenderSystem
{
    // Renders all agents onto the Raylib canvas
    public void RenderAgents(
        List<Agent> agents,
        Agent? selectedAgent,
        Dictionary<string, BehaviorTreeBlob> trees,
        float currentTime);
}
```

### IAgentStatusProvider

```csharp
public interface IAgentStatusProvider
{
    List<(string Text, Color Color)> GetAgentStatus(
        Agent agent, BehaviorTreeBlob blob, float currentTime);
}
```

Implemented by `DefaultStatusProvider`, which translates the running node index and action name into a human-readable status line rendered above each agent (e.g., "PATROL / Moving / dist: 142").

### TreeVisualPanel

```csharp
public class TreeVisualPanel
{
    public void Render(Agent agent, BehaviorTreeBlob blob, float currentTime);
}
```

Renders the tree node hierarchy inside an ImGui child window. The running node is highlighted yellow. Any node can be clicked to open the `NodeDetailPanel`.

### NodeDetailPanel

```csharp
public class NodeDetailPanel
{
    public void Render(Agent agent, BehaviorTreeBlob blob, float currentTime);
    public bool IsNodeSelected(int index);
    public void SetSelectedNode(int index);
}
```

Shows raw node properties (Type, ChildCount, SubtreeOffset, PayloadIndex) and type-specific execution state (wait countdown, repeat count, async data).

---

## Dependencies

| Package / Project | Version / Path | Purpose |
|---|---|---|
| `Raylib-cs` | 7.0.2 | 2D rendering, window, input, camera |
| `ImGui.NET` | 1.91.6.1 | Immediate-mode debug UI panels |
| `rlImgui-cs` | 3.2.0 | Bridge between Raylib and ImGui.NET |
| `Fbt.Kernel` | (project ref) | BehaviorTreeBlob, Interpreter, ActionRegistry, TreeCompiler |

The project has no dependency on `Fbt.Compiler` directly; it uses `TreeCompiler.CompileFromJson()` which is part of `Fbt.Kernel` (or `Fbt.Serialization`).

---

## Behavior Tree JSON Format

The demo loads three JSON trees from the `Trees/` directory. They are copied to the output directory as `PreserveNewest`. The format used by `TreeCompiler.CompileFromJson()`:

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

The combat tree uses a Selector with two child Sequences to implement a priority fallback pattern:

```json
{
    "TreeName": "CombatAgent",
    "Root": {
        "Type": "Repeater", "RepeatCount": -1,
        "Children": [{
            "Type": "Selector",
            "Children": [
                {
                    "Type": "Sequence",
                    "Children": [
                        { "Type": "Condition", "Action": "HasEnemy" },
                        { "Type": "Action",    "Action": "ChaseEnemy" },
                        { "Type": "Action",    "Action": "Attack" },
                        { "Type": "Wait",      "Duration": 1.0 }
                    ]
                },
                {
                    "Type": "Sequence",
                    "Children": [
                        { "Type": "Action", "Action": "FindRandomPoint" },
                        { "Type": "Action", "Action": "MoveToTarget" },
                        { "Type": "Action", "Action": "ScanForEnemy" },
                        { "Type": "Wait",   "Duration": 2.0 }
                    ]
                }
            ]
        }]
    }
}
```

---

## Usage Examples

### Example 1: Running the Demo

```bash
# From the repository root
dotnet run --project FDP/ExtDeps/FastBTree/demos/Fbt.Demo.Visual

# Or from the demos directory
cd FDP/ExtDeps/FastBTree/demos/Fbt.Demo.Visual
dotnet run
```

Controls once running:
- **Mouse wheel**: zoom the 2D camera
- **Left-click drag**: pan the camera
- **Left-click on agent**: select agent and open tree inspector
- **ImGui "Pause" checkbox**: freeze simulation
- **ImGui "Time Scale" slider**: speed up or slow down time
- **ImGui "Spawn" buttons**: add more agents dynamically

### Example 2: Integrating a New Behavior Tree

To add a fourth agent role "Scout" to the demo:

1. Create `Trees/scout.json`:

```json
{
    "TreeName": "ScoutAgent",
    "Root": {
        "Type": "Repeater",
        "RepeatCount": -1,
        "Children": [{
            "Type": "Sequence",
            "Children": [
                { "Type": "Action", "Action": "MoveToEdge" },
                { "Type": "Action", "Action": "Observe" },
                { "Type": "Wait",   "Duration": 3.0 }
            ]
        }]
    }
}
```

2. Load and compile in `DemoApp.Initialize()`:

```csharp
_trees["scout"] = LoadTree("Trees/scout.json");
```

3. Register the new actions in `BehaviorSystem.RegisterActions()`:

```csharp
_registry.Register("MoveToEdge", MoveToEdge);
_registry.Register("Observe", Observe);
```

4. Implement the action delegates:

```csharp
private NodeStatus MoveToEdge(
    ref AgentBlackboard bb,
    ref BehaviorTreeState state,
    ref DemoContext ctx,
    int payload)
{
    ctx.Agent.TargetPosition = new Vector2(10, ctx.Agent.Position.Y);
    return NodeStatus.Success;
}

private NodeStatus Observe(
    ref AgentBlackboard bb,
    ref BehaviorTreeState state,
    ref DemoContext ctx,
    int payload)
{
    // Scout logic here
    return NodeStatus.Success;
}
```

5. Add `AgentRole.Scout` to the enum and spawn logic:

```csharp
_agents.Add(new Agent(_agents.Count + 1, pos, "scout", AgentRole.Scout));
```

### Example 3: Reading the Inspector Output

When an agent is selected, the `TreeVisualPanel` renders its tree like this (running node in yellow):

```
[0] Repeater
    [1] Selector
        [2] Sequence
            [3] Condition "HasEnemy"
            [4] Action "ChaseEnemy"     <-- highlighted yellow
            [5] Action "Attack"
            [6] Wait (1s)
        [7] Sequence
            [8] Action "FindRandomPoint"
            [9] Action "MoveToTarget"
            [10] Action "ScanForEnemy"
            [11] Wait (2s)
```

Clicking node `[4]` opens the `NodeDetailPanel` showing:
- Type: Action
- ChildCount: 0
- PayloadIndex: 1 (index into MethodNames)
- Method: "ChaseEnemy"

---

## Architecture Diagram: Camera and Selection

```
+--[ Camera2D / Selection ]-----------------------------------+
|                                                            |
|  Screen space  <---> World space via Raylib.GetScreenTo-   |
|                       World2D(_camera)                     |
|                                                            |
|  Mouse wheel:                                              |
|    _camera.Zoom *= scaleFactor  (clamped 0.125 .. 64)      |
|    _camera.Offset = mouse pos (zoom towards cursor)        |
|                                                            |
|  Left drag (no ImGui focus):                               |
|    _camera.Target += delta * (-1 / zoom)                   |
|                                                            |
|  Left click (no ImGui focus):                              |
|    worldPos = GetScreenToWorld2D(mousePos, _camera)        |
|    for each agent:                                         |
|      if Distance(worldPos, agent.Position) < 12:           |
|        _selectedAgent = agent                              |
+------------------------------------------------------------+
```

---

## Architecture Diagram: Action Dispatch

```
+--[ Action Dispatch Chain ]----------------------------------+
|                                                            |
|  JSON file                                                 |
|    |                                                       |
|    v  TreeCompiler.CompileFromJson()                       |
|  BehaviorTreeBlob                                          |
|    - Nodes[]         (NodeDefinition structs)              |
|    - MethodNames[]   (string names of registered actions)  |
|    - FloatParams[]   (Wait/Cooldown durations)             |
|    |                                                       |
|    v  Interpreter<AgentBlackboard, DemoContext>(blob, reg) |
|  Interpreter                                               |
|    |                                                       |
|    v  interpreter.Tick(ref bb, ref state, ref ctx)         |
|  When an Action node is reached:                           |
|    node.PayloadIndex -> MethodNames[index] -> registry     |
|    ActionRegistry.Invoke(name, ref bb, ref state, ref ctx) |
|    -> delegates registered by BehaviorSystem               |
|       e.g. Attack(ref bb, ref state, ref ctx, payload)     |
+------------------------------------------------------------+
```

---

## Best Practices Illustrated

1. **One interpreter per tree name, not per agent.** All patrol agents share the same `Interpreter<AgentBlackboard, DemoContext>`. The per-agent state is stored in `Agent.State` (a `BehaviorTreeState` struct) and `Agent.Blackboard`. This avoids allocating an interpreter per agent.

2. **Context carries transient per-tick data.** The `DemoContext` struct holds `Agent`, `Time`, and `DeltaTime`. This is the correct pattern: the context is re-populated each tick before calling `Tick()` and is never stored long-term.

3. **Guards are conditions.** The combat tree's "HasEnemy" node is registered as a `Condition` in the JSON and mapped to a function that returns `NodeStatus.Success` or `NodeStatus.Failure`. This pattern avoids hard-coding control flow in the tree compiler.

4. **Visual feedback from `AttackFlashTimer`.** The demo shows how to add visual signals that do not interact with the behavior tree: the `Attack` action sets `AttackFlashTimer = 0.3f`, and `RenderSystem` reads it without the tree knowing anything about rendering.

5. **Camera zoom toward cursor.** The camera implementation correctly anchors the zoom to the mouse cursor position by setting `_camera.Offset = mousePos` and `_camera.Target = worldPosAtMouse` before scaling.

6. **`IAgentStatusProvider` is injectable.** The `RenderSystem` accepts any `IAgentStatusProvider` implementation, making it straightforward to override status text for specialized agent types without modifying the render system.

---

## Related Projects

| Project | Relationship |
|---|---|
| `Fbt.Kernel` | Runtime dependency. Provides `BehaviorTreeBlob`, `Interpreter<BB,Ctx>`, `ActionRegistry`, `BehaviorTreeState`, `NodeStatus`, `TreeCompiler` |
| `Fbt.Examples.Console` | Sister project. Shows minimal tree execution without Raylib or ImGui |
| `Fbt.Examples.FluentBTree` | Sister project. Shows hot-reload-capable tree setup using the fluent API |
| `Fbt.Examples.FluentBTree.Trees` | Tree definitions consumed by `Fbt.Examples.FluentBTree` |
| `Fhsm.Demo.Visual` | Analogous demo for FastHSM. Same Raylib+ImGui architecture |

---

## Build and Run

Requirements:
- .NET 8 SDK
- Raylib native libraries (pulled via `Raylib-cs` NuGet)

```bash
# Build only
dotnet build FDP/ExtDeps/FastBTree/demos/Fbt.Demo.Visual

# Build and run
dotnet run --project FDP/ExtDeps/FastBTree/demos/Fbt.Demo.Visual
```

The tree JSON files are in `Trees/` and are configured as `CopyToOutputDirectory: PreserveNewest` in the `.csproj`. They must exist relative to the executable at runtime.

---

## Performance Notes

- The demo targets 60 FPS via `Raylib.SetTargetFPS(60)`.
- All behavior tree state is in unmanaged structs (`BehaviorTreeState`, `AgentBlackboard`), keeping GC pressure zero for the behavior layer.
- `Interpreter.Tick()` is a synchronous, single-threaded call; the demo runs all agents sequentially on the main thread.
- The `_timeScale` slider allows up to arbitrarily high simulation speeds to stress-test the trees without changing the render rate.
- The `IAgentStatusProvider` lookup runs every render frame for every agent and does string allocations; this is acceptable for a demo but would be cached in production.
