<!--STATUS
state: LIVE — answers NOTE_Behaviors_To_Terrain_NoWayRound_EdgeClearance.md
updated: 2026-11-18
current-answer: the whole note
related-designs:
  - ../../designs/minefields/DESIGN_Minefields.md — §3e the clearance ring row (CE-4655), MF-2 the edge inset
-->

# NOTE — terrain → behaviors: a route round a known field keeps its clearance now (CE-4655) — your CE-2447 is unblocked

| what | evidence |
|---|---|
| ⭐ a field's navmesh area reaches **max(spacing, 5 m)** OUTSIDE its outline (`MinePattern.RouteClearance`; `NavDoorways.AddClearanceRing`) — priced as the field (×20 suspected / ×200 known, never closed); a lane runs on through it | `MinefieldNavTests.CE4655_ARouteRoundAKnownField_KeepsItsClearance` — the planned path ≥ 4 m off the outline; with the ring off it comes 0.10 m off (red) |
| ⭐ and the field's MINES keep `max(spacing/2, 1 m)` inside the outline (MF-2), so a man ON the edge is safe too | `MineTriggerTests.CE4655_*`; scenario `mf-way-round` 4 / 4 |
| ⚠ **`mf-no-way-round` now goes ROUND, as your note predicted**: no `MinefieldAhead` (the route never crosses the field), no blast, nobody hurt, leader at the end x ≥ 90; red: *"nobody enters the field"* (x > 130 at 51.5 s — the check is x-only, a man going round at y > 115 also passes x 130) and *"still halted at the end"* | `scripts/scenario-regression.sh mf-no-way-round` on `terrain` — ⇒ yours to re-author with a real barrier (your note's plan) |
| the dirty box of a changed field grows by the ring (± clearance + 3 m) | `MinefieldBakeTests.AField_IsBakedIntoTheMusclesNavmesh_*` (13 / 13) |
