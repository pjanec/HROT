# BATCH-02: EQS Phase 1 Systems and BTree Lifecycle Nodes

**Batch Number:** BATCH-02
**Tasks:** Corrective-0 (P1 fix), TASK-EQS-004, TASK-EQS-005, TASK-EQS-006
**Phase:** Phase 1 — Foundations (systems layer + BTree integration)
**Estimated Effort:** 10–13 hours
**Priority:** HIGH
**Dependencies:** BATCH-01 (all files committed)

---

## Onboarding & Workflow

### Developer Instructions

This batch completes Phase 1 of EQS v1.3. You must fix one P1 type issue from BATCH-01 review,
then implement the Brain-side update system, the Phase 1 stub solver wired into EqsModule, and
the three BTree lifecycle nodes.

### Required Reading (IN ORDER)

1. **Workflow Guide:** `docs/AI_DEV_GUIDE.md`
2. **Onboarding:** `.dev/eqs-2/ONBOARDING.md` — especially critical constraints (defensive copy,
   rejection sentinel `-1L`, epoch vs tick comparison, `[BTreeDeactivator]` `@0` suffix).
3. **Task Definitions:** `.dev/eqs-2/TASK-DETAIL.md` — sections TASK-EQS-004, TASK-EQS-005,
   TASK-EQS-006.
4. **Design Reference:** `.dev/eqs-2/EQS_Design_v1.3_final.md` — §3.1, §7, §8, §11, §14.
5. **Implementation Details:** `.dev/eqs-2/IMPLEM_DETAILS.md` — L:543–810, L:3390–3550.
6. **Previous Review:** `.dev/eqs-2/reviews/BATCH-01-REVIEW.md` — mandatory reading; note the
   P1 issue and the pre-existing test failure count discrepancy.

### Source Code Locations

| Area | Path |
|---|---|
| New EQS types from BATCH-01 | `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/` |
| New EqsSolverSystem + EqsResultUpdateSystem | `Hrot/Subsystems/Hrot.SimHost/Systems/` (or create `Eqs/` subfolder) |
| EqsModule (UPDATE existing) | `Hrot/Subsystems/Hrot.SimHost/Modules/EqsModule.cs` |
| BTree lifecycle nodes (NEW FILE) | `Hrot/Subsystems/Hrot.AI.Behaviors/Brains/EqsLifecycleNodes.cs` |
| Existing BTree node patterns | `Hrot/Subsystems/Hrot.AI.Behaviors/Brains/CgfNodes.cs` |
| Existing solver pattern | `Hrot/Subsystems/Hrot.SimHost/Systems/AreaQuerySolverSystem.cs` |
| EditorHarness (integration tests) | `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/EditorHarness.cs` |
| FDP unit tests | `FDP/Toolkits/Fdp.Toolkits.Tests/` |
| Integration tests | `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/` |

### Build Commands

```powershell
# Build full solution
dotnet build IOS-IG-SimHost.sln

# Run FDP unit tests (EQS subset)
dotnet test FDP/Toolkits/Fdp.Toolkits.Tests/ --filter "Eqs"

# Run Hrot integration tests (requires no DDS firewall block)
dotnet test Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/
```

### Report Submission

When done: `.dev/eqs-2/reports/BATCH-02-REPORT.md`

Questions: `.dev/eqs-2/questions/BATCH-02-QUESTIONS.md`

---

## Context

BATCH-01 established the data model. This batch wires up the systems:
- `EqsResultUpdateSystem` (Brain) processes incoming result events and writes the cognitive buffer.
- `EqsSolverSystem` (Phase 1 stub, Muscle) detects sensors and emits empty result events.
- `EqsModule` is updated to drive the new solver instead of delegating to `AreaQuerySolverSystem`.
- BTree nodes allow a behavior tree to open/close the sensor and wait for results.

The full offline-editor round-trip is verified by an integration test after TASK-EQS-005.

**Related Tasks:**
- [TASK-EQS-004](./../TASK-DETAIL.md#task-eqs-004--eqsresultupdatesystem-brain-side) — Brain-side system
- [TASK-EQS-005](./../TASK-DETAIL.md#task-eqs-005--stubbed-eqssolversystem-phase-1-stub) — Solver stub + module wiring
- [TASK-EQS-006](./../TASK-DETAIL.md#task-eqs-006--btree-lifecycle-nodes-waitforsensor--maintaineqssensor) — BTree nodes

---

## Batch Objectives

1. Fix P1 type issue: `EqsResultEvent.Epoch` and `RefreshTick` must be `uint`, not `int`.
2. Implement `EqsResultUpdateEvent` (managed event class for DDS ingress bridge path).
3. Implement `EqsResultUpdateSystem` with two input paths (managed online event + unmanaged offline event).
4. Implement `EqsSolverSystem` Phase 1 stub.
5. Wire `EqsModule` to use `EqsSolverSystem` + initialize `EqsResultPool` singleton.
6. Implement `EqsParams`, `EqsLifecycleNodes` with 3 BTree methods.
7. All tests pass.

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: You MUST complete tasks in sequence with passing tests:**

1. **Corrective-0:** Fix type → `dotnet build` succeeds ✅
2. **TASK-EQS-004:** Implement → Write unit tests → **ALL tests pass** ✅
3. **TASK-EQS-005:** Implement → Write integration test → **ALL tests pass** ✅
4. **TASK-EQS-006:** Implement → Write unit tests → **ALL tests pass** ✅

**DO NOT** move to the next task until:
- Current task implementation complete
- Current task tests written and passing
- `dotnet build IOS-IG-SimHost.sln` succeeds without new errors

**DO NOT** stop mid-batch to ask for permission to run tests, fix errors, or build. Do it all.

---

## Tasks

### Corrective Task 0: Fix EqsResultEvent Type Mismatch (P1)

**File:** `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/EqsResultPool.cs`

**Problem:** `EqsResultEvent.Epoch` and `EqsResultEvent.RefreshTick` are `int` but must be
`uint` to match IMPLEM_DETAILS.md L:243–247, `EqsSensor.Epoch` (uint), and `EqsResultTopic`
(uint Epoch, uint RefreshTick). Type mismatch causes sign-widening in the staleness check and
requires casts in the DDS translator.

**Fix:** Change both fields to `uint`:
```csharp
public uint Epoch;        // was int
public uint RefreshTick;  // was int
```

**Verify:** `dotnet build FDP/FDP.sln` succeeds. Run `dotnet test FDP/Toolkits/Fdp.Toolkits.Tests/ --filter "Eqs"` to confirm all 7 existing tests still pass.

---

### Task 1: EqsResultUpdateSystem — Brain Side (TASK-EQS-004)

**Task Definition:** See [TASK-DETAIL.md](../TASK-DETAIL.md#task-eqs-004--eqsresultupdatesystem-brain-side)

**Design Reference:** Design §3.1 step 6–7; Impl L:595–680

**Files to create:**

**`Hrot/Subsystems/Hrot.SimHost/Systems/EqsResultUpdateEvent.cs`** (NEW):
- Managed event class (not struct) for the DDS-bridged online path.
- Fields: `Entity Observer`, `uint Epoch`, `uint RefreshTick`, `List<EqsResultEntry> Results`.
- `EqsResultEntry` from `Fdp.Toolkit.Spatial.Eqs.Topics` namespace (created in BATCH-01).

**`Hrot/Subsystems/Hrot.SimHost/Systems/EqsResultUpdateSystem.cs`** (NEW):
- `[UpdateInPhase(SystemPhase.Simulation)]` system.
- Handles TWO input paths transparently:

  **Path A — Online (DDS bridged managed event):**
  ```csharp
  // Reads EqsResultUpdateEvent published by EqsResultIngressTranslator
  var managedEvents = bus.ReadManaged<EqsResultUpdateEvent>();
  foreach (var evt in managedEvents)
  {
      if (!repo.IsAlive(evt.Observer)) continue;
      if (!repo.HasComponent<EqsSensor>(evt.Observer)) continue;
      ref var sensor = ref repo.GetComponentRO<EqsSensor>(evt.Observer);
      if (evt.Epoch != sensor.Epoch) continue;  // CRITICAL: epoch != not < (see Design §3.1 bug note)
      // lazy-add buffer, write via GetSpanRW()
  }
  ```

  **Path B — Offline (direct unmanaged event from local solver):**
  ```csharp
  // Reads EqsResultEvent emitted by EqsSolverSystem in the same world
  var unmanagedEvents = view.ReadEvents<EqsResultEvent>();
  // For each: look up entity by NetworkId, check epoch, lazy-add buffer, write pool results
  ref var pool = ref repo.GetSingletonUnmanaged<EqsResultPool>();
  // pool.Results[evt.ResultHandle .. evt.ResultHandle + evt.EntryCount]
  ```

  Both paths must:
  - Guard: entity alive AND has `EqsSensor`
  - Epoch staleness check: `evt.Epoch != sensor.Epoch` — do NOT compare against `LastUpdateTick`
  - Lazy-add `EqsCognitiveBuffer` if not present
  - Write results via `GetSpanRW()` (CRITICAL: not direct `[InlineArray]` index)
  - Set `buffer.Count` and `buffer.LastUpdateTick`

**Tests Required** (new file `FDP/Toolkits/Fdp.Toolkits.Tests/Eqs/EqsResultUpdateSystemTests.cs`
OR in `Hrot.ClusterRunner.Integration.Tests` using `EditorHarness`):

- `EqsResultUpdateSystem_StaleEpoch_IgnoresEvent`: entity has `EqsSensor{Epoch=2}`, publish
  `EqsResultUpdateEvent{Epoch=1}`, pump system — assert `EqsCognitiveBuffer` is NOT added.
- `EqsResultUpdateSystem_MatchingEpoch_PopulatesBuffer`: same entity, publish
  `EqsResultUpdateEvent{Epoch=2}` with 2 result entries, pump — assert `EqsCognitiveBuffer.Count == 2`
  and `IsReady == true` and first result X/Y matches the published entry.
- `EqsResultUpdateSystem_GetSpanRW_WritesPersist`: after the system writes to buffer via
  `GetSpanRW()`, read back via `GetSpanRO()` and assert values match (regression against
  defensive-copy silent-discard).

---

### Task 2: Stubbed EqsSolverSystem + EqsModule Wiring (TASK-EQS-005)

**Task Definition:** See [TASK-DETAIL.md](../TASK-DETAIL.md#task-eqs-005--stubbed-eqssolversystem-phase-1-stub)

**Design Reference:** Design §14 Phase 1; Impl L:543–600

**Files to create/modify:**

**`Hrot/Subsystems/Hrot.SimHost/Systems/EqsSolverSystem.cs`** (NEW):
- `[UpdateInPhase(SystemPhase.Simulation)]` system (or plain class used by module).
- Queries `EqsSensor + NetworkIdentity` using `view.Query()...Build()`.
- For each entity, emits `EqsResultEvent` with `EntryCount=0`:
  ```csharp
  cmd.PublishEvent(new EqsResultEvent
  {
      SensorNetworkId = netId.Value,
      Epoch           = sensor.Epoch,
      RefreshTick     = (uint)view.Tick,
      ResultHandle    = 0,
      EntryCount      = 0
  });
  ```
- Study the existing `AreaQuerySolverSystem.cs` for the exact query pattern and API usage.

**`Hrot/Subsystems/Hrot.SimHost/Modules/EqsModule.cs`** (UPDATE):
- Replace `private readonly Systems.AreaQuerySolverSystem _solver = new()` with the new
  `EqsSolverSystem`.
- Update `Tick()` to call `_solver.Execute(view, deltaTime)`.
- `AreaQuerySolverSystem` is NOT removed — it remains in `CognitiveSpatialModule`.
  `EqsModule` simply stops delegating to it.

**`EqsResultPool` singleton initialization:** Find where `SimHostCoreLogicPack` initializes
singletons (look at how `AreaQueryBatchData` or `EqsTargetPool` are initialized) and add
`EqsResultPool` initialization there with `NativeArray<EqsResult>` of capacity
`EqsResultPool.PoolCapacity`.

**Tests Required** (EditorHarness integration test, new class
`Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/Eqs/EqsSolverSystemTests.cs`):

- `EqsSolverSystem_Phase1Stub_PopulatesBufferAfter3Ticks`: using `EditorHarness`, spawn entity
  with `NetworkIdentity`, attach `EqsSensor`, pump 3 solver ticks (see how `EditorHarness.PumpAsync`
  or equivalent works), assert `EqsCognitiveBuffer.IsReady == true` and `Count == 0`.
  The test verifies that the offline round-trip (solver → event → update system → buffer) works.

---

### Task 3: BTree Lifecycle Nodes (TASK-EQS-006)

**Task Definition:** See [TASK-DETAIL.md](../TASK-DETAIL.md#task-eqs-006--btree-lifecycle-nodes-waitforsensor--maintaineqssensor)

**Design Reference:** Design §8, §11, §14; Impl L:685–810, L:3390–3550

**File to create:** `Hrot/Subsystems/Hrot.AI.Behaviors/Brains/EqsLifecycleNodes.cs` (NEW)

Study `Hrot/Subsystems/Hrot.AI.Behaviors/Brains/CgfNodes.cs` thoroughly for:
- The `[BTreeAction]` attribute pattern and method signature.
- How `[BTreeDeactivator]` is used (match on `TargetAction` string).
- How `ctx.World.HasComponent<T>` / `AddComponent` / `RemoveComponent` are called.
- The `@0` compound-key suffix convention for 3-param bridge actions (ONBOARDING.md).

**`EqsParams` unmanaged struct** (place in `EqsLifecycleNodes.cs`):
- Fields: `uint BlueprintId`, `float SearchRadius`, `float ThreatThreshold`, `uint FactionFilter`.
- Must be `[StructLayout(LayoutKind.Sequential)]` and unmanaged.

**`Action_MaintainEqsSensor`**:
- Signature matches existing BTree actions in `CgfNodes.cs` (observe the exact parameter types).
- Reads `EqsParams` from blackboard param slot.
- On first tick (no `EqsSensor` component): `AddComponent` with new `EqsSensor { Epoch=1, ... }`.
- On subsequent ticks: compare current params against `EqsSensor` fields; ONLY increment
  `sensor.Epoch` when a parameter actually changed (NOT every tick).
- Always returns `NodeStatus.Running`.

**`Deactivate_MaintainEqsSensor`**:
- `[BTreeDeactivator]` with the correct `TargetAction` string (use the full method name with
  namespace and the `@0` suffix if required by the 3-param bridge convention — check CgfNodes
  for examples).
- Removes both `EqsSensor` and `EqsCognitiveBuffer` from `ctx.Self` if they exist.
- `EqsCognitiveBuffer` must also be removed to prevent stale reads on re-activation.

**`Action_WaitForSensor`**:
- Returns `NodeStatus.Running` if entity has no `EqsCognitiveBuffer` or `!buffer.IsReady`.
- Returns `NodeStatus.Success` when `buffer.IsReady == true`.

**Tests Required** (EditorHarness integration tests):

- `EqsLifecycleNodes_WaitForSensor_ReturnsSuccessWhenReady`: tree with
  `Parallel(MaintainEqsSensor, WaitForSensor)`, pump until `WaitForSensor` returns
  `NodeStatus.Success`; this requires the Phase 1 stub solver to be active (TASK-EQS-005
  must be done first). Assert the result on the cognitive buffer: `IsReady == true`.

- `EqsLifecycleNodes_Deactivator_RemovesSensorOnAbort`: force branch abort (simulate tree
  switching away from this branch), pump one tick — assert `EqsSensor` component no longer
  exists on entity AND `EqsCognitiveBuffer` no longer exists.

- `EqsLifecycleNodes_MaintainSensor_EpochIncrementsOnlyOnParamChange`: mutate
  `EqsParams.SearchRadius` and re-tick `Action_MaintainEqsSensor` — assert `EqsSensor.Epoch`
  incremented exactly once. Tick again without changing params — assert `Epoch` did NOT
  increment again.

---

## Testing Requirements

**Minimum test count:** 7 tests (Corrective-0: 0 new tests + 3 from TASK-EQS-004 + 1 from
TASK-EQS-005 + 3 from TASK-EQS-006).

**Quality standards — only these are acceptable:**
- Tests that verify ACTUAL values in the cognitive buffer after system pump.
- Tests that verify ACTUAL absence/presence of components after deactivator fires.
- Tests that verify ACTUAL epoch increment count (not just "epoch changed").
- Integration tests that pump the real system, not just call the method directly.

**NOT ACCEPTABLE:**
- Tests that check only that an exception is not thrown.
- Tests that check only a component was added (not what it contains).
- Tests that directly call `EqsResultUpdateSystem.Execute()` with a mock view rather than
  pumping through the `EditorHarness` lifecycle.

---

## Quality Standards

**REPORT ACCURACY:** State the actual pre-existing failure count from the full test suite
(not just EQS-filtered). Run `dotnet test FDP/Toolkits/Fdp.Toolkits.Tests/` and report
the exact numbers.

**EPOCH CHECK:** The staleness check is `evt.Epoch != sensor.Epoch`. Do NOT compare epoch
against `LastUpdateTick` — that was the original bug. See Design §3.1 and ONBOARDING constraint.

**`@0` SUFFIX:** Verify the `[BTreeDeactivator]` `TargetAction` string for 3-param bridge
actions with the existing pattern in `CgfNodes.cs`. Incorrect suffix = deactivator never fires.

**NO LAZINESS:** Complete every task fully. Run all tests. Fix all errors at root cause before
moving to the next task. Do not stop to ask if you should run tests — just do it.

---

## Success Criteria

- [ ] `EqsResultEvent.Epoch` and `RefreshTick` are `uint` and all existing 7 tests still pass
- [ ] `EqsResultUpdateSystem` handles both online managed path and offline unmanaged path
- [ ] Staleness check is `evt.Epoch != sensor.Epoch` (not vs LastUpdateTick)
- [ ] `EqsSolverSystem` Phase 1 stub emits `EqsResultEvent` with `EntryCount=0` per sensor
- [ ] `EqsModule` drives new `EqsSolverSystem`; `AreaQuerySolverSystem` still runs in its own module
- [ ] `EqsResultPool` singleton initialized in `SimHostCoreLogicPack`
- [ ] Integration test: `EditorHarness` round-trip → `EqsCognitiveBuffer.IsReady == true` after 3 ticks
- [ ] `EqsParams`, `Action_MaintainEqsSensor`, `Deactivate_MaintainEqsSensor`, `Action_WaitForSensor` implemented
- [ ] Epoch increments ONLY on param change, not every tick
- [ ] Deactivator removes both `EqsSensor` AND `EqsCognitiveBuffer`
- [ ] All new tests pass; no pre-existing tests regressed
- [ ] `dotnet build IOS-IG-SimHost.sln` succeeds, 0 errors
- [ ] Report submitted

---

## Common Pitfalls to Avoid

- **Epoch check**: `evt.Epoch != sensor.Epoch` NOT `evt.Epoch < buffer.LastUpdateTick`.
  These compare different things; the latter is a known bug — see ONBOARDING constraint 3.
- **Two-path UpdateSystem**: In offline editor, `EqsResultEvent` is emitted by the local solver;
  there is no `EqsResultUpdateEvent`. Both paths must be handled by `EqsResultUpdateSystem`.
- **`[InlineArray]` write**: Always use `buffer.GetSpanRW()` to write into `EqsCognitiveBuffer`.
- **EqsModule vs AreaQuerySolverSystem**: `AreaQuerySolverSystem` remains — it's in
  `CognitiveSpatialModule`. `EqsModule` previously delegated to it (wrong); now it drives the
  new `EqsSolverSystem`. Do not remove `AreaQuerySolverSystem`.
- **Deactivator removes BOTH components**: removing only `EqsSensor` leaves a stale
  `EqsCognitiveBuffer` that will cause incorrect `IsReady == true` on re-activation.
- **`EqsResultPool` must be initialized**: it contains a `NativeArray<EqsResult>` that
  must be allocated before the solver runs; check how `EqsTargetPool` or `AreaQueryBatchData`
  are initialized in `SimHostCoreLogicPack`.

---

## Reference Materials

- **Task Defs:** `.dev/eqs-2/TASK-DETAIL.md` — TASK-EQS-004, TASK-EQS-005, TASK-EQS-006
- **Design:** `.dev/eqs-2/EQS_Design_v1.3_final.md` — §3.1, §8, §11, §14
- **Impl Details:** `.dev/eqs-2/IMPLEM_DETAILS.md` — L:543–810, L:3390–3550
- **BATCH-01 Review:** `.dev/eqs-2/reviews/BATCH-01-REVIEW.md`
- **Existing module pattern:** `Hrot/Subsystems/Hrot.SimHost/Modules/EqsModule.cs`
- **Existing solver pattern:** `Hrot/Subsystems/Hrot.SimHost/Systems/AreaQuerySolverSystem.cs`
- **Existing BTree nodes:** `Hrot/Subsystems/Hrot.AI.Behaviors/Brains/CgfNodes.cs`
- **EditorHarness:** `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/EditorHarness.cs`

---

## Developer Insights

**Q1:** What issues did you encounter implementing the two-path `EqsResultUpdateSystem`? How
did you decide when to read from the managed bus vs the unmanaged event?

**Q2:** What did you find when studying `AreaQuerySolverSystem` — any patterns you reused or
avoided?

**Q3:** What design decisions did you make for `Action_MaintainEqsSensor` epoch change detection?
How do you compare `EqsParams` fields against the current `EqsSensor` state?

**Q4:** Did you encounter any issues with `[BTreeDeactivator]` registration? How did you
verify the deactivator actually fires?

**Q5:** Suggested commit message?
