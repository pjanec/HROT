<!--STATUS
state: LIVE — coordinator → ui: make every objectives / evaluation / macro screen RUNNABLE AND TESTABLE BY HAND — the missing pieces (End button, what went to whom, the GM palette, MA-10 preview, coaching, feature grid, the seeable rule, bounty marker) + live-verified manual-test rows
updated: 2026-11-03
current-answer: the whole handoff
stale-below: nothing
related-designs:
  - ../../MANUAL_TEST_Scenario_Evaluation.md — ⭐ the deliverable: your rows' steps, run live
  - ../../DESIGN_Rules_View_UI.md — RV-1…RV-9, RE-1…RE-10 + §7.4 (built R1…R5c)
  - ../../DESIGN_Player_HUD_And_Debrief.md — H1…H3, D1, G1 built; PH-3 coaching, PH-8 amended
  - ../../DESIGN_Macro_Authoring_Editor.md — MA-10 (preview of a runtime insert), undesigned
  - ../../DESIGN_Scenario_Rules.md — SR-104 (game-master controls), SR-106 (messages), SR-116 (the instructor's End)
  - ../../DESIGN_Node_Features.md — the `FeatureGridPanel`
  - ../../DESIGN_Entity_Hierarchy_Views.md — the ORBAT / Scenario view (CE-4152)
  - HANDOFF_Coordinator_To_Behaviors_Rules_Runnable.md · HANDOFF_Coordinator_To_Backend_Rules_Runnable.md — the sibling handoffs of this round
-->
# HANDOFF — coordinator → ui: **every screen runnable and testable by hand**

**Dispatched at `ae04e0841`.** ⭐ Merge `origin/coordinator` first. Replaces `HANDOFF_Coordinator_To_Ui_Rules_Finish.md` (withdrawn unstarted).
⛔ **Scope is FROZEN at `ae04e0841`.** A later document that invalidates an item ⇒ STOP that item and report (R-106), never the batch.

🔒 **User, `2026-11-03`:** *"rewrite the handoffs … to the level all the features are runnable and testable manually, no need to have all bugs fixed."*
⭐ P-1…P-9 and G-9 are APPROVED. ⭐ Items ②…⑨ are features still missing for a person to try the whole area; the bugs CE-4199, CE-4219,
CE-4233, CE-4235 are **NOT** in this batch. ⭐ Items ⑥ and ⑦ need a short design first — any lane designs (`2026-10-02`); put the UML in the
owning design, not here; ⚠ a decision with a large blast radius goes to the user with a lean.

| # | item | basis | ⭐ lean / done when |
|---|---|---|---|
| **①** | **Close-outs** — tick CE-4230 (G-8), CE-4232 (target 1000), CE-4229's preset half, CE-4167 if R1/R4 covered it | the rows | rows ticked with evidence or naming what remains |
| **②** | **The instructor's End button** (rules view toolbar + mission summary): fires the scenario's `instructor-end` trigger through `RuleControl` *Fire now* — backend adds that trigger to both showcases (their ③). No such trigger ⇒ the button is disabled, saying why | SR-116 | the round ends with a result; the summary / debrief / game summary open on it |
| **③** | **What went to whom** — in the rules view, a message step / message row shows its recipients (side, unit, player) and when; read from what the message path already records | SR-106, §8 of the explainer | ⭐ measure the data first; missing ⇒ add it cross-lane (say so) or a note to the owner. Done: Bridgehead's briefing row names its recipients |
| **④** | **The round clock + *Coming up* timed triggers** (CE-4238) — after behaviors' ① | PH-4, R4 | Crossfire's clock counts DOWN to End (the trigger whose Do has `EndExercise`); crate drops appear in *Coming up* |
| **⑤** | **The live-edit refusal under the field / step** — after behaviors' ② | RE-7 | a bad live edit shows its full reason under the field it names |
| **⑥** | ⭐ **Game-master palette** (SR-104): *run any shared action by hand* (the `ActionCatalogue` picker + the args form the Do tab already uses) and a **per-side skill slider** (a convenience over `SetSkillAmplifier`). ⭐ lean: the engine path is a **transient one-shot trigger** (no `when`, `maxFires` 1, Do = that step) created through the ONE entity-creation path — no new command. ⭐ lean: **MSEL = the rules view's *Coming up*** + the timeline, no separate panel — say in the design if you disagree | SR-104 · RE-4 | design note + build; done: from the palette, *Message* to Blue and *SetSkillAmplifier* Red 0.5 both happen live and appear in the rule's History |
| **⑦** | ⭐ **MA-10 — preview an `InsertMacro` / *Add ▸ Macros* before committing**: a transient instance at the step's anchor and args (MA-7's creation with `IsTransient`), drawn dimmed; *Insert* keeps it, *Cancel* deletes it | MA-10 (undesigned) | design in `DESIGN_Macro_Authoring_Editor.md` + build; done: preview a counter-attack macro, cancel — nothing remains; insert — it runs |
| **⑧** | ⭐⭐ **ONE "seeable on the map" rule — CE-3365 + CE-4152**: one predicate (has a physical presence) used by the map's macro marker (an instance with no seeable member draws none) AND the ORBAT filter; the Scenario view and rules view still list the virtual ones. ⛔ not two filters | SR-178 · HV-1…HV-5 · CE-4152 | Crossfire shows no *game/score/Default* labels and no score entities in the ORBAT |
| **⑨** | **Remaining small features:** the **bounty 2-D map marker** (CE-4229's other half) · the **coaching hint** `hud.coaching` (PH-3; `RuleLeaves` now exist — show the man's first failing standard leaf as the hint; off by default) · the **`FeatureGridPanel`** (nodes × features, toggled through the run settings) | PH-3 · PH-4 · Node Features | each visible live; the grid turns `hud.kill-feed` off on the Player and it disappears |
| **⑩** | ⭐⭐ **Write and LIVE-RUN your rows** of [`MANUAL_TEST_Scenario_Evaluation.md`](../../MANUAL_TEST_Scenario_Evaluation.md): A1–A8 screen halves · B2–B4 · C2–C4 screen halves · D1 editor half · D2 · E1–E3 | the guide §2 | each row: exact steps → what you see; *verified* = branch@sha + date; a known bug that does not stop the row is noted in it |

⭐ **Order:** ① ② ③ ⑧ (no waits) → ⑥ ⑦ (design, then build) → ④ ⑤ as behaviors pushes → ⑨ → ⑩ last, on the merged tree.

## Gates — the report contract (CLAUDE.md rule 8)
One row per gate · T-1 feature suites first (`RulesModel*`, `RuleTabs*`, `GameScore*`, `Debrief*`, map layer / ORBAT suites) · display rails under `xvfb-run -a`
with counts (a skip is not a pass) · reds confirmed pre-existing against `ae04e0841` · tree clean · ids from **your** block · ⭐ **the guide rows you verified, by id** ·
designs updated as built (⑥ ⑦ carry their UML).

_coordinator lane, 2026-11-03_
