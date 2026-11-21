<!--STATUS
state: LIVE — dispatched — ⭐ UNATTENDED, under PROTOCOL_Autonomous_Lanes.md · updated: 2026-11-18 · current-answer: the whole file
related-designs:
  - ../../DESIGN_Glass_Windows.md — ⭐ approved (R-5068, D-1…D-10): GW-1…GW-3
  - ../../DESIGN_Terrain_Base_Edits.md — ⭐ approved: BE-1…BE-5 (convert to enterable, add a door, remove a baked-in building)
  - PROTOCOL_Autonomous_Lanes.md — HOW (incl. §6a: scenarios per row are proportionate)
-->
# HANDOFF — coordinator → `terrain`: GLASS WINDOWS, then BUILDING CONVERSION AND REMOVAL (CE-5026, CE-5024, CE-5025)

🔒 User, `2026-11-18`: *"i discussed window breaking and building conversion and removal with terrain branch (tasks and design should exist), pls write
them a handoff (they already know just for the trigger in case they forget)"*. ⭐ This is a REMINDER of work you already designed and the user already
approved with you — your own designs and tracker rows are the spec; nothing here changes them.

## Priority — as the user told you
Glass windows FIRST (user to you: *"take this window breaking first, then continue with the terrain editor"*), then the building edits. These come
BEFORE the rows moved to you from backend (CE-4664 ground heat marks, CE-3471 the pig through the wall, CE-3424 scale-1 movers) — unless one of those is
already in progress: finish it first.

| order | row | slice | design |
|---|---|---|---|
| — | CE-5032 | GW-1 the glass pane | ✅ done |
| 1 | **CE-5033** | GW-2 window damage: hits, cracks, broken (`WindowDamage` + `dtWindowDamage` — ids from the census method in the vehicle-appearance design §3: ≈ 112 component ids are free; check the ordinal against 240–244, taken today) | [`DESIGN_Glass_Windows.md`](../../DESIGN_Glass_Windows.md) |
| 2 | **CE-5034** | GW-3 how glass looks (the map's drawing — ui's files: cross-lane, say it, NOTE ui) | same |
| 3 | **CE-5027** | BE-1 the merge removes a baked-in building | [`DESIGN_Terrain_Base_Edits.md`](../../DESIGN_Terrain_Base_Edits.md) |
| 4 | **CE-5028** | BE-2 a removed building's doors and damage go; added houses get doors | same |
| 5 | **CE-5029** | BE-3 make enterable / add a door | same |
| 6 | **CE-5030** | BE-4 the buildings tools on the terrain toolbar | same |
| 7 | **CE-5031** | BE-5 "Save terrain as" bakes the edits | same |

Done when: each design's own acceptance and scenarios (R-4277), §6a per row. Fold any deviation back into the design (obligation ⑤).
