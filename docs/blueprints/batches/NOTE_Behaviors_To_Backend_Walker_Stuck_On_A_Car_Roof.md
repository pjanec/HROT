<!--STATUS
state: OPEN — for backend (the person mover)
updated: 2026-11-19
current-answer: the whole note
related-designs:
  - NOTE_Behaviors_To_Backend_Person_Held_On_A_Short_Path.md — CE-2450, the same family: a walker the mover holds while his path is planned
  - NOTE_Behaviors_To_Backend_Held_Man_Drifts_Into_Doorway.md — the same family: the mover puts a man somewhere the plan never sent him
-->

# NOTE — behaviors → backend: a walker whose straight path crosses a parked car is lifted onto its ROOF and stays there (`ts-aggression`)

📐 **Measured `2026-11-19`, behaviors, `ts-aggression` in the editor, stepped (`/sim/run-to` 1/60 s).** 'Bold' (rifleman, `CombatPosture` advancing to
(200, 395)) starts at (200, 300); 'Bold Cover' (TKB 8806, a car) stands at (200, 308).

| sim time | Bold position | |
|---|---|---|
| 3.8 s | (198.87, 306.41, 0) | his FightFromCover ends; `AdvanceTo` (200, 395) re-planned: `PathTrace` = 2 points, (198.9, 306.3) → (200, 395) — straight THROUGH the car |
| 5 s | (198.91, 308.23, **1.5**) | on the car's roof |
| 6 … 90 s | (198.92, 308.9, 1.5) | never moves again; the move is re-sent every ~2 s (`MoveStarted … (Order)`), `NavigationStatus` Following / InProgress |

The mirrored 'Cautious' (same setup at x 500) walks the same kind of line past his car and keeps going.

⭐ **A second instance, `bt-window-duel` (same day, stepped):** the 'Street Rifleman' (behind 'Van 1' at (104, 82), TKB 8806) starts a
`FightFromCover` cover move at 12.75 s to (105, 80); at 18 s he stands at (105.2, 81.1, **1.8**) — on Van 1's roof — and stays there to the end (60 s),
his cover moves re-sent ((103, 80), (105, 80)). The scenario's row *"the Street Rifleman bounds to Van 2's cover"* can never pass.

⚠ **Suspect, not measured:** the walker's ground snap takes the car's top as his surface (a step-up of 1.5 m), and from the roof every step is
refused (the edge is a drop). ⛔ No line instrumented.

| | |
|---|---|
| ⭐ lean | a walker never steps UP more than a body's step height (a car roof is no stair); a planned path through a parked vehicle goes round it (or the step is refused at the side, not taken onto the top) |
| why behaviors does not build it | the mover and its ground snap are backend's; the brain's re-send is its correct answer to no progress |
| how to verify | `ts-aggression`, editor: Bold's z stays 0 and he ends north of y 340 (the scenario's rows (3) pass) |
