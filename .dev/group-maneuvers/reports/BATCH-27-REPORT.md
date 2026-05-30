# BATCH-27 Report — Phase 5 Part 1: Danger-Area Crossing Maneuver (P5-01)

## Status: COMPLETE

**Task:** TASK-SQD-P5-01  
**Tests:** 92 total squad tests (86 pre-existing + 6 new) — all pass

---

## Files Created

| Action | File |
|--------|------|
| CREATE | `FDP/Toolkits/Fdp.Toolkits/Squad/Maneuvers/DangerAreaCrossingManeuver.cs` |
| CREATE | `FDP/Toolkits/Fdp.Toolkits.Tests/Squad/Maneuvers/DangerAreaCrossingManeuverTests.cs` |

---

## Test Results

```
Passed!  - Failed: 0, Passed: 92, Skipped: 0, Total: 92
```

New tests (SC-P5-01-1 through SC-P5-01-6):
- SC-P5-01-1: `ManeuverRunsAllFivePhases_InOrder` — PASS
- SC-P5-01-2: `ElementPartition_SplitsSquad_IntoTwoElements` — PASS
- SC-P5-01-3: `RoleAssignment_AssignsCrossingAndSecurityRoles` — PASS
- SC-P5-01-4: `ReassignFirstAcrossToCovering_ChangesRoleToSecurity` — PASS
- SC-P5-01-5: `SlotRotation_TwoCrossers_UseDifferentLanes` — PASS
- SC-P5-01-6: `PhaseSequencer_NoTransition_WhenNoEventAndDwellNotElapsed` — PASS

Build: 0 errors, 0 warnings.

---

## Deviations from Instructions

### 1. `StandardCandidates` — 4 entries instead of 2

**Instruction spec:** 2-entry `StandardCandidates` (Crossing + Security) and a 2-column `BuildRoleScoreMatrix`.

**Actual implementation:** 4-entry `StandardCandidates` (Crossing A, Crossing B, Security A, Security B) and a 4-column `BuildRoleScoreMatrix`.

**Reason:** `RoleSlotAssignmentPrimitive.AssignRoles` calls `GreedyMatrixAssigner.Assign` with `maxFocusFire: 1`, which limits each candidate slot to exactly one member. With only 2 candidates and 4 members, the greedy algorithm assigns 1 member to Crossing and 1 member to Security, leaving the other 2 with `RoleId = 0` (unassigned). The instruction's test assertion (all 4 members get correct roles) would then fail. Using 4 candidate slots (2 per role) lets `maxFocusFire: 1` assign all 4 members correctly.

The score matrix columns are:
- 0: Crossing slot A — score 1.0 for crossing-element members
- 1: Crossing slot B — score 0.9 for crossing-element members (second priority)
- 2: Security slot A — score 1.0 for security-element members
- 3: Security slot B — score 0.9 for security-element members (second priority)

Test 3 (`RoleAssignment_AssignsCrossingAndSecurityRoles`) uses `stackalloc float[4 * 4]` accordingly.

### 2. `BuildTransitionTable` — array initializer syntax instead of collection expression

Replaced `[ ... ]` collection expression with `new PhaseTransitionEntry[] { ... }` per the batch rule prohibiting C# 12 collection expression syntax.

### 3. `StandardCandidates` — array initializer syntax instead of collection expression

Same as above: replaced `[ ... ]` with `new RoleSlotCandidate[] { ... }`.

### 4. Test SC-P5-01-5 — `SlotRotation.AcquireSlot` instead of `AllocateNextSlot`

The instruction uses `SlotRotation.AllocateNextSlot(ref state, candidateCount: 2)` which does not exist. The actual API is `SlotRotation.AcquireSlot(ref SlotRotationState rotation, int totalSlots)`. The test uses a standalone `SlotRotationState` local variable (not projected from `SquadCognitiveState`).

### 5. `repo.Dispose()` added at end of repo-based tests

Tests 1–4 call `repo.Dispose()` explicitly since the `EntityRepository` is created in `BuildFixture` and not shared. The instruction's `BuildFixture` does not show `IDisposable` pattern, but disposing is correct practice for deterministic cleanup.
