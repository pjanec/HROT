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
