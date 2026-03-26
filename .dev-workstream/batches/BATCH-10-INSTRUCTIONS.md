# BATCH-10: Scenario lifecycle + DEM1-D009 Phase B + remaining debt

**Batch Number:** BATCH-10  
**Tasks:** P2 **DistributedTank / scenario native teardown** · **DEM1-D009 Phase B** (incremental) · P3 grid index-reuse hardening · P3 ImGui test isolation (if capacity)  
**Phase:** Safety + DEM1 Phase 5 continuation  
**Estimated Effort:** 14–20 hours  
**Priority:** HIGH  
**Dependencies:** BATCH-09 approved — see `.dev-workstream/reviews/BATCH-09-REVIEW.md`

---

## Onboarding

### Developer instructions

1. **Task 1 first** — fixes a real **native resource leak** on the **`fdp-demo-runner`** path for `distributedtank` (and any future `IDisposable` scenarios).  
2. Then advance **DEM1-D009 Phase B** in **small milestones** with tests each step.  
3. Pick up **Tasks 3–4** if Phase B needs to split across batches; document carry-over in **`BATCH-10-REPORT.md`**.

### Required reading

1. `.dev-workstream/guides/CODE-STANDARDS.md`
2. `.dev-workstream/reviews/BATCH-09-REVIEW.md`
3. `.dev-workstream/DEBT-TRACKER.md` — rows **Target BATCH-10** / **BATCH-10+**
4. `docs/demos-1/DEM1-TASK-DETAIL.md` § DEM1-D009
5. `docs/demos-1/DEM1-DESIGN.md` §6.4

### Report / questions

- `.dev-workstream/reports/BATCH-10-REPORT.md`
- `.dev-workstream/questions/BATCH-10-QUESTIONS.md` (if needed)

---

## Mandatory workflow

Task 1 → green `Fdp.Examples.Scenarios.Tests` + manual smoke: `fdp-demo-runner` with `distributedtank` (no lingering native warnings if you have diagnostics).  
Then Phase B slices → green tests after each slice.

---

## Tasks

### Task 1: [DEBT / Safety] Scenario teardown — `Dispose` vs `OnShutdown`

**Debt:** `.dev-workstream/DEBT-TRACKER.md` — BATCH-09 review (Target BATCH-10)

**Problem:** `DistributedTankScenario` tears down DDS participants and the Muscle kernel in **`IDisposable.Dispose`**. **`ScenarioSubsystem.Shutdown`** invokes **`IScenario.OnShutdown()`** only; **`Program.cs`** does not dispose the scenario.

**Pick one approach (document in report):**

- **A (preferred):** Implement **`OnShutdown()`** on `DistributedTankScenario` to call the same logic as **`Dispose`** (and make **`Dispose`** call a shared **`void ReleaseResources()`**), **or**  
- **B:** In **`ScenarioSubsystem.Shutdown`**, if `_scenario is IDisposable d`, call **`d.Dispose()`** after **`OnShutdown()`** (watch double-dispose; guard with flag).

**Tests:** Ensure **`DistributedTankScenarioPhaseATests`** still pass; add a test or comment that **`ScenarioTestHarness`** path remains safe (no double-free).

---

### Task 2: [FEATURE] DEM1-D009 — Phase B (incremental)

**Reference:** `DEM1-TASK-DETAIL.md` § DEM1-D009, `DEM1-DESIGN.md` §6.4, follow-ups listed in **`BATCH-09-REPORT.md`**.

**Goal (this batch — choose what fits; list the rest for BATCH-11):**

- Wire **both** DDS participants to **real topics** or the **same patterns** as `Fdp.Examples.NetworkDemo` (loopback Domain 0, explicit dispose already planned in Task 1).
- Add **`EntityLifecycleModule`** (or minimal ELM) on Brain and/or Muscle and **one observable assertion** (e.g. lifecycle state or spawn ack) with an **xUnit** test.
- Optionally start **ghosting** (`ReplicationLogicModule`) with **one** tank entity — only if time allows; do not skip Task 1.

**Registry / scenario:** Extend **`DistributedTankScenario`** and tests; keep **`DEM1-TASK-TRACKER`** **unchecked** for D009 until milestones in the task detail are met.

---

### Task 3: [DEBT] `LocalGridBuilderSystem` — index reuse / incremental safety

**Debt:** BATCH-09 review row (Target BATCH-10+)

**Goal:** If entity **count** is unchanged but **indices are recycled**, avoid corrupt incremental state — e.g. full rebuild when **generation** changes for a tracked index, or key **`_prevPositions`** by full **`Entity`** handle (if feasible), or document the assumption and add a regression test.

**Tests:** `FDP.Toolkit.Perception.Tests` / grid tests.

---

### Task 4: [DEBT] ImGui test parallel isolation

**Debt:** `FDP.Toolkit.ImGui.Tests` parallel native load conflict — **BD1-BATCH-04** row in `DEBT-TRACKER.md`

**Goal:** `xunit.runner.json` or collection attributes so ImGui tests do not run in parallel with other assemblies, **or** equivalent fix.

---

### Task 5: [OPTIONAL] ParallelStories + `RecordingModule.Blocking`

**Goal:** Replace direct **`AsyncRecorder`** usage with **`RecordingModule`** + **`Blocking: true`** in **`ParallelStoriesScenario`** to validate the BATCH-09 product path end-to-end — only if Tasks 1–2 are stable.

---

## Testing (minimum)

```powershell
dotnet test "FDP\Examples\Fdp.Examples.Scenarios.Tests\Fdp.Examples.Scenarios.Tests.csproj"
dotnet test "FDP\Toolkits\FDP.Toolkit.Perception.Tests\FDP.Toolkit.Perception.Tests.csproj"
```

Add Replay / NetworkDemo / Replication projects as touched.

---

## Success criteria

- [ ] Task 1: **No native teardown gap** for `distributedtank` on **CLI** and **harness**.  
- [ ] Task 2: At least **one** Phase B milestone with **tests**; report lists **remaining** Phase B work.  
- [ ] Tasks 3–4: Landed or explicitly deferred with lead-visible rationale.  
- [ ] `DEBT-TRACKER.md` updated by lead after review.  
- [ ] `BATCH-10-REPORT.md` submitted.

---

## Pitfalls

- **Double dispose:** If both **`OnShutdown`** and **`IDisposable`** run, guard with **`_released`** flag.  
- **DDS:** Keep **Domain 0 loopback**; dispose order: writers/readers before participants.  
- **Phase B scope:** Prefer **one proven milestone** over a half-finished full demo.
