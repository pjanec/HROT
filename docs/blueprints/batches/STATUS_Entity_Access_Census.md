<!--STATUS
state: LIVE — started; N-1…N-8 not yet done
updated: 2026-11-12
current-answer: "Carried from the paused threaded POC 4" (the coordinator should count with it) · then the items
related-designs:
  - ../../DESIGN_Parallel_Simulation.md — OWNS this: §18 per-entity parallelism
  - HANDOFF_Coordinator_To_Backend_Entity_Access_Census.md — the handoff (dispatched at cf8dbebab)
  - STATUS_Threaded_POC_4.md (on exp/threaded-poc-4) — the paused threaded POC 4 (what is built, measured, and the plan to resume)
  - STATUS_Threaded_Analysis_Instruments.md — A-1 the access tracer this extends per entity; A-2 the reach tool
-->

# STATUS — the cross-entity access census (branch `exp/entity-census`)

**Branch:** `exp/entity-census` from `exp/perf@5add60bbe` with `exp/threaded-analysis@253b0f00b` merged
(the A-1 tracer, its hooks, `scripts/access-trace-*.py`, the A-2 reach tool). Every instrument opt-in, default OFF.

## ⭐ Carried from the paused threaded POC 4 — for the coordinator to count with *(user: "mention those in your report so coordinator counts with that")*

The lane paused [`exp/threaded-poc-4`](https://github.com/pjanec/HROT/blob/exp/threaded-poc-4/docs/blueprints/batches/STATUS_Threaded_POC_4.md) (CE-4448) part-way. What it holds:

| what | state |
|---|---|
| the merge of `exp/perf` + `coordinator` into the POC 3 shape (one read path; tracer + mutation detector kept) | ✅ built; two mode-3 sessions at 1 000 men green, 0 faults · ⏳ the rails not run |
| F-2 ① a `StationMode` + edit gate per WORLD (`SharedApplicationBootstrapper.InstallStation`) | ✅ built (`7d99cb66f`) |
| F-2 ② the per-unit debug settings owned by A, ridden to S at the sync (`UnitDebugSettings.TakeRequestsFrom`) | ✅ built (`7d99cb66f`) |
| ⭐ **user decision: NO gizmo split** — the WHOLE gizmo group runs once per tick in the presentation step (threaded design §14.2.0; the split rule §14.2.1 superseded) | decided, not built |
| ⭐ measured: the gizmo group per tick (VM, 1 000 men, playing) | overlays ≈ 10 ms (already sliced 256 / rule / frame), interaction ≈ 1.3 ms |
| measured: the types only gizmos read | 9, all also read by the inspector; the threaded sync copies ALL types today (tables 5.5 ms / tick) |
| measured: the guard after ①② | 14 distinct hits — each mapped to its planned fix in that status |
| ⭐ found, agreed with the user, not built | the row copy follows the SELECTION, not the inspected / pinned entity ⇒ a pinned inspector reads stale values in masked mode; fix: rows for selected ∪ inspected / pinned, with a rail |
| ⭐ a prerequisite for ANY threaded + parallel run (and relevant here) | the guard and the A-1 tracer find "the tick" by an `AsyncLocal` context that the ONE pool's dedicated workers do NOT inherit ⇒ work on pool threads reads as "off the sim thread" / reader "(none)". ⚠ The same gap hits N-1: per-entity attribution set on the main thread is invisible on a pool worker — the census runs the systems SERIAL (default) or the pool must carry the reader / current entity into a batch |
| the plan to resume (F-1 rails → F-2 gizmo move + rows + commands + read-only slot path → F-3 … F-6) | in that status |
