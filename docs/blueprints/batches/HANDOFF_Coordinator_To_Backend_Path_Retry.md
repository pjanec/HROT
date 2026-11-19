<!--STATUS
state: LIVE — dispatched — ⭐ UNATTENDED, under PROTOCOL_Autonomous_Lanes.md · updated: 2026-11-19 · current-answer: the whole file
related-designs:
  - DECISIONS_Terrain.md — CE-5039, DECIDED 2026-11-19 (the gate is ui's; this is the independent hardening)
-->
# HANDOFF — coordinator → `backend`: A PATH THAT FAILED RETRIES WHEN THE NAVMESH CHANGES (CE-4674)

🔒 User, `2026-11-19`: *"approved"* (terrain's CE-5039 lean, its second half). A `PathToPoint` that failed because its target was off the mesh never
retries (`MaxReplans 0`) — after a live terrain edit (a breach, a building made enterable, an overlay bake) the unit stays stuck though its target is now
reachable.

| row | builds | done when |
|---|---|---|
| **CE-4674** | a move that FAILED for an off-mesh / unreachable target re-plans ONCE when the navmesh version changes (the tile / overlay version it planned on moved), not on a timer; a still-unreachable target fails again and says so (the P1 rule: a move that cannot finish says so) | a rail: target inside a closed building → fails; the building made enterable (a new mesh version) → the unit walks in; no re-plan storm (one re-plan per version) |

Priority: after your determinism rows, with VA-S4…S6. ⚠ **CE-4665** (infantry stop after a reload): behaviors REFUTED the crowd cause (production
runs the no-op `EngineBackedDtCrowdProvider`; the coordinator's "verification" checked the class, not the wiring — wrong) and could not reproduce it
in-repo (16 · 15 · 13 · 14 movers over four loads). It is parked until the reporter gives real-1 / exact steps; NOT yours yet.
