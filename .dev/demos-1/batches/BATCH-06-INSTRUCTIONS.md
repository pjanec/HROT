# BATCH-06: Advanced Demos (Phase 4) + BATCH-05 Debt

**Batch Number:** BATCH-06  
**Tasks:** DEM1-D006, DEM1-D007 + DEBT (LocalGridBuilderSystem incremental rebuild, AutonomousPerceptionModule bus isolation)  
**Phase:** Phase 4 — Advanced Demos  
**Estimated Effort:** 10–14 hours  
**Priority:** HIGH  
**Dependencies:** BATCH-05 approved (`DEM1-D005` complete)

---

## 📋 Onboarding & Workflow

### Developer Instructions

Implement Phase 4 scenarios **`DEM1-D006`** and **`DEM1-D007`** exactly as specified in the task detail document (including test names and tick-phase assertions). Before that, close the two **P2/P3** items from `.dev-workstream/DEBT-TRACKER.md` that target **BATCH-06** (rows: `LocalGridBuilderSystem` dirty/incremental updates; `AutonomousPerceptionModule` / global bus flush coupling).

Do **not** duplicate the design doc in this batch file—read the linked sections and implement to the **Success conditions** blocks in `DEM1-TASK-DETAIL.md`.

### Required Reading (IN ORDER)

1. **Code standards:** `.dev-workstream/guides/CODE-STANDARDS.md`
2. **Newcomer / repo layout:** `docs/demos-1/DEM1-ONBOARDING.md`
3. **Previous review (debt context):** `.dev-workstream/reviews/BATCH-05-REVIEW.md`
4. **Architecture (Phase 4):** `docs/demos-1/DEM1-DESIGN.md` — §6.3 (`DEM1-D006`, `DEM1-D007`)
5. **Task specifications (source of truth):** `docs/demos-1/DEM1-TASK-DETAIL.md` — sections **DEM1-D006** and **DEM1-D007**
6. **Debt registry:** `.dev-workstream/DEBT-TRACKER.md` — filter rows with **Target Fix = BATCH-06**

### Source Code Location

- **Scenarios (new/updated):**
  - `FDP/Examples/Fdp.Examples.Scenarios/Cognitive/MissionCommandScenario.cs` (per task detail)
  - `FDP/Examples/Fdp.Examples.Scenarios/Perception/TerrainClampingScenario.cs` (per task detail)
- **Debt / engine (update as needed):**
  - `FDP/Toolkits/FDP.Toolkit.Perception/Systems/LocalGridBuilderSystem.cs`
  - `FDP/Toolkits/FDP.Toolkit.Perception/Modules/AutonomousPerceptionModule.cs`
- **Tests:** `FDP/Examples/Fdp.Examples.Scenarios.Tests/` (add tests named exactly as in `DEM1-TASK-DETAIL.md`)

Study existing scenarios for patterns: e.g. `FDP/Examples/Fdp.Examples.Scenarios/Perception/SensorGridScenario.cs`, `FDP/Examples/Fdp.Examples.Scenarios/Cognitive/BehaviorValidationScenario.cs`.

### Report Submission

**When done, submit:** `.dev-workstream/reports/BATCH-06-REPORT.md`  
**If blocked, create:** `.dev-workstream/questions/BATCH-06-QUESTIONS.md`

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: Complete tasks in sequence; do not start the next until the current one is implemented, tested, and `dotnet test` is green for the whole `Fdp.Examples.Scenarios.Tests` project (plus any project you touch).**

1. **Task 1:** Corrective → tests / regression → **all tests pass**
2. **Task 2:** Corrective → tests / regression → **all tests pass**
3. **Task 3:** `DEM1-D006` → **all tests pass**
4. **Task 4:** `DEM1-D007` → **all tests pass**

---

## Context

Phase 4 proves **mission arbitration** (`DEM1-D006`) and **terrain / network transform clamping** (`DEM1-D007`) in isolation, using the same headless deterministic harness as earlier DEM1 scenarios.

**Related tasks (read specs, do not paraphrase here):**

- [DEM1-D006](docs/demos-1/DEM1-TASK-DETAIL.md#dem1-d006--missioncommand-scenario)
- [DEM1-D007](docs/demos-1/DEM1-TASK-DETAIL.md#dem1-d007--terrainclamping-scenario)

---

## 🎯 Batch Objectives

1. Reduce full-grid rebuild cost in `LocalGridBuilderSystem` via movement/dirty tracking (DEBT-TRACKER).
2. Avoid global event-bus corruption when perception runs synchronously—internal queue or snapshot discipline (DEBT-TRACKER / BATCH-05 notes).
3. Ship `MissionCommandScenario` + `TerrainClampingScenario` with the xUnit tests listed under each task in `DEM1-TASK-DETAIL.md`.

---

## ✅ Tasks

### Task 1: [CORRECTIVE] LocalGridBuilderSystem — incremental / dirty spatial updates

**File:** `FDP/Toolkits/FDP.Toolkit.Perception/Systems/LocalGridBuilderSystem.cs`  
**Debt:** `.dev-workstream/DEBT-TRACKER.md` — P3 performance, `LocalGridBuilderSystem` full rebuild (source BATCH-05).

**Description:** Avoid rebuilding the spatial hash from scratch every tick when only a subset of entities moved. Use dirty flags, timestamps, or explicit change tracking so removals/reinserts target entities that actually changed pose since the last build.

**Requirements:**

- Preserve existing observable behavior for scenarios and toolkits unless the old behavior was a bug; if tests fail, prefer fixing tests only when the new behavior matches the design—otherwise adjust implementation.
- Document in the BATCH-06 report any trade-off (memory vs. CPU, worst-case fallback to full rebuild).

**Tests:** Existing perception/physics tests must still pass; add or extend unit coverage if the debt fix is not exercised by scenario tests.

---

### Task 2: [CORRECTIVE] AutonomousPerceptionModule — decouple synchronous execution from global bus swaps

**File:** `FDP/Toolkits/FDP.Toolkit.Perception/Modules/AutonomousPerceptionModule.cs`  
**Debt:** `.dev-workstream/DEBT-TRACKER.md` — P2 architecture, global bus flush / `FlushEcbAndSwap` coupling (source BATCH-05).

**Description:** When `Execute()` (or equivalent synchronous path) runs, avoid unilateral `SwapBuffers` / ECB patterns that advance or reorder events for unrelated subsystems. Prefer an internal queue, scoped bus, or non-reentrant snapshot so perception can flush safely without cross-layer corruption.

**Tests:** `SensorGridScenario` and related tests must remain green; add a focused test if a regression would otherwise be silent.

---

### Task 3: MissionCommand scenario (`DEM1-D006`)

**File:** `FDP/Examples/Fdp.Examples.Scenarios/Cognitive/MissionCommandScenario.cs` (create if missing)  
**Task definition:** [DEM1-TASK-DETAIL.md — DEM1-D006](docs/demos-1/DEM1-TASK-DETAIL.md#dem1-d006--missioncommand-scenario)  
**Design:** [DEM1-DESIGN.md §6.3 — MissionCommand](docs/demos-1/DEM1-DESIGN.md#dem1-d006-missioncommand-dynamic-mission--preemption)

Implement **exactly** the components, modules, spawn data, `MissionPlanQueue` phases, `EvaluateTick` ticks, and assertions described in the task detail (including **`Span<MissionPhase>`** note for `InlineArray`).

**Tests required (names must match):**

- `MissionCommand_RunToCompletion_ExitsZero`
- `MissionCommand_Phase3_DirectorAdvancesPhase_WhenThreated` (spelling per task detail)
- `MissionCommand_Phase4_ArbitrationPreemptsStaleLocoCommand`

---

### Task 4: TerrainClamping scenario (`DEM1-D007`)

**File:** `FDP/Examples/Fdp.Examples.Scenarios/Perception/TerrainClampingScenario.cs` (create if missing)  
**Task definition:** [DEM1-TASK-DETAIL.md — DEM1-D007](docs/demos-1/DEM1-TASK-DETAIL.md#dem1-d007--terrainclamping-scenario)  
**Design:** [DEM1-DESIGN.md §6.3 — TerrainClamping](docs/demos-1/DEM1-DESIGN.md#dem1-d007-terrainclamping-z-height-smoothing--jump-rejection)

Implement system registration order, `MockTerrainProvider`, vehicle spawn, manual `EvaluateTick` motion (`tf.Position.X += 10f * (1f/60f)`), and phase ticks **as specified**.

**Tests required (names must match):**

- `TerrainClamping_RunToCompletion_ExitsZero`
- `TerrainClamping_Phase1_NoClampingOnFlatGround`
- `TerrainClamping_Phase2_SmoothingActiveOnRamp`
- `TerrainClamping_Phase3_JumpRejectionRejectsSpike`
- `TerrainClamping_Phase4_RecoverAfterAnomaly`

---

## 🧪 Testing Requirements

From repo root:

```powershell
dotnet test "FDP\Examples\Fdp.Examples.Scenarios.Tests\Fdp.Examples.Scenarios.Tests.csproj"
```

If you change kernel/orchestrator code paths, also run:

```powershell
dotnet test "FDP\Framework\FDP.Framework.Runner.Tests\FDP.Framework.Runner.Tests.csproj"
```

New tests must assert **behavior** (doctrine phase, clamping offsets, exit codes)—not only log strings or property existence. Follow `.dev-workstream/guides/CODE-STANDARDS.md`.

---

## 📊 Report Requirements

Answer the **Developer Insights** prompts (issues, weak points, extra design choices, edge cases, performance) as in prior DEM1 batches—see `DEV-LEAD-GUIDE.md` report section for the question list.

---

## 🎯 Success Criteria

Batch is **DONE** when:

- [ ] Task 1 and Task 2 implemented; DEBT-TRACKER rows for BATCH-06 addressed (update tracker rows to ✅ in the same PR if policy allows, or note in report).
- [ ] `DEM1-D006` and `DEM1-D007` implemented per `DEM1-TASK-DETAIL.md`.
- [ ] All tests in `Fdp.Examples.Scenarios.Tests` pass, including the eight named tests above.
- [ ] `.dev-workstream/reports/BATCH-06-REPORT.md` submitted with insights.

---

## ⚠️ Common Pitfalls

- **`MissionPhase` / `[InlineArray]`:** use the task detail’s `Span<MissionPhase>` pattern when mutating queue phases.
- **Terrain stack:** register systems in the **groups** listed in `DEM1-TASK-DETAIL.md`; `TerrainQuerySolverSystem` must receive `MockTerrainProvider` as specified.
- **Obsolete APIs:** codebase may warn on `MissionTrigger.ReachedDestination`; prefer `DoctrineFinished` (see `MissionDirectorSystem` / BS-1 debt) where writing new mission logic.

---

## 📚 Reference Materials

- **Tracker:** `docs/demos-1/DEM1-TASK-TRACKER.md`
- **Task detail:** `docs/demos-1/DEM1-TASK-DETAIL.md`
- **Design:** `docs/demos-1/DEM1-DESIGN.md`
