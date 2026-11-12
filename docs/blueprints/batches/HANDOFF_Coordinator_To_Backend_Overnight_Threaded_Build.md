<!--STATUS
state: LIVE — dispatched, not started
updated: 2026-11-12
current-answer: the whole file
supersedes: HANDOFF_Coordinator_To_Backend_POC_Combined.md (CE-4461, withdrawn unstarted — user: no POC, build the settled design tonight)
related-designs:
  - ../../DESIGN_Threaded_Work_Inventory.md — ⭐ §8 the decision list (D-1…D-6, §8.2a R-4216 two staging worlds), §1 the 133 UI write sites, §2 run controls, §3 the 223 HTTP routes, §4 the gizmo move, §7 the critical review (U-1…U-6, F-1…F-16, P-1…P-7)
  - ../../DESIGN_Threaded_Simulation_Presentation.md — ⭐ §13 shared state, §14 the target picture, §14.1 the anti-race rules, §14.6 the whole gizmo block (R-4211)
  - ../../DESIGN_Parallel_Simulation.md — ⭐ §18–§18.2 the per-entity rule, the redirect, the declared mask; §13 the job system; §11.2 the measured scaling
  - ../RULINGS.md — R-4208…R-4217
-->

# HANDOFF — coordinator → backend (VM): OVERNIGHT, AUTONOMOUS — build the threaded editor + per-entity parallel simulation, END TO END

**For:** one backend VM session, running all night. **From:** coordinator. **Dispatched at `2e57eac40`** — scope frozen at that sha; later
documents are FYI only.

🔒 **User, `2026-11-12`, verbatim:** *"write a handoff for autonomous overnight implementation of the design we just settled. Use 2 staging
buffers and make sure the session never stops. It needs to judge the trouble according to its best judgment and pick a decision. It should fix
the troubles if it finds one. It should test it that it really works. And after that it continues, but it should never ever stop until the
design is implemented and tested."*

## ⛔⛔⛔ THE NEVER-STOP CONTRACT — read this first

| | |
|---|---|
| ⭐⭐⭐ **Never stop until every item below is implemented and tested.** | ⛔ No "waiting for the user", no question in chat, no pause for confirmation, no early report. Nobody is there to answer. |
| ⭐⭐⭐ **Trouble ⇒ judge it, decide, write the decision down, continue.** | Pick the option that best fits the designs and rulings cited above; if they say nothing, pick the standard proven solution and say why in one line. Record it in the status file's **DECISIONS** table (problem · options · chosen · why · `file:line`). The user reviews decisions in the morning — ⛔ never waits on them. |
| ⭐⭐⭐ **A bug found ⇒ fix it, add a test that catches it, continue.** | A behaviour overwriting another's scratch, a shared cache, a shared random generator, a static written from two threads — these are BUGS in that code, fixed where they are, not design problems. |
| ⭐⭐ **Blocked on one item ⇒ do every other item, then come back to it with a different approach** (R-106). | ⛔ A blocked item never stops the run. Try at least two approaches before writing it up as open, and then keep going on the rest. |
| ⭐⭐ **A test red ⇒ root-cause it.** | ⛔ Never skip, disable or quarantine a test to get green. A red that predates your work (proved against the base sha) is noted and left. |
| ⭐⭐ **Context running low ⇒ write the status file (done / in progress / next / decisions), commit, push, carry on.** | The status file is the resume point after a compaction — re-read it and this handoff, then continue from **next**. |
| ⭐ **Commit and push after every finished item** (by path, never `git add -A`). | Nothing of the night is lost if the container dies. |

## Where to work

A NEW branch **`exp/threaded-build`** from **`origin/exp/integration`** (`406659433` or newer). Push an empty
`chore: started overnight threaded build at <sha>` commit first. Status file: `docs/blueprints/batches/STATUS_Overnight_Threaded_Build.md` on the
branch. ⛔ Do not merge into any lane. Every new behaviour behind ONE switch, default OFF (the existing `HROT_THREADED_KERNEL` mode); with it off,
0 B allocated per steady frame (R-2084) and behaviour identical to today.

## The design, in one paragraph (all settled — do not re-open)

Two worlds, one writer each: **S** (simulation) written only by the sim thread and its worker pool; **A** (UI) written only by the main thread,
and A stays one object for life. **Two staging buffers** (R-4216, user `2026-11-12`): after each tick the sim copies the masked state into the
staging buffer the UI is NOT pulling, then marks it "latest complete" with an atomic swap — ⭐ it never waits: if both buffers are busy (the UI is
still pulling the one the sim needs), the sim **skips publishing that tick** and the next tick's publish carries everything accumulated. The UI,
at its frame start, pulls the latest complete into A only when it is newer than what A has. Events, logs and edit outcomes are queued with each
publish and drained into A in order, exactly once — skipped ticks included. UI → S goes ONLY through **one sim job queue** drained on the sim
thread between ticks (UI writes, run controls, HTTP work, the UI side of the orchestration bus) — D-5: the existing write code runs on the sim
thread, retargeted from A to S. Inside the tick, the **per-entity rule** (R-4212 / R-4214): a system writes its own entity directly; other
entities by field-level / append command, merged in a fixed order; reads of types written in the same phase come from a masked last-tick copy
taken serially at the phase start. The whole gizmo block stays on the main thread over A (R-4211). Determinism is owned by the time mode:
stepped mode must give the same result run after run and at any thread count; continuous mode need not (R-4214, user).

## The work — in this order (shared seams first, each built ONCE)

| # | build | tested when |
|---|---|---|
| **N-1** | ⭐ **the one sim job queue** — thread-safe, drained by the sim thread at a fixed point between ticks, in post order; a job may reply (an edit outcome) through the publish queue | rail: posted from several threads ⇒ applied on the sim thread, in order, once |
| **N-2** | ⭐ **tick context + current entity on every pool batch** (F-1); in a parallel phase, an access with no current entity throws in Debug | rail, red-proved |
| **N-3** | ⭐ **the two staging buffers + the publish queue** as described above; the old lock between the threads REMOVED; the mask = the active gizmo layers + the selected rows + the singletons (§14.6) | rails: ① the UI never sees a torn tick (a test system stamps the tick into every entity; a reader asserts one stamp per frame, with a ~5–16 ms frame); ② publish faster than the UI pulls ⇒ every event once, in order; ③ the sim never blocks (lock-acquire time ≈ 0); all red-proved |
| **N-4** | ⭐ **the orchestration bus**: the UI publishes and reads only through N-1 / the publish queue — the K-1 "Collection was modified" race | the K-1 repro: 0 of 5 |
| **N-5** | **run controls** (play, pause, step, Run-to) as N-1 jobs; the clock lives with the sim (§2) | rail per control |
| **N-6** | ⭐ **all UI writes through N-1** — all 133 sites (§1). Drag / rotate / place: a ghost in A every frame, the drop ONE job, snap-back when refused (U-3; `EntityDragGizmo.cs:278` today writes `SimTransform` every frame). Inspector edits through `RepositoryAdapter` (U-4). Scenario new / load: the clear on S, then a full resync of both buffers and A. Callers that read a result at once (`EditTicket` readers `ScenarioClockEdit:49`, `FormView:504`, `KnowledgeOrders:17`, the snap-backs …): give each a pending state that completes when the outcome arrives — judge each, record the non-mechanical ones in DECISIONS | a rail per kind (drag, refused drop, field edit, scenario load); a Debug guard throws on any write to S off the sim thread, and the guard stays silent through the scripted session |
| **N-7** | ⭐ **undo / redo on S** — `WorldUndoRecorder` on S (U-1); the before-snapshot taken on the sim thread (a drag's "before" = S at the drop); undo / redo as N-1 jobs | undo → redo → undo of a drag, a field edit and a spawn: the world equals the expected state |
| **N-8** | **the HTTP routes** (§3, 223): reads answered at a tick boundary, writes as N-1 jobs | the routes' own rails; a write route under the threaded switch lands on the sim thread |
| **N-9** | ⭐ **managed components are never changed in place** — audit every in-place write of a managed component; convert each to build-new + `SetManagedComponent` (CLAUDE.md "NEW COMPONENTS") | the list with `file:line` in the status file; the N-3 reader also hashes each managed component at pull and at frame end — equal |
| **N-10** | ⭐ **the per-entity rule in the repository** (R-4212 / R-4214 ④⑤, §18–§18.2): the last-tick masked copy taken serially at the phase start (mask = generator-declared reads + hand-written declarations); a read of another entity's in-phase-written type served from it; a write to another entity → command (F-5), merged in fixed order; structural changes deferred (F-6); `Bus.Publish` in the phase → the batch's list (F-2) | a rail per path; 0 B per steady tick; zero cost with the switch off |
| **N-11** | ⭐ **convert the systems to per-entity parallel** — the brain first (its ~36 ms serial walk: BT / HSM / blueprints / actions; child sensors deferred + idempotent while pending, F-8; callouts and the order journal as commands; per-thread scratch), then the census list (21 types, R-4212). Shared caches, scratch, random generators, statics found on the way are BUGS — fix them (per-thread / per-entity / seeded per entity) | ⭐ stepped mode: the same world hash over N steps at 1 / 2 / 4 / 8 threads AND across two runs; the Debug guard silent; red-proved by removing one deferral |
| **N-12** | the behaviour effects the review predicted: callout chorus — de-dup at playback (F-10: one "Contact" per burst within 10 m / 2 s); cover ties (F-9); squads per squad (F-11) | counts before / after on a contact-onset run |
| **N-13** | **`--mode all`**: every kernel of the process on the ONE sim worker in sequence, one N-1 queue per kernel; the shared statics (`ParallelEntities.Mode`, `FdpWorkerPool`, timers) made per-kernel or safe | `--mode all` threaded, 2 min at 1 000 men, 0 faults |
| **N-14** | **Windows EcoQoS opt-out** for windowless nodes (§19, CE-4458 — take it from `exp/windows-scheduling` if pushed, else build it) | the call is made at node start on Windows; a no-op elsewhere |
| **N-15** | ⭐⭐ **IT REALLY WORKS — the end-to-end proof.** ① the threaded editor (`HROT_THREADED_KERNEL=3`) with the brain parallel at 4 threads, the 1 000-man scripted session (`threaded-poc2-measure.py --mode 3`) WHILE PLAYING with drags (one refused), a field edit, undo / redo, a scenario load mid-run, play / pause / step — green twice, 0 faults, the guards silent; ② stepped mode: the same hash at 1 / 2 / 4 / 8 threads with the same edits at the same ticks; ③ switch OFF: the touched features' own suites green and 0 B per steady frame; ④ display rails under `xvfb-run -a` (not skipped) | every row in the status file's GATES table: verbatim command, counts, base-sha proof for any pre-existing red |
| **N-16** | the numbers: sim tick period and UI frame (playing / paused), BrainTick and the whole frame at 1 / 2 / 4 threads, staging-buffer MB at 1 000 men, the editor's total memory; plus ONE Windows script that runs N-15 ① at 1…12 threads | in the status file |
| **N-17** | ⭐ **fold the as-built into the designs** (obligation ⑤): inventory §8, Threaded Presentation §14, Parallel Simulation §18 — prior state marked SUPERSEDED; every DECISION that changed a design is folded in | the design commits named in the status file |

⚠ When N-1…N-17 are all done and green, **keep going**: re-run N-15 ①, look for any remaining non-mechanical UI write site, any system still
serial that the census lists, any guard warning — and fix it. Stop only when nothing in this handoff remains undone or untested.

## How to work

- ⭐ Read the cited design sections and RULINGS R-4208…R-4217 before N-1. Use `scripts/cbm.sh` (graph) AND grep before claiming something exists
  or not.
- ⭐ Sub-agents may build items that touch no shared seam in parallel; ⛔ N-1, N-2, N-3 and N-10 have ONE author (no second queue, context or copy
  path). One build at a time on the machine.
- Test with the touched features' OWN rails (T-1); build the TEST project before `--no-build`; restore test projects once after merging. ⛔ No
  broad suites, no scenario sweeps as proof. Long runs in the background, never polled.
- Hot path: no LINQ, no per-frame allocation (R-2084). ⛔ No screenshots in git. Commits end with the Co-Authored-By and Claude-Session lines.

## The morning report (top of the status file)

1. **DONE** — per item, one line + the commit. 2. **GATES** — the table from N-15. 3. **DECISIONS** — every judgment call, for the user to
review. 4. **BUGS FIXED** — each with the test that now catches it. 5. **OPEN** — anything that resisted two approaches, with what was tried.
6. **NUMBERS** — N-16.

---
_coordinator lane, 2026-11-12_
