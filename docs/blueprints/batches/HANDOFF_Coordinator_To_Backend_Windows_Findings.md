<!--STATUS
state: LIVE — dispatched, QUEUED (start after CE-4480…CE-4488 are done)
updated: 2026-11-13
current-answer: the whole file
related-designs:
  - STATUS_Windows_Threaded_Scaling.md (on exp/windows-threaded-measure) — ⭐ the findings (W-5, W-6, "Surprising")
  - ../../blueprints/DESIGN_Time_Architecture.md — §17 stepped mode waits for background jobs; R-3035 stepped mode is deterministic
  - ../../DESIGN_Parallel_Simulation.md — §19 Windows scheduling; §18.5 the per-entity rule
  - ../../DESIGN_Threaded_Simulation_Presentation.md — §0a the architecture as it stands (its numbers)
-->

# HANDOFF — coordinator → backend (VM): AUTONOMOUS — what the Windows run found (stepped repeatability, the gate, small fixes)

**For:** the backend VM lane, **after** its mode + serial-systems batch (CE-4480…CE-4488). **From:** coordinator. **Dispatched at `abc319675`**.
🔒 User, `2026-11-13`: *"Yes"* (to the coordinator's proposal after the Windows run).

## ⛔⛔⛔ Never stop — the same contract

Never stop until every item is built and tested; no question in chat. Trouble ⇒ the best-practice option that fits the engine, no hacks,
recorded in DECISIONS. A bug ⇒ fix + a rail. Blocked ⇒ the other items, then back (two approaches before OPEN). ⛔ Never skip a test. Commit +
push per item, by path; tick its tracker row. A finding that changes a design is folded into it (obligation ⑤).

## Where to work

Continue on **`exp/threaded-build`**. Status file `docs/blueprints/batches/STATUS_Windows_Findings.md`.

## The work

| # | item | done when |
|---|---|---|
| **F-1** · `CE-4501` | ⭐ **commit the stepped determinism gate** as `scripts/stepped-hash.py` (today only in a scratchpad): load a scenario, step from paused in fixed chunks with the same orders at the same steps, `GET /world/hash?byType=1` per chunk, compare runs type by type, `--rows <Type>` to name the entities; Windows and Linux alike (hidden window); a decimal-comma-safe parser | the script on the branch; it reproduces the VM's seven-run result |
| **F-2** · `CE-4502` | ⭐ **is the stepped run repeatable with every switch OFF** (the product as it is)? two identical 1-thread … and switch-off runs, 1 000-man demo-city, 240 steps; and the same in mode 3. ⚠ The Windows machine saw `Utterance`, `NavigationApplied`, `NavigationIntent`, `SquadDrillState` differ between two identical 1-thread runs (positions equal); the VM saw seven equal runs | the table: switch off / mode 3 × repeatable or not, with the differing types |
| **F-3** · `CE-4503` | ⭐⭐ **make stepped mode repeatable** — find what differs (`--rows`), the source (suspects: background solvers — pathfinding, EQS — landing results by WALL clock rather than by step; a wall-clock read; an unseeded random; an ordering by a hash of a reference) and fix it at its source. ⭐ The design says stepped mode WAITS for background jobs (Time Architecture §17) — if a solver's result can land at a different step, that is the defect | `stepped-hash.py` equal across ≥ 5 runs at 1 / 4 / 8 threads, switch off and mode 3; a rail per root cause, red-proved; Time Architecture §17 updated with what was found |
| **F-4** · `CE-4504` | **1 thread slower than the switch off** (tick 246 vs 144 ms, BrainTick 83 vs 28 ms, the group 72 ms more than the sum of its members — Windows): measure the cause (suspect: the one pool thread shared with EQS's background lane, the brain phase waiting for it) and fix it if it is a defect (e.g. the frame lane never starved by the background lane) | the cause stated with a measurement; if fixed, 1 thread ≤ the switch off |
| **F-5** · `CE-4505` | small fixes: `threaded-scale-measure.py` and `threaded-poc2-measure.py` — the `[FrameStats]` regex reads a decimal comma (Czech locale: `8,9 ms` read as 8) · `WindowsScheduling.OptOutProcess()` logs its result once at start (`Program.cs:52` ignores it) | each with a rail / a test line |
| **F-6** · `CE-4506` | fold: Time Architecture §17 (F-3), Parallel Simulation §19 (the opt-out verified on Windows: ControlMask 5 / StateMask 0 on every process; Windows' default cost 1.2–1.4× this day vs ~1.9× earlier — it varies), Threaded Presentation §0a (the state) | the design commits named |

## How to work

The touched features' OWN rails; build the TEST project before `--no-build`; long runs in the background, never polled; `scripts/cbm.sh` AND grep
before claiming something exists. ⛔ No screenshots in git.

---
_coordinator lane, 2026-11-13_
