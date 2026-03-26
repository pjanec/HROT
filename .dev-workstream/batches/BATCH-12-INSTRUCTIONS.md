# BATCH-12: Obsolete kernel API cleanup + DEM1-D009 Phase B (gameplay slice) + optional replay

**Batch Number:** BATCH-12  
**Tasks:** P3 **`ModuleHostKernel.Update(float)`** removal in scenarios · **DEM1-D009** (toolkits, TKB, loco/turret milestones) · optional **ParallelStories** + **`RecordingModule.Blocking`** · doc polish **`LocalGridBuilderSystem`**  
**Phase:** Hygiene + DEM1 Phase 5  
**Estimated Effort:** 18–26 hours  
**Priority:** HIGH  
**Dependencies:** BATCH-11 approved — see `.dev-workstream/reviews/BATCH-11-REVIEW.md`

---

## Onboarding

### Developer instructions

1. **Task 1** first — eliminates **CS0618** noise and aligns Muscle ticking with the supported **`ModuleHostKernel`** API.  
2. **Task 2** is the main **DEM1-D009** push; deliver **tested milestones** (e.g. tick 25 velocity, tick 50 split authority) per **`DEM1-TASK-DETAIL.md`**, or document a thinner slice plus BATCH-13 scope.  
3. **Tasks 3–4** when time allows.

### Required reading

1. `.dev-workstream/guides/CODE-STANDARDS.md`
2. `.dev-workstream/reviews/BATCH-11-REVIEW.md`
3. `.dev-workstream/reports/BATCH-11-REPORT.md` — “Remaining Phase B work”
4. `.dev-workstream/DEBT-TRACKER.md` — **Target BATCH-12** / **BATCH-12+**
5. `docs/demos-1/DEM1-TASK-DETAIL.md` § DEM1-D009, `docs/demos-1/DEM1-DESIGN.md` §6.4

### Report / questions

- `.dev-workstream/reports/BATCH-12-REPORT.md`
- `.dev-workstream/questions/BATCH-12-QUESTIONS.md` (if needed)

---

## Mandatory workflow

Task 1 → build warning-free for touched projects → Task 2 → `Fdp.Examples.Scenarios.Tests` green → optional tasks.

---

## Tasks

### Task 1: [DEBT] Replace `ModuleHostKernel.Update(float)` in `DistributedTankScenario` (and scan)

**Debt:** `.dev-workstream/DEBT-TRACKER.md` — BATCH-11 review (Target BATCH-12)

**Goal:** Use **`ModuleHostKernel.Update()`** (no legacy float argument) so **`SteppingTimeController`** remains the single source of delta. Grep the repo for **`Update(`** on **`ModuleHostKernel`** with a float literal; fix call sites in scope.

**Tests:** **`DistributedTank`** scenario tests must stay **56/56** (or grow).

---

### Task 2: [FEATURE] DEM1-D009 — Phase B continuation

**Reference:** `DEM1-TASK-DETAIL.md` § DEM1-D009 (Brain/Muscle toolkits, **`DemoTkbSetup.RegisterAll`**, ticks **20–50**).

**Minimum intent:**

- Register real TKB data (**`DemoTkbSetup`** or equivalent) so blueprint/ghost promotion paths are exercisable where the spec requires them.  
- Add **CarKinem** / **Behavior** (or scoped subsets) on the appropriate node per design — avoid duplicating half of UrbanCombat if incremental delivery is needed; **document** what is deferred.  
- **Locomotion:** Brain command → observable Muscle motion (e.g. **`SimVelocity`**) by the specified tick.  
- **Turret / split authority:** align with **§6.4**; add tests matching **`DEM1-TASK-DETAIL`** success conditions **as they become true**.

**Only** mark **`DEM1-TASK-TRACKER` → D009** **\[x\]** when **all** success conditions in the task detail are met (or the lead agrees to trim the spec in writing).

---

### Task 3: [OPTIONAL] `ParallelStoriesScenario` + `RecordingModule` + `Blocking: true`

**Goal:** Replace direct **`AsyncRecorder`** with **`RecordingModule`** using **`RecordingConfiguration { Blocking = true, ... }`**, keep deterministic replay tests passing, update scenario XML/comments.

---

### Task 4: [OPTIONAL] `LocalGridBuilderSystem` XML — `_liveByIndex`

**Goal:** Extend the class summary to mention **stale-slot eviction** on index recycle (BATCH-11).

---

## Testing (minimum)

```powershell
dotnet test "FDP\Examples\Fdp.Examples.Scenarios.Tests\Fdp.Examples.Scenarios.Tests.csproj"
dotnet test "FDP\Toolkits\FDP.Toolkit.Replication.Tests\FDP.Toolkit.Replication.Tests.csproj"
```

Add **`FDP.Toolkit.CarKinem.Tests`**, **`FDP.Toolkit.Behavior`** tests, or **`Fdp.Examples.NetworkDemo.Tests`** as touched.

---

## Success criteria

- [ ] Task 1: No **CS0618** from **`ModuleHostKernel.Update(float)`** in scenario / touched code.  
- [ ] Task 2: Measurable progress toward full **D009** with tests; report lists any **BATCH-13** carry-over.  
- [ ] Tasks 3–4: Optional per capacity.  
- [ ] `DEBT-TRACKER.md` updated by lead after review.  
- [ ] `BATCH-12-REPORT.md` submitted.

---

## Pitfalls

- **Time controller:** After removing **`Update(float)`**, confirm Muscle and Brain remain **lock-step** in **`EvaluateTick`**.  
- **DDS / replication:** Prefer existing **`Fdp.Examples.NetworkDemo`** translator patterns over one-off hacks unless the batch report documents why.  
- **Authority:** Split brain/muscle ownership must match **Replication** rules already used in production demos.
