# BATCH-01 Review

**Batch:** BATCH-01
**Tasks:** PACK-N001, PACK-N002, PACK-N003, PACK-N004, PACK-M001, PACK-M002
**Verdict:** ✅ APPROVED

---

## Verification Summary

| Project | Result |
|---|---|
| `FDP.Toolkit.Navigation.Tests` | ✅ 0 failed / 38 passed |
| `FDP.Toolkit.Combat.Tests` | ✅ 0 failed / 52 passed |
| `FDP.Toolkit.Behavior.Tests` | ✅ 0 failed / 75 passed |
| `Hrot.SimHost.Tests` | ✅ 0 failed / 408 passed (1 pre-existing excluded) |
| `Fdp.Examples.Scenarios.Tests` | ✅ 0 failed / 65 passed |

## Task Verification

### PACK-N001 ✅
- `NavigationStatus.ProgressS` field present in `NavigationComponents.cs` (verified).
- `ProgressS` appended at end of DDS `NavigationStatus` in `SimDescriptors.cs` (verified).

### PACK-N002 ✅
- `NavigationExecutionSystem` now writes `ProgressS` (unconditional write). Correct choice —
  ensures Brain nodes never see stale progress. Matches test assertions.

### PACK-N003 ✅
- Both egress and ingress translators updated (verified with test assertions).

### PACK-N004 ✅
- `RouteContextSystem.cs` has zero `NavState` references (confirmed by grep).
- Queries `NavigationIntent + NavigationStatus + BrainBlackboard`. Mode comparison correctly
  updated from `KinematicsMode.CustomTrajectory` to `NavigationMode.FollowRoute`.

### PACK-M001 ✅
- `CombatModule.cs` no longer registers or imports `HsmDamageBridgeSystem` (comment explains
  relocation).
- `CognitiveRuntimeModule.cs` registers it *before* `BTreeTickSystem` — correct ordering.

### PACK-M002 ✅
- `HealthApplicationSystem` strips only `CanMove` on non-lethal hits (not CanShoot).
- `ApcMobilityTriggerSystem` and `ApcMobilitySystem` deleted; zero source references remain.
- UrbanCombat `LatchApcHalted` scenario test still passes (65/65).

## Issues / Debt Recorded

### P3 — AllInOne mode lacks non-lethal CanMove stripping (design gap)
The spec intentionally targets the Brain/CQRS path only. `DamageSystem` (AllInOne path) does
NOT strip `CanMove` on non-lethal hits (an existing test's Part A explicitly prohibits it).
This is a known asymmetry between the two damage pipelines. Not a bug, but should be tracked
for the future AllInOne parity pass if needed.

### P3 — IReadOnlyList<T> lacks FindIndex (minor test ergonomics)
Workaround `.ToList().FindIndex(...)` required. Consider adding an `IndexOf` helper to the
test base or constraining the type to `List<T>` on APIs used in tests.

---

## Suggested Git Commit Messages

### Main repo (`d:\Work\IOS-IG-SimHost-FDP-2`)
```
feat(packs-1): BATCH-01 — NavigationStatus CQRS & Module Realignment

PACK-N001: Add ProgressS to NavigationStatus ECS struct and NED wire struct
PACK-N002: Map NavState.ProgressS → NavigationStatus.ProgressS in NavigationExecutionSystem
PACK-N003: Map ProgressS through NavigationStatus egress/ingress translators
PACK-N004: Refactor RouteContextSystem to Brain-only query (remove NavState dependency)
PACK-M001: Relocate HsmDamageBridgeSystem → CognitiveRuntimeModule (before BTree/HSM ticks)
PACK-M002: HealthApplicationSystem strips CanMove on non-lethal hit; delete ApcMobilityTriggerSystem / ApcMobilitySystem

Tests: 638 new/updated unit tests pass; UrbanCombat integration scenario unbroken.
```

### FDP submodule (`d:\Work\IOS-IG-SimHost-FDP-2\FDP`)
```
feat(packs-1): BATCH-01 — NavigationStatus ProgressS + module realignment

- Add float ProgressS to NavigationStatus struct (FDP.Toolkit.Navigation.Contracts)
- Map NavState.ProgressS → NavigationStatus.ProgressS in NavigationExecutionSystem
- Relocate HsmDamageBridgeSystem from CombatModule → CognitiveRuntimeModule
- HealthApplicationSystem: strip CanMove on non-lethal DamageAssessedEvent
- Delete ApcMobilityTriggerSystem (UrbanCombatNewScenario) and ApcMobilitySystem
```
