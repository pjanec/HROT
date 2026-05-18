# BATCH-07: Phase 3 Navigation Executors (BCS-P3-T2 through T5)

**Batch Number:** BATCH-07  
**Tasks:** CORRECTIVE (DEBT-015 policy verify, DEBT-017 comment fix), BCS-P3-T2, BCS-P3-T3, BCS-P3-T4, BCS-P3-T5  
**Phase:** Phase 3 — FDP.Toolkit.Navigation (completing all executors)  
**Estimated Effort:** 10–14 hours  
**Priority:** HIGH  
**Dependencies:** BATCH-06 ✅ (BCS-P3-T1, all P1 correctives done)

---

## 📋 Onboarding & Workflow

### Developer Instructions

Two parts:

1. **Corrective (30 min):** Check DESIGN.md §4.3 for the zero-score retention policy (DEBT-015), correct the test if needed. Fix the misleading comment in `FollowRouteParams` (DEBT-017, 2-line change).

2. **Navigation Executors (10–13 h):** Implement four `IActionExecutor<LocomotionChannel>` classes that translate `LocomotionChannel` intents into `CarKinem.NavState` configurations. Each executor follows the same lifecycle contract.

### Executor Lifecycle Contract (READ CAREFULLY)

```
OnEnter(entity, ref channel, world):
    Read channel.Params (cast via Unsafe.As or MemoryMarshal) to extract parameters.
    Write NavState fields: Mode, FinalDestination, ArrivalRadius, TargetSpeed.
    channel.Status = NodeStatus.Running; // mandatory

Execute(entity, ref channel, world, dt):
    Guard: view.IsAlive(threat) for any stored Entity references.
    Read NavState to check HasArrived / progress.
    Frustration logic (MoveToExecutor only): count stuck ticks.
    Write channel.Status = NodeStatus.Success or Failure when done.
    Otherwise: leave Status = Running (do not re-write needlessly).

OnExit(entity, ref channel, world):
    INVARIANT: channel still holds OUTGOING action's IDs when OnExit is called.
    Cleanup: set NavState.TargetSpeed = 0, NavState.Mode = None to stop the vehicle.
```

This contract is documented in `IActionExecutor.cs` and `DispatcherSystemBase.cs`. Re-read both before writing any executor.

### Required Reading (IN ORDER)

1. **BATCH-06 Review:** `.dev-workstream/reviews/BATCH-06-REVIEW.md`
2. **DEBT-TRACKER.md:** DEBT-015, DEBT-016, DEBT-017
3. **CODE-STANDARDS.md:** `.dev-workstream/guides/CODE-STANDARDS.md` — §0 test quality + all rules
4. **Task Details BCS-P3-T2 through T5:** `FDP/Docs/projects/behavior-control/TASK-DETAIL.md` — lines 665–748
5. **IActionExecutor contract:** `FDP/Toolkits/FDP.Toolkit.Behavior/Executors/IActionExecutor.cs`
6. **DispatcherSystemBase (OnExit invariant):** `FDP/Toolkits/FDP.Toolkit.Behavior/Systems/DispatcherSystemBase.cs`
7. **NavState definition:** `FDP/Toolkits/FDP.Toolkit.CarKinem/Components/NavState.cs` (or equivalent) — fields: `Mode`, `FinalDestination (Vector2)`, `ArrivalRadius`, `TargetSpeed`, `HasArrived`, `TrajectoryId`, `TargetNodeId`
8. **SimComponents:** `FDP/Kernel/Fdp.Kernel/CoreComponents/SimComponents.cs` — `SimTransform`, `SimVelocity`

### Source Locations

| Area | Path |
|---|---|
| **Corrective** — threat eval test | `FDP/Toolkits/FDP.Toolkit.Perception.Tests/ThreatEvaluationSystemTests.cs` |
| **Corrective** — comment fix | `FDP/Toolkits/FDP.Toolkit.Navigation/NavigationActions.cs` |
| **New executors** | `FDP/Toolkits/FDP.Toolkit.Navigation/Executors/` ← create dir |
| **New executor tests** | `FDP/Toolkits/FDP.Toolkit.Navigation.Tests/ExecutorTests/` ← create dir |
| **NavigationTestWorldFactory** | create in Navigation.Tests if needed |
| NavigationConstants | `FDP/Toolkits/FDP.Toolkit.Navigation/NavigationConstants.cs` |
| NavigationActions (param structs) | `FDP/Toolkits/FDP.Toolkit.Navigation/NavigationActions.cs` |

### Build & Test Commands

```powershell
cd D:\Work\IOS-IG-SimHost-FDP\FDP
dotnet build FDP.sln
dotnet test FDP.sln
dotnet test Toolkits/FDP.Toolkit.Navigation.Tests/
dotnet test Toolkits/FDP.Toolkit.Perception.Tests/   # must remain green after corrective
```

### Report Submission

`.dev-workstream/reports/BATCH-07-REPORT.md`

---

## 🔄 MANDATORY WORKFLOW

1. **Corrective — DEBT-015:** Consult DESIGN.md §4.3 → correct test or confirm policy ✅
2. **Corrective — DEBT-017:** Fix misleading comment in `FollowRouteParams` ✅
3. **BCS-P3-T2:** `MoveToExecutor` + 3 tests ✅
4. **BCS-P3-T3:** `FleeExecutor` + 3 tests ✅  
5. **BCS-P3-T4:** `FollowRoadGraphExecutor` + 1 test ✅
6. **BCS-P3-T5:** `FollowRouteExecutor` + 2 tests ✅
7. Full solution green ✅

---

## ✅ Tasks

### Task 0a (Corrective): Verify zero-score retention policy (DEBT-015)

**Step 1:** Read `DESIGN.md §4.3` (ThreatEvaluationSystem section). Find the policy for zero-score entries within `TargetMemory`:
- If design says **retain** (entries stay until evicted by higher-threat newcomers): `ThreatEvaluation_ZeroScoreEntry_IsRetained` is correct. Add a one-line XML comment referencing the design paragraph, then mark DEBT-015 resolved.
- If design says **evict** (zero-score entries are removed): the test name and assertion are wrong. Rename the test to `ThreatEvaluation_ZeroScoreEntry_IsEvicted`, invert the assertion (`Assert.Equal(0, resultMem.Count)`), and update `ThreatEvaluationSystem` to implement the eviction. Also update `AddOrUpdateTarget`'s XML doc.

Either way: the policy must be documented in code and match the design. No "I'm not sure" — check the doc.

### Task 0b (Corrective): Fix misleading `FollowRouteParams` comment (DEBT-017)

**File:** `FDP/Toolkits/FDP.Toolkit.Navigation/NavigationActions.cs`

Change:
```csharp
// 3 bytes of implicit Sequential padding (aligns struct to 4-byte boundary).
```
To:
```csharp
// 3 bytes of implicit Sequential padding; struct total = 8 bytes (int + byte + 3 pad).
```

---

### Task 1: MoveToExecutor (BCS-P3-T2)

**File:** `FDP/Toolkits/FDP.Toolkit.Navigation/Executors/MoveToExecutor.cs`  
**Task Definition:** [TASK-DETAIL.md §BCS-P3-T2](../../../FDP/Docs/projects/behavior-control/TASK-DETAIL.md#bcs-p3-t2--movetoexecutor) — lines 665–686

`MoveToExecutor` implements `IActionExecutor<LocomotionChannel>`.

**`OnEnter`:**
- Read `MoveToParams` out of `channel` payload (`Unsafe.As` or `MemoryMarshal.Read`).
- Write `NavState`: `Mode = NavigationMode.Direct`, `FinalDestination = params.Destination`, `ArrivalRadius = params.ArrivalRadius`, `TargetSpeed = params.Speed`.
- Set `channel.Status = NodeStatus.Running`.
- Reset frustration counter (store as a private field indexed by `entity.Index`, or via a component — see note below).

**`Execute`:**
- Read `NavState.HasArrived`. If `true` → `channel.Status = NodeStatus.Success` and return.
- Frustration guard: read `SimVelocity.Linear.Length()`. If below `NavigationConstants.FrustrationSpeedThreshold` AND distance to destination > `ArrivalRadius * 2`: increment stuck counter. If counter exceeds `NavigationConstants.FrustrationTickThreshold` → `channel.Status = NodeStatus.Failure`.
- Distance: `Vector2.Distance(new Vector2(tf.Position.X, tf.Position.Y), params.Destination)` — use `SimTransform`, not `VehicleState`.

**`OnExit`:**
- Set `NavState.TargetSpeed = 0f`, `NavState.Mode = NavigationMode.None`.

**Frustration counter storage note:** Since `IActionExecutor` is a class instance shared across entities, the per-entity counter cannot be a simple field. Use a `Dictionary<int, int>` keyed by `entity.Index`, cleared in `OnExit`. This is the main-thread executor; dictionary allocation happens once at startup. Alternative: use `FleeState` equivalent. Whichever approach you choose, document it with a comment.

**Phase 0 adaptation (mandatory):** Any distance computation that appears to use `VehicleState.Position` in the design talk (lines 2000–2050) must use `world.GetComponent<SimTransform>(entity).Position.XY` projected to `Vector2`. No `VehicleState` reads anywhere.

**Tests** (new file `ExecutorTests/MoveToExecutorTests.cs`):

```csharp
[Fact]
void MoveToExecutor_ReportsSuccess_WhenNavStateHasArrived()
// Setup: NavState.HasArrived = 1 (or true)
// OnEnter + Execute
// Assert: channel.Status == NodeStatus.Success

[Fact]
void MoveToExecutor_ReportsFailure_WhenFrustrationThresholdExceeded()
// Setup: SimVelocity.Linear = Vector3.Zero (stuck)
//        Distance to destination >> ArrivalRadius * 2
// Run Execute for NavigationConstants.FrustrationTickThreshold + 1 ticks
// Assert: channel.Status == NodeStatus.Failure
// IMPORTANT: assertion must use NavigationConstants.FrustrationTickThreshold, not "121"
// This resolves DEBT-016.

[Fact]
void MoveToExecutor_OnExit_SetsNavStateSpeedToZero()
// OnEnter then OnExit
// Assert: NavState.TargetSpeed == 0f
// Assert: NavState.Mode == NavigationMode.None
```

---

### Task 2: FleeExecutor (BCS-P3-T3)

**File:** `FDP/Toolkits/FDP.Toolkit.Navigation/Executors/FleeExecutor.cs`  
**Task Definition:** [TASK-DETAIL.md §BCS-P3-T3](../../../FDP/Docs/projects/behavior-control/TASK-DETAIL.md#bcs-p3-t3--fleeexecutor) — lines 690–714

`FleeExecutor` implements `IActionExecutor<LocomotionChannel>`.

**`OnEnter`:**
- Read `FleeParams` from channel payload.
- Set `channel.Status = NodeStatus.Running`.
- Compute initial flee destination and write to `NavState`.

**`Execute`:**
1. **Stale threat guard (MANDATORY, every tick):**
   ```csharp
   if (!world.IsAlive(params.Threat))
   {
       channel.Status = NodeStatus.Success; // threat eliminated
       return;
   }
   ```
2. **Safe-distance check:** if `Vector2.Distance(myPos, threatPos) > params.SafeDistance` → `channel.Status = NodeStatus.Success`.
3. **Throttled replan (every 30 ticks):** read `FleeState.NextReplanTick`. If `currentTick >= NextReplanTick`:
   - Compute away vector: `Vector2.Normalize(myPos - threatPos)`.
   - Set `NavState.FinalDestination = myPos + awayVector * params.SafeDistance`.
   - `NextReplanTick = currentTick + 30`.
   - Store `FleeState` back.

**Phase 0 adaptation:**
```csharp
Vector2 myPos     = new Vector2(world.GetComponent<SimTransform>(entity).Position.X,
                                world.GetComponent<SimTransform>(entity).Position.Y);
Vector2 threatPos = new Vector2(world.GetComponent<SimTransform>(params.Threat).Position.X,
                                world.GetComponent<SimTransform>(params.Threat).Position.Y);
```
Never use `VehicleState.Position`.

**`FleeState` storage:** Store in the channel's `StateSlot` if `LocomotionChannel` has one, OR keep a `Dictionary<int, FleeState>` in the executor class. Document the choice.

**`OnExit`:** `NavState.TargetSpeed = 0f`, `NavState.Mode = NavigationMode.None`.

**Tests** (new file `ExecutorTests/FleeExecutorTests.cs`):

```csharp
[Fact]
void FleeExecutor_ReportsSuccess_WhenSafeDistanceReached()
// Place self beyond params.SafeDistance from threat
// Execute → channel.Status == NodeStatus.Success

[Fact]
void FleeExecutor_ReportsSuccess_WhenThreatEntityIsDead()
// Destroy the threat entity (world.DestroyEntity(threat))
// Execute → channel.Status == NodeStatus.Success (generational check triggered)
// This is the CRITICAL test — verifies DEBT-009 fix propogated to live executor code

[Fact]
void FleeExecutor_RecalculatesFleeVector_AfterThrottlePeriod()
// Run Execute for 31 ticks
// Assert: NavState.FinalDestination was updated at tick 1 (initial) AND at tick 31 (replan)
// I.e., FinalDestination at tick 32 != FinalDestination at tick 2
// Verify via two world.GetComponent<NavState>(entity).FinalDestination snapshots
```

---

### Task 3: FollowRoadGraphExecutor (BCS-P3-T4)

**File:** `FDP/Toolkits/FDP.Toolkit.Navigation/Executors/FollowRoadGraphExecutor.cs`  
**Task Definition:** [TASK-DETAIL.md §BCS-P3-T4](../../../FDP/Docs/projects/behavior-control/TASK-DETAIL.md#bcs-p3-t4--followroadgraphexecutor) — lines 718–732

`FollowRoadGraphExecutor` implements `IActionExecutor<LocomotionChannel>`.

**`OnEnter`:** Read `FollowRoadGraphParams`. Set `NavState.Mode = NavigationMode.RoadGraph`, `NavState.TargetNodeId = params.TargetNodeId`, `NavState.TargetSpeed = params.Speed`. `channel.Status = NodeStatus.Running`.

**`Execute`:** If `NavState.HasArrived` → `channel.Status = NodeStatus.Success`.

**`OnExit`:** `NavState.TargetSpeed = 0f`, `NavState.Mode = NavigationMode.None`.

**Tests** (new file `ExecutorTests/FollowRoadGraphExecutorTests.cs`):

```csharp
[Fact]
void FollowRoadGraphExecutor_SetsRoadGraphMode_OnEnter()
// OnEnter
// Assert: NavState.Mode == NavigationMode.RoadGraph
// Assert: NavState.TargetNodeId == params.TargetNodeId
// Assert: NavState.TargetSpeed == params.Speed
// Assert: channel.Status == NodeStatus.Running

[Fact]
void FollowRoadGraphExecutor_ReportsSuccess_WhenHasArrived()
// OnEnter, then set NavState.HasArrived = 1, then Execute
// Assert: channel.Status == NodeStatus.Success
```

---

### Task 4: FollowRouteExecutor (BCS-P3-T5)

**File:** `FDP/Toolkits/FDP.Toolkit.Navigation/Executors/FollowRouteExecutor.cs`  
**Task Definition:** [TASK-DETAIL.md §BCS-P3-T5](../../../FDP/Docs/projects/behavior-control/TASK-DETAIL.md#bcs-p3-t5--followrouteexecutor) — lines 735–748

`FollowRouteExecutor` implements `IActionExecutor<LocomotionChannel>`.

**`OnEnter`:** Read `FollowRouteParams`. Set `NavState.Mode = NavigationMode.CustomTrajectory`, `NavState.TrajectoryId = params.TrajectoryId`. `channel.Status = NodeStatus.Running`.

**`Execute`:**
- If `NavState.HasArrived`:
  - `params.IsLooped != 0` → reset route (re-write `TrajectoryId`, `Status = Running` stays).
  - `params.IsLooped == 0` → `channel.Status = NodeStatus.Success`.

**`OnExit`:** `NavState.Mode = NavigationMode.None`.

**Tests** (new file `ExecutorTests/FollowRouteExecutorTests.cs`):

```csharp
[Fact]
void FollowRouteExecutor_ReportsSuccess_WhenRouteCompleteAndNotLooped()
// OnEnter (IsLooped=0), set NavState.HasArrived=1, Execute
// Assert: channel.Status == NodeStatus.Success

[Fact]
void FollowRouteExecutor_LoopsRoute_WhenFlagSet()
// OnEnter (IsLooped=1), set NavState.HasArrived=1, Execute once
// Assert: channel.Status == NodeStatus.Running (not Success — looped back)
// Assert: NavState.TrajectoryId is still the original trajectory ID (route re-armed)
```

---

## 🧪 Testing Requirements

- **Minimum 11 new tests:** 3 corrective policy tests (0 or 1 depending on policy direction) + 3 MoveToExecutor + 3 FleeExecutor + 2 FollowRoadGraphExecutor + 2 FollowRouteExecutor = **11**.
- Every test that calls `Execute` must set up all required components (`SimTransform`, `SimVelocity`, `NavState`) — no exceptions.
- The `FleeExecutor_ReportsSuccess_WhenThreatEntityIsDead` test is **mandatory** — it is the end-to-end proof that DEBT-009's generational safety fix propagates through to live executor behaviour.
- The `MoveToExecutor_ReportsFailure_WhenFrustrationThresholdExceeded` test **must** reference `NavigationConstants.FrustrationTickThreshold` in its loop count and assertion — this resolves DEBT-016 and prevents the constant from drifting away from reality.
- All existing tests must remain green.

### NavigationTestWorldFactory

Create `FDP/Toolkits/FDP.Toolkit.Navigation.Tests/NavigationTestWorldFactory.cs`:

```csharp
public static class NavigationTestWorldFactory
{
    public static EntityRepository Create()
    {
        var world = EntityRepository.Create();
        // Register all components used by navigation executors:
        world.RegisterComponent<SimTransform>();
        world.RegisterComponent<SimVelocity>();
        world.RegisterComponent<NavState>();
        world.RegisterComponent<LocomotionChannel>();
        return world;
    }
}
```

---

## ⚠️ Quality Standards

See `.dev-workstream/guides/CODE-STANDARDS.md`.

**❗ No `VehicleState` reads in any executor** — always `SimTransform` for position, `SimVelocity` for speed.

**❗ `FleeExecutor.Execute` checks `world.IsAlive(params.Threat)` EVERY tick** — not just on entry. This is the generational safety guard that DEBT-009 enables. A test verifies it.

**❗ `NavigationConstants.FrustrationTickThreshold` used in the test loop** — not `120`. Resolves DEBT-016.

**❗ Frustration counter storage explicitly documented** — whatever approach is chosen (dictionary vs. state component), leave a comment explaining why.

**❗ No magic numbers in executor code** — all time constants, thresholds, speed values come from `NavigationConstants`. The only exception is the throttle period of 30 ticks in `FleeExecutor` — add `public const int FleeReplanIntervalTicks = 30` to `NavigationConstants`.

---

## 📊 Report Requirements

Submit `.dev-workstream/reports/BATCH-07-REPORT.md`:

- **Test results:** `dotnet test FDP.sln` full summary.
- **Q1 (DEBT-015):** What did DESIGN.md §4.3 say about zero-score retention? What did you do — confirm the current test, or invert it? Quote the relevant design text.
- **Q2 (MoveToExecutor frustration counter):** Which storage approach did you use — `Dictionary<int, int>` or a state component? What are the trade-offs and why did you choose this approach?  
- **Q3 (FleeExecutor Entity guard):** Walk through the `FleeExecutor_ReportsSuccess_WhenThreatEntityIsDead` test step by step: how is the threat entity destroyed in the test, how does `world.IsAlive` detect it, and what is the generation value of the stored `Entity` vs. the live world state at the moment of the check?
- **Q4:** Is there any scenario where a navigator executor could double-write `NavState` in the same frame (once in `OnEnter` and once in `Execute` during the first tick)? Is this a correctness problem, and how is it handled?

---

## 🎯 Success Criteria

- [ ] **DEBT-015** — zero-score policy verified against DESIGN.md; test corrected or confirmed; XML doc added
- [ ] **DEBT-016** — `MoveToExecutor` frustration test uses `NavigationConstants.FrustrationTickThreshold`
- [ ] **DEBT-017** — `FollowRouteParams` comment fixed
- [ ] **BCS-P3-T2** — `MoveToExecutor`; 3 tests pass; frustration test references constant
- [ ] **BCS-P3-T3** — `FleeExecutor`; 3 tests pass including dead-threat generational guard test
- [ ] **BCS-P3-T4** — `FollowRoadGraphExecutor`; 2 tests pass
- [ ] **BCS-P3-T5** — `FollowRouteExecutor`; 2 tests pass including loop-restart test
- [ ] **`FleeReplanIntervalTicks = 30`** added to `NavigationConstants`; no raw `30` in executor
- [ ] **No `VehicleState` reads** — grep confirms zero occurrences in Navigation toolkit
- [ ] **Full solution** — `dotnet build FDP.sln` zero errors; `dotnet test FDP.sln` all green
- [ ] **Report submitted**

---

## 📚 Reference Materials

- **BATCH-06 Review:** `.dev-workstream/reviews/BATCH-06-REVIEW.md`
- **DEBT-TRACKER.md:** DEBT-015, 016, 017
- **CODE-STANDARDS.md:** `.dev-workstream/guides/CODE-STANDARDS.md`
- **Task Details BCS-P3-T2–T5:** `FDP/Docs/projects/behavior-control/TASK-DETAIL.md` — lines 665–748
- **IActionExecutor.cs:** `FDP/Toolkits/FDP.Toolkit.Behavior/Executors/IActionExecutor.cs`
- **DispatcherSystemBase.cs:** `FDP/Toolkits/FDP.Toolkit.Behavior/Systems/DispatcherSystemBase.cs`
- **NavigationConstants.cs:** `FDP/Toolkits/FDP.Toolkit.Navigation/NavigationConstants.cs`
- **NavigationActions.cs:** `FDP/Toolkits/FDP.Toolkit.Navigation/NavigationActions.cs`
- **SimComponents.cs:** `FDP/Kernel/Fdp.Kernel/CoreComponents/SimComponents.cs`
