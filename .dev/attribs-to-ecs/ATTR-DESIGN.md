# Design: Attributes-to-ECS — Zero-Allocation JSON Entity Patching

**Source:** [`design-talk.md`](./design-talk.md)  
**Status:** Ready for implementation  
**Date:** 2026-03-12

---

## 1. Problem Statement

### 1.1 The Monolithic Descriptor Overwrite Flaw

When an IOS operator wants to spawn an entity with a customised name (e.g. `"Bravo-1"`) using a
TKB template, the `CreationTool` currently must send a full `dtEntityInfo` descriptor inside
`CreateEntityRequest.InitialDescriptors`. Because `EntityInfo` is a monolithic struct, supplying
only the `Name` field forces the sender to also supply `ForceIdentifier`. If the IG doesn't know
the correct TKB default affiliation it sends `FORCE_UNKNOWN`, silently obliterating the correct
affiliation stored in the TKB template on the authoritative SimHost.

### 1.2 The Rigid Enum Discriminator Ceiling

Both `CreateEntityRequest` and `UpdateEntityAttributeRequest` rely on the same fixed
`EntityAttribute` enum and `EntityAttributePayload` discriminated union:

```csharp
public enum EntityAttribute { eaName, eaGeoPosition }
```

Adding a new settable property requires modifying the IDL enum, regenerating DDS serialisation
code, and updating the `EntityAttributeCompiler` switch statement — three coordinated changes
across the stack. Moreover, the enum is inherently flat: there is no way to express a deep path
such as `Weapons[2].Ammo.Count`.

The same limitation applies to live entity updates: an operator who wants to change `Name` on a
live entity must use `UpdateEntityAttributeRequest`, which also encodes the target field as the
same inflexible `EntityAttribute` enum. Deep weapon-state patches are structurally impossible
with the current wire format.

### 1.3 Zero-Allocation Mandate Violated

The `EntityAttributeCompiler.CompileOverrides` method allocates on every call:

```csharp
var result = new List<object>(baseComponents);  // always allocates
patched = new IgEntityData();                   // always allocates
```

The project's [CODE-STANDARDS §4](../../.dev-workstream/guides/CODE-STANDARDS.md) prohibits heap
allocations on the hot path for bulk entity operations (10 000+ spawns per burst). Both the
allocation of the result list and the individual component `new` calls violate this constraint.

### 1.4 Duplicate Mapping Logic

`DescriptorMapper.MapToComponents` and `EntityAttributeCompiler` independently implement
overlapping field-mapping logic (e.g. both handle the `GeoPosition`→`SimTransform` conversion).
Any change to coordinate math or component field layout must be applied in both places.

---

## 2. Current State (Baseline)

### 2.1 DDS Wire Format

```csharp
// Bagira.DDS.DataModel/GenericMessages.cs
public partial struct CreateEntityRequest
{
    public Guid RequestId;
    public NodeId Owner;
    public long Flags;
    [DdsManaged] public List<EntityDescriptorUnion> InitialDescriptors;
    [DdsManaged] public List<EntityAttributePayload>? InitialAttributes;  // ← to replace
}

public enum EntityAttribute { eaName, eaGeoPosition }   // ← fixed, flat enum

[DdsUnion] [DdsManaged]
public partial struct EntityAttributePayload
{
    [DdsDiscriminator] public EntityAttribute _d;
    [DdsCase(EntityAttribute.eaName)] public string Name;
    [DdsCase(EntityAttribute.eaGeoPosition)] public GeoPosition GeoPosition;
}
```

### 2.2 IG CreationTool (Emitting Side)

`Bagira.IG/Tools/CreationTool.cs` — `BuildAndPublishCreateRequest`:

- Parses `_initialPropertiesJson` to extract `name` and `affiliation`.
- Builds a full `dtEntityInfo` descriptor from those values.
- Builds a `dtGeoSpatial` descriptor from the map click.
- Populates `request.InitialDescriptors` with all three descriptors.
- `request.InitialAttributes` is **never populated** — the list mechanism is bypassed entirely.

### 2.3 EntityAttributeCompiler (SimHost / SimHost-adjacent)

`Bagira.Map.Common/Replication/Utils/EntityAttributeCompiler.cs`:

- `CompileOverrides(List<EntityAttributePayload>, List<object>, IGeographicTransform)` —  
  hardcoded `if (attr._d == EntityAttribute.eaName)` / `if (attr._d == EntityAttribute.eaGeoPosition)`.
- `CompileFromWorld(...)` — reads live ECS state for `IgEntityData` and `SimTransform` only.
- ✅ **Already correct:** Per-component compilation (the "overwrite flaw" is solved here).
- ❌ **Allocates:** Both a new `List<object>` and individual `new IgEntityData()` / `new SimTransform()`.

### 2.4 SimHost CreateEntityRequestSystem

`Bagira.SimHost/Systems/CreateEntityRequestSystem.cs` — `ProcessPendingRequest`:

```
1. DescriptorMapper.MapToComponents(InitialDescriptors)  →  base component list
2. EntityAttributeCompiler.CompileOverrides(InitialAttributes, base)  →  merged list
3. Unpack SimTransform / SimVelocity into typed SpawnEntityCommand fields
4. Bus.PublishManaged(SpawnEntityCommand)
```

### 2.5 EntityPropertyPatch DTO

```csharp
// Bagira.DDS.DataModel/EntityPropertyPatch.cs
public class EntityPropertyPatch
{
    public string? Name { get; set; }
    public eForceIdentifier? Affiliation { get; set; }
    public GeoPosition? GeoPosition { get; set; }
    public bool? AutogenerateName { get; set; }
    public string? NamePrefix { get; set; }
}
```

This POCO is serialized by the IOS to JSON, embedded inside
`MapCommandRequest.CommandArgsJson` as the `initialPropertiesJson` value, and deserialized by
the IG when activating the `CreationTool`.

### 2.6 UpdateEntityAttributeRequest — Current State

```csharp
// Bagira.DDS.DataModel/GenericMessages.cs
public partial struct UpdateEntityAttributeRequest
{
    public Guid RequestId;
    public int EntityId;
    public EntityAttribute AttributeId;   // ← fixed enum discriminator
    public EntityAttributePayload Payload; // ← binary union
}
```

`UpdateEntityAttributeRequestSystem` extracts the enum value and delegates to
`EntityAttributeCompiler.CompileFromWorld`, which reads the live ECS snapshot for the
affected entity, applies the single hardcoded field (name or geo-position), and writes the
patch back via `EntityComponentReflector.SetComponent`. Like the creation path, it is limited
to the two enum cases and performs heap allocations on every call.

---

## 3. Target Architecture

### 3.1 New Wire Field: `InitialAttributesJson`

Replace the fixed-enum `List<EntityAttributePayload>? InitialAttributes` with a single
`string? InitialAttributesJson` field in `CreateEntityRequest`:

```csharp
public partial struct CreateEntityRequest
{
    public Guid RequestId;
    public NodeId Owner;
    public long Flags;
    [DdsManaged] public List<EntityDescriptorUnion> InitialDescriptors;
    // NEW: replaces List<EntityAttributePayload>
    public string? InitialAttributesJson;
}
```

The JSON schema mirrors the existing `EntityPropertyPatch` POCO:

```json
{
  "Name": "Bravo-1",
  "Affiliation": "FORCE_FRIENDLY",
  "GeoPosition": { "Latitude": 32.1, "Longitude": 34.8, "Altitude": 0 }
}
```

This is the same JSON the IOS already produces and the IG already receives as
`initialPropertiesJson`. The IG becomes a **dumb pipe** — it forwards the JSON directly without
parsing it into descriptor fields.

### 3.2 IG CreationTool — Dumb Pipe

The `CreationTool` no longer parses `initialPropertiesJson` fields into `EntityInfo` values.
It emits only the two structurally mandatory descriptors and forwards the JSON verbatim:

```
InitialDescriptors:
  [0] dtEntityMaster (TkbType)
  [1] dtGeoSpatial   (click position)
InitialAttributesJson = _initialPropertiesJson   ← raw forward, no parsing
```

The `dtEntityInfo` descriptor is **removed** from `CreationTool`. All name/affiliation
delivery shifts to the JSON attribute path processed by the SimHost's `JsonAttributeCompiler`.

The private helpers `ParseNameFromJson` and `ParseAffiliationFromJson` are removed from
`CreationTool`. The `_affiliationForDisplay` field used to draw the ghost colour must derive
from the same `ParseAffiliationFromJson` helper moved or kept inline for the UI ghost only
(not the spawning path).

### 3.3 Zero-Allocation JSON Attribute Compiler

A new `JsonAttributeCompiler` class in `Bagira.Map.Common/Replication/Utils/` replaces the
existing `EntityAttributeCompiler`. It processes the `InitialAttributesJson` string without any
managed heap allocations on the hot path.

#### 3.3.1 Streaming via `Utf8JsonReader`

`System.Text.Json.Utf8JsonReader` is a `ref struct` that scans UTF-8 bytes token-by-token
entirely on the thread stack. No `JsonDocument`, no `JsonNode`, no strings are created.

#### 3.3.2 Unmanaged State Machine (`stackalloc`)

Three lightweight arrays are allocated on the stack at the start of each `Compile` call:

```csharp
Span<ulong> hashStack  = stackalloc ulong[MaxDepth];   // parent hash at each depth
Span<int>   indexStack = stackalloc int[MaxDepth];     // array index at each depth
int depth = 0;
ulong currentHash = FnvOffset;
```

#### 3.3.3 Incremental FNV-1a Path Hashing

As the reader advances token-by-token, the path hash is accumulated via FNV-1a:

| Token | Action |
|-------|--------|
| `PropertyName` (string) | Hash UTF-8 bytes of the name into `currentHash` |
| `PropertyName` (numeric) | Parse integer, push to `indexStack`, hash wildcard token `*` |
| `StartObject` | `hashStack[++depth] = currentHash` |
| `EndObject` | `currentHash = hashStack[depth--]` |

This means `"Weapons"."0"."Ammo"."Count"` produces the same hash as
`"Weapons"."1"."Ammo"."Count"` (both use `*` for the index), and the actual integer `0` or `1`
is recovered from `indexStack` when invoking the delegate.

#### 3.3.4 Routing Table Lookup

A `Dictionary<ulong, RoutingEntry>` built at startup maps each final path hash to the correct
ECS component type and its pre-compiled setter delegate.

No string allocations occur during lookup — the hash `ulong` is the key.

### 3.4 Dual-Mode Pre-Compiled Delegates

The C# type system distinguishes value types (`struct`) and reference types (`class`).
The routing registry uses two strongly-typed delegate signatures to avoid boxing:

```csharp
// For unmanaged struct components (e.g. SimTransform):
public delegate void ValueAttributeSetter<T>(
    ref T component,
    ReadOnlySpan<int> indices,
    ref Utf8JsonReader reader) where T : struct;

// For managed class components (e.g. IgEntityData):
public delegate void ReferenceAttributeSetter<T>(
    T component,
    ReadOnlySpan<int> indices,
    ref Utf8JsonReader reader) where T : class;
```

Delegates are compiled once at application startup using `System.Linq.Expressions`, producing
native-like IL that does not box value types.

### 3.5 AttributeCompilerBuilder API

```csharp
var compiler = new AttributeCompilerBuilder()
    // IgEntityData is a class component → reference setter
    .RegisterReferencePath<IgEntityData>("Name",
        (IgEntityData c, ReadOnlySpan<int> _, ref Utf8JsonReader r) =>
            c.Name = r.GetString() ?? string.Empty)

    .RegisterReferencePath<IgEntityData>("Affiliation",
        (IgEntityData c, ReadOnlySpan<int> _, ref Utf8JsonReader r) =>
            c.ForceId = MapAffiliation(r.GetString()))

    // SimTransform is a struct component → value setter (ref T)
    .RegisterValuePath<SimTransform>("GeoPosition",
        (ref SimTransform c, ReadOnlySpan<int> _, ref Utf8JsonReader r) =>
            c = ApplyGeoJson(ref c, ref r, _geoTransform))

    .Build();
```

`AttributeCompilerBuilder.RegisterValuePath<T>` and `RegisterReferencePath<T>` hash the
supplied `jsonPath` string at registration time; the runtime path never calls `string.GetHashCode`.

### 3.6 IEntityPatchContext and EcsPatchContext

The compiler accesses components through an `IEntityPatchContext` abstraction so it works
identically during entity spawning (baseline from `DescriptorMapper` output) and during live
attribute updates (baseline from the ECS world):

```csharp
public interface IEntityPatchContext
{
    ref T GetUnmanagedComponent<T>() where T : struct;
    void MarkUnmanagedDirty<T>() where T : struct;
    T GetManagedComponent<T>() where T : class;
    void MarkManagedDirty<T>() where T : class;
}
```

Two concrete implementations:

| Class | Used during |
|-------|-------------|
| `ListPatchContext` | Entity creation — baseline from `List<object>` (DescriptorMapper output) |
| `EcsPatchContext` | Live attribute update — baseline from live `EntityRepository` |

`EcsPatchContext` wraps `EntityRepository` and `Entity`:

- `GetUnmanagedComponent<T>` → `_repo.GetComponentRW<T>(_entity)` (returns `ref T`; already stamps `LastChangeTick`)
- `GetManagedComponent<T>` → `_repo.GetManagedComponentRO<T>(_entity)` (returns mutable reference)
- `MarkManagedDirty<T>` → `SmartEgressUtil.MarkDirty(repo, entity, ordinal)` triggers reliable egress

`ListPatchContext` operates over the `List<object>` from `DescriptorMapper.MapToComponents`:

- `GetUnmanagedComponent<T>` → find `T` in the list or return `default(T)`, track it internally
- Yields the accumulated patched list via `FlushComponents()` at the end

### 3.7 Creation Path Integration (SimHost)

`CreateEntityRequestSystem.ProcessPendingRequest` processes the three-step sequence:

```
1. DescriptorMapper.MapToComponents(InitialDescriptors, geoTransform)  → baseComponents
2. JsonAttributeCompiler.Compile(InitialAttributesJson, ListPatchContext(baseComponents))
   → no new List<object> allocated; mutations applied in-place within ListPatchContext
3. Unpack SimTransform / SimVelocity for SpawnEntityCommand typed fields
4. Bus.PublishManaged(SpawnEntityCommand)
```

### 3.8 UpdateEntityAttributeRequest — Wire Format Change

To complete the unification, `UpdateEntityAttributeRequest` also replaces its enum/union fields
with a single JSON string, eliminating `EntityAttribute` and `EntityAttributePayload` entirely:

```csharp
[DdsTopic("UpdateEntityAttributeRequest")]
public partial struct UpdateEntityAttributeRequest
{
    public Guid RequestId;
    public int EntityId;
    // Replaces AttributeId + Payload.
    // e.g. { "Name": "Bravo-2" }  or  { "Weapons": { "0": { "Ammo": { "Count": 10 } } } }
    public string AttributePatchJson;
}
```

With both messages using JSON strings, the same `JsonAttributeCompiler` routing table and
delegate set serves both entity creation and live entity updates — a single source of truth.

### 3.9 Live Update Path Integration

`UpdateEntityAttributeRequestSystem` feeds the incoming JSON string through the same
`JsonAttributeCompiler` using an `EcsPatchContext`:

```
1. Resolve entity via NetworkEntityMap (EntityId → Entity)
2. Build EcsPatchContext(repo, entity)
3. JsonAttributeCompiler.Compile(request.AttributePatchJson, ecsPatchContext)
   → Utf8JsonReader streams JSON (zero alloc)
   → Per component: lazy-load from ECS exactly once, mutate in place
4. EcsPatchContext.FlushDirtyMarks()
   → SmartEgressUtil.MarkDirty called for every touched component (see §3.10)
```

### 3.10 Chunk-Tick Egress Correction

**Architectural flaw in the initial design:** The `EcsPatchContext` description in §3.6 stated
that `GetUnmanagedComponent<T>` via `GetComponentRW<T>` "already stamps `LastChangeTick`".
This is incorrect at the required granularity.

In the FDP archetype-based ECS, `GetComponentRW<T>` bumps the version tick for the entire
**memory chunk** — which may contain dozens or hundreds of entities of the same archetype.
If one tank's `WeaponState` is patched, the chunk tick increments for the whole group. Any
egress translator that uses chunk-level change detection would broadcast weapon updates for
all tanks in that chunk, causing massive false-positive network traffic.

The codebase already handles this correctly for existing translators using two strategies:

| Strategy | Used for | Mechanism |
|----------|----------|-----------|
| Shadow comparison | High-frequency unmanaged data (`SimTransform`) | `GeoSpatialEgressTranslator` compares actual position delta per entity each frame |
| Explicit dirty flag | Low-frequency reliable data (`IgEntityData`) | `SmartEgressUtil.MarkDirty(repo, entity, ordinal)` writes to `EgressPublicationState.DirtyDescriptors` |

**Corrected `EcsPatchContext` contract:** After applying mutations via registered delegates,
the context must call `SmartEgressUtil.MarkDirty` for **every component type it touched**,
for **both managed and unmanaged** components. The caller supplies a component-to-ordinal
mapping at construction time:

```csharp
// Each registered path carries the descriptor ordinal of its target component:
public class AttributeCompilerBuilder
{
    public AttributeCompilerBuilder RegisterReferencePath<T>(
        string jsonPath,
        ReferenceAttributeSetter<T> setter,
        long descriptorOrdinal) where T : class;  // ← ordinal for SmartEgress

    public AttributeCompilerBuilder RegisterValuePath<T>(
        string jsonPath,
        ValueAttributeSetter<T> setter,
        long descriptorOrdinal) where T : struct;  // ← ordinal for SmartEgress
}
```

Each `RoutingEntry` stores its `descriptorOrdinal`. When `EcsPatchContext.FlushDirtyMarks()`
is called at the end of compilation, it iterates the set of touched component types and calls
`SmartEgressUtil.MarkDirty(repo, entity, entry.DescriptorOrdinal)` once per distinct ordinal.

This bypasses chunk-level ticks entirely, guaranteeing per-entity precision on the egress side.
`ListPatchContext` (spawn-time context) has no-op implementations of `MarkDirty` — egress is
always driven by the spawning pipeline, not the attribute compiler.

### 3.11 Unified Descriptor Routing (Phase 6 — Advanced)

As an optional unification step, the same pre-compiled delegates from the routing table can be
reused by `DescriptorMapper` so the field-mapping logic is defined once:

| Path expression | Registered delegate | Used by |
|-----------------|--------------------|---------| 
| `"Name"` | `IgEntityData.Name` setter | JsonCompiler + DescriptorMapper `dtEntityInfo` |
| `"Affiliation"` | `IgEntityData.ForceId` setter | JsonCompiler + DescriptorMapper `dtEntityInfo` |
| `"GeoPosition"` | `SimTransform` + WGS84 conversion | JsonCompiler + DescriptorMapper `dtGeoSpatial` |

`DescriptorMapper.dtEntityInfo` case becomes:

```csharp
case EDescriptorType.dtEntityInfo:
    _compiler.ApplyDescriptorFields(context, new[]
    {
        ("Name",        d.EntityInfo.Name),
        ("Affiliation", d.EntityInfo.ForceIdentifier.ToString()),
    });
    break;
```

---

## 4. Key Design Constraints

| Constraint | Source | Applies To |
|-----------|--------|-----------|
| Zero heap allocation on hot path | CODE-STANDARDS §4 | `JsonAttributeCompiler.Compile`, delegate execution |
| Per-component compilation (overwrite-flaw prevention) | Design Talk | `ListPatchContext`, `EcsPatchContext` |
| Baseline from list when spawning; baseline from ECS when updating | Gap 2 in design talk | `IEntityPatchContext` implementations |
| Array bounds checked in delegates | Gap 3 in design talk | All registered `RegisterValuePath` for indexed components |
| Struct components passed by `ref`; class components by reference | C# semantics | `ValueAttributeSetter<T>` / `ReferenceAttributeSetter<T>` |
| No chunk-tick reliance for egress — `SmartEgressUtil.MarkDirty` explicit for **all** mutations | Design Talk §3.10 | `EcsPatchContext.FlushDirtyMarks`, `RoutingEntry.DescriptorOrdinal` |
| Both `CreateEntityRequest` and `UpdateEntityAttributeRequest` carry JSON strings | Design Talk update | Phase 1 DDS changes |

---

## 5. Phases and Tasks

| Phase | Goal |
|-------|------|
| [Phase 1](#phase-1-dds-api-migration) | Replace enum/union list with JSON string in **both** `CreateEntityRequest` and `UpdateEntityAttributeRequest` |
| [Phase 2](#phase-2-ig-pipe-simplification) | `CreationTool` becomes a dumb pipe; JSON forwarded verbatim |
| [Phase 3](#phase-3-zero-allocation-compiler-core) | `JsonAttributeCompiler` with `Utf8JsonReader` · `stackalloc` · FNV-1a |
| [Phase 4](#phase-4-pre-compiled-delegate-registry) | `AttributeCompilerBuilder` · dual-mode delegates · `IEntityPatchContext` · descriptor ordinal param |
| [Phase 5](#phase-5-registration-and-integration) | Register component paths + ordinals; wire compiler into both SimHost systems |
| [Phase 6](#phase-6-unified-descriptor-routing-advanced) | Optional: share delegates between JSON compiler and `DescriptorMapper` |

---

## Phase 1: DDS API Migration

**Goal:** Change the wire format on **both** request messages so attribute overrides are carried
as JSON strings instead of discriminated-union payloads.

**Files:**
- `Bagira.DDS.DataModel/GenericMessages.cs`

**Changes:**
1. In `CreateEntityRequest`: replace `[DdsManaged] public List<EntityAttributePayload>? InitialAttributes;`
   with `public string? InitialAttributesJson;`.
2. In `UpdateEntityAttributeRequest`: replace `public EntityAttribute AttributeId;` and
   `public EntityAttributePayload Payload;` with `public string AttributePatchJson;`.
3. Remove the `EntityAttribute` enum and `EntityAttributePayload` DDS union entirely from
   `GenericMessages.cs` once both messages are migrated (no other message references them).

---

## Phase 2: IG Pipe Simplification

**Goal:** The `CreationTool` stops parsing `initialPropertiesJson` into a `dtEntityInfo` descriptor
and instead forwards the raw JSON into `InitialAttributesJson`.

**Files:**
- `Bagira.IG/Tools/CreationTool.cs`

**Changes:**
1. Remove the `dtEntityInfo` `EntityDescriptorUnion` from the `InitialDescriptors` list built in
   `BuildAndPublishCreateRequest`.
2. Remove calls to `ParseNameFromJson(_initialPropertiesJson)` from the spawning path.
3. Assign `InitialAttributesJson = _initialPropertiesJson` on the request.
4. Retain `ParseAffiliationFromJson` for the **ghost rendering only** (used to set
   `_affiliationForDisplay` in the constructor — display only, not spawn data).
5. Remove `ParseNameFromJson` helper entirely since name is no longer used on the IG-side spawn path.

---

## Phase 3: Zero-Allocation Compiler Core

**Goal:** Implement the low-level `Utf8JsonReader` streaming + `stackalloc` state machine.

**New file:** `Bagira.Map.Common/Replication/Utils/JsonAttributeCompiler.cs`

**Responsibilities:**
- Accept a `string? json` and an `IEntityPatchContext context` parameter.
- Convert the string to `ReadOnlySpan<byte>` (via `Encoding.UTF8.GetBytes` or cached buffer).
- Stream tokens via `Utf8JsonReader`.
- Maintain `depth`, `hashStack`, `indexStack` on the stack via `stackalloc`.
- Compute FNV-1a hash incrementally for each `PropertyName` token; push/pop on `StartObject`/`EndObject`.
- Numeric property names are normalised to a wildcard token before hashing; actual integer pushed to `indexStack`.
- On primitive value token: look up `currentHash` in the routing table and invoke the delegate.

**Constants:**
```csharp
private const int MaxDepth = 16;
private const ulong FnvOffset = 14695981039346656037UL;
private const ulong FnvPrime  = 1099511628211UL;
private static readonly byte[] Wildcard = Encoding.UTF8.GetBytes("*");
```

---

## Phase 4: Pre-Compiled Delegate Registry

**Goal:** Define the delegate types, `AttributeCompilerBuilder`, and `IEntityPatchContext`.

**New files:**
- `Bagira.Map.Common/Replication/Utils/IEntityPatchContext.cs`
- `Bagira.Map.Common/Replication/Utils/AttributeCompilerBuilder.cs`
- `Bagira.Map.Common/Replication/Utils/ListPatchContext.cs`
- `Bagira.Map.Common/Replication/Utils/EcsPatchContext.cs`

**Key types:**

```csharp
// Delegate for unmanaged struct components:
public delegate void ValueAttributeSetter<T>(
    ref T component, ReadOnlySpan<int> indices, ref Utf8JsonReader reader) where T : struct;

// Delegate for managed class components:
public delegate void ReferenceAttributeSetter<T>(
    T component, ReadOnlySpan<int> indices, ref Utf8JsonReader reader) where T : class;

public interface IEntityPatchContext
{
    ref T GetUnmanagedComponent<T>() where T : struct;
    void MarkUnmanagedDirty<T>() where T : struct;
    T GetManagedComponent<T>() where T : class;
    void MarkManagedDirty<T>() where T : class;
}
```

`AttributeCompilerBuilder` hashes the `jsonPath` string at `Register*` call time and stores
entries in an internal `Dictionary<ulong, RoutingEntry>`.

---

## Phase 5: Registration and Integration

**Goal:** Register all current ECS property paths (with descriptor ordinals for egress); wire the
new `JsonAttributeCompiler` into both SimHost request-handling systems.

**Files:**
- `Bagira.SimHost/Systems/CreateEntityRequestSystem.cs`
- `Bagira.Map.Common/Systems/UpdateEntityAttributeRequestSystem.cs`
- `Bagira.SimHost/SimHostApp.cs` (compiler construction, ordinal mapping, dependency injection)
- `Bagira.Map.Common/Replication/Utils/EntityAttributeCompiler.cs` (keep existing as wrapper
  or deprecate once compiler is fully wired)

**Property paths to register initially (with descriptor ordinals):**

| JSON path | Component | Field (C#) | Setter type | Descriptor ordinal |
|-----------|-----------|-----------|------------|-------------------|
| `"Name"` | `IgEntityData` (class) | `.Name` | `ReferenceAttributeSetter<IgEntityData>` | `(long)EDescriptorType.dtEntityInfo` |
| `"Affiliation"` | `IgEntityData` (class) | `.ForceId` | `ReferenceAttributeSetter<IgEntityData>` | `(long)EDescriptorType.dtEntityInfo` |
| `"GeoPosition.Latitude"` + `"GeoPosition.Longitude"` + `"GeoPosition.Altitude"` | `SimTransform` (struct) | `.Position` via `IGeographicTransform` | `ValueAttributeSetter<SimTransform>` | `(long)EDescriptorType.dtGeoSpatial` |

`EcsPatchContext.FlushDirtyMarks()` deduplicates ordinals so `dtEntityInfo` is only passed to
`SmartEgressUtil.MarkDirty` once even when both `Name` and `Affiliation` are patched in the
same JSON string.

---

## Phase 6: Unified Descriptor Routing (Advanced)

**Goal:** `DescriptorMapper` reuses the same compiled delegates; field-mapping logic is defined
in one place.

**Files:**
- `Bagira.Map.Common/Replication/Utils/DescriptorMapper.cs`

**Change:** `dtEntityInfo` and `dtGeoSpatial` cases delegate to the routing table instead of
hand-coding field assignments. This is optional for the initial delivery and can follow as a
clean-up task.

---

## 6. Data Flow Summary

### Spawn path (CreateEntityRequest)

```
IOS (Operator)
  └─ EntityPropertyPatch { Name="Bravo-1", Affiliation="FORCE_FRIENDLY" }
       │  serialised to JSON
       ▼
  MapCommandRequest.CommandArgsJson["initialPropertiesJson"]
       │  forwarded by IG unchanged
       ▼
  CreateEntityRequest
    ├─ InitialDescriptors: [dtEntityMaster, dtGeoSpatial]  ← mandatory
    └─ InitialAttributesJson: "{\"Name\":\"Bravo-1\",\"Affiliation\":\"FORCE_FRIENDLY\"}"
       │
       ▼  SimHost: CreateEntityRequestSystem.ProcessPendingRequest
  DescriptorMapper.MapToComponents(InitialDescriptors)
    → ListPatchContext seeded with [SimTransform]
       │
       ▼  JsonAttributeCompiler.Compile(InitialAttributesJson, context)
  Utf8JsonReader streams JSON (zero alloc)
  FNV-1a hash lookup → ReferenceAttributeSetter<IgEntityData> for "Name"
  IgEntityData.Name = "Bravo-1"
  FNV-1a hash lookup → ReferenceAttributeSetter<IgEntityData> for "Affiliation"
  IgEntityData.ForceId = ForceId.Friend
       │
       ▼  ListPatchContext.FlushComponents()  [MarkDirty is no-op for spawn]
  SpawnEntityCommand { InitialTransform=SimTransform, InitialComponents=[IgEntityData] }
       │
       ▼  NetworkSpawningSystem
  ECS entity created with correct Name + Affiliation, TKB defaults preserved
```

### Live-update path (UpdateEntityAttributeRequest)

```
IOS / IG (Operator)
  └─ "{\"Name\":\"Bravo-2\"}"  (or deep path: {\"Weapons\":{\"0\":{\"Ammo\":{\"Count\":5}}}})
       │  sent in UpdateEntityAttributeRequest.AttributePatchJson
       ▼
  UpdateEntityAttributeRequestSystem.Execute()
    1. NetworkEntityMap.TryGetEntity(EntityId) → Entity
    2. EcsPatchContext(repo, entity)            ← baseline = live ECS
    3. JsonAttributeCompiler.Compile(AttributePatchJson, context)
       → Utf8JsonReader streams JSON (zero alloc, same routing table)
       → ReferenceAttributeSetter<IgEntityData> for "Name"
         context.GetManagedComponent<IgEntityData>()  [lazy-loaded once]
         IgEntityData.Name = "Bravo-2"
    4. context.FlushDirtyMarks()
       → SmartEgressUtil.MarkDirty(repo, entity, dtEntityInfo ordinal)
         [chunks NOT used — per-entity precision guaranteed]
       ▼
  GeoSpatialEgressTranslator or EntityInfoEgressTranslator
  picks up dirty flag on next tick → broadcasts EntityInfo over DDS
```
