# BATCH-30 Report — Phase 5 Part 4: Hill-Crest Hull-Down Maneuver (P5-04)

**Status:** APPROVED  
**Date:** 2026-05-30

---

## What Was Implemented

### Task 1: `HillCrestHullDownManeuver` static class

**File created:** `FDP/Toolkits/Fdp.Toolkits/Squad/Maneuvers/HillCrestHullDownManeuver.cs`

- `public static unsafe class HillCrestHullDownManeuver` (namespace `Fdp.Toolkit.Squad.Maneuvers`)
- `ManeuverKind = 4` (const ushort)
- Phase IDs: `PhaseDeploying = 0`, `PhaseFiring = 1`, `PhaseRetiring = 2`, `PhaseAborted = 3`
- Role IDs: `RoleUnassigned = 0`, `RoleDeploying = 1`, `RoleCovering = 2`
- Element indices: `ElementWave = 0`, `ElementReserve = 1`
- `BuildTransitionTable()` — 6-entry table covering Deploying/Firing/Retiring transitions plus Abort in each phase
- `ComputePartitionInputs(memberCount, waveSize, inputs)` — wave vs. reserve scoring
- `StandardCandidates` — 4 candidates (2 Deploying + 2 Covering)
- `BuildRoleScoreMatrix(ref state, memberCount, scoreMatrix)` — 4-column score matrix based on element membership; uses `Unsafe.As`/`Unsafe.AsRef` (hence `unsafe` on class)
- `ComputeTotalSlots(segmentLength, spacing)` — parity with legacy `HillAttackMutableState` formula: `Math.Max(1, (int)(segLen / spacing))` capped at 16; defaults spacing to 30f when 0

### Task 2: Tests

**File created:** `FDP/Toolkits/Fdp.Toolkits.Tests/Squad/Maneuvers/HillCrestHullDownManeuverTests.cs`

| Test | ID | Description |
|---|---|---|
| `WaveCycle_DeployFireRetire_CyclesBackToDeploying` | SC-P5-04-1 | Drives Deploying->Firing->Retiring->Deploying via PhaseSequencer.Advance |
| `SlotRotation_2Burns_Over6Slots_Leave4Usable_InOrder` | SC-P5-04-2 | Burns slots 0 and 2; verifies AcquireSlot returns 1,3,4,5,-1 in order |
| `SquadEventIngressSystem_DetectsFarSideReached_FromLiveNavStatus` | SC-P5-04-3 | Resume-trap: sets NavigationStatus after first Run tick, verifies second tick detects FarSideReached |
| `ComputeTotalSlots_MatchesLegacyFormula` | SC-P5-04-4 | Verifies 150/30=5, 0/30=1, 500/30=16 (capped), 150/0=5 (default spacing) |

`BuildFixture` registers `UnitRoster`, `Blackboard1024`, `WeaponState`, `NavigationStatus`, `UnitSubordinate`; adds `WeaponState { Ammo=10, MaxAmmo=10 }`, `NavigationStatus`, and `UnitSubordinate { Commander=commander }` to each member.

---

## Test Results

```
Passed!  - Failed: 0, Passed: 105, Skipped: 0, Total: 105
```

- Pre-existing squad tests: 101
- New tests (SC-P5-04-1 through SC-P5-04-4): 4
- **Total: 105**

---

## Issues

None. Build: 0 errors, 0 warnings. All 105 tests pass.
