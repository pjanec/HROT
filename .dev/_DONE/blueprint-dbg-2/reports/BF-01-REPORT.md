# BF-01 Report: Fix node-granular inspector crash — scratch repo missing recordable-non-snapshotable component types

**Date:** 2026-06-10
**Branch:** `blueprint-integ-1`

---

## Change summary

**File:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/BlueprintDebugSession.cs`, method `RestorePointerToScratch()` (~line 723).

**Before:**
```csharp
_scratchRepo.SyncFrom(_liveRepo);
```

**After:**
```csharp
// Seed registrations + live baseline so PlaybackSystem.ApplyFrame finds all tables.
// Use includeTransient: true so SyncFrom uses GetSnapshotableMask(true) = all registered
// component types, which is a superset of the recordable types the keyframe contains.
// Without this, components marked [DataPolicy(DataPolicy.NoSnapshot)] are recordable but
// NOT snapshotable — the keyframe captures them but the scratch repo never registered
// the type → PlaybackSystem.ApplyChunkData throws "type ID not found".
_scratchRepo.SyncFrom(_liveRepo, includeTransient: true);
```

**Rationale:** `SyncFrom` with no `includeTransient` uses `GetSnapshotableMask(false)`, which returns only snapshotable type IDs. Components marked `[DataPolicy(DataPolicy.NoSnapshot)]` are recordable (captured by `RecordKeyframe` → `GetRecordableMask()`) but NOT snapshotable — so the keyframe contains them but the scratch repo never registers the type. `PlaybackSystem.ApplyChunkData` then throws `InvalidOperationException`. With `includeTransient: true`, `GetSnapshotableMask(true)` returns `ComponentTypeRegistry.GetAllIds()` — all registered types, a superset of recordable types.

**No other files touched.** No changes to FlightRecorder, masks, or any other batch's code.

---

## Test

**New file:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Debug/SubTickRestoreRegistrationTests.cs`

One test: `RestoreTo_IncludeTransient_RegistersRecordableNonSnapshotableTypes`

### What it proves

1. **Mask-level proof (step 4):** Defines `[ComponentId(504)] [DataPolicy(DataPolicy.NoSnapshot)] struct NoSnapshotProbe` — recordable but NOT snapshotable. Asserts `GetSnapshotableMask(false)` does NOT contain its type ID, proving the mask difference the fix relies on.

2. **Runtime proof (step 4 alt):** A scratch repo seeded with `SyncFrom(repo)` (OLD, no `includeTransient`) → `RestoreTo` throws `InvalidOperationException`, reproducing the exact crash.

3. **Fix proof (step 5):** A scratch repo seeded with `SyncFrom(repo, includeTransient: true)` (NEW) → `RestoreTo` does NOT throw. The restored NormalInt value equals the expected value (99), and the NoSnapshotProbe component is present with its correct value (42).

### Design decisions
- Uses `[ComponentId(504)]` and `[ComponentId(505)]` to avoid conflicts with existing test components (501–503 in `SubTickSnapshotRecorderTests`).
- Both mask-level and runtime assertions are included per the batch's fallback guidance — the mask assertion is the more robust cross-environment proof.
- No global state cleanup needed since ComponentId values are unique.

---

## Test results

### New test (isolated)
```
dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests --filter "FullyQualifiedName~SubTickRestoreRegistrationTests"
Passed!  - Failed:     0, Passed:     1, Skipped:     0, Total:     1, Duration: 61 ms
```

### Full suite
```
dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests
Failed!  - Failed:     7, Passed:  1735, Skipped:     8, Total:  1750, Duration: 31 s
```

**All 7 failures are the documented pre-existing reds:**

| Failure | Status |
|---------|--------|
| `AiPrimitive_EmitMatchesGoldenSource(assetName: "MoveToAndFire")` | Pre-existing |
| `AiPrimitive_EmitMatchesGoldenSource(assetName: "HasVisibleTarget")` | Pre-existing |
| `Stage8_PdbContainsEmbeddedSource` | Pre-existing |
| `Stage8_RoslynCompiler_ProducesNonEmptyPeAndPdb` | Pre-existing |
| `TickFrame_1000Frames_AllocatesZeroBytes` | Pre-existing |
| `MoveToAndFire_GeneratedSource_Snapshot` | Pre-existing |
| `WhenNode_ZeroAllocOnHotPath` | Pre-existing |

**Zero new failures.** No regressions introduced.

---

## Known issues / notes

None. The fix is a one-line parameter change with a comment; the test is a single focused regression test. No deviations from the batch prescription.
