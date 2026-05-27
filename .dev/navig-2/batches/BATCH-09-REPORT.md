# BATCH-09 Report: Phase 5 Replan Flow + NAV-P9-T4 Tests

**Batch Number:** BATCH-09
**Status:** COMPLETE
**Build:** 0 errors, 1 pre-existing warning (unrelated)
**Tests:** 214 passed, 0 failed, 0 skipped (204 pre-existing + 10 new)

---

## Tasks Completed

### Task 1: Extend `MoveToParams` with `Flags` and `MaxReplans`

**File:** `FDP/Toolkits/Fdp.Toolkits/Navigation/NavigationActions.cs`

Replaced two padding bytes (`_pad0`, `_pad1`) after `ReverseAllowed` with named public
fields `Flags` and `MaxReplans`. One byte of explicit padding (`_pad0`) retained to keep
the struct at 32 bytes. Added XML doc comments per contract.

### Task 2: Extend `NavigationIntent` with `Flags` and `MaxReplans`

**File:** `FDP/Toolkits/Fdp.Toolkits/Navigation/NavigationComponents.cs`

Replaced `_pad0` / `_pad1` after `Mode` with `public byte Flags` and `public byte MaxReplans`.
Struct size and field offsets unchanged.

### Task 3: Add constants to `NavigationConstants`

**File:** `FDP/Toolkits/Fdp.Toolkits/Navigation/NavigationConstants.cs`

Added after `FleeReplanIntervalTicks`:
- `DefaultMaxReplans = 3` (byte constant)
- `FlagBitAllowReplan = 0` (byte constant)
- `FlagBitAutoSendPathOnReplan = 4` (byte constant)
- Fixed missing closing brace for the `NavigationConstants` class that was present after the
  instruction-guided insertion.

### Task 4: Extend `FrustrationTicks`

**File:** `FDP/Toolkits/Fdp.Toolkits/CarKinem/Core/FrustrationTicks.cs`

Added two flag bytes after `Ticks`:
- `public byte MoveStartedFired` — set to 1 after `MoveStartedEvent` is fired
- `public byte BlockedEventFired` — set to 1 after `MoveBlockedEvent` is fired (throttle flag)
- Two explicit padding bytes (`_pad0`, `_pad1`) to keep the struct at 8 bytes

### Task 5: Update `MoveToExecutor` to copy flags

**File:** `FDP/Toolkits/Fdp.Toolkits/Navigation/Executors/MoveToExecutor.cs`

Added after `intent.ReverseAllowed = p.ReverseAllowed;`:
```csharp
intent.Flags      = p.Flags;
intent.MaxReplans = p.MaxReplans;
```

### Task 6: Extend `NavigationExecutionSystem` with replan flow and events

**File:** `FDP/Toolkits/Fdp.Toolkits/CarKinem/Systems/NavigationExecutionSystem.cs`

Replaced the entire `Execute` body logic with the Phase 5 replan flow:

1. **New-intent block**: added `frustration = new FrustrationTicks()` reset, then fires
   `MoveStartedEvent` and sets `MoveStartedFired = 1`.
2. **Skip-if-terminal guard**: added after the new-intent block — entities at a terminal
   result (`!= InProgress`) are skipped until a new intent is issued.
3. **Arrival block**: now fires `MoveCompletedEvent{Reason=Arrived}` before continuing.
4. **Frustration block** (replan flow):
   - On `Ticks > FrustrationTickLimit`: checks `AllowReplan` bit and replan budget.
   - If replan allowed and budget available: publishes `PathfindingRequestEvent`,
     `PathReplannedEvent`, optionally `NavigationPathDetailsResponseEvent` (AutoSendPathOnReplan
     bit), and `MoveBlockedEvent` (throttled via `BlockedEventFired`). Resets `Ticks = 0`,
     increments `status.ReplanCount`, stays `InProgress`.
   - Else (budget exhausted or replan not allowed): writes `FailedBlocked`, fires
     `MoveCompletedEvent{Reason=FailedBlocked}`.
5. **Moving branch**: resets both `Ticks` and `BlockedEventFired` when vehicle is moving,
   enabling a fresh `MoveBlockedEvent` for the next blocking episode.

**Note:** `MoveBlockedEvent` struct has no `RouteHandle` field (verified from source);
the publish call only sets `Target`.

### Task 7: Update `NavigationTestWorldFactory`

**File:** `FDP/Toolkits/Fdp.Toolkits.Tests/Navigation/NavigationTestWorldFactory.cs`

Added to `Create()`:
- `world.RegisterComponent<FrustrationTicks>()`
- `world.RegisterEvent<MoveStartedEvent>()`
- `world.RegisterEvent<PathReplannedEvent>()`
- `world.RegisterEvent<MoveBlockedEvent>()`
- `world.RegisterEvent<PathfindingRequestEvent>()`
- `world.RegisterEvent<WaypointReachedEvent>()`

### Task 8: Create `NavigationProgressTrackerSystemTests`

**File:** `FDP/Toolkits/Fdp.Toolkits.Tests/Navigation/NavigationProgressTrackerSystemTests.cs`

Created new test class with 10 tests (NAV-P9-T4):

| # | Test | Outcome |
|---|------|---------|
| 1 | `FirstTickOfMove_EmitsMoveStartedEvent` | PASS |
| 2 | `FirstTickOfMove_MoveStartedEvent_NotFiredOnSubsequentTicks` | PASS |
| 3 | `Arrived_EmitsMoveCompletedEventWithArrived` | PASS |
| 4 | `FailedBlocked_WithoutReplan_WritesMoveCompletedFailedBlocked` | PASS |
| 5 | `MoveBlocked_ThrottledEmission` | PASS |
| 6 | `MuscleInternalReplan_EmitsPathReplannedEvent` | PASS |
| 7 | `MuscleInternalReplan_BumpsReplanCount` | PASS |
| 8 | `AutoSendPathOnReplan_FiresPathDetailsResponse` | PASS |
| 9 | `AutoSendPathOnReplan_NotSet_NoResponseFired` | PASS |
| 10 | `ReplanBudgetExhausted_WritesFailedBlocked` | PASS |

### Additional Fix: `NavigationExecutionSystemTests.CreateWorld()`

Added `RegisterEvent<MoveStartedEvent>()` and `RegisterEvent<MoveCompletedEvent>()` to the
existing test helper to prevent event-bus throws now that `NavigationExecutionSystem` publishes
these events. The existing 204 tests all continued to pass.

---

## Issues Encountered and Resolutions

1. **Missing closing brace in `NavigationConstants.cs`**: The constant-insertion pass lost
   the closing `}` for the class. Detected during build and repaired.

2. **`MoveBlockedEvent` has no `RouteHandle` field**: Instructions showed
   `new MoveBlockedEvent { Target = entity, RouteHandle = ... }` but the actual struct
   (`PathfindingEvents.cs`) only has `Target` and `ReasonCode`. Removed the `RouteHandle`
   assignment before build.

3. **`using Fdp.Toolkit.Navigation.Systems` does not exist**: Instructions referenced this
   namespace for the test file, but `NavigationExecutionSystem` lives in `CarKinem.Systems`.
   Used the correct `using CarKinem.Systems;` instead.

---

## Verification

```
Build succeeded.  0 Error(s), 1 Warning(s) (pre-existing, unrelated to this batch)
Passed! Failed: 0, Passed: 214, Skipped: 0, Total: 214
```
