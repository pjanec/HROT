# BATCH-17: Examples coupling + remaining P3 burndown

**Batch Number:** BATCH-17 (**repo-root** — DEM1 / Examples hygiene)  
**Tasks:** **`TransformSyncSystem` / NetworkDemo decoupling** · close **open P3** rows (RVO, Selector doc, DistributedTank XML, MissionDirector, tests flake) · **optional** D010 Latch4 doc/test alignment  
**Phase:** Reduce cross-project coupling + documentation accuracy  
**Estimated Effort:** 10–20 hours  
**Priority:** MEDIUM–HIGH  
**Dependencies:** BATCH-16 (DEM1) approved — **`.dev-workstream/reviews/BATCH-16-REVIEW.md`**

> **Batch ID collision:** **`FDP/.dev-workstream/batches/BATCH-*`** may use the same numeric IDs for **behavior-control / legacy UrbanCombat** work — always check **which** `BATCH-*-INSTRUCTIONS.md** you are executing.

---

## Phase 0 — Tech debt (**do first**)

Complete **at least two** items; record in **`BATCH-17-REPORT.md`**:

| # | Item | Source |
|---|------|--------|
| 1 | **Decouple `Fdp.Examples.Scenarios` from `Fdp.Examples.NetworkDemo`:** move or duplicate **`TransformSyncSystem`** (and minimal deps) into **`FDP.Toolkit.Replication`**, **`Fdp.Examples.Common`**, or another **non–NetworkDemo** home; update **`TerrainClampingScenario`**; then remove **`ProjectReference`** if nothing else needs it. Verify **Scenarios** + **Runner** + **`Fdp.Examples.Scenarios.Tests`**. | DEBT-TRACKER (NetworkDemo row) |
| 2 | **`DistributedTankScenario`** class **`<summary>`:** consolidate redundant Phase B + Phase C locomotion paragraphs into one DDS narrative. | DEBT-TRACKER |
| 3 | **P3** RVO lateral (**BATCH-03**) **or** FastBTree **`Selector`** doc (**BATCH-04**) — pick one **open** row. | DEBT-TRACKER |
| 4 | **P3** **`MissionDirectorSystem`** one-frame **`AssignDoctrineHashEvent`** delay (**BD1-BATCH-04**) — document or unify. | DEBT-TRACKER |
| 5 | **`UrbanAmbushIntegrationTests` flake** (`ScenarioDirector_SpawnsExpectedEntityCount`): reproduce; consider assembly **`CollectionBehavior`**, teardown order, or isolation. | DEBT-TRACKER |

---

## Phase 1 — DEM1 polish (optional)

- **`DEM1-TASK-DETAIL` D010:** align **`UrbanCombatNew_Latch4_InsurgentDies`** stanza with tests (**before tick 400** vs **within budget**) **or** add a **non-brittle** tick observation (e.g. assert **`LastInsurgentAliveTick` < 400** if scenario exposes it).
- **`UrbanCombatNew_Latch3_InsurgentHit`:** add dedicated test only if product wants explicit Latch 3 signal in CI.

---

## Testing

```powershell
dotnet test "FDP\Examples\Fdp.Examples.Scenarios.Tests\Fdp.Examples.Scenarios.Tests.csproj"
dotnet test "FDP\Examples\Fdp.Examples.UrbanCombat.Tests\Fdp.Examples.UrbanCombat.Tests.csproj"
```

---

## Success criteria

- [ ] Phase 0: **≥2** **DEBT-TRACKER** rows **closed** or **retargeted** with rationale.  
- [ ] Phase 1: Optional — documented if skipped.  
- [ ] **`DEBT-TRACKER.md`** updated by lead after review.  
- [ ] **`BATCH-17-REPORT.md`** in **`.dev-workstream/reports/`**.
