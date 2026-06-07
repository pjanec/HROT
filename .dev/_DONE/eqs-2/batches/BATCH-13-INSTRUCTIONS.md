# BATCH-13 — EQS Phase 10: Schema Additions (EQS-032, EQS-033, EQS-034)

**Batch Number:** BATCH-13
**Tasks:** TASK-EQS-032, TASK-EQS-033, TASK-EQS-034
**Phase:** Phase 10 — Corrective Schema Additions (architect findings #1, #2, #3)
**Estimated Effort:** 12–16 hours
**Report target:** `.dev/eqs-2/reports/BATCH-13-REPORT.md`

---

## Onboarding

Read these first:

- `.dev/eqs-2/ONBOARDING.md` — project orientation, folder layout, key types
- `.dev/eqs-2/TASK-DETAIL.md` §§ TASK-EQS-032, TASK-EQS-033, TASK-EQS-034 — full specs
- `.dev/eqs-2/EQS_Design_v1.3_final.md` §§ 3.1, 3.2, 4.1, 4.2, 8 — the WHY behind each change
- Previous batch review: `.dev/eqs-2/reviews/BATCH-12-REVIEW.md` — approved base state
- `.dev/eqs-2/DEBT-TRACKER.md` — check for any open P2/P3 items (D-01)

All EQS tasks 001–031 are complete. The existing files you need to edit are:
- `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/EqsComponents.cs` — `EqsResult`, `EqsCognitiveBuffer`, `EqsSensor`
- `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/EqsDdsTopics.cs` — `EqsSensorConfigTopic`, `EqsResultEntry`
- `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/CheapLineOfSightTest.cs`
- `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/AccurateLineOfSightTest.cs`
- `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/NavmeshReachableTest.cs`
- `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/EqsEvalState.cs` — `SensorEvalState` (for ScoreDelta cache)
- `Hrot/Network/NED/SimHost/EqsSensorConfigEgressTranslator.cs`
- `Hrot/Network/NED/SimHost/EqsSensorConfigIngressTranslator.cs`
- `Hrot/Network/NED/SimHost/EqsResultEventEgressTranslator.cs`
- `Hrot/Network/NED/SimHost/EqsResultIngressTranslator.cs`
- `Hrot/Subsystems/Hrot.SimHost/Systems/EqsResultUpdateSystem.cs`
- `Hrot/Subsystems/Hrot.AI.Behaviors/Brains/EqsLifecycleNodes.cs` — `Action_MaintainEqsSensor` (epoch tracking for ScoreDeltaThreshold)

---

## Mandatory Workflow: Test-Driven Task Progression

For each task, in order:
1. Write the unit tests first (they will fail to compile until you add the feature).
2. Implement the feature.
3. Run `dotnet test` for the relevant test project; verify all new tests pass and no
   pre-existing tests regress.
4. Only then move to the next task.

Build must pass with **0 errors, 0 warnings** before submitting the report.

---

## TASK-EQS-032 — Add `FlagsMeaningful` to `EqsResult`

Full spec: `.dev/eqs-2/TASK-DETAIL.md` §TASK-EQS-032

**Summary:** `EqsResult` has a `short _pad` field that is wasted space. Replace it with
`public short FlagsMeaningful` — a parallel bitset indicating which bits in `Flags` were
actually computed by the template's tests. Bit not set in `FlagsMeaningful` must not be
read by consumers.

### Changes required

**`EqsComponents.cs` — `EqsResult` struct:**
- Replace `public short _pad;` with `public short FlagsMeaningful;`
- Update the XML doc comment to describe `FlagsMeaningful`.
- Verify `Marshal.SizeOf<EqsResult>()` remains 24 (size is unchanged; same 2-byte slot).

**`EqsDdsTopics.cs` — `EqsResultEntry` struct:**
- Add `public ushort FlagsMeaningful;` alongside the existing `public ushort Flags;`.
- (These are wire fields, so the DDS IDL changes; that is expected and intentional.)

**`EqsResultEventEgressTranslator.cs`:**
- When building each `EqsResultEntry` from an `EqsResult`, copy
  `FlagsMeaningful = (ushort)result.FlagsMeaningful`.

**`EqsResultIngressTranslator.cs`:**
- When reconstructing an `EqsResult` from an `EqsResultEntry`, set
  `FlagsMeaningful = (short)entry.FlagsMeaningful`.

**`EqsResultUpdateSystem.cs` (both online and offline paths):**
- Copy `FlagsMeaningful` alongside `Flags` when writing results into `EqsCognitiveBuffer`.

**`CheapLineOfSightTest.cs`:**
- When the test evaluates a candidate (i.e., the bypass conditions are not met), set
  `candidate.FlagsMeaningful |= 1` alongside `candidate.Flags |= 1` (flag bit 0 = HasLOS).
- For candidates that pass through (cover valid), also set `FlagsMeaningful |= 1`.
- The bypass path (threat below threshold, no TargetMemory) must NOT set `FlagsMeaningful`.
  That means: only set `FlagsMeaningful` after confirming the test actually ran, not at
  entry or unconditionally.

**`AccurateLineOfSightTest.cs`:**
- Same pattern: set `candidate.FlagsMeaningful |= 1` (or the matching bit for this test)
  whenever you set or clear bit 0 in `Flags`. Do NOT set it on the budget-bypass path
  (when the ring buffer is not yet populated and the candidate is marked pending).
  Only set it when the raycast result is resolved (hit or miss).

**`NavmeshReachableTest.cs`:**
- When marking a candidate as reachable (`Flags |= (1 << 3)`), also set
  `candidate.FlagsMeaningful |= (short)(1 << 3)`.
- When marking as unreachable (`EntityId = -1L`), also set
  `candidate.FlagsMeaningful |= (short)(1 << 3)` (the bit was computed, even if the
  result is rejection).
- Skip-already-rejected candidates without touching `FlagsMeaningful`.

### Tests (add to existing test file or a new file in the same test project)

Test class: `EqsFlagsMeaningfulTests` in
`Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/Eqs/EqsFlagsMeaningfulTests.cs`

**T-FM1 — `EqsResult_FlagsMeaningful_StructSizeUnchanged`**
```
Marshal.SizeOf<EqsResult>() == 24
```

**T-FM2 — `CheapLosTest_BelowThreshold_FlagsMeaningfulZero`**
- Use `EditorHarness`.
- Template: `CoverPointsGenerator` + `CheapLineOfSightTest` (with `MockLosService` that
  always returns false = covered).
- Observer entity: give it `TargetMemory` with threat score **10**, sensor
  `ThreatThreshold = 50` (below threshold → bypass).
- Run solver, get buffer.
- Assert: all candidates in buffer have `FlagsMeaningful & 1 == 0` (bit not set,
  meaning the test never ran).

**T-FM3 — `CheapLosTest_AboveThreshold_FlagsMeaningfulSet`**
- Same setup but threat score = **100** (above threshold → test runs).
- `MockLosService` returns `false` (covered = keep).
- Assert: all surviving candidates have `FlagsMeaningful & 1 != 0`.

**T-FM4 — `FlagsMeaningful_SurvivesDdsRoundTrip`**
- Use `HrotRunnerHarness("simhost,cgf")`.
- Brain spawns entity, attaches sensor mapped to `FindCoverFromTarget`-like template with
  `CheapLineOfSightTest` (threat above threshold).
- Pump until `EqsCognitiveBuffer.IsReady == true`.
- Assert: top result's `FlagsMeaningful & 1 != 0` on the Brain side (field survived DDS).

---

## TASK-EQS-033 — Add `LastUpdateTimeSeconds` to `EqsCognitiveBuffer`

Full spec: `.dev/eqs-2/TASK-DETAIL.md` §TASK-EQS-033

**Summary:** Add `public float LastUpdateTimeSeconds` to `EqsCognitiveBuffer` after
`LastUpdateTick`. This is the consumer-side simulation-time stamp consumed by the
When-node iteration for `BecomesStale` checks. `LastUpdateTick` remains the
determinism-friendly publish-side timestamp — do not remove it.

### Changes required

**`EqsComponents.cs` — `EqsCognitiveBuffer` struct:**
- Add `public float LastUpdateTimeSeconds;` directly after `public uint LastUpdateTick;`.
- Review struct alignment: the struct starts with `int Count` (4 bytes), then
  `uint LastUpdateTick` (4 bytes), then the new `float LastUpdateTimeSeconds` (4 bytes),
  then `EqsResultArray Results`. Verify that the offset of `Results` remains a multiple
  of `EqsResult.Stride` (= `Marshal.SizeOf<EqsResult>()` = 24). 4 + 4 + 4 = 12 bytes
  before the array; 24-byte stride elements require 0 mod 24 alignment for the array
  start. 12 is not a multiple of 24 — add 12 bytes of padding after
  `LastUpdateTimeSeconds` before `Results` (three `int` padding fields). Alternatively,
  confirm whether the inline array alignment is relaxed (it only requires `sizeof(EqsResult)`
  alignment in memory, not offset-from-struct-start alignment — verify with a unit test
  that the struct compiles and `GetSpanRW` still works correctly). The existing
  `EqsCognitiveBuffer_GetSpanRW_NoDefensiveCopy` test must still pass.
- Add XML doc for the new field, noting it is written by `EqsResultUpdateSystem` from
  `view.Time`, distinct from `LastUpdateTick`.
- `IsReady` must remain `LastUpdateTick > 0` (do NOT change to `LastUpdateTimeSeconds > 0`).

**`EqsResultUpdateSystem.cs`:**
- Stamp `buffer.LastUpdateTimeSeconds = (float)view.Time;` on every path that writes
  `Count` and `LastUpdateTick`, including the "empty result" path (where `Count` becomes
  0 but `IsReady` flips to true).
- Derive `view.Time` from the `ISimulationView` / `EntityRepository` — check how the
  existing system accesses simulation time. Look for `view.CurrentTime`, `view.Time`,
  or `repo.GetSingleton<SimulationTimeState>()`. Match the pattern used elsewhere in the
  codebase.

**No DDS wire format changes** — `EqsResultEvent` does not carry seconds; only the
consumer-side struct is modified.

### Tests

Test class: `EqsLastUpdateTimeTests` in
`Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/Eqs/EqsLastUpdateTimeTests.cs`

**T-LUT1 — `CognitiveBuffer_StampsLastUpdateTimeSeconds`**
- `EditorHarness`.
- Advance simulation time to `5.0f` before triggering result (verify how `EditorHarness`
  exposes time control; look at existing harness time APIs or set
  `SimulationTimeState.CurrentTimeSeconds = 5.0f` directly on the repo singleton if
  that is how the harness works).
- Spawn entity, attach sensor mapped to a trivial template, pump until `IsReady`.
- Assert: `buffer.LastUpdateTimeSeconds == 5.0f` (or within float epsilon).

**T-LUT2 — `CognitiveBuffer_StampsOnEmptyUpdate`**
- Same harness.
- Advance time to `5.5f`.
- Inject an `EqsResultEvent` with `EntryCount = 0` (empty result) — confirm
  `IsReady` becomes true after the update.
- Assert: `buffer.LastUpdateTimeSeconds == 5.5f`.

**T-LUT3 — `CognitiveBuffer_GetSpanRW_StillWorks`**
- The existing `EqsCognitiveBuffer_GetSpanRW_NoDefensiveCopy` test must pass without
  modification. Re-run it explicitly in this batch to confirm no struct layout regression.

---

## TASK-EQS-034 — Add `ScoreDeltaThreshold` to `EqsSensor` and DDS topic

Full spec: `.dev/eqs-2/TASK-DETAIL.md` §TASK-EQS-034

**Summary:** Add `ScoreDeltaThreshold` to `EqsSensor` (Brain-side parameter) and to
`EqsSensorConfigTopic` (DDS wire). When `PublishPolicy == ScoreDelta`, the solver only
emits `EqsResultEvent` when any top-K score changes by more than the threshold since the
last published result. The diff cache lives on `SensorEvalState` (solver-local, not
replicated).

### Changes required

**`PublishPolicy` enum — extend discriminators:**
- Find the existing `PublishPolicy` byte enum (likely in `EqsComponents.cs` or a separate
  file). Add `ScoreDelta = 3` preserving all existing ordinals.

**`EqsComponents.cs` — `EqsSensor` struct:**
- Add `public float ScoreDeltaThreshold;` after `Priority` (or alongside
  `PublishPolicy`/`Priority`).
- Default value is `0.0f`; no special initialization needed (struct default is fine).
- Add XML doc.

**`EqsDdsTopics.cs` — `EqsSensorConfigTopic` struct:**
- Add `public float ScoreDeltaThreshold;` to the wire struct.

**`EqsSensorConfigEgressTranslator.cs`:**
- Copy `ScoreDeltaThreshold` into the DDS sample.

**`EqsSensorConfigIngressTranslator.cs`:**
- Copy `ScoreDeltaThreshold` from the DDS sample into the ghost `EqsSensor`.

**`EqsEvalState.cs` — `SensorEvalState` struct:**
- Add a `LastPublishedTopK` cache field:
  ```csharp
  // 16-float inline array storing scores from the last published result set.
  // Used by ScoreDelta publish policy to avoid re-emitting near-identical results.
  [InlineArray(16)]
  public struct TopKScoreCache { private float _e; }
  public TopKScoreCache LastPublishedTopK;
  ```
  Place the new inner struct in the same file but outside `SensorEvalState`.

**`EqsSolverSystem.cs` — `WriteResultsToPoolAndPublish`:**
- Before emitting `EqsResultEvent`, check the publish policy:
  - `AlwaysPush` (0) or anything not `ScoreDelta`: emit unconditionally (existing behavior).
  - `ScoreDelta` (3): diff the current top-K scores against `evalState.LastPublishedTopK`.
    If **all** score deltas are ≤ `sensor.ScoreDeltaThreshold`, skip emit (no publish).
    If any delta exceeds the threshold, emit and update `evalState.LastPublishedTopK`.
- The diff loop: iterate `min(currentCount, 16)` entries and compare
  `|current[i].Score - lastPublished[i]|`. On first publish (all zeros in cache),
  any non-zero score triggers a publish (correct: 0.0 delta threshold means "every change").
- After a successful emit, copy current top-K scores into `evalState.LastPublishedTopK`.

**`EqsLifecycleNodes.cs` — `Action_MaintainEqsSensor`:**
- Add `ScoreDeltaThreshold` to the existing multi-field comparison block. When it
  changes, increment `sensor.Epoch` (same pattern as `BlueprintId`, `SearchRadius`,
  `FactionFilter`, `ThreatThreshold`).

### Tests

Test class: `EqsScoreDeltaTests` in
`Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/Eqs/EqsScoreDeltaTests.cs`

**T-SD1 — `ScoreDelta_SupressesSmallChanges`**
- `EditorHarness`.
- Template with a `DeterministicScoreGenerator` (use `DeterministicPositionalGenerator`
  pattern from existing tests) that you can control to emit specified scores.
- Set `sensor.PublishPolicy = ScoreDelta`, `sensor.ScoreDeltaThreshold = 0.1f`.
- **First evaluation:** scores [1.0, 0.8, 0.6]. Assert buffer updated (first publish
  always happens).
- **Second evaluation:** mutate generator scores to [1.02, 0.79, 0.61] (max delta 0.02
  < 0.1). Assert buffer NOT updated (same `LastUpdateTick` as after first evaluation).
- **Third evaluation:** mutate to [1.0, 0.6, 0.4] (delta 0.2 > 0.1). Assert buffer
  UPDATED (`LastUpdateTick` advanced).

**T-SD2 — `MaintainEqsSensor_ScoreDeltaThresholdChange_IncrementsEpoch`**
- Unit test (no harness needed, direct struct manipulation).
- Set up a minimal `Action_MaintainEqsSensor` execution context where `EqsSensor` is
  already attached to an entity.
- Mutate only `ScoreDeltaThreshold` in the blackboard params.
- Execute the action.
- Assert `sensor.Epoch` incremented exactly once.

**T-SD3 — `ScoreDeltaThreshold_SurvivesDdsRoundTrip`**
- `HrotRunnerHarness("simhost,cgf")`.
- Brain sets `sensor.ScoreDeltaThreshold = 0.25f`.
- Pump until Muscle ghost `EqsSensor` is created.
- Assert `muscleGhost.ScoreDeltaThreshold == 0.25f` on Muscle side.

---

## Developer Insights Section

In your report, answer the following:

1. **What issues were encountered?** (compilation errors, unexpected behaviors, ECS quirks)
2. **What weak points were spotted in the codebase?** (fragile patterns, hidden assumptions)
3. **What design decisions were made beyond the spec?** (e.g., alignment padding choice,
   how you derived `view.Time`, approach to the ScoreDelta diff loop edge cases)
4. **Were there any pre-existing tests that broke?** If yes, which and why?

---

## Report Format

Write `.dev/eqs-2/reports/BATCH-13-REPORT.md` with:

```markdown
# BATCH-13 Report

## Tasks Completed
- [ ] TASK-EQS-032 — FlagsMeaningful
- [ ] TASK-EQS-033 — LastUpdateTimeSeconds
- [ ] TASK-EQS-034 — ScoreDeltaThreshold

## Test Results
| Test ID | Name | Result |
|---------|------|--------|
| T-FM1 | ... | PASS/FAIL |
...

## Files Changed
- (list every file touched)

## Developer Insights
### Issues Encountered
### Weak Points Spotted
### Design Decisions Beyond Spec
### Pre-existing Test Regressions
```

---

## Success Gate

All of the following must be true before submitting the report:

1. `dotnet build` of the full solution: **0 errors, 0 warnings**.
2. All `T-FM*`, `T-LUT*`, `T-SD*` tests pass.
3. Pre-existing EQS integration tests (33 tests from BATCH-12 baseline) all still pass.
4. `Marshal.SizeOf<EqsResult>()` == 24.
5. `GetSpanRW()` test (`EqsCognitiveBuffer_GetSpanRW_NoDefensiveCopy`) still passes.
