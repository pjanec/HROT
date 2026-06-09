# BATCH-CF6 REPORT: Real Stepping via Temporary Breakpoints

**Batch:** BATCH-CF6  
**Date:** 2026-06-09  
**Branch:** `blueprint-integ-1`  
**Status:** ✅ COMPLETE — All gates passed

---

## Summary

Implemented real stepping (Step Over/Into/Out) using temporary breakpoints on the next exec node(s), replacing the broken `_stepMode` tick-matching approach. When a graph is registered for the paused asset, stepping now:

1. Computes immediate exec successors via `ExecSuccessors`
2. Sets invisible one-shot temporary breakpoints on those successors
3. Suppresses user breakpoints during the step pass
4. Resumes (not single-tick) — the temp target fires, pauses, and auto-clears

When no graph is registered, the legacy `_stepMode` behavior is preserved as a backward-compatible fallback.

---

## Files Changed

### New Files

| File | Purpose |
|------|---------|
| `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Debug/ExecSuccessors.cs` | Computes next exec node IDs from graph links |
| `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Debug/CF6_SteppingTests.cs` | 10 tests covering all CF-6 requirements |

### Modified Files

| File | Changes |
|------|---------|
| `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/BlueprintDebugSession.cs` | Added `BreakpointTarget` record, `_graphs` storage, temp BP mechanism, rewritten `OnNodeEnter` + `Step` methods |
| `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Host/BlueprintDocumentFactory.cs` | Register all graphs on debug session when building blueprint documents |

---

## Implementation Details

### Task 1: ExecSuccessors Utility

**File:** `Hrot.Blueprints.Editor/Debug/ExecSuccessors.cs`

Mimics the compiler's `GetSingleExecSuccessor` / `GetBranchSuccessors` pattern from `Stage5_Schedule.cs`:
- Finds the node in the graph by ID
- Locates all exec-output pins (`p.IsExec && p.Direction == "Out"`)
- Follows each exec-output link to collect target node IDs
- Returns `IReadOnlyList<Guid>` of successors
- Terminal nodes (Return, no exec-out pins) return empty

### Task 2: Graph Registration

**File:** `BlueprintDebugSession.cs`

- Added `private readonly Dictionary<Guid, Graph> _graphs` keyed by graph ID
- Added `public void RegisterGraph(Graph graph)` method
- Graphs persist through Continue/Resume cycles; cleared on Detach

### Task 3: Temporary Breakpoint Mechanism

**File:** `BlueprintDebugSession.cs`

- Added `BreakpointTarget` public readonly record struct: `(Guid AssetId, Guid GraphId, Guid NodeId)`
- Added `_tempBreakpoints` dictionary keyed by probe-id string
- `SetTemporaryBreakpoints(IEnumerable<BreakpointTarget>)` — translates authored → block-probe IDs via `BreakpointTargets`, creates invisible one-shot breakpoints
- `ClearTemporaryBreakpoints()` — clears all temps
- `HasTemporaryBreakpoints` — boolean check
- `ResolveProbeId(Guid assetId, Guid authoredNodeId)` — private helper for auth→block-probe translation

### Task 4: OnNodeEnter Rewrite

**File:** `BlueprintDebugSession.cs`

Modified `OnNodeEnter` to:
1. Check temporary breakpoints **FIRST**
2. If temps active and this node matches → pause + auto-clear ALL temps
3. If temps active but no match → skip user BP matching entirely (suppression)
4. If no temps → normal user BP matching (unchanged)
5. Legacy `_stepMode` matching retained but only activates when `LegacyStepOneTick` was invoked (fallback)

### Task 5: Step Methods Rewrite

**File:** `BlueprintDebugSession.cs`

- `StepOver/StepInto/StepOut` all converge to `Step(StepMode)`:
  - When graph registered: compute successors, set temp BPs, resume
  - When no graph: fall back to `LegacyStepOneTick(fallbackStepMode)` for backward compatibility
- `Continue()` now calls `ClearTemporaryBreakpoints()` to discard leftover temps
- Added `LegacyStepOneTick(StepMode)` that preserves old `_stepMode` + `RequestStepOneTick` behavior

### Task 6: EditorSubsystem Graph Registration

**File:** `BlueprintDocumentFactory.cs`

Graphs are registered on the debug session inside `Build()` where `bpAsset.Graphs` is already available. This avoids re-loading the `.bp.json` file from disk.

---

## Test Results

### CF6 Tests: 10/10 passed

| Test | Status |
|------|--------|
| `ExecSuccessors_LinearChain_ReturnsSingleSuccessor` | ✅ |
| `ExecSuccessors_TerminalNode_ReturnsEmpty` | ✅ |
| `ExecSuccessors_UnknownNode_ReturnsEmpty` | ✅ |
| `TempBreakpoints_HitAndAutoClear` | ✅ |
| `UserBreakpoints_SuppressedWhenTempsActive` | ✅ |
| `Step_FromNodeWithSuccessors_SetsTempBPsAndResumes` | ✅ |
| `Continue_ClearsLeftoverTempBreakpoints` | ✅ |
| `Step_TerminalNode_CallsContinue` | ✅ |
| `TempBreakpoints_NotExposedInGetBreakpoints` | ✅ |
| `Step_NoGraphRegistered_FallsBackToSingleTick` | ✅ |

### Full Blueprints Test Suite: 7 pre-existing, 0 new failures

| Pre-existing Failure | Type |
|---------------------|------|
| `WhenNodePerfTests.WhenNode_ZeroAllocOnHotPath` | Perf benchmark |
| `AiPrimitive_EmitMatchesGoldenSource("HasVisibleTarget")` | Golden snapshot |
| `AiPrimitive_EmitMatchesGoldenSource("MoveToAndFire")` | Golden snapshot |
| `MoveToAndFire_GeneratedSource_Snapshot` | Golden snapshot |
| `TickFrame_1000Frames_AllocatesZeroBytes` | Perf benchmark |
| `Stage8_PdbContainsEmbeddedSource` | Compiler |
| `Stage8_RoslynCompiler_ProducesNonEmptyPeAndPdb` | Compiler |

**Net-new failures: 0** ✅

### Build: 0 errors ✅

```
dotnet build IOS-IG-SimHost.sln -c Debug → Build succeeded.
```

---

## Success Criteria Check

- [x] Build 0 errors
- [x] Hrot.Blueprints.Tests → 7 pre-existing, 0 new
- [x] All 6+ CF6 tests pass (10 tests written, all pass)
- [x] ExecSuccessors correctly follows exec wires
- [x] Temp BPs are invisible (not in `GetBreakpoints()`) — verified by `TempBreakpoints_NotExposedInGetBreakpoints`
- [x] Temp BPs are one-shot (auto-clear on hit) — verified by `TempBreakpoints_HitAndAutoClear`
- [x] User BPs suppressed during step pass — verified by `UserBreakpoints_SuppressedWhenTempsActive`
- [x] Step computes correct successors — verified by `Step_FromNodeWithSuccessors_SetsTempBPsAndResumes`
- [x] Step resumes (not single-tick) — verified by `Step_FromNodeWithSuccessors_SetsTempBPsAndResumes` (asserts `ResumeCount + 1`, `StepRequestCount` unchanged)
- [x] Continue clears leftover temps — verified by `Continue_ClearsLeftoverTempBreakpoints`
- [x] Graph registered in session for stepping — graphs registered in `BlueprintDocumentFactory.Build`

---

## Zed Checklist

- [x] BreakpointTarget translation: temp BPs translated through `BreakpointTargets` (authored→block-probe)
- [x] `_stepMode` fields preserved as legacy; `LegacyStepOneTick` preserves backward compat
- [x] Pin.Direction uses `"Out"` for output (matching the compiler's `GetSingleExecSuccessor`)
- [x] Graph registered when blueprint document is opened
- [x] All existing step tests (`StepTests`, `StepOutEdgeCaseTests`) continue to pass via fallback path

---

## Test Commands

```bash
dotnet build IOS-IG-SimHost.sln -c Debug
dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests -c Debug --filter "FullyQualifiedName~Debug"
dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests -c Debug --filter "FullyQualifiedName~CF6"
```
