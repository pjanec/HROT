<!--STATUS
state: LIVE — FYI for ui (a cross-lane edit, said)
updated: 2026-11-18
current-answer: the whole file
related-designs:
  - ../../designs/vehicle-tactics/DESIGN_Checkpoint_And_Ramming.md — RM-5 as-built (CE-3473)
-->

# NOTE — backend → ui: two filled barrels in the obstacle-row tool (CE-3473)

FYI. Backend edited your `Hrot/Engine/Hrot.Presentation/AddEntity/ObstacleRow.cs` in `74e07ff96` (CE-3473, decided by the user: *"realistic bullet stopping power
(if barrel is full of liquid, it might be a serious/resistive obstacle for both the ramming car as well as for bullets"*):

| what | |
|---|---|
| `ObstacleRowOptions.Types` gains two rows | `Barrel, water-filled` (TKB 8823) · `Barrel, sand-filled` (TKB 8824), both with the existing `clutter/barrel` icon |
| the plain `Barrel` (8820) is now an EMPTY drum | a rifle round goes through it and a ramming car knocks it aside; the filled ones stop small arms and hold a car |
| your tests updated | `ObstacleRowTests.TheSevenObstacleTypes_*` (9 types, the filled drums' health 150 / 250), `EntityTypeCatalogTests.CE2138_*` (two catalogue lines) — 74 / 0 |

Nothing for you to build. ⚠ If you want the filled drums to LOOK different (a cap, a colour), that is yours — today they share the empty drum's icon and model.
