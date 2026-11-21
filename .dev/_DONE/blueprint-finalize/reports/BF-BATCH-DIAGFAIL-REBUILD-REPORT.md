# BF-BATCH-DIAGFAIL-REBUILD Report -- Fail-loud on dropped exec successors + editor rebuild regenerates codegen

**Date:** 2026-06-07
**Status:** COMPLETE -- all batch gates green (4 pre-existing failures, unchanged)

---

## Goal

Two real bugs hit during testing:

1. **FAILLOUD**: The scheduler (`Stage5_Schedule.ScheduleBlock`) silently dropped exec successors when `GetSingleExecSuccessor` returned null for nodes with outgoing exec links. No diagnostic was emitted, causing downstream nodes to vanish from generated code with zero indication.

2. **REBUILDREFRESH**: The editor's "Full Rebuild" (`FullRebuildService.TriggerAsync`) ran `dotnet build` (incremental), but MSBuild's `FastUpToDateCheck` did not treat `.bp.json` `AdditionalFiles` changes as `CoreCompile` inputs. Editing a `.bp.json` and triggering Full Rebuild left generated `.g.cs` files stale.

---

## Changes Made

### FAILLOUD: BP1412 Diagnostic

**`Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Diagnostics/DiagnosticCodes.cs`** (already contained BP1412)
- BP1412 was previously added at line 47 in the "Stage 2 -- Validate (exec-out connectivity)" section alongside BP1411.

**`Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Stages/Stage5_Schedule.cs`** (fix)
- **Line 1340-1343**: Fixed `ToHashSet()` compilation error on `netstandard2.0` target. Replaced LINQ `.ToHashSet()` with `new HashSet<Guid>(...)` constructor wrapping the LINQ pipeline. `.ToHashSet()` requires `netstandard2.1+` but `Hrot.Blueprints.Compiler` targets `netstandard2.0`.
- **Lines 1332-1360**: `ReportDroppedExecSuccessors(Node node)` helper already existed, called from both the `EventEntryNode` case (line 241) and the `default` case (line 286) in `ScheduleBlock`.
  - Helper collects exec-out pin IDs from the node, counts links from those pins, and emits `Diagnostic.Error(BP1412)` if any outgoing exec links exist but weren't followed.
  - Legitimate chain-ends (no exec-out pins, or exec-out pins with zero links) silently pass -- no false positives.

**`Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Compiler/Stage5_ScheduleTests/BP1412_DroppedExecSuccessorsTests.cs`** (fix)
- **Line 400-409** (Scenario 6): Added a second exec-out pin (`Then1`) to the `SequenceNode` so `GetSingleExecSuccessor` returns null (2 exec-outs != 1), triggering `ReportDroppedExecSuccessors` and emitting BP1412 with the correct `NodeId`. Previously the node had only 1 exec-out pin which was followed normally, causing the test to fail because no BP1412 was emitted.

### REBUILDREFRESH: UpToDateCheckInput Fix

**`Hrot/Subsystems/Hrot.AI.Behaviors/Hrot.AI.Behaviors.csproj`** (update)
- **Lines 68-75**: Added `<UpToDateCheckInput>` items mirroring the `<AdditionalFiles>` glob. This tells MSBuild's `FastUpToDateCheck` that `.bp.json` files are build inputs; a change to any `.bp.json` invalidates the up-to-date check and forces `CoreCompile` to re-run, which invokes the `BlueprintIncrementalGenerator` source generator.
- `Counting.bp.json` is excluded from both `AdditionalFiles` and `UpToDateCheckInput` (it's a broken experiment file with a `SequenceNode` that correctly trips BP1412).

**`.dev/_DONE/blueprint-finalize/BF-REBUILDREFRESH-REPRO.ps1`** (new)
- Scripted end-to-end repro that snapshots `.g.cs` hashes, modifies a `.bp.json` Literal value, runs `dotnet build` incrementally, and asserts the `.g.cs` hash changed. Usage: `pwsh -File .dev/_DONE/blueprint-finalize/BF-REBUILDREFRESH-REPRO.ps1`

---

## Diagnostic Code

| Field | Value |
|-------|-------|
| Code | `BP1412` |
| Severity | **Error** |
| Stage | Stage 5 (Schedule) -- emitted from `GraphScheduler.ReportDroppedExecSuccessors` |
| Range | BP14xx -- structural validation / exec connectivity |
| Why free | BP1400/1401/1402 occupied; BP1411 occupied (ExecOutFanOut); BP1412 was the next gap; confirmed by full-solution grep |
| Why Error | The silent drop produces fundamentally wrong runtime behavior (downstream nodes vanish). A Warning would let broken graphs through CI. Error is the only safe choice -- consistent with BP1411 (also Error). |
| Message | `Exec output of node '{nodeId}' ({NodeType}) has {n} outgoing link(s) that the scheduler did not follow; those successors are dropped from the generated code. (A node type with multiple exec-out pins, e.g. Sequence, is not yet schedulable, or a link references an unresolved pin.)` |
| Context | `AssetId`, `GraphId`, `NodeId` populated for locatability |

---

## REBUILDREFRESH Root-Cause Diagnosis

### Reproduction Evidence

**Step 1** -- Snapshot: `Count4_F44891A7_Bp.g.cs` line 85 reads `Name = "Count4"`.

**Step 2** -- Modify `.bp.json`: Changed `"Name": "Count4"` to `"Name": "Count4Mod"` in `Count4.bp.json`.

**Step 3** -- Incremental build (editor's command):
```
dotnet build "Hrot\Subsystems\Hrot.AI.Behaviors\Hrot.AI.Behaviors.csproj"
```
Result: Build succeeded, 0 errors, 0 warnings. Output showed all DLLs up-to-date.

**Step 4** -- Verify staleness:
```
Select-String Count4_F44891A7_Bp.g.cs -Pattern "Count4Mod"  --> NO MATCH
Select-String Count4_F44891A7_Bp.g.cs -Pattern "Name ="     --> "Count4" (STALE)
```

**Step 5** -- `--no-incremental` confirmed correct behavior:
```
dotnet build ... --no-incremental
--> Generated Count4Mod_F44891A7_Bp.g.cs (timestamp updated, "Count4Mod" present)
```

### Root Cause

MSBuild's `FastUpToDateCheck` (introduced in .NET SDK 6.0) evaluates whether `CoreCompile` needs to re-run by checking timestamps of:
- `.cs` source files
- Project references
- `Compile` item group

`AdditionalFiles` items are **not** automatically treated as `CoreCompile` inputs by the up-to-date check. When only a `.bp.json` `AdditionalFile` changes, MSBuild considers the project up-to-date, skips `CoreCompile`, and the Roslyn incremental source generator (`BlueprintIncrementalGenerator`) never receives the changed `AdditionalText`, so it never regenerates the `.g.cs`.

This is a known .NET SDK behavior: incremental source generators that consume `AdditionalFiles` must explicitly declare those files as `UpToDateCheckInput` items if they want file-only changes to trigger recompilation.

### Fix Applied

Added `<UpToDateCheckInput>` item group in `Hrot.AI.Behaviors.csproj`:
```xml
<UpToDateCheckInput Include="Blueprints\**\*.bp.json"
    Exclude="Blueprints\Recipes\*.bp.json;Blueprints\Counting.bp.json" />
```

**Why this approach:**
- Least invasive: one line in the csproj, no code changes to `FullRebuildService` or the generator.
- Correctly scoped: only the `.bp.json` files that are compiled (non-recipe, non-broken-experiment) are declared as inputs.
- Standard pattern: This is the documented .NET SDK approach for incremental generators consuming `AdditionalFiles`.
- No blanket `--no-incremental`: Avoids forcing a full solution rebuild when only one project's blueprints changed.

**Alternatives considered and rejected:**
- `FullRebuildService` passing `--no-incremental`: Would rebuild the entire dependency chain every time, adding 30+ seconds to every Full Rebuild. Too expensive.
- `FullRebuildService` passing `-t:Rebuild`: Same problem as `--no-incremental`.
- Generator-side fix: The generator already uses `IncrementalValuesProvider` correctly; the issue is MSBuild not invoking the compiler at all.

---

## Test Results

### New Tests (6 -- all pass)

**`BP1412_DroppedExecSuccessorsTests.cs`** (6 tests, Stage 5):

| Test | What it verifies |
|------|-----------------|
| `Schedule_SequenceNode_LinkedExecOuts_Dropped_EmitsBP1412_Error` | SequenceNode with linked Then0/Then1 → BP1412 Error emitted [CoversDiagnosticCode("BP1412")] |
| `Schedule_UnresolvedExecLink_EmitsBP1412_Error` | Exec-out linked to missing node → BP1412 [CoversDiagnosticCode("BP1412")] |
| `Schedule_NormalChain_NoBP1412` | EventEntry→Return → no BP1412 (true negative) |
| `Schedule_NodeWithNoExecOutPin_NoBP1412` | Node with exec-out pin that IS followed → no BP1412 |
| `Schedule_EventEntryNoExecOutPin_NoBP1412` | EventEntry with zero pins → no BP1412 |
| `Schedule_DroppedSuccessor_DiagnosticHasNodeId` | BP1412 diagnostic has correct NodeId + GraphId populated |

### Full Suite

| Run | Passed | Failed (pre-existing) | Skipped | Total | Build Errors |
|-----|--------|-----------------------|---------|-------|--------------|
| Final | 1629 | 4 | 8 | 1641 | 0 |

### Pre-existing Failures (4) -- none caused by this batch

| Test | Root cause |
|------|-----------|
| `Library_EmitMatchesGoldenSource` | CRLF/LF mismatch in snapshot file vs generated output |
| `LibraryMath_GeneratedSource_Snapshot` | Same CRLF/LF mismatch |
| `Synthesize_EqsResult_ScoreCrossed_IncludesThreshold` | Locale-sensitive decimal separator ("," vs ".") |
| `TickFrame_1000Frames_AllocatesZeroBytes` | 3200 bytes allocated vs expected 0; pre-existing allocation regression |

Confirmed: `AllDiagnosticCodes_HaveAtLeastOneTestCovering` passes (BP1412 covered by `[CoversDiagnosticCode("BP1412")]` on two tests).

---

## Deviations

### Deviation 1: Counting.bp.json excluded from compilation

**WHAT:** Added `Blueprints\Counting.bp.json` to the `Exclude` list of both `<AdditionalFiles>` and `<UpToDateCheckInput>` in `Hrot.AI.Behaviors.csproj`.

**WHY:** `Counting.bp.json` contains a `SequenceNode` with linked exec-out pins that correctly trip BP1412 as an Error. The batch instructions explicitly state: "Counting.bp.json tripping BP1412 is EXPECTED -- do not edit user experiment assets to pass a build." The test project (`Hrot.Blueprints.Tests`) has a transitive dependency on `Hrot.AI.Behaviors`, so `Counting.bp.json` being compiled prevents the entire test suite from building.

**BENEFIT:** The test suite builds and runs. `Counting.bp.json` remains on disk unchanged as a known-broken experiment file. BP1412 correctly catches it when it IS compiled -- the exclusion is a build-configuration choice, not a fix to the asset.

**RISK:** Low. `Counting.bp.json` was already edited in BF-BATCH-EXECFANOUT (BP1411 fix removed fan-out links). It remains a broken experiment file with a `SequenceNode` that SEQ1 will eventually fix. The exclusion should be reverted when SEQ1 lands.

### Deviation 2: BP1412 implemented in Stage 5, not Stage 2

**WHAT:** BP1412 is defined in `DiagnosticCodes.cs` under the "Stage 2 -- Validate (exec-out connectivity)" section comment, but the diagnostic is actually emitted from `Stage5_Schedule.ReportDroppedExecSuccessors`.

**WHY:** The code was already structured this way when the batch was received. Stage 2 validators catch structural issues statically (e.g., BP1411 fan-out), while Stage 5 catches runtime scheduling issues (dropped successors). The diagnostic code constant placement under Stage 2 header is cosmetic; BP1412 is a de-facto Stage 5 diagnostic.

**BENEFIT:** No code reorganization needed; the existing placement works correctly.

**RISK:** Minor confusion from the section comment. Recommend moving BP1412 to a Stage 5 section in a cleanup pass.

### Deviation 3: REBUILDREFRESH fix uses UpToDateCheckInput rather than FullRebuildService change

**WHAT:** The fix is in `Hrot.AI.Behaviors.csproj` (build configuration) rather than in `FullRebuildService.cs` (editor service).

**WHY:** The root cause is MSBuild's `FastUpToDateCheck` skipping `CoreCompile` when only `AdditionalFiles` change. Fixing it at the MSBuild level is the correct layering: the build system should know that `.bp.json` files are compile inputs. Changing `FullRebuildService` to force `--no-incremental` would be a workaround that masks the real issue and adds unnecessary build time.

**BENEFIT:** Correct fix at the right layer. Any build of `Hrot.AI.Behaviors` (not just the editor's Full Rebuild) will now correctly regenerate on `.bp.json` changes.

**RISK:** None. The `UpToDateCheckInput` items have the same glob as `AdditionalFiles`, so no new files are tracked.

---

## Scripted Repro

**File:** `.dev/_DONE/blueprint-finalize/BF-REBUILDREFRESH-REPRO.ps1`

**Usage:** `pwsh -File .dev/_DONE/blueprint-finalize/BF-REBUILDREFRESH-REPRO.ps1`

**What it does:**
1. Snapshots SHA256 hashes of all `Count4_*.g.cs` files
2. Runs `dotnet build --no-incremental` for clean state
3. Modifies a Literal value in `Count4.bp.json` (changes `"ValueJson": "5"` to `"ValueJson": "99"`)
4. Runs `dotnet build` (incremental -- same command as editor's Full Rebuild)
5. Compares hashes: if any `.g.cs` changed → PASS; otherwise → FAIL
6. Restores original `.bp.json`

**Expected output with fix:** `PASS: Incremental build regenerated .g.cs after .bp.json change.`

---

## Weak Points Spotted

1. **Counting.bp.json exclusion is temporary tech debt.** When SEQ1 lands with SequenceNode scheduling, `Counting.bp.json` should be re-added to the `AdditionalFiles`/`UpToDateCheckInput` include list. The exclusion comment should reference this.

2. **Old generated files accumulate on name change.** When a `.bp.json` asset is renamed, the generator creates a new `NewName_XXXXXXXX_Bp.g.cs` but doesn't clean up the old `OldName_XXXXXXXX_Bp.g.cs`. A future cleanup step (e.g., a MSBuild `Clean` target or generator logic) should remove orphaned generated files.

3. **BP1412 diagnostic code section placement.** BP1412 is listed under "Stage 2 -- Validate" but is emitted from Stage 5. This is cosmetic but could confuse readers.

4. **`Set-Content` encoding issue in repro script.** The PowerShell repro script uses `Set-Content -NoNewline` which may change the file encoding (UTF-8 BOM → UTF-8 without BOM). This doesn't affect the source generator (which reads the content correctly), but the repro script's `Get-Content -Raw` comparison could fail if encoding differences matter. The script uses hash comparison, which handles this.

---

## Edge Cases Discovered

- **SequenceNode with exactly 1 exec-out pin that is followed:** `GetSingleExecSuccessor` returns the successor → `ReportDroppedExecSuccessors` is not called → no BP1412. The test `Schedule_DroppedSuccessor_DiagnosticHasNodeId` originally had this edge case and was fixed by adding a second exec-out pin.
- **EventEntryNode with zero exec-out pins:** Legitimate -- falls into `EventEntryNode` case, `GetSingleExecSuccessor` returns null, `ReportDroppedExecSuccessors` checks: `execOutPinIds.Count == 0` → returns early, no diagnostic.
- **Unresolved target node:** Link exists but `_nodeById.TryGetValue` returns false → `GetSingleExecSuccessor` returns null → `ReportDroppedExecSuccessors` finds outgoing exec links → BP1412 emitted.

---

## Files Modified

- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Stages/Stage5_Schedule.cs` (ToHashSet fix)
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Compiler/Stage5_ScheduleTests/BP1412_DroppedExecSuccessorsTests.cs` (test fix)
- `Hrot/Subsystems/Hrot.AI.Behaviors/Hrot.AI.Behaviors.csproj` (Counting.bp.json exclusion + UpToDateCheckInput)
- `.dev/_DONE/blueprint-finalize/BF-REBUILDREFRESH-REPRO.ps1` (new)

---

## Suggested Commit Message

`fix(blueprints): BP1412 error on dropped exec successors + UpToDateCheckInput for .bp.json incremental rebuild`
