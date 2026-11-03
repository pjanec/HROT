<!--STATUS
state: WITHDRAWN AND REPLACED 2026-11-03 (never started — no lane contained 313f42a67) by HANDOFF_Coordinator_To_Backend_Rules_Runnable.md; user: "rewrite the handoffs (no one took them), to the level all the features are runnable and testable manually, no need to have all bugs fixed"
superseded-by: docs/blueprints/batches/HANDOFF_Coordinator_To_Backend_Rules_Runnable.md
updated: 2026-11-03
current-answer: the whole handoff
stale-below: nothing
related-designs:
  - ../../DESIGN_Scenario_Rules.md — §00.12 (S6, G-8, G-9), §00.13 (S7, P-1…P-9 leans — await the user)
  - ../../DESIGN_Node_Features.md — §11 P-7 the scenario feature layer (F-27…F-31, F-28′)
  - ../../SHOWCASE_Scenarios.md — §1.7 Bridgehead, §2.8 Crossfire as built
  - HANDOFF_Coordinator_To_Behaviors_Rules_Finish.md · HANDOFF_Coordinator_To_Ui_Rules_Finish.md — the sibling handoffs of this round
-->
# HANDOFF — coordinator → backend: **finish the rules / showcase programme's backend half**

**Dispatched at `313f42a67`** (`coordinator` = `ui@67bd589e8` + `backend@5599f85da`). ⭐ Merge `origin/coordinator` first.
⛔ **Scope is FROZEN at `313f42a67`.** A later document that invalidates an item ⇒ STOP that item and report (R-106), never the batch.

## Items, in order

| # | item | design basis | ⭐ lean / acceptance |
|---|---|---|---|
| **①** | ⭐⭐ **CE-3366** — a slow node's C7 catch-up re-sends `PrepareState` with the SAME `ExerciseId`; the node's recorder refuses to recreate `node_<id>.fdp` ⇒ *"Brain (CGF) lost"*, the cluster goes to error (found live, CPU loaded) | the row in [`Blueprint_Issues_Tracker.md`](../Blueprint_Issues_Tracker.md); the C7 catch-up's owning design (find it — `docs/` first, then `.dev/`, and link it both ways) | ⭐ a re-prepare of the exercise a node ALREADY prepared is idempotent: the recorder reuses (or reopens) its own file for the same `ExerciseId` rather than throwing; ⛔ not a new file name per attempt. **Acceptance:** a rail that prepares twice with one id and records; a live slow-node run (CPU load) that settles |
| **②** | **`game.scoreboard = off` in Bridgehead's preset** — [`NOTE_Ui_To_Backend_Scoreboard_Preset_Line.md`](NOTE_Ui_To_Backend_Scoreboard_Preset_Line.md) | P-7 / PH-8 amended | the generator line + regenerate; `GET /features` shows source *scenario* for it |
| **③** | **Answer ui on CE-4224** (a null entity now saves as the empty GUID, ui's cross-lane fix in `ScenarioSerializer`) — [`NOTE_Ui_To_Backend_Scenario_Save_Null_Entity.md`](NOTE_Ui_To_Backend_Scenario_Save_Null_Entity.md) | S8 (no network ids in files) | ⭐ coordinator's lean: **keep the empty GUID** (a row's slot keeps its position; `Entity.Null` round-trips). Check your showcase generators write the same; a short reply note |
| **④** | **Close-outs:** CE-3363 (S7 built + live — tick it, the P-rows stay leans until the user rules) · CE-3362 re-measure after behaviors' CE-3361 ② (likely one cause — their handoff ④) | §00.13 | tracker rows state the evidence; `tracker-counts.py --check` |
| **⑤** | **The awards' `Untouchable` kills floor** (⏳ in §00.13's content line — *fewest deaths* with no floor rewards a man who never fought) | P-7 | ⭐ `min` on a second measure, or the existing `min` on kills via a helper measure — your call, said in §00.13. Rail: a man with 0 kills and 0 deaths does not win |

⛔ **Not here:** CE-3365 (score-macro instances drawn as map labels) — it goes to **ui** with CE-4152 as ONE *"is it seeable on the map"* rule (ui handoff ③). ⛔ No change to the P-leans.

## Gates — the report contract (CLAUDE.md rule 8)
One row per gate · feature suites first (T-1: the recorder / C7 / prepare suites for ①, `ScenarioRulesTests.Score` for ⑤) · ① is cross-node ⇒ **name and run the integration suite that exercises the catch-up** (row 8) or state with evidence why it cannot gate · every red confirmed pre-existing against `313f42a67` · tree clean · ids from **your** block · designs updated.

_coordinator lane, 2026-11-03_
