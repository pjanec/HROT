<!--STATUS
state: LIVE — dispatched, not started
updated: 2026-11-13
current-answer: the whole file
related-designs:
  - STATUS_Windows_Core_Utilisation.md (on exp/windows-threaded-measure) — ⭐ §1 the ranked serial list, §3 the pool, §4 the small-systems tail, §0 the instrument's own cost
  - NOTE_Coordinator_To_Ui_Threaded_Editor_Limitations.md — L-1 … L-4, the threaded-editor bugs this batch also fixes
  - ../../DESIGN_Parallel_Simulation.md (on exp/threaded-build) — §13 / §16 the pool and the dependency-scheduler question (POC 4), §18 the phases
  - ../../DESIGN_Threaded_Simulation_Presentation.md (on exp/threaded-build) — §0a, §14.8 / §15 the staging
-->

# HANDOFF — coordinator → backend (VM): AUTONOMOUS — the serial sim tick: understand each piece first, fix what is plain, and the threaded-editor bugs

**For:** a backend VM lane. **From:** coordinator. **Dispatched at `3786db00b`** — scope frozen.
🔒 User, `2026-11-13`: *"yes pls measure whatever needed to help utilize the cores, this is enormous waste"* · and, on the coordinator's first
proposal: *"how could we have one copy that fits all component sets and timing? (background vs hot???) are you serious? how can we move the
recorder off the tick? it is already mostly off the tick. have you investigated the design before coming with suggestions?"* · *"yes, write both"*.

⛔⛔ **The lesson that shaped this handoff:** the coordinator proposed merging the recorder's, the module snapshots' and the staging's copies, and
moving the recorder off the tick — both WRONG once their designs were read (three different component sets and timings; FDP-DES-004 already
writes the file asynchronously, only the capture is on the tick). ⇒ **for each serial piece below, read its design and measure it BEFORE
proposing anything; the user approves any change to a piece's contract.**

## ⛔⛔⛔ Never stop — the same contract

Never stop until every item is done; no question in chat. Trouble ⇒ the best-practice option that fits the engine, no hacks, recorded in
DECISIONS. A bug ⇒ fix + a rail. Blocked ⇒ the other items, then back. ⛔ Never skip a test. Commit + push per item, by path; tick its row.

## Where to work

A NEW branch **`exp/sim-tick-serial`** from **`origin/exp/threaded-build`**; merge it in when it moves (never rebase). Status file
`docs/blueprints/batches/STATUS_Sim_Tick_Serial.md`.

## Part 1 — understand (no change to a contract)

| # | item | done when |
|---|---|---|
| **T-1** · `CE-4563` | ⭐ **for each serial piece of the Windows list, its DESIGN and its MEASURED anatomy**: the recorder's capture (FDP-DES-004 and CE-1478 / CE-3332 / CE-2045: how many copies per changed chunk, how many chunks change per tick at 1 000 men, what must be on the tick and why) · `OnDemandProvider.AcquireView` (which modules, their masks and rates, what each sync copies) · `StagingWorlds.Publish` (the mask, the bytes per tick) · `HrotNodeContext.StepTimeline` (the live timeline: who reads it, at what rate it must scan) · the 94-system tail (each one's rate, reads / writes) | a section per piece in a NEW design note `docs/DESIGN_Sim_Tick_Serial_Cost.md` (STATUS block, related-designs both ways): what it is for, what it costs, what its design allows, and ⭐ a LEAN per piece with its claim table (both columns: code and design) — ⛔ no change built |
| **T-2** · `CE-4564` | ⭐ **the small-systems tail — the scheduler question as an architect question**: running conflict-free groups side by side must respect the ORDER between systems, not only their data (C-4 counted data only). Draft `Architect_Question_<next free>_System_Level_Parallelism.md` with the INVENTORY (graph + grep), the proven solution (system-level dependency scheduling, as in the engines), POC 4's earlier measurement and why it rejected it, and a lean per sub-question | the question, for the user to approve |

## Part 2 — fix what is plain (no contract changes)

| # | item | done when |
|---|---|---|
| **T-3** · `CE-4565` | ⭐ **the event history builds no text per tick**: `EventHistoryCaptureSystem` → `DiagnosticEventHistoryService.Capture` turns every event into a text summary by reflection + LINQ every tick (`DiagnosticEventHistoryService.cs:37`, `:113-140`), for the Event Browser panel whether or not it is open ⇒ keep the event, build the summary when it is READ | 0 B per steady tick from it; the Event Browser shows the same text (its rails) |
| **T-4** · `CE-4566` | the measuring switch `HROT_FRAME_STATS=1` no longer turns on the per-tick write instrument (`TableWriteStats.On`, `TableWriteStats.cs:14`) — its own switch; `threaded-scale-measure.py` sets only what it measures | the tick with frame stats on = off (± noise) |
| **T-5** · `CE-4567` | phases too small to pay for the hand-off run inline below a measured size (C-3: `CarKinematicsSystem` 11 %, `RaycastSolverSystem` / `EntityQuery.ForEachParallel` ≤ 3 %, Threat / Squad ~20 %) — ONE threshold in `ParallelEntities` / the pool, not per system | each phase's efficiency; the stepped gate equal at 1 / 4 / 8 |

## Part 3 — the threaded-editor bugs

| # | item | done when |
|---|---|---|
| **T-6** · `CE-4568` | ⭐ **CE-4542 — a running live scenario cannot be stopped** (End exercise and Close do nothing; only closing the window works): reproduce with the threaded editor ON and OFF; fix at the source | a rail: Stop ▸ End exercise ends the run in both modes |
| **T-7** · `CE-4569` | ⭐ **L-1 — stepped runs not repeatable in the threaded editor** (the same 10 men react a step apart at 1 vs 4 threads; equal with the threaded editor off) | `stepped-hash.py --kernel 3 --threads 1,4,8` equal over ≥ 5 runs |
| **T-8** · `CE-4570` | **L-2 — the paused preview entry lets an extra step through** (threaded, 1 in 3–6): entering paused takes ZERO steps in both kernels | a rail pinning the step count |
| **T-9** · `CE-4571` | **L-3 — `GET /view`, `POST /view/follow` read the sim's world** (`DebugApiService.View.cs:18`, `:74`) ⇒ the shown world (UI-side routes, D-8) | the routes work threaded |
| **T-10** · `CE-4572` | **L-4 — an HTTP `DELETE /entities/{id}` records no undo step** (both kernels) — bracket it like the component edit (D-38) | undo after an HTTP delete restores it |
| **T-11** · `CE-4573` | fold: the design note (T-1), Threaded Presentation §0a / §15, Parallel Simulation §18 | the design commits named |

## How to work

`scripts/cbm.sh` (graph) AND grep before claiming something exists. The touched features' OWN rails; ⛔ no broad suites. Build the TEST project
before `--no-build`. Long runs in the background, never polled. Language-neutral output (R-4221).

---
_coordinator lane, 2026-11-13_
