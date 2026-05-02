# BATCH-17 Review (Lead)

**Batch:** BATCH-17 — repo-root DEM1 / Examples coupling + P3 burndown  
**Report:** `.dev-workstream/reports/BATCH-17-REPORT.md`  
**Date:** 2026-03-27  
**Verdict:** **Approved** — implementation matches the report; Phase 0 success criteria met; Phase 1 completed as documented.

---

## 1. Tasks vs instructions

| Instruction | Implemented? | Notes |
|-------------|--------------|-------|
| **P0 — Item 1:** Decouple Scenarios from NetworkDemo (`TransformSyncSystem`) | **Yes** | `Fdp.Examples.Common/Systems/TransformSyncSystem.cs` added (`Fdp.Examples.Common.Systems`); `TerrainClampingScenario` uses that namespace; `Fdp.Examples.Scenarios.csproj` no longer references NetworkDemo; direct `ModuleHost.Network.Cyclone` reference restores types that were only transitive via NetworkDemo — correct fix. |
| **P0 — Item 2:** `DistributedTankScenario` summary consolidation | **Yes** | Redundant Phase C paragraph removed; Phase B paragraph carries “one-tick DDS latency” wording. |
| **P0 — Item 3:** RVO **or** FastBTree Selector doc (pick one) | **Explicitly deferred** | Allowed: Phase 0 only requires **≥2** items; the batch delivered **four** P0 items without item 3. Report and debt tracker updated so BATCH-03/BATCH-04 P3 rows stay open. |
| **P0 — Item 4:** `MissionDirectorSystem` one-frame delay | **Yes** | Class XML documents BD1-BATCH-02 behaviour, group ordering, and test workaround. |
| **P0 — Item 5:** UrbanCombat integration flake | **Yes** | `[Collection("SerialTests")]` on `UrbanAmbushIntegrationTests` and `BlueprintTests` with clear `<remarks>`. |
| **P1 — D010 Latch4 doc** | **Yes** | `DEM1-TASK-DETAIL` aligned with tests (600-tick budget; no brittle tick-400 assert). |

**Minor report nit:** Item 4 cites “BD1-BATCH-02” in one narrative line; the debt row target was **BD1-BATCH-04** — substance is correct.

---

## 2. Design alignment

- **Decoupling:** Matches BATCH-17 design intent (Scenarios must not depend on NetworkDemo). Duplicating `TransformSyncSystem` into Common is a reasonable trade-off vs forcing NetworkDemo → Common (avoids awkward dependency while keeping NetworkDemo self-contained). **Follow-on:** two copies can drift; see debt tracker.
- **Latch 4:** Task detail now matches **what CI asserts**. **`DEM1-DESIGN.md` §6.5** latch table still lists InsurgentKilled **“≤tick 400”** as a *design window* while tests only enforce latch + exit under **maxTicks 600**. That is a **doc split**, not a code bug — logged as opportunistic debt for a one-line caveat or table tweak.

---

## 3. Tests — do they check what matters?

- **`Fdp.Examples.Scenarios.Tests`:** **65 / 65** passed (local run). Covers TerrainClamping and the rest of the scenario suite after the project-reference change — the right safety net for Item 1.
- **`Fdp.Examples.UrbanCombat.Tests`:** **29 / 29** passed. The flake fix targets **process-wide component registration** under parallel xUnit; serialising `HeadlessDemoApp` users is the right lever. `ScenarioDirector_SpawnsExpectedEntityCount` still asserts the meaningful invariant (14 entities with `SimTransform` after `SetupAmbushScenario`); the fix addresses **why** that count was non-deterministic, not the assertion shape.

`Fdp.Examples.Runner` was not rebuilt in this review session (transient CycloneDDS / file-lock noise on the agent host); Scenarios references compile via the test project build path. Recommend a clean CI or local `dotnet build` on Runner when the tree is quiet.

---

## 4. Suggested commit message

```
feat(examples): BATCH-17 decouple Scenarios from NetworkDemo, doc and test hygiene

- Add TransformSyncSystem to Fdp.Examples.Common; TerrainClamping uses Common
- Drop NetworkDemo from Scenarios; add ModuleHost.Network.Cyclone ref for D009
- Consolidate DistributedTankScenario class summary (Phase B/C DDS loco)
- Document MissionDirectorSystem one-frame behavior delay (BD1-BATCH-02)
- Serialise UrbanAmbushIntegrationTests and BlueprintTests (HeadlessDemoApp)
- Align DEM1-TASK-DETAIL D010 Latch4 with 600-tick test contract
```

---

## 5. Further batches?

**No dedicated BATCH-18 is required for DEM1 closure** if the goal was Examples coupling + the BATCH-16 follow-ups: those are done. Remaining work is **opportunistic P3** (RVO, Selector doc, TransformSync duplication, DESIGN latch table caveat, batch-ID naming) and unrelated open rows elsewhere in `DEBT-TRACKER.md` — pick up in normal work or a small “debt burndown” batch only if the team wants to burn the BATCH-03/BATCH-04 rows next.
