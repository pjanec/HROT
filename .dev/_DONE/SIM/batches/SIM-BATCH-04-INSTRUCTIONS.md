# SIM-BATCH-04: Implement MissionAdapterSystem & JoinFormationExecutor (Phase S4.3 & S4.4)

**Batch Number:** SIM-BATCH-04  
**Tasks:** TASK-S4.3, TASK-S4.4  
**Phase:** S4  
**Estimated Effort:** 18 hours (2.25 days)  
**Priority:** HIGH  
**Dependencies:** S4.2 (EntityMission Translators)

---

## 📋 Onboarding & Workflow

### Developer Instructions
Welcome back! In SIM-BATCH-02 you stubbed out the `MissionAdapterSystem`. Now that the network layer translates `EntityMissionHolder` properly, it's time to build the adapter logic.

### Required Reading (IN ORDER)
1. **Workflow Guide:** `.dev-workstream/README.md`
2. **Task Definitions:** `docs/design/TASK-DETAILS-SIMHOST.md#task-s43-implement-missionadaptersystem`
3. **Design Document:** `docs/design/DESIGN-SIMHOST.md#44-missionadaptersystem` (CRITICAL for task logic flow)

### Source Code Location
- **Primary Work Area:** `Hrot.SimHost/Systems/MissionAdapterSystem.cs`, `Hrot.SimHost/Systems/JoinFormationExecutor.cs`
- **Test Project:** `Hrot.SimHost.Tests/`

### Report Submission
**When done, submit your report to:**  
`.dev-workstream/reports/SIM-BATCH-04-REPORT.md`

**If you have questions, create:**  
`.dev-workstream/questions/SIM-BATCH-04-QUESTIONS.md`

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: You MUST complete tasks in sequence with passing tests:**

1. **Task 1:** Implement → Write tests → **ALL tests pass** ✅
2. **Task 2:** Implement → Write tests → **ALL tests pass** ✅

**DO NOT** move to the next task until:
- ✅ Current task implementation complete
- ✅ Current task tests written
- ✅ **ALL tests passing** (including previous batch tests)

---

## Context

`MissionAdapterSystem` translates the string-based abstract AI behaviour commands (`MoveToLocation`, `Formation`, etc.) inside the `EntityMission` task list into concrete `BehaviorId` triggers that the toolkit's `BrainBTreeState` actually processes natively. It is the bridge between the abstracted high-level node command from IOS, to low level C# fast tree nodes. 
In addition to translating, it updates the `LocomotionChannel.Status` and triggers task completion when nodes return `Success` or `Failure`.

Furthermore, for the `JoinFormation` behavior specifically, the `JoinFormationExecutor` acts as the low-level action executor linking the behavior to the actual `VehicleAPI` vehicle physics and formation logic.

---

## 🎯 Batch Objectives
- Fill out the `MissionAdapterSystem.cs` stub.
- Filter on `EntityMission`, `BehaviorState`, and `BrainBlackboard`.
- Correctly parse `BehaviorParams` using the matching behavior `ParseParams` via `BehaviorDefinition`.
- Track output status and update task index upon completion.
- Fill out the `JoinFormationExecutor.cs` stub.
- Register `JoinFormationExecutor` properly by uncommenting its registration inside `SimulationLogicModule.cs` from BATCH-02.

---

## ✅ Tasks

### Task 1: Implement MissionAdapterSystem (TASK-S4.3)

**File:** `Hrot.SimHost/Systems/MissionAdapterSystem.cs`

**Task Definition:** See [TASK-DETAILS-SIMHOST.md](../../docs/design/TASK-DETAILS-SIMHOST.md#task-s43-implement-missionadaptersystem)

**Description:**
Map the active `MissionTask.BehaviorId` string to a `BehaviorId`, and process its state.

**Requirements:**
Implementation must follow these exact steps (from design spec):
1. Query entities with `EntityMissionHolder` (Managed), `BehaviorState`, and `BrainBlackboard`. *Note: we use `EntityMissionHolder` since S4.2*.
2. Extract the current underlying active task matching `ActiveTaskId`. If `BehaviorRegistry.TryGetId(task.BehaviorId, out int id)` fails — log a warning and return.
3. Check `BehaviorState.ActiveBehaviorHash`. If it doesn't match `id`:
   - Set the `ActiveBehaviorHash` directly to `id`.
   - Call `BehaviorDefinition.ParseParams(task.BehaviorParams, ref blackboard)`. (Note: use `ActionId` mappings accordingly!).
4. Read the `LocomotionChannel.Status` state:
   - On `NodeStatus.Success` → Execute `AdvanceToNextTask()` 
   - On `NodeStatus.Failure` → Execute `MarkTaskFailed()`
5. Inside the helper `AdvanceToNextTask()`:
   - Change current task `State` to `TASK_DONE`.
   - Select next valid task from list and set `ActiveTaskId`.
   - If there is no next task (mission complete), safely remove the `EntityMissionHolder` component.

**Tests Required:**
- ✅ `MissionAdapter_ResolvesBehaviorId()`: Test mapping `BehaviorId` to correct BT hash correctly.
- ✅ `MissionAdapter_AdvancesTaskOnSuccess()`: Test node changing state automatically from Success -> Next Task pointer.
- ✅ `MissionAdapter_MarksFailedOnChannelFailure()`: Test task failure handling.
- ✅ Unknown strings warn safely.

---

### Task 2: Implement JoinFormationExecutor (TASK-S4.4)

**File:** `Hrot.SimHost/Systems/JoinFormationExecutor.cs`

**Task Definition:** See [TASK-DETAILS-SIMHOST.md](../../docs/design/TASK-DETAILS-SIMHOST.md#task-s44-implement-joinformationexecutor)

**Description:**
Implement the action executor for the JoinFormation behavior.

**Requirements:**
1. In `OnEnter`: Read params from `BrainBlackboard`, look up the leader via `NetworkEntityMap`. Call `VehicleAPI.CreateFormation()` and set `Status = Running`. If leader not found, set `Status = NodeStatus.Failure`. Use `JoinFormationParams`.
2. In `Execute`: Check for `InFormationTag` presence directly on entity -> report `NodeStatus.Success`.
3. In `OnExit`: No cleanup needed.
4. **Important**: Go to `SimulationLogicModule.cs` from BATCH-02 and UNCOMMENT the `locoDispatcher.RegisterExecutor(NavigationConstants.ActionIdJoinFormation, new JoinFormationExecutor(_vehicleAPI, _entityMap));` line, and verify `NavigationConstants.ActionIdJoinFormation` is used.

**Tests Required:**
- ✅ `JoinFormation_LeaderFound_SetsRunning()`: Test leader resolve success and API call.
- ✅ `JoinFormation_LeaderNotFound_SetsFailure()`: Test leader resolve failure.
- ✅ `JoinFormation_Execute_SuccessOnFormationTag()`: Test success status when tag present.

---

## 🧪 Testing Requirements
Set up tests requiring an `EntityRepository` loaded with an `EntityMissionHolder`. Update the `LocomotionChannel` directly through code simulation, and verify the resulting component state on the next run. Ensure tests specifically test for dirty writing states.
For `JoinFormationExecutor`, create mock entities acting as the leader to test `OnEnter` and `Execute` cleanly without heavy dependencies.

---

## 📊 Report Requirements

**Q1 Behavior Definition Access:** How did you find retrieving `BehaviorDefinition` from the integer Registry Hash? Were any methods missing from the toolkit?
- **Q2 Component Read/Write:** Modifying nested items inside the `EntityMissionHolder.Mission.Plan.Tasks` list causes problems with C# mutating returned struct properties by value vs reference. How did you structure your writes to ensure it synchronized safely inside the ECS layer?
- **Q3 Unknown Behaviors:** Could we mitigate unknown strings by mapping them to an 'Idle' command safely instead of logging warnings forever? What do you think about the error output spam?

---

## 🎯 Success Criteria

This batch is DONE when:
- [ ] TASK-S4.3 and TASK-S4.4 completed.
- [ ] `MissionAdapterSystem` acts as the translation logic adapter linking arbitrary IOS behaviors to registered ECS BTree implementations. 
- [ ] `JoinFormationExecutor` acts as the low-level locomotion action executor.
- [ ] Tests explicitly test state changes propagating to tasks.
- [ ] Report submitted via markdown file.

---

## ⚠️ Common Pitfalls to Avoid
- Since `EntityMissionHolder` contains `Mission` as a managed object with lists, ensure that modifications actually happen directly on the list instance. Then, make sure you push it back using `cmd.SetManagedComponent()` to ensure the ECS framework recognizes the reference state was structurally modified so the `Changed()` flag triggers for Egress logic downstream!

---

## 📚 Reference Materials
- **Task Defs:** [TASK-DETAILS-SIMHOST.md](../../docs/design/TASK-DETAILS-SIMHOST.md) - See TASK-S4.3 and S4.4
- **Design:** `docs/design/DESIGN-SIMHOST.md#44-missionadaptersystem`
