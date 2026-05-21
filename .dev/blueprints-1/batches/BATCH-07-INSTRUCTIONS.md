# BATCH-07 Instructions

**Branch:** `blueprints`
**Workspace root:** `d:\WORK\IOS-IG-SimHost-FDP`

**Scope:** Three corrective tasks (DEBT-013, DEBT-014, DEBT-015) + TASK-RT-004 full implementation
+ TASK-RT-007 partial (PartitionAllocatorTests only).

**Not in scope this batch:** TASK-RT-005, TASK-RT-006 (those are BATCH-08).

**Design references:**
- `.dev/blueprints-1/TASK-DETAIL.md` sections TASK-RT-004 and TASK-RT-007
- `.dev/blueprints-1/Blueprint_Subsystem_Runtime_Detailed_Design.md` §4.6, §5 (entire section)
- `.dev/blueprints-1/Blueprint_Subsystem_Runtime_Detailed_Design_InlinePatches.md`

---

## Corrective Task 0-A: Document `BlueprintSlotEntry.StructureHash` truncation (DEBT-014)

File: `FDP/Toolkits/Fdp.Toolkits/Blueprints/Partitioning/BlueprintSlotEntry.cs`

The field comment in the struct already says "lower 32 bits". Add an XML doc comment on the
field itself (in addition to the existing struct-level note):

```csharp
/// <summary>
/// Lower 32 bits of the Blueprint's 64-bit StructureHash.
/// Truncated from ulong to fit the 16-byte slot-entry budget.
/// Callers must compare with <c>(uint)def.StructureHash</c>.
/// </summary>
public uint   StructureHash;    // 4 bytes -- lower 32 bits of the Blueprint's StructureHash
```

No other change. No test change needed.

---

## Corrective Task 0-B: Fix `BlueprintDefinition.StateFields` type (DEBT-015)

File: `FDP/Toolkits/Fdp.Toolkits/Blueprints/BlueprintDefinition.cs`

Change `StateFields` from `IReadOnlyList<BlueprintFieldDescriptor>` to
`IReadOnlyDictionary<string, BlueprintFieldDescriptor>`. Update the default value:

```csharp
// BEFORE:
public IReadOnlyList<BlueprintFieldDescriptor> StateFields { get; init; }
    = Array.Empty<BlueprintFieldDescriptor>();

// AFTER:
public IReadOnlyDictionary<string, BlueprintFieldDescriptor> StateFields { get; init; }
    = new Dictionary<string, BlueprintFieldDescriptor>(StringComparer.Ordinal);
```

Update `BlueprintDefinitionTests.cs` (`SC1_DefaultDefinition_HasEmptyCollections`) to assert
`StateFields.Count == 0` (same as before, but the type check in SC6 equality test is now fine
because the `with { }` copy shares the same dict reference).

---

## Corrective Task 0-C: Mirror-comment on duplicate `BlueprintDispatchKind` enums (DEBT-013)

Two files:

1. `FDP/Toolkits/Fdp.Toolkits/Blueprints/BlueprintDispatchKind.cs`
   Add to the XML summary:
   ```
   /// Mirror of <c>Hrot.Blueprints.Core.Assets.BlueprintDispatchKind</c>.
   /// Both enums are kept in sync manually because the dependency direction
   /// prevents Fdp.Toolkits from referencing Hrot.Blueprints.Core.
   ```

2. `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Assets/BlueprintAsset.cs`
   Locate `public enum BlueprintDispatchKind` and add analogous comment:
   ```
   /// Mirror of <c>Fdp.Toolkit.Blueprints.BlueprintDispatchKind</c>.
   ```

---

## TASK-RT-004 — BlueprintBlackboardPartitions Full Implementation

### Overview

Replace the stub in `FDP/Toolkits/Fdp.Toolkits/Blueprints/Partitioning/BlueprintBlackboardPartitions.cs`
with the full pointer-based allocator described in Runtime DD §5.

The namespace stays `Fdp.Toolkit.Blueprints.Partitioning` (established in BATCH-03).
The class must become `public static unsafe class` (add `unsafe`).
The `using` directive for `Fdp.Core` is no longer needed and should be removed.

### New Public API (replace all old stub methods)

Follow the method signatures, algorithms, and pseudocode from Runtime DD §5.2 through §5.8
exactly. The detailed algorithm is provided verbatim in the design document -- do NOT deviate
from it. Key points:

**Constants (existing `SlotEntrySize = 16` kept; add the others):**
```csharp
public const int SlotEntrySize       = 16;
public const int FreeBlockHeaderSize = 4;
public const int Alignment           = 8;
private const uint HeaderMagicV1     = 0x42504257u;  // same as BlueprintBlackboardHeader.MagicValue
```

**Public methods:**
- `Initialize(byte* memory, int totalSize, byte maxSlots)` -- idempotent
- `TryGetSlotOffset(byte* memory, int blueprintId, out int payloadOffset)` -- hot path
- `TryAttach(byte* memory, int blueprintId, int requestedSize, ulong structureHash, out int payloadOffset)`
- `TryDetach(byte* memory, int blueprintId)`
- `GetSlotCount(byte* memory)`
- `ref BlueprintSlotEntry GetSlot(byte* memory, int slotIndex)`
- `ResetSlot(byte* memory, int slotIndex, ulong newStructureHash)`
- `CopyToLargerTier(byte* src, int srcSize, byte* dst, int dstSize, byte dstMaxSlots)`

**Private helpers:**
- `TryAllocateFromFreeList(byte* memory, ref BlueprintBlackboardHeader header, int alignedSize)`
- `BumpAllocate(byte* memory, ref BlueprintBlackboardHeader header, int alignedSize)`
- `ReturnToFreeList(byte* memory, ref BlueprintBlackboardHeader header, int offset, int size)`
- `AlignUp(int value, int alignment)` -- standard: `(value + alignment - 1) & ~(alignment - 1)`
- `SumAllocated(BlueprintBlackboardHeader header, byte* slots)` -- used in CopyToLargerTier

### StructureHash truncation

`TryAttach` takes `ulong structureHash` but `BlueprintSlotEntry.StructureHash` is `uint`
(DEBT-014). Store only the lower 32 bits:
```csharp
slot.StructureHash = (uint)structureHash; // Lower 32 bits -- DEBT-014
```

`ResetSlot` takes `ulong newStructureHash` -- same:
```csharp
slot.StructureHash = (uint)newStructureHash; // Lower 32 bits -- DEBT-014
```

### Notes on `Initialize`

`sizeof(BlueprintBlackboardHeader)` in an unsafe context returns 32 (due to `[StructLayout(Size=32)]`).
Do NOT hardcode 32 -- use `sizeof(BlueprintBlackboardHeader)` so the code self-documents.

### Notes on `CopyToLargerTier` free-list walk

When shifting free-list offsets, copy the free-block header data from the SOURCE position
(at `cursor - payloadShift`) because the destination bytes may not yet be set when you iterate.
The algorithm in Runtime DD §5.8 handles this correctly -- follow it precisely.

---

## TASK-RT-004 (continued) — Update BlueprintTestFixture to use real allocator

File: `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/BlueprintTestFixture.cs`

The stub's method signatures no longer exist after replacing the allocator. The fixture must
be updated to use the new pointer-based API. All changes are in `unsafe` code.

### Pattern for getting a `byte*` to a component

In an `unsafe` method on the fixture (which can be marked `unsafe` or use an `unsafe` block):

```csharp
ref var bb = ref _repo.GetComponentRW<BlueprintBlackboard1024>(entity);
ref byte memRef = ref Unsafe.As<BlueprintBlackboard1024, byte>(ref bb);
byte* memory = (byte*)Unsafe.AsPointer(ref memRef);
```

### Update `AttachBlueprint`

Replace the stub TryAttach call with:

1. Compute `tier = ChooseTier(def!.StateSize)` (unchanged).
2. Call `EnsureTierComponent(entity, tier)` (unchanged).
3. Get `byte* memory` for the chosen tier component (using the pattern above).
4. Call `BlueprintBlackboardPartitions.Initialize(memory, totalSize, maxSlots)` where
   `totalSize` and `maxSlots` come from the tier component constants (e.g., for B1024:
   `totalSize = BlueprintBlackboard1024.TotalSize`, `maxSlots = BlueprintBlackboard1024.MaxSlots`).
   `Initialize` is idempotent so calling it every attach is safe.
5. Compute `int blueprintId = BlueprintIdHash.Compute(asset.AssetId)`.
6. Call `BlueprintBlackboardPartitions.TryAttach(memory, blueprintId, def.StateSize, def.StructureHash, out int payloadOffset)`.
7. If `TryAttach` returns false, throw `InvalidOperationException` (same as before, adjust message).
8. If `def.InitDefault != null`, create a span and call it:
   ```csharp
   ref byte payloadRef = ref Unsafe.Add(ref memRef, payloadOffset);
   var initSpan = MemoryMarshal.CreateSpan(ref payloadRef, def.StateSize);
   def.InitDefault(initSpan);
   ```

Helper to get (byte*, totalSize, maxSlots) per tier: write a private `unsafe` method
`GetTierMemoryAndMeta(Entity entity, BlackboardTier tier, out byte* memory, out int totalSize, out byte maxSlots)`.

### Update `TryGetSlotAcrossTiers`

Replace the old multi-out-param calls with pointer-based calls. The new allocator's
`TryGetSlotOffset(byte* memory, int blueprintId, out int payloadOffset)` does not return
`slotIndex` -- the `out int slotIndex` parameter should be set to the result of a linear
scan over the slot table (walk `0..GetSlotCount(memory)` calling `GetSlot(memory, i).BlueprintId`
to find the matching index). If no match found (should not happen if `TryGetSlotOffset`
returned true), set `slotIndex = -1`.

Alternatively, simplify: change `TryGetSlotAcrossTiers` to not return `slotIndex` at all.
Check the call sites -- they use `out _` for it everywhere. If `slotIndex` is unused,
remove it from the signature and update the two callers (`HasSlot`, `GetBlueprintState`).

### Un-skip `AttachBlueprint_RegisteredAsset_SetsHasSlot`

File: `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/BlueprintTestFixtureTests.cs`

Remove the `[Fact(Skip = "Requires Phase 3 compiler")]` and replace with `[Fact]`.
Implement the test body using the FakeInstanceBp pattern (see TASK-RT-007 section below).
After BATCH-07 the real allocator exists, so this test can pass without the compiler:

```csharp
[Fact]
public void AttachBlueprint_RegisteredAsset_SetsHasSlot()
{
    using var fixture = new BlueprintTestFixture();
    var asset = new BlueprintAssetBuilder()
        .WithName("TestBp")
        .WithDispatch(BlueprintDispatchKind.Instance)
        .Build();

    // Register a hand-crafted fake definition (no compiler needed)
    var staging = fixture.Registry.BeginStaging();
    var def = new BlueprintDefinition
    {
        Name          = asset.Name,
        Kind          = BlueprintDispatchKind.Instance,
        StructureHash = 0xDEADBEEFCAFEBABEUL,
        StateSize     = 8,
        InitDefault   = bytes => bytes.Clear(),
    };
    staging.Add(BlueprintIdHash.Compute(asset.AssetId), def);
    fixture.Registry.CommitStaging(staging);

    var entity = fixture.CreateEntity();
    fixture.AttachBlueprint(asset, entity);

    Assert.True(fixture.HasSlot(asset, entity));
}
```

---

## TASK-RT-007 (partial) — PartitionAllocatorTests

Create directory: `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Runtime/PartitionAllocator/`

Create file: `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Runtime/PartitionAllocator/PartitionAllocatorTests.cs`

Namespace: `Hrot.Blueprints.Tests.Runtime`

Tests use fixed-size stack-allocated buffers (via `stackalloc` or `fixed byte[]`) to simulate
the component memory. They do NOT use EntityRepository at all -- they work directly with raw
pointers passed to the allocator.

Use `BlueprintBlackboard1024.TotalSize`, `.MaxSlots`, `.PayloadStart`, etc. as test constants.

Implement all 15 test scenarios from Runtime DD §5.10:

1. `Initialize_ZeroedMemory_SetsHeader`
2. `Attach_SingleBlueprint_AllocatesFromBump`
3. `Attach_MultipleBlueprints_UsesContiguousBump`
4. `Detach_Last_FreesSlot`
5. `Detach_Middle_CreatesFreeBlock`
6. `Detach_AdjacentFree_Coalesces`
7. `Attach_AfterDetach_ReusesFreeBlock`
8. `TryGetSlotOffset_AbsentBlueprint_ReturnsFalse`
9. `Attach_WhenSlotsFull_ReturnsFalse`
10. `Attach_WhenInsufficientSpace_ReturnsFalse`
11. `Attach_Fragmented_ReturnsFalseEvenIfTotalFreeBigEnough`
12. `ResetSlot_PreservesSlotIdentity`
13. `CopyToLargerTier_PreservesAllocations` (src=B1024, dst=B4096)
14. `CopyToLargerTier_PreservesFreeList`
15. `LayoutInvariants_HoldAfterEveryOperation`

### Test helper pattern

Use a `stackalloc byte[size]` buffer and a helper to zero-fill and get a pointer:

```csharp
private static unsafe byte* StackAlloc(byte* buffer, int size)
{
    new Span<byte>(buffer, size).Clear();
    return buffer;
}
```

Or allocate on the managed heap (`new byte[size]`) and use a `fixed` statement in each test.
Either approach is acceptable for tests.

### SC11: Layout invariants test

The `LayoutInvariants_HoldAfterEveryOperation` test should run at least 50 steps of random
attach/detach/reset and after each step verify all 7 invariants from Runtime DD §4.6:

1. `Memory[0..3] == 0x42504257` (header magic)
2. `SlotCount <= MaxSlots`
3. For all `i < MaxSlots`: slot at `i < SlotCount` has `BlueprintId != 0`; slot at `i >= SlotCount` has `BlueprintId == 0`
4. For all allocated slots: `PayloadStart <= PayloadOffset < PayloadOffset + PayloadSize <= TotalSize`
5. No overlap between allocated slot ranges
6. Free list is well-formed (no cycles, all offsets within payload bounds)
7. `PayloadFree == sum-of-free-block-sizes + (TotalSize - PayloadHighWater)`

Use a seeded pseudo-random for determinism (e.g., `new Random(42)`).

---

## Success Criteria

All existing 127 passing tests continue to pass.

New tests pass:
- `AttachBlueprint_RegisteredAsset_SetsHasSlot` (un-skipped, now passes)
- All 15 `PartitionAllocatorTests`

Total expected: 143 pass (127 + 1 un-skipped + 15 new), 4 skip (was 5; one un-skipped).

Build: `dotnet build IOS-IG-SimHost.sln` completes with 0 errors, 0 warnings.

---

## Output

Write completion report to `.dev/blueprints-1/reports/BATCH-07-REPORT.md`.

Answer these specific questions in the report:
1. Were all 7 invariants from §4.6 verified after every step in the LayoutInvariants test?
2. Did `CopyToLargerTier_PreservesFreeList` require any deviation from the design algorithm?
3. Was the `TryGetSlotAcrossTiers` `slotIndex` output removed or kept? Justify.
4. Any deviations from the design document or instructions? (None is an acceptable answer.)
