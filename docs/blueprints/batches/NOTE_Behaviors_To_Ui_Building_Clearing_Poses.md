<!--STATUS
state: LIVE — behaviors → ui: four new gesture values and a lean to draw (you asked for a note when a replicated enum you draw grows).
updated: 2026-10-18
current-answer: the whole file.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/designs/group-maneuvers/DESIGN_Building_Clearing.md — OWNS the values: CB-20, CB-21, CB-24 (R-2032)
  - docs/DESIGN_People_Display.md — (ui) the pose pipeline (Gestured stage) that draws them
-->

# NOTE — behaviors → ui: gestures and a lean for building clearing

**Re-stamped `2026-10-18`:** one more row — the head turn from look direction (`LookState.HeadYaw`, last row).

**Design:** [DESIGN_Building_Clearing CB-20, CB-21, CB-24](../../designs/group-maneuvers/DESIGN_Building_Clearing.md) · **Tracker:**
`CE-2265` (behaviors, the gestures), backend BR-4 / BR-5 ([handoff](HANDOFF_Behaviors_To_Backend_Building_Clearing_Interactions.md))

| value | on the replicated … | who | draw it as | with stance |
|---|---|---|---|---|
| `Searching` | gesture byte (appended) | the soldier searching a man | both hands forward and low, on the man | Crouched |
| `HandsOnHead` | gesture byte (appended) | a complying civilian; a man being searched | fingers laced on the back of the head | Prone (or kneeling) |
| `Restrained` | gesture byte (appended) | a detained man | hands tied behind the back | Prone, kneeling or walking |
| `CoverHead` | gesture byte (appended) | a frightened civilian cowering | both arms wrapped over the head | Prone |
| `Lean` None · Left · Right | a new body state (backend BR-5) | a soldier at a corner or a doorway | the upper body tilted to that side, the weapon leading | any |
| 🆕 `LookState.HeadYaw` *(added 2026-10-18)* | a new replicated component (backend LD-1, [Look Direction §3](../../designs/look-direction/DESIGN_Look_Direction_And_FOV.md)) | anyone glancing off his body's facing (≤ ±70°, ≤ 2 s) | the head turned by that yaw; on the map a short tick for the look beside the facing | any |

⭐ `Cower` changes from Crouched to Prone (`CE-2265`). Nothing here needs new data from you; until a value is drawn, the figure shows
its stance as today.
