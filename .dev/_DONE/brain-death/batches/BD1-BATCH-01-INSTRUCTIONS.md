# BD1-BATCH-01: Core Brain-Death Lifecycle

**Batch Number:** BD1-BATCH-01  
**Tasks:** BD1-P1T0a, BD1-P1T0b, BD1-P1T1, BD1-P1T2, BD1-P1T3  
**Phase:** Phase 1 — Core Brain-Death Lifecycle  
**Estimated Effort:** 10-12 hours  
**Priority:** HIGH  
**Dependencies:** None

---

## 📋 Onboarding & Workflow

### Developer Instructions
This batch addresses the core ECS lifecycle for behavior and channel cleanup. When a mission ends, is aborted, or is replaced, the entity must cleanly reach a "brain death" state (no active behavior, no stimulated channels). You will implement two new events to orchestrate this and fix the arbitration/mission systems to properly transition entities out of active cognitive states.

### Required Reading (IN ORDER)
1. **Workflow Guide:** `.dev-workstream/README.md` - How to work with batches
2. **Task Tracker:** `docs/brain-death/BD1-TASK-TRACKER.md` - Overall progress context
3. **Task Definitions:** `docs/brain-death/BD1-TASK-DETAIL.md` - See detailed specs for the tasks in this batch
4. **Design Document:** `docs/brain-death/BD1-DESIGN.md` - Read "Phase 1 - Core Brain-Death Lifecycle" (Lines 32-205)

### Source Code Location
- **Primary Work Areas:**
  - `FDP/Toolkits/FDP.Toolkit.Behavior/`
  - `Hrot.SimHost/`
- **Test Projects:**
  - `FDP/Toolkits/FDP.Toolkit.Behavior.Tests/` (or equivalent test project for Behavior Toolkit)
  - `Hrot.SimHost.Tests/` (or equivalent test project for SimHost)

### Report Submission
**When done, submit your report to:**  
`.dev-workstream/reports/BD1-BATCH-01-REPORT.md`

**If you have questions, create:**  
`.dev-workstream/questions/BD1-BATCH-01-QUESTIONS.md`

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: You MUST complete tasks in sequence with passing tests:**

1. **Task 1:** Implement → Write tests → **ALL tests pass** ✅
2. **Task 2:** Implement → Write tests → **ALL tests pass** ✅  
3. **Task 3:** Implement → Write tests → **ALL tests pass** ✅
4. **Task 4:** Implement → Write tests → **ALL tests pass** ✅
5. **Task 5:** Implement → Write tests → **ALL tests pass** ✅

**DO NOT** move to the next task until:
- ✅ Current task implementation complete
- ✅ Current task tests written
- ✅ **ALL tests passing** (including previous batch tests)

---

## Context

The core cognitive teardown flow is incomplete. We are introducing two explicit events: a bottom-up `BehaviorFinishedEvent` acting as a notification from the cognitive layer, and a top-down `ClearBehaviorEvent` acting as a mandatory imperative signal to halt operations. We will modify `BTreeTickSystem`, `BehaviorIngressSystem`, `ChannelArbitrationSystem`, `MissionDirectorSystem`, and `MissionControlRequestSystem` to guarantee `OnExit` teardown is executed on the muscle layer.

**Related Tasks:**
- **BD1-P1T0a:** `BehaviorFinishedEvent` — Bottom-Up Notification from BTreeTickSystem
- **BD1-P1T0b:** `ClearBehaviorEvent` — Top-Down Imperative via BehaviorIngressSystem
- **BD1-P1T1:** `ChannelArbitrationSystem` — OnExit Guarantee
- **BD1-P1T2:** `MissionDirectorSystem` — BehaviorFinished Trigger + End-of-Mission Clear
- **BD1-P1T3:** `MissionControlRequestSystem` — CMD_ABORT_ALL Behavior Clear

---

## 🎯 Batch Objectives
Ensure behavior is explicitly cleared to `BehaviorIds.None` when a mission ends or is aborted, and that `ChannelArbitrationSystem` always triggers `OnExit` so the muscle layer is cleanly shut down. 

---

## ✅ Tasks

### Task 1: BehaviorFinishedEvent — Bottom-Up Notification (BD1-P1T0a)

**Files:** 
- `FDP/Toolkits/FDP.Toolkit.Behavior/Events/BehaviorFinishedEvent.cs` (NEW FILE)
- `FDP/Toolkits/FDP.Toolkit.Behavior/Systems/BTreeTickSystem.cs`
**Task Definition:** See [BD1-TASK-DETAIL.md](docs/brain-death/BD1-TASK-DETAIL.md#bd1-p1t0a-behaviorfinishedevent--bottom-up-notification-from-btreeticksystem)
**Design Reference:** [BD1-DESIGN.md](docs/brain-death/BD1-DESIGN.md#10a-behaviorfinishedevent-notification-bottom-up)

**Description:**
Create the `BehaviorFinishedEvent` and fire it from `BTreeTickSystem` when the root evaluates to `NodeStatus.Success` or `NodeStatus.Failure`. Do NOT publish from `LocomotionDispatcherSystem`. Ensure it only publishes once per terminal transition.

**Tests Required:**
- ✅ `BehaviorRoot_Success_PublishesBehaviorFinishedEvent`
- ✅ `BehaviorRoot_Failure_PublishesBehaviorFinishedEvent`
- ✅ `BehaviorRoot_Running_DoesNotPublishEvent`
- ✅ `BehaviorRoot_Success_PublishedOnlyOnce`
- ✅ `BehaviorFinished_NotPublishedByLocomotionDispatcher`

### Task 2: ClearBehaviorEvent — Top-Down Imperative (BD1-P1T0b)

**Files:** 
- `FDP/Toolkits/FDP.Toolkit.Behavior/Events/ClearBehaviorEvent.cs` (NEW FILE)
- `FDP/Toolkits/FDP.Toolkit.Behavior/Systems/BehaviorIngressSystem.cs`
**Task Definition:** See [BD1-TASK-DETAIL.md](docs/brain-death/BD1-TASK-DETAIL.md#bd1-p1t0b-clearbehaviorevent--top-down-imperative-via-behavioringresssystem)
**Design Reference:** [BD1-DESIGN.md](docs/brain-death/BD1-DESIGN.md#10b-clearbehaviorevent-imperative-top-down)

**Description:**
Create the `ClearBehaviorEvent` class. Consume it in `BehaviorIngressSystem.OnUpdate`. It must clear `ActiveBehaviorHash`, increment `InstanceId`, set `BrainTier = 0`, and default `BrainBTreeState.State`.

**Tests Required:**
- ✅ `ClearBehaviorEvent_SetsBehaviorToNone`
- ✅ `ClearBehaviorEvent_NoBehaviorState_IsIgnored`
- ✅ `ClearBehaviorEvent_DoesNotAffectOtherEntities`
- ✅ `ClearVsAssign_AreIndependent`

### Task 3: ChannelArbitrationSystem — OnExit Guarantee (BD1-P1T1)

**File:** `FDP/Toolkits/FDP.Toolkit.Behavior/Systems/ChannelArbitrationSystem.cs`  
**Task Definition:** See [BD1-TASK-DETAIL.md](docs/brain-death/BD1-TASK-DETAIL.md#bd1-p1t1-channelarbitrationsystem--onexit-guarantee)
**Design Reference:** [BD1-DESIGN.md](docs/brain-death/BD1-DESIGN.md#11-channelarbitrationsystem--onexit-guarantee)

**Description:**
Fix preemption checks. Instead of `channel = default;`, you must set `channel.ActiveAction = 0;` and apply an unchecked increment to `channel.ActionInstanceId++;`. This must be applied across Locomotion, Weapon, and Interaction channels.

**Tests Required:**
- ✅ `ChannelClear_ShouldNotZeroActionInstanceId` (Locomotion)
- ✅ `NoPreemption_WhenBehaviorMatches`
- ✅ `WeaponChannel_ReceivesOnExitSignal`
- ✅ `InteractionChannel_ReceivesOnExitSignal`

### Task 4: MissionDirectorSystem — End-of-Mission Clear (BD1-P1T2)

**File:** `FDP/Toolkits/FDP.Toolkit.Behavior/Systems/MissionDirectorSystem.cs`  
**Task Definition:** See [BD1-TASK-DETAIL.md](docs/brain-death/BD1-TASK-DETAIL.md#bd1-p1t2-missiondirectorsystem--behaviorfinished-trigger--end-of-mission-clear)
**Design Reference:** [BD1-DESIGN.md](docs/brain-death/BD1-DESIGN.md#12-missiondirectorsystem--end-of-mission-behavior-clear)

**Description:**
Add `BehaviorFinished` trigger logic to consume `BehaviorFinishedEvent`. When the trigger fires and the plan is exhausted (`queue.CurrentPhase >= queue.PhaseCount`), publish `ClearBehaviorEvent`. Do NOT mutate `BehaviorState` directly here.

**Tests Required:**
- ✅ `BehaviorFinishedTrigger_AdvancesPhase`
- ✅ `BehaviorFinishedTrigger_MultiPhase_SetsNextBehavior`
- ✅ `BehaviorFinishedTrigger_WrongEntity_DoesNotFire`
- ✅ `MissionComplete_PublishesClearBehaviorEvent`
- ✅ `MissionComplete_ViaBehaviorIngress_SetsBehaviorToNone`

### Task 5: MissionControlRequestSystem — CMD_ABORT_ALL Behavior Clear (BD1-P1T3)

**File:** `Hrot.SimHost/Systems/MissionControlRequestSystem.cs`  
**Task Definition:** See [BD1-TASK-DETAIL.md](docs/brain-death/BD1-TASK-DETAIL.md#bd1-p1t3-missioncontrolrequestsystem--cmd_abort_all-behavior-clear)
**Design Reference:** [BD1-DESIGN.md](docs/brain-death/BD1-DESIGN.md#13-missioncontrolrequestsystem--cmd_abort_all-behavior-clear)

**Description:**
When a `CMD_ABORT_ALL` request is processed, after zeroing the `MissionPlanQueue`, publish a `ClearBehaviorEvent`. Ensure the existing ACK writing still happens.

**Tests Required:**
- ✅ `AbortAll_PublishesClearBehaviorEvent`
- ✅ `AbortAll_NoBehaviorState_DoesNotThrow`
- ✅ `AbortAll_WritesSuccessAck`

---

## ⚠️ Quality Standards

**❗ TEST QUALITY EXPECTATIONS**
- **NOT ACCEPTABLE:** Tests that only verify "event exists" or properties on mocks.
- **REQUIRED:** Tests that verify actual simulated system pipeline behavior and edge cases. Make sure to check the *exact rules* laid out under each task definition.
- **REQUIRED:** You must run tests locally. Verify tests can detect broken behavior (e.g. failing to increment `InstanceId` causes a test to fail).

**❗ REPORT QUALITY EXPECTATIONS**
- **REQUIRED:** Document issues encountered and how you resolved them.
- **REQUIRED:** Document design decisions YOU made beyond the spec.
- **REQUIRED:** Share insights on code quality and improvement opportunities.
- **REQUIRED:** Note any edge cases or scenarios discovered during implementation.

---

## 📊 Report Requirements

In your `.dev-workstream/reports/BD1-BATCH-01-REPORT.md`, please provide insights on the implementation. Answer the following:

**Developer Insights**

**Q1:** What issues did you encounter during implementation (e.g., regarding the event bus consumption timing or test construction)? How did you resolve them?

**Q2:** Did you spot any weak points or tightly-coupled areas in the existing behavior codebase? What would you improve?

**Q3:** What design decisions did you make beyond the instructions? What alternatives did you consider?

**Q4:** What edge cases did you discover regarding the zero-allocation `ClearBehaviorEvent` publish that weren't mentioned in the spec?

**Q5:** Are there any observed performance concerns or allocation issues you noticed during the BTreeTickSystem modifications?

---

## 🎯 Success Criteria

This batch is DONE when:
- [ ] Task BD1-P1T0a completed (Tests passed)
- [ ] Task BD1-P1T0b completed (Tests passed)
- [ ] Task BD1-P1T1 completed (Tests passed)
- [ ] Task BD1-P1T2 completed (Tests passed)
- [ ] Task BD1-P1T3 completed (Tests passed)
- [ ] ALL tests in `FDP.Toolkit.Behavior.Tests` and `Hrot.SimHost.Tests` passing.
- [ ] Insights Report submitted to `.dev-workstream/reports/BD1-BATCH-01-REPORT.md`

---

## 📚 Reference Materials
- **Task Defs:** `docs/brain-death/BD1-TASK-DETAIL.md`
- **Design:** `docs/brain-death/BD1-DESIGN.md`
