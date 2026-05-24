# ECS 512-Component Expansion — Task Details

**Reference:** See [DESIGN.md](./DESIGN.md) for architecture and rationale.

All tasks are in project `FDP/Engine/Fdp.Core` (namespace `Fdp.Core`).
Test project: `FDP/Engine/Fdp.Core.Tests`.

---

## TASK-E001 — Widen Component ID Type: Attribute and Constants

**Design Reference:** DESIGN.md — Phase 1: Prerequisites

### Scope

**Included:**
- `ComponentIdAttribute.cs`: Change `Id` property and constructor parameter from `byte` to `int`.
- `GlobalComponentIds.cs`: Change all `public const byte` declarations to `public const int`. Add
  an `// ID block 256-511: Reserved for expansion` comment at the bottom.
- No numeric values change; no component behavior changes.

**Not included:**
- `FdpConfig` changes (TASK-E002).
- Any new component IDs.
- Any changes outside `Fdp.Core`.

### Constraints

- All existing `[ComponentId(GlobalComponentIds.XYZ)]` attributes on component structs throughout
  the codebase must continue to compile after this change with no edits to those structs.
  This is guaranteed because `int` is a wider type and all current values fit in both.
- The XML doc on `ComponentIdAttribute` must update the stated range from `[0, 255]` to `[0, 511]`.
- Do not renumber, reorder, or remove any existing constants in `GlobalComponentIds`.

### Success Conditions

1. **Compilation test**: The solution builds without errors or warnings introduced by this task.

2. **Reflection test** — `ComponentIdAttributeTests.cs`:
   - Given: a component struct decorated with `[ComponentId(300)]`.
   - When: `typeof(MyTestComp).GetCustomAttribute<ComponentIdAttribute>().Id` is read.
   - Assert: the value is `300` (not 44 = 300 % 256 from byte overflow).

3. **Registry collision test**:
   - Given: two structs decorated with `[ComponentId(300)]` and `[ComponentId(301)]`.
   - When: both are registered via `ComponentTypeRegistry.GetOrRegisterManaged`.
   - Assert: `300` and `301` are their respective IDs; registering a third struct with
     `[ComponentId(300)]` throws `InvalidOperationException` (collision).

4. **Existing ID test**: All existing tests in `ComponentIdAttributeTests.cs` and
   `ComponentTypeRegistryTests.cs` continue to pass unchanged.

---

## TASK-E002 — Configuration Update: Capacity and Format Version

**Design Reference:** DESIGN.md — Phase 1: Prerequisites

### Scope

**Included:**
- `FdpConfig.cs`:
  - `MAX_COMPONENT_TYPES` from `256` to `512`.
  - `FORMAT_VERSION` from `4` to `5`.
  - Update XML doc on `MAX_COMPONENT_TYPES` to say "Limited by `BitMask512` capacity."
- `QueryBuilder.cs`:
  - `WithComponentId(int)` guard: change `componentId < 256` to `componentId < 512`.

**Not included:**
- Any data structure changes (Phase 2+).
- Any recorder format changes (Phase 6).

### Constraints

- `FORMAT_VERSION` must be exactly `5` (the current value is `4`; incrementing by more than 1
  would skip version numbers that may be used in existing tooling checks).
- `_tableCache` in `EntityRepository` is allocated as `new IComponentTable[FdpConfig.MAX_COMPONENT_TYPES]`.
  This array auto-scales because it uses the constant; no code change is needed in
  `EntityRepository.cs` for this specific array.

### Success Conditions

1. `FdpConfig.MAX_COMPONENT_TYPES` reads as `512`.
2. `FdpConfig.FORMAT_VERSION` reads as `5`.
3. **QueryBuilder range test**:
   - `WithComponentId(255)` sets bit 255 in the include mask (existing behavior, unchanged).
   - `WithComponentId(400)` no longer silently discards the ID; it sets bit 400 in the include mask.
   - `WithComponentId(512)` is still silently ignored (out-of-range guard remains for the upper bound).
4. All existing `QueryTests.cs` tests pass.

---

## TASK-E003 — New Data Structure: BitMask512

**Design Reference:** DESIGN.md — Phase 2, "BitMask512" section

### Scope

**Included:**
- Create `FDP/Engine/Fdp.Core/BitMask512.cs`.

**Not included:**
- Any usage of `BitMask512` in other files (that is Phase 3 and beyond).
- Deletion of `BitMask256.cs` (it remains; other subsystems still use it for non-entity purposes,
  e.g. `PartDescriptor`).

### Constraints

- `[StructLayout(LayoutKind.Explicit, Size = 64)]` — must be exactly 64 bytes.
- 8 `ulong` private fields (`_q0`–`_q7`) at offsets 0, 8, 16, 24, 32, 40, 48, 56.
- `SetBit(int bitIndex)` / `ClearBit(int bitIndex)` / `IsSet(int bitIndex)`: use `switch` on
  `bitIndex >> 6` (quad index = 0–7), plus `bitIndex & 0x3F` for the bit offset within the quad.
- `#if FDP_PARANOID_MODE` guards must check `bitIndex >= 0 && bitIndex < 512`.
- `Matches(in BitMask512 target, in BitMask512 include, in BitMask512 exclude)`:
  - AVX2 path: lower 256 bits (quad 0-3) first with `Avx.TestC` (HasAll) and `Avx.TestZ` (HasNone).
    Return `false` immediately if lower half fails.
  - Upper 256 bits (quad 4-7) only if lower half passes.
  - Scalar fallback: interleaved include/exclude checks for each quad, lower half first.
- `HasAll`, `HasAny` follow same two-stage AVX2 pattern.
- `IEquatable<BitMask512>` implementation with `Equals`, `GetHashCode`, `==`, `!=`.
- Namespace: `Fdp.Core`.

### Success Conditions

1. **Size test**: `Unsafe.SizeOf<BitMask512>()` returns `64`.

2. **SetBit/IsSet round-trip** — for each boundary: bit 0, bit 63, bit 64, bit 127, bit 255,
   bit 256, bit 383, bit 511:
   - Create empty mask, call `SetBit(n)`, assert `IsSet(n)` is true.
   - Assert all other tested bits remain false.

3. **ClearBit test**: Set bit 400, clear bit 400, assert `IsSet(400)` is false and `IsEmpty()` is true.

4. **HasAll / HasAny**:
   - `HasAll(mask, required)`: true when all required bits are set; false when any required bit
     is missing (test with bits in lower half, upper half, and straddling the boundary).
   - `HasAny(mask, test)`: true when at least one bit overlaps; false when no bits overlap.

5. **Matches**:
   - Correctly returns false when include bit missing (lower half, upper half).
   - Correctly returns false when exclude bit present (lower half, upper half).
   - Returns true when all include bits set and no exclude bits set.

6. **AVX2 short-circuit**: A mask with no lower bits set and one upper include bit fails at the
   lower-half check (upper half is never loaded). This is a behavioral constraint, not directly
   testable as a black-box test, but the implementation must follow the early-return pattern
   from DESIGN.md.

7. **Equality**: Two masks with identical bits compare equal; masks differing in any bit compare
   unequal. `GetHashCode` is consistent with equality.

8. **Paranoid mode**: Under `FDP_PARANOID_MODE`, `SetBit(-1)` throws `ArgumentOutOfRangeException`;
   `SetBit(512)` throws `ArgumentOutOfRangeException`.

---

## TASK-E004 — New Data Structure: EntityMetadataCold

**Design Reference:** DESIGN.md — Phase 2, "EntityMetadataCold" section

### Scope

**Included:**
- Create `FDP/Engine/Fdp.Core/EntityMetadataCold.cs`.

**Not included:**
- Deletion of `EntityHeader.cs` (happens in TASK-E005).
- Any usage in `EntityIndex` or other files (Phase 3+).

### Constraints

- `[StructLayout(LayoutKind.Explicit, Size = 128)]` — must be exactly 128 bytes (two cache lines).
- Field layout:
  ```
  [FieldOffset(  0)] public BitMask512     AuthorityMask
  [FieldOffset( 64)] public ushort         Generation
  [FieldOffset( 66)] public ushort         Flags
  [FieldOffset( 68)] public ulong          LastChangeTick
  [FieldOffset( 76)] public DISEntityType  DisType
  [FieldOffset( 84)] public EntityLifecycle LifecycleState
  (remaining bytes are implicit padding)
  ```
- `IsActive` computed property: `(Flags & 0x0001) != 0`.
- `SetActive(bool active)`: sets or clears bit 0 of `Flags`.
- Namespace: `Fdp.Core`.
- The struct must be `unmanaged` (no managed fields).

### Success Conditions

1. **Size test**: `Unsafe.SizeOf<EntityMetadataCold>()` returns `128`.

2. **IsActive / SetActive round-trip**:
   - Default `EntityMetadataCold` has `IsActive == false`.
   - After `SetActive(true)`, `IsActive` is `true`.
   - After `SetActive(false)`, `IsActive` is `false`.
   - `SetActive` does not modify any bits other than bit 0 of `Flags`.

3. **AuthorityMask field is a BitMask512**:
   - `meta.AuthorityMask.SetBit(300)` succeeds and `meta.AuthorityMask.IsSet(300)` is true.

4. **Unmanaged constraint**: `EntityMetadataCold` satisfies `where T : unmanaged` (verified by
   creating a `NativeChunkTable<EntityMetadataCold>` instance in a test without compilation error).

---

## TASK-E005 — EntityIndex Rewrite: Hot/Cold Parallel Tables

**Design Reference:** DESIGN.md — Phase 3

### Scope

**Included:**
- Full rewrite of `FDP/Engine/Fdp.Core/EntityIndex.cs`.
- Delete `FDP/Engine/Fdp.Core/EntityHeader.cs`.
- Update all call sites in `EntityIndex` itself.

**Not included:**
- `EntityQuery`, `EntityRepository`, `RecorderSystem`, `PlaybackSystem` — those are
  Phases 4, 5, and 6.
- Any test file that currently tests `EntityHeader` directly; those tests must be updated as
  part of this task.

### Constraints

- Replace `NativeChunkTable<EntityHeader> _headers` with:
  - `NativeChunkTable<BitMask512> _hotMasks` (hot path).
  - `NativeChunkTable<EntityMetadataCold> _coldMeta` (cold path).
- Both tables must be allocated in the constructor and disposed in `Dispose()`.
- `CreateEntity`: must clear the hot mask, set `AuthorityMask` empty in cold, increment population
  on **both** tables in sync.
- `DestroyEntity`: must clear hot mask (not just cold), increment generation in cold, decrement
  population on both tables.
- `SyncFrom(EntityIndex)`: calls `SyncDirtyChunks` on **both** tables.
- `ApplyComponentFilter(BitMask512)` — parameter type changes from `BitMask256`.
- `RebuildMetadata()`: derives liveness from cold table only.
- `GetChunkLiveness(int chunkIndex, Span<bool>)`: reads `IsActive` from cold table.
- Old accessor `GetHeader(int)` and `GetHeaderUnsafe(int)` are **removed**.
- New accessors: `GetComponentMask(int)`, `GetMetadata(int)`, `GetComponentMaskUnsafe(int)`,
  `GetMetadataUnsafe(int)`.
- Flight Recorder proxy methods (new): `CopyHotChunkToBuffer`, `CopyColdChunkToBuffer`,
  `RestoreHotChunkFromBuffer`, `RestoreColdChunkFromBuffer`, `SanitizeHotChunk`,
  `SanitizeColdChunk`.
- `CopyChunkToBuffer` and `RestoreChunkFromBuffer` (old monolithic proxies) are **removed**.
- `ForceRestoreEntity` signature changes: `BitMask256 componentMask` → `BitMask512 componentMask`.

### Success Conditions

1. **Create/Destroy round-trip**:
   - Create entity, assert `IsAlive` returns true.
   - Destroy entity, assert `IsAlive` returns false.
   - Hot mask of a destroyed entity is `IsEmpty()` == true (zero mask for fast fail).

2. **Mask independence**:
   - Create entity A and entity B.
   - Set bit 400 on A's hot mask.
   - Assert B's hot mask does NOT have bit 400 set.

3. **Population counters stay in sync**:
   - Create 10 entities, destroy 3.
   - `ActiveCount` == 7.
   - `GetChunkPopulation(0)` == 7 (assuming single chunk).

4. **SyncFrom**:
   - Source EntityIndex has entity 5 with hot mask bit 300 set.
   - After `dest.SyncFrom(source)`, `dest.GetComponentMask(5).IsSet(300)` is true.

5. **GetChunkLiveness** returns correct liveness from cold data:
   - Create entities 0, 1, 2. Destroy entity 1.
   - `GetChunkLiveness(0, span)`: span[0]=true, span[1]=false, span[2]=true.

6. **ForceRestoreEntity** correctly populates both tables:
   - Call `ForceRestoreEntity(10, true, 3, someMask512)`.
   - `GetComponentMask(10)` returns `someMask512`.
   - `GetMetadata(10).IsActive` is true.
   - `GetMetadata(10).Generation` is 3.

7. **Existing passing tests** from `EntityIndexLivenessTests.cs`, `EntityIndexSyncTests.cs`,
   `ChunkIterationTests.cs` are updated for the new API and all pass.

---

## TASK-E006 — EntityQuery and QueryBuilder: Hot-First Traversal

**Design Reference:** DESIGN.md — Phase 4

### Scope

**Included:**
- `FDP/Engine/Fdp.Core/EntityQuery.cs`: update all `BitMask256` fields/types to `BitMask512`,
  rewrite `MoveNext()` to the hot-first two-stage check.
- `FDP/Engine/Fdp.Core/QueryBuilder.cs`: update all `BitMask256` fields to `BitMask512`.

**Not included:**
- `EntityRepository` (Phase 5).
- No changes to chunk-skip optimization in `GenerateBatches` (it still reads chunk populations
  from `EntityIndex`, which is now based on cold data).

### Constraints

- `MoveNext()` hot-first order (must match DESIGN.md Phase 4):
  1. `GetComponentMaskUnsafe(i)` — hot memory only.
  2. `BitMask512.HasAll(compMask, _includeMask)` — continue on fail.
  3. `BitMask512.HasAny(compMask, _excludeMask)` — continue on true (exclude match).
  4. Only then: `GetMetadataUnsafe(i)` — cold memory.
  5. `meta.IsActive`, lifecycle, authority, DIS filter checks.
- `Entity.Current` must read `Generation` from cold metadata.
- `QueryBuilder.WithComponentId(int)` guard: `< 512` (was `< 256`; already covered by TASK-E002
  if done first, but this task must not regress it).
- `Matches(in BitMask512 mask, in EntityMetadataCold meta)` replaces the old overload that
  accepted `EntityHeader`.

### Success Conditions

1. **Include filter** (upper-range bits):
   - Create entity with bit 400 set; query with `.With<T>()` where `ComponentType<T>.ID == 400`.
   - Assert entity appears in the query result.
   - Create entity without bit 400; assert it does NOT appear.

2. **Exclude filter**:
   - Create entity with bit 300 set; query with `.Without<T>()` where ID == 300.
   - Assert entity does NOT appear.

3. **Hot short-circuit (cold not accessed)**:
   - Create entity with `IsActive = false` and a non-empty component mask (edge case).
   - Since the mask check passes before the `IsActive` check, the entity is rejected at
     the cold `IsActive` check. The test verifies the entity does not appear.
   - Verify: dead entities (ComponentMask == 0) never reach the cold check because
     `HasAll(zeroMask, nonZeroInclude)` returns false.

4. **Parallel iteration** (`ForEachParallel`): results are the same as serial iteration for
   a 5000-entity world with 200 matching entities.

5. **Count / Any** correctness:
   - Empty world: `Count()` == 0, `Any()` == false.
   - 3 matching entities: `Count()` == 3, `Any()` == true.

6. **Existing `QueryTests.cs`** and `EntityQueryEnumeratorTests.cs` all pass.

---

## TASK-E007 — EntityRepository: Split Header Access

**Design Reference:** DESIGN.md — Phase 5

### Scope

**Included:**
- `FDP/Engine/Fdp.Core/EntityRepository.cs`: Replace every `GetHeader`/`GetHeaderUnsafe` call
  with the appropriate split hot/cold access.
- `FDP/Engine/Fdp.Core/EntityRepository.Sync.cs`: Update `GetRecordableMask()`,
  `GetSnapshotableMask()`, `GetSaveableMask()` to return `BitMask512`.

**Not included:**
- `EntityRepository.DeltaQuery.cs` and `EntityRepository.View.cs` — only change these if they
  contain direct `GetHeader` calls (check before assuming they do not).

### Constraints

- Every operation that **sets or clears a component bit** uses `GetComponentMask(entity.Index)`.
- Every operation that **reads or writes `LastChangeTick`, `Generation`, `DisType`, `IsActive`,
  `LifecycleState`, or `AuthorityMask`** uses `GetMetadata(entity.Index)`.
- `GetRecordableMask()` iterates `ComponentTypeRegistry.GetRecordableTypeIds()` and sets bits
  in a `BitMask512` (was `BitMask256`).
- Same pattern for `GetSnapshotableMask()` and `GetSaveableMask()`.
- No behavioral change: the same component IDs are marked recordable/snapshotable/saveable.

### Success Conditions

1. **AddComponent sets hot mask bit**:
   - Create entity, add component of type ID 350.
   - `repo.GetEntityIndex().GetComponentMask(entity.Index).IsSet(350)` is true.

2. **RemoveComponent clears hot mask bit**:
   - Add then remove component of type ID 350.
   - Hot mask bit 350 is false.

3. **GetRecordableMask returns BitMask512**:
   - Register a component with `record: true`.
   - `repo.GetRecordableMask()` is a `BitMask512`; the bit for that component is set.

4. **Existing `EntityRepositoryTests.cs`** suite passes.

---

## TASK-E008 — RecorderSystem: Dual-Stream Entity Index

**Design Reference:** DESIGN.md — Phase 6, "RecorderSystem changes" section

### Scope

**Included:**
- `FDP/Engine/Fdp.Core/FlightRecorder/RecorderSystem.cs`.

**Not included:**
- `PlaybackSystem.cs` (TASK-E009).

### Constraints

- Add `private const int ENTITY_INDEX_COLD_TYPE_ID = -2`.
- Keep `private const int ENTITY_INDEX_TYPE_ID = -1` renamed to
  `ENTITY_INDEX_HOT_TYPE_ID = -1` (or add the new name and deprecate the old; the binary
  value `-1` for the hot stream must not change to avoid confusing existing parsers that
  inspect the format version before reading data).
- The entity index flush loop (currently labelled "3.1 FLUSH ENTITY INDEX") is replaced:
  for each chunk:
  1. Build liveness from cold metadata via `FillLiveness(entityIndex, ...)`.
  2. Write hot chunk: copy, sanitize dead slots, apply recordable `BitMask512` mask.
  3. Write cold chunk: copy, sanitize dead slots only.
- `SanitizeHeadersMask` is replaced by `SanitizeHotMasks(byte[] buf, int bytesWritten, BitMask512 mask)`:
  - Cast buffer to `BitMask512*`.
  - Apply `BitwiseAnd` per element.
- `GetRecordableMask()` returns `BitMask512` (delegates to `EntityRepository` after TASK-E007).
- Both `RecordDeltaFrame` and `RecordKeyframe` (if it has a separate entity index flush path)
  must be updated consistently.

### Success Conditions

1. **Dual stream written**:
   - Record a frame with at least one active entity.
   - Parse the raw binary: verify that exactly two chunks with `typeId == -1` (hot) and
     `typeId == -2` (cold) are present for the entity index.

2. **Hot chunk size**: the byte count written for a hot chunk is
   `entityIndex.GetChunkCapacity() * Unsafe.SizeOf<BitMask512>()` (== capacity * 64).

3. **Sanitization**: A dead entity's hot mask slot must be all-zero in the recorded data.
   Create 3 entities, destroy entity 1, record a keyframe, re-read the hot chunk bytes, and
   verify that the slot for entity 1 is a 64-byte zero block.

4. **Recordable mask filter**: A component marked `record: false` must have its bit cleared in
   the hot chunk data. Verify by reading back the hot chunk bytes and checking the corresponding
   bit is zero for a non-recordable component that the entity has.

5. **Format version**: the outer `RecordingGlobalHeader` in the written stream has
   `FORMAT_VERSION == 5`.

6. **Existing `RecorderSystemTests.cs`** and `RecorderDeltaLogicTests.cs` pass (updated for
   the new dual-stream format).

---

## TASK-E009 — PlaybackSystem: Route Hot/Cold Streams

**Design Reference:** DESIGN.md — Phase 6, "PlaybackSystem changes" section

### Scope

**Included:**
- `FDP/Engine/Fdp.Core/FlightRecorder/PlaybackSystem.cs`.

**Not included:**
- Any recorder changes (TASK-E008).

### Constraints

- `ApplyChunkData` must handle three cases in order:
  1. `typeId == -1` → `entityIndex.RestoreHotChunkFromBuffer(chunkIndex, data)` → return.
  2. `typeId == -2` → `entityIndex.RestoreColdChunkFromBuffer(chunkIndex, data)` → return.
  3. All other typeIds: existing component table routing unchanged.
- `RepairManagedComponentMasks`: replace `ref var header = ref entityIndex.GetHeaderUnsafe(i)`
  with two refs:
  - `ref var meta = ref entityIndex.GetMetadataUnsafe(i)` for liveness.
  - `ref var mask = ref entityIndex.GetComponentMaskUnsafe(i)` for bit set/clear.
- `SchemaValidator` or `RecordingGlobalHeader` version checks: `FORMAT_VERSION == 5` is the
  accepted version. Recordings with `FORMAT_VERSION < 5` must be rejected with a clear error
  message (not silently misinterpreted).

### Success Conditions

1. **Round-trip**: Record a world (via RecorderSystem after TASK-E008), play it back, assert
   the `EntityIndex` state (active entities, component masks, generation numbers) matches the
   original before recording.

2. **Hot chunk applied**: After playback, `entityIndex.GetComponentMask(n).IsSet(bit)` matches
   the original entity's mask.

3. **Cold chunk applied**: After playback, `entityIndex.GetMetadata(n).Generation` and
   `.IsActive` match the originals.

4. **Managed mask repair**: After playback, managed component mask bits are correct (the
   `RepairManagedComponentMasks` pass runs successfully and sets/clears bits based on actual
   managed data presence).

5. **Version mismatch rejection**: Attempting to play back a FORMAT_VERSION 4 file throws an
   exception (or returns an error code, per existing error handling convention in
   `SchemaValidator` / `PlaybackSystem`).

6. **Existing `PlaybackSystemTests.cs`**, `FlightRecorderIntegrationTests.cs`, and
   `ManagedComponentPlaybackTests.cs` all pass (updated for the new dual-stream format).
