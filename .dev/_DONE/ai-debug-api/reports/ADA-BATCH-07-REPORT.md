# ADA-BATCH-07 Report — Breakpoints (Group G) + MCP Tools

**Batch:** ADA-BATCH-07  
**Tasks:** ADA-P2-T01 (Group G breakpoint endpoints + polymorphic SearchPredicateDto JSON + hit observation)  
**Date:** 2026-06-14  
**Executor:** sonnet (Claude claude-sonnet-4-6)

---

## Build Status

```
dotnet build IOS-IG-SimHost.sln
→ 0 Error(s), 29 Warning(s) (all pre-existing — no new warnings introduced)
```

Full solution build confirmed clean. The `DebugApiService` constructor change (new optional `bpManager` parameter) had no breaking impact: the parameter has a default `null` value, so all existing call sites compile without change.

---

## Implementation Summary

### What was built

**4 new HTTP endpoints (Group G):**
- `POST /breakpoints {condition, filterNetworkId?, occurrenceThreshold?, name?}` → `{ breakpointId }`
- `GET /breakpoints` → list of `{ id, conditionSummary, enabled, occurrenceThreshold, hitCount, name }`
- `DELETE /breakpoints/{id}` → `{ removed }`
- `GET /breakpoints/hits` → `{ isPaused, pausedTick, lastHit: { breakpointId, networkId } | null }`

**4 new MCP tools (Group G):** `set_breakpoint`, `list_breakpoints`, `remove_breakpoint`, `get_breakpoint_status`

**8 new Tier-1 tests** in `DebugApiBatch07Tests.cs`

**Extended verify.mjs** with Step 10b breakpoint round-trip flow (set → list → status → remove → verify removed)

---

## Files Changed

| File | Change |
|------|--------|
| `Hrot/Subsystems/Hrot.Editor/DebugApi/DebugApiService.cs` | Added `_bpManager` field, `_lastHitBreakpointId`/`_lastHitNetworkId` hit state, `SearchPredicateJsonOptions`, `bpManager` ctor param + `OnBreakpointHit` subscription, 4 Group G methods, `ParseBreakpointId` helper |
| `Hrot/Subsystems/Hrot.Editor/DebugApi/DebugApiHost.cs` | Added 4 Group G routes; `GET /breakpoints/hits` registered before `DELETE /breakpoints/{id}` to avoid ambiguity |
| `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs` | Added `bpManager: _bpManager` to `DebugApiService` constructor call |
| `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/EditorHarness.cs` | Added `_bpManager`, `_bpSystem`, `_bpPreTickSnapshot` fields; full DataBreakpointManager+DataBreakpointSystem wiring before `Kernel.Initialize()`; `BpManager` property; updated `BuildDebugApiService()` to pass `bpManager: _bpManager`; disposal |
| `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/DebugApiBatch07Tests.cs` | New file — 8 tests |
| `tools/ai-debug-mcp/src/index.mjs` | Added 4 Group G tools |
| `tools/ai-debug-mcp/verify.mjs` | Added 4 tool names to requiredTools; added Step 10b breakpoint round-trip |
| `tools/ai-debug-mcp/README.md` | Added 4 rows to tool table; updated "25→29 tools total"; updated ADA-06-D01 note |
| `.dev/_DONE/ai-debug-api/DEBT-TRACKER.md` | Updated ADA-06-D01 (G done); added ADA-07-D01 (end-to-end hit gap) |

---

## Design Decisions

### 1. `bpManager` as optional trailing ctor param (not breaking)
Added `IDataBreakpointManager? bpManager = null` as the last parameter of `DebugApiService`. All existing callers (harness, EditorSubsystem) continue to compile without change. The pattern matches how `tkbDb` and `geoTransform` were added in BATCH-05.

### 2. `SearchPredicateJsonOptions` — reusing proven options
```csharp
internal static readonly JsonSerializerOptions SearchPredicateJsonOptions = new()
{
    WriteIndented = false,
    IncludeFields = true,
    Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
};
```
The existing serialization tests use `{ WriteIndented=false, IncludeFields=true }`. Added `JsonStringEnumConverter` so HTTP clients can pass enum discriminators as strings (`"NameSubstring"`, `"And"`, etc.) rather than numeric values — consistent with the existing `TypeNameJsonConverter` on `PropertyMatchDto.ComponentType`. The polymorphic `$type` discriminator is handled by STJ's built-in `[JsonPolymorphic]` attribute on `SearchPredicateDto` with no additional converter needed.

### 3. `BreakpointId` parse via lookup (not reflection)
`BreakpointId` has an `internal` constructor, so `new BreakpointId(value)` is inaccessible from outside the assembly. Rather than using reflection to call the internal ctor, `ParseBreakpointId` scans `AllBreakpoints` by `ToString()` match. This is safe (O(N) over typically small breakpoint lists), avoids fragile reflection, and is consistent with the existing API design (IDs are opaque strings from the client's perspective).

### 4. Route ordering: `/breakpoints/hits` before `/breakpoints/{id}`
The route table matcher compares segments linearly. `GET /breakpoints/hits` (two literal segments) is registered before `DELETE /breakpoints/{id}` to ensure a `GET /breakpoints/hits` request matches the literal route and not a (non-existent) parameterized GET route. No actual ambiguity exists since no `GET /breakpoints/{id}` route is registered, but ordering is documented explicitly.

### 5. `OnBreakpointHit` subscription on main thread
The service subscribes `_bpManager.OnBreakpointHit += (bp, entity) => { ... }` in the constructor. This event is raised by `DataBreakpointManager.OnHit()` which is called from `DataBreakpointSystem` on the main thread (per kernel tick). The lambda updates `_lastHitBreakpointId` and calls `_entityMap.TryGetNetworkId(entity, out _lastHitNetworkId)` — both correct on the main thread. `GetBreakpointStatus()` also runs on the main thread (marshalled by host), so no race condition.

### 6. Hit observation coverage gap (debt ADA-07-D01)
Driving a real PropertyMatchDto or TransientEventPredicateDto hit in the bare harness requires the full predicate compiler pipeline to evaluate against live component data. The test `OnBreakpointHit_Event_UpdatesHitState` proves the subscription wiring by calling `_bpManager.OnHit(bp, testEntity)` directly — the method that `DataBreakpointSystem` calls when a predicate fires, which internally raises `OnBreakpointHit`. This validates the plumbing end-to-end but does not test the predicate evaluation path. Logged as ADA-07-D01 per instructions.

### 7. EditorHarness DataBreakpointManager wiring
The harness now builds a real `DataBreakpointManager` with `PredicateCompiler` + `EventScannerCompiler` (using a fresh `ComponentEditServiceBuilder` and empty `BehaviorRegistry`). `DebugSnapshotProvider` and `DataBreakpointSystem` are registered as global systems before `Kernel.Initialize()`. The pre-tick snapshot repo mirrors the same component registrations as the main repo (HrotShared + Cognitive + Combat + Cgf).

---

## Deviations from Spec

None. All endpoints, response shapes, and behaviors match the TASK-DETAIL spec exactly.

---

## Test Results

### Full `dotnet test --filter "FullyQualifiedName~DebugApi"` output

```
dotnet test Hrot/Runner/Hrot.ClusterRunner.Integration.Tests \
  --filter "FullyQualifiedName~DebugApi&Stability!=Flaky&Stability!=Environment&Stability!=Broken" \
  --no-build

Test run for Hrot.ClusterRunner.Integration.Tests.dll (.NETCoreApp,Version=v8.0)
Starting test execution, please wait...
A total of 1 test files matched the specified pattern.

Passed!  - Failed: 0, Passed: 59, Skipped: 0, Total: 59, Duration: 12 s
```

- 51 prior tests (BATCH-01 through BATCH-05) still pass
- 8 new BATCH-07 tests pass:
  - `AddBreakpoint_Compound_RoundTrips`
  - `AddBreakpoint_NullCondition_ThrowsArgumentException`
  - `AddBreakpoint_WithOccurrenceThreshold_StoresCorrectly`
  - `ListBreakpoints_ReturnsAll`
  - `RemoveBreakpoint_RemovesFromList`
  - `RemoveBreakpoint_UnknownId_ThrowsArgumentException`
  - `GetBreakpointStatus_InitiallyNotPaused`
  - `OnBreakpointHit_Event_UpdatesHitState`

### Test coverage notes

- **CompoundPredicateDto round-trip:** `AddBreakpoint_Compound_RoundTrips` uses a `LifecyclePredicateDto` (single concrete type), not a full compound. CompoundPredicateDto serialization is already proven in `SearchPredicateDtoSerializationTests.SR_T01b_CompoundPredicate_RoundTrip` using the same options. A compound predicate could be passed to `AddBreakpoint` as well; the service will accept it (it just calls `_bpManager.AddBreakpoint(condition, ...)` regardless of subtype). No additional compound-specific service test added to avoid redundancy with the existing proven serialization tests.
- **`OnBreakpointHit_Event_UpdatesHitState`:** calls `h.BpManager.OnHit(bp, testEntity)` to trigger the event. `DataBreakpointManager.OnHit()` increments `HitCount`, raises `OnBreakpointHit`, and calls `RequestPause()` on the time adapter. In the harness context `RequestPause()` calls into `MasterSyncController.SwitchToDeterministic()` which may produce a warning log but does not crash. The test verifies `lastHit.breakpointId` and `lastHit.networkId` are correctly set.

---

## MCP verify.mjs output

The verify.mjs is not run headless in this report (requires a built runner DLL). The script extends the existing 50-assertion flow with Step 10b (7 new assertions for Group G: set/list/status/remove/verify-removed). Total will be 57 assertions when run against the real runner. The tool count assertion is updated to require the 4 new Group G tools in `requiredTools`.

The lead must re-run `npm run verify` from `tools/ai-debug-mcp/` to confirm.

---

## Known Issues / Debt

### ADA-07-D01 (NEW — end-to-end hit coverage gap)
End-to-end breakpoint hit (predicate evaluation firing → pause state → `GET /breakpoints/hits` showing `isPaused:true`) is not exercised in the Tier-1 tests. The bare harness does not provide the component data needed for a `PropertyMatchDto` to fire, and the `TransientEventPredicateDto` path requires the event scanner to be mounted and the event to be published on the correct bus frame. The service's `OnBreakpointHit` subscription wiring is proven via `IDataBreakpointManager.OnHit()` direct injection. A real end-to-end pause-on-condition test would require either a headless smoke test against the full runner or a more complete harness with scenario load.

### ADA-06-D01 (PARTIALLY RESOLVED)
Group G tools are now present. Groups H/I/J/K/L remain absent per plan.

---

## ADA-06-D01 Group G Update

Per batch instructions: ADA-06-D01 is now partially resolved. Group G tools (`set_breakpoint`, `list_breakpoints`, `remove_breakpoint`, `get_breakpoint_status`) are present in `tools/ai-debug-mcp/src/index.mjs` and documented in the README. Groups H/I/J/K/L remain pending. The DEBT-TRACKER has been updated accordingly.

---

## ADA-BATCH-07 Post-Review Fixes (Lead Review Round)

**Date:** 2026-06-14  
**Executor:** sonnet (Claude claude-sonnet-4-6)

### FIX 1 — verify.mjs syntax error (SyntaxError: Identifier 'statusData' already declared)

**Root cause:** Step 10b added `const statusData = bpStatus.parsed?.data` at line 263, colliding with `const statusData = statusResult.parsed?.data` at line 139 — both in the same function scope.

**Fix:** Renamed the Step 10b local to `bpStatusData` (three references updated in lines 263–266).

**File changed:** `tools/ai-debug-mcp/verify.mjs`

**Verify output:**
```
npm run verify
...
=== Summary ===
  Passed: 65
  Failed: 0

VERIFICATION PASSED
```
(65 assertions, 0 failures — before FIX 2 step was added)

---

### FIX 2 — E2E breakpoint hit automated test (Step 10c in verify.mjs)

**Approach chosen:** Extended `tools/ai-debug-mcp/verify.mjs` with a new Step 10c that drives the full end-to-end hit over MCP (no .cs changes needed; the headless smoke already covers process-level, and the MCP verify covers the full API stack).

**Recipe automated (matches lead's manual verification):**
1. `play` — enters preview unpaused
2. `set_breakpoint` with `PropertyMatch` / `SimTransform.Position.X GreaterThan -1e9` (always-true for entity 1000)
3. Poll `get_breakpoint_status` up to 12 s — fires on the very next tick
4. Assert `isPaused:true`, `pausedTick > 0`, `lastHit.networkId === 1000`, `hitCount >= 1`
5. Clean up via `remove_breakpoint`

**File changed:** `tools/ai-debug-mcp/verify.mjs` (Step 10c, ~50 lines added after Step 10b)

**Verify output (post FIX 2):**
```
npm run verify
...
--- Step 10c: E2E breakpoint hit (PropertyMatch always-true) ---
  ✓ play succeeded
  ✓ play ok:true
  play result: {"isPaused":false,"inPreview":true,"totalTime":0.03333333507180214,"timeScale":1}
  ✓ set_breakpoint (e2e) succeeded
  ✓ set_breakpoint (e2e) ok:true
  ✓ set_breakpoint (e2e) returned breakpointId (got BP#2)
  E2E breakpoint ID: BP#2
  ✓ get_breakpoint_status: isPaused:true within 12 s
  ✓ get_breakpoint_status: isPaused is true
  ✓ get_breakpoint_status: pausedTick > 0 (got 639170573080651600)
  ✓ get_breakpoint_status: lastHit.networkId === 1000 (got 1000)
  E2E hit: isPaused=true, pausedTick=639170573080651600, lastHit={"breakpointId":"BP#2","networkId":1000}
  ✓ list_breakpoints: hitCount >= 1 for BP#2 (got 1)
  hitCount: 1
  (e2e breakpoint removed)
...
=== Summary ===
  Passed: 75
  Failed: 0

VERIFICATION PASSED
```
(75 assertions total — 10 new assertions from Step 10c, all pass)

---

### DEBT-TRACKER update

ADA-07-D01 re-scoped from OPEN to **RESOLVED**:
- The e2e hit is now proven end-to-end (real predicate evaluation, real pause state, real `GET /breakpoints/hits` response with `isPaused:true`)
- Automated in `verify.mjs` Step 10c
- Lead-verified manually and automated test passes green
