<!--STATUS
state: LIVE — ui → behaviors: two asks for the rule editor (step progress; a live edit that keeps the rule's state)
updated: 2026-11-03
current-answer: the whole note
stale-below: nothing
related-designs:
  - ../../DESIGN_Rules_View_UI.md — §7 the rule editor (ui; RE-4 needs ①, RE-7 needs ②)
  - ../../DESIGN_Scenario_Rules.md — SR-109 (a live edit lasts this run only, journaled), §00.10 V-3 / V-4 as built
  - ../designs/rules-view/mockup/TriggerDetails.html · EditAtRuntime.html — "do 2/3", live step progress, the live-edit dialog
-->
# NOTE — ui → behaviors: **the rule editor needs two things from you**

🔒 **User, `2026-11-03`:** *"yes, send the asks to behaviors and design the editor"*. ui designed the editor in `DESIGN_Rules_View_UI.md` §7. Everything it
draws already exists (the params form, `RuleLeaves` 431, `RuleControl`, the action catalogue, the journal) except these two.

| # | ask | measured | ui's lean |
|---|---|---|---|
| ① | **the Do list's progress on the status**: the current step index and the step count (+ the failed step) while a trigger is `Acting` | `RunActionsState { Index, Failed, FailedStep }` lives in the runner's state (`ActionList.cs:132`); `ObjectiveStatus` has only `TriggerState.Acting`: no step field | ⭐ `StepCur` / `StepCount` (bytes) on `ObjectiveStatus`, written where `Index` moves. The rules view shows *do 2/3* and the Do tab marks the running step. A failed step's reason is already in `Reason` |
| ② | **a live edit that KEEPS the rule's state** (SR-109: this run only, journaled with its origin) | the editor's running path is `AssignBehaviorEvent` (`EntityAiDetailsView.cs:171-173`), which RE-ASSIGNS, so a trigger loses `FireCount`, its cooldown and its arm state, and an objective loses its progress. ⭐ Precedent: `BlueprintInstanceService.ApplyParams` keeps the instance's state | ⭐ a re-parameterise for rule behaviours: the new params replace the old ones at the next tick, and the runtime state stays (fire count, cooldown, hold timer, progress). A step list that is `Acting` finishes on its old list. Journaled as an instructor edit. Written through the existing path (`EntityWriteRouter`, owner applies), the way `RuleControl` is. ⛔ No new transport. If a field cannot change live (e.g. the objective kind), refuse it with a reason the editor can show |

Until ② lands, the editor's live mode offers the `RuleControl` buttons only, and every field shows *"needs the live edit (behaviors)"*. Edit mode needs neither ask.

_ui lane, 2026-11-03_
