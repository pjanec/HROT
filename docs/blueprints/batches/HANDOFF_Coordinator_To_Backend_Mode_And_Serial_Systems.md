<!--STATUS
state: LIVE — dispatched, not started
updated: 2026-11-13
current-answer: the whole file
related-designs:
  - NOTE_Backend_To_Coordinator_Parallel_Switches_And_Throughput.md (on exp/threaded-build) — ⭐ the findings this answers (D1–D5)
  - STATUS_Overnight_Threaded_Build.md (on exp/threaded-build) — the overnight build, its decisions D-1…D-43, its numbers and its gates (the commands reused here)
  - ../../DESIGN_Parallel_Simulation.md — §10–§11 the utility pre-score (R-4207, retired here), §18–§18.6 the per-entity rule
  - ../RULINGS.md — R-4207 (the mode is chosen by what is running), R-4214, R-4215, R-4218, R-4219
-->

# HANDOFF — coordinator → backend (VM): AUTONOMOUS — the mode set by what is running, the pre-score retired, the serial systems converted

**For:** a backend VM session (the lane that wrote the note). **From:** coordinator. **Dispatched at `ffc802ab2`** — scope frozen at that sha.

🔒 User, `2026-11-13`: *"yes, approved, write the handoff"* — approving the coordinator's leans on the note's D1–D5 (recorded as R-4218 / R-4219).

## ⛔⛔⛔ THE NEVER-STOP CONTRACT — the same as last night

| | |
|---|---|
| ⭐⭐⭐ **Never stop until every item is implemented and tested.** | No question in chat, no pause for confirmation. |
| ⭐⭐⭐ **Trouble ⇒ judge, decide, record, continue.** | The option that follows best practice and fits the engine — no shortcuts, no hacks (user, overnight). A row in the status file's DECISIONS table. |
| ⭐⭐⭐ **A bug found ⇒ fix it, a rail that catches it, continue.** | A result that differs by batch size or thread count is a BUG in the code that produced it, not a reason for a mode. |
| ⭐⭐ **Blocked ⇒ every other item first, then back with a different approach** (R-106). | Two approaches before an item is written up OPEN. |
| ⭐⭐ **A red ⇒ root-cause it.** | ⛔ Never skip, disable or quarantine a test. A pre-existing red is proved against the base sha and noted. |
| ⭐ **Commit and push after every item, by path; tick the tracker row.** | `🔄 IN PROGRESS` → `[x]` + commit, or `⚠ OPEN` + what was tried. Status file = the resume point after a compaction. |

## Where to work

Continue on **`exp/threaded-build`** (it carries everything this builds on). Push `chore: started mode + serial systems at <sha>` first.
Status file: `docs/blueprints/batches/STATUS_Mode_And_Serial_Systems.md`. ⛔ Merge into no lane. Every conversion follows the switches already
there (`HROT_PARALLEL_SIM` / `HROT_THREADED_KERNEL=3`), default OFF, 0 B per steady frame (R-2084).

## The work

| # | item | tested when |
|---|---|---|
| **M-1** · `CE-4480` | ⭐ **does the batch size change the result?** (note D1) — the stepped 1 000-man gate (`scratchpad/stepped-hash.py` or its committed successor) with the fixed 32 batch vs the adaptive `max(32, n/(threads×4))` batch, at 1 / 2 / 4 / 8 threads. By design it cannot (contiguous batches merged in batch order = entity order); ⭐ **a difference is a bug — find it (`?byType=1`, `?rows=<Type>`) and fix it** | the same world in every run, both batch rules; the result in the status file |
| **M-2** · `CE-4481` | ⭐ **the kernel sets the mode from what is running** (R-4207, R-4218): stepped, Run-to, filming, the scenario regression and the test harnesses ⇒ Deterministic; continuous live play ⇒ Throughput. ONE value for the whole process (R-4215), changed only at a tick boundary when the clock's mode changes, applied to every kernel of the process. ⛔ `HROT_SIM_MODE` REMOVED (and its readers); `ParallelEntities.Mode`'s comment made true | rails: stepping starts ⇒ Deterministic before the next tick; continuous play ⇒ Throughput; `--mode all` ⇒ every kernel the same; red-proved |
| **M-3** · `CE-4482` | ⭐ **retire `UtilityPrescore`** (R-4219, note D4): first read Parallel Simulation §10–§11 and R-4207 and confirm no host keeps SERIAL brains wanting parallel scoring; then remove the pre-score, `HROT_BRAIN_PRESCORE` and its rails; fold §10–§11 — the prior state marked SUPERSEDED by the per-entity brain phase (R-4214 ①) | the build clean; the brains' own suites green; the switch gone (grep) |
| **M-4** · `CE-4483` | ⭐ **measure what is serial in the tick**: per-system time inside `TogglableSimulationGroup` and `WeaponDispatcherSystem` at 1 000 men, mode 3, at 1 and 4 threads (`threaded-scale-measure.py`) — Debug on the VM AND Release on the VM; the ranked list | the table in the status file: system · ms · per-entity or not · what it writes on other entities |
| **M-5** · `CE-4484` | ⭐ **convert the heavy members of `TogglableSimulationGroup`** in M-4's order to per-entity phases under the rule (own entity direct, others by command a tick later, declared cross reads from the tick-start view, structural changes deferred, follow-ups in batch order); shared caches / scratch / statics found on the way are bugs — per-thread or per-entity; a system that genuinely cannot be per-entity is written up with the measured reason | per system: its own rails green; the read survey (`HROT_READ_CHECK=2`) clean; stepped hash equal at 1 / 2 / 4 / 8 |
| **M-6** · `CE-4485` | ⭐ **convert `WeaponDispatcherSystem`** the same way (shots on other entities = commands; effects / detonations spawned deferred) | as M-5 |
| **M-7** · `CE-4486` | ⭐⭐ **it really works**: ① the threaded 1 000-man scripted session ×2 (the overnight N-15 ① chain), 0 faults, strict guard 0; ② the stepped gate equal at 1 / 2 / 4 / 8; ③ continuous play in Throughput 5 min at 1 000 men, 0 faults; ④ `--mode all` threaded 2 min playing; ⑤ switches OFF: the touched features' own suites green, 0 B per steady frame | the GATES table: verbatim commands, counts, base-sha proof for any pre-existing red |
| **M-8** · `CE-4487` | **the numbers**: sim tick, UI frame, BrainTick and the converted systems at 1 / 2 / 4 threads, Debug and Release on the VM — before / after; the Windows command line for 1…12 threads (Release, hidden window) | in the status file |
| **M-9** · `CE-4488` | **fold the as-built** into Parallel Simulation §18 (+ §10–§11 superseded), Threaded Presentation §14.8, the note (mark it answered) | the design commits named |

⚠ When all are done: keep going down M-4's ranking — the next serial system — until no system above ~5 % of the tick at 4 threads remains
serial, or each remaining one is written up with its measured reason.

## How to work

The touched features' OWN rails; build the TEST project before `--no-build`; long runs in the background, never polled; one build at a time.
`scripts/cbm.sh` (graph) AND grep before claiming something exists or not. No LINQ / allocation on the hot path. ⛔ No screenshots in git.

## The report (top of the status file)

DONE · GATES · DECISIONS · BUGS FIXED · OPEN · NUMBERS (before / after).

---
_coordinator lane, 2026-11-13_
