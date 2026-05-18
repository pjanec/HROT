# Quick Start Guide

This guide will get you up and running with FastBTree in minutes.

## 1. Concept Overview

FastBTree separates the **Node Logic** (C#) from the **Tree Structure** (JSON).

- **Interpreter**: The engine that runs the tree.
- **Blackboard**: Shared state struct (entity data).
- **Context**: Environment info struct (time, world queries).
- **State**: Persistent execution state struct (running node index).

## 2. Setting Up Data Structures

Define your structs. They must be `struct` to support zero-allocation by-ref passing.

```csharp
public struct MyBlackboard
{
    public float Health;
    public Vector3 Position;
    public int TargetEntityId;
}

public struct MyContext : IAIContext
{
    public float DeltaTime { get; set; }
    public float Time { get; set; }
    public int FrameCount { get; set; }
    
    // Implement required interface methods (can be stubs if not used)
    public int RequestRaycast(...) => 0;
    public RaycastResult GetRaycastResult(...) => default;
    public int RequestPath(...) => 0;
    public PathResult GetPathResult(...) => default;
    public float GetFloatParam(int i) => 0;
    public int GetIntParam(int i) => 0;
}
```

## 3. Writing Actions and Conditions

Actions are static methods or delegates matching `NodeLogicDelegate`.

```csharp
public static class AIBehaviors
{
    public static NodeStatus IsEnemyVisible(
        ref MyBlackboard bb, 
        ref BehaviorTreeState state, 
        ref MyContext ctx, 
        int paramIndex)
    {
        // Simple check
        if (bb.TargetEntityId > 0)
            return NodeStatus.Success;
            
        return NodeStatus.Failure;
    }
    
    public static NodeStatus ChaseTarget(
        ref MyBlackboard bb, 
        ref BehaviorTreeState state, 
        ref MyContext ctx, 
        int paramIndex)
    {
        // Move towards target logic...
        // Return Running if taking time
        return NodeStatus.Running;
    }
}
```

## 4. Authoring a Tree

Create a JSON file (e.g., `enemy.json`).

```json
{
  "TreeName": "BasicEnemy",
  "Root": {
    "Type": "Selector",
    "Children": [
      {
        "Type": "Sequence", 
        "Children": [
          { "Type": "Condition", "Action": "IsEnemyVisible" },
          { "Type": "Action", "Action": "ChaseTarget" }
        ]
      },
      { 
        "Type": "Sequence",
        "Children": [
            { "Type": "Action", "Action": "Patrol" },
            { "Type": "Wait", "WaitTime": 2.0 }
        ]
      }
    ]
  }
}
```

**Common Nodes:**
- `Sequence`: Runs children in order. Fails if one fails.
- `Selector`: Runs children in order. Succeeds if one succeeds.
- `Parallel`: Runs children simultaneously.
- `Wait`: Pauses execution for a time.
- `Repeater`: Loops a child.
- `Cooldown`: Limits execution rate.

## 5. Integrating into Game Loop

```csharp
// 1. Compile (do this once on load)
var blob = TreeCompiler.CompileFromJson(File.ReadAllText("enemy.json"));

// 2. Register (do this once)
var registry = new ActionRegistry<MyBlackboard, MyContext>();
registry.Register("IsEnemyVisible", AIBehaviors.IsEnemyVisible);
registry.Register("ChaseTarget", AIBehaviors.ChaseTarget);
registry.Register("Patrol", AIBehaviors.Patrol);

// 3. Init agents (per entity)
var interpreter = new Interpreter<MyBlackboard, MyContext>(blob, registry);
var state = new BehaviorTreeState(); // Persistent state
var blackboard = new MyBlackboard(); // Entity data

// 4. Update Loop (every frame)
void Update(float dt)
{
    var context = new MyContext { DeltaTime = dt, Time = Time.Time };
    
    // Pass by ref for performance
    interpreter.Tick(ref blackboard, ref state, ref context);
}
```

## Best Practices

1. **Keep Context/Blackboard Small**: They are structs passed by ref. 
2. **Use Cached Actions**: Always register actions once and reuse the registry or interpreter.
3. **Avoid Garbage**: Don't allocate classes inside Actions.
4. **Use Subtree Offsets**: The binary format handles this, but know that "Jumping" is basically pointer arithmetic (index + offset).

## Design Patterns

### Patrol with Wait
```json
"Type": "Sequence",
"Children": [
    { "Type": "Action", "Action": "MoveToNextPoint" },
    { "Type": "Wait", "WaitTime": 5.0 }
]
```

### Rate-Limited Attack
```json
"Type": "Selector",
"Children": [
    { 
        "Type": "Cooldown", 
        "CooldownTime": 2.0,
        "Children": [ { "Type": "Action", "Action": "HeavyAttack" } ]
    },
    { "Type": "Action", "Action": "LightAttack" }
]
```
(Tries Heavy Attack every 2s, otherwise Light Attack)

### Constant Pressure (Parallel)
```json
"Type": "Parallel",
"Policy": "RequireOne",
"Children": [
    { "Type": "Action", "Action": "ChargeForward" },
    { "Type": "Action", "Action": "ScreamAudio" }
]
```
(Charges and Screams at the same time)

---
See `examples/` for more code.
