# ECS 512-Component Expansion — Design

## Overview

**Goal:** Double the ECS component type capacity from 256 to 512 while simultaneously improving
entity traversal performance by splitting the entity index into separate Hot (mask) and Cold
(metadata) arrays.

**Problem:** The current `ComponentIdAttribute.Id` is typed as `byte`, bounding component IDs to
the range [0, 255]. The current `EntityHeader` (96 bytes) stores both component mask and all
metadata in a single struct, causing three cache-line fetches per entity during traversal. A naive
doubling to `BitMask512` would inflate the header to 160 bytes — five cache-line fetches — making
traversal significantly slower.

**Scope:** All changes are within `Fdp.Core` (project `FDP/Engine/Fdp.Core`). No other projects
change their source code, though downstream assemblies that use `ComponentIdAttribute` or
`GlobalComponentIds` will recompile once after the ID type change.

---

## Architectural Decision: Hot/Cold Split (Structure of Arrays)

The design talk evaluated three options:

| Option | Description | Verdict |
|--------|-------------|---------|
| 1 | Pure doubling (BitMask512 inside EntityHeader) | Rejected — 160-byte header = 3-5 cache lines per entity |
| 2 | Frequent/rare branching flag | Rejected — branch mispredictions cost 15-20 cycles vs. 1-2 for the extra AVX2 instruction |
| 3 | Hot/Cold SoA split | **Chosen** — 1 cache line for 99% of filtered-out entities |

**Chosen approach:**

- Separate the component mask (the only field needed to reject most entities) into a dedicated
  64-byte structure (`BitMask512`) stored in its own `NativeChunkTable`.
- Place everything else — authority mask, generation, flags, timestamps, DIS type, lifecycle state
  — into a 128-byte cold structure (`EntityMetadataCold`) in a parallel `NativeChunkTable`.
- During traversal, the hot check loads 1 cache line. Only on a mask match does the cold data
  (2 cache lines) get fetched.

Dead entities have their `BitMask512` explicitly zeroed on destroy; they fail the hot check in 1-2
CPU instructions with no cold memory access.

---

## Phase 1: Prerequisites — ID Type and Configuration

### Why this must go first

`ComponentIdAttribute.Id` is currently `byte`. Every component ID constant in `GlobalComponentIds`
is `const byte`. If the ID type is not widened first, any value above 255 is a compile error. This
phase has zero runtime behavior change — it is purely a type promotion.

### Changes

**`ComponentIdAttribute.cs`** (`Fdp.Core` namespace)
- Change `public byte Id { get; }` to `public int Id { get; }`
- Change constructor parameter from `byte id` to `int id`
- Update XML doc: range becomes `[0, 511]`

**`GlobalComponentIds.cs`** (`Fdp.Core` namespace)
- Change every `public const byte` to `public const int`
- No numeric values change
- Update block comment: upper bound changes from 255 to 511, add `256-511: Reserved for expansion`

**`FdpConfig.cs`** (`Fdp.Core` namespace)
- Change `MAX_COMPONENT_TYPES = 256` to `MAX_COMPONENT_TYPES = 512`
- Bump `FORMAT_VERSION` from `4` to `5` (mandatory — binary layout of entity index chunks
  changes completely)
- Update XML doc on `MAX_COMPONENT_TYPES` to reference `BitMask512` capacity

**`QueryBuilder.cs`** — the hardcoded guard `componentId < 256` in `WithComponentId` must become
`componentId < 512` to match the new capacity.

---

## Phase 2: New Data Structures

### BitMask512

**File:** `FDP/Engine/Fdp.Core/BitMask512.cs`

A 512-bit bitmask, **exactly 64 bytes**, fitting one L1 cache line. Replaces `BitMask256` as the
component presence/authority mask type for entities.

Key design constraints:
- `[StructLayout(LayoutKind.Explicit, Size = 64)]` — hard-coded to 64 bytes.
- 8 `ulong` fields (`_q0`–`_q7`).
- `SetBit`, `ClearBit`, `IsSet` use a `switch` on `bitIndex >> 6` (quad index).
- `Clear()`, `SetAll()`, `IsEmpty()` are scalar.
- `BitwiseAnd`, `BitwiseOr` are scalar bulk operations.
- `HasAll`, `HasAny`, `Matches` use AVX2 VPTEST (`Avx.TestC` / `Avx.TestZ`) with scalar fallback:
  - Lower 256 bits: single `Vector256` load → early `return false` on mismatch.
  - Upper 256 bits: second `Vector256` load, only reached if lower half passes.

The scalar fallback interleaves include and exclude checks (lower-half first, then upper-half)
to maximise short-circuit rejection on CPU families without AVX2.

Paranoid-mode bounds checks guard `bitIndex` against `[0, 511]` under `#if FDP_PARANOID_MODE`.

### EntityMetadataCold

**File:** `FDP/Engine/Fdp.Core/EntityMetadataCold.cs`

A 128-byte cold metadata struct (two cache lines). Replaces the non-mask portion of the old
`EntityHeader`.

Fields (same logical content, new layout):
```
[FieldOffset( 0)] BitMask512 AuthorityMask   // 64 bytes — now 512-bit authority
[FieldOffset(64)] ushort     Generation      // 2 bytes
[FieldOffset(66)] ushort     Flags           // Bit 0: IsActive
[FieldOffset(68)] ulong      LastChangeTick  // 8 bytes
[FieldOffset(76)] DISEntityType DisType      // 8 bytes
[FieldOffset(84)] EntityLifecycle LifecycleState // 1 byte
                  (padding to 128 bytes)
```

`IsActive` computed property reads `(Flags & 0x0001) != 0`.
`SetActive(bool)` sets/clears bit 0 of `Flags`.

### Deletion of EntityHeader

`EntityHeader.cs` becomes obsolete and is deleted. Its responsibilities are fully covered by the
combination of `BitMask512` (hot) and `EntityMetadataCold` (cold).

---

## Phase 3: Core Rewrite — EntityIndex

**File:** `FDP/Engine/Fdp.Core/EntityIndex.cs`

The `EntityIndex` class is rewritten to manage two parallel chunk tables instead of one.

### New internal layout

```csharp
private readonly NativeChunkTable<BitMask512>         _hotMasks;
private readonly NativeChunkTable<EntityMetadataCold> _coldMeta;
```

The old `NativeChunkTable<EntityHeader>` `_headers` field is removed.

### API changes

| Old API | New API |
|---------|---------|
| `GetHeader(int)` | `GetComponentMask(int)` + `GetMetadata(int)` |
| `GetHeaderUnsafe(int)` | `GetComponentMaskUnsafe(int)` + `GetMetadataUnsafe(int)` |
| `CopyChunkToBuffer(c, buf)` | `CopyHotChunkToBuffer(c, buf)` + `CopyColdChunkToBuffer(c, buf)` |
| `RestoreChunkFromBuffer(c, data)` | `RestoreHotChunkFromBuffer(c, data)` + `RestoreColdChunkFromBuffer(c, data)` |

`ApplyComponentFilter(BitMask256)` becomes `ApplyComponentFilter(BitMask512)`.

### Invariants preserved

- `CreateEntity`: Clears the hot mask (guarantees dead-entity fast fail in queries), sets
  `AuthorityMask` to empty, increments population on **both** tables.
- `DestroyEntity`: Clears hot mask (immediate AVX2 fast-fail), increments generation in cold.
  Both tables' population counters are decremented together.
- `SyncFrom(EntityIndex)`: Calls `SyncDirtyChunks` on both tables; syncs counters.
- `RebuildMetadata()`: Rebuilds `_activeCount`, `_maxIssuedIndex`, and the free-list from cold
  data only (liveness lives in cold).

### Flight Recorder hooks added to EntityIndex

New proxy methods for the recorder (see Phase 4):
```csharp
int  CopyHotChunkToBuffer(int c, Span<byte> dest)
int  CopyColdChunkToBuffer(int c, Span<byte> dest)
void RestoreHotChunkFromBuffer(int c, byte[] data)
void RestoreColdChunkFromBuffer(int c, byte[] data)
void SanitizeHotChunk(int c, ReadOnlySpan<bool> liveness)
void SanitizeColdChunk(int c, ReadOnlySpan<bool> liveness)
```

---

## Phase 4: Query Engine — EntityQuery and QueryBuilder

**Files:**
- `FDP/Engine/Fdp.Core/EntityQuery.cs`
- `FDP/Engine/Fdp.Core/QueryBuilder.cs`

### EntityQuery

All `BitMask256` mask fields become `BitMask512`:
- `_includeMask`, `_excludeMask`, `_authorityIncludeMask`, `_authorityExcludeMask`

Constructor parameter types change accordingly.

`Matches(in BitMask512 mask, in EntityMetadataCold meta)` replaces the old overload that took
`EntityHeader`.

**`MoveNext()` rewrite (the main performance change):**

```
1. Get ref to hot mask (1 cache line fetch)
2. BitMask512.HasAll(compMask, _includeMask) -> continue if false
3. BitMask512.HasAny(compMask, _excludeMask) -> continue if true
---- if survived: fetch cold (2 cache lines) ----
4. meta.IsActive check
5. _lifecycleFilter check
6. Authority mask checks
7. DIS filter check
```

Steps 2-3 happen entirely in hot memory. Only entities that pass the component filter cause a
cold memory fetch. This is the key performance benefit of the hot/cold split.

`ForEach`, `Count`, `Any`, `ForEachParallel`, and `GenerateBatches` all update to the same
hot-first pattern.

### QueryBuilder

- All internal `BitMask256` mask fields become `BitMask512`.
- `WithComponentId(int)` guard changes from `componentId < 256` to `componentId < 512`.
- The `Build()` method passes `BitMask512` values to the `EntityQuery` constructor.

---

## Phase 5: Repository Layer — EntityRepository

**Files:**
- `FDP/Engine/Fdp.Core/EntityRepository.cs`
- `FDP/Engine/Fdp.Core/EntityRepository.Sync.cs`

### EntityRepository.cs

All call sites that previously called `entityIndex.GetHeader(entity.Index)` must be split:
- Hot access (component mask bit set/clear): `entityIndex.GetComponentMask(entity.Index)`
- Cold access (LastChangeTick, DisType, Generation, etc.): `entityIndex.GetMetadata(entity.Index)`

The `_tableCache` array size is `FdpConfig.MAX_COMPONENT_TYPES` — this auto-resizes when the
constant changes from 256 to 512.

### EntityRepository.Sync.cs

`GetRecordableMask()`, `GetSnapshotableMask(bool)`, and `GetSaveableMask()` currently return
`BitMask256`. These must return `BitMask512` to match the new query and recorder APIs.

---

## Phase 6: Flight Recorder — RecorderSystem and PlaybackSystem

**Files:**
- `FDP/Engine/Fdp.Core/FlightRecorder/RecorderSystem.cs`
- `FDP/Engine/Fdp.Core/FlightRecorder/PlaybackSystem.cs`

### Motivation

The current recorder treats the entity index as a monolithic chunk stream identified by
`ENTITY_INDEX_TYPE_ID = -1`. With the hot/cold split, the single stream becomes two:

```csharp
private const int ENTITY_INDEX_HOT_TYPE_ID  = -1;  // BitMask512 array
private const int ENTITY_INDEX_COLD_TYPE_ID = -2;  // EntityMetadataCold array
```

Type ID `-2` is new. The existing type ID `-1` is **reused** for the hot stream to maintain
numeric meaning (entity index structural data), while `-2` is a strictly additive constant.

### RecorderSystem changes

1. Add `ENTITY_INDEX_COLD_TYPE_ID = -2` constant (keep `-1` as hot).
2. Replace the single entity index flush loop with two loops per chunk:
   - Write hot chunk (sanitize dead-entity slots, apply recordable mask via new
     `SanitizeHotMasks` helper).
   - Write cold chunk (sanitize dead-entity slots only; no mask filtering needed on cold data).
3. Rename `SanitizeHeadersMask` to `SanitizeHotMasks`:
   - Input type changes from `EntityHeader*` to `BitMask512*`.
   - The method now calls `masks[i].BitwiseAnd(mask)` directly.
4. `GetRecordableMask()` returns `BitMask512` (was `BitMask256`).

### PlaybackSystem changes

1. `ApplyChunkData` intercepts both `-1` (hot) and `-2` (cold) to route to the correct
   `RestoreHotChunkFromBuffer` / `RestoreColdChunkFromBuffer` proxy on `EntityIndex`.
2. `RepairManagedComponentMasks` replaces:
   - `ref var header = ref entityIndex.GetHeaderUnsafe(i)` with two separate accesses:
     `ref var meta = ref entityIndex.GetMetadataUnsafe(i)` (liveness check)
     `ref var mask = ref entityIndex.GetComponentMaskUnsafe(i)` (mask bit update)

### Recording size impact

Hot arrays are 64 bytes per entity; cold arrays are 128 bytes. Total is 192 bytes vs. 96 bytes
in the old format. However, because the two streams are written as homogeneous blocks (all masks
contiguous, then all metadata contiguous), LZ4 compression ratios improve significantly compared
to interleaved 96-byte structs.

---

## Out of Scope

- **PartDescriptor**: Uses `BitMask256` to track which 64-byte *parts of a single component*
  are present for network sync. This is a different domain (parts-within-component, not
  component IDs). It is not modified in this workstream.
- **AVX-512 / single-instruction 512-bit compare**: Target hardware may not support AVX-512.
  The two-instruction AVX2 approach is the baseline; AVX-512 is a future optimization.
- **Archetype ECS**: A full archetype rewrite would eliminate per-entity mask checks but
  requires a much larger structural change to the ECS. Out of scope.
- **Test updates**: Each task lists its own success conditions. Existing passing tests that
  exercise `EntityHeader` will be updated as part of the individual task that removes `EntityHeader`.

---

## Dependency Order

```
Phase 1 (ID types + config) 
  -> Phase 2 (BitMask512 + EntityMetadataCold)
    -> Phase 3 (EntityIndex rewrite)
      -> Phase 4 (EntityQuery + QueryBuilder)
      -> Phase 5 (EntityRepository)
      -> Phase 6 (Flight Recorder)
```

Phases 4, 5, and 6 all depend on Phase 3 and can be developed in parallel once Phase 3 is
complete and tests pass.

---

## Project Dependency Note

All changed files are in `Fdp.Core`. Downstream projects (`Fdp.Core.Tests`, `Fdp.ModuleHost`,
`Fdp.Engine`, `Fdp.Toolkits`, etc.) reference `Fdp.Core` and will recompile cleanly after the
`byte -> int` type change in `ComponentIdAttribute` because the constructor parameter type widens.
No downstream project has direct references to `EntityHeader`, `BitMask256` (mask usage on entity
headers), or `EntityIndex` internals beyond what is already exposed through the public API.

The `Fdp.Core.Tests` project tests all changed components and must be updated in the same commit
as each functional task.
