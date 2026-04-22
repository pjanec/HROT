# BATCH-01: Core ECS Correctness Fixes

**Batch Number:** BATCH-01
**Tasks:** TASK-S301, TASK-S302, TASK-S303, TASK-S304
**Phase:** Phase 1 — Core ECS Correctness
**Estimated Effort:** 3-5 hours
**Priority:** HIGH
**Dependencies:** None — all Phase 1 tasks are independent of other phases.

---

## Onboarding & Workflow

### Developer Instructions

This batch fixes four independent bugs that together cause authored missions to be silently lost
when saving a scenario in the HROT Editor.  Each fix is small and surgical.  Read the design
docs before touching any code.

### Required Reading (IN ORDER)
1. **Workflow Guide:** `FDP\.dev-workstream\guides\DEV-GUIDE.md`
2. **Design Document:** `.dev\cgf-scn-3\DESIGN.md` — read Phase 1 (§1.1–§1.4) and the Decision Log
3. **Task Definitions:** `.dev\cgf-scn-3\TASK-DETAIL.md` — TASK-S301, TASK-S302, TASK-S303, TASK-S304
4. **Onboarding:** `.dev\cgf-scn-3\ONBOARDING.md` — folder layout and build commands

### Source Code Locations
- `Hrot\Engine\Hrot.Common\Systems\MissionControlExecutionSystem.cs` — S301, S302
- `FDP\Toolkits\Fdp.Toolkits\Behavior\Components\BehaviorComponents.cs` — S303
- `FDP\Toolkits\Fdp.Toolkits\Time\Controllers\SteppingTimeController.cs` — S304

### Test Projects
- `Hrot\Subsystems\Hrot.SimHost.Tests\Hrot.SimHost.Tests.csproj` — existing tests for S301/S302
- `FDP\Toolkits\Fdp.Toolkits.Tests\Fdp.Toolkits.Tests.csproj` — for S303, S304

### Report Submission
Submit your report to: `.dev\cgf-scn-3\reports\BATCH-01-REPORT.md`

If you have blocking questions, create: `.dev\cgf-scn-3\questions\BATCH-01-QUESTIONS.md`

---

## Context

`MissionControlExecutionSystem` processes `MissionControlIntent` events and writes two ECS
components: `MissionPlanQueue` (unmanaged struct) and `ActiveMissionPlan` (managed class).  The
system has two independent bugs that cause `ActiveMissionPlan` to never be stored correctly and
all mission phases to be zeroed.  Additionally, `BrainBlackboard` leaks runtime memory into
scenario JSON, and `SteppingTimeController.GetMode()` returns the wrong mode.

All four changes are one-to-five line fixes.  Do not refactor anything beyond the targeted lines.

---

## Mandatory Workflow

**Complete each task fully before moving to the next.  After every task: build, run the tests, fix
root causes until all pass.  Do NOT ask for permission to do obvious steps.  Do NOT stop until
all four tasks are done and all tests pass.**

---

## Tasks

### Task 1 — TASK-S301: Fix SetManagedComponent / RemoveManagedComponent

**File:** `Hrot\Engine\Hrot.Common\Systems\MissionControlExecutionSystem.cs`
**Task Definition:** See [TASK-DETAIL.md — TASK-S301](../TASK-DETAIL.md#task-s301--fix-setmanagedcomponent--removemanagedcomponent-for-activemissionplan)

**What to fix:**

Line 167 — wrong API for a managed class:
```csharp
// BEFORE (line 167):
repo.SetComponent(entity, new ActiveMissionPlan
{
    Plan = domainPlan
});

// AFTER:
repo.SetManagedComponent(entity, new ActiveMissionPlan
{
    Plan = domainPlan
});
```

Line 216 — wrong removal API for a managed class:
```csharp
// BEFORE (line 216):
repo.RemoveComponent<ActiveMissionPlan>(entity);

// AFTER:
repo.RemoveManagedComponent<ActiveMissionPlan>(entity);
```

**Tests to add in `MissionControlExecutionSystemTests.cs`:**

The existing test `ReplaceMission_ValidEntity_UpdatesQueueAndPublishesSuccessAck` already verifies
`MissionPlanQueue`.  Extend it (or add a new test) to also assert:
- `repo.HasManagedComponent<ActiveMissionPlan>(entity)` is `true` after CMD_REPLACE_MISSION
- `repo.GetManagedComponent<ActiveMissionPlan>(entity)` is not null and `Plan.Tasks` has the
  expected count
- After CMD_ABORT_ALL, `repo.HasManagedComponent<ActiveMissionPlan>(entity)` is `false`

---

### Task 2 — TASK-S302: Fix InlineArray Span Mutation in TryBuildQueue

**File:** `Hrot\Engine\Hrot.Common\Systems\MissionControlExecutionSystem.cs`
**Task Definition:** See [TASK-DETAIL.md — TASK-S302](../TASK-DETAIL.md#task-s302--fix-inlinearray-span-mutation-in-trybuildqueue)

**What to fix:**

In `TryBuildQueue` (starting at line 243), before the `for` loop add a `Span` extraction and
replace direct indexing:

```csharp
// ADD before the for loop:
Span<MissionPhase> phases = queue.Phases;

// REPLACE (inside the loop, was queue.Phases[i] = ...):
phases[i] = new MissionPhase
{
    DoctrineId  = doctrineId,
    Trigger     = trigger,
    TriggerParam = param
};
```

**Tests to add in `MissionControlExecutionSystemTests.cs`:**

Add a test that:
- Creates a plan with 3 tasks (all "MoveToLocation" doctrine)
- After processing CMD_REPLACE_MISSION, asserts `queue.PhaseCount == 3`
- Asserts each of the 3 phases has a non-default `DoctrineId` matching the registered doctrine

---

### Task 3 — TASK-S303: Add DataPolicy.NoSave to BrainBlackboard

**File:** `FDP\Toolkits\Fdp.Toolkits\Behavior\Components\BehaviorComponents.cs`
**Task Definition:** See [TASK-DETAIL.md — TASK-S303](../TASK-DETAIL.md#task-s303--add-datapolicynosave-to-brainblackboard)

**What to fix:**

Add `[DataPolicy(DataPolicy.NoSave)]` at line 53, between the existing `[ComponentId(...)]` and
the struct declaration.  Use the same pattern as `LocomotionChannel` in
`FDP\Toolkits\Fdp.Toolkits\Behavior\Components\ChannelComponents.cs`.

```csharp
// BEFORE:
[StructLayout(LayoutKind.Sequential)]
[ComponentId(GlobalComponentIds.BrainBlackboard)]
public unsafe struct BrainBlackboard

// AFTER:
[StructLayout(LayoutKind.Sequential)]
[ComponentId(GlobalComponentIds.BrainBlackboard)]
[DataPolicy(DataPolicy.NoSave)]
public unsafe struct BrainBlackboard
```

**Tests:** Find or create a serialization test in the Fdp.Toolkits.Tests project that:
- Creates an `EntityRepository`, registers `BrainBlackboard`, sets it on an entity
- Serializes via `FdpAutoSerializer` (or the same path used by scenario save)
- Asserts the resulting JSON does NOT contain `"BrainBlackboard"`

Look for existing `FdpAutoSerializer` tests in
`FDP\Toolkits\Fdp.Toolkits.Tests\` to understand the test pattern before writing new ones.
If a suitable existing test already covers this (e.g. verifies NoSave exclusion for ChannelComponents),
adapt it for BrainBlackboard.

---

### Task 4 — TASK-S304: Fix SteppingTimeController.GetMode()

**File:** `FDP\Toolkits\Fdp.Toolkits\Time\Controllers\SteppingTimeController.cs`
**Task Definition:** See [TASK-DETAIL.md — TASK-S304](../TASK-DETAIL.md#task-s304--fix-steppingtimecontrollergetmode)

**What to fix:**

Line 96–99 — change the return value and remove the incorrect comment:

```csharp
// BEFORE:
public TimeMode GetMode()
{
    return TimeMode.Continuous; // Or add TimeMode.Stepping? treating as continuous mode compatible
}

// AFTER:
public TimeMode GetMode()
{
    return TimeMode.Deterministic;
}
```

**Tests:** Find or create a test in `FDP\Toolkits\Fdp.Toolkits.Tests\` that verifies
`new SteppingTimeController(new GlobalTime()).GetMode() == TimeMode.Deterministic`.

---

## Build and Verify

After all four tasks are done:

```
cd d:\Work\IOS-IG-SimHost-FDP-2
dotnet build IOS-IG-SimHost.sln --no-restore
dotnet test Hrot\Subsystems\Hrot.SimHost.Tests\Hrot.SimHost.Tests.csproj --no-build
dotnet test FDP\Toolkits\Fdp.Toolkits.Tests\Fdp.Toolkits.Tests.csproj --no-build
```

Fix all compiler errors and test failures before submitting the report.  Zero build errors and
zero test failures are the acceptance bar.

---

## Quality Standards

- Do not change any code beyond the lines specified.
- Do not reformat, rename, or add comments unless fixing the targeted line.
- Tests must verify actual component state, not just compilation.
- `Assert.True(repo.HasManagedComponent<ActiveMissionPlan>(entity))` is required — do not
  substitute a weaker assertion.

---

## Success Criteria

- [ ] TASK-S301: `SetManagedComponent` / `RemoveManagedComponent` used; tests verify managed component presence
- [ ] TASK-S302: `Span<MissionPhase> phases = queue.Phases` extraction present; 3-phase test passes
- [ ] TASK-S303: `[DataPolicy(DataPolicy.NoSave)]` on `BrainBlackboard`; JSON exclusion test passes
- [ ] TASK-S304: `GetMode()` returns `TimeMode.Deterministic`; unit test passes
- [ ] `dotnet build IOS-IG-SimHost.sln --no-restore` succeeds (zero errors)
- [ ] All touched test projects pass

---

## Developer Insights (Report Required)

**Q1:** What issues did you encounter? How did you resolve them?

**Q2:** Did you spot any other weak points in the existing test coverage?

**Q3:** What design decisions did you make beyond the instructions?

**Q4:** Are there edge cases not covered by the existing tests that you noticed?

**Q5:** Suggested commit message for this batch.
