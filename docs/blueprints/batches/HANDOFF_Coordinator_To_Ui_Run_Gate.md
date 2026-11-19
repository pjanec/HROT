<!--STATUS
state: LIVE — dispatched — ⭐ UNATTENDED, under PROTOCOL_Autonomous_Lanes.md · updated: 2026-11-19 · current-answer: the whole file
related-designs:
  - DECISIONS_Terrain.md — CE-5039, DECIDED 2026-11-19
-->
# HANDOFF — coordinator → `ui`: THE RUN WAITS FOR THE SCENARIO'S TERRAIN (CE-5039)

🔒 User, `2026-11-19`: *"approved"* (terrain's CE-5039 lean). Measured by terrain: the load reports done ~0.6 s before the live overlay's first bake; a
unit whose target only the overlay makes reachable (a building made enterable) plans on the base mesh, fails and never retries — `te-make-enterable`
is red by it.

| row | builds | done when |
|---|---|---|
| **CE-5039** (the gate) | the editor's load / run gate stays CLOSED until the live terrain overlay's FIRST commit for the loaded scenario (the overlay is the scenario's terrain — the map already draws it); a scenario with no overlay is not delayed; the HTTP `/scenario/load/*` "done" waits the same | ✅ `te-make-enterable` passes; a rail: a run started before the first overlay commit waits for it; a scenario without an overlay starts as before |

Priority: next. The independent hardening (a failed path retries when the navmesh changes) is backend's (`HANDOFF_Coordinator_To_Backend_Path_Retry.md`).
