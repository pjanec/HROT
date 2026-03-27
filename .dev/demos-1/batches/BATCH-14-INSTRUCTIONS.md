# BATCH-14: DEM1-D009 sign-off — design sync + regression hardening

**Batch Number:** BATCH-14  
**Tasks:** P2 **`DEM1-DESIGN.md`** §6.4 **D009 topology** · **`DistributedTankScenario`** docs + tests (**DDS loco consumption**, **Phase 3** milestone) · **`DEM1-TASK-DETAIL`** success list alignment · optional **one** P3 from **`DEBT-TRACKER`**  
**Phase:** Close **Phase 5 — Network Demo** as **normative** (no known drift between DESIGN / TASK-DETAIL / code / CI)  
**Estimated Effort:** 10–18 hours  
**Priority:** HIGH (design §6.4 still contradicts implemented Brain/Muscle story; BATCH-13 review gap on strict DDS-path assertion)  
**Dependencies:** BATCH-13 approved — see `.dev-workstream/reviews/BATCH-13-REVIEW.md`

**Not in this batch:** **DEM1-D010** (UrbanCombat new) — see **`BATCH-15-INSTRUCTIONS.md`**.

---

## Onboarding

### Developer instructions

1. **Task 1** — **`DEM1-DESIGN.md` §6.4** matches **`DEM1-TASK-DETAIL`** § D009 and **`DistributedTankScenario`** (Brain: ELM + `DemoLocomotionMsg`; Muscle: ELM + `ReplicationLogicModule` + `DemoTkbSetup.RegisterAll`, etc.).  
2. **Task 2** — Harden tests so **Phase 2** proves **`DemoLocomotionMsg`** was **consumed** on the Muscle path, not only velocity (see BATCH-13 review *“Gap (low): No dedicated test that `_locoMsgConsumed` / DDS sample was used”*).  
3. **Task 3** — Add an **explicit xUnit** milestone for **Phase 3** (tick **40**): Brain turret **`SimTransform`** tracks Brain hull within **±0.1 m** (already enforced inside **`EvaluateTick`** — surface as a named test + optional observable for clarity).  
4. **Task 4** — Scenario **file header / `<summary>`** paragraphs: remove any stale **“NavState injected directly on ghost at tick 20”** wording; single story = **`LocomotionChannel`** + **`DemoLocomotionMsg`** → Muscle poll → **`NavState`**.  
5. **Task 5** — **`DEM1-TASK-DETAIL.md`** § D009 **Success conditions** block includes every CI test you ship (including Phase 3 if new).  
6. **Task 6** — Optional: one **P3** row from **`DEBT-TRACKER.md`**.  
7. **`DEM1-TASK-TRACKER.md`** — Refresh D009 footnote when BATCH-14 completes (e.g. doc + DDS-path test + Phase 3 test named in TASK-DETAIL).

### Required reading

1. `.dev-workstream/guides/CODE-STANDARDS.md`  
2. `.dev-workstream/reviews/BATCH-13-REVIEW.md`  
3. `docs/demos-1/DEM1-TASK-DETAIL.md` § **DEM1-D009**  
4. `docs/demos-1/DEM1-DESIGN.md` § **6.4**  
5. `Fdp.Examples.Scenarios/Network/DistributedTankScenario.cs`  
6. `Fdp.Examples.Scenarios.Tests/ScenarioTests.cs` — **`DistributedTankScenarioPhaseATests`**  
7. `.dev-workstream/DEBT-TRACKER.md` — open rows  

### Report / questions

- `.dev-workstream/reports/BATCH-14-REPORT.md`

---

## Tasks

### Task 1: [DOCS] `DEM1-DESIGN.md` §6.4 — DEM1-D009 topology

**Debt:** `.dev-workstream/DEBT-TRACKER.md` — BATCH-13 review (target this batch)

**Goal:** Replace Brain/Muscle bullet list, phase table, and any **BehaviorToolkit** / Brain **ReplicationLogicModule** claims with the **implemented** story:

- Brain: ELM, manual hull + turret, **`EntityMasterTopic`**, **`DemoLocomotionMsg`**, **`LocomotionChannel`** on hull.  
- Muscle: ELM + **`ReplicationLogicModule`**, **`DemoTkbSetup.RegisterAll`**, kinematics module, ghost + promotion, **`NavState`** from loco message.  
- Short **“Why not RPL/BT on Brain”** pointer (link or one paragraph) matching **`DEM1-TASK-DETAIL`** architecture note.

**Cross-check:** Grep **`DEM1-DESIGN.md`** for **DistributedTank**, **D009**, **BehaviorToolkit**, **ReplicationLogicModule** in the Phase 5 section; fix table-of-contents anchors if headings change.

---

### Task 2: [CODE + TEST] Assert **`DemoLocomotionMsg`** consumption on Muscle

**Goal:** Not only **`SimVelocity.Linear.X > 0.1`**, but proof the Muscle node **read** a locomotion sample (closes the BATCH-13 review gap).

- Expose a **test-visible** flag (e.g. public **`bool LocoCommandReceivedViaDds { get; }`**) set when the same path currently sets **`_locoMsgConsumed`**, **or** assert an existing member if already visible.  
- Extend **`DistributedTank_Phase2_MuscleNodeMovesOnCommand`** (or add **`DistributedTank_Phase2_LocoMsgConsumedViaDds`**) so **both** velocity **and** DDS consumption hold by tick **25**.

---

### Task 3: [TEST] Phase 3 — turret tracks hull (tick 40)

**Goal:** Named test aligned with **`DEM1-TASK-DETAIL`** narrative (“Tick 40: Phase 3 — turret position tracks hull ±0.1”).

- Add **`DistributedTank_Phase3_BrainTurretTracksHull_AtTick40`** (or equivalent name matching project conventions).  
- **Max ticks** can be **41** if you only need to observe Phase 3; ensure harness **exit code** expectations match how **`ScenarioSubsystem`** treats early success vs full run (mirror patterns from other phase-scoped tests).  
- If the scenario does not yet expose a latch, add **`PhaseBTurretTracksHull`** (or reuse **`EvaluateTick`** order guarantees + snapshot fields set at tick **40**) so the test does not duplicate distance math.

---

### Task 4: [DOCS — CODE] `DistributedTankScenario` summary comments

- Update the **class `<summary>`** so **Phase B locomotion** paragraphs consistently describe **BATCH-13+** (`DemoLocomotionMsg`, poll before Muscle `Update`), not direct ghost **`NavState`** injection.  
- Align **constant / phase** comments with **`DEM1-TASK-DETAIL`** tick table (10 / 20 / 25 / 30 / 40 / 50).

---

### Task 5: [DOCS] `DEM1-TASK-DETAIL.md` § D009 — Success conditions

- Add the **Phase 3** test stanza to the fenced **Success conditions** block so DESIGN / TASK-DETAIL / test names match.  
- If Task 2 splits assertions into two tests, document **both**.

---

### Task 6: [OPTIONAL / DEBT] Single P3 row

Pick **one** from **`DEBT-TRACKER.md`**. Land code **or** doc **or** explicit retarget + rationale in **`BATCH-14-REPORT.md`**.

---

## Testing

```powershell
dotnet test "FDP\Examples\Fdp.Examples.Scenarios.Tests\Fdp.Examples.Scenarios.Tests.csproj"
```

Add other projects if Task 6 touches them.

---

## Success criteria

- [ ] **Task 1:** No contradiction between **DESIGN** §6.4, **TASK-DETAIL** D009, and **`DistributedTankScenario`**.  
- [ ] **Tasks 2–5:** DDS loco consumption covered in CI; Phase **3** has a **named** test; TASK-DETAIL success list updated; scenario headers accurate.  
- [ ] **Task 6:** Optional — documented if skipped.  
- [ ] **`DEM1-TASK-TRACKER.md`:** D009 row reflects BATCH-14 sign-off note.  
- [ ] **`DEBT-TRACKER.md`:** Updated by lead after review (close §6.4 drift row when Task 1 lands).  
- [ ] **`BATCH-14-REPORT.md`** submitted.

---

## Pitfalls

- **Do not** widen D009 into **D010** scope — UrbanCombat is **BATCH-15**.  
- **Do not** re-expand D009 product scope (e.g. add Brain **`ReplicationLogicModule`**) unless product explicitly changes the demo — this batch is **sign-off**, not redesign.  
- Early-stop tests must stay consistent with **`ScenarioTestHarness`** / **`ScenarioSubsystem`** semantics (success exit vs exception mapping).
