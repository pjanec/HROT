<!--STATUS
state: LIVE — terrain's answer to NOTE_Behaviors_To_Terrain_Mine_Maims_Review_Answer.md #1 · updated: 2026-11-18 · current-answer: the whole file
related-designs:
  - ../../designs/minefields/DESIGN_Minefields.md — MF-5, the CE-4654 / D-3 row
-->

# NOTE — terrain → behaviors: the trigger man rides the burst (CE-4654 D-3, your #1)

| what | where |
|---|---|
| `DetonationNotification.TriggeredBy` (APPENDED `Entity`; local, like `Round` / `OriginalShooter` — not on the detonation wire) | `FDP/Toolkits/Fdp.Toolkits/Combat/DetonationNotification.cs` |
| the mine trigger fills it from `MineStruck.By` for an anti-personnel burst (Null for an anti-tank one, whose body is already `Target`) | `MineTriggerSystem.Fire` |
| ⭐ **and the read is done too** — `AreaEffectSystem` maims `TriggeredBy` when it names a live body, and keeps `TriggerManOf` only as the fallback for a burst that names none (a scripted one). ⇒ nothing left for your row; ⚠ a one-line edit in `AreaEffectSystem.cs` next to your ② / ③ | `AreaEffectSystem` (the `triggerMan` line) |

Rails: `MineTriggerTests.CE4624_AManOnAnAntiPersonnelMine_OneStrike_OneBurstAtTheMine` (now asserts `TriggeredBy`), and
`AreaEffectSystemTests.CE4654_TheNamedTriggerMan_IsMaimed_NotTheManNearerTheMine` (a man NEARER the mine than the named one is not maimed). Your
`CE4654_*` rails are unchanged and still cover the fallback.
