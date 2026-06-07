# BATCH-30 Review

**Status: APPROVED**

## Tests
- Squad-only: 105/105 pass (+4 over BATCH-29 baseline of 101)

## Code Review

### `HillCrestHullDownManeuver.cs` — PASS
- 6-entry transition table (FarSideReached/ShotFired/DefiladeReached drive Deploying→Firing→Retiring→Deploying
  cycle; Abort in each phase → Aborted terminal). Correct.
- Wave cycling: DefiladeReached from Retiring → back to PhaseDeploying (0) — correct loop.
- `ComputeTotalSlots`: `Math.Max(1, (int)(segLen / spacing))` capped at 16, default spacing 30f
  when 0 supplied — exactly matches legacy `HillAttackCommanderNodes.Action_CalculateSegments`.
- `StandardCandidates` 4-slot pattern consistent with P5-01..P5-03.
- `unsafe` class keyword present.
- Style matches existing maneuver files.

### `HillCrestHullDownManeuverTests.cs` — PASS (4 tests)
- SC-P5-04-1: Full wave cycle Deploying→Firing→Retiring→Deploying (3 transitions) ✓
- SC-P5-04-2: Burn 2 of 6 slots, 4 remaining acquired in order 1,3,4,5, 5th=-1 ✓
- SC-P5-04-3: SquadEventIngressSystem detects FarSideReached from live NavStatus (not cached) ✓
- SC-P5-04-4: ComputeTotalSlots matches legacy formula for 150m/30m=5, 0m=1, 500m=16 (capped) ✓

### Parity proof scope
The parity test (SC-P5-04-2, SC-P5-04-3) validates the abstract slot-rotation and live-detection
semantics match the legacy behavior, without running the full BTree simulation. This is appropriate
given the fabricated-fixture constraint from the task spec.

## No issues found.
