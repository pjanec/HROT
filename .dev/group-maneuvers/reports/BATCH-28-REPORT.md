# BATCH-28 Report — Bounding Overwatch Maneuver (P5-02)

**Status:** APPROVED  
**Task:** TASK-SQD-P5-02  
**Date:** 2026-05-30

---

## Summary

Implemented the `BoundingOverwatchManeuver` static class and 5 integration tests covering
the 3-phase leapfrog maneuver (Element0Moving → Element1Moving → Aborted).

---

## Files Created

| File | Description |
|---|---|
| `FDP/Toolkits/Fdp.Toolkits/Squad/Maneuvers/BoundingOverwatchManeuver.cs` | Static class with constants, transition table, partition inputs, role matrix, GetMovingElement |
| `FDP/Toolkits/Fdp.Toolkits.Tests/Squad/Maneuvers/BoundingOverwatchManeuverTests.cs` | 5 tests: SC-P5-02-1 through SC-P5-02-5 |

---

## Implementation Details

### BoundingOverwatchManeuver.cs

- Namespace: `Fdp.Toolkit.Squad.Maneuvers`
- `ManeuverKind = 2`
- 3 phases: `PhaseElement0Moving=0`, `PhaseElement1Moving=1`, `PhaseAborted=2`
- 3 roles: `RoleUnassigned=0`, `RoleMoving=1`, `RoleCovering=2`
- 2 elements: `ElementAlpha=0`, `ElementBravo=1`
- `BuildTransitionTable()`: 4 entries (2 per active phase: BoundComplete + Abort)
- `ComputePartitionInputs()`: first-half → Alpha, second-half → Bravo (same heuristic as DangerAreaCrossingManeuver)
- `StandardCandidates`: 4 slots (2 Moving + 2 Covering) for full 4-member squad coverage
- `BuildRoleScoreMatrix()`: 4-column matrix; moving element scores 1.0/0.1, covering element scores 0.1/1.0
- `GetMovingElement()`: Phase 0 → ElementAlpha, Phase 1 → ElementBravo

Style followed `DangerAreaCrossingManeuver.cs` exactly (usings, doc comments, inline initializers,
`new T[] { ... }` array syntax, no C# 12 collection expressions).

### BoundingOverwatchManeuverTests.cs

`BuildFixture` is an exact copy of the one in `DangerAreaCrossingManeuverTests.cs`
(same component registrations: `UnitRoster`, `Blackboard1024`, `NavigationStatus`, `UnitSubordinate`).

Tests use `new ReadOnlySpan<PhaseEvent>(new[] { ... })` as specified (no C# 12 `[]` syntax).

---

## Test Results

```
Passed! - Failed: 0, Passed: 97, Skipped: 0, Total: 97
```

| Test | ID | Result |
|---|---|---|
| `BoundingOverwatch_PhaseAlternates_OnBoundComplete` | SC-P5-02-1 | PASS |
| `BoundingOverwatch_AbortEvent_TransitionsToAborted` | SC-P5-02-2 | PASS |
| `RoleAssignment_AtMost2Members_HaveMovingRole` | SC-P5-02-3 | PASS |
| `RoleAssignment_AfterSwap_Element0MembersGetCoveringRole` | SC-P5-02-4 | PASS |
| `GetMovingElement_ReturnsCorrectElement_ForEachPhase` | SC-P5-02-5 | PASS |
| All 92 pre-existing squad tests | — | PASS |

**Note on flaky test:** `AllReaders_ZeroAlloc_After1MillionCalls` (in `SquadInputsP3Tests`) occasionally
fails when run alongside all 97 squad tests due to GC noise from parallel test memory — it passes
reliably in isolation and on the second run. This is a pre-existing issue unrelated to BATCH-28.

---

## Build

```
Build succeeded. 0 Warning(s), 0 Error(s)
```

No new warnings introduced.

---

## Checklist

- [x] `BoundingOverwatchManeuver.cs` compiles with 0 errors
- [x] 5 new tests pass (SC-P5-02-1 through SC-P5-02-5)
- [x] All 92 pre-existing squad tests still pass
- [x] Total squad tests: 97 (92 + 5)
- [x] No new warnings
- [x] No existing files modified
