# BATCH-01: CGF Registry Hardening, Validator Extraction & NetworkGateway DRY Refactor

**Batch Number:** BATCH-01  
**Tasks:** PACK3-C001, PACK3-U001, PACK3-U002, PACK3-N001, PACK3-N002, PACK3-N003  
**Phase:** Phase 0 (C001), Phase 1 partial (U001, U002), Phase 4 (N001, N002, N003)  
**Estimated Effort:** 12–16 hours  
**Priority:** HIGH  
**Dependencies:** None (first batch)

---

## 📋 Onboarding & Workflow

### Developer Instructions

This is the first batch of `packs-3`. You will:
1. Create `CgfComponentRegistry` to centralise ECS component registration in the CGF node (removing ad-hoc per-component calls from `CgfApplication`).
2. Extract `UrbanCombatValidator` from `UrbanCombatNewScenario` as a reusable validator class that resolves entities by `TkbIdentity`.
3. Promote the canonical `NetworkGatewaySystem` to `FDP.Toolkit.Replication`, delete the copy-pasted clones, and rewire `CycloneNetworkModule`.

### Required Reading (IN ORDER)
1. **Workflow Guide:** `.dev/.guides/DEV-GUIDE.md` — How to work with batches
2. **Onboarding:** `.dev/packs-3/ONBOARDING.md` — Project context
3. **Design Document:** `.dev/packs-3/DESIGN.md` — Full architectural overview for packs-3 (read §Phase 0, §Phase 1, §Phase 4)
4. **Task Definitions:** `.dev/packs-3/TASK-DETAIL.md` — Per-task specs for PACK3-C001, PACK3-U001, PACK3-U002, PACK3-N001, PACK3-N002, PACK3-N003
5. **Prior Pack Context:** `.dev/packs-2/TASK-TRACKER.md` — What was delivered in packs-2

### Source Code Locations
- **CGF Application:** `Hrot.CGF/CgfApplication.cs`, `Hrot.CGF/CgfApplication.cs.txt`
- **SimHost Registry Pattern:** `Hrot.SimHost/` — Look for `SimHostComponentRegistry.cs` as the pattern to follow
- **Urban Combat Scenario:** `FDP/Examples/Fdp.Examples.Scenarios/Integrated/UrbanCombatNewScenario.cs`
- **Shared Component Registries:** Look for `HrotSharedComponentRegistry.cs` in `Hrot.Common` or similar
- **Cyclone Transport Pack:** `FDP/ModuleHost/ModuleHost.Network.Cyclone/`
- **FDP Toolkit Replication:** `FDP/Toolkits/FDP.Toolkit.Replication/`
- **Integration Tests:** `Hrot.ClusterRunner.Integration.Tests/`
- **CGF Tests:** `Hrot.ClusterRunner.Integration.Tests/CgfSubsystemHeadlessTests.cs`, `DistributedBrainMuscleIntegrationTests.cs`

### Report Submission
**When done, submit your report to:**  
`.dev/packs-3/reports/BATCH-01-REPORT.md`

**If you have questions, create:**  
`.dev/packs-3/questions/BATCH-01-QUESTIONS.md`

---

## Context

`packs-2` delivered the HROT Editor composition root, the shared `Hrot.ScenarioEditor` Logic Pack, and the Feature Switch that blends offline and distributed operation. `packs-3` now completes the full scenario authoring lifecycle, eliminates the ACL backdoor, and resolves a DRY violation in NetworkGatewaySystem.

This BATCH-01 covers the cleanest, most independent tasks:
- **C001**: Pure refactor with no change in behaviour — just centralising registration.
- **U001/U002**: Behaviour-preserving extraction — the validator logic stays identical, only the resolution strategy changes from cached handles to TkbIdentity lookup.
- **N001/N002/N003**: Consolidation — remove duplicated code and point the single consumer to the new canonical location.

All three streams are largely independent of each other and of the DTOs/services in Phase 2–3, making this an ideal first batch.

---

## 🎯 Batch Objectives

1. Reduce `CgfApplication.cs` constructor noise by delegating all component registrations to `CgfComponentRegistry`.
2. Make `UrbanCombatNewScenario.EvaluateTick` resilient to serialisation round-trips by switching to `TkbIdentity`-based entity resolution.
3. Promote `NetworkGatewaySystem` to the FDP toolkit and delete copy-paste debt.

---

## ✅ Tasks

### Task 1: Create `CgfComponentRegistry` (PACK3-C001)

**Files:**
- `Hrot.CGF/CgfComponentRegistry.cs` — **NEW FILE**
- `Hrot.CGF/CgfApplication.cs` — **REFACTOR** (replace per-component calls with `RegisterAll`)

**Task Definition:** See [TASK-DETAIL.md — PACK3-C001](../TASK-DETAIL.md#pack3-c001--create-cgfcomponentregistry)

**Key Requirements:**
- Static class with `public static void RegisterAll(EntityRepository world)`.
- Three ordered tiers: (1) `HrotSharedComponentRegistry.RegisterAll(world)`, (2) cognitive/kinematic components, (3) IG presentation components.
- Check `SimHostComponentRegistry.cs` for naming conventions and tier patterns.
- Do **not** move `CognitiveComponentRegistry` / `KinematicComponentRegistry` out of `Hrot.SimHost` (out of scope).

**Tests Required:**
- Unit test: instantiate bare `EntityRepository`, call `RegisterAll`, assert `BrainBTreeState`, `VehicleState`, and `EntityInfo` are registered without throwing.
- Regression: `CgfSubsystemHeadlessTests` and `DistributedBrainMuscleIntegrationTests` must still pass.

---

### Task 2: Extract `UrbanCombatValidator` (PACK3-U001)

**File:** `FDP/Examples/Fdp.Examples.Scenarios/Integrated/UrbanCombatValidator.cs` — **NEW FILE**

**Task Definition:** See [TASK-DETAIL.md — PACK3-U001](../TASK-DETAIL.md#pack3-u001--extract-urbancombatvalidator)

**Key Requirements:**
- `public class UrbanCombatValidator` (non-static, non-sealed).
- `public bool EvaluateTick(uint tick, EntityRepository world)` — resolves entities via `TkbIdentity` query each call (no cached `Entity` fields).
- Four sequential bool latches: `_latchAmbushFired`, `_latchApcHalted`, `_latchInsurgentHit`, `_latchInsurgentKilled`.
- Returns `true` when `_latchInsurgentKilled` fires. Throws `ScenarioFailureException(5, …)` if `tick > 600`.
- Study the **existing** `UrbanCombatNewScenario.EvaluateTick` carefully to reproduce the exact latch logic. The key change is replacing `Entity`-handle field access with `TkbIdentity` queries.

**Tests Required:**
- Unit test 1: minimal repo with a `TkbInsurgent` entity having `WeaponChannel.ActiveAction == CombatConstants.ActionIdAimAndFire` → after one call, `_latchAmbushFired` should be set. (expose via test subclass or `internal` + `[InternalsVisibleTo]`)
- Unit test 2: `tick > 600`, no latches → `ScenarioFailureException` thrown.
- Unit test 3: simulate all latches firing sequentially → `EvaluateTick` returns `true`.

---

### Task 3: Simplify `UrbanCombatNewScenario` (PACK3-U002)

**File:** `FDP/Examples/Fdp.Examples.Scenarios/Integrated/UrbanCombatNewScenario.cs` — **UPDATE**

**Task Definition:** See [TASK-DETAIL.md — PACK3-U002](../TASK-DETAIL.md#pack3-u002--simplify-urbancombatscenario)

**Key Requirements:**
- Add `private readonly UrbanCombatValidator _validator = new();`.
- Replace entire `EvaluateTick` body with `return _validator.EvaluateTick(tick, world);`.
- Remove the now-redundant latch fields (`_latchAmbushFired`, `_latchApcHalted`, `_latchInsurgentHit`, `_latchInsurgentKilled`).

**Tests Required:**
- Regression: existing `UrbanCombatNewScenario` integration test still passes (same 600-tick budget, same success signal).

---

### Task 4: Create canonical `NetworkGatewaySystem` (PACK3-N001)

**File:** `FDP/Toolkits/FDP.Toolkit.Replication/Systems/NetworkGatewaySystem.cs` — **NEW FILE**

**Task Definition:** See [TASK-DETAIL.md — PACK3-N001](../TASK-DETAIL.md#pack3-n001--relocate-networkgatewaysystem-to-fdptoolkitreplication)

**Key Requirements:**
- Namespace: `FDP.Toolkit.Replication.Systems`.
- Content: transport-agnostic logic from the Cyclone clone — PendingNetworkAck handling, ConstructionOrder processing, topology peer tracking, ELM promotion.
- **Zero** CycloneDDS imports. Only `Fdp.Kernel`, `FDP.Toolkit.Lifecycle`, `FDP.Toolkit.Replication.Components`.
- Study both the Cyclone local `NetworkGatewaySystem.cs` and the legacy `ModuleHost.Core` one to understand the full logic.

**Tests Required:**
- Unit test: instantiate with mock `INetworkTopology`, feed synthetic `PendingNetworkAck`, verify `EntityLifecycleModule.MarkPeerReady` is called.

---

### Task 5: Rewire `CycloneNetworkModule` (PACK3-N003)

**File:** `FDP/ModuleHost/ModuleHost.Network.Cyclone/Modules/CycloneNetworkModule.cs` — **UPDATE**

**Task Definition:** See [TASK-DETAIL.md — PACK3-N003](../TASK-DETAIL.md#pack3-n003--rewire-cyclonenetworkmodule)

**Key Requirements:**
- Replace `using <CycloneLocal>.Systems;` with `using FDP.Toolkit.Replication.Systems;`.
- `new NetworkGatewaySystem(...)` must now resolve to the toolkit class.
- Remove any references to the deleted `NetworkGatewayModule` (local clone).
- **Do N003 before N002** — confirm compiles, then delete in N002.

---

### Task 6: Delete clones and legacy originals (PACK3-N002)

**Files to DELETE:**
- `FDP/ModuleHost/ModuleHost.Network.Cyclone/Systems/NetworkGatewaySystem.cs`
- `FDP/ModuleHost/ModuleHost.Network.Cyclone/Modules/NetworkGatewayModule.cs`
- `FDP/ModuleHost/ModuleHost.Core/Network/NetworkGatewaySystem.cs`
- `FDP/ModuleHost/ModuleHost.Core/Network/NetworkGatewayModule.cs`

**Task Definition:** See [TASK-DETAIL.md — PACK3-N002](../TASK-DETAIL.md#pack3-n002--delete-clones-and-legacy-originals)

**Key Requirements:**
- Execute **only after** N001 (canonical created) and N003 (CycloneNetworkModule rewired) are both compiling.
- After deletions, run `dotnet build` to confirm no remaining references to deleted symbols.
- Also remove any leftover `using` directives in `ModuleHost.Network.Cyclone` pointing to the deleted namespace.

**Tests Required:**
- `grep -r "class NetworkGatewaySystem"` returns exactly one result (the new toolkit file).
- All distributed integration tests that use `CycloneNetworkModule` (e.g. ghost promotion tests, `DistributedBrainMuscleIntegrationTests`) continue to pass.

---

## 🧪 Testing Requirements

### Minimum Test Coverage

| Task | Test Type | Minimum Assertions |
|------|-----------|--------------------|
| C001 | Unit | 3 (one per tier: BrainBTreeState, VehicleState, EntityInfo registered) |
| U001 | Unit | 3 (latch fires, timeout throws, all latches → true) |
| U002 | Regression | UrbanCombatNewScenario integration test passes |
| N001 | Unit | 1 (MarkPeerReady called after synthetic ACK) |
| N002 | Regression | Ghost promotion + DistributedBrainMuscle pass |
| N003 | Regression | CycloneNetworkModule-dependent tests pass |

### Test-Driven Task Progression

**MANDATORY WORKFLOW — Test-Driven Task Progression:**

> For each task, before writing production code:
> 1. Read the existing tests to understand what currently passes.
> 2. Write the failing test(s) first (unit or integration as specified).
> 3. Implement the production code to make the tests pass.
> 4. Run the full relevant test suite to confirm no regressions.
> 5. Only then mark the task done in your report.
>
> **Never consider a task complete until all its tests pass AND existing tests remain green.**

Run all tests before submitting the report:
```
dotnet build d:\Work\IOS-IG-SimHost-FDP-2\IOS-IG-SimHost.sln
dotnet test Hrot.ClusterRunner.Integration.Tests --filter "CgfSubsystemHeadless|DistributedBrainMuscle|UrbanCombat" --no-build
```

---

## 📊 Report Requirements

Submit your report to `.dev/packs-3/reports/BATCH-01-REPORT.md`.

**Report structure:**

```markdown
# BATCH-01 Report

## Implementation Summary
[Brief summary of what was done per task]

## Tests Added
[List new test methods with test file paths]

## Test Results
[Paste or describe the test run output — include pass/fail counts]

## Developer Insights
1. **Issues Encountered:** What problems did you hit during implementation?
2. **Weak Points Spotted:** What fragile or unclear areas did you notice in the codebase?
3. **Design Decisions Made Beyond the Spec:** Any decisions not explicitly stated in the spec that you had to make?

## Deviations from Spec (if any)
[List any deviations with justification]
```

---

## ⚠️ Important Notes

1. **Do NOT start Phase 2 or Phase 3 work** — that is reserved for future batches.
2. **CgfComponentRegistry**: do NOT move `CognitiveComponentRegistry` or `KinematicComponentRegistry` out of `Hrot.SimHost` — that is explicitly out of scope.
3. **N002 deletions**: only delete after N001+N003 compile cleanly.
4. **FDP submodule**: `FDP/` is a git submodule. Commit changes in `FDP/` separately with a descriptive commit message before committing the parent repo.
