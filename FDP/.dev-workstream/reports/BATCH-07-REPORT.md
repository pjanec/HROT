# BATCH-07 Report

**Batch:** BATCH-07  
**Developer:** GitHub Copilot  
**Date:** 2026-02-24  
**Status:** ✅ COMPLETE

---

## Test Results

```
dotnet test FDP.sln
```

All test assemblies green:

| Assembly | Passed | Failed | Skipped |
|---|---|---|---|
| FDP.Toolkit.Navigation.Tests | 17 | 0 | 0 |
| FDP.Toolkit.Perception.Tests | 18 | 0 | 0 |
| FDP.Toolkit.CarKinem.Tests | 113 | 0 | 0 |
| FDP.Toolkit.Behavior.Tests | 22–25 | 0 | 0 |
| Fdp.Tests | 675 | 0 | 2 (pre-existing) |
| All others | ✅ | 0 | — |

**New tests added:** 10 executor tests (3 MoveToExecutor + 3 FleeExecutor + 2 FollowRoadGraphExecutor + 2 FollowRouteExecutor).

---

## Q1 (DEBT-015): Zero-score retention policy

**What DESIGN.md §4.3 says:**

> | `ThreatEvaluationSystem` | Async (SoD) | Simulation | Decays scores, integrates `TargetVisibleEvent` + `AudioStimulusEvent`; writes back via ECB |

The design describes only **decay** — there is no eviction step in the specification. The text is silent on what happens when a score reaches zero. No phrase like "evict", "remove", or "garbage-collect" appears anywhere in §4.3.

**Decision:** The current test `ThreatEvaluation_ZeroScoreEntry_IsRetained` is **correct** for Phase 2. The design does not mandate eviction, and the test's existing comment accurately documents the retention behaviour as a deliberate Phase 2 choice. I added an expanded XML `<summary>` to the test method explicitly quoting the relevant DESIGN.md paragraph and stating the eviction precondition (see `ThreatEvaluationSystemTests.cs`). DEBT-015 resolved.

---

## Q2 (MoveToExecutor frustration counter storage)

**Chosen approach:** `Dictionary<int, int>` keyed by `entity.Index`, held as a private field on the `MoveToExecutor` instance.

**Why:** The executor is a class instance allocated once at startup and registered with the dispatcher. A simple `int _stuckTicks` field would be shared across ALL entities using this executor simultaneously — a correctness bug whenever two entities have active `MoveTo` actions in the same frame. The dictionary gives true per-entity isolation with O(1) amortised look-up.

**Trade-offs vs. a state component:**

| | Dictionary<int,int> | State component in LocomotionChannel.State |
|---|---|---|
| Schema change | None | None — LocomotionChannel.State is 32 bytes, `int` fits |
| ECS discipline | Breaks "state lives in ECS" principle | Fully ECS-native |
| Code simplicity | Simple dict ops | Requires fixed-buffer write/read (unsafe) |
| Cache locality | Dictionary heap object | Part of LocomotionChannel struct |
| Chosen | ✅ | — |

The dictionary approach was chosen for simplicity (minimal unsafe code in the executor) and because the frustration counter is a transient implementation detail, not observable ECS state. A comment in the source documents this decision. The OnExit lifecycle cleans up the entry to prevent stale data accumulation for recycled entity indices.

---

## Q3 (FleeExecutor_ReportsSuccess_WhenThreatEntityIsDead walkthrough)

**Step-by-step:**

1. **Setup:** Two entities are created — `self` and `threat`. `FleeParams.Threat` is set to the full `Entity` handle returned by `world.CreateEntity()`. That handle carries `(Index, Generation)` — e.g., `Entity(1, generation=0)`.

2. **OnEnter:** The executor stores no extra state — `FleeParams.Threat` lives in `channel.Params`. Initial flee destination is computed; `NextReplanTick` is stored in `channel.State`.

3. **First Execute (threat alive):** `world.IsAlive(params.Threat)` checks whether index 1's live generation matches the stored generation (0 == 0 → true). Executor proceeds to safe-distance check; entity is within safe distance → `Status = Running`.

4. **`world.DestroyEntity(threat)`:** The entity repository bumps index 1's generation counter (now generation=1). The slot is marked free.

5. **Second Execute (threat dead):** `world.IsAlive(params.Threat)` checks index 1's live generation against the stored generation. Stored = 0, live = 1 → **mismatch → false**. Executor immediately sets `channel.Status = NodeStatus.Success` and returns.

**Generation value at the moment of the check:**
- Stored handle in `FleeParams.Threat`: generation = 0  
- World's generation table for index 1: generation = 1  
- `IsAlive` returns false because 0 ≠ 1.

This is the DEBT-009 fix propagating all the way to executor behaviour: the `Entity` struct carries the generation because it was fixed in BATCH-06 (raw `int` index → full `Entity` handle). Without that fix, `FleeParams.Threat` would have been a raw `int`, and `IsAlive` could never be called correctly.

---

## Q4 (Double-write of NavState in first tick)

**Scenario:** `DispatcherSystemBase` calls `OnEnter` followed immediately by `Execute` on the very first tick an action becomes active (the `ActionInstanceId` changed — OnEnter is fired, then Execute is also fired in the same frame).

**Is this a double-write?**

Yes, technically. `OnEnter` writes `NavState.Mode`, `FinalDestination`, `ArrivalRadius`, `TargetSpeed` and clears `HasArrived`. Then, in the same frame, `Execute` reads `HasArrived` (0) and checks the frustration guard. For `MoveToExecutor`, Execute does NOT write NavState in the first frame (unless the vehicle is already stuck). For `FleeExecutor`, Execute checks `IsAlive` and may re-compute `FinalDestination` if `FrameNumber >= NextReplanTick`. Since `OnEnter` sets `NextReplanTick = FrameNumber + FleeReplanIntervalTicks`, the initial `Execute` call will NOT trigger a replan because `FrameNumber < NextReplanTick`.

**Is this a correctness problem?** No, for these executors:
- `MoveToExecutor`: Execute only writes `channel.Status` on success/failure, not NavState. No conflict.
- `FleeExecutor`: The replan guard (`FrameNumber >= NextReplanTick`) prevents the first Execute from overwriting the destination that OnEnter just computed.
- `FollowRoadGraphExecutor` / `FollowRouteExecutor`: Execute only checks `HasArrived`; it only writes `channel.Status`. No NavState conflict.

**Architectural note:** The contract is intentional — `OnEnter` sets up the action, and `Execute` runs first in the same frame. Executors must be designed so that the first Execute call is safe after OnEnter. All four executors satisfy this invariant.

---

## Changes Made

### Corrective

| Item | File | Change |
|---|---|---|
| DEBT-015 | `FDP.Toolkit.Perception.Tests/ThreatEvaluationSystemTests.cs` | Expanded XML doc on `ThreatEvaluation_ZeroScoreEntry_IsRetained` referencing DESIGN.md §4.3 |
| DEBT-017 | `FDP.Toolkit.Navigation/NavigationActions.cs` | Fixed comment: "aligns struct to 4-byte boundary" → "struct total = 8 bytes (int + byte + 3 pad)" |

### NavigationConstants additions

| Item | File | Change |
|---|---|---|
| FleeReplanIntervalTicks | `FDP.Toolkit.Navigation/NavigationConstants.cs` | Added `public const int FleeReplanIntervalTicks = 30` |

### NavigationMode enum

| Item | File | Change |
|---|---|---|
| Direct mode | `FDP.Toolkit.CarKinem/Core/NavigationEnums.cs` | Added `Direct = 4` |
| CarKinematicsSystem | `FDP.Toolkit.CarKinem/Systems/CarKinematicsSystem.cs` | Explicit `case NavigationMode.Direct:` grouped with `None` |

### New Executor files

| File | Lines |
|---|---|
| `FDP.Toolkit.Navigation/Executors/MoveToExecutor.cs` | Implements OnEnter/Execute/OnExit; frustration guard via `Dictionary<int,int>` |
| `FDP.Toolkit.Navigation/Executors/FleeExecutor.cs` | Implements throttled replan; stale-threat guard every tick; FleeState in channel.State |
| `FDP.Toolkit.Navigation/Executors/FollowRoadGraphExecutor.cs` | Sets RoadGraph mode; success on HasArrived |
| `FDP.Toolkit.Navigation/Executors/FollowRouteExecutor.cs` | Sets CustomTrajectory mode; loop or success on HasArrived |

### New Test files

| File | Tests |
|---|---|
| `FDP.Toolkit.Navigation.Tests/NavigationTestWorldFactory.cs` | Factory with GlobalTime singleton |
| `FDP.Toolkit.Navigation.Tests/ExecutorTests/MoveToExecutorTests.cs` | 3 tests (DEBT-016 resolved) |
| `FDP.Toolkit.Navigation.Tests/ExecutorTests/FleeExecutorTests.cs` | 3 tests (DEBT-009 propagation) |
| `FDP.Toolkit.Navigation.Tests/ExecutorTests/FollowRoadGraphExecutorTests.cs` | 2 tests |
| `FDP.Toolkit.Navigation.Tests/ExecutorTests/FollowRouteExecutorTests.cs` | 2 tests |

---

## Success Criteria Checklist

- [x] **DEBT-015** — zero-score policy verified; test confirmed correct; XML doc added referencing DESIGN.md §4.3
- [x] **DEBT-016** — `MoveToExecutor` frustration test uses `NavigationConstants.FrustrationTickThreshold` (not literal 120)
- [x] **DEBT-017** — `FollowRouteParams` comment fixed
- [x] **BCS-P3-T2** — `MoveToExecutor`; 3 tests pass; frustration test references constant
- [x] **BCS-P3-T3** — `FleeExecutor`; 3 tests pass including dead-threat generational guard test
- [x] **BCS-P3-T4** — `FollowRoadGraphExecutor`; 2 tests pass
- [x] **BCS-P3-T5** — `FollowRouteExecutor`; 2 tests pass including loop-restart test
- [x] **`FleeReplanIntervalTicks = 30`** added to `NavigationConstants`; no raw `30` in executor
- [x] **No `VehicleState` reads** — zero occurrences in Navigation toolkit executors (all use SimTransform/SimVelocity)
- [x] **Full solution** — `dotnet build FDP.sln` zero C# errors; `dotnet test FDP.sln` all green
- [x] **Report submitted**
