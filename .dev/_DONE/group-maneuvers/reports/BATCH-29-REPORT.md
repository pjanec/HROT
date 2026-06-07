# BATCH-29 Report — Phase 5 Part 3: Suppress-and-Maneuver (P5-03)

**Status:** APPROVED  
**Task:** TASK-SQD-P5-03  
**Date:** 2026-05-30

---

## What was implemented

### New file: `FDP/Toolkits/Fdp.Toolkits/Squad/Maneuvers/SuppressAndManeuverManeuver.cs`

Static class `SuppressAndManeuverManeuver` (namespace `Fdp.Toolkit.Squad.Maneuvers`):

- **Constants:** `ManeuverKind = 3`, phase IDs (`PhaseSuppressing=0`, `PhaseAssaultComplete=1`, `PhaseAborted=2`), role IDs (`RoleBaseOfFire=1`, `RoleAssault=2`), element indices (`ElementBaseOfFire=0`, `ElementAssault=1`).
- **`BuildTransitionTable()`** — 2-entry table: `PhaseSuppressing + FarSideReached -> PhaseAssaultComplete`, `PhaseSuppressing + Abort -> PhaseAborted`.
- **`ComputePartitionInputs()`** — First half of members score high for BaseOfFire element; second half score high for Assault element.
- **`StandardCandidates`** — 4-slot array: 2 BaseOfFire + 2 Assault.
- **`BuildRoleScoreMatrix()`** — 4-column matrix; BaseOfFire element members score high for BaseOfFire columns, Assault element members score high for Assault columns. Uses `Unsafe.As`/`MemoryMarshal` pattern matching `BoundingOverwatchManeuver`.

Class marked `unsafe` per coding rules (uses `Unsafe.As`).

### New file: `FDP/Toolkits/Fdp.Toolkits.Tests/Squad/Maneuvers/SuppressAndManeuverManeuverTests.cs`

4 tests covering SC-P5-03-1 through SC-P5-03-4:

| Test | Criterion | Result |
|---|---|---|
| `Suppressing_FarSideReached_TransitionsToAssaultComplete` | SC-P5-03-1 | Pass |
| `Suppressing_AbortEvent_TransitionsToAborted` | SC-P5-03-2 | Pass |
| `RoleAssignment_SplitsBaseOfFireAndAssault_Correctly` | SC-P5-03-3 | Pass |
| `PhaseSequencer_DwellTimeout_TransitionsToRecovery_WhileSuppressing` | SC-P5-03-4 | Pass |

`BuildFixture` helper copied exactly from `BoundingOverwatchManeuverTests.cs` (same registrations).

---

## Test results

```
Passed!  - Failed: 0, Passed: 101, Skipped: 0, Total: 101
```

- Pre-existing squad tests: 97 (all pass, no regressions)
- New tests added: 4
- **Total: 101**

## Build

```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

## Issues

None.
