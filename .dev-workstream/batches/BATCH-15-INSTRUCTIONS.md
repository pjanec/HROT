# BATCH-15: DEM1-D010 — UrbanCombat (new) foundation

**Batch Number:** BATCH-15  
**Tasks:** **DEM1-D010** UrbanCombat new — **UrbanCombatNewScenario** (toolkits, road graph, entity plan, sequential latches, registry, tests) per **`DEM1-TASK-DETAIL`** and **`DEM1-DESIGN`** §6.5  
**Phase:** Phase 6 — Grand Integration Demo (may require **follow-up batches** if 14-entity / toolkit work spills)  
**Estimated Effort:** 16–28 hours  
**Priority:** HIGH after Phase 5 is signed off  
**Dependencies:** **BATCH-14** approved — `.dev-workstream/reviews/BATCH-14-REVIEW.md`

---

## Phase 0 — Tech debt burndown (do **first**)

Pick **at least one** open row from **`.dev-workstream/DEBT-TRACKER.md`** so debt does not pile behind D010 (report which row(s) in **`BATCH-15-REPORT.md`**):

| Suggested order | Row (tracker) | Notes |
|-----------------|-----------------|-------|
| 1 | **P3** `TryTakeCreateAck` → **TwoAck-BATCH-05** | Extract helper to **`RunnerTestHelpers`** (or equivalent); touches runner integration tests only. |
| 2 | **P3** RVO lateral (**BATCH-03** / target BATCH-05) **or** FastBTree **Selector** doc (**BATCH-04** / BATCH-05) | Choose whichever is smaller after scoping. |
| 3 | **P3** `MissionDirectorSystem` one-frame delay (**BD1-BATCH-02** → BD1-BATCH-04) | Document or unify with **`MissionAdapterSystem`**. |
| 4 | **P2** OC1-BATCH-03 items | Only if this developer owns IG/Map tooling; otherwise **skip** and note. |

If Phase 0 is **blocked**, document the blocker and still proceed to **Task 4a** — do not defer the entire batch.

---

## Onboarding

### Developer instructions

1. **Phase 0** — at least one debt row (above).  
2. Complete **4a–4f** in order where possible: scenario shell → toolkit registration → road graph → spawns → latch machine → registry + tests.  
3. **`4d`** may spill to BATCH-16 if **Common** lacks self-contained templates — use structured stubs + report rather than blocking **4a–4c** and **4e** skeleton.

### Required reading

1. `.dev-workstream/guides/CODE-STANDARDS.md`  
2. `docs/demos-1/DEM1-TASK-DETAIL.md` § **DEM1-D010**  
3. `docs/demos-1/DEM1-DESIGN.md` § **6.5**  
4. `docs/demos-1/DEM1-TASK-TRACKER.md` — Phase 6  
5. **Pattern reference (read-only):** legacy **`Fdp.Examples.UrbanCombat`** — copy **ideas**, not project references  

### Report / questions

- `.dev-workstream/reports/BATCH-15-REPORT.md`

---

## Tasks

### Task 4a — Scenario type and location

- Add **`Fdp.Examples.Scenarios/Integrated/UrbanCombatNewScenario.cs`** implementing **`IScenario`**.  
- **`ScenarioName`** must use **`ScenarioNames.UrbanCombat`** (`"urbancombat"`).  
- Mirror bootstrap style of other multi-toolkit scenarios. **Do not** add a **csproj** reference to **`Fdp.Examples.UrbanCombat`**.

### Task 4b — Toolkit registration (phase-correct order)

- Register **all** toolkits required by §6.5: **Behavior**, **CarKinem**, **Navigation**, **Perception**, **Physics**, **Combat** (and any shared modules other integrated demos use — align with **TASK-DETAIL** “Register ALL toolkits in phase-correct order”).  
- Document order in a short header comment or **`RegisterToolkits`** helper.

### Task 4c — Road network

- Call **`DemoRoadGraphFactory.CreateCityIntersection()`** so the world owns a minimal 4-way **`RoadNetworkBlob`**.

### Task 4d — Entity spawn plan (14 entities)

Implement **or** structure with **`// DEM1-D010:`** comments and stable entity IDs:

| Count | Role | TKB | Notes |
|------:|------|-----|-------|
| 5 | CivilianPedestrian | 1001 | background |
| 3 | CivilianCar | 1002 | background |
| 1 | MilitaryAPC | 2001 | `ConvoyEscort` HSM, northbound |
| 4 | InfantrySoldier | 2002 | embarked in APC |
| 1 | Insurgent | 2003 | Ambush BTree, **TargetMemory** pre-seeded to APC |

If blocked, land **`UrbanCombatNewDirectorSetup`** (or equivalent) stubs with actionable **`throw`** messages and document gaps in **`BATCH-15-REPORT.md`**.

### Task 4e — Sequential latches + 600-tick budget

Per **TASK-DETAIL** / §6.5 table:

- Latches: **AmbushFired** → **ApcHalted** → **InsurgentHit** → **InsurgentKilled** → **MissionResumed**.  
- **`currentTick > 600`** without completion → **`ScenarioFailureException`** with latch diagnostics (match TASK-DETAIL template).  
- Deterministic **60 Hz** via **`ScenarioSubsystem`** / **`SteppingTimeController`** — no ad-hoc time unless subsystem already provides it.

### Task 4f — Registry + tests

- Add **`ScenarioNames.UrbanCombat`** branch in **`ScenarioRegistry.Create`** → **`new UrbanCombatNewScenario()`**.  
- Tests per **TASK-DETAIL** **Success conditions**, at minimum **`UrbanCombatNew_RunToCompletion_ExitsZero`** when feasible. Use **`Skip`** only with explicit debt in report + tracker.

**Cross-cutting:** Respect **TASK-DETAIL** log line expectations where **`ScenarioSubsystem`** already emits them.

---

## Testing

```powershell
dotnet test "FDP\Examples\Fdp.Examples.Scenarios.Tests\Fdp.Examples.Scenarios.Tests.csproj"
```

---

## Success criteria

- [ ] **Phase 0:** At least one **DEBT-TRACKER** row closed, retargeted with rationale, or explicitly blocked-with-owner.  
- [ ] **4a–4f:** Scenario, road graph hook, toolkit list, latch + timeout structure, **`ScenarioRegistry`** entry, tests as feasible.  
- [ ] **`DEM1-TASK-TRACKER.md`:** D010 updated (notes if partial; **[x]** only when TASK-DETAIL success tests are green).  
- [ ] **`DEBT-TRACKER.md`:** Updated by lead after review.  
- [ ] **`BATCH-15-REPORT.md`** submitted.

---

## Pitfalls

- **Do not** reference legacy **`Fdp.Examples.UrbanCombat`** from **`Fdp.Examples.Scenarios`** — copy patterns only (**DESIGN** legacy boundary).  
- **`ScenarioNames.UrbanCombat`** is the sole CLI key for this demo.  
- Do not register a scenario that **always fails** in production CLI — use stubs + **Skip** + debt until a minimal success path exists.
