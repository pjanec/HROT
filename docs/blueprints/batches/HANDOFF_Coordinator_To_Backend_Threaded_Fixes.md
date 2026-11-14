<!--STATUS
state: LIVE — re-dispatched to `backend` 2026-11-14, not started
updated: 2026-11-14
current-answer: the whole file
related-designs:
  - ../../DESIGN_Parallel_Simulation.md (merged into coordinator) — ⭐ §18.2 the declared mask (what the last-tick view copies; "the generator emits each asset's cross reads" is the target, `BrainReads` the hand list today), §18.5 / §18.6 the brain and squad phases
  - ../../DESIGN_Threaded_Simulation_Presentation.md (merged into coordinator) — §0a / §15 the threaded node, the staging, the sim worker
  - ../blueprints/DESIGN_Time_Architecture.md — §17 stepped mode
  - NOTE_Coordinator_To_Ui_Threaded_Editor_Limitations.md — L-1 … L-4 as the lanes met them
  - HANDOFF_Coordinator_To_Backend_Sim_Tick_Serial.md — the HELD batch these build items come from; its design items (T-1, T-2) stay with the coordinator
-->

# HANDOFF — coordinator → backend (VM): AUTONOMOUS — the threaded branch's fixes: declared reads, determinism, the bugs the lanes met

**For:** a backend VM lane. **From:** coordinator. **Dispatched at `c09648f4b`, RE-DISPATCHED at `c1a231dc7`** (rule 1a: re-pointed to `backend`, not started) — scope frozen at `c1a231dc7`.
🔒 User, `2026-11-14`: *"Yes pls lets prepare the batch for those fixes. The lanes mentioned that determinism rails fail because squad behavior
does not declare what components it reads, this should be included as well."* All items are FIXES (a root cause found and repaired at its
source) — ⛔ no new design: a fix that would change a contract (a component set, a phase's order, a public route) STOPS that item and is reported.

## ⛔⛔⛔ Never stop — the same contract

Never stop until every item is done; no question in chat. Trouble ⇒ the best-practice option that fits the engine, no hacks, a DECISIONS row,
continue. A bug ⇒ fix + a rail, red-proved. Blocked ⇒ the other items, then back (two approaches before OPEN). ⛔ Never skip, disable or quarantine
a test. Commit + push per item, by path; tick its tracker row.

## Where to work

⭐ **RE-POINTED `2026-11-14` (rule 1a — not started):** the lane branch **`backend`**, after merging **`origin/coordinator`** at the
re-dispatch sha below or newer — `exp/threaded-build` is MERGED into `coordinator` (`e4af862f6`) and frozen. 🔒 User, `2026-11-14`: *"we are now
out of experimental phase so we could merge the threaded build aggregation branch into coordinator branch and from that time on to continue
developing in usual lane-specific branches"*. ⛔ No `exp/threaded-fixes`. Push `chore: started threaded fixes at <sha>`. Status file `docs/blueprints/batches/STATUS_Threaded_Fixes.md`.

## The work, in this order

| # | item | design basis | done when |
|---|---|---|---|
| **X-1** · `CE-7035` | ⭐⭐ **declared reads — the drill fixture and the squad code.** The drills' `Squad` fixture runs `BrainTickSystem` without the phase's declarations (`BrainPhase.Contribute(…, declareReads)`, `BrainReads.Declare`), so `HROT_READ_CHECK=1` reds 83 of 233. ① the fixture registers the declarations through the SAME call the production phase uses — ⛔ never a copied list; ② the remaining throws are REAL undeclared cross reads in squad / brain code (e.g. `SquadGates.cs:47` a member's `WeaponState`; `ReactToContact`'s choice reading a man's `WeaponState` with real weapons, CE-2278): each declared — the tick-start view if the phase writes it, a stable read if nothing in the phase writes it (`IStableReads`) | Parallel Simulation §18.2, §18.5 / §18.6 (squads own their members, `OwnAlso`); D-43 the precedent (`AcousticEmitter` declared stable) | ⭐ the whole drill suite green under `HROT_PARALLEL_SIM=1 HROT_READ_CHECK=1 HROT_WORLD_GUARD=1` AND with `HROT_PARALLEL_SIM=0`; ⭐ a rail that runs the drills under `HROT_READ_CHECK=1` permanently, so a new undeclared read fails at once |
| **X-2** · `CE-7036` | the SOP suites and the drill suite cannot share a run under `HROT_PARALLEL_SIM=1` (10–23 rotating reds; each class alone green) — cross-class state under the parallel phase (xUnit runs classes concurrently): find the shared static (`ParallelReadCheck`, the pool's tick context, a registration) and make it per-world / per-run | Parallel Simulation §18.5 ("no static mutable state", CLAUDE.md BEHAVIOUR CODE row 5) | the combined filter green 5 runs in a row |
| **X-3** · `CE-4569` | ⭐⭐ **L-1 — stepped runs not repeatable in the threaded editor** (the same 10 men react a step apart at 1 vs 4 threads; equal with the threaded editor off). ⭐ First: the read survey (`HROT_READ_CHECK=2`) on the stepped run in mode 3 — an undeclared live read after X-1 is a prime suspect; then the threaded node's step (what differs from kernel 0: the job drain, the publish, the sense recording order of R-195) | Time Architecture §17; Threaded Presentation §15 | `scripts/stepped-hash.py --kernel 3 --threads 1,4,8` STATE EQUAL over ≥ 5 runs; a rail for the cause |
| **X-4** · `CE-4570` | **L-2 — entering the preview paused lets one step through** (threaded 1 in 3–6; the serial kernel always or never): zero steps in both kernels | Time Architecture §17 | a rail pinning the step count after a paused entry, both kernels |
| **X-5** · `CE-4574` | ⭐ **the editor crashes on shutdown** — `AccessViolationException` in `EntityRepository.SyncPresentationCore` (the sim worker's `StagingWorlds.Publish`), always after `/shutdown`: the sim worker must be stopped and joined before any world it publishes from or to is disposed | Threaded Presentation §15 (the shared node frame owns the worker) | a rail: start → play → shutdown ×20, no crash; the Windows command to confirm on real cores |
| **X-6** · `CE-4568` | ⭐ **CE-4542 — a running live scenario cannot be stopped** (End exercise and Close do nothing): reproduce threaded and serial; fix at its source | Threaded Presentation §15 (run controls as sim jobs, overnight N-5 / D-12) | a rail: Stop ▸ End exercise ends the run, both modes |
| **X-7** · `CE-4571` | **L-3 — `GET /view`, `POST /view/follow` read the sim's world** (`DebugApiService.View.cs:18`, `:74`) ⇒ the shown world (a UI-side route, overnight D-8) | overnight D-8 / D-18 | the routes work threaded |
| **X-8** · `CE-4572` | **L-4 — an HTTP `DELETE /entities/{id}` records no undo step** (both kernels) — bracket it as the component edit is (overnight D-38) | D-38 | undo after an HTTP delete restores it |
| **X-9** · `CE-4575` | **an HTTP move order is dropped**: `POST /entities/{id}/order {kind:'move', x, y}` — a documented form — fails to parse in `BehaviorIngress` (`System.Numerics.Vector3`) while the route answers ok: the route maps the documented form, and a refused order answers an error, never ok | the route's doc (`DebugApiRouteDocs`) | a rail: the documented form lands; a malformed one answers an error |
| **X-10** · `CE-4565` | the event history builds no text per tick: `DiagnosticEventHistoryService.Capture` (reflection + LINQ every tick, `:37`, `:113-140`) keeps the event; the summary is built when the Event Browser reads it | CLAUDE.md HOT PATH | 0 B per steady tick from it; the Event Browser's rails show the same text |
| **X-11** · `CE-4566` | `HROT_FRAME_STATS=1` no longer turns on `TableWriteStats` (13 ms per tick, `TableWriteStats.cs:14`) — its own switch; the measuring scripts set only what they measure | — | the tick with frame stats on equals off (± noise) |
| **X-12** · `CE-4573` | fold: Parallel Simulation §18.2 (the declarations as built, the permanent read-check rail), Threaded Presentation §15 (shutdown order, run controls), Time Architecture §17 (the paused entry); the limitations note to ui marked resolved per item | — | the design commits named |

⛔ Not here (the coordinator's, in the held batch): what each serial piece of the tick is for and costs (T-1), system-level parallelism (T-2),
the small-phase threshold (T-5 — it needs T-1's numbers).

## How to work

`scripts/cbm.sh` (graph) AND grep before claiming something exists. The touched features' OWN rails — named with their switches in every
command; ⛔ no broad suites. Build the TEST project before `--no-build`. Long runs in the background, never polled. Language-neutral output.

---
_coordinator lane, 2026-11-14_
