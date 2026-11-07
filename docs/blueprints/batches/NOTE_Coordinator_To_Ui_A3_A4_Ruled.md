<!--STATUS
state: LIVE
updated: 2026-11-07
current-answer: the whole file
stale-below: nothing
known-rot: none
known-conflict: none
related-designs:
  - ../../UX/DESIGN_Action_Catalogue.md — §8 carries the ruling (R-4162) under the A3 table
-->

# NOTE — coordinator → ui: your two waiting questions are ruled (R-4162)

Answers the ⏸ WAITING FOR THE USER table in `RESUME_UI_Lane.md` (session 2026-11-07 (b)). Both are now in
[Action Catalogue §8](../../UX/DESIGN_Action_Catalogue.md) and [RULINGS R-4162](../RULINGS.md).

## A3 review — the menus per mode

| your lean | ruling |
|---|---|
| ① Follow + Select in every mode | ✅ as leaned |
| ② Rotate Edit-only | ✏ **Edit AND Live**: 🔒 *"Rotate should be possible in life as well, the same way as drag and drop is."* — same modes as the move-by-drag gesture |
| ③ *Save as macro…* in Review | ✏ **Edit only**: 🔒 *"Save as macro makes sense only in edit mode."* |
| *(new)* Save | ⭐ the Save icon / Ctrl+S / File ▸ Save **writes the scenario only in Edit**; in **Preview and Live it takes a checkpoint**; in Review it is not offered — 🔒 *"This was already planned like that."* (Editing UX §3.5, row *saved*, corrected: *save as* in Live is gone). ⚠ Measure what is built (CAPABILITIES says "in Live, Save takes a checkpoint") and gate only what is missing |

⇒ update `menus.golden.txt` / `menus.station.golden.txt` to the corrected table.

## O6 vs A4 — whom a menu command acts on

| you right-click | the selection | the command goes to |
|---|---|---|
| an entity **in** the selection | unchanged | **every selected entity**, whichever was clicked |
| an entity **not** in the selection | becomes **that entity alone** | that entity |
| empty ground | unchanged | the selection |

🔒 *"Right-clicking an unselected entity should indeed select just that entity"* · *"if we are right clicking an entity which is already part of
the selection, then the menu command should go to all selected ones. Doesn't matter which one was right clicked."*
The click AFTER a command (target point / entity) is its parameter. ONE rule for every menu command, orders included. ⇒ **A4 is unblocked**;
O6 is marked SUPERSEDED in the orders design.

## Also

🔴 `ABehaviourAdvertisesItsRealParametersTests.TriggerAdvertisesItsAuthoredKeys` (your "not ours") — the Trigger behaviour is the behaviors
lane's (rules S2, `4302e2d9`); leave it to them.

---
_coordinator lane, 2026-11-07_
