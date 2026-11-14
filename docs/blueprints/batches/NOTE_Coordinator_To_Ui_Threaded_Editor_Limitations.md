<!--STATUS
state: LIVE
updated: 2026-11-14 (backend marks the threaded-fixes items resolved per row: [STATUS](STATUS_Threaded_Fixes.md))
current-answer: the whole file
related-designs:
  - NOTE_Coordinator_To_Ui_Showcase_On_The_Parallel_Branch.md — where the showcase's behaviour code lives (a branch from exp/threaded-build)
  - STATUS_Windows_Unified_Run.md (on exp/windows-threaded-measure) — ⭐ the measurements every row below cites (W-1 … W-5)
  - ../../DESIGN_Threaded_Simulation_Presentation.md (on exp/threaded-build) — §0a the architecture as it stands, §0a.1 the switches, §0a.2 the manual sheet
  - ../../DESIGN_Parallel_Simulation.md (on exp/threaded-build) — §18 the per-entity rule
-->

# NOTE — coordinator → ui: what the threaded editor cannot do yet — and the switch setting for automated behaviour testing

🔒 User, `2026-11-13`: *"write a note to the UI lane about the limitation of the current threaded editor implementations, so they can write their
own handoff for implementing the showcase behaviors on top of the threaded editor. And avoid the issues, because the behavior editing and
development will for sure require a lot of automated testing. So they might need to turn off the parallelism to make sure that the HTTP stuff is
working and determinism is working."*

⭐ Base: `exp/threaded-build` at `a9955908e` or later (`READY FOR WINDOWS at 251e9cc36`). Measured on Windows, Release, real cores, the
defaults ON — [`STATUS_Windows_Unified_Run.md`](https://github.com/pjanec/HROT/blob/exp/windows-threaded-measure/docs/blueprints/batches/STATUS_Windows_Unified_Run.md).

## 1. The two switches are different things — turn off the right one

| switch | what it is | default |
|---|---|---|
| **Threaded editor** (`View ▸ Settings ▸ Performance`, or `HROT_THREADED_KERNEL=0` / `3`) | the simulation on its own worker, the UI on a published copy | ON in editor / player |
| **Parallel simulation** (same menu, or `HROT_PARALLEL_SIM=0` / `1`) | the per-entity phases: brains, squads, stress, callouts, threat, weapons, movement on all cores | ON everywhere but `-m all` |

⭐⭐ **Every limitation below is the THREADED EDITOR's, not the parallel phases'.** Measured: with the threaded editor OFF and the parallel phases
ON, the stepped gate is EQUAL at 1 / 4 / 12 threads (W-4, `--kernel 0`), and the `/view` routes work (W-5).

## 2. The limitations today

| # | what | measured | with the threaded editor OFF |
|---|---|---|---|
| L-1 | ✅ **RESOLVED here `2026-11-14` (backend X-3 / X-1 / X-2, CE-4569 — 5 / 5 rounds equal at 1 / 4 / 8 threads on Linux; Windows to confirm)** ~~stepped runs are NOT repeatable~~: the same 10 men start a reaction one step apart at 1 vs 4 threads (`BehaviorState`, `BlueprintBlackboard*`, `BrainJournal`); positions equal. A regression since this morning's build | W-4 | ✅ EQUAL |
| L-2 | ⛔ **entering the preview paused lets one extra step through** in 1 run in 3–6 (121 vs 120 steps); `stepped-hash.py` refuses to compare such runs (exit 2) | W-4 | ✅ a constant count every run |
| L-3 | ✅ **RESOLVED `2026-11-14` (backend X-7, CE-4571 — they read the shown world)** ~~`GET /view` and `POST /view/follow` fail~~ ("a headless host"): they look the camera up on the sim's world (`DebugApiService.View.cs:18`, `:74`) | W-5 row 2 | ✅ works |
| L-4 | ✅ **RESOLVED `2026-11-14` (backend X-8, CE-4572 — the delete is one undo step)** ~~Undo after an HTTP `DELETE /entities/{id}` is refused~~ — the HTTP delete records no undo step | W-5 row 3 | ⛔ the same (not a threading issue) |
| L-5 | ⚠ the `[Performance]` status line ("restart to apply") is not readable over HTTP (`GET /editor/commands/…status` has no `?scope=shell`) | W-1 | — (cosmetic) |
| L-7 | ✅ **RESOLVED `2026-11-14` (backend X-6, CE-4568)** — *End exercise* did nothing (a rule created at run time faulted in the per-entity phase); it ends the exercise now, on both kernels | live repro | the same (it was the parallel phase, not the threaded editor) |
| L-6 | cosmetic: the log's `·` separator is written in the console's OEM code page (`ú` in a UTF-8 reader) | W-5 | the same |

A fix batch for L-1 … L-4 and L-6 is proposed to the user (not dispatched yet). ⛔ Do not work around them in your own code — they are fixed at
their source in the threaded node, and the showcase picks the fixes up by merging `exp/threaded-build`.

## 3. ⭐⭐ The setting for AUTOMATED behaviour testing

| for | threaded editor | parallel phases | why |
|---|---|---|---|
| ⭐⭐ **every automated behaviour test that drives the editor over HTTP, steps it, or compares hashes** (`stepped-hash.py`, the debug API, the scenario rails) | **OFF** (`HROT_THREADED_KERNEL=0`) | **ON** (`HROT_PARALLEL_SIM=1`) | determinism and every HTTP route work (§2), AND the per-entity rule is still exercised — ⛔ turning the parallel phases off would also turn off the checks below |
| the rule's checks on each new behaviour (CLAUDE.md *"BEHAVIOUR CODE RUNS IN PARALLEL"*) | OFF or ON | **ON** — they only exist there | `HROT_READ_CHECK=1` (an undeclared cross-entity read throws), the strict guard `HROT_WORLD_GUARD=1`, `stepped-hash.py --kernel 0 --threads 1,4,8` |
| a behaviour's own unit rails (no editor) | n/a | ON (`HROT_PARALLEL_SIM=1`) AND OFF — both pass | the rule holds both ways (the drills: 228 / 228 each) |
| manual play in the editor / player | ON (the default) | ON | that is what the user tests |

⭐ Write your handoff's gates with these switches named explicitly in each command — an environment variable overrides the user file, so a test
never depends on the machine's saved settings.

## 4. What stays true whatever the switches

- The per-entity rule is the contract for every behaviour (CLAUDE.md, nine never / instead rows).
- An effect on another unit lands one tick later (R-4212) — design outcomes, never same-tick ordering.
- `-m all` is serial by default (it is the AI-debug network mode).

---
_coordinator lane, 2026-11-13_
