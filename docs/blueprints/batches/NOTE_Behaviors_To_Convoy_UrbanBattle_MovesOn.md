<!--STATUS
state: LIVE — answers NOTE_Convoy_To_Behaviors_UrbanBattle_MovesOn.md (CE-9023)
updated: 2026-11-18
current-answer: the whole note
related-designs:
  - ../../designs/group-maneuvers/DESIGN_Defend_And_Attack_Position.md — §11a DA-100 (the platoon hold pauses the convoy), DA-102 (remount; the CE-9023 row)
-->

# NOTE — behaviors → convoy: `uc-urban-battle` "the convoy moves on" — one defect fixed, the rest is the approved design (D-5)

| your question | answer |
|---|---|
| a defect? | ✅ **yes, one, fixed** (`b7a088964`): a KILLED man (body, Health 0) held a squad's boarding back — `SquadEmbarkBehavior.AboardIt` only skipped a destroyed entity. Squad A's four riflemen died at the IED; its leader boarded the IFV at 79 s, yet `SquadEmbark` failed on its 60 s timeout and CRemount waited its whole 120 s. Rail `SquadManeuverTests.CE9023_*` (red-proved) |
| the rest? | ⚠ **by design**: the platoon SOP's `PlatoonHold` pauses `PlatoonConvoy` while an enemy team is reported within 400 m in the last 30 s — DA-100 approved exactly that and rejected a "not convoying" exemption. A live run held from 14.9 s and was still holding at 250 s with the ambush still reported; the regression run remounted at 147 s (the editor run does not repeat). So the move-on waits for the ambush to end |
| the row | still red; written as **D-5** in `DECISIONS_Behaviors.md` — lean: author the fire team's end (withdraws / is killed by a fixed time) and keep runFor 200, so the row tests the move-on, not how long the ambush lives. ⛔ The row was not loosened |
