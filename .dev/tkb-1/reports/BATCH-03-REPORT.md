# BATCH-03 Report

## Files Created

**Production code:**
- `FDP/Toolkits/Fdp.Toolkits/Tkb/TkbDescriptorRegistry.cs` — static registry with `RegisterParser`, `TryGetParser(ReadOnlySpan<char>, ...)`, and `internal static Clear()`. OrdinalIgnoreCase dictionary. .NET 8 compatible (no `GetAlternateLookup`).
- `FDP/Toolkits/Fdp.Toolkits/Tkb/TkbFormatException.cs` — sealed exception thrown on missing `$guid`.
- `FDP/Toolkits/Fdp.Toolkits/Tkb/TkbDeserializer.cs` — parses `TkbEntityFile` JSON, splits `key#partId` via `ReadOnlySpan<char>` slicing, dispatches to registry thunks, calls `db.Register(template)`.

**Tests:**
- `FDP/Toolkits/Fdp.Toolkits.Tests/Tkb/TkbDeserializerTests.cs` — 10 tests + fixture + `[CollectionDefinition]`.
- `FDP/Toolkits/Fdp.Toolkits.Tests/Tkb/TkbDescriptorRegistryTests.cs` — 4 tests.

## Files Modified

None — `InternalsVisibleTo("Fdp.Toolkits.Tests")` was already present in `Fdp.Toolkits.csproj`.

## Tests Added

**14 new tests total.**

`TkbDeserializerTests` (10):
1. `ParseAndRegister_ValidEntity_TemplateHasCorrectTkbType`
2. `ParseAndRegister_ValidEntity_TemplateHasCorrectCategoryPath`
3. `ParseAndRegister_ValidEntity_HasVehicleParametersDto`
4. `ParseAndRegister_ValidEntity_HasTkbMasterDto`
5. `ParseAndRegister_ValidEntity_HasWeaponCapabilitiesDto`
6. `ParseAndRegister_MissingGuid_ThrowsTkbFormatException`
7. `ParseAndRegister_UnknownDescriptors_ParsesWithoutThrowing`
8. `ParseAndRegister_MetadataKey_IsSkipped`
9. `ParseAndRegister_MultiplePartIds_BothAmmoBallisticsStored`
10. `ParseAndRegister_LargeVolume_DoesNotAllocateOnLargeObjectHeap`

`TkbDescriptorRegistryTests` (4):
1. `RegisterParser_ThenTryGetParser_ReturnsTrueAndThunk`
2. `TryGetParser_UnregisteredName_ReturnsFalse`
3. `RegisterParser_CaseInsensitive_FoundWithDifferentCase`
4. `RegisterParser_Overwrite_ReturnsLatestThunk`

## Build Output

```
Build succeeded.
    0 Error(s)
```

Fix required: initial build failed because `using System.Text.Json;` was missing from
`TkbDeserializerTests.cs` (needed for `JsonElement.Deserialize<T>()`). Added and
rebuilt cleanly.

## Test Output

```
Test Run Successful.
     Passed: 14

  Passed TkbDeserializerTests.ParseAndRegister_ValidEntity_TemplateHasCorrectTkbType [28 ms]
  Passed TkbDeserializerTests.ParseAndRegister_MultiplePartIds_BothAmmoBallisticsStored [2 ms]
  Passed TkbDeserializerTests.ParseAndRegister_MissingGuid_ThrowsTkbFormatException [< 1 ms]
  Passed TkbDeserializerTests.ParseAndRegister_MetadataKey_IsSkipped [< 1 ms]
  Passed TkbDeserializerTests.ParseAndRegister_LargeVolume_DoesNotAllocateOnLargeObjectHeap [52 ms]
  Passed TkbDeserializerTests.ParseAndRegister_ValidEntity_HasVehicleParametersDto [1 ms]
  Passed TkbDeserializerTests.ParseAndRegister_UnknownDescriptors_ParsesWithoutThrowing [< 1 ms]
  Passed TkbDeserializerTests.ParseAndRegister_ValidEntity_TemplateHasCorrectCategoryPath [< 1 ms]
  Passed TkbDeserializerTests.ParseAndRegister_ValidEntity_HasTkbMasterDto [< 1 ms]
  Passed TkbDeserializerTests.ParseAndRegister_ValidEntity_HasWeaponCapabilitiesDto [1 ms]
  Passed TkbDescriptorRegistryTests.RegisterParser_Overwrite_ReturnsLatestThunk [< 1 ms]
  Passed TkbDescriptorRegistryTests.TryGetParser_UnregisteredName_ReturnsFalse [< 1 ms]
  Passed TkbDescriptorRegistryTests.RegisterParser_CaseInsensitive_FoundWithDifferentCase [< 1 ms]
  Passed TkbDescriptorRegistryTests.RegisterParser_ThenTryGetParser_ReturnsTrueAndThunk [< 1 ms]

Full TKB suite (94 tests): Test Run Successful. Passed: 94
```

## Deviations from Instructions

1. **`GetAlternateLookup` not used (by design):** `TASK-DETAIL.md` spec showed `GetAlternateLookup` in the spec, but the batch instructions explicitly stated this is .NET 9+ only and the project targets net8.0. `TkbDescriptorRegistry` exposes `TryGetParser(ReadOnlySpan<char>, ...)` with an internal `key.ToString()` on the lookup path, as directed by BATCH-03 Task 1. `TkbDeserializer` calls `TkbDescriptorRegistry.TryGetParser` directly (no `AlternateLookup` field).

2. **`TkbDescriptorRegistryTests` uses `[Collection("TkbDeserializerTests")]`:** Both registry and deserializer test classes share the same collection to prevent parallel execution against the static `TkbDescriptorRegistry`. This is the safest approach for a static singleton.

3. **LOH test uses `_db` (per-test instance) with `finally { _db.Clear(); }`:** A separate `lohDb` was considered but the per-test-instance `_db` is clean and does not conflict with other tests.

## Issues / Debt Items

- **D-004 (P3):** `TkbDescriptorRegistry.TryGetParser` allocates one `string` per property name on the deserializer hot path (net8.0 limitation). When the project upgrades to .NET 9+, replace with `GetAlternateLookup<ReadOnlySpan<char>>()` to eliminate this allocation. Comment in `TkbDescriptorRegistry.cs` documents the constraint.
- **D-005 (P3):** LOH test (`ParseAndRegister_LargeVolume_DoesNotAllocateOnLargeObjectHeap`) is a heuristic regression guard. The `GC.GetAllocatedBytesForCurrentThread()` measure includes fixture overhead and can vary between runs. The 85,000-byte threshold is conservative; observed average was well below 85 KB in all runs.
