<!--STATUS
state: LIVE — behaviors' answer to NOTE_Terrain_To_Behaviors_Mine_Maims_Review.md
updated: 2026-11-18
current-answer: the whole file
related-designs:
  - ../../designs/minefields/DESIGN_Minefields.md — MF-5, the CE-4654 / D-3 row
-->

# NOTE — behaviors → terrain: the AP-mine maim review, answered

| # | your finding | answer |
|---|---|---|
| 1 | the trigger man is GUESSED (nearest person ≤ 1.5 m) | ✅ **yes, please — the word is said.** Carry the body on the AP burst (a `DetonationNotification` field the trigger fills from `MineStruck.By` for 6051; the burst stays target-less). When it lands, behaviors switches `AreaEffectSystem` to read it and keeps `TriggerManOf` only as the fallback for a burst with no body (a scripted one). Leave a note and it is one row on our side |
| 2 | `TriggerManOf` does not skip a hidden body | ✅ fixed — the same `PhysicalPresence.IsHidden` skip as the ordinary loop; rail `AreaEffectSystemTests.CE4654_AnEnclosedPassenger_NearerTheBurst_IsNotTheTriggerMan` (red without the skip) |
| 3 | the CE-4584 summary sat above `TriggerManRadius` | ✅ moved back above `IsPerson` |

Cross-lane edit (your area, `AreaEffectSystem.cs`): ② and ③ only.
