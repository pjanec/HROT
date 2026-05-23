# Fdp.Examples.Showcase

| Field | Value |
|---|---|
| **Project path** | `FDP/Examples/Fdp.Examples.Showcase/Fdp.Examples.Showcase.csproj` |
| **Output type** | Executable (`<OutputType>Exe</OutputType>`) |
| **Target framework** | net8.0 |
| **Window size** | 1920 x 1080 @ 144 FPS target |
| **Date documented** | 2026-05-23 |

## README Validation

**Up-to-date** — A `USER_GUIDE.md` exists in the project folder and describes the
application.

---

## Executive Overview

`Fdp.Examples.Showcase` is a **real-time interactive demonstration application** built on
Raylib + ImGui that showcases the core FDP ECS (Entity Component System) kernel with a
military simulation theme. It is the only example in the FDP suite that uses Raylib for
rendering, making it the primary visual showcase for human observers.

The application renders a live 2D battlefield where:

- Three unit types (Tanks, Aircraft, Infantry) patrol and engage each other.
- Projectiles are fired, detected via a spatial map, and explode on impact.
- Particle effects spawn on unit death.
- The user can spawn and remove units interactively at runtime.
- A Raylib `R`-key recording and `P`-key playback cycle demonstrates the
  `FlightRecorder` system.

The ImGui overlay provides:
- **Entity Inspector** — per-entity component browser.
- **Event Inspector** — live event stream monitor.
- **Performance panel** — per-system execution time breakdown.
- **Controls panel** — keyboard shortcuts reference.

### Key learning objectives

1. **Raylib + rlImGui integration** with FDP's ECS — how to set up the window, game loop,
   and Dear ImGui bridge.
2. **Parallel ECS iteration** — `ForEachParallel` in `MovementSystem`, `HitFlashSystem`,
   and `PatrolSystem`.
3. **Spatial map** for O(1) proximity queries replacing O(n^2) collision detection.
4. **EntityCommandBuffer** as the "end-of-frame barrier" — `LifecycleSystem` owns the
   shared ECB; all other systems enqueue structural mutations but none apply them directly.
5. **Recording and playback** — `R` to record, `P` to play back in frame-step mode
   (`Left`/`Right` arrows for per-frame seeking).
6. **Soft delete via `Corpse` component** — instead of immediate destruction, killed units
   receive a `Corpse` component and are removed by `LifecycleSystem` after a timer.

---

## Architecture

### Application Structure

```
+---------------------------------------------------------------+
|  Program.Main                                                 |
|    Raylib.InitWindow(1920, 1080, "FDP Military Showcase")     |
|    rlImGui.Setup(true)                                        |
|    ShowcaseGame.Initialize()                                  |
|    ShowcaseGame.RunRaylibLoop()                               |
|    ShowcaseGame.Cleanup()                                     |
+---------------------------------------------------------------+
```

### ShowcaseGame Core Loop

```
+------------------------------------------------------------+
|  ShowcaseGame                                             |
|                                                            |
|  Initialize():                                             |
|    new EntityRepository (world)                            |
|    Register modules: Physics, Combat, Render               |
|    Register singletons: GlobalTime, GlobalState            |
|    Build system pipeline:                                  |
|      SpatialSystem -> PatrolSystem -> MovementSystem       |
|      -> CombatSystem -> ProjectileSystem                   |
|      -> CollisionSystem -> HitFlashSystem                  |
|      -> ParticleSystem -> LifecycleSystem                  |
|    SpawnInitialUnits()                                      |
|                                                            |
|  RunRaylibLoop():                                          |
|    while !WindowShouldClose:                               |
|      input.Process(world)                                  |
|      if !paused: UpdateSystems(dt)                         |
|      BeginDrawing()                                        |
|        renderer.Draw(world)                                |
|        ui.DrawImGui(world)                                 |
|      EndDrawing()                                          |
+------------------------------------------------------------+
```

### System Execution Order

```
+------------------+
| SpatialSystem    |  Rebuilds SpatialMap from all Position components
+------------------+
         |
+------------------+
| PatrolSystem     |  Bounce-at-boundary patrol velocity reversal
+------------------+
         |
+------------------+
| MovementSystem   |  pos += vel * dt  (ForEachParallel)
+------------------+
         |
+------------------+
| CombatSystem     |  Fire projectile at nearest enemy in range
+------------------+
         |
+------------------+
| ProjectileSystem |  Advance projectile, detect hit via SpatialMap
+------------------+
         |
+------------------+
| CollisionSystem  |  Elastic collision response
+------------------+
         |
+------------------+
| HitFlashSystem   |  Countdown flash timers (ForEachParallel)
+------------------+
         |
+------------------+
| ParticleSystem   |  Process death events, spawn explosions
+------------------+
         |
+------------------+
| LifecycleSystem  |  Apply ECB: destroy expired corpses + pending entities
+------------------+  <- ONLY system that calls CommandBuffer.Playback()
```

### Component Ownership Diagram

```
+-------------------------------------------+
|  Entity: Tank / Aircraft / Infantry        |
|  +--------+  +----------+  +-----------+  |
|  |Position|  |Velocity  |  |UnitStats  |  |
|  +--------+  +----------+  +-----------+  |
|  +------------+  +----------+             |
|  |RenderSymbol|  |CombatState|            |
|  +------------+  +----------+             |
|  (on damage)  +-----------+               |
|               |  HitFlash |               |
|               +-----------+               |
|  (on death)   +--------+                  |
|               | Corpse |                  |
|               +--------+                  |
+-------------------------------------------+

+-------------------------------------------+
|  Entity: Projectile                        |
|  +--------+  +----------+  +----------+  |
|  |Position|  |Velocity  |  |Projectile|  |
|  +--------+  +----------+  +----------+  |
|  +------------+                           |
|  |RenderSymbol|                           |
|  +------------+                           |
+-------------------------------------------+

+-------------------------------------------+
|  Entity: Particle (explosion fragment)     |
|  +--------+  +----------+  +----------+  |
|  |Position|  |Velocity  |  |Particle  |  |
|  +--------+  +----------+  +----------+  |
|  +------------+                           |
|  |RenderSymbol|                           |
|  +------------+                           |
+-------------------------------------------+
```

---

## Source Structure

```
FDP/Examples/Fdp.Examples.Showcase/
+-- Fdp.Examples.Showcase.csproj
+-- Program.cs                              namespace Fdp.Examples.Showcase
+-- Modules.cs                              namespace Fdp.Examples.Showcase.Modules
|     interface IModule
|     class PhysicsModule : IModule
|     class CombatModule  : IModule
|     class RenderModule  : IModule
+-- USER_GUIDE.md
+-- Components/
|     CombatState.cs       struct CombatState
|     Corpse.cs            struct Corpse
|     Events.cs            struct CollisionEvent, ProjectileFiredEvent, DeathEvent
|     HealthComponent.cs   struct HealthComponent
|     HitFlash.cs          struct HitFlash
|     ManagedEvents.cs     (managed event type declarations)
|     Particle.cs          struct Particle
|     Position.cs          struct Position
|     Projectile.cs        struct Projectile
|     RenderSymbol.cs      struct RenderSymbol + enum EntityShape
|     UnitStats.cs         struct UnitStats
|     UnitType.cs          enum UnitType { Tank, Aircraft, Infantry }
|     Velocity.cs          struct Velocity
+-- Core/
|     KeyAutoRepeat.cs     struct KeyAutoRepeat
|     KeyInputManager.cs   class KeyInputManager
|     ShowcaseGame.cs      class ShowcaseGame
|     ShowcaseInput.cs     class ShowcaseInput
|     ShowcaseRenderer.cs  class ShowcaseRenderer
|     ShowcaseUI.cs        class ShowcaseUI
+-- Systems/
      CollisionSystem.cs   class CollisionSystem : ComponentSystem
      CombatSystem.cs      class CombatSystem : ComponentSystem
      HitFlashSystem.cs    class HitFlashSystem : ComponentSystem
      LifecycleSystem.cs   class LifecycleSystem : ComponentSystem
      MovementSystem.cs    class MovementSystem : ComponentSystem
      ParticleSystem.cs    class ParticleSystem : ComponentSystem
      PatrolSystem.cs      class PatrolSystem : ComponentSystem
      ProjectileSystem.cs  class ProjectileSystem : ComponentSystem
      SpatialMap.cs        class SpatialMap
      SpatialSystem.cs     class SpatialSystem : ComponentSystem
```

---

## Public API Reference

### Components

#### `Position`
```csharp
public struct Position { public float X; public float Y; }
```

#### `Velocity`
```csharp
public struct Velocity { public float X; public float Y; }
```

#### `UnitStats`
```csharp
public struct UnitStats
{
    public float Health;
    public float MaxHealth;
    public UnitType Type;
}
```

#### `UnitType`
```csharp
public enum UnitType { Tank, Aircraft, Infantry }
```

#### `RenderSymbol`
```csharp
public struct RenderSymbol
{
    public EntityShape Shape;
    public byte R, G, B;
    public float Size;
}

public enum EntityShape { Circle, Square, Triangle, Cross }
```

#### `Projectile`
```csharp
public struct Projectile
{
    public Entity Owner;
    public float  Damage;
    public float  Speed;
    public float  Lifetime;
}
```

#### `Particle`
```csharp
public struct Particle
{
    public float LifeRemaining;
    public float MaxLife;
    public byte R, G, B;
    public float Size;
}
```

#### `HitFlash`
```csharp
public struct HitFlash { public float Remaining; }
```

#### `Corpse`
```csharp
public struct Corpse { public float TimeRemaining; }
```

#### `CombatState`
```csharp
public struct CombatState { /* combat history flags */ }
```

#### Events

```csharp
public struct CollisionEvent    { public Entity EntityA, EntityB; public float ImpactForce; }
public struct ProjectileFiredEvent { public Entity Shooter, Projectile; public UnitType ShooterType; }
public struct DeathEvent        { public Entity Entity; public UnitType Type; }
```

### Modules

#### `PhysicsModule`

Registers `Position`, `Velocity`, `Projectile`, `Particle` with the `EntityRepository`.

#### `CombatModule`

Registers `UnitStats`, `CombatHistory` (with `DataPolicy.Default` for snapshot support),
`CombatState`, `Corpse`.

#### `RenderModule`

Registers `RenderSymbol`, `HitFlash`.

### Systems

#### `SpatialSystem`

Rebuilds `SpatialMap` each tick from all entities with `Position`. Other systems access
the map via the `_spatial` reference injected at construction.

#### `MovementSystem`

```csharp
_query.ForEachParallel(entity =>
{
    ref var pos = ref World.GetComponentRW<Position>(entity);
    ref var vel = ref World.GetComponentRW<Velocity>(entity);
    pos.X += vel.X * dt;
    pos.Y += vel.Y * dt;
});
```

Runs in parallel. Requires all `Position`/`Velocity` components to be already written for
the current tick before this system runs.

#### `CombatSystem`

Fires one projectile per shooter per frame toward the nearest enemy within `CombatRange
= 15f` units. Uses `SpatialMap.Query` for O(log n) candidate lookup. Rock-paper-scissors
damage model:

```
Tank   vs Infantry : 25 dmg   Aircraft vs Tank     : 30 dmg
Infantry vs Aircraft: 15 dmg   Tank    vs Aircraft  :  5 dmg
Aircraft vs Infantry: 20 dmg   Infantry vs Tank     :  5 dmg
```

#### `LifecycleSystem`

Owns the shared `EntityCommandBuffer CommandBuffer` (4096 initial capacity). Runs last.

- Counts down `Corpse.TimeRemaining` timers.
- Queues expired corpses via `CommandBuffer.DestroyEntity(entity)`.
- Calls `CommandBuffer.Playback(World)` — the **only** place structural changes are applied.

#### `CollisionSystem`

Detects overlapping entities via `SpatialMap.Query` with radius 2 m. On collision: fires
`CollisionEvent`, then swaps `Velocity` components (elastic collision).

#### `ProjectileSystem`

Ages projectiles, applies lifetime decay, detects hits via spatial proximity, applies
damage to target `UnitStats.Health`, fires `DeathEvent` if health drops to zero, and
queues the projectile for destruction via `LifecycleSystem.CommandBuffer`.

#### `ParticleSystem`

Consumes `DeathEvent` from the event bus and spawns 8-12 explosion particles at the
death position. Ages particles and queues expired ones via `LifecycleSystem.CommandBuffer`.

#### `HitFlashSystem`

Counts down `HitFlash.Remaining` timers in parallel. Removes the `HitFlash` component
when the timer expires (uses a `ConcurrentQueue` to avoid concurrent modification).

#### `PatrolSystem`

Reverses `Velocity.X` or `Velocity.Y` when an entity's `Position` exceeds the patrol
boundary (0-80 x, 0-24 y). Runs in parallel.

### Core Layer

#### `ShowcaseGame`

Central coordinator. Owns `EntityRepository`, all modules, all systems, and the
`FlightRecorder`. Provides `Initialize()`, `RunRaylibLoop()`, and `Cleanup()`.

#### `ShowcaseInput`

Processes keyboard input each frame:

| Key | Action |
|---|---|
| ESC | Quit |
| SPACE | Pause/Resume |
| R | Toggle recording |
| P | Start playback |
| I | Toggle entity inspector |
| 1 | Spawn tank |
| 2 | Spawn aircraft |
| 3 | Spawn infantry |
| DELETE | Remove random unit |
| Left/Right | Seek ±1 frame (replay mode) |
| SHIFT + Left/Right | Seek ±10 frames |
| CTRL + Left/Right | Seek ±100 frames |
| Home/End | First/Last frame (replay mode) |

#### `ShowcaseRenderer`

Draws all entities with `Position` + `RenderSymbol` using Raylib primitives (circles,
rectangles, triangles). Entity color reflects unit type; `HitFlash` temporarily overrides
color to white.

#### `ShowcaseUI`

Renders ImGui panels:
- **Entity Inspector** — lists all entities, shows component values.
- **Event Inspector** — shows the last N events from the event bus.
- **Performance** — per-system execution time table (ms + percentage of frame).
- **Controls** — keyboard shortcut reference.

---

## Dependencies

### NuGet packages

| Package | Version | Purpose |
|---|---|---|
| `Raylib-cs` | 7.0.2 | Windowing, 2D rendering, input |
| `rlImGui-cs` | 3.2.0 | Dear ImGui bridge for Raylib |

### Project references

| Project | Purpose |
|---|---|
| `Fdp.Core` | `EntityRepository`, `EntityCommandBuffer`, event bus, `FdpConfig` |

---

## Usage Examples

### Example 1 — Running the showcase

```bash
cd FDP/Examples/Fdp.Examples.Showcase
dotnet run
# Opens 1920x1080 window at 144 FPS
# Press 1/2/3 to spawn units, SPACE to pause
```

### Example 2 — Recording and replaying a session

```bash
# In the running application:
# Press R to start recording
# Watch entities fight for ~10 seconds
# Press R again to stop recording
# Press P to start playback
# Use Left/Right arrows to step through frames
# Press SHIFT+Right to jump 10 frames at a time
```

### Example 3 — Parallel component iteration

```csharp
// MovementSystem demonstrates ForEachParallel:
protected override void OnUpdate()
{
    ref var time = ref World.GetSingletonUnmanaged<GlobalTime>();
    float dt = time.DeltaTime * time.TimeScale;

    _query.ForEachParallel(entity =>
    {
        ref var pos = ref World.GetComponentRW<Position>(entity);
        ref var vel = ref World.GetComponentRW<Velocity>(entity);
        pos.X += vel.X * dt;
        pos.Y += vel.Y * dt;
    });
}
// Key rule: each entity's components are accessed independently.
// No two entities share state in this operation, making it thread-safe.
```

### Example 4 — Using the shared EntityCommandBuffer

```csharp
// ParticleSystem queues destruction through LifecycleSystem:
public class ParticleSystem : ComponentSystem
{
    private LifecycleSystem _lifecycle;

    protected override void OnUpdate()
    {
        // Particles that have expired:
        foreach (var entity in toDestroy)
        {
            // Enqueue the destruction command - DO NOT destroy immediately
            _lifecycle.CommandBuffer.DestroyEntity(entity);
        }
        // LifecycleSystem will call CommandBuffer.Playback(World) at end of frame
    }
}
```

### Example 5 — Adding a new system

```csharp
// 1. Create the system class
public class HealingSystem : ComponentSystem
{
    private EntityQuery _query = null!;

    public HealingSystem(EntityRepository repo) { Create(repo); }

    protected override void OnCreate()
    {
        _query = World.Query()
            .With<UnitStats>()
            .Without<Corpse>() // Don't heal corpses
            .Build();
    }

    protected override void OnUpdate()
    {
        ref var time = ref World.GetSingletonUnmanaged<GlobalTime>();
        float dt = time.DeltaTime;

        foreach (var entity in _query)
        {
            ref var stats = ref World.GetComponentRW<UnitStats>(entity);
            if (stats.Health < stats.MaxHealth)
                stats.Health = Math.Min(stats.MaxHealth, stats.Health + 1f * dt);
        }
    }
}

// 2. Instantiate and add to ShowcaseGame.Initialize() pipeline
// (before LifecycleSystem):
var healingSystem = new HealingSystem(world);
_systems.Add(healingSystem);
```

---

## Best Practices

### 1. Only LifecycleSystem calls CommandBuffer.Playback

All other systems that need to destroy or create entities enqueue commands to
`LifecycleSystem.CommandBuffer`. This guarantees a consistent world view throughout the
frame — no system sees newly created or destroyed entities that were mutated mid-frame.

### 2. Parallel iteration requires no shared mutable state

`ForEachParallel` is safe only when each entity's data is independent. `MovementSystem`
and `PatrolSystem` only read/write the entity's own `Position` and `Velocity`. Never use
`ForEachParallel` if systems read one entity's data to modify another's.

### 3. Soft deletion via Corpse

Immediate entity destruction mid-frame can invalidate iterators or leave dangling
references in other systems. The `Corpse` component defers destruction to `LifecycleSystem`,
ensuring all systems complete their frame iteration before the entity is removed.

### 4. SpatialMap must be rebuilt before spatial queries

`SpatialSystem` runs first in the pipeline. Any system that calls `SpatialMap.Query()`
must run after `SpatialSystem` in the pipeline order. The `_spatial` field injected into
`CombatSystem`, `CollisionSystem`, and `ProjectileSystem` references the same `SpatialSystem`
instance.

### 5. Use DataPolicy.Default for mutable class components

`CombatHistory` is a reference type. Registering it with `DataPolicy.Default` enables
snapshotting for the `FlightRecorder`. Do not use this pattern for frequently mutated
value types — the snapshot overhead is significant.

---

## Related Projects

| Project | Relationship |
|---|---|
| `Fdp.Core` | ECS kernel (`EntityRepository`, `EntityCommandBuffer`, event bus) |
| `Fdp.Examples.UrbanCombat` | Sister headless demo using the same toolkit subsystems |
| `Fdp.Toolkit.Replay` | `FlightRecorder` referenced indirectly for R/P recording feature |
| `Fdp.Examples.Scenarios` | `ParallelEpisodesScenario` demonstrates recording/replay without a GUI |
