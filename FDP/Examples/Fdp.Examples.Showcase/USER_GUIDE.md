# Fdp.Examples.Showcase - User's Guide

## Overview

The **Fdp.Examples.Showcase** is an interactive console application that demonstrates the core features of the Fast Data Plane (FDP) entity-component system (ECS) through a simple military simulation. It showcases:

- **Entity-Component System (ECS)**: Entities with Position, Velocity, RenderSymbol, and UnitStats components
- **Flight Recorder**: Real-time recording and playback of simulation state
- **Modular Architecture**: Physics, Combat, and Render modules
- **Query System**: Efficient filtering and iteration over entities
- **Real-time Visualization**: Live ASCII battlefield display using Spectre.Console

## What It Demonstrates

The application simulates a battlefield with three types of military units:
- **Infantry** (`i`) - White colored units
- **Tanks** (`T`) - Yellow colored units  
- **Aircraft** (`^`) - Cyan colored units

Each unit moves across the battlefield with bounce physics at the boundaries. Units automatically engage enemies in range by firing projectiles. The simulation demonstrates:

1. **Component-based design**: Each entity has Position, Velocity, RenderSymbol, and UnitStats components
2. **System execution**: Multiple systems update entity state each frame:
   - MovementSystem - Updates positions based on velocity
   - PatrolSystem - Boundary bounce physics
   - CollisionSystem - Detects and resolves entity collisions  
   - CombatSystem - Spawns projectiles when enemies are in range
   - ProjectileSystem - Moves projectiles and detects hits
   - HitFlashSystem - Visual damage feedback
   - ParticleSystem - Explosion effects on death
3. **Event Bus**: Systems communicate via events (CollisionEvent, ProjectileFiredEvent, DamageEvent, DeathEvent, etc.)
4. **Flight Recorder**: Frame-by-frame recording with full rewind/replay capability and seeking
5. **Live rendering**: Real-time ASCII visualization with colored entities, projectiles, and effects
6. **Interactive controls**: Spawn/remove entities, pause, seek through recording

## How to Run

### Prerequisites
- .NET 6.0 or later
- Windows, Linux, or macOS with terminal support
- Terminal with ANSI color support (for best experience)

### Building and Running

From the `Examples\Fdp.Examples.Showcase` directory:

```bash
dotnet build
dotnet run
```

Or from the solution root:

```bash
dotnet run --project Examples\Fdp.Examples.Showcase\Fdp.Examples.Showcase.csproj
```

## Interactive Controls

Once the application is running, you can interact with it using the following keyboard commands:

| Key | Action | Description |
|-----|--------|-------------|
| **ESC** | Quit | Exits the application |
| **SPACE** | Pause/Unpause | Pauses or resumes the simulation |
| **R** | Toggle Recording | Pauses/resumes flight recorder recording |
| **P** | Toggle Replay Mode | Enters/exits replay mode (rewind) |
| **←** | Seek Backward 1 Frame | Steps back one frame (replay mode only) |
| **SHIFT + ←** | Seek Backward 10 Frames | Jumps back 10 frames (replay mode only) |
| **CTRL + ←** | Seek Backward 100 Frames | Jumps back 100 frames (replay mode only) |
| **→** | Seek Forward 1 Frame | Steps forward one frame (replay mode only) |
| **SHIFT + →** | Seek Forward 10 Frames | Jumps forward 10 frames (replay mode only) |
| **CTRL + →** | Seek Forward 100 Frames | Jumps forward 100 frames (replay mode only) |
| **HOME** | Jump to Start | Goes to the first recorded frame (replay mode only) |
| **END** | Jump to Latest | Goes to the latest recorded frame (replay mode only) |
| **1** | Spawn Infantry | Adds a new infantry unit at random position |
| **2** | Spawn Tank | Adds a new tank at random position |
| **3** | Spawn Aircraft | Adds a new aircraft at random position |
| **DELETE / BACKSPACE** | Remove Random Unit | Destroys a random entity |

## User Interface

The application displays a split-screen interface:

### Left Panel: Battlefield View
- A 60x20 ASCII grid representing the battlefield
- Units are displayed as characters:
  - `i` = Infantry
  - `T` = Tank
  - `^` = Aircraft
- Background filled with `.` (empty space)

### Right Panel: Statistics Table
Displays real-time simulation metrics:
- **Time**: Total simulation time in seconds
- **Frame**: Current frame count
- **Mode**: LIVE (simulation running) or REPLAY (viewing recording)
- **Recording**: Recording status (ON/OFF)
- **Paused**: Whether simulation is paused
- **Entities**: Current number of entities in the simulation
- **Rec Frames**: Total number of recorded frames
- **Replay Frame**: Current frame position (only shown in replay mode)
- **Buffer Size**: Size of the flight recorder buffer in KB

### Controls Panel
Shows keyboard shortcuts for quick reference during gameplay.

## Combat Mechanics

### Projectile-Based Combat
Units automatically fire projectiles at enemies within a 15-unit detection range:
- Projectiles are spawned with velocity aimed at the target
- Colored bullets (`*`) match the firing unit's color
- Projectiles travel at 15 units/second
- Maximum lifetime of 3 seconds before auto-destruction
- 1.5-unit hit radius for collision detection
- 1.5 second cooldown between shots

### Rock-Paper-Scissors Damage System
Different unit types have advantages and disadvantages:
- **Tanks vs Infantry**: 25 damage (strong)
- **Infantry vs Aircraft**: 15 damage (effective)
- **Aircraft vs Tanks**: 30 damage (very strong)
- **Tanks vs Aircraft**: 5 damage (weak)
- **Aircraft vs Infantry**: 20 damage (strong)
- **Infantry vs Tanks**: 5 damage (weak)

All units start with 100 health.

### Visual Effects
- **Hit Flash**: Units flash red for 0.3 seconds when damaged
- **Death Indicator**: Dead units turn dark gray and display 'x'
- **Explosion Particles**: 8-12 particles scatter outward on death
  - Particles have random velocities (4-12 units/sec)
  - Colored based on unit type
  - Fade to dark gray over 0.5-1.0 seconds
  - Various particle symbols: `.`, `*`, `+`, `o`

### Collision Physics
- Entities bounce off each other when within 2 units
- Simple elastic collision - velocities are swapped
- Generates CollisionEvent for each collision


## Architecture Highlights

### Components
Located in `Components.cs`:
- **Position**: X, Y coordinates
- **Velocity**: X, Y velocity vectors
- **RenderSymbol**: Display character and color
- **UnitStats**: Unit type, health, and max health

### Systems
Located in `Systems.cs`:
- **MovementSystem**: Updates entity positions based on velocity and delta time
- **PatrolSystem**: Implements bounce physics at battlefield boundaries
- **TimeSystem**: Manages GlobalTime singleton (frame count, delta time, time scale)

### Modules
Located in `Modules.cs`:
- **PhysicsModule**: Registers Position and Velocity components
- **CombatModule**: Registers UnitStats component
- **RenderModule**: Registers RenderSymbol component

### Flight Recorder
The application continuously records simulation state:
- Uses **in-memory stream** to store keyframe data
- Records every frame when recording is enabled
- Supports snapshot replay (press `P` to reset to first frame)
- Buffer size grows as simulation runs (displayed in stats)

## Technical Notes

### Headless Mode
The application detects if the console output is redirected and automatically switches to headless mode (no UI, simulation only). This is useful for:
- Automated testing
- Performance benchmarking
- Running in environments without terminal support

### Frame Rate
The simulation runs at approximately **30 FPS** (33ms per frame), with proper delta-time handling for consistent physics regardless of actual frame rate.

### Error Handling
- Gracefully handles terminal resize and I/O errors
- Falls back to headless mode if Spectre.Console features are unavailable
- Displays critical errors before exiting

## Extending the Example

To add new features:

1. **Add new components**: Define structs in `Components.cs`
2. **Register components**: Create or modify modules in `Modules.cs`
3. **Create systems**: Add new system classes in `Systems.cs` that inherit from `ComponentSystem`
4. **Initialize in Program.cs**: Instantiate your systems in the `Initialize()` method
5. **Update in loop**: Call your system's `Run()` method in `UpdateLoop()`

## Troubleshooting

### No visual output
- Ensure your terminal supports ANSI escape codes
- Try running in a different terminal (Windows Terminal, iTerm2, etc.)
- Check if output is being redirected (pipe, file, etc.)

### Performance issues
- The application is designed to run efficiently even with many entities
- If experiencing lag, check CPU usage and reduce console window size
- Recording increases memory usage over time (monitor Buffer Size)

### Units not visible
- Units may be overlapping at the same position
- Press `P` to reset to initial state
- Check that units haven't wandered outside the visible 60x20 grid

## Learning Objectives

This example is designed to help you learn:
1. How to set up an FDP Entity Repository
2. How to register components using modules
3. How to create and use EntityQuery for efficient entity filtering
4. How to implement ComponentSystem-based game logic
5. How to integrate Flight Recorder for state recording and replay
6. How to manage singletons (GlobalTime) in FDP
7. How to structure a real-time simulation application

## Next Steps

After exploring this showcase:
- Review the source code to understand implementation details
- Experiment with adding new unit types or behaviors
- Try implementing additional systems (e.g., combat, AI)
- Explore the Flight Recorder API for more advanced playback features
- Check out other FDP examples and documentation

---

**Happy Simulating!** 🚀
