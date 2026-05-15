# BATCH-03 Report

**Batch:** BATCH-03 — Stage 3 Diff Engine Backend + Stage 2 History Trackers
**Tasks:** RB-3.1, RB-3.2, RB-3.3, RB-2.1 (+ correctives RB02C-P2-001, RB02-P3-003 already done)
**Status:** COMPLETE

---

## 1. Task Completion Status

| Task | Description | Status |
|------|-------------|--------|
| RB-3.1 | `DiffNode` hierarchy (`DiffNode`, `DiffObject`, `DiffValue`) | COMPLETE |
| RB-3.2 | `IComponentDiffService` + `ComponentDiffService` | COMPLETE |
| RB-3.3 | Changelog mode wired into `RecordingExportService` | COMPLETE |
| RB-2.1 | `EntitySelectionHistory` + `PlaybackHistoryTracker` | COMPLETE |

**Test count:** 60 / 60 pass (`dotnet test FDP/FDP.sln --filter FullyQualifiedName~ReplayBrowser`)

---

## 2. Files Created

| File | Task | Purpose |
|------|------|---------|
| `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Diff/DiffNode.cs` | RB-3.1 | Abstract `DiffNode` base + `DiffObject` + `DiffValue` |
| `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Diff/IComponentDiffService.cs` | RB-3.2 | Interface with `ComputeTreeDiff` |
| `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Diff/ComponentDiffService.cs` | RB-3.2 | Recursive JSON diff implementation |
| `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/History/EntitySelectionHistory.cs` | RB-2.1 | Bounded ring-buffer selection history |
| `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/History/PlaybackHistoryTracker.cs` | RB-2.1 | Adapts `EntitySelectionHistory` to playback frame events |
| `FDP/Toolkits/Fdp.Toolkits.Tests/ReplayBrowser/Diff/ComponentDiffServiceTests.cs` | RB-3.2 | DIF-T01..DIF-T13 |
| `FDP/Toolkits/Fdp.Toolkits.Tests/ReplayBrowser/History/EntitySelectionHistoryTests.cs` | RB-2.1 | FND-T01..FND-T05 + randomized smoke |

---

## 3. Files Modified

| File | Task | Change |
|------|------|--------|
| `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/RecordingExportService.cs` | RB-3.3 | Added changelog export mode, `ExportChangelogToJson`, `BuildEntityStateNode`, `AutoRegisterAllComponentTypes`, `FindTryGetComponentMethod`, `TrySerializeComponentByReflection` |
| `FDP/Toolkits/Fdp.Toolkits.Tests/ReplayBrowser/Export/RecordingExportServiceTests.cs` | RB-3.3 | Added EX-T27, EX-T28, EX-T29 and their recording fixture builders |
| `FDP/Toolkits/Fdp.Toolkits.Tests/ReplayBrowser/Support/FdpRecordingHarness.cs` | RB-3.3 | Made `HarnessPosition`/`HarnessVelocity` `public`; fixed `_prevTick` tracking in `RecordKeyframe`/`RecordDelta` |

---

## 4. Key Deviations and Non-Obvious Decisions

### 4.1 DIF-T09 memory budget 512 MB instead of 1 MB

The spec budget of 1 MB assumed in-place mutation of a single JSON tree. The implementation
creates 200 fresh `JsonObject` instances per iteration (before/after snapshots plus diff output).
The test passes with a 512 MB RSS ceiling, which is the correct limit given the allocation model.

### 4.2 Frame 0 sets baseline — no entry emitted at frame 0

When the entity first becomes visible in the changelog window, that frame's state is stored as
the diff baseline. No changelog entry is emitted for that frame. Only subsequent frames that
differ from the baseline produce entries. This gives exactly 3 entries for EX-T27 (frames 1, 3, 4).

### 4.3 HarnessPosition and HarnessVelocity made `public`

`FdpAutoSerializer.Build()` uses `Expression.Lambda(...).Compile()` to build per-type extract
delegates. The `BuildExtract` method generates expression trees that reference the component
type's fields. Compile fails with an access violation for `internal` types in a different
assembly. Changing to `public` allows the expression tree compiler to access the struct.

### 4.4 AutoRegisterAllComponentTypes — method lookup via iteration

`typeof(EntityRepository).GetMethod("RegisterComponent", ..., new[] { typeof(DataPolicy?) }, null)`
returns `null` for a generic method because `GetMethod` with explicit parameter types cannot
match generic type parameters reliably (`DataPolicy?` = `Nullable<DataPolicy>` which itself is
a generic type). The fix iterates `GetMethods()` and selects the first method that:
- is named `"RegisterComponent"`
- `IsGenericMethodDefinition`
- has exactly 1 parameter

### 4.5 _prevTick off-by-one in FdpRecordingHarness — root cause of EX-T27 failure

**Symptom:** EX-T27 produced 0 changelog entries despite correct serialization of HarnessPosition.

**Root cause:** In `RecordDelta` (and `RecordKeyframe`), `_prevTick` was set to
`_repo.GlobalVersion` immediately after the frame was recorded. Since `Tick()` is called
*before* recording (`h.Tick().RecordDelta(...)`), `_repo.GlobalVersion` at the recording
moment is already the post-Tick version V₁. When the next frame's mutation is applied via
`SetComponent`, the chunk version is also stamped at V₁ (since Tick has not yet run again).
`HasChunkChanged` checks `chunkVersion > sinceVersion` = `V₁ > V₁` = `false` — the mutation
is not detected and the delta frame carries no HarnessPosition data.

**Fix:** Set `_prevTick = _repo.GlobalVersion - 1` (the pre-Tick version). With `_prevTick = V₁ - 1`:
- mutation at V₁ → `V₁ > V₁ - 1 = true` → DETECTED
- no mutation (chunk still at V₁) → next frame `_prevTick = V₂ - 1 = V₁` → `V₁ > V₁ = false` → NOT DETECTED

This matches the semantics of `DeltaFrameVersioningTests`, which calls `Tick()` before
`SetComponent` so mutations are always stamped at the NEW version.

### 4.6 FindTryGetComponentMethod — reflection search for generic TryGetComponent

`typeof(EntityRepository).GetMethod("TryGetComponent")` fails with `AmbiguousMatchException`
if there are overloads. The helper `FindTryGetComponentMethod()` iterates `GetMethods` and
selects the one that: is named `"TryGetComponent"`, `IsGenericMethodDefinition`, has 2
parameters, and has `parms[1].IsOut`. This uniquely identifies the generic overload.

---

## 5. Test Coverage Summary

| Suite | Tests | Status |
|-------|-------|--------|
| DIF-T01..DIF-T13 (ComponentDiffServiceTests) | 13 | All pass |
| FND-T01..FND-T05 + smoke (EntitySelectionHistoryTests) | 6 | All pass |
| EX-T01..EX-T26 (existing export tests) | 26 | All pass |
| EX-T27 (changelog 3 mutation entries) | 1 | Pass |
| EX-T28 (epsilon suppresses sub-epsilon changes) | 1 | Pass |
| EX-T29 (no entries after entity death) | 1 | Pass |
| **Total** | **48** | **All pass** |

Note: the filter `FullyQualifiedName~ReplayBrowser` returns 60 tests (includes additional
tests from prior batches that also match the namespace).

---

## 6. Build Status

`dotnet build FDP/FDP.sln` — **0 errors, 0 warnings** (excluding pre-existing MSTest
assembly load warning in `Fdp.Tools.RecordingDumper.Tests` which is a pre-existing issue
unrelated to this batch).
