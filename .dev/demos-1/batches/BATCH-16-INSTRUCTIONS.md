# BATCH-16: Tech debt burndown + DEM1-D010 doc / Latch 5 hardening (optional)

> **Batch ID collision:** **`FDP/.dev-workstream/batches/BATCH-16-INSTRUCTIONS.md`** targets **legacy `Fdp.Examples.UrbanCombat`** (behavior-control Phase 7), not this file.

**Batch Number:** BATCH-16 (**DEM1 / repo-root**)  
**Tasks:** Close **open P3/P2** rows from **`DEBT-TRACKER`** (prioritize items below) · optional **D010** normative doc sync · optional **real** “Mission Resumed” (HSM recovery) or **explicit Latch3** test  
**Phase:** Post–Phase 6 maintenance + specification accuracy  
**Estimated Effort:** 12–22 hours (depends on whether HSM recovery is in scope)  
**Priority:** MEDIUM–HIGH (doc drift confuses future agents; dead `ProjectReference` complicates dependency graph)  
**Dependencies:** BATCH-15 approved — `.dev-workstream/reviews/BATCH-15-REVIEW.md`

---

## Phase 0 — Tech debt (do **first**)

Complete **at least two** of the following (or equivalent **DEBT-TRACKER** rows), and record in **`BATCH-16-REPORT.md`**:

| # | Item | Source |
|---|------|--------|
| 1 | Remove **`Fdp.Examples.NetworkDemo`** **`ProjectReference`** from **`Fdp.Examples.Scenarios.csproj`** if no code uses it; verify full **`Fdp.Examples.Scenarios`** build + **`Fdp.Examples.Scenarios.Tests`**. | BATCH-15 review |
| 2 | Fix **CS8602** in **`UrbanCombatNewScenario.cs`** (HSM init / `TryGetDefinition` path ~line 800). | BATCH-15 review |
| 3 | **P3** RVO lateral (**BATCH-03**) **or** FastBTree **`Selector`** documentation (**BATCH-04**) — pick one row still **open** in **`DEBT-TRACKER`**. | Historical Examples debt |
| 4 | **P3** `MissionDirectorSystem` one-frame **`AssignDoctrineHashEvent`** delay (**BD1-BATCH-02** → BD1-BATCH-04) — document or unify. | Behavior toolkit |
| 5 | **P3** Consolidate **`DistributedTankScenario`** redundant class **`<summary>`** paragraphs (BATCH-14 review debt). | Documentation |

If a row is **blocked**, document owner and **retarget** in **`DEBT-TRACKER`**.

---

## Phase 1 — DEM1-D010 specification alignment (recommended)

**Goal:** **`DEM1-DESIGN.md` §6.5** latch table and **`DEM1-TASK-DETAIL.md` D010** pseudo-code match **`UrbanCombatNewScenario`** observables **or** the code is adjusted to match the docs (pick one coherent story).

| Normative text | Current code |
|----------------|---------------|
| Latch 1: `FireRequestEvent` | `WeaponChannel.ActiveAction == AimAndFire` |
| Latch 3: `HitEvent.HitEntity == insurgent` | `Health.Current < max` |
| Latch 5: APC `LocomotionChannel` FollowRoute / MoveTo | Log `"Mission Resumed"`; APC remains **Disabled** |

**Minimum:** Update **docs** to describe the **implemented** latches and note **Latch 5** as narrative / log milestone until HSM recovery exists.

**Stretch (optional):** Add **HSM** transition **Disabled → Cruising** on a **`RecoveryComplete`**-style event (or repair doctrine) and assert **FollowRoute** on APC before success **or** rename log / test to avoid implying movement.

---

## Phase 2 — Test hardening (optional)

- Add **`UrbanCombatNew_Latch3_InsurgentHit`** (assert **`LatchInsurgentHit`** or health drop) if docs retain a distinct Latch 3.  
- **`UrbanCombatNew_Latch4_InsurgentDies`:** optionally assert death **before tick 400** per TASK-DETAIL narrative.

---

## Testing

```powershell
dotnet test "FDP\Examples\Fdp.Examples.Scenarios.Tests\Fdp.Examples.Scenarios.Tests.csproj"
dotnet test "Bagira.Runner.Integration.Tests\Bagira.Runner.Integration.Tests.csproj"
```

(Add other projects if Phase 0 touches them.)

---

## Success criteria

- [ ] Phase 0: **≥2** debt items **closed** or **retargeted** with rationale.  
- [ ] Phase 1: **DESIGN** §6.5 and/or **TASK-DETAIL** D010 **no longer contradict** `UrbanCombatNewScenario` without an explicit “spec caveat” callout.  
- [ ] Phase 2: Optional — documented if skipped.  
- [ ] **`DEBT-TRACKER.md`** updated by lead after review.  
- [ ] **`BATCH-16-REPORT.md`** submitted.

---

## Pitfalls

- **HSM recovery** touches doctrine design — do not half-implement; prefer **doc honesty** if product has not signed off on new transitions.  
- Removing **`NetworkDemo`** reference may expose **transitive** dependency misuse elsewhere — run **build** for **Runner** + **Scenarios** together.
