# BATCH-14 Report

**Batch:** BATCH-14  
**Developer:** GitHub Copilot  
**Date:** 2026-03-27  
**Status:** Complete

---

## Task Completion

| Task | Status | Notes |
|------|--------|-------|
| Task 1 — `DEM1-DESIGN.md` §6.4 topology sync | ✅ Complete | Brain/Muscle bullets, phase table, and `ChildBlueprintDefinition` replaced; "Why not RPL/BT on Brain" note added |
| Task 2 — `LocoCommandReceivedViaDds` + test | ✅ Complete | New public property; new test `DistributedTank_Phase2_LocoMsgConsumedViaDds` |
| Task 3 — Phase 3 named test (tick 40) | ✅ Complete | `PhaseBTurretTracksHull` property + `DistributedTank_Phase3_BrainTurretTracksHull_AtTick40` test |
| Task 4 — Scenario summary comment cleanup | ✅ Complete | Phase B locomotion paragraph updated; stale "NavState injected directly" wording removed |
| Task 5 — `DEM1-TASK-DETAIL` §D009 success list | ✅ Complete | Phase 3 stanza + DDS consumption stanza added; existing stanza names aligned to actual test method names |
| Task 6 — Optional P3 debt | ✅ Retargeted | `TryTakeCreateAck` duplication row retargeted to `TwoAck-BATCH-05` with rationale |

---

## Testing Results

**Fdp.Examples.Scenarios.Tests:** 60 / 60 passed (58 existing + 2 new)

**New tests added:**
- `DistributedTankScenarioPhaseATests.DistributedTank_Phase2_LocoMsgConsumedViaDds`
- `DistributedTankScenarioPhaseATests.DistributedTank_Phase3_BrainTurretTracksHull_AtTick40`

---

## Developer Insights

**Q1: Issues encountered?**  
None. All changes were doc+code alignment with no behaviour modification. The `PhaseBTurretTracksHull` latch was already enforced inside `EvaluateTick` via `ScenarioFailureException`; surfacing it as a public bool required only a one-line set after the existing log statement.

**Q2: Weak points noticed?**  
The `Phase C — BATCH-13` paragraph in the scenario class `<summary>` is now partially redundant with the updated Phase B paragraph. It was intentionally retained as a batch-history note; future cleanup could consolidate all locomotion paragraphs into one authoritative description.

**Q3: Design decisions beyond instructions?**  
The `DEM1-TASK-DETAIL` success conditions block had two test names that did not match the actual method names in `ScenarioTests.cs` (`DistributedTank_RunToCompletion_ExitsZero` and `DistributedTank_Phase1_ELMHandshakeCompletesWithinTen_Ticks`). Task 5 was used as an opportunity to align all names to the real method names, matching the batch goal of "DESIGN / TASK-DETAIL / test names match."

**Q4: Edge cases discovered?**  
No surprises. Both Brain and Muscle start at the origin (0,0,0), so the turret-hull distance assertion at tick 40 is effectively zero — the test is structurally sound and would catch any accidental position mutation on either entity before tick 40.

**Q5: Performance concerns?**  
None — purely doc and latch-flag changes with no hot-path impact.

---

## Task 6 — Optional P3 Retarget

**Row:** `TwoAck-BATCH-03 | TryTakeCreateAck helper is duplicated across 3 runner integration test files`  
**Previous target:** `TwoAck-BATCH-04`  
**New target:** `TwoAck-BATCH-05`  
**Rationale:** The helper extraction touches SimHost and IOS wiring in three separate runner integration test files. BATCH-14 is a DEM1-D009 sign-off batch; importing unrelated runner test refactoring risks scope creep and merge conflicts with BATCH-15 (UrbanCombat). Retargeting to a dedicated TwoAck batch keeps it risk-free.

---

## Files Changed

| File | Change |
|------|--------|
| `docs/demos-1/DEM1-DESIGN.md` | §6.4 topology bullets, phase table, `ChildBlueprintDefinition`, "Why not RPL/BT" note |
| `FDP/Examples/Fdp.Examples.Scenarios/Network/DistributedTankScenario.cs` | `LocoCommandReceivedViaDds` property; `PhaseBTurretTracksHull` property; Phase B locomotion paragraph updated |
| `FDP/Examples/Fdp.Examples.Scenarios.Tests/ScenarioTests.cs` | 2 new tests; class `<summary>` updated |
| `docs/demos-1/DEM1-TASK-DETAIL.md` | §D009 success conditions — Phase 3 + DDS consumption stanzas; test name alignment |
| `docs/demos-1/DEM1-TASK-TRACKER.md` | D009 row — BATCH-14 sign-off footnote |
| `.dev-workstream/DEBT-TRACKER.md` | `TryTakeCreateAck` row retargeted to `TwoAck-BATCH-05` |

---

## Outstanding Issues / Next Steps

- [ ] **BATCH-15:** DEM1-D010 UrbanCombat New (Grand Integration) — see `BATCH-15-INSTRUCTIONS.md`
- [ ] **DEBT-TRACKER `TwoAck-BATCH-05`:** `TryTakeCreateAck` helper extraction
- [ ] Lead to close `DEM1-DESIGN.md §6.4 drift` row in `DEBT-TRACKER.md` after review
