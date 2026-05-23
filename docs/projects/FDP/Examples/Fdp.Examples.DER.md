# Fdp.Examples.DER

| Field | Value |
|---|---|
| **Project path** | `FDP/Examples/Fdp.Examples.DER/Fdp.Examples.DER.csproj` |
| **Output type** | Executable (`<OutputType>Exe</OutputType>`) |
| **Target framework** | net8.0 |
| **Date documented** | 2026-05-23 |

## README Validation

**Missing** — No README.md exists in the project folder. This document serves as the
primary reference.

---

## Executive Overview

`Fdp.Examples.DER` demonstrates the **Data Entity Replication (DER)** pattern — a technique
for building a local mirror of a DDS-published entity database with dynamic descriptors.

The project shows how to:

1. **Ingest an `EntityMaster` topic** that carries entity lifecycle (create / update /
   dispose) and maps each entity to a TKB template type.
2. **Subscribe to multiple "descriptor" topics** simultaneously — each descriptor adds
   attributes to an entity — using a uniform `IIngressHandler` abstraction that scales to
   50+ topics without exponential complexity.
3. **Write DDS traffic** from a background task to simulate a real publisher, then observe
   the replicated state in a `DerRepo`.

The executable runs until the user presses Enter, polling all registered DDS topics in a
5 ms loop and reporting entity counts.

### Key learning objectives

1. How to use `DerRepo` to maintain a dynamic entity map updated from DDS.
2. The `MasterIngressHandler<T>` / `DescriptorIngressHandler<T>` pattern for scalable
   multi-topic ingress.
3. DDS QoS settings (Reliable + TransientLocal + KeepLast) for late-joining subscribers.
4. Multi-key descriptors using `[DdsManaged]` and composite `[DdsKey]` fields.
5. Using `DdsParticipant`, `DdsWriter<T>`, and `DdsReader<T>` from `CycloneDDS.NET`.

---

## Architecture

### Component Topology

```
+-----------------------------------------------+
|               Program (Main)                   |
|                                                |
|  EntityMasterIngressExample         SimulateTraffic (Task)
|  +--------------------------------+  +-------------------+
|  | DdsParticipant (domain 0)      |  | DdsParticipant(0) |
|  |                                |  | DdsWriter         |
|  | _handlers: IIngressHandler[]   |  |  <LocalEntityMaster>|
|  |  [0] MasterIngressHandler      |  | Writes 5 entities |
|  |       <LocalEntityMaster>      |  | Updates 1 entity  |
|  |  [1] DescriptorIngressHandler  |  | Disposes 2 entities|
|  |       <LocalGeoSpatial>        |  +-------------------+
|  |  [2] DescriptorIngressHandler  |
|  |       <LocalMapEntitySymbol>   |  Every 5 ms:
|  |                                |  -> handler.Poll()
|  | _repo: DerRepo                 |  -> routes to DerRepo
|  +--------------------------------+
+-----------------------------------------------+
```

### Ingress Handler Pipeline

```
+-------------------+     +----------------------+     +-----------------+
|  DDS Network       |     |  IIngressHandler      |     |  DerRepo        |
|                   |     |  .Poll()              |     |                 |
|  DDS Reader       |---->| Read DDS samples      |---->| Create/Update/  |
|  (any topic T)    |     | Map to EntityId       |     | Delete entities |
|                   |     | Apply to repo         |     | and descriptors |
+-------------------+     +----------------------+     +-----------------+
         ^                      ^
         |                      | Registered at startup:
         |                      | MasterIngressHandler  (entity lifecycle)
         |                      | DescriptorIngressHandler (attribute sets)
         |                      | ... (50+ handlers, same pattern)
         +----------------------+
```

### Data Flow for Entity Lifecycle

```
 Traffic Task                     Ingress Loop               DerRepo
    |                                  |                         |
    | Write(LocalEntityMaster{Id=1001})|                         |
    |--------------------------------->|                         |
    |                                  | MasterIngressHandler    |
    |                                  | .Poll() reads sample    |
    |                                  |------------------------>|
    |                                  |  CreateEntity(1001,TkbType=1)
    |                                  |                         |
    | Write(LocalGeoSpatial{Id=1001})  |                         |
    |--------------------------------->|                         |
    |                                  | DescriptorIngressHandler|
    |                                  | .Poll() reads sample    |
    |                                  |------------------------>|
    |                                  |  UpdateDescriptor(1001, GeoSpatial{Lat,Lon})
    |                                  |                         |
    | DisposeInstance({Id=1001})       |                         |
    |--------------------------------->|                         |
    |                                  | MasterIngressHandler    |
    |                                  | .Poll() reads dispose   |
    |                                  |------------------------>|
    |                                  |  DeleteEntity(1001)     |
```

---

## Source Structure

```
FDP/Examples/Fdp.Examples.DER/
+-- Fdp.Examples.DER.csproj
+-- Program.cs                              namespace Fdp.Toolkit.DER.Examples
|     class Program
+-- LocalDescriptors.cs                     namespace Fdp.Toolkit.DER.Examples
|     struct LocalEntityMaster              [DdsTopic("LocalEntityMaster")]
|     struct LocalGeoSpatial                [DdsTopic("LocalGeoSpatial")]
|     struct LocalMapEntitySymbol           [DdsTopic("LocalMapEntitySymbol")]
+-- EntityMasterIngressExample.cs           namespace Fdp.Toolkit.DER.Examples
      class EntityMasterIngressExample
```

> **Namespace note:** All types use `Fdp.Toolkit.DER.Examples` (not `Fdp.Examples.DER`).
> This mirrors the `Fdp.Toolkit.DER` toolkit namespace.

---

## Public API Reference

### `LocalEntityMaster`

```csharp
[DdsTopic("LocalEntityMaster")]
[DdsQos(Reliability = DdsReliability.Reliable,
        Durability  = DdsDurability.TransientLocal,
        HistoryKind = DdsHistoryKind.KeepLast,
        HistoryDepth = 1)]
public partial struct LocalEntityMaster
```

Lifecycle master for entities. Each write creates/updates an entity; each dispose removes
it.

| Field | Type | DDS role | Description |
|---|---|---|---|
| `EntityId` | `int` | `[DdsKey]` | Primary entity identifier |
| `TkbType` | `long` | data | TKB template type |
| `DisType` | `ulong` | data | DIS entity type |
| `Flags` | `ulong` | data | Miscellaneous capability flags |
| `MockHealth` | `float` | data | Demonstration health field |

### `LocalGeoSpatial`

```csharp
[DdsTopic("LocalGeoSpatial")]
[DdsQos(Reliability = DdsReliability.Reliable,
        Durability  = DdsDurability.TransientLocal,
        HistoryKind = DdsHistoryKind.KeepLast,
        HistoryDepth = 1)]
public partial struct LocalGeoSpatial
```

Single-part descriptor carrying spatial attributes for an entity.

| Field | Type | DDS role | Description |
|---|---|---|---|
| `EntityId` | `int` | `[DdsKey]` | Foreign key to `LocalEntityMaster` |
| `InternalLatitude` | `double` | data | Example latitude field |
| `InternalLongitude` | `double` | data | Example longitude field |
| `MockSpeed` | `float` | data | Example speed field |

### `LocalMapEntitySymbol`

```csharp
[DdsTopic("LocalMapEntitySymbol")]
[DdsQos(Reliability = DdsReliability.Reliable,
        Durability  = DdsDurability.TransientLocal,
        HistoryKind = DdsHistoryKind.KeepLast,
        HistoryDepth = 1)]
[DdsManaged]
public partial struct LocalMapEntitySymbol
```

Multi-part (composite-key) descriptor. Each entity can have multiple map symbol groups.
The `[DdsManaged]` attribute enables automatic lifecycle tracking by the `DerRepo`.

| Field | Type | DDS role | Description |
|---|---|---|---|
| `EntityId` | `int` | `[DdsKey]` | Foreign key to `LocalEntityMaster` |
| `MapGroupId` | `int` | `[DdsKey]` | Secondary key for multi-group support |
| `MockSymbolCode` | `string` | data | Military symbol code string |
| `MockColorIndex` | `int` | data | Rendering color index |

### `EntityMasterIngressExample`

```csharp
public class EntityMasterIngressExample
```

Top-level orchestrator that registers all handlers and drives the polling loop.

| Member | Description |
|---|---|
| `EntityMasterIngressExample()` | Constructor: creates `DerRepo`, `DdsParticipant`, and registers 3 handlers |
| `void Start()` | Starts the background ingress `Task` |
| `void Stop()` | Cancels the ingress loop and waits up to 2 seconds for clean shutdown |
| `void PrintRepoStatus()` | Prints the number of entities currently in the repo |

### Private `IIngressHandler` implementations

| Type | Registered topic | Role |
|---|---|---|
| `MasterIngressHandler<LocalEntityMaster>` | `LocalEntityMaster` | Entity create/delete lifecycle |
| `DescriptorIngressHandler<LocalGeoSpatial>` | `LocalGeoSpatial` | Single-key attribute update |
| `DescriptorIngressHandler<LocalMapEntitySymbol>` | `LocalMapEntitySymbol` | Multi-key attribute update |

### `Program`

```csharp
class Program
{
    static async Task Main(string[] args);
    static async Task SimulateTraffic();
}
```

`Main` — constructs `EntityMasterIngressExample`, starts it, launches `SimulateTraffic`
on a background task, waits for Enter, then calls `Stop()`.

`SimulateTraffic` — uses a separate `DdsParticipant` to write 5 entities, update one,
and dispose two, simulating a realistic publisher lifecycle.

---

## Dependencies

### NuGet packages

| Package | Version | Purpose |
|---|---|---|
| `CycloneDDS.NET` | 0.2.2 | DDS runtime: participants, readers, writers, QoS |

### Project references

| Project | Purpose |
|---|---|
| `Fdp.Toolkits` | Provides `DerRepo`, `IIngressHandler`, `MasterIngressHandler<T>`, `DescriptorIngressHandler<T>`, `IDerRepo` |

---

## Usage Examples

### Example 1 — Running the demo

```bash
cd FDP/Examples/Fdp.Examples.DER
dotnet run
# Output:
# ========================================
#    FDP.Toolkit.DER Example Application
# ========================================
# Starting Ingress...
# Starting Traffic Simulator...
# [TRAFFIC] Wrote Entity 1000
# [TRAFFIC] Wrote Entity 1001
# ...
# Press ENTER to stop...
# Repo contains 3 entities.
# Stopping Ingress...
```

### Example 2 — Extending to 50+ descriptors

```csharp
// In EntityMasterIngressExample constructor, adding more descriptors
// follows the exact same pattern with no structural changes:

_handlers.Add(new DescriptorIngressHandler<LocalGeoSpatial>(
    _participant, _repo, "LocalGeoSpatial", data => data.EntityId));

_handlers.Add(new DescriptorIngressHandler<LocalMapEntitySymbol>(
    _participant, _repo, "LocalMapEntitySymbol",
    data => data.EntityId, data => data.MapGroupId));

// Each additional descriptor is one more Add() call:
_handlers.Add(new DescriptorIngressHandler<LocalTacticalStatus>(
    _participant, _repo, "LocalTacticalStatus", data => data.EntityId));

_handlers.Add(new DescriptorIngressHandler<LocalWeaponStatus>(
    _participant, _repo, "LocalWeaponStatus", data => data.EntityId));

// ... repeat for all 50+ topics
```

### Example 3 — Querying the DerRepo after ingress

```csharp
// After calling ingress.Start() and letting traffic flow:
var repo = ingressExample.GetRepo(); // exposed if made public

// Get all entities and their master attributes
foreach (var entity in repo.GetAllEntities())
{
    long tkbType = repo.GetMasterAttribute<long>(entity.EntityId, "TkbType");
    Console.WriteLine($"Entity {entity.EntityId}: TkbType={tkbType}");

    // Check if a descriptor has arrived
    if (repo.HasDescriptor<LocalGeoSpatial>(entity.EntityId))
    {
        var geo = repo.GetDescriptor<LocalGeoSpatial>(entity.EntityId);
        Console.WriteLine($"  Lat={geo.InternalLatitude:F6}");
    }
}
```

### Example 4 — Custom QoS for real-time descriptors

```csharp
// For descriptors that change every frame, use BestEffort + VolatileQoS:
[DdsTopic("LocalHighFrequencyTransform")]
[DdsQos(Reliability = DdsReliability.BestEffort,
        Durability  = DdsDurability.Volatile,
        HistoryKind = DdsHistoryKind.KeepLast,
        HistoryDepth = 1)]
public partial struct LocalHighFrequencyTransform
{
    [DdsKey] public int EntityId;
    public float X;
    public float Y;
    public float Z;
}

// Then register just like any other descriptor:
_handlers.Add(new DescriptorIngressHandler<LocalHighFrequencyTransform>(
    _participant, _repo, "LocalHighFrequencyTransform",
    data => data.EntityId));
```

---

## Best Practices

### 1. Use TransientLocal + KeepLast for master and descriptor topics

The QoS applied to `LocalEntityMaster`, `LocalGeoSpatial`, and `LocalMapEntitySymbol`
uses `Durability = TransientLocal` and `HistoryKind = KeepLast`. This ensures that a
late-joining subscriber (e.g., a visualization node that starts after the simulation) can
immediately receive the last known state of every entity without waiting for the publisher
to republish.

### 2. Throttle the poll loop, not the handlers

The `IngressLoop` calls `handler.Poll()` for all handlers every 5 ms. Individual handlers
should not introduce sleep or blocking — that would delay all subsequent handlers. Rate
control belongs at the loop level.

### 3. Separate DdsParticipant instances for reader and writer

`Program.SimulateTraffic` creates its own `DdsParticipant` on the same domain, which is
the idiomatic CycloneDDS pattern. Reusing the ingress participant for writing would
complicate ownership and lifetime management.

### 4. Handle exceptions per-handler

The `IngressLoop` wraps each `handler.Poll()` in a try-catch and logs errors without
stopping the loop. A single malformed sample on one topic should not interrupt all other
topic processing.

### 5. Dispose DdsParticipant with `using`

`DdsParticipant` and `DdsWriter<T>` implement `IDisposable`. Always use `using` blocks or
explicit disposal in a finally block to avoid leaking DDS resources on abnormal exit.

---

## Related Projects

| Project | Relationship |
|---|---|
| `Fdp.Toolkits` | Source of `DerRepo`, `IIngressHandler`, `MasterIngressHandler<T>`, `DescriptorIngressHandler<T>` |
| `Fdp.Examples.DDS` | Shared schema library whose message types follow the same keyed-topic conventions shown here |
| `Fdp.Network.Cyclone` | Provides `DdsIdAllocatorServer` for assigning the `EntityId` values used as DDS keys |
| `Fdp.Examples.IdAllocatorDemo` | Companion demo showing how `DdsIdAllocatorServer` hands out unique IDs |
| `FDP.Toolkit.DER` | The production toolkit that `DerRepo` and the ingress handlers are part of |

---

## Architecture Deep Dive

### What is Data Entity Replication (DER)?

DER is a pattern for maintaining a **local read-only mirror** of a remote entity database.
The authoritative source publishes entity lifecycle and attributes over DDS; a DER subscriber
(the "Ingress") listens to all relevant topics and assembles a `DerRepo` that application
code queries locally, without round-trips to the network.

The key insight is that a simulation can have dozens of descriptor topics (geospatial,
symbol, health, capabilities, sensors, weapons, …) for a single entity. Instead of writing
bespoke subscriber code for each topic, `DescriptorIngressHandler<T>` handles any topic T
with a uniform `Poll()` interface.

### IIngressHandler Pattern

```
interface IIngressHandler
{
    void Poll();
}

class MasterIngressHandler<T> : IIngressHandler
    where T : struct
{
    // Reads DDS samples from T's topic
    // On new sample:    repo.CreateOrUpdateEntity(entityId, tkbType)
    // On dispose event: repo.DeleteEntity(entityId)
    void Poll() { ... }
}

class DescriptorIngressHandler<T> : IIngressHandler
    where T : struct
{
    // Reads DDS samples from T's topic
    // On new sample:    repo.UpdateDescriptor<T>(entityId, sample)
    // On dispose event: repo.RemoveDescriptor<T>(entityId)
    void Poll() { ... }
}
```

The `_handlers` list is a flat `List<IIngressHandler>`. Adding a new descriptor requires
exactly one line. The polling loop is `O(H)` where H is the number of handlers — constant
per topic, independent of entity count.

### DerRepo Conceptual Model

```
DerRepo
+---Entity 1001---+
|  master:         |
|    TkbType=1     |
|    DisType=1     |
|    Flags=0       |
|  descriptors:    |
|    GeoSpatial:   |
|      Lat=48.8... |
|      Lon=2.3...  |
|    MapSymbol[0]: |
|      GroupId=0   |
|      Symbol="..." |
+-----------------+

+---Entity 1002---+
|  master:         |
|    TkbType=2     |
|  descriptors:    |
|    GeoSpatial:   |
|      ...         |
+-----------------+
```

### Namespace Discrepancy

The project's assembly name is `Fdp.Examples.DER` but all source code uses namespace
`Fdp.Toolkit.DER.Examples`. This mirrors the toolkit namespace. The discrepancy is
intentional: this project lives in the `Examples` folder but its code belongs to the
`Fdp.Toolkit.DER` namespace family to indicate it is a reference implementation of the
toolkit's DER pattern, not just a standalone demo.

### TransientLocal QoS and Late-Joining Subscribers

All three topics in this demo use `DdsDurability.TransientLocal`. This means the DDS
middleware retains the last known state of each keyed instance. A subscriber that joins
after entities have been created (e.g., a map display that starts 30 seconds into the
simulation) will immediately receive the current state of all entities without waiting for
publishers to retransmit.

Without `TransientLocal`:
- Late-joining subscriber sees only future updates, not the current state.
- The `DerRepo` starts empty and slowly fills as publishers happen to write again.

### Multi-Key Descriptors

`LocalMapEntitySymbol` has two key fields: `EntityId` and `MapGroupId`. This models
entities that have multiple map symbols (e.g., a unit with a main symbol and a NATO
overlay symbol). The `DescriptorIngressHandler` receives a secondary key extractor lambda:

```csharp
_handlers.Add(new DescriptorIngressHandler<LocalMapEntitySymbol>(
    _participant, _repo, "LocalMapEntitySymbol",
    data => data.EntityId,      // primary key
    data => data.MapGroupId));  // secondary key (for multi-part descriptor)
```

The `DerRepo` stores these as `repo.UpdateDescriptor<T>(entityId, groupId, sample)`.

### Error Isolation

The `IngressLoop` catches exceptions per handler:

```csharp
foreach (var handler in _handlers)
{
    try { handler.Poll(); }
    catch (Exception ex) { Console.WriteLine($"Error in handler: {ex.Message}"); }
}
```

This ensures a malformed sample on one topic (e.g., a corrupted `LocalGeoSpatial` that
throws during deserialization) does not stop `LocalEntityMaster` or other topics from
being processed in the same iteration. In production systems, the error should increment
a per-handler fault counter and trigger an alarm if the rate exceeds a threshold.

### Extending the Demo to a Production Ingress

```csharp
// Production pattern: inject DerRepo externally, add health monitoring:
public class ProductionIngress : IDisposable
{
    private readonly IDerRepo _repo;
    private readonly List<IIngressHandler> _handlers = new();
    private readonly ILogger _logger;
    private int _pollErrors;

    public ProductionIngress(
        DdsParticipant participant,
        IDerRepo repo,
        ILogger logger)
    {
        _repo   = repo;
        _logger = logger;

        // Core: entity lifecycle
        _handlers.Add(new MasterIngressHandler<EntityMasterMsg>(
            participant, repo, "EntityMaster",
            d => d.EntityId, d => d.TkbType));

        // Spatial
        _handlers.Add(new DescriptorIngressHandler<GeoSpatialMsg>(
            participant, repo, "GeoSpatial", d => d.EntityId));

        // ... add all production topics here
    }

    public void PollOnce()
    {
        foreach (var h in _handlers)
        {
            try { h.Poll(); }
            catch (Exception ex)
            {
                _pollErrors++;
                _logger.LogWarning(ex, "Ingress poll error #{Count}", _pollErrors);
            }
        }
    }

    public void Dispose() { /* dispose participant */ }
}
```
