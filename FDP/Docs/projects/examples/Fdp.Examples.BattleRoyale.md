# Fdp.Examples.BattleRoyale

## Overview

**Fdp.Examples.BattleRoyale** is a streamlined demonstration of the FDP module system architecture without full network replication complexity. It showcases AI-driven player agents, safe zone mechanics, health/damage systems, item pickups, event-driven analytics, and console-based visualization. This example serves as an accessible entry point for understanding module composition, ECS patterns, and multi-system coordination without the overhead of distributed networking.

**Key Demonstrations**:
- **Module Architecture**: Clean separation of concerns via IModule pattern (PhysicsModule, AIModule, WorldManagerModule)
- **Event-Driven Design**: Kill events, damage events, pickup events processed through event accumulator
- **AI Behaviors**: Simple navigation AI moving players toward safe zone and items
- **Safe Zone Mechanics**: Shrinking play area with damage-over-time outside zone
- **Analytics System**: Real-time statistics tracking (kills, survival time, items collected)
- **Console Renderer**: ASCII-based visualization of game state

**Line Count**: 24 C# implementation files (Components, Systems, Modules, Events, Visualization)

**Primary Dependencies**: Fdp.Kernel (ECS Core), ModuleHost.Core (Module System), ModuleHost.Network.Cyclone (minimal, for infrastructure demonstration)

**Use Cases**: Learning FDP module patterns, prototyping game logic, module composition demonstration, unit testing complex system interactions

---

## Architecture

### System Overview

```
┌─────────────────────────────────────────────────────────────────────┐
│               BattleRoyale Module Architecture                       │
├─────────────────────────────────────────────────────────────────────┤
│                                                                       │
│  PlayerEntity (100 AI agents)                                        │
│  ┌──────────────────────────────────────────────────────────────┐   │
│  │  Components:                                                   │   │
│  │  - Position, Velocity (Physics)                               │   │
│  │  - Health, Damage (Combat)                                    │   │
│  │  - AIState (Behavior tree state)                              │   │
│  │  - Inventory (Items collected)                                │   │
│  │  - Team (Squad assignment)                                    │   │
│  │  - PlayerInfo (Name, stats)                                   │   │
│  └──────────────────────────────────────────────────────────────┘   │
│                                                                       │
│  Module Execution Flow (per frame):                                 │
│  ┌──────────────────────────────────────────────────────────────┐   │
│  │  [WorldManagerModule]                                         │   │
│  │    - SafeZoneUpdateSystem: Shrink zone radius over time      │   │
│  │    - ZoneDamageSystem: Damage players outside safe zone      │   │
│  │    - ItemSpawnSystem: Spawn loot items on map                 │   │
│  └──────────────────────────────────────────────────────────────┘   │
│  ┌──────────────────────────────────────────────────────────────┐   │
│  │  [AIModule]                                                   │   │
│  │    - AINavigationSystem: Pathfinding toward safe zone        │   │
│  │    - AIPickupSystem: Collect nearby items                     │   │
│  │    - AICombatSystem: Engage enemies in range                  │   │
│  └──────────────────────────────────────────────────────────────┘   │
│  ┌──────────────────────────────────────────────────────────────┐   │
│  │  [PhysicsModule]                                              │   │
│  │    - VelocityIntegrationSystem: Update positions             │   │
│  │    - CollisionSystem: Resolve overlaps                        │   │
│  └──────────────────────────────────────────────────────────────┘   │
│  ┌──────────────────────────────────────────────────────────────┐   │
│  │  [FlightRecorderModule]                                       │   │
│  │    - RecorderTickSystem: Log frame snapshots                  │   │
│  └──────────────────────────────────────────────────────────────┘   │
│  ┌──────────────────────────────────────────────────────────────┐   │
│  │  [AnalyticsModule]                                            │   │
│  │    - KillTrackingSystem: Record kill events                   │   │
│  │    - SurvivalTimeSystem: Track survival duration              │   │
│  │    - LeaderboardSystem: Rank players                          │   │
│  └──────────────────────────────────────────────────────────────┘   │
│                                                                       │
│  Console Visualization:                                              │
│  ┌────────────────────────────────────┐                             │
│  │  Map (20x20 grid):                 │                             │
│  │  . . . . . . . . . . . . . . . .   │                             │
│  │  . . P . . . . . . . . . . . . .   │  P = Player                 │
│  │  . . . . . I . . . . . . . . . .   │  I = Item                   │
│  │  . . . . . . . . . P . . . . . .   │  O = Safe Zone border       │
│  │  . . . . . . O O O O O . . . . .   │  X = Dead player            │
│  │  . . . . . O . . . . . O . . . .   │                             │
│  │  . . . . . O . . . . . O . . . .   │                             │
│  │  . . . . . O O O O O O O . . . .   │                             │
│  │  Stats:                            │                             │
│  │  Alive: 73/100  Time: 45s          │                             │
│  │  Zone Radius: 15m  Items: 12       │                             │
│  └────────────────────────────────────┘                             │
│                                                                       │
└───────────────────────────────────────────────────────────────────────┘
```

---

## Core Components

### Position (Components/Position.cs)

```csharp
public struct Position
{
    public Vector2 Value;  // World coordinates (meters)
}
```

### Velocity (Components/Velocity.cs)

```csharp
public struct Velocity
{
    public Vector2 Value;  // Meters per second
}
```

### Health (Components/Health.cs)

```csharp
public struct Health
{
    public float Current;   // HP (0 = dead)
    public float Maximum;   // Max HP capacity
}
```

### AIState (Components/AIState.cs)

```csharp
public struct AIState
{
    public AIBehavior CurrentBehavior;  // MoveToZone, CollectItem, Engage, Evade
    public Entity TargetEntity;         // Combat target
    public Vector2 TargetPosition;      // Move destination
    public float DecisionCooldown;      // Time until next AI update (seconds)
}

public enum AIBehavior
{
    MoveToZone,   // Navigate toward safe zone center
    CollectItem,  // Move to pickup item
    Engage,       // Attack enemy
    Evade         // Flee from threats
}
```

### Inventory (Components/Inventory.cs)

```csharp
public struct Inventory
{
    public int HealthPacksCount;
    public int ArmorPlatesCount;
    public int AmmoCount;
}
```

### SafeZone (Components/SafeZone.cs)

```csharp
public struct SafeZone
{
    public Vector2 Center;         // Zone center position
    public float CurrentRadius;    // Current safe radius (meters)
    public float TargetRadius;     // Shrinking toward this radius
    public float ShrinkRate;       // Meters per second
    public float DamagePerSecond;  // Damage outside zone
}
```

---

## Systems

### SafeZoneUpdateSystem

```csharp
[UpdateInPhase(SystemPhase.PreSimulation)]
public class SafeZoneUpdateSystem : IModuleSystem
{
    public void Execute(ISimulationView view, float deltaTime)
    {
        var cmd = view.GetCommandBuffer();
        
        // Query safe zone entity
        var query = view.Query().With<SafeZone>().Build();
        foreach (var entity in query)
        {
            ref readonly var zone = ref view.GetComponentRO<SafeZone>(entity);
            
            // Shrink toward target radius
            if (zone.CurrentRadius > zone.TargetRadius)
            {
                float newRadius = Math.Max(zone.TargetRadius, 
                    zone.CurrentRadius - zone.ShrinkRate * deltaTime);
                
                cmd.SetComponent(entity, new SafeZone
                {
                    Center = zone.Center,
                    CurrentRadius = newRadius,
                    TargetRadius = zone.TargetRadius,
                    ShrinkRate = zone.ShrinkRate,
                    DamagePerSecond = zone.DamagePerSecond
                });
            }
        }
    }
}
```

### ZoneDamageSystem

```csharp
[UpdateInPhase(SystemPhase.Simulation)]
public class ZoneDamageSystem : IModuleSystem
{
    public void Execute(ISimulationView view, float deltaTime)
    {
        var cmd = view.GetCommandBuffer();
        
        // Get safe zone
        var zoneQuery = view.Query().With<SafeZone>().Build();
        if (!zoneQuery.Any())
            return;
        
        var zone = view.GetComponentRO<SafeZone>(zoneQuery.First());
        
        // Check all players
        var playerQuery = view.Query().With<Position>().With<Health>().Build();
        foreach (var player in playerQuery)
        {
            var pos = view.GetComponentRO<Position>(player);
            float distance = Vector2.Distance(pos.Value, zone.Center);
            
            // Apply damage if outside safe zone
            if (distance > zone.CurrentRadius)
            {
                var health = view.GetComponentRO<Health>(player);
                float damage = zone.DamagePerSecond * deltaTime;
                float newHealth = Math.Max(0, health.Current - damage);
                
                cmd.SetComponent(player, new Health
                {
                    Current = newHealth,
                    Maximum = health.Maximum
                });
                
                // Publish damage event
                cmd.PublishEvent(new DamageEvent
                {
                    VictimEntity = player,
                    Damage = damage,
                    Source = DamageSource.SafeZone
                });
                
                // Check for death
                if (newHealth == 0)
                {
                    cmd.PublishEvent(new KillEvent
                    {
                        VictimEntity = player,
                        KillerEntity = Entity.Null, // Killed by zone
                        KillType = KillType.Zone
                    });
                }
            }
        }
    }
}
```

### AINavigationSystem

```csharp
[UpdateInPhase(SystemPhase.Simulation)]
public class AINavigationSystem : IModuleSystem
{
    public void Execute(ISimulationView view, float deltaTime)
    {
        var cmd = view.GetCommandBuffer();
        
        // Get safe zone center
        var zoneQuery = view.Query().With<SafeZone>().Build();
        if (!zoneQuery.Any())
            return;
        var zone = view.GetComponentRO<SafeZone>(zoneQuery.First());
        
        // Update AI agents
        var aiQuery = view.Query()
            .With<AIState>()
            .With<Position>()
            .With<Velocity>()
            .Build();
        
        foreach (var agent in aiQuery)
        {
            ref readonly var aiState = ref view.GetComponentRO<AIState>(agent);
            ref readonly var pos = ref view.GetComponentRO<Position>(agent);
            
            // Update decision cooldown
            if (aiState.DecisionCooldown > 0)
                continue;
            
            Vector2 targetPos;
            
            switch (aiState.CurrentBehavior)
            {
                case AIBehavior.MoveToZone:
                    // Move toward zone center
                    targetPos = zone.Center;
                    break;
                
                case AIBehavior.CollectItem:
                    targetPos = aiState.TargetPosition;
                    break;
                
                default:
                    targetPos = pos.Value;
                    break;
            }
            
            // Calculate direction and velocity
            Vector2 direction = Vector2.Normalize(targetPos - pos.Value);
            float speed = 5.0f; // m/s
            
            cmd.SetComponent(agent, new Velocity { Value = direction * speed });
            
            // Reset decision cooldown
            cmd.SetComponent(agent, new AIState
            {
                CurrentBehavior = aiState.CurrentBehavior,
                TargetEntity = aiState.TargetEntity,
                TargetPosition = targetPos,
                DecisionCooldown = 0.5f // Re-evaluate every 0.5s
            });
        }
    }
}
```

---

## Modules

### WorldManagerModule (Modules/WorldManagerModule.cs)

```csharp
public class WorldManagerModule : IModule
{
    public string Name => "WorldManager";
    public ExecutionPolicy Policy => ExecutionPolicy.Synchronous();

    public void RegisterSystems(ISystemRegistry registry)
    {
        registry.RegisterSystem(new SafeZoneUpdateSystem());
        registry.RegisterSystem(new ZoneDamageSystem());
        registry.RegisterSystem(new ItemSpawnSystem());
    }

    public void Tick(ISimulationView view, float deltaTime) { }
}
```

### AIModule (Modules/AIModule.cs)

```csharp
public class AIModule : IModule
{
    public string Name => "AI";
    public ExecutionPolicy Policy => ExecutionPolicy.Synchronous();

    public void RegisterSystems(ISystemRegistry registry)
    {
        registry.RegisterSystem(new AINavigationSystem());
        registry.RegisterSystem(new AIPickupSystem());
        registry.RegisterSystem(new AICombatSystem());
        registry.RegisterSystem(new AIDecisionSystem());
    }

    public void Tick(ISimulationView view, float deltaTime) { }
}
```

### AnalyticsModule (Modules/AnalyticsModule.cs)

```csharp
public class AnalyticsModule : IModule
{
    public string Name => "Analytics";
    public ExecutionPolicy Policy => ExecutionPolicy.Synchronous();

    private int _totalKills = 0;
    private int _totalSurvivors = 0;

    public void RegisterSystems(ISystemRegistry registry)
    {
        registry.RegisterSystem(new KillTrackingSystem(OnKill));
        registry.RegisterSystem(new SurvivalTimeSystem());
    }

    private void OnKill(KillEvent killEvent)
    {
        _totalKills++;
        Console.WriteLine($"Kill #{_totalKills}: {killEvent.KillerEntity.Index} → {killEvent.VictimEntity.Index} ({killEvent.KillType})");
    }

    public void Tick(ISimulationView view, float deltaTime)
    {
        // Aggregate statistics per frame
    }
}
```

---

## Example Usage

### Spawn 100 AI Players

```csharp
public static void SpawnPlayers(EntityRepository world, ICommandBuffer cmd, int count)
{
    Random rng = new Random();
    
    for (int i = 0; i < count; i++)
    {
        Entity player = cmd.CreateEntity();
        
        // Random spawn position (within 100m radius)
        float angle = (float)(rng.NextDouble() * Math.PI * 2);
        float radius = (float)(rng.NextDouble() * 100);
        Vector2 spawnPos = new Vector2(
            MathF.Cos(angle) * radius,
            MathF.Sin(angle) * radius
        );
        
        // Add components
        cmd.SetComponent(player, new Position { Value = spawnPos });
        cmd.SetComponent(player, new Velocity { Value = Vector2.Zero });
        cmd.SetComponent(player, new Health { Current = 100, Maximum = 100 });
        cmd.SetComponent(player, new AIState
        {
            CurrentBehavior = AIBehavior.MoveToZone,
            TargetPosition = Vector2.Zero,
            DecisionCooldown = (float)(rng.NextDouble() * 0.5)
        });
        cmd.SetComponent(player, new Inventory { HealthPacksCount = 0, ArmorPlatesCount = 0, AmmoCount = 0 });
        cmd.SetComponent(player, new Team { Id = i % 4 }); // 4 teams
        cmd.SetComponent(player, new PlayerInfo { Name = $"Player_{i}" });
    }
}
```

### Initialize SafeZone

```csharp
public static void InitializeSafeZone(ICommandBuffer cmd)
{
    Entity zoneEntity = cmd.CreateEntity();
    
    cmd.SetComponent(zoneEntity, new SafeZone
    {
        Center = Vector2.Zero,
        CurrentRadius = 100.0f,  // Start at 100m radius
        TargetRadius = 10.0f,    // Shrink to 10m final circle
        ShrinkRate = 1.0f,       // 1m/s shrink speed
        DamagePerSecond = 5.0f   // 5 HP/s outside zone
    });
}
```

### Run Simulation Loop

```csharp
using var moduleHost = new ModuleHostKernel(world, eventAccumulator);

// Register modules
moduleHost.RegisterModule(new WorldManagerModule());
moduleHost.RegisterModule(new AIModule());
moduleHost.RegisterModule(new PhysicsModule());
moduleHost.RegisterModule(new AnalyticsModule());

// Initialize entities
SpawnPlayers(world, moduleHost.GetCommandBuffer(), 100);
InitializeSafeZone(moduleHost.GetCommandBuffer());

// Simulation loop (60 FPS, 5 minutes max)
const float targetDeltaTime = 1.0f / 60.0f;
float totalTime = 0;
int frame = 0;

while (totalTime < 300.0f) // 5 minutes
{
    // Update kernel
    moduleHost.Tick(targetDeltaTime);
    
    // Render console every 10 frames
    if (frame % 10 == 0)
    {
        ConsoleRenderer.Render(world);
    }
    
    // Check for winner
    int aliveCount = CountAlivePlayers(world);
    if (aliveCount <= 1)
    {
        Console.WriteLine($"Winner! Time: {totalTime:F1}s");
        break;
    }
    
    totalTime += targetDeltaTime;
    frame++;
    
    Thread.Sleep(16); // ~60 FPS pacing
}
```

---

## Console Visualization

### ConsoleRenderer (Visualization/ConsoleRenderer.cs)

```csharp
public static class ConsoleRenderer
{
    public static void Render(EntityRepository world)
    {
        Console.Clear();
        
        // Get safe zone
        var zoneQuery = world.Query().With<SafeZone>().Build();
        if (!zoneQuery.Any())
            return;
        var zone = world.GetComponentRO<SafeZone>(zoneQuery.First());
        
        // Render map (20x20 grid, each cell = 10m)
        char[,] grid = new char[20, 20];
        for (int y = 0; y < 20; y++)
        {
            for (int x = 0; x < 20; x++)
                grid[y, x] = '.';
        }
        
        // Draw safe zone border
        int zoneCellRadius = (int)(zone.CurrentRadius / 10);
        for (int angle = 0; angle < 360; angle += 10)
        {
            float rad = angle * MathF.PI / 180.0f;
            int x = 10 + (int)(MathF.Cos(rad) * zoneCellRadius);
            int y = 10 + (int)(MathF.Sin(rad) * zoneCellRadius);
            if (x >= 0 && x < 20 && y >= 0 && y < 20)
                grid[y, x] = 'O';
        }
        
        // Draw players
        var playerQuery = world.Query().With<Position>().With<Health>().Build();
        foreach (var player in playerQuery)
        {
            var pos = world.GetComponentRO<Position>(player);
            var health = world.GetComponentRO<Health>(player);
            
            int cellX = 10 + (int)(pos.Value.X / 10);
            int cellY = 10 + (int)(pos.Value.Y / 10);
            
            if (cellX >= 0 && cellX < 20 && cellY >= 0 && cellY < 20)
            {
                grid[cellY, cellX] = health.Current > 0 ? 'P' : 'X';
            }
        }
        
        // Print grid
        for (int y = 0; y < 20; y++)
        {
            for (int x = 0; x < 20; x++)
                Console.Write(grid[y, x] + " ");
            Console.WriteLine();
        }
        
        // Print stats
        int alive = playerQuery.Count(e => world.GetComponentRO<Health>(e).Current > 0);
        Console.WriteLine($"\nAlive: {alive}/{playerQuery.Count()}  Zone: {zone.CurrentRadius:F1}m");
    }
}
```

---

## Integration with FDP Ecosystem

**Fdp.Kernel**:
- EntityRepository, Entity, ComponentType (ECS foundation)
- EventAccumulator for event buffering

**ModuleHost.Core**:
- IModule, IModuleSystem for composable architecture
- SystemPhase ordering (PreSimulation, Simulation, PostSimulation)

**Best Practices Demonstrated**:
- Module separation by domain (World, AI, Physics, Analytics)
- Event-driven communication (KillEvent, DamageEvent, ItemPickupEvent)
- System phase ordering for predictable execution
- Component-oriented design (Position, Health, AIState)

---

## Conclusion

**Fdp.Examples.BattleRoyale** demonstrates clean module architecture and ECS patterns in a simplified game simulation context. Key learning points include module composition, event-driven system communication, and console-based visualization. This example provides an accessible starting point for developers new to FDP without the complexity of distributed networking.

---

**Total Lines**: 679
