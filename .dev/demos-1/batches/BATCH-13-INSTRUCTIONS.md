# BATCH-13: DEM1-D009 spec alignment + controller/kernel hygiene

**Batch Number:** BATCH-13  
**Tasks:** P2 **DEM1-D009 vs task-detail alignment** · P3 **`SteppingTimeController`** first-frame **`DeltaTime`** · P3 **`ModuleHostKernel`** module disposal story · open **toolkit** debt (pick 1 if capacity)  
**Phase:** Spec compliance + platform hygiene  
**Estimated Effort:** 16–24 hours  
**Priority:** HIGH  
**Dependencies:** BATCH-12 approved — see `.dev-workstream/reviews/BATCH-12-REVIEW.md`

---

## Onboarding

### Developer instructions

1. **Task 1** is the **default main effort**: bring **`DistributedTankScenario`** (or the **normative `DEM1-TASK-DETAIL`**) to a **single coherent story** — either implement missing Brain toolkit / `DemoTkbSetup` / DDS locomotion path, **or** obtain lead sign-off to **narrow the written spec** and adjust **`DEM1-TASK-TRACKER`** expectations.  
2. **Tasks 2–3** reduce **footguns** for future batches; do not block Task 1 unless trivial.  
3. **Task 4** optional — pick one **P3** row from **`DEBT-TRACKER.md`** (e.g. FastBTree **Selector**, RVO) only if Task 1 finishes under budget.

### Required reading

1. `.dev-workstream/guides/CODE-STANDARDS.md`
2. `.dev-workstream/reviews/BATCH-12-REVIEW.md`
3. `.dev-workstream/reports/BATCH-12-REPORT.md` — “Outstanding Issues / Carry-over”
4. `.dev-workstream/DEBT-TRACKER.md` — **Target BATCH-13**
5. `docs/demos-1/DEM1-TASK-DETAIL.md` § DEM1-D009, `docs/demos-1/DEM1-DESIGN.md` §6.4
6. `Fdp.Examples.NetworkDemo` — `DemoLocomotionMsg` / translator patterns

### Report / questions

- `.dev-workstream/reports/BATCH-13-REPORT.md`
- `.dev-workstream/questions/BATCH-13-QUESTIONS.md` (if needed)

---

## Mandatory workflow

Task 1 design note in **report** (implement vs trim spec) → code + tests → Tasks 2–3 → optional Task 4.

---

## Tasks

### Task 1: [FEATURE / SPEC] `DEM1-D009` — align with `DEM1-TASK-DETAIL` **or** revise the doc

**Debt:** `.dev-workstream/DEBT-TRACKER.md` — BATCH-12 review (Target BATCH-13)

**Minimum if implementing:**

- **`DemoTkbSetup.RegisterAll`** (or documented equivalent parity).  
- **Brain node:** register **`ReplicationLogicModule`** and/or **`BehaviorToolkit`** as required by **§D009** (scope to what the demo needs; do not fork a second UrbanCombat).  
- **Tick 20:** inject **`LocomotionChannel`** (or spec-approved channel) on the **Brain hull** and propagate the command to the **Muscle** ghost via **`DemoLocomotionMsg`** + Cyclone (loopback Domain 0), replacing **only-Muscle `NavState` mutation** where the spec mandates Brain-origin commands.  
- **Tests:** Match **`DEM1-TASK-DETAIL`** success conditions; extend **`ScenarioTests`** accordingly.

**Minimum if trimming spec:**

- Edit **`DEM1-TASK-DETAIL.md`** § D009 (and design cross-links) to describe the **harness** actually checked in; get **lead** acknowledgment in **`BATCH-13-REPORT.md`**.  
- Then **`DEM1-TASK-TRACKER`** may move to **[x]** only when the **edited** success conditions are met.

---

### Task 2: [DEBT] `SteppingTimeController` — first-frame `DeltaTime`

**Debt:** BATCH-12 report (Target BATCH-13+)

**Goal:** Ensure **`SeedState`** / constructor behaviour is **documented** or **fixed** so **`ModuleHostKernel.Update()`** without a prior **`Step()`** does not silently use **`DeltaTime = 0`**. Add a **unit test** if behaviour changes.

---

### Task 3: [DEBT] `ModuleHostKernel` — **`IDisposable`** modules

**Debt:** BATCH-12 report (Target BATCH-13+)

**Goal:** Either **dispose registered `IEcsModule` instances** that implement **`IDisposable`** from **`ModuleHostKernel.Dispose`**, **or** document the **contract** in **`ModuleHostKernel`** XML that hosts **must** dispose modules / use **`IScenario.OnShutdown`**. Prefer the option with the smallest blast radius; list exceptions in the report.

---

### Task 4: [OPTIONAL] Single P3 item from **DEBT-TRACKER**

**Examples:** `FastBTree` **Selector** re-evaluation doc, RVO lateral bias, **`MissionDirectorSystem`** one-frame doctrine delay — pick **one** row and close or retarget.

---

## Testing (minimum)

```powershell
dotnet test "FDP\Examples\Fdp.Examples.Scenarios.Tests\Fdp.Examples.Scenarios.Tests.csproj"
dotnet test "FDP\Toolkits\FDP.Toolkit.Time.Tests\FDP.Toolkit.Time.Tests.csproj"
```

Add **`ModuleHost.Core`** tests if Task 3 touches **`ModuleHostKernel`**.

---

## Success criteria

- [ ] Task 1: **Implement-or-trim** decision recorded; **D009** tracker state matches reality.  
- [ ] Tasks 2–3: Landed **or** explicitly deferred with rationale.  
- [ ] Task 4: Optional.  
- [ ] `DEBT-TRACKER.md` updated by lead after review.  
- [ ] `BATCH-13-REPORT.md` submitted.

---

## Pitfalls

- **DDS:** Writers/readers before participants; same Domain 0 loopback discipline.  
- **Do not** mark **D009** complete on the tracker while the **written** task detail still claims Brain toolkits / topics that the scenario does not implement.
