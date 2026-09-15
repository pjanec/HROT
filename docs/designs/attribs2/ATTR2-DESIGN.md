# Design: ATTR2 — Binary Attribute Pipeline

**Source:** [`design_talk.md`](./design_talk.md)  
**Predecessor:** [`../attribs-to-ecs/ATTR-DESIGN.md`](../attribs-to-ecs/ATTR-DESIGN.md)  
**Status:** Ready for implementation  
**Date:** 2026-03-13

---

## 1. Problem Statement

The ATTR workstream (now complete) replaced the rigid `EntityAttribute` enum–based wire format
with a zero-allocation JSON compiler (`JsonAttributeCompiler`) that parses arbitrary key-path
strings from `InitialAttributesJson` / `AttributePatchJson` and routes them to ECS components via
pre-compiled FNV-1a hashed delegates.  That work solved the "overwrite flaw", the "rigid enum
ceiling", and most of the zero-allocation mandate.

Three residual problems remain.

### 1.1 SimHost Parses JSON on the Hot Path

`CreateEntityRequestSystem` and `UpdateEntityAttributeRequestSystem` still receive raw UTF-8
JSON strings from the network and feed them through `JsonAttributeCompiler`.  On a burst of
10 000 spawns, or at 60 Hz live-attribute updates for hundreds of entities, the UTF-8 parsing and
FNV-1a hashing loops constitute measurable CPU overhead that should be eliminated from the
SimHost simulation tick entirely.

### 1.2 High Bandwidth (JSON Verbosity)

A JSON attribute patch such as `{"GeoPoint.Latitude":32.085}` is ~34 bytes.  The equivalent
binary record (2-byte id + 2-byte sub-index + 8-byte double) is 12 bytes — a **~65 % reduction**.
For live entity tracking scenarios streaming position updates to hundreds of entities, this
difference is significant.

### 1.3 No Path to Deep Array Indexing in JSON

The ATTR JSON compiler supports the wildcard `*` syntax for registered array routes (e.g.
`"Weapons.*.Ammo.Count"`), but the JSON sent over the wire must be structured to match.  Binary
sub-indices cleanly fill this gap: `AttributeId=50, SubIndex1=2` means "weapon slot 2, ammo
count", with zero parsing ambiguity.

---

## 2. Approach Overview

Keep the existing `JsonAttributeCompiler` pipeline intact (backward-compatible, useful for
tooling and debugging).  Add a parallel **binary attribute pipeline** consisting of two new
components:

```
IOS / IG client                    SimHost
─────────────────                  ────────────────────────────────────────────
                                   
  JSON (human-editable)            AttributeRecord[]  (binary, on DDS wire)
         │                                  │
   ┌─────▼──────┐             ┌─────────────▼──────────────┐
   │ EdgeCompiler│             │   BinaryInterpreter        │
   │ (JSON→Records)│           │   O(1) dispatch table      │
   └─────┬──────┘             │   Installer-based handlers │
         │                    │   Scratchpad grouping       │
         │  DDS               │   Authority checks          │
         └──────────────────► │   SmartEgress dirty-marks  │
                              └────────────────────────────┘
```

The IOS / IG placement tool converts JSON to `AttributeRecord[]` using the `EdgeCompiler`
**before** sending the DDS message.  The SimHost never sees a JSON string in the binary
pipeline; it only processes structured binary records.

---

## 3. Component Designs

### 3.1 Binary Wire Contract (`AttributeRecord`)

A new pair of DDS-compatible C# structs is added to `Hrot.NED/GenericMessages.cs`.

**`AttributeValueUnion`** carries the value as an extended primitive.  Supported value types:

| Tag | C# type | Notes |
|-----|---------|-------|
| `Int32` | `int` | General integer |
| `Int64` | `long` | Large integers / IDs |
| `Float32` | `float` | Euler angles, RPM, etc. |
| `Float64` | `double` | Geo-coordinates, high-precision values |
| `Bool` | `bool` | Flags |
| `String` | `string` | Entity names, identifiers (managed) |
| `Vec3f` | `float[3]` | Heading/pitch/roll, local Cartesian offsets |
| `Vec3d` | `double[3]` | WGS-84 lat/lon/alt, world-space positions |
| `Vec4f` | `float[4]` | Float quaternions, RGBA colour |

`AttributeRecord` is the packet atom:

```csharp
// Hrot.NED/GenericMessages.cs
public struct AttributeRecord
{
    public ushort AttributeId;   // maps to the well-known schema table
    public short  SubIndex1;     // first array index (0 = not applicable)
    public short  SubIndex2;     // second array index (nested array)
    public AttributeValueUnion Value;
}
```

Both `CreateEntityRequest` and `UpdateEntityAttributeRequest` gain a new binary attribute
list field.  The JSON string fields are **retained** for backward-compat but new senders should
prefer the binary list.

```csharp
public partial struct CreateEntityRequest
{
    // ... existing fields ...
    [DdsManaged] public string? InitialAttributesJson;       // retained (legacy)
    [DdsManaged] public List<AttributeRecord>? InitialAttributeRecords; // new
}

public partial struct UpdateEntityAttributeRequest
{
    // ... existing fields ...
    [DdsManaged] public string AttributePatchJson;           // retained (legacy)
    [DdsManaged] public List<AttributeRecord>? AttributeRecords; // new
}
```

The SimHost processes `AttributeRecords` when non-null and non-empty, falling back to the JSON
field otherwise.  This allows a staged rollout.

### 3.2 Edge Compiler (`JsonToRecordCompiler`)

Lives in `FDP.Toolkit.Replication.Patching`.

Converts a JSON attribute patch (flat or hierarchically nested) to a
`Span<AttributeRecord>` buffer without heap allocations on the hot path.

**Key design points:**

- Accepts `ReadOnlySpan<byte>` (not `string`); callers avoid encoding allocation.
- Uses `Utf8JsonReader` with a `stackalloc PathSegment[16]` stack to handle both formats:
  - **Flat**: `{ "GeoPoint.Latitude": 32.0 }` — the property key is tokenised inline.
  - **Nested**: `{ "GeoPoint": { "Latitude": 32.0 } }` — the stack accumulates segments.
  - **Array with integer-keyed children**: `{ "Weapon": { "2": { "Ammo": 5 } } }` — an
    integer key is extracted as `SubIndex1`.
- The routing table maps `ulong` FNV-1a path hashes → `EdgeSchemaEntry` (AttributeId + expected
  type).  Hashing is done at registration time (build) exactly like `AttributeCompilerBuilder`.
- Output is written into a caller-supplied `Span<AttributeRecord>`.  Returns the count of records
  emitted.  Callers typically use `ArrayPool<AttributeRecord>.Shared` or `stackalloc`.

**Builder:**

```csharp
// FDP.Toolkit.Replication.Patching.JsonToRecordCompilerBuilder
builder
    .Register("Name",                   AttributeId.Name,             AttributeValueType.String)
    .Register("GeoPoint.Latitude",   AttributeId.GeoLat,           AttributeValueType.Float64)
    .Register("GeoPoint.Longitude",  AttributeId.GeoLon,           AttributeValueType.Float64)
    .Register("GeoPoint.Altitude",   AttributeId.GeoAlt,           AttributeValueType.Float64)
    .Register("Weapon.*.Ammo",          AttributeId.WeaponAmmo,       AttributeValueType.Int32);

JsonToRecordCompiler compiler = builder.Build();
```

Domain-specific schema registration is encapsulated in a factory class (analogous to
`AttributeCompilerFactory`), preserving the same separation of concerns.

### 3.3 Binary Interpreter (`BinaryInterpreter`)

Lives in `FDP.Toolkit.Replication.Patching`.

Applies a `ReadOnlySpan<AttributeRecord>` to live ECS components (live update path) or to staged
pre-spawn component structs (creation path).

**Architecture: Installer Pattern**

The interpreter is not hard-coded to any component type.  Domain code registers handlers at
startup via `IBinaryAttributeInstaller` implementations:

```csharp
public interface IBinaryAttributeInstaller
{
    void Install(BinaryInterpreterBuilder builder);
}
```

Examples:
- `EntityDataAttributeInstaller` — registers handlers for `Name`, `Affiliation`
- `SimTransformAttributeInstaller` — registers handlers for `GeoLat`, `GeoLon`, `GeoAlt` with
  scratchpad-based grouping

**Dispatch table:**  
A flat `delegate*<ref BinaryPatchContext, in AttributeRecord, void>[]` array of size 1024 (or
`ushort.MaxValue` if IDs will exceed 1024).  `AttributeId` is the array index → O(1) lookup with
no branching.

**`BinaryPatchContext`:**\
A lean context object (not a `ref struct` for now, given managed-component compatibility) that:

- Holds a reference to `EntityRepository` and the target `Entity`.
- Provides `GetUnmanagedComponent<T>()` and `GetManagedComponent<T>()` matching the existing
  `IEntityPatchContext` contract (so `ListPatchContext` / `EcsPatchContext` semantics can be
  reused without duplication).
- Exposes a raw `byte[]` scratchpad block (pre-allocated, installer-specific slots) for
  grouping logic.
- Tracks a `uint DirtySubsystemsMask` bitmask; individual installers flip bits to request
  deferred flush handlers (e.g. "run GeodeticToCartesian once at end").
- Tracks a `ulong DirtyDescriptorMask` for `SmartEgressUtil.MarkDirty`.

**Scratchpad slots mechanism:**  
Each installer reserves a fixed byte-offset into the shared scratchpad at build/install time
(analogous to a struct layout).  The installer casts the span slice to its typed scratchpad struct
via `MemoryMarshal.Cast`.  This is allocation-free and thread-safe (context is per-request).

**Authority check:**  
Performed lazily inside each handler: the handler calls `ctx.Repo.HasAuthority(entity, ComponentType)` before touching component memory (identical to the `CanWrite<T>` guard in
`ValueInvoker<T>`).  Unrecognised or unauthorized records are skipped in O(1).

**Flush phase:**  
After all records are processed, the interpreter iterates only the set bits in
`DirtySubsystemsMask` and calls the registered `SubsystemFlusher` for each — in installer-
registration order.  Each flusher reads the scratchpad and finalises the ECS write (e.g.
computing `GeodeticToCartesian` once and writing `SimTransform.Position`).

**SmartEgress:**  
After flush, `SmartEgressUtil.MarkDirty` is called for each distinct ordinal in
`DirtyDescriptorMask` — identical to the existing `EcsPatchContext.FlushDirtyMarks` contract.

### 3.4 Attribute ID Schema

A static well-known table shared between the Edge Compiler and the Binary Interpreter to
ensure the two components agree on IDs.

```csharp
// FDP.Toolkit.Replication.Patching
public static class AttributeId
{
    public const ushort Name            = 1;
    public const ushort Affiliation     = 2;
    public const ushort GeoLat          = 10;
    public const ushort GeoLon          = 11;
    public const ushort GeoAlt          = 12;
    // ... expandable by domain modules
}
```

Numeric ranges are reserved per subsystem to avoid collisions when modules are added.

### 3.5 `CreateEntityRequestSystem` Changes

- Accepts the injected `BinaryInterpreter` (optional, alongside the existing `JsonAttributeCompiler`).
- When `request.InitialAttributeRecords` is non-null and non-empty, applies them via
  `BinaryInterpreter` using a `ListPatchContext`-equivalent staged context (pre-spawn, no live
  ECS entity yet). Falls back to `InitialAttributesJson` + `JsonAttributeCompiler` when the
  binary list is absent.

### 3.6 `UpdateEntityAttributeRequestSystem` Changes

- Accepts the injected `BinaryInterpreter` (optional).
- When `request.AttributeRecords` is non-null and non-empty, applies them via
  `BinaryInterpreter` on the live `EcsPatchContext`-equivalent.  Falls back to
  `AttributePatchJson` + `JsonAttributeCompiler`.

### 3.7 `CreationTool` (IG Side) Changes

- `CreationTool` (in `Hrot.IG/Tools/`) is injected with a `JsonToRecordCompiler`.
- Before publishing `CreateEntityRequest`, it calls `compiler.Compile(utf8Json, buffer)` to
  convert `_initialPropertiesJson` into binary records.
- Sets `request.InitialAttributeRecords` with the result, and leaves `InitialAttributesJson`
  null (or populated for backward-compat with older SimHost nodes).

### 3.8 Keeping the JSON Pipeline

`JsonAttributeCompiler`, `AttributeCompilerBuilder`, `ListPatchContext`, and `EcsPatchContext`
are **not modified**.  The binary pipeline is entirely additive.  The two pipelines share:

- The `IEntityPatchContext` contract
- `SmartEgressUtil.MarkDirty` egress semantics

---

## 4. Implementation Phases

### Phase 1: Binary Contract & Schema Foundation

Establish the DDS wire types (`AttributeValueUnion`, `AttributeRecord`), the `AttributeId`
schema table, and update `CreateEntityRequest` / `UpdateEntityAttributeRequest` with the new
optional list fields.  No runtime behaviour changes.

### Phase 2: Edge Compiler

Implement `JsonToRecordCompiler` and `JsonToRecordCompilerBuilder` in
`FDP.Toolkit.Replication.Patching`.  Implement the domain-specific schema registration
(`EdgeCompilerFactory` in `Hrot.SimHost` or `Hrot.IG`).

### Phase 3: Binary Interpreter Core

Implement `BinaryInterpreter`, `BinaryInterpreterBuilder`, `IBinaryAttributeInstaller`, and
`BinaryPatchContext` in `FDP.Toolkit.Replication.Patching`.

### Phase 4: Domain Installers

Implement `EntityDataAttributeInstaller` and `SimTransformAttributeInstaller` in
`Hrot.SimHost`, wiring the concrete ECS component handlers and GeoCoord scratchpad logic.

### Phase 5: System Integration

Wire `BinaryInterpreter` into `CreateEntityRequestSystem` and
`UpdateEntityAttributeRequestSystem` (dual-path: binary primary, JSON fallback).

### Phase 6: Client-Side (CreationTool)

Inject `JsonToRecordCompiler` into `CreationTool`; convert `_initialPropertiesJson` to
`AttributeRecord[]` before sending `CreateEntityRequest`.

---

## 5. Architectural Constraints & Decisions

| # | Decision | Rationale |
|---|----------|-----------|
| A1 | keep JSON pipeline unchanged | Backward compat; JSON remains the authoring format |
| A2 | EdgeCompiler on client, not SimHost | Offload JSON parsing cost from hot path |
| A3 | `AttributeId` stays in FDP.Toolkit (generic) | Decoupled from domain ECS types |
| A4 | domain-specific IDs in domain projects | Avoids polluting the generic toolkit |
| A5 | `ushort` attribute ID, `short` sub-indices | Fits DDS IDL cleanly; 65536 IDs sufficient |
| A6 | Installer pattern for BinaryInterpreter | Open/Closed — add components without touching core |
| A7 | BinaryPatchContext is a class, not ref struct | Managed component support requires heap ref |
| A8 | Scratchpad slots pre-allocated per installer | Zero allocation on hot path |
| A9 | Both pipelines share IEntityPatchContext | Reuse of ListPatchContext / EcsPatchContext |
| A10 | Dual-field on wire for migration period | Allows staged rollout without a flag-day cut-over |

---

## 6. Files & Modules Affected

| File | Change |
|------|--------|
| `Hrot.NED/GenericMessages.cs` | Add `AttributeValueUnion`, `AttributeRecord`; add list fields to existing requests |
| `FDP.Toolkit.Replication/Patching/AttributeIds.cs` | New — well-known ID constants |
| `FDP.Toolkit.Replication/Patching/AttributeValueUnion.cs` | New — C# repr of the union |
| `FDP.Toolkit.Replication/Patching/JsonToRecordCompiler.cs` | New — Edge Compiler impl |
| `FDP.Toolkit.Replication/Patching/JsonToRecordCompilerBuilder.cs` | New — Edge Compiler builder |
| `FDP.Toolkit.Replication/Patching/BinaryInterpreter.cs` | New — Core Interpreter impl |
| `FDP.Toolkit.Replication/Patching/BinaryInterpreterBuilder.cs` | New — Core Interpreter builder |
| `FDP.Toolkit.Replication/Patching/IBinaryAttributeInstaller.cs` | New — installer interface |
| `FDP.Toolkit.Replication/Patching/BinaryPatchContext.cs` | New — context for binary patching |
| `Hrot.SimHost/AttributeCompilerFactory.cs` | Add `BuildEdgeCompiler()` and `BuildBinaryInterpreter()` |
| `Hrot.SimHost/Installers/EntityDataAttributeInstaller.cs` | New |
| `Hrot.SimHost/Installers/SimTransformAttributeInstaller.cs` | New |
| `Hrot.SimHost/Systems/CreateEntityRequestSystem.cs` | Accept + use BinaryInterpreter |
| `Hrot.Map.Common/Systems/UpdateEntityAttributeRequestSystem.cs` | Accept + use BinaryInterpreter |
| `Hrot.IG/Tools/CreationTool.cs` | Inject + use JsonToRecordCompiler |
