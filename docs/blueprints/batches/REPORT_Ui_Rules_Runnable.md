<!--STATUS
state: LIVE — ui → coordinator: the report of HANDOFF_Coordinator_To_Ui_Rules_Runnable (dispatched ae04e0841)
updated: 2026-11-04
current-answer: the whole report
stale-below: nothing
related-designs:
  - HANDOFF_Coordinator_To_Ui_Rules_Runnable.md — the handoff this reports on
  - ../../MANUAL_TEST_Scenario_Evaluation.md — ui's rows (item ⑩)
  - ../../DESIGN_Rules_View_UI.md — §7.10, §7.11, §8 (as built) · ../../DESIGN_Macro_Authoring_Editor.md §10 · ../../DESIGN_Player_HUD_And_Debrief.md §9 · ../../DESIGN_Node_Features.md §12 · ../../DESIGN_Entity_Hierarchy_Views.md §6
-->
# REPORT — ui: **Rules Runnable — every item done; ui at `7ee2f308d`**

Started at `32ba41d8d` (merged `coordinator@f384b11fa`). Scope frozen at `ae04e0841`: nothing invalidated an item.

| # | item | outcome | ids · commit |
|---|---|---|---|
| ① | close-outs | ✅ CE-4167 (TL-X1…X5 all in §6/§6d), CE-4230, CE-4232, CE-3365 + CE-4152 (via ⑧) ticked; CE-4229's preset half ✅, bounty 2-D half via ⑨ | `fb466ef30` |
| ② | the instructor's End | ✅ Rules toolbar + Mission summary, confirm modal, fires the `instructor-end` RefKey trigger via `RuleCommands.Control`; the Mission summary opens on Ended. Live Crossfire: *BLUE WINS · Instructor ended* | CE-4240 · `fb466ef30` |
| ③ | what went to whom | ✅ `PlayerMessageEvent.Source` was already on the wire, the store dropped it; the Overview lists each message, address and time, and the Do tab shows the same under each Message step. Live Bridgehead: *0:05 to everyone …*. ⚠ node memory, not the recording | CE-4241 · `fb466ef30` |
| ④ | round clock + Coming up | ✅ counts down to the soonest armed trigger with `EndsExercise` (behaviors' bit, every node); Coming up lists crate drops / sudden death | CE-4238 · `b73dc5c8b`, `7ee2f308d` |
| ⑤ | live-edit refusal | ✅ `EditOutcomeLog` (behaviors CE-2380): Pending → Applied / Refused with the WHOLE reason, *"the owner did not answer"* after 5 s; also for the RV-5 buttons. ⚠ shown under the Apply bar (the reason names the step), not under the field | `7ee2f308d` |
| ⑥ | game-master palette | ✅ design rules view §8 (GM-1…GM-5), built: a transient one-shot trigger through the one creation path; `gm.palette` feature. Measured: a live-created rule needs the genesis `InitialBrainIntent`. Live: SetSkillAmplifier Red −1 ⇒ fired once, amplifier −1 | CE-4243 · `13b81077c` |
| ⑦ | MA-10 macro preview | ✅ design macro authoring §10 (MP-1…MP-4) — ⚠ **deviates from the handoff's lean**: ghosts from the pure expander, never a transient instance (measured: an inserted QRF's men act from the first tick, so a cancel could not undo them). Live: preview *11 to create*, entities 214 → 214; Insert now → the QRF | CE-4244 · `13b81077c` |
| ⑧ | ONE seeable-on-the-map rule | ✅ `Seeable.OnMap` (Hrot.Common) for the map's gizmos AND the ORBAT; the Scenario / rules views still list virtual entities. ⚠ no transform required (body-less commanders stay in the ORBAT). Live Crossfire: no score labels, ORBAT 12 rows | CE-4239 · `fb466ef30` |
| ⑨ | small features | ✅ bounty marker on the map card · `hud.coaching` (OFF by default) · `FeatureGridWindow` (live: `hud.kill-feed` off ⇒ the feed vanished). ⚠ the bounty gizmo is not feature-gated (no gizmo reads a node feature); the grid's source column cannot tell a scenario preset from a role default | CE-4246, CE-4247 · `b73dc5c8b` |
| ⑩ | manual-test rows | ✅ written and run live — see the table below | `5f3f519ee` and later |

**Rows of `MANUAL_TEST_Scenario_Evaluation.md` (ui's halves):** ✅ A1 A2 A3 A4 A5 A6 A7 A8 B2 B4 D2 E1 E2 E3 · ⏳ partial B3 (the recording case not run) · C4 (recording not run) · B4's one-man case (needs an authored rule) · C3's 2-D card (railed; a 5-kill streak is rare) · D1's instance marker / member lines not isolated. ⚠ run in the editor window only — a separate Player window (`run_All.sh`) was not driven.

**Defects found running the rows:**
- CE-4248 ✅ fixed: Overview links overprinted, and the coaching hint showed a GUID.
- CE-4249 ✅ root-caused: a When-less trigger Apply was refused silently; it now says why.
- CE-4250 — ui half ✅: Save during Preview is refused. **Backend half open:** `TargetMemoryTranslator.Extract` throws on a runtime reference outside the save map (`NOTE_Ui_To_Backend_Save_During_Preview.md`).
- CE-4245 refuted: an Xvfb focus trap, not a product bug; the runbook has the trap.

## Gates *(rule 8)*
| gate | result |
|---|---|
| `Hrot.Presentation.Tests` full, `xvfb-run -a` (last run, `7ee2f308d`) | 1511 passed · 2 skipped (audio) · 1 failed = **CE-4242** `CE4115_TheLoadingScreen…`, red identically before this batch (base-sha proof in the row) |
| feature suites (T-1): `RulesModel*`, `RuleEditor*`, `RunnableRules*`, `PlayerHud*`, `KillFeed*`, `MacroPreview*`, `FeatureGrid*`, `StringFieldTypingFrameRail`, `Hrot.IG.Tests` gizmos (62) | all green in the last runs; each item red-proved by a compiling mutation (named in its commit / as-built) |
| tracker | `tracker-counts.py --check` OK; ids from ui's block: CE-4239…CE-4250 |
| designs | as-built folded: rules view §7.10, §7.11, §8.3 · macro authoring §10.3 · HUD §9 · Node Features §12 · Hierarchy Views §6; mermaid-check passes |
| cross-lane files (named in their commits) | `Hrot.Common` (Seeable, MacroPreview, `MacroExpansionSystem.Expander`, EntityCardGizmos) · `Hrot.Core` `HudFeatures` · `Fdp.Presentation` / `Fdp.Core` logging (the Message Log bound, earlier batch) |

**Still open (not ui's or out of this batch):** CE-4250's backend half · CE-4233 (the debrief head mark needs the recorded event) · CE-4235 (the one-off ~1 s/frame) · CE-4242 (the loading-screen red).

_ui lane, 2026-11-04_
