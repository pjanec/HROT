# BATCH-18: BATCH-17 Fix-Up + TASK-DBG-003 -- Breakpoints and Step Semantics

**Batch Number:** BATCH-18
**Tasks:** CT0-A (GcRetries fix), CT0-B (NodeHistoryEntry struct fix), TASK-DBG-003
**Phase:** 5 -- Debug Protocol
**Estimated Effort:** 1 day (CT0) + 3-4 days (DBG-003) = ~4-5 days total
**Priority:** HIGH
**Dependencies:** BATCH-17 (DebugMapIndex, ExecutionHistory, RegisterDebugMap/UnregisterDebugMap in place)

---

## 0. Onboarding

### Required Reading (IN ORDER)

1. `.dev/blueprints-1/reviews/BATCH-17-REVIEW.md` -- root cause of the 2-5 failures you are fixing.
2. `.dev/blueprints-1/TASK-DETAIL.md` §DBG-003 -- full scope and success conditions for breakpoints + steps.
3. `.dev/blueprints-1/Blueprint_Subsystem_Debug_Protocol_Detailed_Design.md` §6 (Breakpoints) and §7 (Step Semantics).
4. `.dev/blueprints-1/Blueprint_Subsystem_Debug_Protocol_Detailed_Design_InlinePatches.md` -- Patch 1 (soft-pause semantics) supersedes §6.4, §6.5, §7.x. Read Patch 1 first.
5. `.dev/blueprints-1/DEBT-TRACKER.md` -- DEBT-019, DEBT-020.

### Source Code Locations

- `BlueprintTestFixtureOptions.cs`: `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/BlueprintTestFixtureOptions.cs`
- `IBlueprintDebugSession.cs`: `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/IBlueprintDebugSession.cs`
- `BlueprintDebugSession.cs`: `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/BlueprintDebugSession.cs`
- `ExecutionHistory.cs`: `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/ExecutionHistory.cs`
- `DebugMapTests.cs`: `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Debug/DebugMapTests.cs`
- New test file: `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Debug/BreakpointTests.cs`
- New test file: `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Debug/StepTests.cs`

### Report Submission

Submit to: `.dev/blueprints-1/reports/BATCH-18-REPORT.md`

If questions: `.dev/blueprints-1/questions/BATCH-18-QUESTIONS.md`

---

## 1. Corrective Task CT0-A: Increase GcReclaimRetries to 30

**File:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/BlueprintTestFixtureOptions.cs`

**Problem:** BATCH-17 added 16 new tests (total 390). The additional GC pressure from new `BlueprintDebugSession` instances (each pre-allocates 256-slot `ExecutionHistory` arrays) causes HotReload ALC GC reclaim tests to fail 2-5 times per full-suite run. The current 20-retry limit is insufficient.

**Fix:** Increase `GcReclaimRetries` default from 20 to 30:
```csharp
public int GcReclaimRetries { get; init; } = 30;  // was 20; bumped for BATCH-17 GC pressure
```

---

## 2. Corrective Task CT0-B: Change NodeHistoryEntry to readonly record struct

**File:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/IBlueprintDebugSession.cs`

**Problem:** `NodeHistoryEntry` is defined as `sealed record` (a class). Every call to `BlueprintDebugSession.OnNodeEnter` allocates `new NodeHistoryEntry(nodeId, tick, simTime)` on the heap. This allocation pressure compounds with 16 new tests, causing GC failures.

**Fix:** Change to `readonly record struct`:
```csharp
public readonly record struct NodeHistoryEntry(string NodeId, uint Tick, float SimTime);
```

The `ExecutionHistory._buffer` is `NodeHistoryEntry[]`. Changing from class to struct means each array slot stores the value directly (inline layout), eliminating per-entry heap allocation.

After this change:
- `ExecutionHistory._buffer = new NodeHistoryEntry[capacity]` allocates one array; no per-slot objects.
- `hist.Record(new NodeHistoryEntry(nodeId, tick, simTime))` is a stack allocation + array copy.

**Check:** The `ExecutionHistory_Record_ZeroAllocation` test in `DebugMapTests.cs` must now measure zero allocation INCLUDING the `new NodeHistoryEntry(...)` construction. Update the test to include the construction inside the measured region (currently it measures only `hist.Record(entry)` where `entry` was pre-allocated outside). Move `new NodeHistoryEntry(...)` into the warm-up and measured calls:

```csharp
[MethodImpl(MethodImplOptions.NoInlining)]
private static void RecordEntry(ExecutionHistory hist, string nodeId, uint tick)
    => hist.Record(new NodeHistoryEntry(nodeId, tick, 0f));
```
Then measure `RecordEntry(hist, "n1", 1u)` with `GC.GetAllocatedBytesForCurrentThread()`.

After the struct change, this call chain should be zero-allocation.

---

## 3. Verification: all BATCH-17 tests must pass

After CT0-A and CT0-B:

```powershell
# Run HotReload tests (must be 0 failures)
dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests --filter "FullyQualifiedName~HotReload" -v minimal

# Full suite (must be 0 failures)
dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests -v minimal
```

Expected: 390 total / 0 fail / 5 skip. If HotReload tests still fail occasionally, increase `GcReclaimRetries` to 40.

---

## 4. TASK-DBG-003: Breakpoints and Step Semantics

See `TASK-DETAIL.md §DBG-003` for full scope, success conditions, and constraints.

**Design references:**
- Debug Protocol DD §6 for breakpoint implementation
- Debug Protocol DD §7 for step semantics
- Inline Patches Patch 1 for **soft-pause model** (supersedes §6.4, §6.5, §7.x)

### 4.1 Key types to implement

**`StepMode` enum** in `Hrot.Blueprints.Core.Debug` (add to `IBlueprintDebugSession.cs` or a new `StepMode.cs`):
```csharp
public enum StepMode { None, Over, Into, Out }
```

### 4.2 Replace stub breakpoint storage with proper per-asset structure

Current `BlueprintDebugSession._nodeBreakpoints` is a raw `HashSet<string>` with no asset association. Replace it with a proper `Breakpoint` dictionary (note: `Breakpoint` record already defined in `IBlueprintDebugSession.cs`):

```csharp
private readonly Dictionary<BreakpointId, Breakpoint>   _breakpoints    = new();
private readonly Dictionary<string, Breakpoint>         _bpByNodeString = new(StringComparer.Ordinal);
private int _nextBpId = 1;
```

Update `SetBreakpoint(Guid assetId, Guid graphId, Guid nodeId)`:
- Creates a `Breakpoint` record with new `BreakpointId(_nextBpId++)`.
- Stores in both dictionaries: `_breakpoints[bp.Id]` and `_bpByNodeString[nodeId.ToString("D")]`.
- Returns the `BreakpointId`.

Update `ClearBreakpoint(BreakpointId id)`: removes from both dictionaries.

Update `ClearAllBreakpoints()`: clears both dictionaries.

Implement `GetBreakpoints()`: returns `_breakpoints.Values.ToList().AsReadOnly()` (or equivalent read-only view).

Fix `IsAnyBreakpointActive`: `_breakpoints.Count > 0`.

This also fixes **DEBT-020** (the brittle `nodeId.ToString()` matching stub).

### 4.3 Implement OnNodeEnter with full breakpoint check (Patch 1 semantics)

Update `OnNodeEnter` to:
1. Record execution history (already implemented in BATCH-17).
2. Look up `_bpByNodeString.TryGetValue(nodeId, out var bp)`.
3. If found AND `!_isPaused`: call `HandleBreakpointHit(self, bp, nodeId)`.
4. If step mode is active: check step-mode matching (see §4.5).

```csharp
// New state fields
private bool       _isPaused;
private StepMode   _stepMode = StepMode.None;
private Entity     _stepFromEntity;
private int        _stepFromDepth;  // used for StepOver/StepOut

// Breakpoint hit (Patch 1: no blocking, returns immediately)
private void HandleBreakpointHit(Entity self, Breakpoint bp, string nodeId)
{
    _isPaused        = true;
    _pausedAt        = bp;
    _pausedOnEntity  = self;
    _stepMode        = StepMode.None;
    _stepFromEntity  = default;
    _stepFromDepth   = 0;
    _timeController.RequestPause();
    var assetId = _debugMaps.TryGetValue(/* ... */ ..., out var idx) ? idx.AssetId : Guid.Empty;
    OnBreakpointHit?.Invoke(new BreakpointHit(
        self,
        nodeId,
        assetId,
        _view.Time,
        _view.Tick));
    OnSessionStateChanged?.Invoke();
}
```

Key constraint per Patch 1: `_isPaused` guards re-entrant pause. If a second entity hits the same breakpoint while already paused, ignore it (`if (found && !_isPaused)`).

### 4.4 Implement Continue, Pause, hit-count tracking

**`Continue()`**: clears `_isPaused = false`, clears `_pausedAt = null`, clears `_pausedOnEntity = null`, calls `_timeController.RequestResume()`, fires `OnSessionStateChanged`.

**`Pause()`**: calls `_timeController.RequestPause()`, sets `_isPaused = true`, fires `OnSessionStateChanged`.

**`IsPaused` property**: return `_isPaused`.

**`PausedAt` property**: return `_pausedAt`.

**Hit count**: increment `Breakpoint.HitCount` on each hit. Since `Breakpoint` is a `sealed record`, create a new record with `HitCount + 1` and update `_breakpoints[bp.Id]` and `_bpByNodeString[nodeId]`. Fire `OnBreakpointListChanged?.Invoke(assetId)` after updating hit count.

### 4.5 Implement StepOver, StepInto, StepOut (Patch 1 soft-pause)

Per design doc §7 adapted for Patch 1 (step = request one tick, then match next OnNodeEnter):

**`StepOver()`**:
- Sets `_stepMode = StepMode.Over`, `_stepFromEntity = _pausedOnEntity`, `_stepFromDepth = _currentCallDepth.GetValueOrDefault(_pausedOnEntity, 0)`.
- Sets `_isPaused = false`.
- Calls `_timeController.RequestStepOneTick()`.
- Fires `OnSessionStateChanged`.

**`StepInto()`**: Same as StepOver but `_stepMode = StepMode.Into`.

**`StepOut()`**: Same but `_stepMode = StepMode.Out`.

**Step matching in `OnNodeEnter`** (called after recording history, before BP check):
```csharp
if (_stepMode != StepMode.None && self == _stepFromEntity)
{
    int depth = _currentCallDepth.GetValueOrDefault(self, 0);
    bool matched = _stepMode switch
    {
        StepMode.Into => true,                           // any next node for this entity
        StepMode.Over => depth <= _stepFromDepth,        // same or shallower depth
        StepMode.Out  => depth < _stepFromDepth,         // strictly shallower
        _ => false
    };
    if (matched)
    {
        _stepMode = StepMode.None;
        // Use a pseudo-breakpoint (no real BP, just fire the event)
        var pseudoBp = new Breakpoint(default, Guid.Empty, Guid.Empty, nodeId, 0, true);
        HandleBreakpointHit(self, pseudoBp, nodeId);
    }
}
```

### 4.6 Implement OnPeerCallEnter/Exit for call depth tracking

`_currentCallDepth` per-entity:
```csharp
private readonly Dictionary<Entity, int> _currentCallDepth = new();
```

`OnPeerCallEnter(Entity entity, string targetAssetName, string targetGraphName)`:
- Increment `_currentCallDepth[entity]` (default 0 if missing).
- (Optional for now) Fire `OnPeerCallChanged` if/when that event is added.

`OnPeerCallExit(Entity entity)`:
- Decrement `_currentCallDepth[entity]`, min 0.

### 4.7 Implement CaptureStateSnapshot stub

`GetCurrentStateSnapshot()` returns a `BlueprintStateSnapshot` for the current paused entity if `_isPaused`:
```csharp
public BlueprintStateSnapshot? GetCurrentStateSnapshot()
    => _isPaused && _pausedOnEntity.HasValue
        ? new BlueprintStateSnapshot(_pausedOnEntity.Value, Guid.Empty)  // assetId stub until DBG-004
        : null;
```

---

## 5. Tests Required

### 5.1 BreakpointTests.cs

Create `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Debug/BreakpointTests.cs` with tests:

**SC1: `Breakpoint_FiresOnNodeEntry_RequestsPauseOncePerFrame`** (from Patch 1):
- Set breakpoint on node "bp-node".
- Call `OnNodeEnter(E1, "bp-node")` -- assert `MockTimeController.PauseRequestCount == 1`.
- Call `OnNodeEnter(E2, "bp-node")` again while `_isPaused == true` -- assert `PauseRequestCount` is still 1 (re-entrant guard).

**SC2: `Continue_CallsRequestResume_ClearsPausedState`**:
- Hit a breakpoint (PauseRequestCount becomes 1, _isPaused == true).
- Call `Continue()`.
- Assert `MockTimeController.ResumeCount == 1`.
- Assert `session.IsPaused == false`.
- Assert `session.PausedAt == null`.

**SC3: `Breakpoint_HitCount_IncreasesOnEachHit`**:
- Set a breakpoint. Call `OnNodeEnter` 3 times (interleaved with `Continue()` to clear pause each time).
- Assert `GetBreakpoints()[0].HitCount == 3`.

**SC4: `ClearBreakpoint_RemovesFromSession`**:
- Set a breakpoint, get the `BreakpointId`.
- `ClearBreakpoint(id)`.
- Call `OnNodeEnter` with the node -- assert `PauseRequestCount == 0` (no pause requested).

**SC5: `ClearAllBreakpoints_RemovesAll`**:
- Set 2 breakpoints. `ClearAllBreakpoints()`. Assert `IsAnyBreakpointActive == false`.

**SC6: `StructureHashMismatch_ClearsBreakpoints`**:
- Register map v1, set breakpoint on a node in that asset.
- Register map v2 for same asset with different structure hash.
- Assert `IsAnyBreakpointActive == false`.
- Assert `OnBreakpointListChanged` fired.

**SC7: `HandleBreakpointHit_RecordsCorrectSelf_And_Tick`**:
- Use a `StubSimulationView` that returns `Tick == 42u` and `Time == 1.5f`.
- Hit a breakpoint. Assert `OnBreakpointHit` event received with `Hit.Tick == 42u`, `Hit.SimulationTime == 1.5f`, `Hit.Self == E1`.

### 5.2 StepTests.cs

Create `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Debug/StepTests.cs`:

**SC1: `StepOver_RequestsOneTick_ThenPausesOnNextSameDepthNode`**:
- Hit breakpoint (pause). Call `StepOver()`.
- Assert `MockTimeController.StepRequestCount == 1`, `IsPaused == false`.
- Call `OnNodeEnter(E1, "next-node")` (same entity, depth 0).
- Assert `IsPaused == true` (step matched).

**SC2: `StepInto_PausesOnNextNodeForSameEntity`**:
- Hit breakpoint. Call `StepInto()`. Call `OnNodeEnter(E1, "any-node")`.
- Assert `IsPaused == true`.

**SC3: `StepOut_PausesOnlyAtShallerDepth`**:
- Hit breakpoint at depth 1 (simulate: call `OnPeerCallEnter(E1, ...)` before hitting BP).
- Call `StepOut()`.
- Call `OnNodeEnter(E1, "still-deep-node")` at depth 1 -- assert NOT paused.
- Call `OnPeerCallExit(E1)` -- depth becomes 0.
- Call `OnNodeEnter(E1, "shallow-node")` -- assert `IsPaused == true`.

**SC4: `StepOver_StepRequestCount_IsExactlyOne`**:
- Hit breakpoint. Call `StepOver()`. Assert `StepRequestCount == 1`.

---

## 6. Verification

```powershell
# Run only Debug tests
dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests --filter "FullyQualifiedName~Debug" -v minimal

# Full suite -- must be 0 failures
dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests -v minimal
```

Expected: 0 failures. Total test count >= 402 (390 + ~12 new tests).

---

## 7. Mandatory Task Progression

1. Apply CT0-A (GcReclaimRetries = 30) and CT0-B (NodeHistoryEntry struct). Run full suite -- must be 0 failures before proceeding.
2. Implement `StepMode` enum.
3. Replace stub `_nodeBreakpoints` with proper `Breakpoint` dictionaries.
4. Implement `OnNodeEnter` with real breakpoint check and `HandleBreakpointHit`.
5. Implement `Continue`, `Pause`, hit-count tracking.
6. Implement `StepOver`, `StepInto`, `StepOut`.
7. Implement `OnPeerCallEnter`/`OnPeerCallExit` depth tracking.
8. Write `BreakpointTests.cs` (7 tests).
9. Write `StepTests.cs` (4 tests).
10. Full suite 0 failures.
11. Commit and write report.

**DO NOT STOP** between steps. Do not ask for permission to run tests or fix compilation errors. Run tests, fix errors, repeat until 0 failures.

---

## 8. Commit

After all tests pass:

```powershell
cd d:\WORK\IOS-IG-SimHost-FDP
git add .
git commit -m "feat(blueprints): BATCH-18 DBG-003 breakpoints step semantics + CT0 GC fix

- CT0-A: GcReclaimRetries 20 -> 30 (BATCH-17 GC pressure regression)
- CT0-B: NodeHistoryEntry readonly record struct (eliminates per-probe heap alloc)
- BlueprintDebugSession: full breakpoint storage (BreakpointId-indexed), proper OnNodeEnter
  with re-entrant guard, HandleBreakpointHit (soft-pause Patch 1), hit-count tracking
- Continue/Pause: clears _isPaused, calls time controller Resume/Pause
- StepOver/Into/Out: sets StepMode, calls RequestStepOneTick, matches next OnNodeEnter
- OnPeerCallEnter/Exit: per-entity _currentCallDepth for step-depth matching
- BreakpointTests.cs: SC1-SC7 (7 tests)
- StepTests.cs: SC1-SC4 (4 tests)
- DEBT-020 resolved: brittle nodeId.ToString() stub replaced by proper BP dictionary

Baseline: 390 total -> target: 401+ pass / 5 skip / 0 fail"
```

> Check `git status` for FDP submodule changes before committing. If any FDP files modified, commit FDP first.

---

## 9. Report

Submit to `.dev/blueprints-1/reports/BATCH-18-REPORT.md`. Required sections:
- Work completed (CT0-A, CT0-B, each DBG-003 sub-task)
- Test results (before CT0 fix / after CT0 fix / final)
- Issues encountered and resolution
- Design decisions beyond spec (esp. step-matching logic, hit-count update strategy)
- Weak points spotted

---

## Quality Standards

**❗ TEST QUALITY EXPECTATIONS**
- **NOT ACCEPTABLE:** Tests that only assert `PauseRequestCount > 0` or `IsPaused != false` without checking actual values.
- **REQUIRED SC1:** Two entities hitting same BP while paused -- assert `PauseRequestCount == 1`, not 2.
- **REQUIRED SC7:** Verify actual `BreakpointHit.Tick` and `SimulationTime` values match the `StubSimulationView` values.
- **REQUIRED step tests:** Verify correct step-mode clearing (step matched, `_stepMode` back to `None`). Assert `StepRequestCount == 1` exactly.

---

## Success Criteria Summary

| SC | Task | Check |
|----|------|-------|
| CT0-A | GC fix | Full suite 0 failures after retries increase |
| CT0-B | Struct fix | `ExecutionHistory_Record_ZeroAllocation` passes with struct construction in measured region |
| SC1 | DBG-003 | `PauseRequestCount == 1` even when 2nd entity hits same BP while paused |
| SC2 | DBG-003 | `Continue()` calls `RequestResume()`; `IsPaused == false` |
| SC3 | DBG-003 | Hit count increments per-hit |
| SC4 | DBG-003 | `HandleBreakpointHit` records correct `Tick`, `SimulationTime`, `Self` |
| SC5 | DBG-003 | `StepOver`: `StepRequestCount == 1` per step; pauses on next same/shallower-depth node |
| Build | All | `dotnet build` zero errors |
| Tests | All | 0 failures full suite |
