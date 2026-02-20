# FDP.Toolkit.DER — Dynamic Entity Repository

A thread-safe, descriptor-based entity repository for bridging live DDS data into the application layer. DER (**D**ynamic **E**ntity **R**epository) provides a clean, type-generic model for storing and querying any number of DDS topic structs against a known entity, with built-in support for multi-part descriptors and a pluggable ingress pipeline.

---

## Key Concepts

### Entity
An entity is a uniquely identified simulation object (e.g. a vehicle, platform, or system), keyed by an integer `EntityId` and tagged with a `TkbType` (Template Knowledge Base type). Entities are managed by a central `IDerRepo`.

### Descriptor
A descriptor is any piece of data associated with an entity — typically a raw DDS-generated struct (e.g. `GeoSpatial`, `EntityMaster`). There is **no wrapper class** or interface required; any `struct` or `class` can be attached directly.

### Multi-Part Descriptor
Some DDS topics describe properties of individual sub-parts of an entity (e.g. compartment temperatures per section, or per-joint articulation). These are stored using the same `Type` key plus an integer `partId`, allowing multiple instances of the same descriptor type per entity.

---

## Core API

### `IDerRepo` — Entity Repository

```csharp
IDerTepo repo = new DerRepo();

// Create a new entity (throws if ID already exists)
IDerEntity entity = repo.CreateEntity(entityId: 42, tkbType: 100);

// Retrieve by ID (returns null if not found)
IDerEntity? entity = repo.GetEntity(42);

// Remove an entity
repo.DeleteEntity(42);

// Enumerate all
IEnumerable<IDerEntity> all = repo.GetAllEntities();

// Subscribe to lifecycle events
repo.EntityCreated += e => Console.WriteLine($"Created {e.EntityId}");
repo.EntityDeleted += e => Console.WriteLine($"Deleted {e.EntityId}");
```

### `IDerEntity` — Entity with Descriptors

```csharp
// Attach a raw struct descriptor (no wrapper needed)
entity.SetDescriptor(new GeoSpatial { EntityId = 42, Pos = ... });

// Retrieve it back (returns default if absent)
GeoSpatial geo = entity.GetDescriptor<GeoSpatial>();

// Check presence
if (entity.HasDescriptor<GeoSpatial>()) { ... }

// Multi-part: store separate instances by partId
entity.SetDescriptor(new CompartmentTemp { Value = 42.5f }, partId: 0);
entity.SetDescriptor(new CompartmentTemp { Value = 88.1f }, partId: 1);

CompartmentTemp front = entity.GetDescriptor<CompartmentTemp>(partId: 0);
CompartmentTemp rear  = entity.GetDescriptor<CompartmentTemp>(partId: 1);

// Query all distinct descriptor types on this entity
IEnumerable<Type> types = entity.GetAllDescriptorTypes();
```

---

## DDS Ingress Pipeline

The library ships with a ready-to-use polling pipeline that bridges any DDS topic into the repo without repetitive boilerplate.

### `IIngressHandler`

```csharp
public interface IIngressHandler
{
    void Poll();
}
```

Call `Poll()` from your application loop (e.g. every 5 ms) to drain available DDS samples.

### `MasterIngressHandler<T>` — Entity Lifecycle

Manages entity creation and deletion driven by a "master" topic (e.g. `EntityMaster`).  
Provide two lambdas to extract entity ID and TKB type from the struct:

```csharp
var master = new MasterIngressHandler<EntityMaster>(
    participant, repo, "EntityMaster",
    getEntityId: data => data.EntityId,
    getTkbType:  data => data.TkbType);
```

When a sample becomes `NotAliveDisposed`, the entity is automatically removed from the repo.

### `DescriptorIngressHandler<T>` — Descriptor Routing

Routes any DDS topic's data into the appropriate existing entity. Optionally supply a `getPartId` lambda for multi-part topics:

```csharp
// Single-part descriptor
var geoHandler = new DescriptorIngressHandler<GeoSpatial>(
    participant, repo, "GeoSpatial",
    getEntityId: data => data.EntityId);

// Multi-part descriptor
var mapSymbol = new DescriptorIngressHandler<MapEntitySymbol>(
    participant, repo, "MapEntitySymbol",
    getEntityId: data => data.EntityId,
    getPartId:   data => data.MapGroupId);  // partId comes from the struct itself
```

> **Note:** `DescriptorIngressHandler` silently drops samples for unknown entities. The assumption is that `MasterIngressHandler` is always polled first and entity lifecycle is established before any other descriptor arrives.

### Assembling the Pipeline

Collect all handlers into a list and poll them in a loop:

```csharp
var handlers = new List<IIngressHandler>
{
    new MasterIngressHandler<EntityMaster>(participant, repo, "EntityMaster",
        d => d.EntityId, d => d.TkbType),

    new DescriptorIngressHandler<GeoSpatial>(participant, repo, "GeoSpatial",
        d => d.EntityId),

    new DescriptorIngressHandler<GeoSpatialDR>(participant, repo, "GeoSpatialDR",
        d => d.EntityId),

    new DescriptorIngressHandler<EntityDamage>(participant, repo, "EntityDamage",
        d => d.EntityId),

    new DescriptorIngressHandler<MapEntitySymbol>(participant, repo, "MapEntitySymbol",
        d => d.EntityId, d => d.MapGroupId),

    // ... add one line per topic, however many you need
};

while (!cancellationToken.IsCancellationRequested)
{
    foreach (var h in handlers)
        h.Poll();

    Thread.Sleep(5);
}
```

Adding a 51st topic is a single additional registration line — nothing else changes.

---

## Design Notes

| Decision | Rationale |
|---|---|
| **No `IDerDescriptor` interface** | DDS-generated structs are plain value types; requiring them to implement an interface would break the generation pipeline and force wrapper allocations. |
| **`Tuple<Type, int>` dictionary key** | Supports multi-part descriptors with zero overhead. Single-part topics always use `partId = 0` by default. |
| **`object` boxing for storage** | Enables a single generic dictionary to hold any mix of struct and class descriptors. Boxing overhead is negligible at ingress rates typical for simulation data. |
| **Lambda-based key extraction** | Avoids reflection at runtime and keeps the library independent of any concrete DDS data model. |
| **`ConcurrentDictionary` throughout** | All repo and entity storage is thread-safe for multi-threaded ingress scenarios. |

---

## Project Structure

```
FDP.Toolkit.DER/
  IDerRepo.cs               — Repository interface
  IDerEntity.cs             — Entity + descriptor interface
  DerRepo.cs                — Thread-safe repository implementation
  DerEntity.cs              — Entity implementation with partId-keyed storage
  DdsIngressHandlers.cs     — IIngressHandler, MasterIngressHandler<T>, DescriptorIngressHandler<T>

FDP.Toolkit.DER.Tests/
  EntityRepoTests.cs        — Unit tests (9 tests, covering lifecycle, descriptors, multi-part, concurrency)

FDP.Toolkit.DER.Examples/
  EntityMasterIngressExample.cs  — End-to-end ingress pipeline example
```

---

## See Also

- `docs/design/TASK-DETAILS-SHARED.md` — Phase 3 task specifications
- `docs/design/DESIGN-SHARED.md` — High-level DER architecture
- `FDP.Toolkit.Commands` — Companion library for RPC-over-DDS command/acknowledge patterns
