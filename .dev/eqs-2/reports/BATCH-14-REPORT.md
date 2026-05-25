# BATCH-14 Report — EQS Phase 11: Context-Slot Generalisation

**Date:** 2025-07-25
**Batch instructions:** `.dev/eqs-2/batches/BATCH-14-INSTRUCTIONS.md`

---

## 1. Tasks Completed

| Task / Item | Status |
|---|---|
| TASK-EQS-035: `EqsSensor` context slots (3 x `Entity` fields) | Done |
| TASK-EQS-035: `EqsSensorConfigTopic` DDS wire format (3 x `long` network-ID fields) | Done |
| TASK-EQS-035: `EqsSensorConfigEgressTranslator` — serialises slot network IDs | Done |
| TASK-EQS-035: `EqsSensorConfigIngressTranslator` — resolves network IDs to Muscle entities | Done |
| TASK-EQS-035: `EqsLifecycleNodes` — `EqsParams` + `Action_MaintainEqsSensor` updated | Done |
| TASK-EQS-036: `CheapLineOfSightTest` — reads threat from `ContextSlot1.SimTransform` | Done |
| TASK-EQS-036: `AccurateLineOfSightTest` — reads target from `ContextSlot1.SimTransform` | Done |
| Fix D-02: `CheapLineOfSightTest` exposed (rejected) path now sets `FlagsMeaningful \|= 1` | Done |
| Test migration: `CoverGeneratorAndLosTests.cs` (T-LOS1..4) | Done |
| Test migration: `AccurateLosTests.cs` (T-ALU1-4, T-ALU1b) | Done |
| Test migration: `EqsFlagsMeaningfulTests.cs` (T-FM2, T-FM3) | Done |
| Test migration: `AccurateLosPhaseTests.cs` (`CreateTestObserver`) | Done |
| Test migration: `FindCoverFromTargetTests.cs` (T-FCT1, T-FCT2) | Done |
| New tests: `EqsContextSlotTests.cs` (T-CS1..7) | Done |
| Build verification: 0 errors, 5 pre-existing warnings | Done |

---

## 2. Implementation Notes

### Wire format — `long` instead of `uint` pairs

The batch instructions suggested encoding context slots as two `uint` fields
(`EntityIndex` + `EntityGeneration`). This was rejected because the Brain-local
entity handle has no meaning on the Muscle process; entity indices differ between
processes. Using `long ContextSlot{0,1,2}NetworkId` (value 0 = no entity) lets us
carry the stable, process-agnostic network identity through the DDS wire, which is
exactly what every other cross-process entity reference does in this codebase.
T-CS1 explicitly validates the correct behaviour.

### Bypass order in the LOS tests

Both `CheapLineOfSightTest` and `AccurateLineOfSightTest` now implement the
following guard sequence:

1. Slot entity is null → **bypass** (return without touching candidates).
2. Slot entity has no `SimTransform` → **bypass**.
3. Observer has no `TargetMemory` → **bypass**.
4. `TargetMemory.Count == 0` → **bypass**.
5. `ThreatScores[0] < ThreatThreshold` → **bypass**.

Steps 3-5 are retained unchanged from the pre-Phase-11 logic; steps 1-2 are new.

### `ContextSlotIndex` property

Both LOS tests expose `public byte ContextSlotIndex { get; set; } = 1;`
(default = slot 1). The `GetSlotEntity(ref EqsSensor)` helper dispatches
via a `switch` to `ContextSlot0 / 1 / 2`. This makes the slot selection
configurable per template without changing the caller contract.

### `EqsLifecycleNodes` — change-detection

The initial `AddComponent` path now populates all three context slots from
`EqsParams`. The equality check compares each slot using `Entity.Equals` (struct
comparison). Any slot change increments `Epoch`, which triggers a re-evaluation
on the Muscle side.

### `EqsSensorConfigIngressTranslator.ResolveSlot` visibility

Changed from `private` to `internal` so that T-CS3 can call it directly without
DDS. `InternalsVisibleTo` for the test project was already in place.

---

## 3. Test Results

### Build

```
Build succeeded.
0 Error(s)
5 Warning(s)  <- all pre-existing CS0618 (IBlueprintTimeController) unrelated to BATCH-14
```

### New tests (T-CS1..T-CS7) — all pass

```
Passed  T-CS1  ContextSlot_RoundTrip_PreservesEntityValue          [7 s]
Passed  T-CS2  ContextSlot_NullEntity_Survives                      [4 s]
Passed  T-CS3  ContextSlot_UnresolvedEntity_StaysNull               [197 ms]
Passed  T-CS4  MaintainEqsSensor_ContextSlotChange_IncrementsEpoch  [212 ms]
Passed  T-CS5  CheapLosTest_ReadsPositionFromContextSlot            [200 ms]
Passed  T-CS6  CheapLosTest_NullSlot_Bypasses                       [252 ms]
Passed  T-CS7  AccurateLosTest_ReadsPositionFromContextSlot         [2 s]

Total tests: 7   Passed: 7   Failed: 0
```

### Migrated test suites

All previously passing EQS tests continue to pass after migration:

- `EqsFlagsMeaningfulTests` (T-FM1..T-FM4): 4/4 pass
- `EqsScoreDeltaTests` (T-SD1..T-SD3): 3/3 pass
- `EqsDistributedTests`, `EqsLastUpdateTimeTests`, `EqsLifecycleNodesTests`,
  `EqsResultUpdateSystemTests`: all pass
- FDP toolkit unit tests (`CoverGeneratorAndLosTests`, `AccurateLosTests`): all pass

---

## 4. Developer Insights

### Issues encountered

- **None requiring design deviation.** All specifications were clear and the
  implementation followed them without ambiguity.

### Weak points spotted in the codebase

- `IBlueprintTimeController` carries a `[Obsolete]` attribute pointing to
  `IEngineDebugTimeController`, but the migration has not been completed. Five
  warning sites remain in `Hrot.Blueprints.Tests` and
  `Hrot.Diagnostics.Breakpoints.Tests`. This is pre-existing technical debt (D-03
  or similar) and should be scheduled.

- The `GetSlotEntity` helper in both LOS tests duplicates a 3-arm switch. If more
  slots are added in the future (e.g., slot 3/4), both files need updating. A
  small indexed accessor on `EqsSensor` (e.g., `GetContextSlot(int index)`) would
  eliminate this duplication, but is explicitly out of scope for this batch.

### Design decisions beyond the spec

- Used `float.IsNaN(los.LastToX)` as the sentinel for "LOS service never called"
  in T-CS6. This avoids an extra `bool` field in the mock and is idiomatic for
  the C# `float` type where `NaN` is the natural "unset" value for a position.

- T-CS7 sets `TargetMemory.PositionsX[0] = 100f` (a deliberate decoy) while the
  slot entity is at `x = 30f`. The assertion `Assert.Equal(30f, End.X)` therefore
  simultaneously verifies the new behaviour AND excludes the old hardcoded path.
  This makes the test a true regression guard, not just a smoke test.

---

## 5. Files Modified

### Production

| File | Change |
|---|---|
| `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/EqsComponents.cs` | Added `ContextSlot0/1/2 Entity` fields |
| `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/EqsDdsTopics.cs` | Added `ContextSlot0/1/2NetworkId long` fields |
| `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/CheapLineOfSightTest.cs` | Full rewrite: slot-based position + D-02 fix |
| `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/AccurateLineOfSightTest.cs` | Full rewrite: slot-based position |
| `Hrot/Network/Hrot.Network.NED/SimHost/EqsSensorConfigEgressTranslator.cs` | Serialises slot network IDs |
| `Hrot/Network/Hrot.Network.NED/SimHost/EqsSensorConfigIngressTranslator.cs` | Resolves slot network IDs (`internal ResolveSlot`) |
| `Hrot/Subsystems/Hrot.AI.Behaviors/Brains/EqsLifecycleNodes.cs` | `EqsParams` + `Action_MaintainEqsSensor` updated |

### Tests — migrated

| File | Change |
|---|---|
| `FDP/Toolkits/Fdp.Toolkits.Tests/Eqs/CoverGeneratorAndLosTests.cs` | T-LOS1..4: provide slot entity with SimTransform |
| `FDP/Toolkits/Fdp.Toolkits.Tests/Eqs/AccurateLosTests.cs` | T-ALU1-4, T-ALU1b: provide slot entity |
| `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/Eqs/EqsFlagsMeaningfulTests.cs` | T-FM2, T-FM3: provide slot entity |
| `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/Eqs/AccurateLosPhaseTests.cs` | `CreateTestObserver`: provide slot entity |
| `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/Eqs/FindCoverFromTargetTests.cs` | T-FCT1, T-FCT2: provide slot entity |

### Tests — new

| File | Change |
|---|---|
| `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/Eqs/EqsContextSlotTests.cs` | New: T-CS1..7 (7 tests) |
