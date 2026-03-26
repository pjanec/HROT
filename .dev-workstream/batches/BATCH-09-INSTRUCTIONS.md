# BATCH-09: Tech-debt burndown (grid + perception + recorder) + DEM1-D009 Phase A

**Batch Number:** BATCH-09  
**Tasks:** P3 `LocalGridBuilderSystem` · P3 perception scoped bus · P3 `RecordingModule` blocking · naming hygiene · **DEM1-D009** Phase A  
**Phase:** Performance/architecture debt + DEM1 Phase 5 kickoff  
**Estimated Effort:** 12–18 hours  
**Priority:** HIGH  
**Dependencies:** BATCH-08 approved — see `.dev-workstream/reviews/BATCH-08-REVIEW.md`

---

## Onboarding

### Developer instructions

Complete **Tasks 1–3 (debt)** first so backlog does not grow, then **Task 4** (quick), then **Task 5** (D009). If D009 is large, deliver Tasks 1–4 fully and document remaining D009 work in `BATCH-09-REPORT.md`.

### Required reading

1. `.dev-workstream/guides/CODE-STANDARDS.md`
2. `.dev-workstream/reviews/BATCH-08-REVIEW.md`
3. `.dev-workstream/DEBT-TRACKER.md` — open rows **Target Fix BATCH-09** / **BATCH-09+**
4. `docs/demos-1/DEM1-TASK-DETAIL.md` § DEM1-D009
5. `docs/demos-1/DEM1-DESIGN.md` §6.4 DistributedTank

### Report / questions

- `.dev-workstream/reports/BATCH-09-REPORT.md`
- `.dev-workstream/questions/BATCH-09-QUESTIONS.md` (if needed)

---

## Mandatory workflow

Sequence: Task 1 → green tests; Task 2 → green tests; Task 3 → green tests or documented partial; Task 4; Task 5 → green tests for delivered slice.

---

## Tasks

### Task 1: [DEBT] `LocalGridBuilderSystem` — incremental grid updates

**Debt:** `.dev-workstream/DEBT-TRACKER.md` — BATCH-06 review row (Target BATCH-09)

**Problem:** Dirty path still does full `Clear()` + re-insert on movement; scales poorly.

**Goal:** Move toward localized updates (per-cell or dirty-chunk insert/remove) without changing observable simulation results. Document behaviour and complexity in code comments where non-obvious.

**Tests:** Extend `FDP.Toolkit.Physics` / geographic tests that cover spatial hash / grid builder; run `dotnet test` for affected projects.

---

### Task 2: [DEBT] `AutonomousPerceptionModule` — scoped event consumption

**Debt:** `PerceptionScopedView.ConsumeEvents<T>` always reads `_scopedBus`; risk of empty reads for mixed event sources.

**Goal:** Clarify contract (XML + short design note in report) and/or implement a whitelist / dual-read path so future systems cannot silently miss world events — minimal change that preserves current demo behaviour.

**Tests:** Perception module or scenario tests that exercise scoped consumption; avoid regressions in `SensorGrid` / autonomy demos.

---

### Task 3: [DEBT / Product] Deterministic recording — `Blocking` option

**Debt:** `RecordingConfiguration` / `RecorderTickSystem` lack a blocking mode; Examples use raw `AsyncRecorder` in tight loops.

**Goal:** Add an opt-in **blocking** path (or configuration flag) on `RecordingModule` / tick pipeline so `ParallelStories`-class scenarios can use the module without delta drops — **or** document deferral with concrete API sketch if out of scope.

**Tests:** At minimum keep `Fdp.Examples.Scenarios.Tests` green; add a unit/integration test if a new flag lands.

---

### Task 4: [HYGIENE] Rename `DemoDoctrineIds.cs`

**File:** `FDP/Examples/Fdp.Examples.Scenarios/DemoDoctrineIds.cs` → `BehaviorValidationDoctrineIds.cs` (type already renamed in BATCH-08).

**Goal:** File name matches type; update `.csproj` if explicit compile items exist (usually automatic).

---

### Task 5: [FEATURE] DEM1-D009 — **Phase A only** (DistributedTank)

**Reference:** `DEM1-TASK-DETAIL.md` § DEM1-D009, `DEM1-DESIGN.md` §6.4

**Goal:** Smallest defensible slice:

- `DistributedTankScenario.cs` (or stub scenario) + shared constants if needed under `Fdp.Examples.Scenarios/Network/`
- Two `ModuleHostKernel` instances + **FastCycloneDDS loopback Domain 0** (explicit dispose)
- One **xUnit** test proving setup/teardown and a trivial assertion (e.g. both kernels initialize, or a single DDS round-trip / topic discovery — match what the codebase already supports in `Fdp.Examples.NetworkDemo`)
- `ScenarioNames.DistributedTank` + `ScenarioRegistry` entry **when** the scenario is runnable

**Out of scope for Phase A:** Full brain/muscle gameplay, ghosting, and all milestone ticks from the long-form task detail — list follow-ups in the report and leave `DEM1-TASK-TRACKER` **unchecked** for D009 until Phase B.

---

## Testing (minimum)

```powershell
dotnet test "FDP\Examples\Fdp.Examples.Scenarios.Tests\Fdp.Examples.Scenarios.Tests.csproj"
dotnet test "FDP\Toolkits\FDP.Toolkit.Physics.Tests\FDP.Toolkit.Physics.Tests.csproj"
```

Add projects touched by Tasks 1–3 (perception, replay, geographic).

---

## Success criteria

- [ ] Task 1: Measurable reduction in full-grid churn or documented incremental path with tests.  
- [ ] Task 2: Scoped consumption contract improved; tests green.  
- [ ] Task 3: Blocking recorder path landed **or** explicit deferral with design note.  
- [ ] Task 4: File rename complete.  
- [ ] Task 5: D009 Phase A delivered **or** clearly deferring with lead-visible notes.  
- [ ] `DEBT-TRACKER.md` updated by lead after review.  
- [ ] `BATCH-09-REPORT.md` submitted.

---

## Pitfalls

- **Grid:** Preserve determinism; avoid order-dependent iteration bugs.  
- **DDS:** Loopback only; dispose participants/writers/readers in `OnShutdown`.  
- **Recorder:** Blocking must not deadlock the main thread in production defaults — keep Examples/tests as the primary consumers.
