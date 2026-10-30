<!--STATUS
state: LIVE
updated: 2026-10-30
current-answer: whole file
related-designs:
  - ../../DESIGN_System_State_Inventory.md — the K/A/B/C classes (R-3039)
-->
# NOTE backend → behaviors — two `GrenadeDangerSystem` fields need a class (CE-2360)

`EverySystemStateFieldIsClassifiedTests` (`Hrot.ClusterRunner.Tests`) is red on two fields that CE-2360 (`4cfc08e49`) added. Backend
classified the other 22 unclassified fields (`222cf0ccb`). These two look like class C, and the fix is a design call for your lane, so
backend did not guess:

| field | what it holds | why it looks like C |
|---|---|---|
| `GrenadeDangerSystem._reacted` | once-per-man-per-grenade reaction memory | empty after a restore ⇒ a man re-reacts to the same live grenade |
| `GrenadeDangerSystem._seen` | each replica round's first-sighting time; the remaining fuze is computed from it | empty after a restore ⇒ the fuze restarts |

R-3039 says class-C state lives in a component, so it survives a checkpoint restore and a review. Please move both into components, or
classify them in `docs/DESIGN_System_State_Inventory.md` with the reason if you disagree. The rail turns green either way.
FYI, same commit: `GrenadeDangerSystem._near` and `OneManCommandSystem._done` (CE-2375) were exempted as per-pass scratch.

## Relayed from ui (`2026-10-30`, `NOTE_Ui_To_Backend_Reds_After_ExCon_Removal.md`)
`CE2083_SopOrderUrgency_MirrorsReactionUrgency` is red: `SopOrderUrgency` lacks **Grenade** and **Break**. ui saw it after merging backend, but
backend's commits do not touch it. It is yours.
