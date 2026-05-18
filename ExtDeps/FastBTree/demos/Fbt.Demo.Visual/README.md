# Fbt.Demo.Visual

This is a Raylib-based visual demonstration of the FastBTree library.

## Features

- **2D Agent Simulation**: Watch agents perform different behaviors (Patrol, Gather, Combat).
- **Real-time Tree Visualization**: Inspect the active behavior tree of any agent to see exactly which nodes are running.
- **Interactive Controls**: Pause, spawn agents, and adjust time scale.
- **Performance Metrics**: Monitor FPS and tick efficiency.

## Controls

- **Pause**: Toggle simulation pause.
- **Time Scale**: Slider to speed up or slow down time.
- **Spawn Buttons**: Add more agents dynamically.
- **Agent List**: Select an agent to inspect its behavior tree.

## Running the Demo

1. Ensure you have the .NET SDK installed.
2. Run from the root directory:

```bash
dotnet run --project demos/Fbt.Demo.Visual
```

## Trees

The demo uses three sample behavior trees defined in `Trees/`:
- `patrol.json`: Simple patrol loop.
- `gather.json`: Resource gathering cycle.
- `combat.json`: Enemy scanning and engagement logic.
