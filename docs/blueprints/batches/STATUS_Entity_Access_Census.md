<!--STATUS
state: LIVE — N-1…N-8 done (first pass); VERDICT at the top
updated: 2026-11-12
current-answer: ⭐ "N-8 VERDICT" (top) · then "Carried from the paused threaded POC 4" (the coordinator should count with it) · then the evidence per item
related-designs:
  - ../../DESIGN_Parallel_Simulation.md — OWNS this: §18 per-entity parallelism
  - HANDOFF_Coordinator_To_Backend_Entity_Access_Census.md — the handoff (dispatched at cf8dbebab)
  - STATUS_Threaded_POC_4.md (on exp/threaded-poc-4) — the paused threaded POC 4 (what is built, measured, and the plan to resume)
  - STATUS_Threaded_Analysis_Instruments.md — A-1 the access tracer this extends per entity; A-2 the reach tool
-->

# STATUS — the cross-entity access census (branch `exp/entity-census`)

**Branch:** `exp/entity-census` from `exp/perf@5add60bbe` with `exp/threaded-analysis@253b0f00b` merged
(the A-1 tracer, its hooks, `scripts/access-trace-*.py`, the A-2 reach tool). Every instrument opt-in, default OFF.

## ⭐ N-8 VERDICT — **the rule is VIABLE for the whole Simulation phase; the payoff is bounded by what is NOT per entity** *(VM census, Windows ms)*

**Decision: GO on per-entity parallelism under §18's rule.** Every heavy system of the CGF's Simulation phase is a per-entity walk whose
conversion list is SHORT — no system needs its own parallel algorithm, and the measured cross-entity WRITES are a handful.

| what the census measured (1 000 men, steady state after a 25 s warm-up, every count per EXECUTION of the system) | |
|---|---|
| ⭐ CGF Simulation + PostSimulation, by class | **READY 18 (6.5 ms VM)** · **CONVERTIBLE 11 (128 ms)** · GLOBAL 6 (0.3 ms) · no per-entity access 36 (1.4 ms) |
| ⭐ the genuine cross-entity WRITES of the 11 | BrainTick: callouts / utterances (`Callouts.Say` from squad manoeuvres, ~0.1 per run) · the command order journal (`CommandOrders.Journal`) · ⭐ nothing else — its other "foreign" writes are its OWN child sensors (`EqsChildSensor`), now classed own-part through `PartMetadata.ParentEntity` |
| writes with no entity walk open | Stress (`StressModel.Add`) and WeaponDispatcher (`ShotLedger.Settle`) — ⭐ these ARE POC 1's deferred per-batch merges (the rule's command buffer already) · HealthApplication (event-driven: the damage events name the targets) · SquadCoordination (`SquadSenseChanges.Store`, squad-level sense) |
| structural changes on others | BrainTick creates its child sensors in-tick (`EqsChildSensor.Ensure`: `PartMetadata` is added before its parent field is set, ~58 per run) ⇒ DEFERRED creation |
| ⭐ the last-tick set (cross-entity reads of a type some system writes in the phase) | **18 types** (`Health`, `SimTransform`, `WeaponState`, `BehaviorState`, `StanceIntent`, `TargetMemory`, `UnitRoster`, …) |
| ⭐ N-5 the last-tick view's cost | copy just those 18 types per tick: **CGF 1.9–2.7 ms · SimHost 0.9 ms** — vs a full (snapshotable) copy **CGF 5.2–8.5 ms · SimHost 1.6–2.2 ms** ⇒ the masked copy, about a third |
| N-3 state outside components | the SAME statics in every big system: `QueryTimings` (the per-query timer's registry and buffers), `Armament._conductJudgements` (a cache), `UtilityInputNames` / `UtilityDecisionCatalog` (name registries), `DecisionCadence.s_wakeSlots` · each system's own scratch lists (`_list`, `_people`, `_commanders`, `_cadenceReaders`) ⇒ per-thread / per-batch |
| N-6 BrainTick's time (VM, everything on, inclusive, all threads) | utility scoring **58.7 %** (the parallel pre-score 54.9 % — `EnemyStrengthRatio` 21 %, part-index lookups 18 %) · BTree execution **17.6 %** · the blueprint occurrence store **≈ 16 %** (`TryFindSlot`, `TickViewState.Defer`) · ⚠ the SAMPLED query timer still **11.9 %** |

**N-7 the prize** (Windows serial CGF 256 ms; per-entity systems 181 ms, the rest 75 ms — egress 34, recorder 20, ingress 16; +4.7 ms last-tick
copy and command playback): **4 cores 125 ms (2.05×) · 8 cores 102 ms (2.5×) · 16 cores 91 ms (2.8×) · 32 cores 85 ms (3.0×)**, floor ≈ 80 ms.
⇒ ⭐ **beyond 8 cores the floor is egress, the recorder and ingress — not the brains.** Those three are per-entity scans too (egress already has an
off-frame replica, CE-3427); making them parallel moves the floor to ≈ 10 ms. ⚠ Assumes perfect scaling inside a system — the pre-score flattened
after 4 threads on the 7 pinned Windows CPUs.

**The conversion list, in order of payoff:** ① BrainTick (104 ms Windows) — deferred child-sensor creation; callouts / journal as commands; the
last-tick view for 12 types; per-thread scratch (`_cadenceReaders`, `_blueprintLayout`), `DecisionCadence.s_wakeSlots` per batch · ② the shared
statics once for all (`QueryTimings` per thread, the caches / registries frozen after warm-up or locked) · ③ WeaponDispatcher, Stress (already
deferred), Callout, Threat, Squad (squad-level state: one squad per job, not one man) · ④ egress / recorder / ingress — the floor.

**The user calls it needs:** ① a brain sees other units' state one tick late (the last-tick view) — like R-4208 · ② a callout / order reaches
its listeners a tick later (commands) · ③ squad-level systems run per SQUAD, not per man.

**What would change it:** a hidden cross-entity write the hooks cannot see — ⛔ blind spots stated in `EntityCensus`: raw pointers / spans taken
once, `GetComponentTable`, loops that are not `EntityQuery` walks (counted as "none", not missed) · 36 systems made NO per-entity access in the
run (they ran but did nothing per entity, or did not run) — a run with combat-heavy scenarios may light up more.

| item | state | evidence |
|---|---|---|
| N-1 per-entity attribution + rail | ✅ | `EntityCensus`; `EntityCensusTests` (on: exact classes incl. own-part; off: 0 B); red-proved |
| N-2 the census (CGF + SimHost) | ✅ | [CGF](entity-census/N2_Census_CGF.md) · [SimHost](entity-census/N2_Census_SimHost.md) (`scripts/entity-census-report.py`) |
| N-3 state outside components | ✅ static reach, per system | [N3_Shared_State_Per_System.md](entity-census/N3_Shared_State_Per_System.md) |
| N-4 the classification | ✅ in N-2's tables (READY / CONVERTIBLE / GLOBAL) | |
| N-5 the last-tick view's cost | ✅ | `LastTickProbe` (`HROT_LASTTICK_TYPES`) |
| N-6 BrainTick's split | ✅ VM trace | above |
| N-7 the prize model | ✅ | above |
| N-8 the verdict | ✅ | this section; design §18 |

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
