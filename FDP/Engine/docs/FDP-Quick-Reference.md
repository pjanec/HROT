# **FDP Quick Reference Guide**

**Fast Data Plane - Developer Handbook**  
**All APIs, Patterns, and Best Practices**

---

## **Table of Contents**

1. [Core API Summary](#core-api-summary)
2. [Common Patterns](#common-patterns)
3. [Performance Guidelines](#performance-guidelines)
4. [Testing Patterns](#testing-patterns)
5. [Debugging Tips](#debugging-tips)

---

# **Core API Summary**

## **Entity Management**

```csharp
using Fdp.Kernel;

// Create repository
var repo = new EntityRepository();

// Register components (do this at startup)
repo.RegisterComponent<Position>();      // Tier 1 (unmanaged)
repo.RegisterComponent<Velocity>();      // Tier 1
repo.RegisterManagedComponent<AIBrain>(); // Tier 2 (managed)
repo.RegisterTag<IsStatic>();            // Tag (no storage)
repo.RegisterMultiPart<WheelComponent>(); // Multi-part

// Create/destroy entities
Entity e = repo.CreateEntity();
repo.DestroyEntity(e);

// Check if alive
bool alive = repo.IsAlive(e);
```

---

## **Component Access**

### **Single Components (Tier 1)**

```csharp
// Get (creates if missing)
ref var pos = ref repo.GetComponent<Position>(entity);
pos.X = 10;
pos.Y = 20;

// Check existence
if (repo.HasComponent<Position>(entity))
{
    ref var p = ref repo.GetComponent<Position>(entity);
}
```

### **Managed Components (Tier 2)**

```csharp
// Managed components use classes
ref var brain = ref repo.GetManagedComponent<AIBrain>(entity);
brain.State = AIState.Patrolling;
```

### **Tags**

```csharp
// Add/remove tags
repo.AddTag<IsStatic>(entity);
repo.RemoveTag<IsStatic>(entity);

// Check tag
bool isStatic = repo.HasComponent<IsStatic>(entity);
```

### **Multi-Part Components**

```csharp
// Get all parts (as Span for zero-allocation iteration)
Span<WheelComponent> wheels = repo.GetParts<WheelComponent>(vehicle);

// Iterate and modify
for (int i = 0; i < wheels.Length; i++)
{
    wheels[i].Rotation += deltaTime;
}

// Set all parts at once
repo.SetParts(entity, new[]
{
    new WheelComponent { Radius = 0.5f },
    new WheelComponent { Radius = 0.5f },
    new WheelComponent { Radius = 0.5f },
    new WheelComponent { Radius = 0.5f }
}.AsSpan());

// Add single part
repo.AddPart(entity, new WheelComponent { Radius = 0.6f });

// Remove part by index
repo.RemovePart<WheelComponent>(entity, 2);
```

---

## **Queries & Iteration**

### **Basic Query**

```csharp
// Simple iteration
foreach (var e in repo.Query(new EntityQuery().With<Position>()))
{
    ref var pos = ref repo.GetComponent<Position>(e);
    Console.WriteLine($"Entity at ({pos.X}, {pos.Y})");
}
```

### **Multi-Component Queries**

```csharp
// Require multiple components
var query = new EntityQuery()
    .With<Position>()
    .With<Velocity>()
    .Without<IsStatic>();  // Exclude static entities

foreach (var e in repo.Query(query))
{
    ref var pos = ref repo.GetComponent<Position>(e);
    ref var vel = ref repo.GetComponent<Velocity>(e);
    
    pos.X += vel.DX;
    pos.Y += vel.DY;
}
```

### **Exclusion Filters**

```csharp
// Find all entities with Position but NOT Velocity
var query = new EntityQuery()
    .With<Position>()
    .Without<Velocity>();

foreach (var e in repo.Query(query)) { }
```

### **Authority Queries (Network)**

```csharp
// Only process entities we own
var query = new EntityQuery()
    .WithOwned<Position>()  // We must own Position
    .WithOwned<Velocity>(); // We must own Velocity

foreach (var e in repo.Query(query))
{
    // Safe to modify - we have authority
}

// Or: require at least one owned component
var anyOwned = new EntityQuery()
    .With<Position>()
    .WithAnyOwned();  // We own something on this entity
```

---

## **Advanced Iteration**

### **Delta Iteration (Changed Entities)**

```csharp
ulong lastTick = time.CurrentTick;

// ... time passes ...

// Only iterate changed entities
foreach (var e in repo.QueryDelta(query, sinceTick: lastTick))
{
    // This entity was modified since lastTick
    // Perfect for network updates
}
```

### **Time-Sliced Iteration**

```csharp
// Spread iteration over multiple frames
var state = new IteratorState();

while (!state.IsComplete)
{
    // Process 1ms worth each frame
    foreach (var e in repo.QueryTimeSliced(query, state, budgetMs: 1.0))
    {
        // Expensive processing here
    }
    
    yield return null; // Next frame
}
```

### **Parallel Iteration**

```csharp
// Multi-threaded processing
repo.QueryParallel<Position, Velocity>(query, (Entity e, ref Position p, ref Velocity v) =>
{
    // This lambda runs on multiple threads
    p.X += v.DX;
    p.Y += v.DY;
});
```

### **Hierarchical Queries (DIS)**

```csharp
// Query by DIS entity type
var platforms = repo.Query(new EntityQuery()
    .WithDISCategory(DISCategory.Platform));

var landVehicles = repo.Query(new EntityQuery()
    .WithDISCategory(DISCategory.Platform)
    .WithDISType(DISPlatformType.LandVehicle));
```

---

## **Time System**

```csharp
// Set up time system
var time = new TimeSystem();
time.IsDeterministic = true; // For replay/networking

// Each frame (in deterministic mode)
time.SetFrameTime(0.016);  // 60Hz = 16ms

// Access time
double dt = time.DeltaTime;
ulong tick = time.CurrentTick;

// Check frame budget
if (!time.HasTimeRemaining(5.0)) // Need 5ms
{
    // Defer to next frame
}
```

---

## **Phase System**

```csharp
// Phases ensure correct execution order
public enum Phase
{
    Initialization,  // Setup
    NetworkReceive,  // Ingest remote updates
    Simulation,      // Game logic
    NetworkSend,     // Export changes
    Presentation     // Rendering
}

// Set current phase
repo.SetPhase(Phase.Simulation);

// Phase is enforced automatically
// e.g., modifying ghost entities only allowed in NetworkReceive
```


---

## **Entity Command Buffers**

```csharp
// Defer structural changes (thread-safe)
var ecb = new EntityCommandBuffer();

// Inside parallel job
Parallel.For(0, 1000, i =>
{
    // Can't modify repo directly in parallel
    // Use command buffer instead
    var e = ecb.CreateEntity();
    ecb.AddComponent(e, new Position { X = i });
});

// Playback on main thread
ecb.Playback(repo);
```

---

## **Global Singletons**

```csharp
// Set global data
repo.SetSingleton(new GameConfig
{
    Gravity = -9.81f,
    MaxPlayers = 64
});

// Access anywhere
ref var config = ref repo.GetSingleton<GameConfig>();
float gravity = config.Gravity;
```

---

## **TKB Templates**

```csharp
// Load entity templates from TKB database
var tkb = new TkbDatabase();
tkb.LoadFromFile("entities.tkb");

// Spawn by template name
Entity tank = tkb.Spawn("M1A2_Abrams", repo);

// Modify spawned entity
repo.GetComponent<Position>(tank) = new Position { X = 100, Y = 200 };

// Spawn by DIS type
Entity genericTank = tkb.SpawnByDIS(
    category: DISCategory.Platform,
    country: DISCountry.USA,
    domain: DISDomain.Land,
    kind: 1, // Tank
    repo: repo
);
```

---

## **Serialization**

```csharp
// Save entire repository
repo.SaveToFile("savegame.fdp");

// Load
var repo2 = new EntityRepository();
repo2.LoadFromFile("savegame.fdp");

// Verify
Assert.Equal(repo.EntityIndex.ActiveCount, repo2.EntityIndex.ActiveCount);
```

---

# **Common Patterns**

## **Pattern 1: Physics Update**

```csharp
public void PhysicsUpdate(EntityRepository repo, double deltaTime)
{
    // Apply velocity
    var query = new EntityQuery()
        .With<Position>()
        .With<Velocity>();
    
    foreach (var e in repo.Query(query))
    {
        ref var pos = ref repo.GetComponent<Position>(e);
        ref var vel = ref repo.GetComponent<Velocity>(e);
        
        pos.X += vel.DX * deltaTime;
        pos.Y += vel.DY * deltaTime;
    }
}
```

## **Pattern 2: Network Synchronization**

```csharp
public void SendNetworkUpdate(EntityRepository repo, NetworkSocket socket)
{
    // Only send entities we own that changed
    var query = new EntityQuery()
        .WithAnyOwned()
        .With<Position>();
    
    ulong lastSendTick = getLastSendTick();
    
    foreach (var e in repo.QueryDelta(query, lastSendTick))
    {
        ref var pos = ref repo.GetComponent<Position>(e);
        
        socket.Send(new PositionUpdate
        {
            EntityId = e.Index,
            X = pos.X,
            Y = pos.Y
        });
    }
}
```

## **Pattern 3: Vehicle Wheel Update**

```csharp
public void UpdateVehicleWheels(Entity vehicle, EntityRepository repo, float steering)
{
    Span<WheelComponent> wheels = repo.GetParts<WheelComponent>(vehicle);
    
    // Front wheels steer
    wheels[0].SteerAngle = steering;
    wheels[1].SteerAngle = steering;
    
    // All wheels rotate
    ref var vel = ref repo.GetComponent<Velocity>(vehicle);
    float rotation = vel.DX * 0.1f; // Speed to rotation
    
    for (int i = 0; i < wheels.Length; i++)
    {
        wheels[i].Rotation += rotation;
    }
}
```

## **Pattern 4: Hierarchical Spawning**

```csharp
public Entity SpawnTankPlatoon(TkbDatabase tkb, EntityRepository repo, Position startPos)
{
    // Spawn platoon leader
    Entity leader = tkb.Spawn("M1A2_Commander", repo);
    repo.GetComponent<Position>(leader) = startPos;
    repo.AddTag<PlatoonLeader>(leader);
    
    // Spawn 3 followers
    for (int i = 0; i < 3; i++)
    {
        Entity follower = tkb.Spawn("M1A2_Abrams", repo);
        repo.GetComponent<Position>(follower) = new Position
        {
            X = startPos.X + (i + 1) * 50,
            Y = startPos.Y
        };
        
        // Link to leader
        repo.GetComponent<Formation>(follower).LeaderEntity = leader;
    }
    
    return leader;
}
```

## **Pattern 5: Cleanup Dead Entities**

```csharp
public void CleanupDeadEntities(EntityRepository repo, EntityCommandBuffer ecb)
{
    var query = new EntityQuery().With<Health>();
    
    foreach (var e in repo.Query(query))
    {
        ref var health = ref repo.GetComponent<Health>(e);
        
        if (health.Value <= 0)
        {
            // Defer destruction
            ecb.DestroyEntity(e);
        }
    }
    
    // Playback outside iteration
    ecb.Playback(repo);
}
```

---

# **Performance Guidelines**

## **DO's**

### ✅ Use `ref` for Large structs
```csharp
ref var transform = ref repo.GetComponent<Transform>(e);
transform.Matrix = Matrix4x4.Identity;
```

### ✅ Batch queries
```csharp
// Good: Single query, single loop
foreach (var e in repo.Query(query))
{
    // Process everything here
}

// Bad: Multiple queries for same entities
foreach (var e in repo.Query(query1)) { }
foreach (var e in repo.Query(query2)) { } // May overlap!
```

### ✅ Use Span<T> for multi-part iteration
```csharp
Span<Wheel> wheels = repo.GetParts<Wheel>(vehicle);
for (int i = 0; i < wheels.Length; i++)
{
    wheels[i].Rotation += deltaTime;
}
```

### ✅ Enable aggressive inlining for hot paths
```csharp
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public static Vector3 FastNormalize(Vector3 v) { }
```

## **DON'Ts**

### ❌ Don't modify structure during iteration
```csharp
// BAD!
foreach (var e in repo.Query(query))
{
    repo.DestroyEntity(e); // Undefined behavior!
}

// GOOD: Use EntityCommandBuffer
var ecb = new EntityCommandBuffer();
foreach (var e in repo.Query(query))
{
    ecb.DestroyEntity(e);
}
ecb.Playback(repo);
```

### ❌ Don't query inside loops
```csharp
// BAD!
for (int i = 0; i < 1000; i++)
{
    foreach (var e in repo.Query(query)) { } // 1000 queries!
}

// GOOD:
foreach (var e in repo.Query(query))
{
    for (int i = 0; i < 1000; i++) { }
}
```

### ❌ Don't use LINQ on hot paths
```csharp
// BAD: Allocates enumerators
var entities = repo.Query(query).Where(e => condition).ToList();

// GOOD: Manual filtering
var entities = new List<Entity>();
foreach (var e in repo.Query(query))
{
    if (condition)
        entities.Add(e);
}
```

---

# **Testing Patterns**

## **Basic Test Structure**

```csharp
[Fact]
public void Feature_Scenario_ExpectedResult()
{
    // Arrange
    using var repo = new EntityRepository();
    repo.RegisterComponent<Position>();
    
    var entity = repo.CreateEntity();
    
    // Act
    ref var pos = ref repo.GetComponent<Position>(entity);
    pos.X = 10;
    
    // Assert
    Assert.Equal(10, pos.X);
}
```

## **Stress Testing**

```csharp
[Fact]
public void Repository_100KEntities_HandlesCorrectly()
{
    using var repo = new EntityRepository();
    repo.RegisterComponent<Position>();
    
    // Create
    var entities = new Entity[100_000];
    for (int i = 0; i < 100_000; i++)
        entities[i] = repo.CreateEntity();
    
    // Verify
    Assert.Equal(100_000, repo.EntityIndex.ActiveCount);
    
    // Cleanup
    foreach (var e in entities)
        repo.DestroyEntity(e);
    
    Assert.Equal(0, repo.EntityIndex.ActiveCount);
}
```

## **Benchmarking**

```csharp
[MemoryDiagnoser]
public class PerformanceBenchmarks
{
    private EntityRepository _repo;
    
    [GlobalSetup]
    public void Setup()
    {
        _repo = new EntityRepository();
        _repo.RegisterComponent<Position>();
        
        for (int i = 0; i < 10_000; i++)
        {
            var e = _repo.CreateEntity();
            _repo.GetComponent<Position>(e) = new Position { X = i };
        }
    }
    
    [Benchmark]
    public int IterateAllEntities()
    {
        int count = 0;
        foreach (var e in _repo.Query(new EntityQuery().With<Position>()))
        {
            count++;
        }
        return count;
    }
}
```

---

# **Debugging Tips**

## **Enable Paranoid Mode**

Add to `.csproj`:
```xml
<PropertyGroup>
    <DefineConstants>$(DefineConstants);FDP_PARANOID_MODE</DefineConstants>
</PropertyGroup>
```

This enables:
- Bounds checking on all array access
- Entity lifetime validation
- Component type validation

## **Common Errors**

### **Entity Not Alive**
```
InvalidOperationException: Entity (42, v3) is not alive
```
**Cause:** Using stale entity handle after destruction  
**Fix:** Check `IsAlive()` before use or track separately

### **Wrong Phase**
```
WrongPhaseException: Cannot modify in phase Presentation (expected: Simulation)
```
**Cause:** Trying to modify entities outside Simulation phase  
**Fix:** Move logic to correct phase or use EntityCommandBuffer

### **Component Not Registered**
```
InvalidOperationException: Type Position is not registered
```
**Cause:** Forgot to call `RegisterComponent`  
**Fix:** Add registration at startup

## **Inspecting State**

```csharp
// Debug helper: Print all entities
public static void DebugPrintAllEntities(EntityRepository repo)
{
    int maxId = repo.EntityIndex.MaxIssuedIndex;
    
    for (int i = 0; i <= maxId; i++)
    {
        ref var header = ref repo.EntityIndex.GetHeader(i);
        
        if (!header.IsActive)
            continue;
        
        Console.WriteLine($"Entity {i} (gen {header.Generation}):");
        
        // Print component mask
        for (int bit = 0; bit < 256; bit++)
        {
            if (header.ComponentMask.IsSet(bit))
            {
                var metadata = ComponentRegistry.GetMetadata(bit);
                Console.WriteLine($"  - {metadata.Name}");
            }
        }
    }
}
```

---

# **Component Design Guidelines**

## **Good Component Design**

```csharp
// ✅ GOOD: Small, focused, blittable
[StructLayout(LayoutKind.Sequential)]
public struct Position
{
    public float X, Y, Z;
}

[StructLayout(LayoutKind.Sequential)]
public struct Velocity
{
    public float DX, DY, DZ;
}

// ✅ GOOD: Tag (1 byte, but recognized as tag)
public struct IsStatic { } // Empty struct
```

## **Bad Component Design**

```csharp
// ❌ BAD: Too large (wastes cache)
public struct MegaComponent
{
    public float[,] BigArray; // Don't do this!
    // Keep components under ~256 bytes
}

// ❌ BAD: Contains managed references in Tier 1
public struct BadComponent
{
    public string Name; // Use FixedString32 instead
    public List<int> Items; // Use multi-part instead
}

// ✅ GOOD: Fixed-size string
[StructLayout(LayoutKind.Sequential, Size = 32)]
public struct NameComponent
{
    private unsafe fixed byte _data[32];
    
    public void SetName(string value)
    {
        // UTF-8 encode into buffer
    }
}
```

---

# **Architecture Summary**

```
┌─────────────────────────────────────────────────────────────┐
│                    EntityRepository                         │
│  (Facade for all ECS operations)                            │
└───────────────────┬─────────────────────────────────────────┘
                    │
        ┌───────────┴───────────┐
        │                       │
   ┌────▼────┐          ┌───────▼───────┐
   │ Entity  │          │ Component     │
   │ Index   │          │ Tables        │
   │         │          │               │
   │ Headers │          │ Tier1 (native)│
   │ ├─Mask  │          │ Tier2 (managed│
   │ ├─Auth  │          │ MultiPart     │
   │ └─Gen   │          │ Tags (bitmask)│
   └─────────┘          └───────────────┘
        │
   ┌────▼────────────────────────────────────┐
   │  NativeChunkTable<EntityHeader>         │
   │  ┌────────┬────────┬────────┬────────┐  │
   │  │Chunk 0 │Chunk 1 │Chunk 2 │ ...    │  │
   │  │1024ent │1024ent │1024ent │        │  │
   │  └────────┴────────┴────────┴────────┘  │
   │  (Lazy allocation, 64KB chunks)         │
   └─────────────────────────────────────────┘
```

---

# **Quick Start Example**

```csharp
using Fdp.Kernel;

// Define components
public struct Position { public float X, Y; }
public struct Velocity { public float DX, DY; }
public struct IsEnemy { } // Tag

class Program
{
    static void Main()
    {
        // Create repository
        using var repo = new EntityRepository();
        
        // Register components
        repo.RegisterComponent<Position>();
        repo.RegisterComponent<Velocity>();
        repo.RegisterTag<IsEnemy>();
        
        // Create 1000 enemies
        for (int i = 0; i < 1000; i++)
        {
            var enemy = repo.CreateEntity();
            repo.AddTag<IsEnemy>(enemy);
            repo.GetComponent<Position>(enemy) = new Position { X = i, Y = 0 };
            repo.GetComponent<Velocity>(enemy) = new Velocity { DX = 1, DY = 0 };
        }
        
        // Simulate 60 frames
        for (int frame = 0; frame < 60; frame++)
        {
            UpdatePhysics(repo, 1.0 / 60.0);
        }
        
        Console.WriteLine("Simulation complete!");
    }
    
    static void UpdatePhysics(EntityRepository repo, double dt)
    {
        var query = new EntityQuery()
            .With<Position>()
            .With<Velocity>();
        
        foreach (var e in repo.Query(query))
        {
            ref var pos = ref repo.GetComponent<Position>(e);
            ref var vel = ref repo.GetComponent<Velocity>(e);
            
            pos.X += (float)(vel.DX * dt);
            pos.Y += (float)(vel.DY * dt);
        }
    }
}
```

---

**This quick reference covers 90% of daily FDP usage!** 🚀

For complete implementation details, see:
- `FDP-Implementation-Plan.md` - Stages 1-2
- `FDP-Implementation-Plan-Stages-3-10.md` - Stages 3-4  
- `FDP-Complete-Specification.md` - Overview & Stages 5-8
- `FDP-Stages-9-20-Detailed.md` - Stages 9-20  

**Ready to build!** 💪
