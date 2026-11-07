<!--STATUS
state: LIVE — dispatched, not started
updated: 2026-11-07
current-answer: the whole file
stale-below: nothing
known-rot: none
known-conflict: none
related-designs:
  - ../../UX/DESIGN_Mission_Plan_Editor.md — THE design (READY-TO-BUILD): §5.2 D1–D9, §5.3–§5.4 UML, §5.5 stages, §5.6 picker, §5.7 storage, §5.8 E1–E8, §5.9 sequencing + E9 Wait (R-4160, R-4161)
  - ../../DESIGN_Readable_Scenario_Json.md — item P-1 (R-4159)
  - PROTOCOL_Two_Lanes_Self_Coordinating.md — the SAME protocol: status file after every item, fetch, merge, notes; anything needing the user is skipped (R-4156)
  - HANDOFF_Coordinator_To_Ui_Rules_Bundle_And_Takeover.md — the ui lane finishes that one first, then joins here
-->

# HANDOFF — coordinator → backend and ui: the mission plan editor, end to end

**For:** `backend` first (it is idle), `ui` when its current handoff is done. **From:** the coordinator lane. **Dispatched at `<see commit>`**
(the commit that adds this file). ⭐ Scope FROZEN there; later documents are FYI.
Merge `coordinator` first, push `chore: started HANDOFF_Coordinator_To_Lanes_Mission_Plan at <sha>`, then the
[protocol](PROTOCOL_Two_Lanes_Self_Coordinating.md) exactly as before — add the rows below to your `STATUS_Editing_UX_Build_<lane>.md`.
⛔⛔ **Anything that needs the user's decision is skipped, never blocking (R-4156)** — a QUESTION note with your lean, then the next item.
⭐ **Roles are not honoured** (user, `2026-11-07`): an item is CLAIMED by writing *"P-n: started (lane)"* in your status file and pushing it
BEFORE writing code; ⛔ never start an item the other lane's status file already claims.

## Why

🔒 User, `2026-11-07`: *"Lets focus on mission plan editor. What does it need?"* → §5 approved (*"Okay, approved"*, R-4160) → E1–E9 and the
sequencing rule approved (*"Approved"*, R-4161; *"I would expect that they are executed one by one"*).

## The items — in order (design: [DESIGN_Mission_Plan_Editor.md](../../UX/DESIGN_Mission_Plan_Editor.md) — build the §5.3 classes and §5.4 sequences; report match / deviation per obligation ③, fold any deviation back per ⑤)

| # | id | what | design basis | done when | ⏳ needs | suggested lane |
|---|---|---|---|---|---|---|
| **P-1** | CE-4295 | readable scenario JSON: `[JsonText]` + `JsonTextConverter` on `SavedBrainSlot.Params`, `DomainMissionTask.BehaviorParams`, `DomainMissionTrigger.Params` (+ the inventory's others); the 117 files rewritten ONCE in one commit | [Readable JSON](../../DESIGN_Readable_Scenario_Json.md) §2, Q1–Q4 | a round-trip rail per marked field (string → object → same string; old file → load → identical world); the scenario suites unchanged | — | backend |
| **P-2** | CE-4298 | **M1** — D1 `Order.<Kind>` accepted without the warning · **G1** an order task's params resolved on load · D2 ONE start path (director advances, adapter starts; no Idle frame) · D3 old trigger types translated to the one condition (`PlanConditionCache`) · ⭐ **§5.9: an empty condition = *when done*** in `ResolveTrigger`, the three editor defaults (`MissionPanel.cs:240`, `DebugApiService.Missions.cs:198`, `NedSimHostMissionSender.cs:53`) deleted, the `…NoTriggerHoldsForever` rail flipped · **E9** a Wait task (empty name) starts nothing and clears the previous behaviour (origin Superior) | §5.2 D1–D3, §5.5 M1, §5.7 G1, §5.9 | §5.5 M1's acceptance + a plan of three bare tasks runs one by one + a Wait-first plan holds the SOP until its *then* | P-1 (the params land as objects) | backend |
| **P-3** | CE-4299 | **M1b** — D7: 16 slots; the phase→task renames through **Roslyn** (⛔ never a text rename) | §5.2 D7 | builds; the plan suites unchanged | P-2 | backend |
| **P-4** | CE-4300 | **M4 runtime** — E1 progress carried over BY TASK ID on a replace; E7 append; E8 HTTP `add_mission_task` through the same model; the version precondition (Conflict) | §5.8 E1, E7, E8; D9 | an edit of task 4 while task 2 runs leaves task 2 running (elapsed kept); a two-station conflict rail | P-2 | backend |
| **P-5** | CE-4301 | **M5 runtime** — D6 the `TaskFinished` leaf; **G2** the conditions' references resolved on load (GUID / `@Key`) | §5.2 D6, §5.7 G2 | a unit waits on another's task 3; a file with `@Key` in a condition loads to the right entity; 0 B per steady frame | P-2 | backend |
| **P-6** | CE-4302 | **M6 runtime** — E3 `"phase": n` in `ObjectiveCommon` (+ unmanaged twin); *Begin now* = `RuleRequest.Fire`; the *when Phase N begins* condition | §5.2 D5, §5.8 E3 | a phase begun by the instructor releases two units' waiting tasks | P-5 | backend |
| **P-7** | CE-4303 | **M2 + M4 UI** — D8 the Orders tab (task sentences, the order form via `OrderMenu.OfferedFor`, the *then* picker §5.6 with WHO = *this unit* (E5), *give up after* (D4), Wait first in *+ Add task* (E9), progress, Jump / Stop); E6 a group shows its commander's plan; D9 Edit-mode undo / Live Commit, Conflict → Reload / Overwrite; `MissionPanel` retired | §5.2 D8, D9, §5.6, §5.8 E5, E6, §5.9 E9 | side-by-side with `mission-plan-editor.svg` + `mission-plan-detail-visual.svg` (builder checks fidelity itself, R-4146) | P-2 (P-4 for Live) | ui — or backend if ui has not claimed it when backend reaches it |
| **P-8** | CE-4304 | **M3 + M5/M6 UI** — the plan on the map (E2: marker at the first place field, the route, drag = edit, *Add task ▸*); the picker's *Advanced condition… · When a rule fires… · New rule…*; the Rules window's *Phases* group; the Timeline's ONE *Tasks* lane (E4) | §5.5 M3, M5, M6; §5.8 E2, E4 | side-by-side with `mission-plan-detail-visual.svg`; a 1000-unit scenario's timeline stays one lane | B-11 (action catalogue) for *Add task ▸*; P-5, P-6 | ui |

⛔ **Not here:** recording-format changes (the user: *"Recording is not important"*), map interaction P3 / P7, the Replay Browser's Q-B3 / Q-B6.

## Gates (report per item — protocol §6)

⭐ The CLAUDE.md gate contract, rows 1–8. ⭐ Row 8 here: P-2 / P-4 change the plan engine on every node ⇒ name and run the mission suites
(`MissionControlExecutionSystemTests`, `MissionPlanTranslatorTests`, `EntityMissionIngressTranslatorTests`, the SimHost integration mission
rails) and the scenario suites. ⭐ P-1 rewrites 117 files ⇒ the scenario load suites before AND after, same counts.

---
_coordinator lane, 2026-11-07_
