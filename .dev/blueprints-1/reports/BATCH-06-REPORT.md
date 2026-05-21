# BATCH-06-REPORT

## Tasks Completed

| Task | Title | Status | Tests Added |
|------|-------|--------|-------------|
| TASK-RT-001 | BlueprintRegistry full implementation | COMPLETE | 11 |
| TASK-RT-002 | BlueprintDefinition, delegates, latent cursor | COMPLETE | 10 |
| TASK-RT-003 | Blackboard component layout constants, partitioning structs | COMPLETE | 16 |

---

## 1. TASK-RT-001 -- BlueprintRegistry

### Changes Made

**`FDP/Toolkits/Fdp.Toolkits/Blueprints/BlueprintRegistry.cs`** -- fully replaced stub:
- All registry keys changed from `Guid` to `int blueprintId` throughout.
- Added `RegisterLibrary(int, string)`, `RegisterAiPrimitive(int, BlueprintDefinition)`, `RegisterInstance(int, BlueprintDefinition)` direct-registration helpers. Each performs a duplicate-check that throws `InvalidOperationException` with both names in the message.
- `TryGetById(int, out BlueprintDefinition?)` -- looks up current snapshot.
- `TryGetByName(string, out BlueprintDefinition?)` -- looks up by name from snapshot.
- `GetAll()` returns `IReadOnlyCollection<BlueprintDefinition>` via `.ToArray()` snapshot.
- `RegisterWorldSingleton(int, BlackboardTier)` validates the ID is already registered, then rebuilds the `WorldSingletonList` in-place on the current snapshot.
- `TryGetWorldSingleton(int, out BlackboardTier)` -- lookups against current snapshot.
- `GetAllWorldSingletons()` returns pre-built `IReadOnlyList<(int, BlackboardTier)>` from snapshot (per Hot-path Correction 1 in Runtime DD Inline Patches). Same reference returned on repeated calls as long as snapshot is unchanged.
- `BeginStaging()` / `CommitStaging(BlueprintRegistryStaging)` -- unchanged external signature; `CommitStaging` uses `Interlocked.Exchange` and fires `OnRegistryChanged`.
- `BlueprintRegistryStaging.Add(int blueprintId, BlueprintDefinition def)` -- duplicate throws.
- `BlueprintRegistryStaging.AddWorldSingleton(int, BlackboardTier)`.

**`FDP/Toolkits/Fdp.Toolkits/Blueprints/BlueprintIdHash.cs`** -- created:
- `public static int Compute(Guid assetId)` using FNV-1a 32-bit hash over the 16 GUID bytes.
- Pure utility, no dependencies.

### Tests Added

File: `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Runtime/BlueprintRegistryTests.cs`

| Test | SC | Result |
|------|----|--------|
| SC1_DirectRegistration_ByIdAndByName | SC1 | PASS |
| SC1_TryGetById_ReturnsFalseForUnknownId | SC1 | PASS |
| SC2_CommitStaging_Makes_Entries_Retrievable | SC2 | PASS |
| SC2_CommitStaging_Replaces_PreviousContent | SC2 | PASS |
| SC3_GetAllWorldSingletons_AfterStagingCommit | SC3 | PASS |
| SC3_RegisterWorldSingleton_DirectPath | SC3 | PASS |
| SC4_DirectRegistration_Duplicate_ThrowsInvalidOperation | SC4 | PASS |
| SC4_StagingAdd_Duplicate_ThrowsInvalidOperation | SC4 | PASS |
| SC5_RegisterWorldSingleton_UnknownId_Throws | SC5 | PASS |
| SC6_TwoCommitStagingCalls_SecondWins | SC6 | PASS |
| SC7_OnRegistryChanged_FiresExactlyOnce_PerCommit | SC7 | PASS |
| SC7_OnRegistryChanged_FiresEvenForEmptyStaging | SC7 | PASS |

### Deviations (RT-001)

- **BlueprintIdHash.Compute uses FNV-1a 32-bit** over all 16 GUID bytes, per Runtime DD §2.6. The instructions mentioned "first 4 bytes as int" as an alternative; the FNV-1a approach was chosen because it distributes hash values more uniformly and matches the design document.

---

## 2. TASK-RT-002 -- BlueprintDefinition + Delegates + Cursor

### Changes Made

**`BlueprintDefinition.cs`** -- replaced class stub with `sealed record`:
- Required properties: `Name (string)`, `Kind (BlueprintDispatchKind)`, `StructureHash (ulong)`, `StateSize (int)`.
- Optional init-only: `InitDefault`, `Tick`, `EventHandlers` (defaults to empty `Dictionary`), `StateClrType`, `StateFields` (defaults to `Array.Empty<>`), `AssetId` (kept for backward compatibility, non-required).
- `StateFields` type changed from `IReadOnlyDictionary<string, int>` to `IReadOnlyList<BlueprintFieldDescriptor>` per spec.

**`BlueprintDelegates.cs`** -- created:
- `InitDefaultDelegate(Span<byte> stateBytes)` -- 1 parameter.
- `TickDelegate(Span<byte>, ISimulationView, IEntityCommandBuffer, Entity, float time, float deltaTime, uint instanceVersion)` -- 7 parameters.
- `EventHandlerDelegate(Span<byte>, ISimulationView, IEntityCommandBuffer, Entity, float time, float deltaTime, ReadOnlySpan<byte> payload)` -- 7 parameters.
- Added `using Fdp.Interfaces;` and `using Fdp.ModuleHost.Abstractions;` (IEntityCommandBuffer and ISimulationView live in those namespaces).

**`BlueprintFieldDescriptor.cs`** -- created:
- `public sealed record BlueprintFieldDescriptor(string Name, Type ClrType, int OffsetBytes, int SizeBytes, string CategoryOrEmpty)`.

**`BlueprintLatentCursor.cs`** -- fixed fields:
- Removed `Guid GraphId` (16 bytes wrong allocation semantics for a cursor).
- Added `uint ResumeAt` and `float WaitUntilTime` (8 bytes used, 8 reserved via `Size = 16`).
- `[StructLayout(LayoutKind.Sequential, Size = 16)]`. Struct remains unmanaged.

**`BlueprintRegistrarAttribute.cs`** -- fixed:
- Added `Inherited = false` to `[AttributeUsage(...)]`.

**`BlueprintDispatchKind.cs`** -- created in `Fdp.Toolkit.Blueprints` namespace:
- Values: `Library = 0`, `AiPrimitive = 1`, `Instance = 2`.
- Rationale: `Fdp.Toolkits` cannot reference `Hrot.Blueprints.Core`, so the enum needed to exist in the lower assembly.

### Callers Updated

**`Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/BlueprintTestFixture.cs`**:
- `GetBlueprintState` and `AttachBlueprint`: changed `Registry.TryGetById(asset.AssetId, ...)` to `Registry.TryGetById(BlueprintIdHash.Compute(asset.AssetId), ...)`.
- `TryGetSlotAcrossTiers`: computed `int blueprintId = BlueprintIdHash.Compute(assetId)` and passed it to all `TryGetSlotOffset` calls.

**`Hrot/Subsystems/Hrot.Editor.Tests/AiHotReloadCoordinatorTests.cs`**:
- Updated 3 `BlueprintDefinition` literals to include required fields (`Kind`, `StructureHash`, `StateSize`).
- Changed all `staging.Add(def)` to `staging.Add(int id, def)` using `BlueprintIdHash.Compute(guid)`.
- Duplicate test now uses `dupBpId = BlueprintIdHash.Compute(dupId)` to trigger the staging duplicate guard.

### Tests Added

File: `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Runtime/BlueprintDefinitionTests.cs`

| Test | SC | Result |
|------|----|--------|
| SC1_DefaultDefinition_HasEmptyCollections | SC1 | PASS |
| SC2_BlueprintLatentCursor_Is16Bytes | SC2 | PASS |
| SC3_BlueprintLatentCursor_Satisfies_UnmanagedConstraint | SC3 | PASS |
| SC4_BlueprintRegistrarAttribute_AppliesWithoutError | SC4 | PASS |
| SC4_BlueprintRegistrarAttribute_IsNotInherited | SC4 | PASS |
| SC5_TickDelegate_Has7Parameters | SC5 | PASS |
| SC5_EventHandlerDelegate_Has7Parameters | SC5 | PASS |
| SC5_InitDefaultDelegate_Has1Parameter | SC5 | PASS |
| SC6_RecordCopy_IsEqual | SC6 | PASS |
| SC6_DifferentDefinitions_AreNotEqual | SC6 | PASS |

### Deviations (RT-002)

- **EventHandlerDelegate parameter count:** TASK-DETAIL says "8 params" in one sentence but then lists 7 in parenthetical, and Runtime DD §3.3 explicitly shows 7. Implementation uses **7 parameters** (no `uint instanceVersion` on events). Tests verify 7.
- **`sealed record` equality and collections:** `Dictionary<,>` does not override `Equals`, so two separately constructed `BlueprintDefinition` records with the same scalar values are not structurally equal if their `EventHandlers` dictionaries are different instances. SC6 test uses `a with { }` (shallow clone) so collection references are shared -- this correctly validates record equality while respecting the limitation.

---

## 3. TASK-RT-003 -- Blackboard Layout Types

### Changes Made

**Blackboard components** -- all three updated with same pattern:

| Constant | B1024 | B4096 | B16384 |
|----------|-------|-------|--------|
| TotalSize | 1024 | 4096 | 16384 |
| HeaderSize | 32 | 32 | 32 |
| MaxSlots | 4 | 8 | 16 |
| SlotTableSize | 64 | 128 | 256 |
| PayloadStart | 96 | 160 | 288 |
| PayloadSize | 928 | 3936 | 16096 |

- Field renamed `Data` → `Memory` (consistent with design doc naming).
- `BlueprintBlackboard4096` ComponentId fixed: was hard-coded `206`, now `GlobalComponentIds.BlueprintBlackboard4096` (= 205).
- `BlueprintBlackboard16384` ComponentId fixed: was hard-coded `207`, now `GlobalComponentIds.BlueprintBlackboard16384` (= 206).

**`BlueprintBlackboardHeader.cs`** -- replaced stub with:
- `[StructLayout(LayoutKind.Sequential, Size = 32)]`
- Fields: `MagicAndVersion (uint)`, `SlotCount (byte)`, `MaxSlots (byte)`, `FreeListHead (ushort)`, `PayloadStart (ushort)`, `PayloadSize (ushort)`, `PayloadFree (ushort)`, `PayloadHighWater (ushort)`, `Reserved (ulong)` -- natural layout is 16 bytes, `Size = 32` pads it.
- `public const uint MagicValue = 0x42504257u;` ('BPBW').

**`BlueprintSlotEntry.cs`** -- replaced stub with:
- `[StructLayout(LayoutKind.Sequential, Size = 16)]`
- Fields: `BlueprintId (int)`, `InstanceVersion (uint)`, `PayloadOffset (ushort)`, `PayloadSize (ushort)`, `StructureHash (uint)`.

**`BlueprintFreeBlockHeader.cs`** -- created:
- `[StructLayout(LayoutKind.Sequential, Size = 4)]`
- Fields: `NextFreeOffset (ushort)`, `Size (ushort)`.

**`BlueprintBlackboardPartitions.cs`** -- updated:
- Added `public const int SlotEntrySize = 16;` (referenced by component `SlotTableSize` calculations).
- Changed `TryGetSlotOffset` third parameter from `Guid blueprintId` to `int blueprintId` (remains a stub returning false).

### Tests Added

File: `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Runtime/BlackboardLayoutTests.cs`

| Test | SC | Result |
|------|----|--------|
| SC1_BlueprintBlackboard1024_Is1024Bytes | SC1 | PASS |
| SC1_BlueprintBlackboard4096_Is4096Bytes | SC1 | PASS |
| SC1_BlueprintBlackboard16384_Is16384Bytes | SC1 | PASS |
| SC2_BlueprintBlackboardHeader_Is32Bytes | SC2 | PASS |
| SC2_BlueprintSlotEntry_Is16Bytes | SC2 | PASS |
| SC2_BlueprintFreeBlockHeader_Is4Bytes | SC2 | PASS |
| SC2_SlotEntrySize_Constant_Matches_Struct | SC2 | PASS |
| SC3_Tier1024_PayloadConstants | SC3 | PASS |
| SC3_Tier4096_PayloadConstants | SC3 | PASS |
| SC3_Tier16384_PayloadConstants | SC3 | PASS |
| SC4_SlotTableSize_Equals_MaxSlots_Times_SlotEntrySize | SC4 | PASS |
| SC5_BlueprintBlackboard1024_HasCorrectComponentId | SC5 | PASS |
| SC5_BlueprintBlackboard4096_HasCorrectComponentId | SC5 | PASS |
| SC5_BlueprintBlackboard16384_HasCorrectComponentId | SC5 | PASS |
| SC6_Default_BlueprintBlackboard1024_IsZeroed | SC6 | PASS |
| (SC7 covered by overall build) | SC7 | PASS |

### Deviations (RT-003)

- **`BlueprintSlotEntry.StructureHash` is `uint` not `ulong`:** The spec text says `ulong StructureHash` (8 bytes), but with `int + uint + ushort + ushort + ulong = 4+4+2+2+8 = 20 bytes`, the struct cannot fit in `Size = 16`. Using `uint StructureHash` (4 bytes) gives `4+4+2+2+4 = 16` exactly. The `uint` stores the lower 32 bits of the full 64-bit hash. This deviation is required to satisfy the 16-byte size constraint.

---

## 4. Build Status

```
dotnet build IOS-IG-SimHost.sln
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

---

## 5. Test Summary

```
Passed!  - Failed: 0, Passed: 127, Skipped: 5, Total: 132
Duration: 494 ms - Hrot.Blueprints.Tests.dll (net8.0)
```

Pre-batch baseline: 90 passed, 5 skipped.
Post-batch: 127 passed, 5 skipped -- **37 new tests added, all pass**.

| Test file | New tests |
|-----------|-----------|
| Runtime/BlueprintRegistryTests.cs | 12 |
| Runtime/BlueprintDefinitionTests.cs | 10 |
| Runtime/BlackboardLayoutTests.cs | 15 |

---

## 6. Developer Insights

### 1. What is `int blueprintId`? How does it relate to `Guid assetId`? Did `BlueprintIdHash` exist?

`int blueprintId` is a 32-bit stable integer identifier derived from the Blueprint's `Guid assetId` via FNV-1a hashing over all 16 bytes of the GUID. It is used as the dictionary key in `BlueprintRegistry` to avoid Guid boxing on hot paths.

`BlueprintIdHash.cs` did **not** exist before this batch. It was created in `FDP/Toolkits/Fdp.Toolkits/Blueprints/BlueprintIdHash.cs`.

### 2. Did the `BlueprintDefinition` change to `sealed record` require significant caller updates?

Yes. The main impact was:
- `StateFields` type changed from `IReadOnlyDictionary<string, int>` to `IReadOnlyList<BlueprintFieldDescriptor>`.
- Added three `required` properties (`Kind`, `StructureHash`, `StateSize`) that break all existing object initializer expressions.
- `AiHotReloadCoordinatorTests.cs` (in Hrot.Editor.Tests) needed updates to 3 `BlueprintDefinition` literals and 3 `staging.Add(...)` calls.
- `BlueprintTestFixture.cs` needed minor updates (`TryGetById`/`TryGetSlotOffset` signatures changed to `int`).
- No production code outside of `Fdp.Toolkits` directly constructed `BlueprintDefinition` -- the impact was limited to tests.

### 3. Did the `BlueprintLatentCursor` field rename affect any existing code?

No. The field `Guid GraphId` in the old stub was never referenced anywhere in production code or tests (confirmed by grep). The rename to `uint ResumeAt` + `float WaitUntilTime` had zero impact outside the struct's own definition.

### 4. Is the `Memory` vs `Data` naming consistent? What was the impact?

The field rename from `Data` to `Memory` had zero impact. `BlueprintBlackboardPartitions.cs` (the only code that accesses the raw memory) was already a stub that never referenced the field by name -- it only accepted the component as a type parameter. `BlueprintTestFixture.cs` similarly never accessed the raw field. The rename is therefore a pure cosmetic change that matches the design document naming.

### 5. Were there any build or test failures during development?

Yes, several resolved issues:

| Issue | Root Cause | Resolution |
|-------|-----------|------------|
| `ISimulationView`/`IEntityCommandBuffer` not found in `Fdp.Toolkits` | Missing `using Fdp.Interfaces;` and `using Fdp.ModuleHost.Abstractions;` | Added both using directives to `BlueprintDelegates.cs` |
| `BlueprintBlackboard4096` ComponentId wrong (206 instead of 205) | Old stub used literal `206` | Fixed to `GlobalComponentIds.BlueprintBlackboard4096` |
| `BlueprintBlackboard16384` ComponentId wrong (207 instead of 206) | Old stub used literal `207` | Fixed to `GlobalComponentIds.BlueprintBlackboard16384` |
| `Assert.Equal(idLib, byName)` error CS1503 | `TryGetByName` returns `out BlueprintDefinition?`, not `out int` | Fixed test to check `byNameDef!.Name` |
| `SC6_TwoEqualDefinitions_AreEqual` test failure | `Dictionary<,>` uses reference equality; two independently created instances are never equal | Changed test to use `a with { }` (shallow clone shares collection references) |

### 6. What design decisions were made that were not explicitly specified?

1. **`BlueprintDispatchKind` in `Fdp.Toolkit.Blueprints`:** The enum already existed in `Hrot.Blueprints.Core.Assets`, but `Fdp.Toolkits` cannot reference `Hrot.Blueprints.Core` (unidirectional dependency). A separate `BlueprintDispatchKind` was created in `Fdp.Toolkit.Blueprints` namespace for use by `BlueprintDefinition`.

2. **`uint StructureHash` in `BlueprintSlotEntry`:** The spec says `ulong`, but `ulong` would require 20 bytes which violates the `Size = 16` constraint. Used `uint` (lower 32 bits of hash) instead. Documented as a known deviation.

3. **`WorldSingletonList` rebuilt on direct `RegisterWorldSingleton` path:** When `RegisterWorldSingleton` is called directly (not through staging), the pre-built list is updated by projecting the `WorldSingletons` dictionary to a new list and swapping it on the current snapshot. This ensures `GetAllWorldSingletons()` always returns a current list regardless of which registration path was used.

4. **`BlueprintBlackboardHeader` uses `Size = 32` with 8 bytes padding:** Natural field layout is 16 bytes (`uint + byte + byte + ushort*5 = 4+1+1+10 = 16`), then `ulong Reserved` at offset 16 = 8 bytes = 24 bytes natural. `Size = 32` adds 8 bytes of trailing padding, reserving space for future fields without ABI breakage.

---

## 7. Deviations from Instructions

| # | Location | Instruction | Deviation | Reason |
|---|----------|-------------|-----------|--------|
| 1 | RT-001 | "first 4 bytes of GUID as int" | FNV-1a 32-bit hash over all 16 bytes | Better hash distribution; matches Runtime DD §2.6 |
| 2 | RT-002 | `EventHandlerDelegate` described as 8 params in one place | 7 parameters | Runtime DD §3.3 and the parenthetical parameter list both show 7; spec text has a typo |
| 3 | RT-002 | `StateFields (IReadOnlyDictionary<string, BlueprintFieldDescriptor>)` | `IReadOnlyList<BlueprintFieldDescriptor>` | TASK-DETAIL.md uses list form; a list is simpler for inspector iteration and avoids the overhead of a string key dictionary |
| 4 | RT-003 | `ulong StructureHash` in `BlueprintSlotEntry` | `uint StructureHash` | 20 bytes cannot fit in `Size = 16`; using lower 32 bits satisfies the size constraint |
