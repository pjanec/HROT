# MOD1-BATCH-09: Generic Application Lifecycle Toolkit (Phase 9)

**Batch Number:** MOD1-BATCH-09  
**Tasks:** DB-MOD1-08 (debt fix), MOD1-P9T1, MOD1-P9T2, MOD1-P9T3, MOD1-P9T4, MOD1-P9T5  
**Phase:** Phase 9 (`FDP.Framework.Runner` — Generic Application Lifecycle Toolkit)  
**Estimated Effort:** 10-12 hours  
**Priority:** HIGH  
**Dependencies:** MOD1-BATCH-08

---

## 📋 Onboarding & Workflow

### Who You Are
You are a developer implementing the modularization of the IOS-IG-SimHost application. This is an ongoing, multi-batch effort. **Read this section entirely before touching any code.**

### Project Goal
Refactoring towards better modularization and generalization. **What should be generic must come under FDP, not be left in the Bagira domain.** This batch's focus is the Runner layer — the application orchestration infrastructure (`SubsystemOrchestrator`, `HeadlessTestExecutor`, `WaitingRoomCoordinator`) that is currently hard-wired to Bagira-specific concrete types. Your job is to cut those concrete references out and move the generic orchestration into a new `FDP.Framework.Runner` toolkit.

### Non-Negotiable Rules
1. **The application must keep working.** `Bagira.Runner -x all` integration tests must pass after every task. The app must still launch in both windowed and headless mode.
2. **Tests must check real behaviour.** Don't just assert method call counts — verify observable outcomes.
3. **Component IDs belong in toolkit-local registries** — never add to `GlobalComponentIds` directly.
4. **`FDP.*` assemblies may never reference `Bagira.*` assemblies.** The dependency flows in one direction only: Bagira → FDP.
5. **Do not modify third-party submodules** under `FDP\ExtDeps\`.

### Required Reading (IN ORDER)
1. **Developer workflow guide:** `.dev-workstream/README.md`
2. **Architecture design:** `docs/modularizing/MOD1-DESIGN.md` — Phase 9 (§3.9)
3. **Task Details:** `docs/modularizing/MOD1-TASK-DETAIL.md` — Phase 9 tasks (MOD1-P9T1 through MOD1-P9T5)
4. **Previous Review:** `.dev-workstream/reviews/MOD1-BATCH-08-REVIEW.md`
5. **Debt Tracker:** `docs/modularizing/MOD1-DEBT-TRACKER.md`

### Source Code Location
- **New project to create:** `FDP/Framework/FDP.Framework.Runner/`
- **Source of truth for types to move:** `Bagira.Runner/` — subsystems, orchestrator, test executor
- **Debt targets:** `Bagira.SimHost/Modules/Orchestration/DrillSlave.cs`, `Bagira.SimHost/SimulationLogicModule.cs`

### Report Submission
**When done, submit your report to:**  
`.dev-workstream/reports/MOD1-BATCH-09-REPORT.md`

---

## 🚨 IMPORTANT DEBT ITEMS TO FIX FIRST

### Debt Fix DB-MOD1-08: `SimulationLogicModule` Role-Conditional Sub-Module Creation

**Priority:** P2 — fix before starting Phase 9 tasks.

Currently `SimulationLogicModule` creates all sub-modules unconditionally in its constructor, regardless of the `NodeRole`. On a Muscle-only or IG-only node, this wastes memory and CPU spinning up modules it will never tick.

**What to do:**
- Accept a `NodeRole` parameter in `SimulationLogicModule`'s constructor.
- Use the role to conditionally create (or skip) each sub-module. For example, a `Muscle` node does not need `CognitiveRuntimeModule` or `MissionControlModule`; a `NavigationSolver` node needs only `NavigationSolverModule`.
- Update all call sites in `NodeBootstrapper` to pass the role.
- Update tests to pass a role.

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

1. **DB-MOD1-08:** Role-conditional `SimulationLogicModule` → **ALL tests pass** ✅
2. **MOD1-P9T1:** Create `FDP.Framework.Runner` + extract `ISubsystem` / `IMapCameraProvider` → **ALL tests pass** ✅
3. **MOD1-P9T2:** Refactor `SubsystemOrchestrator` into `FDP.Framework.Runner` → **ALL tests pass + app launches** ✅
4. **MOD1-P9T3:** Extract `WaitingRoomCoordinator` and `RunnerConfiguration` → **ALL tests pass** ✅
5. **MOD1-P9T4:** Extract `HeadlessTestExecutor` and generic handlers → **ALL tests pass** ✅
6. **MOD1-P9T5:** Refactor `Bagira.Runner` as pure composition root → **`-x all` integration tests pass + app launches** ✅

---

## ✅ Tasks

### Debt Fix DB-MOD1-08: Role-Conditional `SimulationLogicModule`
See description in debt section above.

---

### Task 1: MOD1-P9T1 — Create `FDP.Framework.Runner` + Extract `ISubsystem` / `IMapCameraProvider`

**Task Definition:** See [MOD1-TASK-DETAIL.md §MOD1-P9T1](docs/modularizing/MOD1-TASK-DETAIL.md#mod1-p9t1--create-fdpframeworkrunner-project--extract-isubsystem--imapcameraprovider)

**Key constraint:** `FDP.Framework.Runner.csproj` must reference `Raylib-cs`, `ImGui.NET`, and `ModuleHost.Core`. It must have **zero** references to any `Bagira.*` assembly. Each concrete subsystem in `Bagira.Runner` adds its own `TitleBarColor` implementation.

---

### Task 2: MOD1-P9T2 — Refactor `SubsystemOrchestrator` into `FDP.Framework.Runner`

**Task Definition:** See [MOD1-TASK-DETAIL.md §MOD1-P9T2](docs/modularizing/MOD1-TASK-DETAIL.md#mod1-p9t2--refactor-subsystemorchestrator-into-fdpframeworkrunner)

**Key constraint:** Remove `BuildSubsystems` factory method completely. Remove the hardcoded `PushSubsystemColors` switch. Replace with loops over `subsystem.TitleBarColor` and `subsystems.OfType<IMapCameraProvider>()`. `Bagira.Runner.Program` becomes the composition root that creates and injects concrete subsystems.

---

### Task 3: MOD1-P9T3 — Extract `WaitingRoomCoordinator` and `RunnerConfiguration`

**Task Definition:** See [MOD1-TASK-DETAIL.md §MOD1-P9T3](docs/modularizing/MOD1-TASK-DETAIL.md#mod1-p9t3--extract-waitingroomcoordinator-and-runnerconfiguration-into-fdpframeworkrunner)

**Key constraint:** `WaitingRoomCoordinator` moves as-is (it's already generic — no Bagira references). `RunnerConfiguration` carries only generic flags (`--headless`, `--domain`, `--no-wait`). `BagiraRunnerConfiguration : RunnerConfiguration` in `Bagira.Runner` adds `--mode` and `--role`.

---

### Task 4: MOD1-P9T4 — Extract `HeadlessTestExecutor` Core + Generic Action Handlers

**Task Definition:** See [MOD1-TASK-DETAIL.md §MOD1-P9T4](docs/modularizing/MOD1-TASK-DETAIL.md#mod1-p9t4--extract-headlesstestexecutor-core--generic-action-handlers-into-fdpframeworkrunner)

**Key constraint:** `SpawnActionHandler`, `MoveActionHandler`, `AssertPositionActionHandler` stay in `Bagira.Runner` (they reference Bagira ECS types). Only the domain-agnostic handlers (`WaitActionHandler`, `TickActionHandler`, `AssertAllActionHandler`) and the executor framework itself move to `FDP.Framework.Runner.Testing`.

---

### Task 5: MOD1-P9T5 — Refactor `Bagira.Runner` as Pure Composition Root

**Task Definition:** See [MOD1-TASK-DETAIL.md §MOD1-P9T5](docs/modularizing/MOD1-TASK-DETAIL.md#mod1-p9t5--refactor-bagirarunner-as-pure-composition-root)

**Key constraint:** `Program.cs` must not import `Raylib.*` or `ImGui.*` directly — those belong inside `SubsystemOrchestrator`. Program parses args, constructs subsystems, constructs the orchestrator, and either runs headless or windowed. That's it.

---

## 📊 Report Requirements

Submit `.dev-workstream/reports/MOD1-BATCH-09-REPORT.md` with:

**Developer Insights**

**Q1:** For DB-MOD1-08 — what role combinations are meaningful? Which modules are skipped for which roles (provide a table)?

**Q2:** For P9T2 — how many lines of Bagira-specific code were removed from `SubsystemOrchestrator`? Was any orchestration logic harder to generalize than expected?

**Q3:** For P9T5 — does `Program.cs` still have any direct references to Raylib or ImGui? List them if any.

**Q4:** Were there any circular dependency issues discovered when moving types to `FDP.Framework.Runner`?

---

## 🎯 Success Criteria

This batch is DONE when:
- [ ] `SimulationLogicModule` skips sub-modules that are irrelevant for the current `NodeRole`.
- [ ] `FDP.Framework.Runner` compiles with zero `Bagira.*` references.
- [ ] `SubsystemOrchestrator` is in `FDP.Framework.Runner` with no hardcoded concrete type references.
- [ ] `Bagira.Runner.Program` is a pure composition root: parse args → construct subsystems → inject → run.
- [ ] `Bagira.Runner -x all` integration tests pass unconditionally.
- [ ] All unit and integration test suites pass with 0 failures.
