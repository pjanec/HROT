<!--STATUS
state: LIVE — coordinator → backend: make the rules / showcase programme's backend features RUNNABLE AND TESTABLE BY HAND (the start path, the catch-up fault, the crate, the End trigger content, live-verified manual-test rows)
updated: 2026-11-03
current-answer: the whole handoff
stale-below: nothing
related-designs:
  - ../../MANUAL_TEST_Scenario_Evaluation.md — ⭐ the deliverable: §1 (how to start) and your rows' steps, run live
  - ../../DESIGN_Scenario_Rules.md — §00.12 (S6, G-1…G-9 ✅), §00.13 (S7, P-1…P-9 ✅ approved 2026-11-03), SR-116 (the instructor's End)
  - ../../SHOWCASE_Scenarios.md — §1.7 Bridgehead, §2.8 Crossfire as built
  - ../../DESIGN_Scenario_Macros.md — the director as a macro (D3)
  - HANDOFF_Coordinator_To_Behaviors_Rules_Runnable.md · HANDOFF_Coordinator_To_Ui_Rules_Runnable.md — the sibling handoffs of this round
-->
# HANDOFF — coordinator → backend: **runnable and testable by hand**

**Dispatched at `ae04e0841`.** ⭐ Merge `origin/coordinator` first. Replaces `HANDOFF_Coordinator_To_Backend_Rules_Finish.md` (withdrawn unstarted;
its ② — Bridgehead's `game.scoreboard` — you already did, `eb88c5159`).
⛔ **Scope is FROZEN at `ae04e0841`.** A later document that invalidates an item ⇒ STOP that item and report (R-106), never the batch.

🔒 **User, `2026-11-03`:** *"rewrite the handoffs … to the level all the features are runnable and testable manually, no need to have all bugs fixed."*
⭐ **P-1…P-9 and G-9 are APPROVED** (user, same day) — §00.13 / G-9 now say so; tick CE-3363.

| # | item | basis | ⭐ lean / done when |
|---|---|---|---|
| **①** | ⭐⭐ **How a person starts it** — write §1 of [`MANUAL_TEST_Scenario_Evaluation.md`](../../MANUAL_TEST_Scenario_Evaluation.md): the exact command(s) (`run_All.sh` / `.bat`, or editor + player), how to load Bridgehead / Crossfire LIVE, how to take a player seat, where the recording lands | `RUNBOOK_Cluster_Debugging_Over_Http.md`, the run scripts | a person on a fresh checkout follows it and reaches a running Crossfire round |
| **②** | ⭐ **CE-3366** — a slow node's C7 catch-up re-sends the same `ExerciseId`, the recorder refuses its own file, the cluster goes to error. ⚠ A laptop under load hits this, so it blocks manual testing | the row; find the C7 owning design and link it both ways | ⭐ a re-prepare of an exercise already prepared reuses that node's file. Done: a rail preparing twice with one id; a live run under CPU load that settles. ⭐ Name the integration suite that exercises catch-up (rule 8 row 8) |
| **③** | **The instructor-End content** — every showcase carries an `instructor-end` trigger (no `when`; its Do = the scenario's own end, e.g. `EndExercise { reason: "Instructor ended", winner? }` per the lead) so ui's End button (their ②) fires it (SR-116: the same path as an automatic end) | SR-116 | both generators + regenerated; firing it by `POST /objectives/{id}/control` ends the round with a result |
| **④** | **The supply crate's hold-for-tickets objective** (Crossfire: the crates are inserted, nothing rewards holding one) | SHOWCASE §2.8 | ⭐ inside `game/supply-crate`: a `HoldArea` on the crate with `points` / an `AddToCounter` on hold, so the existing machinery does it (no new kind). Done: a live round where a held crate moves the tickets |
| **⑤** | **The `Untouchable` award's kills floor** (fewest deaths, today no floor — a man who never fought wins) | P-7 | Done: a rail — 0 kills, 0 deaths does not win |
| **⑥** | **Answer ui on CE-4224** (null entity saves as the empty GUID) — lean: keep it | `NOTE_Ui_To_Backend_Scenario_Save_Null_Entity.md` | a short reply note |
| **⑦** | ⭐⭐ **Write and LIVE-RUN your rows** of the guide: **C1** (capture, tickets, respawn waves, the winner named) · **C2** / **C3** data half (score, streaks, bounty, awards) · **C5** (crate) · **D1** placement half (macro marker, member lines, transient dimmed) · **D3** (the director's counter-attack, the skill amplifier) · **E2** preset half (training vs game HUD by preset) | the guide §2 | each row: exact steps → what you see; *verified* = branch@sha + date. A bug that stops a row ⇒ fix if small, else a tracker row named in the guide. ⚠ Known and NOT required: ~1 in 10 downings uncredited (behaviors' note), point A never visited (CE-3362) — note them in C1 / C2 |

⛔ **Not here:** CE-3365 (score macros drawn as map labels) — ui's one *"seeable on the map"* rule (their ⑧).

## Gates — the report contract (CLAUDE.md rule 8)
One row per gate · T-1 feature suites first · ② is cross-node ⇒ its integration suite run or a stated reason · reds confirmed pre-existing against `ae04e0841` ·
tree clean · ids from **your** block · ⭐ **the guide rows you verified, by id** · designs / SHOWCASE updated as built.

_coordinator lane, 2026-11-03_
