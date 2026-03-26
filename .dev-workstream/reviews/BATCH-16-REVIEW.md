# BATCH-16 Review (DEM1 / repo-root)

**Batch:** BATCH-16 — **`.dev-workstream/batches/BATCH-16-INSTRUCTIONS.md`** (tech debt + D010 normative docs)  
**Reviewer:** Development Lead  
**Date:** 2026-03-27  
**Report:** **`.dev-workstream/reports/BATCH-16-REPORT.md`**  
**Status:** **APPROVED** — Phase 0 minimum met; Phase 1 satisfies success criteria; Phase 2 correctly deferred.

**Note:** **`FDP/.dev-workstream/batches/BATCH-16-INSTRUCTIONS.md`** is a *different* stream (legacy UrbanCombat Phase 7). Disambiguate by path when filing reports.

---

## Summary

Validated the **repo-root** report against **source** and **`dotnet test`** on **`Fdp.Examples.Scenarios.Tests`**: **65 / 65** passed.

---

## Phase 0 — Tech debt

| Instruction item | Verdict |
|------------------|---------|
| **1 — Remove `NetworkDemo` reference** | **Retargeted (correct).** **`TerrainClampingScenario.cs`** imports **`Fdp.Examples.NetworkDemo.Systems`** and constructs **`TransformSyncSystem`** — reference is **not** dead. **`DEBT-TRACKER`** row rewritten accordingly. Matches investigation in report. |
| **2 — CS8602 `UrbanCombatNewScenario`** | **Done.** **`TryGetDefinition`** block now requires **`convoyDef.HsmDefinition != null`** before dereferencing **`Header.StructureHash`** (lines 797–804). |

**Minimum “≥2 items”:** satisfied via **(1) retarget with rationale** + **(2) code fix**.

**Not done (optional within Phase 0 table):** Items **3–5** (RVO / Selector doc, **`MissionDirectorSystem`** delay, **`DistributedTankScenario`** summary consolidation) — acceptable given “at least two”; carry to **BATCH-17**.

---

## Phase 1 — DEM1-D010 specification alignment

**Verified in tree:**

- **`docs/demos-1/DEM1-DESIGN.md` §6.5** latch table: **`WeaponChannel`**, health-based hit, **`!IsAlive`**, **`Mission Resumed`** log with **explicit spec notes** for original vs implemented observables and Latch 5 caveat.
- **`docs/demos-1/DEM1-TASK-DETAIL.md` D010:** pseudo-code comments and **Success conditions** align with the above; Latch 5 clarifies log vs APC loco.

**Design intent:** Normative text now describes **`UrbanCombatNewScenario`** honestly; caveats preserve traceability for future HSM recovery or event-bus–based latches.

---

## Phase 2 — Optional tests

Skipped per instructions — rationale in report is reasonable (cascade coverage; tick-400 brittleness).

**Residual doc vs test gap:** **`DEM1-TASK-DETAIL`** still says Latch4 test outcome **“before tick 400”** while **`UrbanCombatNew_Latch4_InsurgentDies`** only asserts **`LatchInsurgentKilled`** + **exit 0** within **maxTicks 600** — no **tick &lt; 400** probe. **P3** follow-up in **`DEBT-TRACKER`** / **BATCH-17** (tighten doc **or** add soft bound assertion).

---

## Tests

**`Fdp.Examples.Scenarios.Tests`:** **65/65** — regression suite still green after doc + nullability change.

**What still matters:** End-to-end **`UrbanCombatNew_RunToCompletion_ExitsZero`** remains the primary safety net; per-latch tests still meaningful; optional hardening remains optional.

---

## Suggested commit message

```
docs(dem1): align D010 latches with UrbanCombatNewScenario; fix HSM init null guard

- DEM1-DESIGN §6.5 + DEM1-TASK-DETAIL D010: observables + spec caveats (Latch 1/3/5)
- UrbanCombatNewScenario: guard convoyDef.HsmDefinition before HSM brain init
- DEBT-TRACKER: NetworkDemo ref retarget (TerrainClamping uses TransformSyncSystem)
```

---

## Follow-ups

**`.dev-workstream/batches/BATCH-17-INSTRUCTIONS.md`** — updated after this review ( **`TransformSyncSystem`** decoupling, remaining Phase-0 picks, Latch4 doc/test alignment).
