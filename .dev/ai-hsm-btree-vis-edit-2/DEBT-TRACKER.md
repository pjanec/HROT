# BTree / HSM Visual Editing — Technical Debt Tracker

> Deferred issues found during implementation / review. **P1 never enters this tracker** — it becomes Corrective Task 0 of the next batch. Track P2/P3 here; **do not delete resolved rows** (mark ✅/RESOLVED in place).
> Companions: [TASK-TRACKER.md](./TASK-TRACKER.md) · [TASK-DETAIL.md](./TASK-DETAIL.md) · design [docs/blueprints/BTree_HSM_Editor_State_And_Forward_Plan.md](../../docs/blueprints/BTree_HSM_Editor_State_And_Forward_Plan.md)

| ID | Title | Priority | Origin | Description / Issue | Status / Resolution |
|---|---|---|---|---|---|
| VE-DEBT-001 | HSM-state 4-slot param binding (= DEBT-BF-04) | P2 | blueprint-finalize/BATCH-BB1A review | BB1's type-filtered param-binding picker + Promote covers HSM **transitions/globals only, NOT states**. An HSM state hosts 4 action slots (Entry/Exit/Activity/Timer); the "one DTO → one variable" model needs a per-slot extension. **Needs an architect design call** — not an autonomous guess. Blocks `REVIEW-BB1(HSM)` (NOT the HSM authoring tasks HS-01..08). Cross-ref: `.dev/blueprint-finalize/DEBT-TRACKER.md` DEBT-BF-04. | OPEN — design call (NotebookLM/architect) before/at the HSM visual pass |

---

## Notes

- **Pre-existing test failures** (not ours; do not chase): `Hrot.Blueprints.Tests` DEBT-006 set + flaky sub-150ns WhenNode perf (DEBT-014). Keep any failing set a *subset* (0 new) in our test projects.
- **Out of scope (Phase 2 — separate thread):** runtime debug overlays, breakpoints, stepping, heatmaps, trace-timeline population, `GetCurrentStateSnapshot()` kernel wiring. Do not pull these into the authoring tasks.
- **Cross-workstream, do not touch:** the merged `main-toolbar-1` (asset browser, create-new, Save, toolbar/menus) and committed `blueprint-finalize/BB1` files — except the explicit composition-root wiring named in a task.

## Legend
- **P1** Critical — never tracked here; becomes Corrective Task 0 next batch.
- **P2** Should fix — tracked, assigned when scheduled.
- **P3** Nice to have — tracked, best-effort.
- Status: OPEN / RESOLVED (do not delete resolved rows).
