# BATCH-02: Blueprint Debug Map + Debug Protocol Fixes

**Batch Number:** BATCH-02  
**Tasks:** BPF-002, BPF-021, BPF-001, BPF-003, BPF-004, BPF-005  
**Source:** `.dev/blueprint-fixes-1/TASK-DETAIL.md`  
**Tracker:** `.dev/blueprint-fixes-1/TASK-TRACKER.md`  
**Estimated Effort:** 14-18 hours  
**Priority:** HIGH -- BPF-002/021 are the root cause of multiple dependent gaps; BPF-003 is a structural correctness issue  
**Dependencies:** BATCH-01 (done)

---

## 📋 Onboarding & Workflow

### Developer Instructions

This batch fixes the blueprint **Debug Map** (the on-disk data structure the debugger reads to resolve nodes, pins, and state layout) and the **Debug Protocol** session logic that uses the map (breakpoint firing, state snapshot, peer-call resolution, StepOut).

Work in dependency order: BPF-002/BPF-021 first (they extend the DebugMap), then BPF-001 (uses stateLayout from the map), then BPF-003/BPF-004/BPF-005 (breakpoint and stepping correctness).

Do not touch the compiler emit area (BATCH-01 tasks); stay in the debug-map and debug-session area.

### Required Reading (IN ORDER)
1. **Task Details:** `.dev/blueprint-fixes-1/TASK-DETAIL.md` -- read BPF-002, BPF-021, BPF-001, BPF-003, BPF-004, BPF-005 sections in full
2. **Debug Protocol Design:** `.dev/blueprints-1/Blueprint_Subsystem_Debug_Protocol_Detailed_Design.md` -- §2.2, §4.2-4.5, §5.2-5.3, §6.1, §7.4, §7.6, §8.4-8.6, §9.2, §9.5
3. **Compiler Design:** `.dev/blueprints-1/Blueprint_Subsystem_Compiler_Detailed_Design.md` -- §TASK-DBG-002/004/005 (debug-map compiler stage)
4. **Workflow Guide:** `.dev/.guides/DEV-GUIDE.md`
5. **Code Standards:** `.dev/.guides/CODE-STANDARDS.md`

### Codebase Memory MCP (MANDATORY)
Use `mcp_codebase-memo_list_projects` then `mcp_codebase-memo_get_architecture`. Use `mcp_codebase-memo_search_graph` to locate symbols before reading files. Use `mcp_codebase-memo_get_code_snippet` to read implementations.

### Source Code Location
- **Debug map compiler:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Emit/DebugMapBuilder.cs`
- **Debug map runtime:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/DebugMapIndex.cs` (and `DebugMapSerializer`, `DebugMapDto` -- find via graph)
- **Debug session:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/BlueprintDebugSession.cs`
- **Debug interfaces:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/IBlueprintDebugSession.cs`
- **Tests:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/`

### Report Submission
`.dev/blueprint-fixes-1/reports/BATCH-02-REPORT.md`  
Questions: `.dev/blueprint-fixes-1/questions/BATCH-02-QUESTIONS.md`

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

For **each task** in sequence:
1. **Define success condition** -- read the TASK-DETAIL entry; state exactly what correct looks like and what tests will verify it
2. **Implement the fix**
3. **Write tests** -- tests must verify actual behavior (field values, structure content), not string presence
4. **Run all tests** -- `dotnet test` on the blueprint test project; ALL must pass
5. **Fix failures at root cause** -- do not skip or comment out failing tests
6. Only when ALL tests pass: move to next task

Do not finish or write the report until every task is done and all tests green.

---

## Context

The Debug Protocol design specifies a rich `DebugMap` (per §4.2-4.5) with pins, graphs, stateLayout, assetName, and generatedSourcePath. The current `DebugMap` is minimal -- it only has a node list. This gap cascades: `GetCurrentStateSnapshot` cannot read field values (no stateLayout), breakpoints have no structure-hash safety, peer-call probe uses a name that never resolves. This batch extends the map and fixes the downstream protocol logic.

---

## ✅ Tasks

### Task 1: BPF-002 + BPF-021 -- Extend the compiler debug map (HIGH, root cause)

**Task Definitions:**
- [BPF-002](../TASK-DETAIL.md#bpf-002----compiler-debug-map-omits-pins-graphs-statelayout-assetname)
- [BPF-021](../TASK-DETAIL.md#bpf-021----debugmap-nodekinddisplayname-never-populated-recordpin--generatedsourcepath-absent-compiler-extends-bpf-002)

**Success Condition (define before implementing):**  
The emitted `DebugMap` must contain: `assetName`, `blueprintIdHex`, `generatedSourcePath`, a `graphs[]` array (id+name per graph), a `pins[]` array (pinId, valueAccessExpression, typeFullName per pin), `stateLayout.fields` (field offsets/types), and each `DebugMapEntry` must have a non-empty `NodeKind` and `DisplayName`. `DebugMapIndex` must expose pin-by-id and graph-by-id resolution. Tests must verify these fields are populated.

**What to do:**
1. Read the current `DebugMap`, `DebugMapEntry`, `DebugMapDto`, `DebugMapSerializer`, `DebugMapIndex` via `get_code_snippet`. Understand the full serialization path.
2. Read the Debug Protocol DD §4.2-4.5 for the exact required fields and their types.
3. Extend `DebugMap` / `DebugMapDto` models to carry `assetName`, `generatedSourcePath`, `graphs[]`, `pins[]`, `stateLayout.fields`.
4. Extend `DebugMapEntry` / `DebugMapEntryDto` to include `NodeKind` and `DisplayName`.
5. Update `DebugMapBuilder.cs` (the compiler emit stage) to populate all new fields by reading from the IR asset.
6. Update `DebugMapIndex` to expose `TryGetPinById(Guid pinId, out DebugPinInfo pin)` and `TryGetGraphById(Guid graphId, out DebugGraphInfo graph)`.
7. Update `DebugMapSerializer` / `DebugMapDto` for the new fields (JSON schema update).

**Tests Required:**
- Compile a blueprint with at least one known pin, two graphs, and a parameter field; assert the emitted debug map contains the pin's `valueAccessExpression`, the graph names, and the stateLayout field offsets
- Assert `DebugMapIndex.TryGetPinById` returns the correct pin info
- Assert `NodeKind` and `DisplayName` are populated on at least one `DebugMapEntry`
- All assertions on actual values (not just non-null/non-empty)

---

### Task 2: BPF-001 -- Implement GetCurrentStateSnapshot (HIGH)

**Task Definition:** [BPF-001](../TASK-DETAIL.md#bpf-001----pause-time-state-inspection-getcurrentstatesnapshot-is-a-stub)

**Success Condition (define before implementing):**  
`GetCurrentStateSnapshot()` must return a fully populated `BlueprintStateSnapshot` with `Self`, `AssetId`, `AssetName`, `Dispatch`, `FieldValues` (dictionary of field name -> object), and `Cursor?` (if applicable). It must read field offsets from the `stateLayout` produced in Task 1. Tests must verify actual field values, not just a non-null snapshot.

**What to do:**
1. Read the Debug Protocol DD §8.4-8.6 for the full snapshot capture logic (dispatch-kind switch, AiPrimitive structure-hash check, field offset/type reading).
2. Extend the `BlueprintStateSnapshot` record to match the designed shape (Self, AssetId, AssetName, Dispatch, FieldValues, Cursor?).
3. Implement `GetCurrentStateSnapshot()` in `BlueprintDebugSession.cs` using the populated debug map from Task 1.
4. Implement the three dispatch paths (Instance, AiPrimitive, Library) per §8.4-8.6.
5. For AiPrimitive: verify structure-hash header before reading fields (§8.6).

**Tests Required:**
- Snapshot returns correct `AssetName` and `AssetId` (not Guid.Empty, not stub comment)
- Snapshot `FieldValues` contains at least one field with the correct value for a known blueprint parameter
- Structure-hash mismatch (AiPrimitive) returns null or a stale snapshot (per design)

---

### Task 3: BPF-003 -- Breakpoint structure-hash safety, staleness, per-frame dedup (HIGH)

**Task Definition:** [BPF-003](../TASK-DETAIL.md#bpf-003----breakpoint-structure-hash-safety-staleness-and-per-frame-multi-entity-dedup-missing)

**Success Condition (define before implementing):**  
`Breakpoint` record must have `AssetStructureHashAtSetTime` and `IsStale` fields. `OnNodeEnter` must:
1. Not fire if `!Enabled || IsStale`
2. Check structure-hash; if mismatch, set `IsStale = true` and not fire (do not clear)
3. Track per-frame entity dedup set; subsequent same-frame entities increment `HitCount` and emit an event, but do not pause again
4. `OnNewTick` must reset the per-frame dedup set
Tests must verify each of these behaviors individually.

**What to do:**
1. Read the current `Breakpoint` record, `OnNodeEnter`, `HandleBreakpointHit`, and `OnNewTick` in `BlueprintDebugSession.cs`.
2. Read Debug Protocol DD §5.2, §5.3, §6.1, §9.2.
3. Add `AssetStructureHashAtSetTime` and `IsStale` to the `Breakpoint` record.
4. Implement hash-guard logic in `OnNodeEnter`.
5. Implement per-frame entity dedup (using a `HashSet<Entity>` cleared in `OnNewTick`).
6. Change `RegisterDebugMap` reload handling: instead of clearing breakpoints on hash mismatch, mark them `IsStale` and let them be rebound.

**Tests Required:**
- Breakpoint does not fire when `IsStale = true`
- Breakpoint does not fire on hash mismatch; `IsStale` becomes true
- Second same-frame hit on different entity: `HitCount` incremented, event emitted, no second pause
- `OnNewTick` resets the dedup set (third-tick hit on same entity fires normally)
- Structure-hash match: breakpoint fires normally

---

### Task 4: BPF-004 -- Align peer-call probe signature; fix asset-name matching (MEDIUM)

**Task Definition:** [BPF-004](../TASK-DETAIL.md#bpf-004----peer-call-probe-signature-diverges-asset-matching-is-dead)

**Success Condition (define before implementing):**  
`OnPeerCallEnter` and `OnPeerCallExit` must accept a peer `assetId` (Guid) directly per Design DD §2.4/§7.4. Active-entity keying must use the resolved asset-id Guid, not a fallback `Guid.Empty`. Tests must verify active entities are correctly keyed.

**What to do:**
1. Read Design DD §2.4, §7.4 for the designed probe signature.
2. Read `IBlueprintProbeSink.cs` and `OnPeerCallEnter`/`OnPeerCallExit` in `BlueprintDebugSession.cs`.
3. Change the probe signature to pass `Guid peerAssetId` directly (removing the string `targetAssetName` matching approach), OR if the probe emitter can only provide a name, fix the name resolution using the real asset names now available from Task 1.
4. Verify `GetActiveEntities()` returns entities keyed by correct asset-id.

**Tests Required:**
- `OnPeerCallEnter` with a known `peerAssetId` causes the entity to appear under that asset-id in `GetActiveEntities()`
- `Guid.Empty` is not used as a fallback key

---

### Task 5: BPF-005 -- StepOut tick-boundary and entity-death abandonment (MEDIUM)

**Task Definition:** [BPF-005](../TASK-DETAIL.md#bpf-005----stepout-tick-boundary-semantics-and-entity-death-step-abandonment-missing)

**Success Condition (define before implementing):**  
StepOut from depth 0 must re-pause at the next tick boundary (track `_stepFromTick`; re-pause when `_view.Tick > _stepFromTick`). A step must be abandoned (cancelled) when `_stepFromEntity` is no longer alive. Tests must verify both behaviors.

**What to do:**
1. Read Design DD §7.6 (StepOut at depth 0) and §9.5 (entity-death abandonment).
2. Read `StepOut`, `OnNodeEnter`, and relevant fields in `BlueprintDebugSession.cs`.
3. Add `_stepFromTick` tracking.
4. For StepOut at depth 0: re-pause when `_view.Tick > _stepFromTick`.
5. In `OnNodeEnter` step block: add `IsAlive` check that cancels the pending step if `_stepFromEntity` is dead.

**Tests Required:**
- StepOut from depth-0 node re-pauses on the next tick (not immediately, not never)
- Step is cancelled (no pause) when the stepping entity dies before the step completes
- Normal StepOut from depth > 0 still works (re-pauses when depth decreases below start depth)

---

## 🧪 Testing Requirements

- Run the full blueprint test project after each task; all tests must pass including BATCH-01 tests
- Tests must verify **actual values** (field contents, entity keys, state snapshots), not just non-null/non-empty
- Do not test implementation internals; test behavior through the public `IBlueprintDebugSession` interface where possible

## ⚠️ Quality Standards

**TEST QUALITY EXPECTATIONS:**
- **NOT ACCEPTABLE:** `Assert.NotNull(snapshot)` -- does not verify anything about correctness
- **REQUIRED:** `Assert.Equal("ExpectedAssetName", snapshot.AssetName)` and `Assert.True(snapshot.FieldValues.ContainsKey("MyField"))`
- **NOT ACCEPTABLE:** Tests that pass even if `GetCurrentStateSnapshot` continues to return an empty stub
- **REQUIRED:** Tests that feed a known blueprint with known field values and assert those values in the snapshot

## 📊 Report Requirements

Submit `.dev/blueprint-fixes-1/reports/BATCH-02-REPORT.md`.

Required sections:
- Each task: completed?, what you did, what tests were added
- Test count and pass/fail
- Issues encountered and how resolved
- Design decisions beyond the spec
- Weak points spotted
- Edge cases discovered
- Suggested commit message

---

## 🎯 Success Criteria

This batch is DONE when:
- [ ] BPF-002/021 fixed: DebugMap populated with pins/graphs/stateLayout/assetName/NodeKind/DisplayName; tests verify actual values
- [ ] BPF-001 fixed: `GetCurrentStateSnapshot` returns fully populated snapshot; field values verified in tests
- [ ] BPF-003 fixed: Breakpoint has hash+IsStale; per-frame dedup and OnNewTick reset work; 5+ behavioral tests pass
- [ ] BPF-004 fixed: Peer-call probe uses Guid-based keying; entity keys correct in tests
- [ ] BPF-005 fixed: StepOut depth-0 tick-boundary and entity-death abandonment work; tests pass
- [ ] All pre-existing tests still pass
- [ ] Report submitted

---

## 📚 Reference Materials
- **Task Details:** `.dev/blueprint-fixes-1/TASK-DETAIL.md` (BPF-001, BPF-002, BPF-003, BPF-004, BPF-005, BPF-021)
- **Debug Protocol Design:** `.dev/blueprints-1/Blueprint_Subsystem_Debug_Protocol_Detailed_Design.md` (§2.2, §4.2-4.5, §5.2-5.3, §6.1, §7.4, §7.6, §8.4-8.6, §9.2, §9.5)
- **Compiler Design:** `.dev/blueprints-1/Blueprint_Subsystem_Compiler_Detailed_Design.md` (debug-map emit stages)
- **Previous Review:** `.dev/blueprint-fixes-1/reviews/BATCH-01-REVIEW.md`
- **Code Standards:** `.dev/.guides/CODE-STANDARDS.md`
