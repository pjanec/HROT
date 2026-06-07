# BATCH-31 Report — Phase 5 Part 5: Briefer Catalog Entries (P5-05)

**Status:** APPROVED  
**Tasks:** SC-P5-05-1, SC-P5-05-2, SC-P5-05-3  
**Test count:** 108 total (105 pre-existing + 3 new) — all pass

---

## Files Created

| File | Notes |
|---|---|
| `FDP/Toolkits/Fdp.Toolkits/Squad/Maneuvers/StackAndRoomEntryManeuver.cs` | No `unsafe` modifier |
| `FDP/Toolkits/Fdp.Toolkits/Squad/Maneuvers/TravellingOverwatchManeuver.cs` | `unsafe` class (uses `Unsafe.As` in `BuildRoleScoreMatrix`) |
| `FDP/Toolkits/Fdp.Toolkits.Tests/Squad/Maneuvers/BrieferCatalogManeuverTests.cs` | 3 new tests |

---

## Implementation Summary

### StackAndRoomEntryManeuver (§8.6a)

- `ManeuverKind = 5`
- 4 phases: `Stacking` (0) → `Entering` (1) → `Cleared` (2, terminal); `Aborted` (3, terminal)
- 3 named roles: `PointMan` (1), `BreachCover` (2), `Secondary` (3)
- `BuildTransitionTable()`: 4 entries (BoundComplete → Entering, Abort → Aborted from Stacking; FarSideReached → Cleared, Abort → Aborted from Entering)
- `StandardCandidates`: 4 slots — 1 PointMan + 1 BreachCover + 2 Secondary
- `BuildRoleScoreMatrix(int memberCount, Span<float> scoreMatrix)`: position-based scoring (no element lookup, no `unsafe` needed)

### TravellingOverwatchManeuver (§8.6b)

- `ManeuverKind = 6`
- 3 phases: `Moving` (0) → `Arrived` (1, terminal); `Aborted` (2, terminal)
- 2 named roles: `Lead` (1), `Overwatch` (2); 2 element constants: `ElementLead` (0), `ElementOverwatch` (1)
- `BuildTransitionTable()`: 2 entries (FarSideReached → Arrived, Abort → Aborted from Moving)
- `StandardCandidates`: 4 slots — 2 Lead + 2 Overwatch
- `ComputePartitionInputs`: first-half → Lead, second-half → Overwatch (Math.Max(1, count/2) split)
- `BuildRoleScoreMatrix(ref SquadCognitiveState, int, Span<float>)`: reads `state.Elements.MemberElements` via `Unsafe.As<MemberElementIndexArray, byte>` — `unsafe` class required

### BrieferCatalogManeuverTests

- `StackAndRoomEntry_AssignsFourDistinctRoles` (SC-P5-05-1): builds 4-member fixture, runs `BuildRoleScoreMatrix` + `AssignRoles`, asserts ≥3 distinct roles including PointMan, BreachCover, Secondary
- `TravellingOverwatch_FarSideReached_TransitionsToArrived` (SC-P5-05-2): exercises `PhaseSequencer.Advance` directly; asserts transition to `PhaseArrived`
- `CatalogCoverageCheck_AllPrimitivesExercised` (SC-P5-05-3): static `typeof(...).IsPublic` assertions for all 5 primitives (`ElementPartitionPrimitive`, `TacticalFeatureHandles`, `RoleSlotAssignmentPrimitive`, `PhaseSequencer`, `SlotRotation`)
- `BuildFixture` copied exactly from `HillCrestHullDownManeuverTests.cs`

---

## Build and Test Results

```
dotnet build FDP/Toolkits/Fdp.Toolkits.Tests/Fdp.Toolkits.Tests.csproj -c Debug
  Build succeeded. 0 Warning(s), 0 Error(s)

dotnet test ... --filter "FullyQualifiedName~Squad"
  Passed! - Failed: 0, Passed: 108, Skipped: 0, Total: 108
```

---

## Issues / Deviations

None. All constraints satisfied:
- No `unsafe` on `StackAndRoomEntryManeuver`
- `unsafe` only on `TravellingOverwatchManeuver` (uses `Unsafe.As`)
- No C# 12 `[]` collection expression syntax used (all arrays use `new T[] { ... }`)
- Only new files created; no existing files modified
- 0 build warnings
