# BATCH-47 Review — UBP-INT1 + INT2 + INT3

**Reviewer:** Dev Lead  
**Tests before:** 97  
**Tests after:** 103 (all passing)  
**Build:** 0 errors, 0 new warnings

---

## Files created

- `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/IntegrationTests.cs` (6 tests)

## Deviations from instructions

- Added `<ProjectReference>` to `Fdp.Toolkits` in the test `.csproj` — necessary because `RecordingModule` lives in `Fdp.Toolkit.Replay` and wasn't transitively available. Acceptable: direct reference is the right fix.
- `module.Dispose()` called explicitly (not relying on `using`) before reading the `.fdp` file, per the instructions' "better approach" note.

---

## INT1 quality review

### `E2E_PropertyMatchBreakpoint_PausesAndStepsCleanly` ✓
- Exercises the full `snapshotProvider.Execute → liveRepo.Tick() → mutate → system.Execute → OnHit → Pause → Rewind → RequestStep → Restore` pipeline.
- Asserts `snapshot.GetComponent<E2EHealthComp>(entity).Value == 20` (pre-tick view), `liveRepo` also rewound to 20, then after step `liveRepo == 5`.
- **Not trivial** — directly validates the triple-buffer rewind semantics (the heart of the feature).

### `E2E_CompoundPredicate_FiresOnlyWhenBothConditionsMet` ✓
- Exercises `CompoundPredicateDto[And: Health<10, Ammo==0]` across 3 tick sequence:
  - Health=20, Ammo=5 → no hit (both false)
  - Health=5, Ammo=5 → no hit (only one true)
  - Health=5, Ammo=0 → hit (both true)
- Tests compile + runtime evaluation of the compound predicate against real ECS data. Non-trivial: single-condition false-positive would cause the test to fail on Tick 2.

### `E2E_DeferredMutation_AppliedAtNplus1` ✓
- Stages `E2EHealthComp{Value=1000}` mutation while paused.
- Asserts `PendingMutationsCount == 1` before step, `== 0` after.
- Applies ECB explicitly via `ISimulationView.GetCommandBuffer().Playback(liveRepo)`.
- Asserts `liveRepo.GetComponent<E2EHealthComp>.Value == 1000` after playback.
- **Not a trivial value-pass-through** — validates the ECB staging + deferred application contract.

---

## INT2 quality review

### `Perf_HeavyScenario_NoBreakpoints_FastPath` ✓
- 1000 entities, no breakpoints, 100 ticks. Gate is closed (snapshotProvider no-ops, system early-exits).
- Threshold: < 500ms. Actual runtime ~4ms (effectively zero overhead). Correct for an early-exit path.
- Validates the "zero overhead when idle" contract.

### `Perf_HeavyScenario_OneActiveBreakpoint_FitsBudget` ✓
- 1000 entities, 1 armed `Health < 10` breakpoint, all entities at Health=20 (no fires), 100 ticks.
- Gate is open (snapshot runs every tick = full SyncFrom of 1000 entities × 100 ticks).
- Threshold: < 5000ms. Actual runtime ~58ms. Confirms no O(n²) scan pathology.
- Validates that scan cost is bounded and linear.

---

## INT3 quality review

### `Recorder_PausedSession_ProducesMonotonicTicks` ✓
- Creates a `RecordingModule` with `Blocking=true` for deterministic flush.
- Extracts `RecorderTickSystem` via `CapturingSystemRegistry`.
- Tick 1: records frame (GlobalVersion=1).
- Tick 2: fires breakpoint, pauses. Recorder is NOT called while paused (simulating real engine behavior).
- `RequestStep()` unpauses.
- Tick 3: records another frame (GlobalVersion=3).
- `module.Dispose()` flushes, then reads `.fdp` binary with `ReadFrameTicks`.
- Asserts: `ticks.Count >= 2` and `ticks[i] >= ticks[i-1]` for all i.
- Temp files cleaned up in `finally`.
- **Non-trivial**: validates the critical invariant that a breakpoint pause doesn't corrupt the recorder's tick sequence — the GlobalVersion gap (1 → 3, skipping 2) is valid since monotonic means non-decreasing, and the test correctly allows a gap.

---

## Overall verdict

All 6 tests exercise real integration points with real production components (no mocks for ECS, real `PredicateCompiler`, real `RecordingModule`). The tests match the DESIGN intent for INT1-INT3. No fake or trivially-trivial tests. **Approved.**
