# BATCH-01: Togglable Group Foundation

**Batch Number:** BATCH-01
**Tasks:** T-RMF-01, T-RMF-02, T-RMF-03, T-RMF-04, T-RMF-05
**Phase:** Phase 1 — Togglable Group Foundation
**Estimated Effort:** 4-6 hours
**Priority:** HIGH
**Dependencies:** None (first batch)

---

## Onboarding & Workflow

### Developer Instructions

This batch creates the three new togglable group classes and updates the two orchestration files that wire them into the replay handler. This is pure new-file creation + small signature changes — no existing logic is altered. Do **not** start on Phase 2 (system conversion). Finish this batch, run all tests, then write the report.

**DO NOT ASK for permission to run tests, fix compile errors, or iterate. Do it all, then report.**

### Required Reading (IN ORDER)

1. **Design document:** `.dev/replay-and-modules/DESIGN.md` — Sections 3.1 (three new groups), 3.5 (ReferenceReplayLoadHandler), 3.6 (NodeBootstrapper)
2. **Task details:** `.dev/replay-and-modules/TASK-DETAIL.md` — T-RMF-01 through T-RMF-05 (lines 1–245 approximately)
3. **Onboarding guide:** `.dev/replay-and-modules/ONBOARDING.md` — ISystemGroup requirement section
4. **Reference model:** `FDP/Engine/Fdp.ModuleHost/Scheduling/NetworkLifecycleSystemGroup.cs` — study this, the three new classes follow the same structural pattern (but implement `ISystemGroup` instead of being a plain class)
5. **Interface definition:** `FDP/Engine/Fdp.ModuleHost/Abstractions/ISystemGroup.cs` — must implement this exactly

### Source Code Location

- **New files:** `FDP/Engine/Fdp.ModuleHost/Scheduling/` (create three new files here)
- **Handler update:** `FDP/Toolkits/Fdp.Toolkits/Orchestration/Handlers/ReferenceReplayLoadHandler.cs`
- **Bootstrapper update:** `Hrot/Subsystems/Hrot.SimHost/NodeBootstrapper.cs`
- **Test files to update:** search for `SimulationSystemGroup` in test files under `FDP/Toolkits/Fdp.Toolkits.Tests/` and `Hrot/Subsystems/Hrot.SimHost.Tests/`

### Report Submission

When done, submit your report to: `.dev/replay-and-modules/reports/BATCH-01-REPORT.md`

---

## Context

The replay system currently uses `SimulationSystemGroup` (from `Fdp.Core` — the legacy ECS engine) as the mechanism for disabling simulation during replay. This is broken in three ways:

1. `SimulationSystemGroup` is passed empty to the replay handler — actual game systems live in a separate `_kernelGroup` that the handler cannot touch.
2. Nothing stops input-phase systems during replay.
3. Nothing stops post-simulation physics integration during replay.

This batch creates the three modern replacements (`TogglableSimulationGroup`, `TogglableInputGroup`, `TogglablePostSimulationGroup`) and updates the replay handler + bootstrapper signatures to accept them. The actual wiring into applications (SimHostApp, CgfSubsystem) is done in Batch 03.

**Critical design constraint:** All three groups must implement `ISystemGroup` (from `Fdp.ModuleHost.Abstractions`), not just `IEcsModuleSystem`. The reason: `SystemScheduler.ExecuteSystem` checks `if (system is ISystemGroup group)` and calls `ExecuteGroup` which profiles each inner system individually in the `ArchitectureDiagnosticsWindow`. Without `ISystemGroup`, the entire group appears as a single black-box entry.

---

## Tasks

### Task 1: Create `TogglableSimulationGroup` (T-RMF-01)

**File:** `FDP/Engine/Fdp.ModuleHost/Scheduling/TogglableSimulationGroup.cs` (NEW FILE)

See T-RMF-01 in `TASK-DETAIL.md` for the complete implementation with comments.

Key requirements:
- `[UpdateInPhase(SystemPhase.Simulation)]`
- Implements `ISystemGroup` (not just `IEcsModuleSystem`)
- `public bool Enabled { get; set; } = true;`
- Constructor: `(string name, params IEcsModuleSystem[] innerSystems)` — also add an overload accepting `IReadOnlyList<IEcsModuleSystem>` for callers that have property lists
- `GetSystems()` returns the inner array as `IReadOnlyList<IEcsModuleSystem>`
- `Execute`: skip all inner systems when `!Enabled`

### Task 2: Create `TogglableInputGroup` (T-RMF-02)

**File:** `FDP/Engine/Fdp.ModuleHost/Scheduling/TogglableInputGroup.cs` (NEW FILE)

Identical structure to `TogglableSimulationGroup` but with `[UpdateInPhase(SystemPhase.Input)]`.

See T-RMF-02 in `TASK-DETAIL.md`.

### Task 3: Create `TogglablePostSimulationGroup` (T-RMF-03)

**File:** `FDP/Engine/Fdp.ModuleHost/Scheduling/TogglablePostSimulationGroup.cs` (NEW FILE)

Identical structure but with `[UpdateInPhase(SystemPhase.PostSimulation)]`.

**Additional doc requirement:** XML doc must explain why physics integration must be disabled during replay (PlaybackTickSystem restores historical positions; if Ballistics/LinearKin/CarKin run afterwards they advance positions past the recorded values).

See T-RMF-03 in `TASK-DETAIL.md`.

### Task 4: Update `ReferenceReplayLoadHandler` (T-RMF-04)

**File:** `FDP/Toolkits/Fdp.Toolkits/Orchestration/Handlers/ReferenceReplayLoadHandler.cs` (UPDATE)

See T-RMF-04 in `TASK-DETAIL.md` for the complete change list.

Summary:
- Replace `SimulationSystemGroup? _simGroup` field with `TogglableSimulationGroup? _simGroup`
- Add `TogglableInputGroup? _inputGroup` field
- Add `TogglablePostSimulationGroup? _postSimGroup` field
- Update constructor to accept and store all three
- Update `SetSystemsEnabled` to toggle all four groups (`_inputGroup`, `_simGroup`, `_postSimGroup`, `_lifecycleGroup`)
- Fix `using` directives (remove `Fdp.Core` if no longer needed, add `Fdp.ModuleHost.Scheduling`)

After updating the handler, find all **test files** that construct `ReferenceReplayLoadHandler` or pass `SimulationSystemGroup` to it and update them to pass `new TogglableSimulationGroup("test")` or `null` as appropriate.

### Task 5: Update `NodeBootstrapper.BuildOrchestration` (T-RMF-05)

**File:** `Hrot/Subsystems/Hrot.SimHost/NodeBootstrapper.cs` (UPDATE)

See T-RMF-05 in `TASK-DETAIL.md`.

Summary:
- Replace `Fdp.Core.SimulationSystemGroup? simGroup = null` parameter with `Fdp.ModuleHost.Scheduling.TogglableSimulationGroup? simGroup = null`
- Add `Fdp.ModuleHost.Scheduling.TogglableInputGroup? inputGroup = null` parameter
- Add `Fdp.ModuleHost.Scheduling.TogglablePostSimulationGroup? postSimGroup = null` parameter
- Forward all three to `new ReferenceReplayLoadHandler(controller, inputGroup, simGroup, postSimGroup, lifecycleGroup, bypassToggle, localTempRoot)`
- Update the null-guard condition to check any of the four groups

All callers of `BuildOrchestration` that pass the old `simGroup:` named argument must be updated. Find them with a workspace search for `BuildOrchestration` and fix each call site (pass `null` for the new parameters for now — Batch 03 will pass real instances).

---

## Testing Requirements

### Mandatory workflow

Implement Task 1 → build → Task 2 → build → Task 3 → build → Task 4 → build + run affected tests → Task 5 → build + run ALL tests.

**Do not move to the next task until the current one compiles.**

### Test updates required

1. Search for all test files using `SimulationSystemGroup` in the construction of `ReferenceReplayLoadHandler` or `NodeBootstrapper`. Replace with `TogglableSimulationGroup` (or `null`).
2. Ensure the existing test suite still passes at full count after the changes.

### New tests required (in the appropriate existing test project)

Write at least 3 unit tests for the togglable groups:

- `TogglableSimulationGroup_WhenEnabled_ExecutesAllInnerSystems` — verify all inner systems' `Execute` is called
- `TogglableSimulationGroup_WhenDisabled_SkipsAllInnerSystems` — verify no inner system's `Execute` is called
- `TogglableSimulationGroup_GetSystems_ReturnsAllInnerSystems` — verify `GetSystems()` returns the correct list

Write equivalent tests for `TogglableInputGroup` and `TogglablePostSimulationGroup` (9 tests total, or combine into parameterized tests).

Place tests in the closest appropriate existing test project (look for `Fdp.ModuleHost.Tests` or similar under `FDP/Engine/`).

---

## Quality Standards

- All XML doc comments must be complete (no `///` stubs without text).
- No `#pragma warning disable` or suppressed warnings.
- Follow the existing code style in `NetworkLifecycleSystemGroup.cs`.
- Tests must verify **actual behavior** (call counts, enabled/disabled effect on execution), not just that the class compiles.

---

## Success Criteria

- [ ] T-RMF-01: `TogglableSimulationGroup.cs` exists, implements `ISystemGroup`, `[UpdateInPhase(SystemPhase.Simulation)]`
- [ ] T-RMF-02: `TogglableInputGroup.cs` exists, implements `ISystemGroup`, `[UpdateInPhase(SystemPhase.Input)]`
- [ ] T-RMF-03: `TogglablePostSimulationGroup.cs` exists, implements `ISystemGroup`, `[UpdateInPhase(SystemPhase.PostSimulation)]`
- [ ] T-RMF-04: `ReferenceReplayLoadHandler` stores and toggles all four groups; no `SimulationSystemGroup` field
- [ ] T-RMF-05: `NodeBootstrapper.BuildOrchestration` accepts all three new group types; all call sites compile
- [ ] Solution builds with 0 errors
- [ ] All pre-existing tests still pass
- [ ] At least 9 new unit tests covering Enabled/Disabled behavior for all three group types

---

## Reference Materials

- **Task details:** `.dev/replay-and-modules/TASK-DETAIL.md` (T-RMF-01 through T-RMF-05)
- **Design:** `.dev/replay-and-modules/DESIGN.md` (Sections 3.1, 3.5, 3.6)
- **Model class:** `FDP/Engine/Fdp.ModuleHost/Scheduling/NetworkLifecycleSystemGroup.cs`
- **Interface:** `FDP/Engine/Fdp.ModuleHost/Abstractions/ISystemGroup.cs`
- **Handler:** `FDP/Toolkits/Fdp.Toolkits/Orchestration/Handlers/ReferenceReplayLoadHandler.cs`
- **Bootstrapper:** `Hrot/Subsystems/Hrot.SimHost/NodeBootstrapper.cs`
