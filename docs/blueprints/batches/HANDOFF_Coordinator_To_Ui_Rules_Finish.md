<!--STATUS
state: WITHDRAWN AND REPLACED 2026-11-03 (never started — no lane contained 313f42a67) by HANDOFF_Coordinator_To_Ui_Rules_Runnable.md; user: "rewrite the handoffs (no one took them), to the level all the features are runnable and testable manually, no need to have all bugs fixed"
superseded-by: docs/blueprints/batches/HANDOFF_Coordinator_To_Ui_Rules_Runnable.md
updated: 2026-11-03
current-answer: the whole handoff
stale-below: nothing
related-designs:
  - ../../DESIGN_Rules_View_UI.md — R1…R6 (RV-1…RV-9, RE-1…RE-10 + §7.4), approved
  - ../../DESIGN_Player_HUD_And_Debrief.md — H1, H2, D1, G1 built; PH-8 amended (R-4115)
  - ../../DESIGN_Scenario_Macros.md — SR-178 the macro instance marker, SR-191 transient members dimmed
  - ../../DESIGN_Entity_Hierarchy_Views.md — HV-1…HV-5, the entity tree (CE-4152's ORBAT)
  - HANDOFF_Coordinator_To_Behaviors_Rules_Finish.md · HANDOFF_Coordinator_To_Backend_Rules_Finish.md — the sibling handoffs of this round
-->
# HANDOFF — coordinator → ui: **finish the rules view / HUD programme's ui half**

**Dispatched at `313f42a67`** (`coordinator` = `ui@67bd589e8` + `backend@5599f85da`). ⭐ Merge `origin/coordinator` first (backend's G-9 winner and CE-4232 target are there).
⛔ **Scope is FROZEN at `313f42a67`.** A later document that invalidates an item ⇒ STOP that item and report (R-106), never the batch.

## Items, in order

| # | item | design basis | ⭐ lean / acceptance |
|---|---|---|---|
| **①** | **Close the rows others finished** — CE-4230 (G-8 capture facts, built by backend + read by W-H3) · CE-4232 (Score target 1000, backend `5599f85da` — check *your best moment* now rises) · CE-4229's P-7 half (presets built; ⭐ the **bounty 2-D map marker** half stays open — build it here if small) · CE-4167 (TL-X1…X3, if R1/R4 cover it all) | the rows | each row ticked with its evidence, or left open naming what remains |
| **②** | **G-9 check** — `EndExercise { winner }` now names the outcome; your `GameScore.Build` already reads `SideResult.Outcome` (`GameScore.cs:113`), so it should need nothing | `DESIGN_Scenario_Rules.md` G-9 | a live Crossfire round shows *BLUE WINS / RED WINS / DRAW*; a model rail on a synthetic Draw if none exists |
| **③** | ⭐⭐ **ONE "seeable on the map" rule — CE-3365 + CE-4152 together.** CE-3365: every per-man score macro instance draws a map label (*game/score/Default #1781*); CE-4152: the ORBAT must list only entities seen on the 3-D map (user: *"there shouldn't be any virtual entities that are not seeable"*) | SR-178 · HV-1…HV-5 · CE-4152's row | ⭐ ONE predicate (has a physical presence — a body / a mesh, not an objective / trigger / evaluation) used by BOTH the map's macro marker (an instance with no seeable member draws no marker) and the ORBAT filter; the Scenario view of the entity tree and the rules view still show the virtual ones. ⛔ not two filters. Rails: both surfaces on one fixture |
| **④** | **CE-4238 the round clock** — waits on behaviors' handoff ② (`ExpectedAt` from `ScenarioTime`) | PH-4 | ⭐ count down to the `ExpectedAt` of the trigger whose Do contains `EndExercise` (no new field, agreed). Until it lands, keep counting up. Also R4's *Coming up* gains every timed trigger for free — check it |
| **⑤** | **RE-7 refusal under the field / step** — waits on behaviors' handoff ③ (full refusal text in the journal, keyed by your Seq) | RE-7 | show it under the field or step it names; *pending* from your own Seq until the applied / refused entry appears |
| **⑥** | **CE-4224 unit rail** you promised (null entity saves as the empty GUID and loads back) | the note to backend | the rail; backend answers the intent (their ③) |
| **⑦** | **Small open rows:** CE-4199 (an untouched recipe turns dirty once its traffic runs — `ScenarioIgnoreTag` entities must not dirty) · CE-4219 (fold the remaining undo / condition duplicates into the shared `EditHistory` and predicate leaf) · CE-4233 (the debrief's *(head)* mark needs the recorded downing's body part — ⭐ lean: ask behaviors to put the part on the `combat.downed` lane rather than reading raw events; a note, not a build here) | the rows | each row's own *done when* |

⛔ **Not here:** MA-10 (preview of a runtime insert — still undesigned), new screens, CE-4235 (unreproduced; leave open unless it recurs).

## Gates — the report contract (CLAUDE.md rule 8)
One row per gate · the feature suites first (T-1: `RulesModel*`, `GameScore*`, `Debrief*`, the map layer and ORBAT suites for ③) · display rails under `xvfb-run -a` with counts (a skip is not a pass) · every red confirmed pre-existing against `313f42a67` · tree clean · ids from **your** block · designs updated as built.

_coordinator lane, 2026-11-03_
