# BATCH-14 — EQS Phase 11: Context-Slot Generalization (EQS-035, EQS-036)

**Batch Number:** BATCH-14
**Tasks:** TASK-EQS-035, TASK-EQS-036
**Phase:** Phase 11 — Corrective: Context-slot generalization (architect finding #4)
**Estimated Effort:** 12–16 hours
**Report target:** `.dev/eqs-2/reports/BATCH-14-REPORT.md`

---

## Onboarding

Read these first:

- `.dev/eqs-2/ONBOARDING.md` — project orientation
- `.dev/eqs-2/TASK-DETAIL.md` §§ TASK-EQS-035, TASK-EQS-036 — full specs
- `.dev/eqs-2/EQS_Design_v1.3_final.md` §4.2 — context-slot design rationale
- `.dev/eqs-2/reviews/BATCH-13-REVIEW.md` — previous batch review (APPROVED)
- `.dev/eqs-2/DEBT-TRACKER.md` — open P3 items (D-01, D-02, D-03)

**BATCH-13 has been committed** (Phase 10 schema additions: FlagsMeaningful,
LastUpdateTimeSeconds, ScoreDeltaThreshold are all live).

**Open tech debt from BATCH-13 review:**
- D-02 (P3): `CheapLineOfSightTest` missing `FlagsMeaningful` on rejected path.
  Fix this in TASK-EQS-036's rewrite since you are touching that file anyway.

Key files for this batch:
- `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/EqsComponents.cs` — `EqsSensor` (context slots)
- `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/EqsDdsTopics.cs` — `EqsSensorConfigTopic`
- `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/CheapLineOfSightTest.cs` — LOS rewrite
- `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/AccurateLineOfSightTest.cs` — LOS rewrite
- `Hrot/Network/Hrot.Network.NED/SimHost/EqsSensorConfigEgressTranslator.cs`
- `Hrot/Network/Hrot.Network.NED/SimHost/EqsSensorConfigIngressTranslator.cs`
- `Hrot/Subsystems/Hrot.AI.Behaviors/Brains/EqsLifecycleNodes.cs` — `EqsParams`

Existing tests to keep passing:
- `CoverGeneratorAndLosTests.cs` (uses `CheapLineOfSightTest`)
- `AccurateLosPhaseTests.cs` (uses `AccurateLineOfSightTest`)
- All other 43 existing EQS tests

---

## Mandatory Workflow: Test-Driven Task Progression

For each task, in order:
1. Write the unit tests first (they will fail to compile until you add the feature).
2. Implement the feature.
3. Run `dotnet test` for the relevant project to verify new tests pass and no prior
   tests regress.
4. Only then move to the next task.

Build must pass with **0 errors, 0 warnings** before submitting the report.

---

## TASK-EQS-035 — Add context slots to `EqsSensor` and DDS topic

Full spec: `.dev/eqs-2/TASK-DETAIL.md` §TASK-EQS-035

**Summary:** Add three `Entity` context-slot fields to `EqsSensor` and to
`EqsSensorConfigTopic`. These are arbitrary entity handles (e.g., target, leader) whose
positions the LOS tests will read instead of the hardcoded `TargetMemory[0]` lookup.
The EQS subsystem assigns no semantic meaning — convention ("Slot 0 = Self, Slot 1 =
Target, Slot 2 = Leader") is documented only in the spawn helper.

### Changes required

**`EqsComponents.cs` — `EqsSensor` struct:**
- Add three new fields after `ScoreDeltaThreshold`:
  ```csharp
  /// <summary>Context slot 0 (by convention: Self). Position source for tests that need
  /// the observer position. Filled by the spawn/maintain helper.</summary>
  public Entity ContextSlot0;
  /// <summary>Context slot 1 (by convention: Target). Primary position source for LOS
  /// tests. Replaces TargetMemory[0] position read.</summary>
  public Entity ContextSlot1;
  /// <summary>Context slot 2 (by convention: Leader / Squad-mate). Optional secondary
  /// LOS context.</summary>
  public Entity ContextSlot2;
  ```
- `Entity` is an existing struct in `Fdp.Core`. Look at how it is used elsewhere (it is
  typically unmanaged and can be stored in ECS components). Verify the struct remains
  blittable/unmanaged after adding these fields.

**`EqsDdsTopics.cs` — `EqsSensorConfigTopic`:**
- Add three wire fields. DDS must carry the entity as a pair of integers (Index + Generation)
  since `Entity` is not directly serializable as-is on the wire. Use the existing pattern for
  transmitting entity references in other DDS topics — look at how `NetworkIdentity` or
  entity-typed fields are handled in existing translators.
  If there is no existing pattern, use:
  ```csharp
  public uint ContextSlot0EntityIndex;
  public uint ContextSlot0EntityGeneration;
  public uint ContextSlot1EntityIndex;
  public uint ContextSlot1EntityGeneration;
  public uint ContextSlot2EntityIndex;
  public uint ContextSlot2EntityGeneration;
  ```

**`EqsSensorConfigEgressTranslator.cs`:**
- When building the DDS sample from the sensor, serialize each context slot entity
  as `(entity.Index, entity.Generation)`. An `Entity.Null` serializes as `(0, 0)`.

**`EqsSensorConfigIngressTranslator.cs`:**
- When applying the DDS sample to the Muscle-side ghost `EqsSensor`:
  - For each slot: if `(Index == 0 && Generation == 0)` → `Entity.Null`.
  - Otherwise, attempt to resolve the Brain-side entity reference to a Muscle-side
    entity via the existing `NetworkEntityMap` (or `_entityMap` field used by other
    translators in the same file). The Brain-side `Entity` handle is not valid on Muscle,
    but for the **offline editor path** (no entity map), keep the entity handle as-is
    (the consumer reads from the SAME repository so the handle is directly valid).
  - If resolution fails (ghost not yet promoted), store `Entity.Null` in the slot.
    Do NOT block or throw — defer to the next replication tick.

**`EqsLifecycleNodes.cs` — `EqsParams`:**
- Add three new fields:
  ```csharp
  public Entity ContextSlot0;
  public Entity ContextSlot1;
  public Entity ContextSlot2;
  ```
- In `Action_MaintainEqsSensor`, copy the slot values into the sensor on initial
  `AddComponent` and update + increment `Epoch` when any of the three slots changes
  on subsequent ticks. Use `.Equals()` for entity comparison (do not use `==` reference
  equality unless the type supports it).

### Tests

Test class: `EqsContextSlotTests` in
`Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/Eqs/EqsContextSlotTests.cs`

**T-CS1 — `ContextSlot_RoundTrip_PreservesEntityValue`**
- `HrotRunnerHarness("simhost,cgf")`.
- Brain spawns parent entity (with `NetworkIdentity`). Also spawns a second "target"
  entity (with `NetworkIdentity`). Captures both `networkId`s.
- Brain sets `sensor.ContextSlot1 = targetEntity` (Brain-side entity handle).
- Pump until Muscle ghost `EqsSensor` gains `ContextSlot1 != Entity.Null`.
- Assert `muscleGhost.ContextSlot1` is the Muscle-side ghost entity for the target
  (NOT the Brain-side entity handle, since Entity.Index values differ between processes).
  Use `harness.SimHost.TestHook_EntityMap.TryGetEntity(targetNetworkId, out Entity muscleTarget)`.
  Assert `muscleGhost.ContextSlot1.Equals(muscleTarget)`.

**T-CS2 — `ContextSlot_NullEntity_Survives`**
- `HrotRunnerHarness("simhost,cgf")`.
- Brain sets all three slots to `Entity.Null` explicitly.
- Pump until sensor lands on Muscle.
- Assert all three Muscle-side slots are `Entity.Null`.

**T-CS3 — `ContextSlot_UnresolvedEntity_StaysNull`**
- Unit test (offline `EntityRepository` only — no harness).
- Build a fake sensor config DDS sample with `ContextSlot1EntityIndex = 999,
  ContextSlot1EntityGeneration = 1` (not a valid entity in the repo).
- Feed through the ingress translator manually.
- Assert `muscleGhost.ContextSlot1 == Entity.Null` (unresolved, not thrown).

**T-CS4 — `MaintainEqsSensor_ContextSlotChange_IncrementsEpoch`**
- Unit test (direct struct, like T-SD2).
- First tick: `ContextSlot1 = entityA`. Verify epoch incremented from initial.
- Second tick: same `ContextSlot1`. Verify epoch unchanged.
- Third tick: `ContextSlot1 = entityB`. Verify epoch incremented again.

---

## TASK-EQS-036 — Generalize LOS tests to read from context slots

Full spec: `.dev/eqs-2/TASK-DETAIL.md` §TASK-EQS-036

**Summary:** Replace the hardcoded `TargetMemory.PositionsX[0]/PositionsY[0]` position
reads in `CheapLineOfSightTest` and `AccurateLineOfSightTest` with a configurable context
slot index (default 1 = "Target" slot). Both tests now read the threat-entity position from
`sensor.ContextSlot1` (or whichever index is configured) via `view.GetComponentRO<SimTransform>`.

**Note:** The threat-score THRESHOLD check (`sensor.ThreatThreshold`) remains reading from
the observer's **own** `TargetMemory` — the slot is only the POSITION source. The threshold
gate still uses the observer's threat list.

Also fix D-02 from BATCH-13: set `FlagsMeaningful` on the rejected (exposed) path in
`CheapLineOfSightTest` while you are rewriting it.

### Changes to `CheapLineOfSightTest`

Current logic:
```
1. Bypass if no TargetMemory (no threats).
2. Bypass if TargetMemory.Count == 0.
3. Bypass if ThreatScores[0] < sensor.ThreatThreshold.
4. Read threat position from TargetMemory.PositionsX/Y[0].
5. Evaluate each candidate.
```

New logic after TASK-EQS-036:
```
1. Read the configured context slot from sensor (default: ContextSlot1).
2. If slot == Entity.Null -> bypass entirely (no threat configured);
   do NOT set FlagsMeaningful for any candidate.
3. Lookup SimTransform on the slot entity.
   If the slot entity has no SimTransform -> bypass (slot entity not yet ready);
   do NOT set FlagsMeaningful.
4. (Keep) Bypass if observer has no TargetMemory.
5. (Keep) Bypass if TargetMemory.Count == 0.
6. (Keep) Bypass if ThreatScores[0] < sensor.ThreatThreshold.
7. Read threat position from the slot entity's SimTransform.Position.
8. Evaluate each candidate.
   - Exposed (clear LOS): EntityId = -1L, FlagsMeaningful |= 1. [FIX D-02]
   - Covered (blocked LOS): Flags |= 1, FlagsMeaningful |= 1.
```

Add a `ContextSlotIndex` property (default 1):
```csharp
public byte ContextSlotIndex { get; set; } = 1;
```

### Changes to `AccurateLineOfSightTest`

Same pattern:
1. Add `public byte ContextSlotIndex { get; set; } = 1;`.
2. Replace `mem.PositionsX[0]/PositionsY[0]` position read with the slot entity's
   `SimTransform.Position`.
3. Keep the TargetMemory bypass checks (threshold gate still reads from observer's
   TargetMemory; only the position source changes).
4. If slot is `Entity.Null` or has no `SimTransform`, bypass entirely (same as
   old "no threats" path — do NOT submit raycasts).

### Migration of existing tests

The existing `CoverGeneratorAndLosTests.cs` and `AccurateLosPhaseTests.cs` tests currently
set up `TargetMemory` with threat position and do not set `sensor.ContextSlot1`. After this
change:
- `CheapLineOfSightTest` will now bypass when `ContextSlot1 == Entity.Null` (step 2 above).
- This will BREAK the existing tests because they relied on reading from TargetMemory.

You MUST update the existing tests:
- In each test fixture that exercises `CheapLineOfSightTest` or `AccurateLineOfSightTest`,
  set `sensor.ContextSlot1 = targetEntity` where `targetEntity` is an entity with a
  `SimTransform` whose Position matches the threat position previously read from TargetMemory.
- The TargetMemory (threat score threshold check) can remain in place or be removed if
  the test's purpose is position-reading only — **check each test individually**.
- Tests that exercise the threat-threshold bypass (e.g., T-FM2, the
  `Eqs_ThreatThreshold_BypassesContextFilters` test) must be updated to ALSO set
  `sensor.ContextSlot1` to a live entity with a SimTransform; then the threshold gate still
  reads from TargetMemory and the bypass still fires.

### New tests for TASK-EQS-036

Add to `EqsContextSlotTests.cs`:

**T-CS5 — `CheapLosTest_ReadsPositionFromContextSlot`**
- `EditorHarness`.
- Observer entity with no `TargetMemory` initially; sensor has `ContextSlot1 = targetEntity`.
- Target entity has `SimTransform.Position = (20f, 0f, 0f)`.
- `MockLosService` returns:
  - `true` (exposed) when called with target position ~(20, 0) → verify the rejection fires.
  - `false` for all other positions.
- Template: `CoverPointsGenerator` (3 cover points) + `CheapLineOfSightTest`.
- Set `sensor.ThreatThreshold = 0f` and observer's TargetMemory with threat score = 100 (above threshold)
  so the threshold gate passes.
- Run solver, assert all 3 candidates are rejected (EntityId == -1L in the intermediate span,
  buffer Count == 0).

**T-CS6 — `CheapLosTest_NullSlot_Bypasses`**
- `EditorHarness`.
- `sensor.ContextSlot1 = Entity.Null`.
- Observer has TargetMemory with a high-threat entity at position (100f, 0f).
- Template: `CoverPointsGenerator` + `CheapLineOfSightTest`.
- `MockLosService` returns `true` (exposed), but should never be called.
- Assert buffer.Count > 0 (all cover candidates survive, test bypassed).

**T-CS7 — `AccurateLosTest_ReadsPositionFromContextSlot`**
- `EditorHarness` with `MockRaycastSolverSystem`.
- `sensor.ContextSlot1 = targetEntity` where target has `SimTransform.Position = (30f, 0f, 0f)`.
- Template uses `AccurateLineOfSightTest`.
- Verify that raycast is submitted (phase enters `_AwaitingRaycasts`) and target position
  is derived from the slot entity's transform, not `TargetMemory`.
- Hint: check that `RaycastRequestEvent.End.X` == 30f in the mock.

---

## Developer Insights Section

In your report, answer:

1. **What issues were encountered?** (migration of existing tests, entity wire-encoding choices)
2. **What weak points were spotted?**
3. **What design decisions were made beyond the spec?** (e.g., how you handled the offline
   vs. distributed entity resolution, what Wire encoding you chose for Entity fields in DDS)
4. **Were there pre-existing test regressions?** If yes, which and how did you fix them?
5. **Did you fix D-02 (CheapLineOfSightTest FlagsMeaningful on reject path)?** (yes/no + confirmation)

---

## Report Format

Write `.dev/eqs-2/reports/BATCH-14-REPORT.md` with:

```markdown
# BATCH-14 Report

## Tasks Completed
- [ ] TASK-EQS-035 — Context slots on EqsSensor
- [ ] TASK-EQS-036 — LOS tests generalized to context slots

## Test Results
| Test ID | Name | Result |
|---------|------|--------|
| T-CS1 | ... | PASS/FAIL |
...

## Files Changed
- (list every file touched)

## Developer Insights
### Issues Encountered
### Weak Points Spotted
### Design Decisions Beyond Spec
### Pre-existing Test Regressions (and fixes)
### D-02 Fix Confirmation
```

---

## Success Gate

1. `dotnet build` — 0 errors, 0 warnings.
2. All `T-CS*` tests pass.
3. All 43 pre-existing EQS tests still pass (updated as needed for context-slot migration).
4. D-02 resolved: `CheapLineOfSightTest` sets `FlagsMeaningful` on BOTH paths (exposed and
   covered) when the test actually runs.
