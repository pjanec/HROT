# FDP.Toolkit.Tkb - Template Knowledge Base Registry

## Overview

**FDP.Toolkit.Tkb** provides a **concrete implementation of [ITkbDatabase](FDP.Interfaces.md#itkbdatabase)**, serving as a **centralized registry** for **entity templates** ([TkbTemplate](FDP.Interfaces.md#tkbtemplate)). It enables **blueprint-based entity spawning**, pre-configuring entities with **component archetypes** for consistent, high-performance instantiation.

### Purpose

The Template Knowledge Base (TKB) solves the **entity configuration problem**: instead of manually adding components every time you spawn an entity, **define reusable blueprints** once and **apply them instantly**.

**Without TKB:**
```csharp
// Manual configuration (tedious, error-prone)
var tank = world.CreateEntity();
world.AddComponent(tank, new Position { X = 100, Y = 200 });
world.AddComponent(tank, new Velocity { X = 0, Y = 0 });
world.AddComponent(tank, new Health { Value = 100 });
world.AddComponent(tank, new Armor { Thickness = 50, Type = ArmorType.Composite });
world.AddComponent(tank, new TurretRotation { Azimuth = 0, Elevation = 0 });
world.AddComponent(tank, new NetworkIdentity { NetworkId = 5000 });
// ... 20 more components
```

**With TKB:**
```csharp
// Blueprint configuration (once)
var template = new TkbTemplate("M1A2_Abrams", tkbType: 1001);
template.AddComponent(new Position { X = 0, Y = 0 });
template.AddComponent(new Velocity { X = 0, Y = 0 });
template.AddComponent(new Health { Value = 100 });
template.AddComponent(new Armor { Thickness = 50, Type = ArmorType.Composite });
// ... configure once

tkbDatabase.Register(template);

// Spawning (instant)
var tank1 = world.CreateEntity();
template.ApplyTo(world, tank1);

var tank2 = world.CreateEntity();
template.ApplyTo(world, tank2); // Same blueprint, fresh instance
```

### Key Features

| Feature | Description |
|---------|-------------|
| **Dual-Key Lookup** | Retrieve templates by `Name` (string) or `TkbType` (long) |
| **Case-Insensitive Names** | Template names compared with `StringComparer.OrdinalIgnoreCase` |
| **Duplicate Detection** | Throws `InvalidOperationException` if name or type already registered |
| **Thread-Safe Iteration** | `GetAll()` returns `IEnumerable<TkbTemplate>` for enumeration |
| **Minimal Footprint** | Single-file implementation, ~60 lines of code |

---

## Architecture

### Design Pattern

**FDP.Toolkit.Tkb** implements the **Registry Pattern** with **dual-index lookup**:

```
┌────────────────────────────────────────────────────────────────┐
│                      TkbDatabase                               │
│  ┌──────────────────────────────────────────────────────────┐ │
│  │ Primary Index: Dictionary<long, TkbTemplate>             │ │
│  │ (TkbType → Template)                                     │ │
│  │                                                          │ │
│  │ Examples:                                                │ │
│  │   1001 → M1A2_Abrams template                           │ │
│  │   1002 → F-16_Falcon template                           │ │
│  │   2005 → Infantry_Rifleman template                      │ │
│  └──────────────────────────────────────────────────────────┘ │
│                                                                │
│  ┌──────────────────────────────────────────────────────────┐ │
│  │ Secondary Index: Dictionary<string, TkbTemplate>         │ │
│  │ (Name → Template, case-insensitive)                     │ │
│  │                                                          │ │
│  │ Examples:                                                │ │
│  │   "M1A2_Abrams" → template (TkbType=1001)               │ │
│  │   "m1a2_abrams" → same template (case-insensitive)      │ │
│  │   "F-16_Falcon" → template (TkbType=1002)               │ │
│  └──────────────────────────────────────────────────────────┘ │
│                                                                │
│  Public API:                                                   │
│    - Register(template)                                        │
│    - GetByType(long tkbType)                                   │
│    - TryGetByType(long tkbType, out template)                  │
│    - GetByName(string name)                                    │
│    - TryGetByName(string name, out template)                   │
│    - GetAll()                                                  │
└────────────────────────────────────────────────────────────────┘
```

### Class Structure

```csharp
public class TkbDatabase : ITkbDatabase
{
    // Dual indexes for O(1) lookup
    private readonly Dictionary<string, TkbTemplate> _byName;
    private readonly Dictionary<long, TkbTemplate> _byType;
    
    // ITkbDatabase implementation
    public void Register(TkbTemplate template);
    public TkbTemplate GetByType(long tkbType);
    public bool TryGetByType(long tkbType, out TkbTemplate template);
    public TkbTemplate GetByName(string name);
    public bool TryGetByName(string name, out TkbTemplate template);
    public IEnumerable<TkbTemplate> GetAll();
}
```

---

## Implementation Details

### Registration

```csharp
public void Register(TkbTemplate template)
{
    if (template == null)
        throw new ArgumentNullException(nameof(template));
    
    // Guard: Prevent duplicate names
    if (_byName.ContainsKey(template.Name))
        throw new InvalidOperationException(
            $"Template with name '{template.Name}' already exists.");
    
    // Guard: Prevent duplicate types
    if (_byType.ContainsKey(template.TkbType))
        throw new InvalidOperationException(
            $"Template with TkbType '{template.TkbType}' already exists.");
    
    // Register in both indexes
    _byName[template.Name] = template;
    _byType[template.TkbType] = template;
}
```

**Key Points:**
- **Dual-index update**: Both dictionaries receive the same reference (no duplication)
- **Fail-fast validation**: Throws immediately if duplicate detected
- **Case-insensitive names**: `_byName` uses `StringComparer.OrdinalIgnoreCase`

---

### Lookup by Type

```csharp
public TkbTemplate GetByType(long tkbType)
{
    if (!_byType.TryGetValue(tkbType, out var template))
        throw new KeyNotFoundException(
            $"Template with TkbType {tkbType} not found.");
    return template;
}

public bool TryGetByType(long tkbType, out TkbTemplate template)
{
    return _byType.TryGetValue(tkbType, out template);
}
```

**Usage Example:**
```csharp
// Network replication: Received EntityMaster with TkbType=1001
if (tkbDatabase.TryGetByType(1001, out var template))
{
    template.ApplyTo(world, entity);
}
else
{
    Console.Error.WriteLine($"Unknown TkbType: 1001");
}
```

---

### Lookup by Name

```csharp
public TkbTemplate GetByName(string name)
{
    if (!_byName.TryGetValue(name, out var template))
        throw new KeyNotFoundException(
            $"Template with Name {name} not found.");
    return template;
}

public bool TryGetByName(string name, out TkbTemplate template)
{
    return _byName.TryGetValue(name, out template);
}
```

**Usage Example:**
```csharp
// Spawn by name (e.g., from config file or script)
if (tkbDatabase.TryGetByName("M1A2_Abrams", out var template))
{
    var tank = world.CreateEntity();
    template.ApplyTo(world, tank);
}
```

**Case-Insensitive Behavior:**
```csharp
tkbDatabase.Register(new TkbTemplate("M1A2_Abrams", 1001));

// All of these work:
var t1 = tkbDatabase.GetByName("M1A2_Abrams");
var t2 = tkbDatabase.GetByName("m1a2_abrams");
var t3 = tkbDatabase.GetByName("M1A2_ABRAMS");

Assert.Same(t1, t2);
Assert.Same(t2, t3);
```

---

### Enumeration

```csharp
public IEnumerable<TkbTemplate> GetAll()
{
    return _byType.Values;
}
```

**Usage Example:**
```csharp
// List all registered templates
foreach (var template in tkbDatabase.GetAll())
{
    Console.WriteLine($"{template.Name} (Type: {template.TkbType})");
}
```

---

## Code Examples

### Example 1: Basic Registration and Spawning

```csharp
using Fdp.Interfaces;
using Fdp.Kernel;
using Fdp.Toolkit.Tkb;

// 1. Create TKB database
var tkbDatabase = new TkbDatabase();

// 2. Define template
var tankTemplate = new TkbTemplate("M1A2_Abrams", tkbType: 1001);
tankTemplate.AddComponent(new Position { X = 0, Y = 0, Z = 0 });
tankTemplate.AddComponent(new Health { Value = 100 });
tankTemplate.AddComponent(new Armor { Thickness = 50, Type = ArmorType.Composite });
tankTemplate.AddComponent(new TurretRotation { Azimuth = 0, Elevation = 0 });

// 3. Register template
tkbDatabase.Register(tankTemplate);

// 4. Spawn entities
var world = new EntityRepository();
world.RegisterComponent<Position>();
world.RegisterComponent<Health>();
world.RegisterComponent<Armor>();
world.RegisterComponent<TurretRotation>();

var tank1 = world.CreateEntity();
tankTemplate.ApplyTo(world, tank1);

var tank2 = world.CreateEntity();
tankTemplate.ApplyTo(world, tank2);

// Both tanks have identical component configuration
var health1 = world.GetComponent<Health>(tank1);
var health2 = world.GetComponent<Health>(tank2);
Assert.Equal(100, health1.Value);
Assert.Equal(100, health2.Value);
```

---

### Example 2: Multiple Templates (Vehicle Fleet)

```csharp
var tkbDatabase = new TkbDatabase();

// Tank template
var tank = new TkbTemplate("M1A2_Abrams", 1001);
tank.AddComponent(new Health { Value = 100 });
tank.AddComponent(new Armor { Thickness = 50 });
tank.AddComponent(new Weapon { Type = WeaponType.MainGun, Damage = 100 });
tkbDatabase.Register(tank);

// APC template
var apc = new TkbTemplate("M2_Bradley", 1002);
apc.AddComponent(new Health { Value = 75 });
apc.AddComponent(new Armor { Thickness = 30 });
apc.AddComponent(new Weapon { Type = WeaponType.AutoCannon, Damage = 50 });
tkbDatabase.Register(apc);

// Helicopter template
var heli = new TkbTemplate("AH-64_Apache", 2001);
heli.AddComponent(new Health { Value = 60 });
heli.AddComponent(new Armor { Thickness = 10 });
heli.AddComponent(new Weapon { Type = WeaponType.Missiles, Damage = 150 });
tkbDatabase.Register(heli);

// Spawn mixed fleet
var tankEntity = SpawnByName(tkbDatabase, world, "M1A2_Abrams");
var apcEntity = SpawnByName(tkbDatabase, world, "M2_Bradley");
var heliEntity = SpawnByName(tkbDatabase, world, "AH-64_Apache");

Entity SpawnByName(TkbDatabase db, EntityRepository repo, string name)
{
    var template = db.GetByName(name);
    var entity = repo.CreateEntity();
    template.ApplyTo(repo, entity);
    return entity;
}
```

---

### Example 3: Network Integration (DDS EntityMaster)

**Scenario**: Receiving `EntityMaster` from DDS network, need to spawn local replica.

```csharp
// DDS Message
public struct EntityMaster
{
    public long EntityId;
    public int OwningNodeId;
    public long TemplateId; // TkbType from transmitting node
}

// Translator ingress
public void OnEntityMasterReceived(EntityMaster msg, IEntityCommandBuffer cmd, ISimulationView view)
{
    // Lookup template by TkbType
    if (!tkbDatabase.TryGetByType(msg.TemplateId, out var template))
    {
        Console.Error.WriteLine($"Unknown TkbType: {msg.TemplateId}. Skipping entity.");
        return;
    }
    
    // Create entity
    var entity = cmd.CreateEntity();
    
    // Map network ID
    networkEntityMap.Register(entity, msg.EntityId);
    
    // Apply template components
    template.ApplyTo((EntityRepository)view, entity);
    
    // Set network ownership
    cmd.SetComponent(entity, new NetworkOwnership 
    { 
        PrimaryOwnerId = msg.OwningNodeId 
    });
}
```

---

### Example 4: Dynamic Template Loading (JSON/Config File)

```csharp
using System.Text.Json;

public class TkbLoader
{
    public static void LoadFromJson(TkbDatabase database, string jsonPath)
    {
        var json = File.ReadAllText(jsonPath);
        var config = JsonSerializer.Deserialize<TkbConfig>(json);
        
        foreach (var templateDef in config.Templates)
        {
            var template = new TkbTemplate(templateDef.Name, templateDef.TkbType);
            
            // Add components from config
            if (templateDef.Health != null)
                template.AddComponent(new Health { Value = templateDef.Health.Value });
            
            if (templateDef.Armor != null)
                template.AddComponent(new Armor 
                { 
                    Thickness = templateDef.Armor.Thickness,
                    Type = ParseArmorType(templateDef.Armor.Type)
                });
            
            database.Register(template);
        }
    }
}

// JSON Format
/*
{
  "Templates": [
    {
      "Name": "M1A2_Abrams",
      "TkbType": 1001,
      "Health": { "Value": 100 },
      "Armor": { "Thickness": 50, "Type": "Composite" }
    },
    {
      "Name": "F-16_Falcon",
      "TkbType": 2001,
      "Health": { "Value": 80 }
    }
  ]
}
*/
```

---

### Example 5: Managed Component Templates (Factory Pattern)

**Scenario**: Component contains `List<string>` (managed data).

```csharp
public class Inventory
{
    public string[] Items { get; set; }
}

// Template with managed component factory
var heroTemplate = new TkbTemplate("Hero", 3001);

heroTemplate.AddManagedComponent(() => new Inventory 
{ 
    Items = new[] { "Sword", "Shield", "Potion" } 
});

tkbDatabase.Register(heroTemplate);

// Spawn two heroes
var hero1 = world.CreateEntity();
heroTemplate.ApplyTo(world, hero1);

var hero2 = world.CreateEntity();
heroTemplate.ApplyTo(world, hero2);

// Each hero gets a FRESH inventory instance
var inv1 = world.GetManagedComponent<Inventory>(hero1);
var inv2 = world.GetManagedComponent<Inventory>(hero2);

inv1.Items[0] = "Bow"; // Modify hero1's inventory

Assert.Equal("Bow", inv1.Items[0]);
Assert.Equal("Sword", inv2.Items[0]); // Hero2 unchanged (separate instance)
```

---

## Integration Patterns

### Pattern 1: Centralized Registration (Startup)

```csharp
public class GameInitializer
{
    public static TkbDatabase CreateTkbDatabase()
    {
        var db = new TkbDatabase();
        
        // Register all templates at startup
        RegisterVehicles(db);
        RegisterWeapons(db);
        RegisterInfantry(db);
        
        return db;
    }
    
    private static void RegisterVehicles(TkbDatabase db)
    {
        db.Register(CreateTankTemplate());
        db.Register(CreateAPCTemplate());
        db.Register(CreateHelicopterTemplate());
    }
    
    private static TkbTemplate CreateTankTemplate()
    {
        var template = new TkbTemplate("M1A2_Abrams", 1001);
        // ... configure components
        return template;
    }
}

// Usage
var tkbDatabase = GameInitializer.CreateTkbDatabase();
var kernel = new ModuleHostKernel(world, tkbDatabase);
```

---

### Pattern 2: Lazy Registration (On-Demand)

```csharp
public class TkbManager
{
    private readonly TkbDatabase _database = new();
    private readonly HashSet<long> _registered = new();
    
    public TkbTemplate GetOrCreate(long tkbType)
    {
        if (_database.TryGetByType(tkbType, out var template))
            return template;
        
        // Template doesn't exist - create dynamically
        template = LoadFromExternalSource(tkbType);
        _database.Register(template);
        _registered.Add(tkbType);
        
        return template;
    }
    
    private TkbTemplate LoadFromExternalSource(long tkbType)
    {
        // Load from file, DB, or remote service
        // ...
    }
}
```

---

### Pattern 3: Dependency Injection (ModuleHost)

```csharp
public class NetworkSpawnerModule : IModule
{
    private readonly ITkbDatabase _tkbDatabase;
    
    public NetworkSpawnerModule(ITkbDatabase tkbDatabase)
    {
        _tkbDatabase = tkbDatabase;
    }
    
    public void Tick(ISimulationView view, float deltaTime)
    {
        // Use injected database
        if (_tkbDatabase.TryGetByType(receivedTkbType, out var template))
        {
            // Spawn entity
        }
    }
}

// Setup with DI container
var services = new ServiceCollection();
services.AddSingleton<ITkbDatabase>(new TkbDatabase());
services.AddTransient<NetworkSpawnerModule>();

var provider = services.BuildServiceProvider();
var module = provider.GetService<NetworkSpawnerModule>();
```

---

## Architectural Diagrams

### TKB Ecosystem

```
┌──────────────────────────────────────────────────────────────────┐
│                  FDP Template Knowledge Base Ecosystem           │
├──────────────────────────────────────────────────────────────────┤
│                                                                   │
│  ┌─────────────────────────────────────────────────────────┐    │
│  │ FDP.Interfaces (Abstractions)                           │    │
│  │  - ITkbDatabase (interface)                             │    │
│  │  - TkbTemplate (entity blueprint)                       │    │
│  │  - MandatoryDescriptor (network replication)            │    │
│  └────────────────┬────────────────────────────────────────┘    │
│                   │                                              │
│                   │ implements                                   │
│                   ▼                                              │
│  ┌─────────────────────────────────────────────────────────┐    │
│  │ FDP.Toolkit.Tkb (Implementation)                        │    │
│  │  - TkbDatabase (concrete registry)                      │    │
│  │    * Dual-index lookup (Name, Type)                     │    │
│  │    * Case-insensitive names                             │    │
│  │    * Thread-safe enumeration                            │    │
│  └────────────────┬────────────────────────────────────────┘    │
│                   │                                              │
│                   │ used by                                      │
│                   ▼                                              │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │ Consuming Modules                                        │   │
│  │                                                          │   │
│  │  ┌──────────────────────┐  ┌─────────────────────────┐  │   │
│  │  │ EntityLifecycleModule│  │ NetworkSpawnerModule    │  │   │
│  │  │ (FDP.Toolkit.        │  │ (Application)           │  │   │
│  │  │  Lifecycle)          │  │ - Receives TkbType      │  │   │
│  │  │ - Spawns entities    │  │   from network          │  │   │
│  │  │   from templates     │  │ - Looks up template     │  │   │
│  │  │ - Manages lifecycle  │  │ - Spawns local replica  │  │   │
│  │  └──────────────────────┘  └─────────────────────────┘  │   │
│  │                                                          │   │
│  │  ┌──────────────────────┐  ┌─────────────────────────┐  │   │
│  │  │ ReplicationModule    │  │ SaveGameSystem          │  │   │
│  │  │ (FDP.Toolkit.        │  │ (Application)           │  │   │
│  │  │  Replication)        │  │ - Saves TkbType with    │  │   │
│  │  │ - Publishes TkbType  │  │   entity state          │  │   │
│  │  │   in descriptors     │  │ - Restores from         │  │   │
│  │  │ - Hydrates entities  │  │   template on load      │  │   │
│  │  └──────────────────────┘  └─────────────────────────┘  │   │
│  └──────────────────────────────────────────────────────────┘   │
│                                                                   │
└──────────────────────────────────────────────────────────────────┘
```

---

### Lookup Flow

```
┌──────────────────────────────────────────────────────────────────┐
│               Template Lookup Flow (Network Example)              │
├──────────────────────────────────────────────────────────────────┤
│                                                                    │
│  Network Message:                                                 │
│    EntityMaster { EntityId = 5000, TkbType = 1001 }               │
│                    │                                               │
│                    ▼                                               │
│  ┌──────────────────────────────────────────────────────┐         │
│  │ NetworkSpawnerModule.PollIngress()                   │         │
│  │   - Extract TkbType: 1001                            │         │
│  └────────────────┬─────────────────────────────────────┘         │
│                   │                                                │
│                   ▼                                                │
│  ┌──────────────────────────────────────────────────────┐         │
│  │ tkbDatabase.TryGetByType(1001, out template)         │         │
│  │   - Lookup in _byType dictionary                     │         │
│  │   - O(1) hash lookup                                 │         │
│  └────────────────┬─────────────────────────────────────┘         │
│                   │                                                │
│          ┌────────┴────────┐                                      │
│          │                 │                                      │
│      Found?              Not Found?                               │
│          │                 │                                      │
│          ▼                 ▼                                      │
│  ┌──────────────┐  ┌──────────────────────────────┐              │
│  │ Return       │  │ Log error:                   │              │
│  │ template     │  │ "Unknown TkbType: 1001"      │              │
│  │ (M1A2_Abrams)│  │ Skip entity creation         │              │
│  └──────┬───────┘  └──────────────────────────────┘              │
│         │                                                          │
│         ▼                                                          │
│  ┌──────────────────────────────────────────────────────┐         │
│  │ template.ApplyTo(world, entity)                      │         │
│  │   - Add Position, Health, Armor, Turret components   │         │
│  │   - Entity now fully configured                      │         │
│  └──────────────────────────────────────────────────────┘         │
│                                                                    │
└──────────────────────────────────────────────────────────────────┘
```

---

## Performance Characteristics

### Lookup Performance

| Operation | Time Complexity | Notes |
|-----------|-----------------|-------|
| `Register(template)` | O(1) | Two dictionary insertions + duplicate checks |
| `GetByType(tkbType)` | O(1) | Direct hash lookup in `_byType` |
| `GetByName(name)` | O(1) | Direct hash lookup in `_byName` (case-insensitive) |
| `TryGetByType()` | O(1) | No exception overhead |
| `GetAll()` | O(N) | Enumerates all N templates |

### Memory Overhead

| Component | Memory Cost | Notes |
|-----------|-------------|-------|
| **Per Template** | 2× dictionary entry | Both `_byName` and `_byType` store same reference |
| **Dictionary Overhead** | ~16 bytes per entry | CLR dictionary metadata |
| **Total (100 templates)** | ~3.2 KB overhead | Negligible for most applications |

**Example Calculation:**
- 100 templates
- Each entry: 8 bytes (key) + 8 bytes (value reference) = 16 bytes
- Two dictionaries: 100 × 16 × 2 = 3,200 bytes ≈ 3.2 KB

---

## Thread Safety

**TkbDatabase is NOT thread-safe** for concurrent writes. Design assumes:

1. **Registration Phase** (single-threaded, initialization): `Register()` called during app startup
2. **Lookup Phase** (multi-threaded, runtime): `GetByType()`, `GetByName()` called from multiple threads

**Safe Usage Pattern:**
```csharp
// Initialization (single-threaded)
var tkbDatabase = new TkbDatabase();
tkbDatabase.Register(template1);
tkbDatabase.Register(template2);

// Freeze database (no more registrations)
var databaseReadOnly = (ITkbDatabase)tkbDatabase;

// Runtime (multi-threaded)
Parallel.For(0, 1000, i =>
{
    // Safe: Read-only access after initialization
    var template = databaseReadOnly.GetByType(1001);
});
```

**Unsafe Pattern (Concurrent Writes):**
```csharp
// UNSAFE: Two threads registering simultaneously
Parallel.Invoke(
    () => tkbDatabase.Register(template1), // Thread 1
    () => tkbDatabase.Register(template2)  // Thread 2 - RACE CONDITION!
);
```

---

## Error Handling

### Common Exceptions

| Exception | Trigger | Resolution |
|-----------|---------|------------|
| `ArgumentNullException` | `Register(null)` | Validate template before registering |
| `InvalidOperationException` | Duplicate name or type | Check with `TryGetByType()` before registering |
| `KeyNotFoundException` | Template not found | Use `Try*` variants (`TryGetByType()`, `TryGetByName()`) |

### Defensive Coding

```csharp
// BAD: Assumes template exists (throws KeyNotFoundException)
var template = tkbDatabase.GetByType(1001);

// GOOD: Graceful handling
if (tkbDatabase.TryGetByType(1001, out var template))
{
    template.ApplyTo(world, entity);
}
else
{
    Console.Error.WriteLine($"Template 1001 not found. Using default configuration.");
    ApplyDefaultComponents(world, entity);
}
```

---

## Testing Strategies

### Unit Tests

```csharp
[Fact]
public void Register_ValidTemplate_Success()
{
    var db = new TkbDatabase();
    var template = new TkbTemplate("Test", 1);
    
    db.Register(template);
    
    var retrieved = db.GetByType(1);
    Assert.Same(template, retrieved);
}

[Fact]
public void Register_DuplicateName_Throws()
{
    var db = new TkbDatabase();
    db.Register(new TkbTemplate("Test", 1));
    
    Assert.Throws<InvalidOperationException>(() =>
        db.Register(new TkbTemplate("Test", 2))
    );
}

[Fact]
public void GetByName_CaseInsensitive_Success()
{
    var db = new TkbDatabase();
    db.Register(new TkbTemplate("M1A2_Abrams", 1001));
    
    var t1 = db.GetByName("M1A2_Abrams");
    var t2 = db.GetByName("m1a2_abrams");
    
    Assert.Same(t1, t2);
}

[Fact]
public void TryGetByType_NotFound_ReturnsFalse()
{
    var db = new TkbDatabase();
    
    var found = db.TryGetByType(9999, out var template);
    
    Assert.False(found);
    Assert.Null(template);
}
```

---

## Dependencies

**Project References:**
- **Fdp.Interfaces**: `ITkbDatabase` interface, `TkbTemplate` class
- **Fdp.Kernel**: None (optional, depends on usage)

**External Packages:**
- None (uses BCL `Dictionary<TKey, TValue>`, `StringComparer`)

---

## Future Enhancements

### Read-Only Snapshot

Freeze database after initialization:

```csharp
public class ReadOnlyTkbDatabase : ITkbDatabase
{
    private readonly TkbDatabase _inner;
    
    public ReadOnlyTkbDatabase(TkbDatabase database)
    {
        _inner = database;
    }
    
    public TkbTemplate GetByType(long tkbType) => _inner.GetByType(tkbType);
    // ... delegate all reads, block writes
    
    public void Register(TkbTemplate template) => 
        throw new InvalidOperationException("Database is read-only");
}
```

### Versioning

Track template versions for hot-reloading:

```csharp
public class VersionedTkbDatabase : ITkbDatabase
{
    private readonly Dictionary<long, (TkbTemplate Template, int Version)> _byType;
    
    public void Register(TkbTemplate template, int version)
    {
        _byType[template.TkbType] = (template, version);
    }
    
    public bool IsNewer(long tkbType, int currentVersion)
    {
        return _byType TryGetValue(tkbType, out var entry) && entry.Version > currentVersion;
    }
}
```

### Persistence

Save/load database from disk:

```csharp
public static class TkbSerializer
{
    public static void SaveToFile(TkbDatabase database, string path)
    {
        var templates = database.GetAll().ToList();
        var json = JsonSerializer.Serialize(templates);
        File.WriteAllText(path, json);
    }
    
    public static TkbDatabase LoadFromFile(string path)
    {
        var json = File.ReadAllText(path);
        var templates = JsonSerializer.Deserialize<List<TkbTemplate>>(json);
        
        var database = new TkbDatabase();
        foreach (var template in templates)
        {
            database.Register(template);
        }
        return database;
    }
}
```

---

## Summary

**FDP.Toolkit.Tkb** provides a **minimal, high-performance implementation** of the Template Knowledge Base pattern. Key characteristics:

1. **Dual-Index Lookup**: O(1) retrieval by `Name` (string) or `TkbType` (long)
2. **Case-Insensitive Names**: Flexible string matching for user convenience
3. **Thread-Safe Reads**: After initialization, lookups safe from multiple threads
4. **Fail-Fast Validation**: Duplicate detection prevents registry corruption
5. **Single-File Implementation**: ~60 lines, zero external dependencies

**Typical Usage:**
- **Network Replication**: Map received `TkbType` to local template, spawn replica entity
- **Save/Load**: Serialize `TkbType` with entity state, restore from template on load
- **Lifecycle Management**: EntityLifecycleModule uses templates for entity construction
- **Blueprint System**: Game designers configure archetypes ("M1A2_Abrams", "F-16_Falcon"), runtime applies instantly

**Line Count**: 62 lines (implementation)  
**Dependencies**: FDP.Interfaces only  
**Test Coverage**: None (toolkit layer, integration tested via consuming modules)

---

END OF DOCUMENT

*Document Statistics:*
- **Lines**: 831
- **Sections**: 12
- **Code Examples**: 5
- **ASCII Diagrams**: 3
- **Dependencies Documented**: 1
- **Integration Patterns**: 3
