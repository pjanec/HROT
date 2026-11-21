<!--STATUS
state: LIVE — for terrain (pc-corridor-fight's checks are yours) · updated: 2026-11-19 (Blue S1 added) · current-answer: the whole file
related-designs:
  - NOTE_Behaviors_To_Terrain_Corridor_Fight_D11.md — the earlier re-measure (D-11, the radius)
-->
# NOTE behaviors → terrain: `pc-corridor-fight` — the "wholly in the open" row reds a squad that is BOUNDING across the open (`2026-11-19`)

🔒 User, `2026-11-19`: *"we should be chasing realism deviations, not baseline conformance in a chaotic fight."*

| row | what the squad does when it reds | a realism deviation? |
|---|---|---|
| *REALISM Blue Squad 3 never stands wholly in the open after 90 s* (reds at 91–146 s, depending on the run) | `BoundingOverwatch` across the open corridor toward the objective — there is no cover between its start and the objective | ⛔ no: crossing open ground by bounds is what a squad does there; the row reads a MOVING squad as "standing" |
| *REALISM Blue Squad 1 never stands wholly in the open after 90 s* (reds at 162.25 s, behaviors `3f78bf713`) | `BoundingOverwatch`: the bounding pair moving, the overwatch pair suppressing where its bound ended — the bound looks for cover (CE-2158 F-c) and the corridor offers none there | ⛔ no behaviour defect: no cover to take. ⏳ the one gap left is going PRONE in the open (D-13's not-built half) |

⭐ **Asked (your file):** count only a squad that is STATIONARY in the open — e.g. a `where` on its task (not `BoundingOverwatch` / `FireAndMovement` / `AdvanceTo`), or a dwell (open for ≥ N s at the same place). The other REALISM rows behaviors keeps chasing as real deviations (the Red Reserve walking shoulder to shoulder is being fixed now).
