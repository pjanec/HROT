<!--STATUS
state: OPEN — for backend (the person mover)
updated: 2026-11-19
current-answer: the whole note
related-designs:
  - NOTE_Behaviors_To_Backend_Person_Held_On_A_Short_Path.md — CE-2450, the same family: a man by a wall the mover handles differently from the plan
  - ../Blueprint_Issues_Tracker.md — CE-2452 (behaviors), where this was found
-->

# NOTE — behaviors → backend: a man held still against a wall drifts 1.8 m into the doorway beside him (`rf-wall-hugger`)

📐 **Measured `2026-11-19`, behaviors, `rf-wall-hugger` in the editor, stepped (`/sim/run-to` 1/60 s).** The 'Hugger' (Friend grenadier, `InertSop`,
`HoldStance` Crouched — no move of his own) is authored at (119, 152.3), against the OBJ house's south wall, between the back door and the window.

| sim time | Hugger position |
|---|---|
| 1 s | (119.00, 152.30) |
| 5 s | (117.75, 153.52) |
| 7 s | (117.94, 153.96) |
| 8 s | (117.94, 153.97) — then stays |

He ends 1.8 m north-west, in the back doorway, before anybody shoots at him or throws (the first grenade bursts ~7.1 s). Nobody else is near him.

⚠ **Suspect, not measured:** his authored spot is inside the navmesh's wall margin (the mesh stops a body radius short of the wall), and the mover
puts him back onto the nearest mesh — which here is the doorway's corridor. ⛔ No line was instrumented.

| | |
|---|---|
| ⭐ lean | a man with no move stays where he is put; or, if he must be brought onto the mesh, the nearest point on the SAME side of the wall (not through a doorway) |
| why it matters | in the doorway the defenders' grenades burst on the inside of the wall and hurt them (CE-2452) — the scenario's geometry changes under the behaviour |
| how to verify | `rf-wall-hugger`, editor: the Hugger's position at 10 s is within 0.5 m of (119, 152.3) |
